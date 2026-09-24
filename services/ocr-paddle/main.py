import io
import logging
import multiprocessing
import os
import re
import tempfile
import threading
import time
from concurrent.futures import ProcessPoolExecutor, TimeoutError as FuturoTimeoutError, wait as futures_wait
from concurrent.futures.process import BrokenProcessPool
from contextlib import asynccontextmanager
from typing import Dict, List, Optional, Tuple

import cv2
import numpy as np
from fastapi import FastAPI, File, HTTPException, UploadFile
from PIL import Image
import pypdfium2 as pdfium
from paddleocr import PaddleOCR
from pydantic import BaseModel
from starlette.concurrency import run_in_threadpool

# Logger propio con su propio handler, en vez de logging.basicConfig()
# sobre el logger raíz. Comprobado en pruebas (2026-08-24 y 2026-08-25) que
# ni siquiera basicConfig(force=True) bastaba: los logs INFO de los procesos
# worker (p.ej. "Página N procesada...") seguían sin llegar a `docker logs`
# — solo se veían los print()/warnings.warn() de paddlex, que no pasan por
# el módulo logging. Causa más probable: PaddleOCR(...) reconfigura el
# logger raíz internamente al INSTANCIARSE (dentro de _inicializar_worker,
# que se ejecuta en cada proceso worker DESPUÉS de este bloque), deshaciendo
# cualquier configuración previa del raíz, incluida la de force=True. Dar a
# "ocr-paddle" su propio handler y desactivar la propagación al raíz
# (propagate=False) lo hace inmune a lo que paddlex haga con el raíz
# después, porque ya no depende de él para nada. Sin visibilidad real de
# progreso por página, diagnosticar los problemas de memoria de estos días
# ha dependido de vías indirectas (dmesg, docker stats, temporizadores).
logger = logging.getLogger("ocr-paddle")
logger.setLevel(logging.INFO)
if not logger.handlers:
    _handler = logging.StreamHandler()
    _handler.setFormatter(logging.Formatter("%(levelname)s:%(name)s:%(message)s"))
    logger.addHandler(_handler)
logger.propagate = False

