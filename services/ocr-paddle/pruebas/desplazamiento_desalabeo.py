"""¿Cuánto mueve el desalabeo de PaddleOCR la posición de los renglones?

Las coordenadas de las cajas que devuelve el motor son de la imagen YA
desalabeada, no de la página original. Para colocar la capa de texto del PDF
encima de cada renglón hace falta saber si coinciden. Se pasa la misma
página con y sin desalabeo, se emparejan los renglones por su texto y se mide
cuánto se ha desplazado cada uno, en píxeles y en alturas de renglón.

    docker run --rm --entrypoint python -v "$PWD/services/ocr-paddle":/src \\
        -v "$PWD/test-data":/data -v ocr_paddle_models:/root/.paddlex \\
        legal-case-management-ocr-paddle:latest /src/pruebas/desplazamiento_desalabeo.py \\
        /data/muestra_escaneo_limpio_3p.pdf 0
"""
import sys

import cv2
import numpy as np
import pypdfium2 as pdfium
from paddleocr import PaddleOCR

sys.path.insert(0, "/src")
import main  # noqa: E402

ruta, pagina_n = sys.argv[1], int(sys.argv[2])
pagina = pdfium.PdfDocument(ruta)[pagina_n]
dpi = main._dpi_para_pagina(pagina)
img = main._deskew_y_limpiar(
    cv2.cvtColor(np.array(pagina.render(scale=dpi / 72).to_pil().convert("RGB")), cv2.COLOR_RGB2BGR))

modelo_det, modelo_rec = main._VARIANTES["mobile"]
cajas = {}
for desalabeo in (False, True):
    motor = PaddleOCR(use_textline_orientation=True, use_doc_orientation_classify=False,
                      use_doc_unwarping=desalabeo, text_detection_model_name=modelo_det,
                      text_recognition_model_name=modelo_rec, cpu_threads=1)
    r = list(motor.predict(img))[0]
    cajas[desalabeo] = {t: p for t, p in zip(r["rec_texts"], r["rec_polys"]) if len(t.strip()) >= 12}

comunes = sorted(set(cajas[False]) & set(cajas[True]), key=lambda t: cajas[False][t][:, 1].min())
altura = float(np.median([p[:, 1].max() - p[:, 1].min() for p in cajas[False].values()]))
desplazamientos = []
alto_img, ancho_img = img.shape[:2]
print(f"{ruta} pág {pagina_n + 1} @ {dpi} DPI, {ancho_img}x{alto_img}px, altura de renglón {altura:.0f}px")
print(f"{'posición vertical':>18} {'dx':>6} {'dy':>6}  renglón")
for t in comunes:
    a, b = cajas[False][t], cajas[True][t]
    dx = float(b[:, 0].mean() - a[:, 0].mean())
    dy = float(b[:, 1].mean() - a[:, 1].mean())
    desplazamientos.append(np.hypot(dx, dy))
    print(f"{a[:, 1].mean() / alto_img * 100:>17.0f}% {dx:>6.1f} {dy:>6.1f}  {t[:45]}")

d = np.array(desplazamientos)
print(f"\n{len(d)} renglones emparejados: desplazamiento mediano {np.median(d):.1f}px, "
      f"máximo {d.max():.1f}px = {d.max() / altura:.2f} alturas de renglón")
