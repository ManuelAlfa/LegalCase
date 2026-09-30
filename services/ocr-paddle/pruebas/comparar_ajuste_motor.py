"""Compara la configuración actual del motor con una variante que cambia UN
ajuste, midiendo fidelidad frente a la verdad de referencia y tiempo.

Generaliza comparar_desalabeo.py para no escribir un script por ajuste.

    docker run --rm --entrypoint python -v "$PWD/services/ocr-paddle":/src \\
        -v "$PWD/test-data":/data -v ocr_paddle_models:/root/.paddlex \\
        legal-case-management-ocr-paddle:latest \\
        /src/pruebas/comparar_ajuste_motor.py use_textline_orientation 10

Ajustes que admite: los parámetros booleanos del constructor de PaddleOCR
que usa main._inicializar_worker (use_textline_orientation,
use_doc_orientation_classify, use_doc_unwarping). La variante es el valor
contrario al de producción.

Documentos: fallo-suris (1).pdf rasterizado (limpio) y su versión degradada
fallo_suris_degradado_96dpi_real.pdf; la verdad es el texto digital del
primero.
"""
import difflib
import re
import sys
import time

import cv2
import numpy as np
import pypdfium2 as pdfium
from paddleocr import PaddleOCR

sys.path.insert(0, "/src")
import main  # noqa: E402

AJUSTE = sys.argv[1]
N = int(sys.argv[2]) if len(sys.argv) > 2 else 10

# Configuración de producción (la misma que _inicializar_worker).
PRODUCCION = {
    "use_textline_orientation": main.OCR_ORIENTACION_RENGLON,
    "use_doc_orientation_classify": True,
    "use_doc_unwarping": main.OCR_DESALABEO,
}
if AJUSTE not in PRODUCCION:
    sys.exit(f"Ajuste desconocido: {AJUSTE}. Admite: {', '.join(PRODUCCION)}")
VARIANTE = dict(PRODUCCION, **{AJUSTE: not PRODUCCION[AJUSTE]})

original = pdfium.PdfDocument("/data/fallo-suris (1).pdf")
degradado = pdfium.PdfDocument("/data/fallo_suris_degradado_96dpi_real.pdf")


def normalizar(t):
    t = re.sub(r"-\s*\n\s*", "", t.replace("\r", " "))
    return re.sub(r"\s+", " ", t).strip()


def preparar(doc, i):
    p = doc[i]
    dpi = main._dpi_para_pagina(p)
    img = cv2.cvtColor(np.array(p.render(scale=dpi / 72).to_pil().convert("RGB")), cv2.COLOR_RGB2BGR)
    return main._deskew_y_limpiar(img)


verdad = [normalizar(original[i].get_textpage().get_text_bounded()) for i in range(N)]
imagenes = {"limpio": [preparar(original, i) for i in range(N)],
            "degradado": [preparar(degradado, i) for i in range(N)]}

modelo_det, modelo_rec = main._VARIANTES["mobile"]
resultados = {}
for nombre, config in (("producción", PRODUCCION), ("variante", VARIANTE)):
    main._motor = PaddleOCR(text_detection_model_name=modelo_det, text_recognition_model_name=modelo_rec,
                            cpu_threads=main.OCR_HILOS_POR_PROCESO, **config)
    main._motor.predict(np.full((800, 600, 3), 255, dtype=np.uint8))
    for estado, imgs in imagenes.items():
        fid, t0 = [], time.perf_counter()
        for i, img in enumerate(imgs):
            texto = normalizar(main._extraer_texto_y_confianza(img)[0])
            fid.append(difflib.SequenceMatcher(None, verdad[i], texto, autojunk=False).ratio())
        segundos = (time.perf_counter() - t0) / len(imgs)
        resultados[(estado, nombre)] = fid
        print(f"{estado:9} {nombre:10} ({AJUSTE}={config[AJUSTE]}): fidelidad media "
              f"{np.mean(fid) * 100:.1f}%  ({segundos:.1f} s/pág)", flush=True)

print(f"\nPor página (fidelidad producción / variante):")
for estado in imagenes:
    a, b = resultados[(estado, "producción")], resultados[(estado, "variante")]
    print(f"  {estado:9}: " + "  ".join(f"{x * 100:.0f}/{y * 100:.0f}" for x, y in zip(a, b)))
    mejor_a = sum(x > y + 0.005 for x, y in zip(a, b))
    mejor_b = sum(y > x + 0.005 for x, y in zip(a, b))
    print(f"  {'':9}  mejor producción en {mejor_a}, mejor variante en {mejor_b}, iguales en {N - mejor_a - mejor_b}")
