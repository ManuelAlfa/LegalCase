"""Curva de degradación: fidelidad, confianza y tiempo del OCR en cada nivel
de degradado de un mismo documento, con la configuración de producción.

Sirve de referencia antes de tocar el motor: un cambio se da por bueno si
mejora (o no empeora) TODOS los niveles, no solo el más limpio.

    docker run --rm --entrypoint python -v "$PWD/services/ocr-paddle":/src \\
        -v "$PWD/test-data":/data -v ocr_paddle_models:/root/.paddlex \\
        -v /ruta/salida:/out \\
        legal-case-management-ocr-paddle:latest /src/pruebas/curva_degradacion.py

Documentos: test-data/degradado/ (banco del Código Penal del BOE, 13 págs.).
La verdad es el texto digital de BOE_CP_13pag_original.pdf, página a página.
Si existe /out, deja allí el texto reconocido de cada nivel
(curva_<nivel>_caja<umbral>.txt, páginas separadas por \f) para mirar las diferencias con fidelidad.py.

Para comparar ajustes, pasar variables de entorno del servicio con -e
(p. ej. -e OCR_DET_UMBRAL_CAJA=0.4).

Mide en un solo proceso, como uno de los workers del servicio: el tiempo es
por página y por proceso, no el del documento completo con todos los
workers en paralelo.
"""
import difflib
import os
import re
import sys
import time

import cv2
import numpy as np
import pypdfium2 as pdfium

sys.path.insert(0, "/src")
import main  # noqa: E402

CARPETA = "/data/degradado"
NIVELES = ["original", "degradado_nivel1_1", "degradado_nivel2", "degradado_nivel3", "degradado_nivel4"]
# Una página "mal leída" a pesar de que el motor dice estar seguro: es lo
# que haría inútil un aviso basado en la confianza.
FIDELIDAD_MALA, CONFIANZA_ALTA = 0.90, 0.90


def normalizar(texto):
    texto = re.sub(r"-\s*\n\s*", "", texto.replace("\r", " "))
    # Líneas de puntos de los índices ("De las coacciones ....... 71"): la
    # referencia y el OCR las escriben distinto (más o menos puntos, con o
    # sin espacios) y hundían la fidelidad de páginas bien leídas. Fuera de
    # la medida: lo que importa es el texto, no cuántos puntos hay.
    texto = re.sub(r"(?:\.\s*){3,}", " ", texto)
    return re.sub(r"\s+", " ", texto).strip()


def palabras_correctas(verdad, ocr):
    pv, po = verdad.lower().split(), ocr.lower().split()
    bloques = difflib.SequenceMatcher(None, pv, po, autojunk=False).get_matching_blocks()
    return sum(b.size for b in bloques), len(pv)


def leer(pagina):
    dpi = main._dpi_para_pagina(pagina)
    imagen = cv2.cvtColor(np.array(pagina.render(scale=dpi / 72).to_pil().convert("RGB")), cv2.COLOR_RGB2BGR)
    enderezada, angulo = main._enderezar(imagen)
    texto, confianza, _ = main._extraer_texto_y_confianza(enderezada)
    return texto, confianza, dpi, angulo


original = pdfium.PdfDocument(os.path.join(CARPETA, "BOE_CP_13pag_original.pdf"))
verdad = [normalizar(original[i].get_textpage().get_text_bounded()) for i in range(len(original))]

main._inicializar_worker("mobile")
main._motor.predict(np.full((800, 600, 3), 255, dtype=np.uint8))  # calentar, fuera de la medida

print(f"Configuración: DPI {main.OCR_DPI_MODO}, umbral de caja={main.OCR_DET_UMBRAL_CAJA}, desalabeo={main.OCR_DESALABEO}, "
      f"giro de renglones={main.OCR_ORIENTACION_RENGLON}, hilos={main.OCR_HILOS_POR_PROCESO}\n", flush=True)

resumen = []
for nivel in NIVELES:
    doc = pdfium.PdfDocument(os.path.join(CARPETA, f"BOE_CP_13pag_{nivel}.pdf"))
    filas, textos, t0 = [], [], time.perf_counter()
    for i in range(len(doc)):
        tp = time.perf_counter()
        texto, confianza, dpi, angulo = leer(doc[i])
        segundos = time.perf_counter() - tp
        ocr = normalizar(texto)
        fid = difflib.SequenceMatcher(None, verdad[i], ocr, autojunk=False).ratio()
        ok, total = palabras_correctas(verdad[i], ocr)
        filas.append((i + 1, fid, ok, total, confianza, dpi, angulo, segundos))
        textos.append(texto)
    total_seg = time.perf_counter() - t0

    print(f"== {nivel}", flush=True)
    print("  pág  caracteres  palabras   confianza  dpi  ángulo  s/pág")
    for p, fid, ok, total, conf, dpi, ang, seg in filas:
        alarma = "  <- seguro pero mal leído" if fid < FIDELIDAD_MALA and conf >= CONFIANZA_ALTA else ""
        print(f"  {p:3}  {fid * 100:9.1f}%  {ok / max(total, 1) * 100:7.1f}%   {conf:8.3f}  {dpi:3}  {ang:6.2f}  {seg:5.1f}{alarma}")

    ok_total = sum(f[2] for f in filas)
    pal_total = sum(f[3] for f in filas)
    todo_verdad = " ".join(verdad)
    todo_ocr = " ".join(normalizar(t) for t in textos)
    fid_doc = difflib.SequenceMatcher(None, todo_verdad, todo_ocr, autojunk=False).ratio()
    resumen.append((nivel, fid_doc, ok_total / pal_total, float(np.mean([f[4] for f in filas])),
                    min(f[4] for f in filas), total_seg / len(filas)))

    if os.path.isdir("/out"):
        # Páginas separadas por salto de página (\f) para poder compararlas
        # una a una después.
        with open(f"/out/curva_{nivel}_caja{main.OCR_DET_UMBRAL_CAJA}.txt", "w", encoding="utf-8") as f:
            f.write("\f".join(textos))

print("\nResumen (documento completo):")
print(f"  {'nivel':22} caracteres  palabras  conf.media  conf.mínima  s/pág")
for nivel, fid, pal, conf, conf_min, seg in resumen:
    print(f"  {nivel:22} {fid * 100:9.1f}%  {pal * 100:7.1f}%  {conf:10.3f}  {conf_min:11.3f}  {seg:5.1f}")
