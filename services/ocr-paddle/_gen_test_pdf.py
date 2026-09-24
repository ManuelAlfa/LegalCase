"""Generador del documento de prueba (~500 páginas) para medir el pipeline
de OCR: texto realista renderizado a imagen + una franja minoritaria de
páginas degradadas a propósito (desenfoque + ruido + bajo contraste), para
comprobar que esas páginas concretas caen por debajo del umbral de confianza
y se reprocesan con la variante "server". Script de un solo uso, no forma
parte del producto — se ejecuta dentro del contenedor ocr-paddle porque ya
trae Pillow/numpy/opencv instalados.

El lienzo se genera a 2481x3508 px (A4 a 300 DPI real) y se guarda con
resolution=300: si no se fija resolution al guardar, Pillow interpreta los
píxeles como puntos PDF (72/pulgada) y la página queda ~4x más grande de lo
debido en cada eje — con /ocr rasterizando siempre a 300 DPI (ver main.py),
eso produce imágenes de decenas de megapíxeles que revientan la memoria del
proceso de OCR. Ese fue justo el bug que causó el OOM en la primera prueba.
"""
import os
import random
import subprocess
import sys
import tempfile

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ANCHO, ALTO = 2481, 3508  # A4 a 300 DPI real
FUENTE = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf", 39)
FUENTE_TITULO = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf", 48)

random.seed(20260812)

PARRAFOS_BASE = [
    "En la Villa de Madrid, siendo las diez horas del día señalado, se procede a dar "
    "lectura al presente escrito conforme a lo dispuesto en la Ley de Enjuiciamiento "
    "Civil, compareciendo las partes debidamente representadas por sus respectivos "
    "letrados y procuradores, quienes ratifican en este acto el contenido íntegro de "
    "los escritos previamente presentados ante este Juzgado.",
    "PRIMERO.- Que la parte actora fundamenta su pretensión en los hechos expuestos "
    "en el escrito de demanda, solicitando se dicte sentencia por la que se condene a "
    "la parte demandada al cumplimiento íntegro de las obligaciones contractuales "
    "asumidas, con expresa imposición de costas procesales a la parte contraria.",
    "SEGUNDO.- Que examinada la documentación aportada, obrante en autos a los folios "
    "correspondientes, resulta acreditado que las partes suscribieron el contrato de "
    "referencia con fecha cierta, sin que conste impugnación alguna sobre su "
    "autenticidad ni sobre la capacidad de obrar de los otorgantes.",
    "TERCERO.- Que en cuanto a la valoración de la prueba practicada, este juzgador "
    "considera, de conformidad con las reglas de la sana crítica establecidas en el "
    "artículo 316 de la Ley de Enjuiciamiento Civil, que los hechos controvertidos "
    "han quedado suficientemente esclarecidos a través de la prueba documental y "
    "testifical practicada en el acto de la vista oral.",
    "CUARTO.- Que procede, en consecuencia, estimar parcialmente la demanda "
    "interpuesta, condenando a la parte demandada al abono de la cantidad reclamada "
    "más los intereses legales devengados desde la fecha de interposición de la "
    "demanda hasta su completo pago, sin perjuicio de lo dispuesto en el fallo.",
]


def _pagina_limpia(numero: int) -> Image.Image:
    img = Image.new("RGB", (ANCHO, ALTO), "white")
    draw = ImageDraw.Draw(img)

    draw.text((180, 150), f"EXPEDIENTE DE PRUEBA — FOLIO {numero}", font=FUENTE_TITULO, fill="black")
    draw.line([(180, 225), (ANCHO - 180, 225)], fill="black", width=3)

    y = 330
    for _ in range(4):
        parrafo = random.choice(PARRAFOS_BASE)
        lineas = _envolver_texto(parrafo, FUENTE, ANCHO - 360)
        for linea in lineas:
            draw.text((180, y), linea, font=FUENTE, fill="black")
            y += 57
        y += 45

    draw.text((ANCHO - 300, ALTO - 120), f"- {numero} -", font=FUENTE, fill="black")
    return img