def _cpus_disponibles() -> int:
    """CPUs realmente asignadas a ESTE contenedor (no del host), leídas del
    propio límite de cgroup — el mismo que fija `cpus:` en
    deploy.resources.limits de docker-compose.yml.

    Por qué esto y no os.cpu_count(): os.cpu_count() (y `nproc`) devuelven
    las CPUs del HOST completo, ignorando el límite del contenedor —
    llevaría a sobresuscripción de CPU (el mismo tipo de bug que costó días
    diagnosticar con cpu_threads, ver OCR_HILOS_POR_PROCESO) en cualquier
    host donde `cpus:` sea menor que el total de núcleos disponibles, que es
    el caso normal en producción (reservando núcleos para el resto del
    stack). Comprobado el 2026-08-25: en la máquina de desarrollo (16 CPUs
    en total) el contenedor solo tenía 2 asignadas — con os.cpu_count() el
    valor por defecto de OCR_PROCESOS habría sido drásticamente incorrecto.

    Con esto, la ÚNICA decisión que hay que tomar a mano al desplegar en un
    host nuevo es cuántas CPUs totales dar a `cpus:` (ver ese mismo
    comentario en docker-compose.yml) — OCR_PROCESOS se ajusta solo a partir
    de ahí, sin recalcular nada a mano.
    """
    try:
        with open("/sys/fs/cgroup/cpu.max") as f:
            cuota, periodo = f.read().split()
        if cuota == "max":  # sin límite de cgroup fijado
            return os.cpu_count() or 1
        return max(1, int(cuota) // int(periodo))
    except (FileNotFoundError, ValueError, ZeroDivisionError):
        # cgroup v1, entorno sin contenedor, etc. — aproximación menos
        # precisa (puede sobreestimar si el host no está dedicado por
        # completo a este servicio), pero mejor que un valor fijo a ciegas.
        return os.cpu_count() or 1


# Ajustables sin recompilar (solo reiniciar el contenedor): ver comentario
# sobre reserva de núcleos en docker-compose.yml. Por defecto, todas las
# CPUs que el propio contenedor tenga asignadas menos las reservadas para
# OCR_PROCESOS_SERVER (ver más abajo) — ver _cpus_disponibles() para el
# porqué de no usar simplemente os.cpu_count().
OCR_PROCESOS = int(os.environ.get("OCR_PROCESOS") or max(1, _cpus_disponibles() - int(os.environ.get("OCR_PROCESOS_SERVER", "1"))))

# Pool dedicado y separado del de arriba, solo para el reprocesado con la
# variante server de páginas que quedaron por debajo del umbral de
# confianza. Antes (hasta 2026-08-23) cada worker del pool único cargaba
# AMBAS variantes (mobile y server) a la vez y podía ejecutar las dos
# seguidas sobre la misma página degradada — comprobado con `dmesg` que eso
# hacía que un solo proceso llegara a 2-3.6 GB de RSS y disparara el
# OOM-killer del kernel bajo carga real (páginas degradadas reales, no el
# caso límite sintético). Con pools separados, un worker mobile nunca carga
# el motor server (ni al revés), así que ningún proceso individual necesita
# nunca memoria para las dos tuberías de inferencia a la vez — el pico de
# memoria por proceso baja aproximadamente a la mitad. El coste es que este
# proceso pasa la mayor parte del tiempo inactivo (las páginas degradadas
# son la minoría por diseño), pero es memoria ya cargada una vez, no CPU
# desperdiciada. Por defecto 1: las páginas degradadas se reprocesan en
# serie, aceptable porque se espera que sean pocas.
OCR_PROCESOS_SERVER = int(os.environ.get("OCR_PROCESOS_SERVER", "1"))

OCR_CONFIANZA_MINIMA = float(os.environ.get("OCR_CONFIANZA_MINIMA", "0.80"))

# Interruptor para el reprocesado con la variante "server" (el pool
# dedicado, ver OCR_PROCESOS_SERVER). Estuvo desactivado por defecto desde
# el 2026-08-26 al 2026-08-28: con PP-OCRv5, tanto en la Prueba 1 (500
# páginas sintéticas, 32 degradadas) como con páginas reales de la Gaceta
# de Madrid, el reprocesado con la variante server de PP-OCRv5 no mejoraba
# nada (mismo vocabulario multilingüe que mobile, mismas alucinaciones
# CJK) y costaba 2.3-2.8x más tiempo — ver memoria de proyecto
# "ocr_calidad_ppocrv6_gaceta". Reactivado el 2026-08-30 (IA.2c) porque el
# pool "server" ahora carga PP-OCRv6 medium en vez de PP-OCRv5 server (ver
# _VARIANTES abajo): probado sobre las mismas páginas reales, PP-OCRv6
# medium sí sube la confianza (0.68-0.94 → 0.90-0.997) y elimina la
# alucinación CJK — el reprocesado dirigido (solo páginas por debajo del
# umbral o con CJK detectado, ver _contiene_cjk) vuelve a tener sentido.
# Se deja como variable de entorno (no hardcoded) por si hiciera falta
# desactivarlo de nuevo sin reconstruir la imagen.
OCR_HABILITAR_SERVER = os.environ.get("OCR_HABILITAR_SERVER", "true").strip().lower() in ("1", "true", "yes")

# Alucinación CJK (ver memoria "ocr_calidad_ppocrv6_gaceta"): el vocabulario
# multilingüe de PP-OCR incluye chino/japonés/coreano, y cuando el modelo no
# reconoce algo con confianza (orlas tipográficas, tablas, ruido) puede
# "alucinar" caracteres de esos scripts en vez de fallar limpiamente — visto
# en páginas reales con confianza POR ENCIMA del umbral (p. ej. 0.865),
# así que el umbral de confianza solo no basta como disparador de
# reprocesado. Rango de codepoints: ideogramas CJK unificados y extensión A,
# hiragana, katakana, símbolos/puntuación CJK (、。「」【】 etc., vistos en la
# práctica) y hangul coreano — nunca deberían aparecer en un documento en
# español, así que su sola presencia es una señal de alta precisión.
_PATRON_CJK = re.compile(
    "["
    "　-〿"  # símbolos y puntuación CJK (、。「」【】 ...)
    "぀-ヿ"  # hiragana + katakana
    "㐀-䶿"  # ideogramas CJK, extensión A
    "一-鿿"  # ideogramas CJK unificados
    "가-힣"  # hangul (coreano)
    "]"
)


def _contiene_cjk(texto: str) -> bool:
    return bool(_PATRON_CJK.search(texto))

# Hilos internos de cómputo por proceso worker — NO es lo mismo que
# OMP_NUM_THREADS/OPENBLAS_NUM_THREADS/MKL_NUM_THREADS (variables de entorno,
# ver docker-compose.yml): esas gobiernan las librerías BLAS/OpenMP, pero
# PaddleX tiene su propio parámetro "cpu_threads" (paddlex/inference/utils/
# pp_option.py, valor por defecto 8) que las ignora por completo, y OpenCV
# tiene su propio pool de hilos (cv2.setNumThreads) tampoco gobernado por
# esas variables. Comprobado empíricamente: con el valor por defecto de
# PaddleX (8 hilos) y OpenCV sin fijar, dos workers en paralelo (OCR_PROCESOS
# por defecto) tardaban ~83s en una predicción que en aislamiento tarda
# ~25s — sobresuscripción real muy por encima de las 2 CPUs reservadas al
# contenedor. Fijando cpu_threads=1 aquí y cv2.setNumThreads(1) en cada
# worker, la misma predicción en paralelo bajó a ~17.6s. Por defecto "1"
# porque encaja exacto con 2 procesos sobre 2 CPUs (ver el límite de cpus en
# docker-compose.yml) sin sobresuscribir; subir esto solo tiene sentido si
# también se sube el límite de CPUs del contenedor o se baja OCR_PROCESOS.
OCR_HILOS_POR_PROCESO = int(os.environ.get("OCR_HILOS_POR_PROCESO", "1"))

# Tope duro de tiempo para UN documento completo (todas sus páginas), no por
# página: si se supera, se matan a la fuerza los procesos worker que sigan
# vivos y la petición falla explícitamente en vez de quedarse colgada. Es la
# última red de seguridad contra un cuelgue real (no una caída, un
# atasco silencioso) dentro de la inferencia nativa de Paddle, que
# ProcessPoolExecutor por sí solo no detecta si el proceso sigue "vivo".
OCR_TIMEOUT_SEGUNDOS = int(os.environ.get("OCR_TIMEOUT_SEGUNDOS", str(25 * 60)))

# El pool server ya se recicla tras CADA página (ver
# _ejecutar_lote_server_reciclando) porque solo tiene 1 proceso. El pool
# mobile tiene varios workers de larga vida procesando cientos de páginas
# seguidas, y reciclarlo por página sería carísimo (cada recarga de modelo
# cuesta ~9-15s por worker). Pero comprobado en pruebas reales (2026-08-24 y
# 2026-08-25, con `dmesg` confirmando el OOM): un worker mobile TAMBIÉN
# acumula memoria según procesa páginas sucesivas dentro de su propio
# proceso, llegando a 4+ GB de RSS y disparando el OOM-killer tras
# 84-140 páginas según la prueba. Reciclar el pool completo cada N páginas
# acota ese crecimiento sin pagar el coste de recargar en cada página. 40
# páginas da margen de sobra bajo el rango de fallo observado (84-140), con
# un coste de recarga (~10-15s cada 40 páginas) despreciable frente al
# tiempo de OCR de ese mismo tramo.
OCR_RECICLAR_CADA_N_PAGINAS = int(os.environ.get("OCR_RECICLAR_CADA_N_PAGINAS", "20"))

# Nº de intentos por chunk antes de abandonar (ver _ejecutar_chunk). Subido
# de 2 a 3 el 2026-08-26: comprobado en una prueba real de 500 páginas que
# dos caídas SEGUIDAS en el mismo chunk (un worker OOM en el primer intento
# Y en el reintento) agotaban el margen y tiraban todo el documento — no es
# tan raro como para descartarlo, con varios chunks por documento la
# probabilidad de que le pase a AL MENOS uno no es despreciable. Un tercer
# intento reduce bastante esa probabilidad a cambio de, como mucho, una
# recarga de modelo más en el caso ya raro de que haga falta.
OCR_INTENTOS_POR_CHUNK = int(os.environ.get("OCR_INTENTOS_POR_CHUNK", "3"))

_VARIANTES = {
    # PP-OCRv6 (paddleocr 3.7.0, ver requirements.txt) en vez de PP-OCRv5 —
    # soporta 46 idiomas latinos nativamente en un solo modelo, sin el
    # "latin_..." que no reconocía paddlex 3.0.0 (ver memoria de proyecto
    # "ocr_calidad_ppocrv6_gaceta" para el detalle de la investigación).
    #
    # "mobile" = tier "small": pasa por todas las páginas, es el tier
    # rápido/por defecto (equivalente en propósito al "mobile" de PP-OCRv5,
    # aunque PP-OCRv6 ya no usa esa palabra en el nombre del modelo).
    # "server" = tier "medium": reprocesa solo las páginas marcadas por
    # _contiene_cjk/OCR_CONFIANZA_MINIMA (ver _ejecutar_ocr_paginas).
    # Probado sobre 20 páginas reales de la Gaceta de Madrid: confianza
    # 0.68-0.94 → 0.90-0.997 y elimina la alucinación CJK que sí tenía
    # PP-OCRv5 server (mismo vocabulario multilingüe que mobile, no
    # aportaba nada). Pendiente de validar a escala completa (500/928
    # páginas), no solo la muestra.
    "mobile": ("PP-OCRv6_small_det", "PP-OCRv6_small_rec"),
    "server": ("PP-OCRv6_medium_det", "PP-OCRv6_medium_rec"),
}

# Poblado una vez por proceso worker del Pool (ver _inicializar_worker), no
# en el proceso principal de FastAPI: el proceso principal nunca hace OCR
# directamente, solo reparte páginas al pool. Un solo motor, no un dict por
# variante: desde la separación en dos pools (ver OCR_PROCESOS_SERVER), cada
# worker pertenece a un pool de una única variante y nunca carga la otra.
_motor: Optional[PaddleOCR] = None


class PaginaTexto(BaseModel):
    pagina: int
    texto: str
    confianza_ocr: float
    variante_ocr: str


class OcrResponse(BaseModel):
    paginas: List[PaginaTexto]


def _inicializar_worker(variante: str) -> None:
    """Se ejecuta una sola vez por proceso worker al crear el Pool, no en
    cada página: cargar PaddleOCR es lento (carga de pesos del modelo), así
    que hacerlo por tarea en vez de por proceso anularía la ganancia de
    paralelizar. Cada worker carga solo la variante de su propio pool (ver
    OCR_PROCESOS_SERVER) y la mantiene en memoria durante toda su vida.
    """
    global _motor

    # Pool de hilos de OpenCV (usado en _deskew_y_limpiar): es por proceso,
    # así que hay que fijarlo aquí, una vez por worker. Ver OCR_HILOS_POR_PROCESO.
    cv2.setNumThreads(OCR_HILOS_POR_PROCESO)

    modelo_det, modelo_rec = _VARIANTES[variante]
    _motor = PaddleOCR(
        use_textline_orientation=True,
        # SIN lang="es": con paddleocr==3.0.0, fijar `lang` junto con un
        # text_detection_model_name/text_recognition_model_name
        # explícito hace que PaddleX ignore en silencio el modelo pedido
        # y cargue el mobile por defecto — comprobado empíricamente
        # (con lang="es" nunca se llegaba a descargar PP-OCRv5_server_*,
        # solo mobile, pasara lo que pasara). No hace falta de todos
        # modos: el diccionario de reconocimiento de PP-OCRv5 ya es
        # multilingüe e incluye los caracteres propios del español
        # (á é í ó ú ñ), a diferencia de versiones anteriores de
        # PaddleOCR con un modelo/diccionario por idioma.
        text_detection_model_name=modelo_det,
        text_recognition_model_name=modelo_rec,
        # Ver OCR_HILOS_POR_PROCESO arriba: sin esto, PaddleX usa 8 hilos
        # por defecto por motor, muy por encima de las CPUs reservadas.
        cpu_threads=OCR_HILOS_POR_PROCESO,
    )
    logger.info(
        "Worker %d (%s): motor cargado (cpu_threads=%d).",
        os.getpid(), variante, OCR_HILOS_POR_PROCESO,
    )


def _deskew_y_limpiar(imagen_bgr: np.ndarray) -> np.ndarray:
    """Endereza páginas torcidas y reduce ruido antes de pasarlas al modelo.

    Sin este preprocesado, PaddleOCR pierde precisión notablemente en
    escaneos con una inclinación de pocos grados (habitual al escanear o
    fotografiar papel a mano) o con ruido de fondo (fotocopias de
    fotocopias, manchas de escáner).
    """
    gris = cv2.cvtColor(imagen_bgr, cv2.COLOR_BGR2GRAY)

    # Reducción de ruido conservando bordes de texto.
    gris = cv2.fastNlMeansDenoising(gris, h=10)

    # Umbral binario inverso + ángulo del rectángulo mínimo que envuelve los
    # píxeles "tinta", para estimar la inclinación dominante de la página.
    _, binaria = cv2.threshold(gris, 0, 255, cv2.THRESH_BINARY_INV | cv2.THRESH_OTSU)
    coords = cv2.findNonZero(binaria)
    if coords is None:
        return cv2.cvtColor(gris, cv2.COLOR_GRAY2BGR)

    angulo = cv2.minAreaRect(coords)[-1]
    if angulo < -45:
        angulo = -(90 + angulo)
    else:
        angulo = -angulo

    # Evita "corregir" páginas ya rectas por ruido de redondeo del ángulo.
    if abs(angulo) < 0.5:
        return cv2.cvtColor(gris, cv2.COLOR_GRAY2BGR)

    alto, ancho = gris.shape[:2]
    centro = (ancho // 2, alto // 2)
    matriz = cv2.getRotationMatrix2D(centro, angulo, 1.0)
    enderezada = cv2.warpAffine(
        gris,
        matriz,
        (ancho, alto),
        flags=cv2.INTER_CUBIC,
        borderMode=cv2.BORDER_REPLICATE,
    )
    return cv2.cvtColor(enderezada, cv2.COLOR_GRAY2BGR)


def _detectar_columna(centros_x: np.ndarray, ancho_pagina: float) -> List[int]:
    """Asigna cada caja de texto a la columna 0 (izquierda) o 1 (derecha).

    PaddleOCR no reconstruye columnas; solo devuelve cajas de texto. Muchos
    documentos oficiales (BOE, diligencias, ciertos formularios) usan
    maquetación a dos columnas, así que se detecta el mayor "hueco"
    horizontal vacío de texto y, si es suficientemente ancho y cae en la
    franja central de la página, se trata como separador de columnas.
    """
    if ancho_pagina <= 0 or len(centros_x) < 2:
        return [0] * len(centros_x)

    orden = np.argsort(centros_x)
    xs_ordenados = centros_x[orden]
    huecos = np.diff(xs_ordenados)
    if len(huecos) == 0:
        return [0] * len(centros_x)

    idx_mayor_hueco = int(np.argmax(huecos))
    posicion_relativa = (xs_ordenados[idx_mayor_hueco] - xs_ordenados[0]) / ancho_pagina

    # Umbral deliberadamente conservador: un hueco pequeño o fuera del centro
    # de la página es solo el espacio entre palabras de una línea corta, no
    # un separador de columnas real.
    if huecos[idx_mayor_hueco] < 0.08 * ancho_pagina or not (0.3 <= posicion_relativa <= 0.7):
        return [0] * len(centros_x)

    separador = (xs_ordenados[idx_mayor_hueco] + xs_ordenados[idx_mayor_hueco + 1]) / 2
    return [0 if x < separador else 1 for x in centros_x]


def _ordenar_por_lectura(polys: List[np.ndarray], textos: List[str]) -> List[str]:
    """Reordena las cajas detectadas a orden de lectura humano (columna,
    línea, izquierda-a-derecha) en vez del orden de detección del modelo,
    que no sigue ningún orden de lectura garantizado.
    """
    if not textos:
        return []

    centros_x = np.array([poly[:, 0].mean() for poly in polys])
    y_tops = np.array([poly[:, 1].min() for poly in polys])
    alturas = np.array([poly[:, 1].max() - poly[:, 1].min() for poly in polys])
    altura_media = float(np.median(alturas)) if len(alturas) else 20.0

    ancho_pagina = float(centros_x.max() - centros_x.min()) if len(centros_x) > 1 else 0.0
    columna_idx = _detectar_columna(centros_x, ancho_pagina)

    indices = list(range(len(textos)))
    indices.sort(key=lambda i: (
        columna_idx[i],
        round(y_tops[i] / max(altura_media, 1.0)),  # agrupa por línea aproximada
        centros_x[i],
    ))
    return [textos[i] for i in indices]


def _extraer_texto_y_confianza(imagen_bgr: np.ndarray) -> Tuple[str, float]:
    resultado = _motor.predict(imagen_bgr)

    lineas: List[str] = []
    puntuaciones: List[float] = []
    for pagina_resultado in resultado:
        textos = pagina_resultado.get("rec_texts", [])
        polys = pagina_resultado.get("rec_polys", [])
        scores = pagina_resultado.get("rec_scores", [])

        tripletas = [(t, p, s) for t, p, s in zip(textos, polys, scores) if t and t.strip()]
        if not tripletas:
            continue

        textos_filtrados = [t for t, _, _ in tripletas]
        polys_filtrados = [p for _, p, _ in tripletas]
        puntuaciones.extend(s for _, _, s in tripletas)
        lineas.extend(_ordenar_por_lectura(polys_filtrados, textos_filtrados))

    texto = "\n".join(lineas)
    # Página sin ninguna línea detectada: confianza 0.0 (no 1.0) a propósito,
    # para que dispare el reprocesado con la variante server en vez de darse
    # por buena en silencio — podría ser una página realmente en blanco o
    # podría ser que la variante mobile no encontró nada por mala calidad.
    confianza = float(np.mean(puntuaciones)) if puntuaciones else 0.0
    return texto, confianza


def _ocr_una_pagina(indice: int, imagen_png: bytes, variante: str) -> dict:
    """Tarea ejecutada en un proceso worker del Pool. Debe ser una función
    de nivel de módulo (no un closure/lambda ni un método) para que
    multiprocessing pueda enviarla (picklear su referencia) a los workers.

    La imagen viaja como PNG comprimido, no como array numpy crudo: a 300
    DPI una página sin comprimir ronda los 25 MB, y con cientos de páginas
    el coste de serializar/enviar eso a cada worker por IPC se vuelve
    significativo frente al propio cómputo de OCR.

    `variante` ya no decide aquí si hay que reprocesar — ese enrutado entre
    el pool mobile y el pool server dedicado lo hace _ejecutar_ocr_paginas,
    porque cada pool solo tiene cargada una variante (ver
    OCR_PROCESOS_SERVER). Esta función solo ejecuta la variante que le
    corresponde al worker que la llama, sea cual sea.
    """
    imagen_bgr = cv2.imdecode(np.frombuffer(imagen_png, dtype=np.uint8), cv2.IMREAD_COLOR)
    preprocesada = _deskew_y_limpiar(imagen_bgr)
    texto, confianza = _extraer_texto_y_confianza(preprocesada)

    logger.info("Página %d procesada (%s, confianza=%.3f, %d caracteres)", indice, variante, confianza, len(texto))
    return {"pagina": indice, "texto": texto, "confianza_ocr": confianza, "variante_ocr": variante}


# Dos pools independientes, uno por variante (ver OCR_PROCESOS_SERVER para
# el porqué). Un dict en vez de dos variables sueltas para que la lógica de
# reintento/recreación (_ejecutar_lote) sea una sola función reutilizada por
# las dos fases, no dos copias casi idénticas.
_executores: Dict[str, Optional[ProcessPoolExecutor]] = {"mobile": None, "server": None}
_contexto_mp = multiprocessing.get_context("spawn")

# /ocr puede atenderse concurrentemente (run_in_threadpool, varios documentos
# a la vez), y todos los que usan la misma variante comparten el mismo
# ProcessPoolExecutor. Un lock por variante evita que dos peticiones que
# detectan el mismo pool roto a la vez (un worker muerto invalida TODAS las
# tareas pendientes del pool, no solo las del documento que lo detectó
# primero) lo recreen cada una por su cuenta — sin esto, la segunda deja el
# executor recién creado por la primera huérfano: procesos worker ya
# lanzados (con sus modelos ya cargados en memoria) que nadie vuelve a usar
# ni a apagar, una fuga real de procesos y memoria del sistema.
_locks_recreacion: Dict[str, threading.Lock] = {"mobile": threading.Lock(), "server": threading.Lock()}


def _recrear_si_sigue_siendo_este(variante: str, executor_roto: ProcessPoolExecutor) -> None:
    """Mata a la fuerza y recrea el pool de una variante, pero solo si
    `executor_roto` sigue siendo el pool activo en este momento. Si otra
    petición concurrente ya lo recreó mientras se esperaba el lock, no hay
    nada que hacer — evita la recreación redundante descrita arriba.
    """
    with _locks_recreacion[variante]:
        if _executores[variante] is executor_roto:
            _matar_workers_a_la_fuerza(executor_roto)
            executor_roto.shutdown(wait=False, cancel_futures=True)
            _executores[variante] = _crear_executor(variante)


def _reciclar_executor(variante: str, executor_actual: ProcessPoolExecutor) -> None:
    """Apaga limpiamente (sin forzar) y recrea el pool de una variante tras
    una tarea ya terminada con éxito — ver _ejecutar_lote_server_reciclando.
    Mismo chequeo de identidad bajo lock que _recrear_si_sigue_siendo_este,
    por la misma razón: evita reciclar/recrear por duplicado si dos
    peticiones concurrentes reciclan el mismo pool casi a la vez.

    Comprobado en pruebas reales (2026-08-25/26, tras ~1h20 de proceso real
    de un documento de 500 páginas): un worker puede morir justo en la
    estrecha ventana entre "el chunk terminó con éxito" y que este
    shutdown(wait=True) "limpio" complete, dejando `BrokenProcessPool` sin
    capturar aquí — escapaba hasta el handler genérico del endpoint,
    perdiendo el detalle de qué páginas habían quedado pendientes ("A child
    process terminated abruptly..." en vez del RuntimeError informativo
    habitual). Con este try/except, si el apagado limpio falla por
    cualquier motivo, se recurre al apagado forzoso (mismo mecanismo que
    _recrear_si_sigue_siendo_este) en vez de dejar la excepción sin
    controlar.
    """
    with _locks_recreacion[variante]:
        if _executores[variante] is executor_actual:
            try:
                executor_actual.shutdown(wait=True)
            except Exception:
                logger.exception(
                    "[%s] Fallo al reciclar limpiamente el pool (posible worker caído durante el "
                    "propio reciclado) — forzando apagado.", variante,
                )
                _matar_workers_a_la_fuerza(executor_actual)
            _executores[variante] = _crear_executor(variante)


def _crear_executor(variante: str) -> ProcessPoolExecutor:
    # "spawn" en vez del "fork" por defecto en Linux: evitar heredar por
    # fork el estado de hilos/librerías nativas (BLAS de paddlepaddle) del
    # proceso principal, fuente conocida de bloqueos con multiprocessing +
    # librerías de cómputo numérico.
    num_procesos = OCR_PROCESOS if variante == "mobile" else OCR_PROCESOS_SERVER
    return ProcessPoolExecutor(
        max_workers=num_procesos,
        mp_context=_contexto_mp,
        initializer=_inicializar_worker,
        initargs=(variante,),
    )


@asynccontextmanager
async def lifespan(app: FastAPI):
    # Los executors viven toda la vida del proceso FastAPI, no uno por
    # petición: crearlos por petición recargaría los modelos (lento) en cada
    # llamada a /ocr.
    for variante in _executores:
        _executores[variante] = _crear_executor(variante)
    logger.info(
        "Pools de OCR listos: %d proceso(s) mobile + %d proceso(s) server dedicado(s), umbral de confianza %.2f.",
        OCR_PROCESOS, OCR_PROCESOS_SERVER, OCR_CONFIANZA_MINIMA,
    )
    try:
        yield
    finally:
        for executor in _executores.values():
            executor.shutdown(wait=False, cancel_futures=True)


app = FastAPI(title="OCR PaddleOCR (PP-OCRv6, mobile/small con reintento server/medium)", lifespan=lifespan)


def _matar_workers_a_la_fuerza(executor: ProcessPoolExecutor) -> None:
    """Termina con SIGKILL los procesos worker del executor, en vez de
    limitarse a "olvidar" sus tareas.

    concurrent.futures no expone una forma pública de cancelar una tarea ya
    en marcha ni de matar sus workers — shutdown(cancel_futures=True) solo
    descarta tareas que ni siquiera habían empezado. Si un worker se queda
    de verdad atascado (no muerto: un cuelgue silencioso dentro de la
    inferencia nativa de Paddle) seguiría vivo consumiendo CPU/memoria para
    siempre. _processes es una API interna de concurrent.futures.process,
    pero es la única manera de garantizar que el proceso muere de verdad —
    justo lo que exige no dejar la máquina bloqueada.
    """
    procesos = list(getattr(executor, "_processes", {}).values())
    for proceso in procesos:
        if proceso.is_alive():
            logger.warning("Matando a la fuerza el proceso worker %d (colgado tras el límite de tiempo).", proceso.pid)
            proceso.kill()
    for proceso in procesos:
        proceso.join(timeout=10)


def _ejecutar_chunk(variante: str, tareas: List[Tuple[int, bytes, str]], plazo_absoluto: float) -> Dict[int, dict]:
    """Ejecuta UN lote (chunk) de páginas contra el pool de una variante, con
    dos redes de seguridad independientes para que un documento nunca se
    quede colgado indefinidamente ni pueda bloquear la máquina:

    1. BrokenProcessPool (worker muerto, p.ej. OOM-killer): ProcessPoolExecutor
       lo detecta solo porque el worker YA NO EXISTE. Se recrea el pool de
       esta variante y se reintentan las páginas perdidas, hasta
       OCR_INTENTOS_POR_CHUNK veces en total.
    2. Timeout global (`plazo_absoluto`, un instante monotonic() compartido
       entre TODOS los chunks y las fases mobile y server de un mismo
       documento — ver _ejecutar_ocr_paginas): cubre el caso contrario, un
       worker que sigue "vivo" pero atascado de verdad. Si se supera, se
       matan a la fuerza los workers que sigan corriendo (ver
       _matar_workers_a_la_fuerza) y se abandona.

    No lanza excepción si quedan páginas sin procesar — solo devuelve lo que
    consiguió; es _ejecutar_lote quien decide si eso implica abandonar el
    resto de chunks y lanzar el RuntimeError final.
    """
    pendientes = {t[0]: t for t in tareas}
    resultados: Dict[int, dict] = {}

    for intento in range(OCR_INTENTOS_POR_CHUNK):
        plazo_restante = plazo_absoluto - time.monotonic()
        if plazo_restante <= 0:
            break

        executor = _executores[variante]
        try:
            futuros = {executor.submit(_ocr_una_pagina, *t): idx for idx, t in pendientes.items()}
        except BrokenProcessPool:
            # El pool ya estaba roto ANTES de poder enviar ninguna tarea de
            # este intento (no durante la espera de resultados, que es el
            # caso ya cubierto más abajo) — comprobado en pruebas reales que
            # sin este except, submit() puede lanzar BrokenProcessPool sin
            # control y escapar hasta el handler genérico del endpoint.
            logger.error(
                "[%s] El pool ya estaba roto al intentar enviar %d página(s): %s. Recreando.",
                variante, len(pendientes), sorted(pendientes),
            )
            _recrear_si_sigue_siendo_este(variante, executor)
            continue
        listos, no_listos = futures_wait(futuros, timeout=plazo_restante)

        for futuro in listos:
            idx = futuros[futuro]
            try:
                resultados[idx] = futuro.result()
            except BrokenProcessPool:
                pass  # se reintenta más abajo junto con el resto de páginas perdidas
            except Exception:
                # Cualquier otro fallo dentro de _ocr_una_pagina (imagen
                # corrupta, cv2.error, etc.) — el worker en sí sigue vivo,
                # no es un BrokenProcessPool, pero sin este except la
                # excepción se propagaría sin control fuera de este chunk y
                # tiraría TODO el documento con un 500 no controlado en vez
                # de seguir el camino de fallo explícito (RuntimeError con la
                # lista de páginas pendientes) que el resto del módulo espera.
                logger.exception("[%s] Fallo procesando la página %d (no es un worker caído).", variante, idx)

        if no_listos:
            perdidas = sorted(futuros[f] for f in no_listos)
            logger.error(
                "[%s] Límite de tiempo agotado con %d página(s) sin terminar: %s. Matando workers y abandonando.",
                variante, len(perdidas), perdidas,
            )
            _recrear_si_sigue_siendo_este(variante, executor)
            break  # el plazo global ya se agotó: no tiene sentido reintentar

        pendientes = {idx: t for idx, t in pendientes.items() if idx not in resultados}
        if not pendientes:
            break

        logger.error(
            "[%s] Pool de OCR roto (worker caído, intento %d/%d). Recreando y reintentando %d página(s): %s",
            variante, intento + 1, OCR_INTENTOS_POR_CHUNK, len(pendientes), sorted(pendientes),
        )
        _recrear_si_sigue_siendo_este(variante, executor)

    return resultados


def _ejecutar_lote(variante: str, tareas: List[Tuple[int, bytes, str]], plazo_absoluto: float) -> Dict[int, dict]:
    """Reparte `tareas` en chunks de OCR_RECICLAR_CADA_N_PAGINAS páginas (solo
    para "mobile" — el pool server ya se recicla por página, ver
    _ejecutar_lote_server_reciclando) y los ejecuta uno tras otro con
    _ejecutar_chunk, reciclando el pool completo entre chunks para acotar el
    crecimiento de memoria de los workers a lo largo de documentos largos
    (ver OCR_RECICLAR_CADA_N_PAGINAS más arriba — comprobado con `dmesg` que
    sin esto, un worker mobile de larga vida podía llegar a 4+ GB de RSS y
    disparar el OOM-killer del kernel tras 84-140 páginas).

    Si un chunk no consigue procesar todas sus páginas (timeout global
    agotado, o el segundo intento tras un worker roto también falló), se
    abandona sin ejecutar los chunks siguientes — el plazo global ya se
    agotó o el pool sigue fallando, seguir no tiene sentido.

    Se lanza RuntimeError en vez de devolver un resultado con páginas
    faltantes: el Worker de .NET usa len(páginas) para calcular el rango de
    folios (Bates) del documento, así que una respuesta parcial silenciosa
    produciría una numeración legal incorrecta — es preferible un fallo
    explícito y reintentable.
    """
    tareas_ordenadas = sorted(tareas, key=lambda t: t[0])
    tamano_chunk = OCR_RECICLAR_CADA_N_PAGINAS if variante == "mobile" and OCR_RECICLAR_CADA_N_PAGINAS > 0 else len(tareas_ordenadas)
    tamano_chunk = tamano_chunk or 1

    resultados: Dict[int, dict] = {}
    for inicio in range(0, len(tareas_ordenadas), tamano_chunk):
        chunk = tareas_ordenadas[inicio:inicio + tamano_chunk]
        resultados_chunk = _ejecutar_chunk(variante, chunk, plazo_absoluto)
        resultados.update(resultados_chunk)

        if len(resultados_chunk) < len(chunk):
            break  # este chunk no se completó del todo: no seguir con los siguientes

        plazo_restante = plazo_absoluto - time.monotonic()
        if variante == "mobile" and plazo_restante > 0:
            # Se recicla también tras el ÚLTIMO chunk, no solo "si quedan
            # más" — comprobado en pruebas reales (2026-08-26) que sin esto,
            # los workers mobile siguen residentes con su memoria de
            # trabajo (no la base en reposo) durante TODA la fase server
            # siguiente, dejando muy poco margen al worker server (que
            # necesita hasta ~4 GB) dentro del mismo límite de memoria del
            # contenedor — provocó un OOM sistemático y reproducible (RSS
            # casi idéntico en cada caída) en el pool server, en un
            # documento real con páginas degradadas. El coste de este
            # reciclado extra (~10-15s) es insignificante frente al riesgo
            # de que la fase server entera falle por falta de memoria.
            logger.info(
                "[%s] Reciclando el pool tras %d/%d página(s) para acotar el crecimiento de memoria "
                "de los workers en documentos largos.",
                variante, inicio + len(chunk), len(tareas_ordenadas),
            )
            _reciclar_executor(variante, _executores[variante])

    pendientes = sorted(t[0] for t in tareas if t[0] not in resultados)
    if pendientes:
        raise RuntimeError(
            f"[{variante}] No se pudieron procesar {len(pendientes)} página(s) (worker roto y/o límite de "
            f"tiempo agotado): {pendientes}"
        )

    return resultados


def _ejecutar_lote_server_reciclando(tareas: List[Tuple[int, bytes, str]], plazo_absoluto: float) -> Dict[int, dict]:
    """Variante de _ejecutar_lote específica para el pool server: procesa
    las páginas UNA A UNA, reciclando (recreando) el único proceso del pool
    tras cada una, en vez de repartir todo el lote de una vez.

    Comprobado en pruebas (2026-08-24): con el pool ya separado del mobile
    (ver OCR_PROCESOS_SERVER), procesar varias páginas degradadas SEGUIDAS
    en el mismo proceso server (la variante más pesada en memoria de las
    dos) seguía haciendo crecer la RSS hasta ~4 GB y disparaba el
    OOM-killer real (`dmesg`), aunque cada proceso ya cargaba una sola
    variante. Como OCR_PROCESOS_SERVER=1 por defecto no hay paralelismo que
    perder al reciclar entre página y página — el coste es ~9-20s de
    recarga de modelo por página degradada (la ruta ya lenta y minoritaria
    por diseño), a cambio de acotar el pico de memoria a lo que necesita UNA
    sola página, no varias acumuladas en el mismo proceso.
    """
    resultados: Dict[int, dict] = {}

    for indice, png, variante in tareas:
        plazo_restante = plazo_absoluto - time.monotonic()
        if plazo_restante <= 0:
            break

        executor = _executores["server"]
        try:
            # submit() dentro del try, no solo result(): comprobado en
            # pruebas reales que el pool puede estar ya roto ANTES de poder
            # enviar la tarea (no solo mientras se espera el resultado), y
            # sin esto BrokenProcessPool escapaba sin control.
            futuro = executor.submit(_ocr_una_pagina, indice, png, variante)
            resultados[indice] = futuro.result(timeout=plazo_restante)
            _reciclar_executor("server", executor)  # éxito: reciclar igualmente, no solo tras fallo
        except BrokenProcessPool:
            logger.error("[server] Worker caído procesando la página %d. Recreando y reintentando.", indice)
            _recrear_si_sigue_siendo_este("server", executor)

            plazo_restante = plazo_absoluto - time.monotonic()
            if plazo_restante > 0:
                executor = _executores["server"]
                try:
                    futuro = executor.submit(_ocr_una_pagina, indice, png, variante)
                    resultados[indice] = futuro.result(timeout=plazo_restante)
                    _reciclar_executor("server", executor)
                except (BrokenProcessPool, FuturoTimeoutError):
                    pass  # se cuenta como pendiente más abajo
                except Exception:
                    logger.exception("[server] Fallo procesando la página %d (no es un worker caído).", indice)
        except FuturoTimeoutError:
            logger.error(
                "[server] Límite de tiempo agotado procesando la página %d. Matando worker y abandonando.",
                indice,
            )
            _recrear_si_sigue_siendo_este("server", executor)
            break  # el plazo global ya se agotó: no tiene sentido seguir
        except Exception:
            # Ver el except Exception equivalente en _ejecutar_lote: un
            # fallo dentro de _ocr_una_pagina que no es ni BrokenProcessPool
            # ni timeout (p.ej. imagen corrupta) no debe tirar toda la
            # petición — el worker sigue vivo, así que ni siquiera hace
            # falta matar/recrear el pool, solo dejar la página como
            # pendiente para que el RuntimeError final la reporte.
            logger.exception("[server] Fallo procesando la página %d (no es un worker caído).", indice)

    pendientes = sorted(t[0] for t in tareas if t[0] not in resultados)
    if pendientes:
        raise RuntimeError(
            f"[server] No se pudieron procesar {len(pendientes)} página(s) (worker roto y/o límite de "
            f"tiempo agotado): {pendientes}"
        )

    return resultados


def _ejecutar_ocr_paginas(paginas_png: List[Tuple[int, bytes]]) -> List[dict]:
    """Orquesta las dos fases del pipeline dual-variant, cada una contra su
    propio pool (ver _executores y OCR_PROCESOS_SERVER): primero TODAS las
    páginas van al pool mobile; las que queden por debajo de
    OCR_CONFIANZA_MINIMA se reprocesan en el pool server, dedicado — salvo
    que OCR_HABILITAR_SERVER esté desactivado (por defecto desde el
    2026-08-26, ver ese comentario), en cuyo caso esas páginas se quedan
    con el resultado de mobile tal cual. Las dos fases, cuando se ejecutan,
    comparten el mismo plazo absoluto (OCR_TIMEOUT_SEGUNDOS contado desde
    el principio del documento), no uno por fase — así una fase lenta no
    puede duplicar el tiempo máximo total de la petición.
    """
    plazo_absoluto = time.monotonic() + OCR_TIMEOUT_SEGUNDOS

    tareas_mobile = [(idx, png, "mobile") for idx, png in paginas_png]
    resultados = _ejecutar_lote("mobile", tareas_mobile, plazo_absoluto)

    png_por_indice = dict(paginas_png)
    # Disparador combinado: confianza baja O alucinación CJK detectada. Una
    # sola de las dos condiciones no basta — hay páginas con confianza por
    # ENCIMA del umbral que igualmente tenían caracteres CJK colados (ver
    # _contiene_cjk), así que confiar solo en el umbral se las saltaría.
    pendientes_server = sorted(
        idx for idx, r in resultados.items()
        if r["confianza_ocr"] < OCR_CONFIANZA_MINIMA or _contiene_cjk(r["texto"])
    )
    if pendientes_server and not OCR_HABILITAR_SERVER:
        logger.info(
            "%d página(s) con confianza baja y/o alucinación CJK, pero OCR_HABILITAR_SERVER está "
            "desactivado — se quedan con el resultado de mobile: %s",
            len(pendientes_server), pendientes_server,
        )
    elif pendientes_server:
        logger.info(
            "%d página(s) con confianza baja y/o alucinación CJK, reprocesando con server: %s",
            len(pendientes_server), pendientes_server,
        )
        tareas_server = [(idx, png_por_indice[idx], "server") for idx in pendientes_server]
        resultados.update(_ejecutar_lote_server_reciclando(tareas_server, plazo_absoluto))

    return [resultados[i] for i in sorted(resultados)]


def _renderizar_paginas_pdfium(args: Tuple[str, List[int], str, int]) -> None:
    """Worker de proceso aparte: renderiza un subconjunto de páginas a PNG en
    disco. Cada proceso abre su propio PdfDocument (pypdfium2 no admite
    compartir un mismo documento entre procesos) leyendo del PDF ya escrito
    en disco (`ruta_pdf`), no de bytes serializados por IPC — evita
    duplicar el PDF completo (hasta ~1.5GB visto en pruebas reales) en la
    memoria de cada proceso hijo.
    """
    ruta_pdf, indices, tmpdir, dpi = args
    pdf = pdfium.PdfDocument(ruta_pdf)
    escala = dpi / 72
    try:
        for i in indices:
            pagina = pdf[i]
            try:
                bitmap = pagina.render(scale=escala)
                bitmap.to_pil().save(os.path.join(tmpdir, f"pagina_{i:06d}.png"))
            finally:
                pagina.close()
    finally:
        pdf.close()


def _paginas_a_png(contenido: bytes, es_pdf: bool) -> List[bytes]:
    """Decodifica el archivo recibido a una lista de páginas PNG.

    Para PDF, cada página se renderiza a disco (nunca se acumulan imágenes
    decodificadas en memoria a la vez): a 300 DPI cada página sin comprimir
    ronda los 25 MB, así que un documento de 500 páginas llegaría a ~13 GB
    solo en imágenes decodificadas dentro del propio proceso de Uvicorn —
    eso es lo que provocaba el OOM-kill real observado en pruebas (mataba al
    proceso principal, no a un worker, así que ni ProcessPoolExecutor ni el
    reintento por BrokenProcessPool podían detectarlo ni recuperarse). Aquí
    se escribe, lee, comprime y descarta cada archivo de página de una en
    una.

    Motor: pypdfium2 (PDFium, Apache-2.0/BSD-3, sin problema de licencia
    AGPL) en vez de pdftoppm/poppler (vía pdf2image) — medido 2026-09-03
    sobre 40 páginas reales de la Gaceta de Madrid a 300 DPI: 0.124s/página
    frente a 0.735s/página de pdftoppm con thread_count=4, ~6x más rápido.
    Paralelizado con ProcessPoolExecutor (ctx "spawn", no "fork": evita
    compartir el estado interno de PDFium entre procesos) en vez de hilos,
    porque un PdfDocument de pypdfium2 no es seguro de usar desde varios
    hilos a la vez.
    """
    if not es_pdf:
        imagen = Image.open(io.BytesIO(contenido)).convert("RGB")
        buffer = io.BytesIO()
        imagen.save(buffer, format="PNG")
        return [buffer.getvalue()]

    with tempfile.TemporaryDirectory() as tmpdir:
        ruta_pdf = os.path.join(tmpdir, "entrada.pdf")
        with open(ruta_pdf, "wb") as f:
            f.write(contenido)

        pdf = pdfium.PdfDocument(ruta_pdf)
        n_paginas = len(pdf)
        pdf.close()

        n_procesos = min(OCR_PROCESOS, n_paginas) or 1
        indices = list(range(n_paginas))
        lotes = [indices[i::n_procesos] for i in range(n_procesos)]
        tareas = [(ruta_pdf, lote, tmpdir, 300) for lote in lotes if lote]

        ctx = multiprocessing.get_context("spawn")
        with ProcessPoolExecutor(max_workers=n_procesos, mp_context=ctx) as executor:
            list(executor.map(_renderizar_paginas_pdfium, tareas))

        paginas_png = []
        for i in range(n_paginas):
            ruta = os.path.join(tmpdir, f"pagina_{i:06d}.png")
            with open(ruta, "rb") as f:
                paginas_png.append(f.read())
            os.remove(ruta)
        return paginas_png


@app.post("/ocr", response_model=OcrResponse)
async def ocr(file: UploadFile = File(...)) -> OcrResponse:
    contenido = await file.read()
    if not contenido:
        raise HTTPException(status_code=400, detail="Archivo vacío.")

    content_type = (file.content_type or "").lower()
    es_pdf = content_type == "application/pdf" or (file.filename or "").lower().endswith(".pdf")

    try:
        # También delegado a un hilo aparte: pdftoppm (subproceso) puede
        # tardar bastante en un documento largo y no debe bloquear el event
        # loop mientras tanto.
        paginas_png = await run_in_threadpool(_paginas_a_png, contenido, es_pdf)
    except Exception as exc:
        logger.exception("No se pudo decodificar el archivo recibido")
        raise HTTPException(status_code=422, detail=f"No se pudo leer el archivo: {exc}") from exc

    paginas_indexadas = list(enumerate(paginas_png, start=1))

    # _ejecutar_ocr_paginas bloquea el hilo actual hasta que todas las
    # páginas terminan; se delega a un hilo aparte (run_in_threadpool) para
    # no congelar el event loop de Uvicorn mientras tanto (p.ej. /health
    # seguiría respondiendo durante un documento largo).
    try:
        resultados = await run_in_threadpool(_ejecutar_ocr_paginas, paginas_indexadas)
    except RuntimeError as exc:
        # BrokenProcessPool hereda de RuntimeError — si una instancia suya
        # escapa sin control desde algún punto de _ejecutar_ocr_paginas no
        # protegido explícitamente, cae aquí con su mensaje genérico de
        # stdlib ("A child process terminated abruptly...") en vez del
        # RuntimeError informativo habitual (con la lista de páginas
        # pendientes) — comprobado en pruebas reales (2026-08-25/26). Se
        # sigue devolviendo 500 igual, pero al menos queda registrado con
        # traceback completo para localizar el punto exacto si se repite.
        logger.exception("Fallo irrecuperable procesando el documento")
        raise HTTPException(status_code=500, detail=str(exc)) from exc
    except Exception as exc:
        # Red de seguridad final: cualquier otra excepción inesperada que no
        # sea RuntimeError tampoco debe perderse sin traceback ni devolver
        # una respuesta a medias.
        logger.exception("Fallo inesperado (no RuntimeError) procesando el documento")
        raise HTTPException(status_code=500, detail=f"Fallo inesperado: {exc}") from exc

    paginas = [PaginaTexto(**r) for r in sorted(resultados, key=lambda r: r["pagina"])]
    return OcrResponse(paginas=paginas)


@app.get("/health")
async def health():
    return {"status": "ok"}
