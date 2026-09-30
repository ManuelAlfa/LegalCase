"""¿Mejora la lectura el desalabeo (UVDoc) de PaddleOCR en documentos
escaneados, o solo cuesta tiempo?

Fidelidad página a página frente a la verdad de referencia, con y sin
desalabeo, sobre el mismo documento en dos estados:
  - limpio:     fallo-suris (1).pdf rasterizado (escaneo perfecto);
  - degradado:  fallo_suris_degradado_96dpi_real.pdf (el mismo documento,
                degradado a 96 DPI con ruido y desenfoque);
y la verdad es el texto digital de fallo-suris (1).pdf.

Mismo camino que producción en todo lo demás (resolución adaptativa,
enderezado, extracción con ordenación y separación del margen).

    docker run --rm --entrypoint python -v "$PWD/services/ocr-paddle":/src \\
        -v "$PWD/test-data":/data -v ocr_paddle_models:/root/.paddlex \\
        legal-case-management-ocr-paddle:latest /src/pruebas/comparar_desalabeo.py 10
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

N = int(sys.argv[1]) if len(sys.argv) > 1 else 10
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
for desalabeo in (True, False):
    main._motor = PaddleOCR(use_textline_orientation=True, use_doc_orientation_classify=True,
                            use_doc_unwarping=desalabeo, text_detection_model_name=modelo_det,
                            text_recognition_model_name=modelo_rec, cpu_threads=main.OCR_HILOS_POR_PROCESO)
    main._motor.predict(np.full((800, 600, 3), 255, dtype=np.uint8))
    for estado, imgs in imagenes.items():
        fid, t0 = [], time.perf_counter()
        for i, img in enumerate(imgs):
            texto = normalizar(main._extraer_texto_y_confianza(img)[0])
            fid.append(difflib.SequenceMatcher(None, verdad[i], texto, autojunk=False).ratio())
        resultados[(estado, desalabeo)] = (fid, (time.perf_counter() - t0) / len(imgs))
        print(f"{estado:9} desalabeo={'sí' if desalabeo else 'no'}: fidelidad media "
              f"{np.mean(fid) * 100:.1f}%  ({(time.perf_counter() - t0) / len(imgs):.1f} s/pág)", flush=True)

print("\nPor página (fidelidad con / sin desalabeo):")
for estado in imagenes:
    con, sin = resultados[(estado, True)][0], resultados[(estado, False)][0]
    mejor_con = sum(c > s + 0.005 for c, s in zip(con, sin))
    mejor_sin = sum(s > c + 0.005 for c, s in zip(con, sin))
    print(f"  {estado:9}: " + "  ".join(f"{c * 100:.0f}/{s * 100:.0f}" for c, s in zip(con, sin)))
    print(f"  {'':9}  mejor CON en {mejor_con} páginas, mejor SIN en {mejor_sin}, iguales en {N - mejor_con - mejor_sin}")