def _envolver_texto(texto: str, fuente: ImageFont.FreeTypeFont, ancho_max: int) -> list[str]:
    palabras = texto.split()
    lineas, actual = [], ""
    for palabra in palabras:
        prueba = f"{actual} {palabra}".strip()
        if fuente.getlength(prueba) <= ancho_max:
            actual = prueba
        else:
            lineas.append(actual)
            actual = palabra
    if actual:
        lineas.append(actual)
    return lineas


def _degradar(img: Image.Image) -> Image.Image:
    """Simula un escaneo/fotocopia de mala calidad: desenfoque, ruido
    gaussiano y bajo contraste — el tipo de página real que motiva tener
    una variante "server" más precisa como red de seguridad.

    Intensidad calibrada empíricamente contra el generador real de páginas
    (no a ojo): con (9,3.0,28,0.45,90) la confianza media de la variante
    mobile quedaba en ~0.97, por encima del umbral por defecto (0.80) — la
    página "se veía" degradada pero el modelo seguía muy seguro de su
    lectura, así que nunca disparaba el reprocesado con server. Con estos
    parámetros, mobile cae a ~0.47-0.50 (bajo el umbral con margen) y
    server mejora sobre eso (~0.58) sin llegar a texto perfecto — un caso
    realista de "escaneo malo que server rescata parcialmente", no un caso
    de laboratorio donde server lo arregla todo.
    """
    arr = cv2.cvtColor(np.array(img), cv2.COLOR_RGB2BGR)
    arr = cv2.GaussianBlur(arr, (15, 15), sigmaX=5.0)

    ruido = np.random.normal(0, 35, arr.shape).astype(np.float32)
    arr = np.clip(arr.astype(np.float32) + ruido, 0, 255).astype(np.uint8)

    # Bajo contraste: comprime el rango dinámico hacia el gris medio.
    arr = cv2.convertScaleAbs(arr, alpha=0.38, beta=95)

    return Image.fromarray(cv2.cvtColor(arr, cv2.COLOR_BGR2RGB))


def main():
    total_paginas = int(sys.argv[1]) if len(sys.argv) > 1 else 500
    # ~8% de páginas degradadas, repartidas en bloques (como manchas reales
    # de escaneo por lotes), no una por una intercaladas uniformemente.
    degradadas = set()
    num_bloques = max(1, total_paginas // 60)
    for _ in range(num_bloques):
        inicio = random.randint(1, total_paginas - 5)
        for p in range(inicio, min(inicio + 4, total_paginas + 1)):
            degradadas.add(p)

    salida = sys.argv[2] if len(sys.argv) > 2 else "/tmp/prueba_500_paginas.pdf"

    # Cada página (2481x3508 px, ~26 MB sin comprimir) se guarda a disco
    # como PDF de una sola hoja en cuanto se genera, en vez de acumular las
    # 500 en una lista en memoria: 500 páginas completas a la vez agotaban
    # la RAM y el proceso moría por SIGKILL (OOM) antes de llegar a
    # escribir nada. pdfunite (poppler-utils) fusiona los ficheros
    # individuales al final sin cargarlos todos en Python.
    with tempfile.TemporaryDirectory() as tmp:
        rutas_paginas = []
        for numero in range(1, total_paginas + 1):
            img = _pagina_limpia(numero)
            if numero in degradadas:
                img = _degradar(img)

            ruta_pagina = os.path.join(tmp, f"pagina_{numero:04d}.pdf")
            img.save(ruta_pagina, resolution=300.0)
            rutas_paginas.append(ruta_pagina)
            del img

            if numero % 50 == 0:
                print(f"...{numero}/{total_paginas} páginas generadas", file=sys.stderr)

        subprocess.run(["pdfunite", *rutas_paginas, salida], check=True)

    print(f"OK {total_paginas} páginas, {len(degradadas)} degradadas: {sorted(degradadas)}", file=sys.stderr)


if __name__ == "__main__":
    main()
