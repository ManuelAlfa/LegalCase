"""Fidelidad del texto reconocido frente a una verdad de referencia.

La verdad es el texto digital de un PDF ORIGINAL (no escaneado), del que se
fabricó el escaneo que se pasó por el OCR. Así se mide cuánto del texto real
se recupera, no la confianza que declara el propio modelo.

    docker run --rm --entrypoint python \\
        -v "$PWD/services/ocr-paddle":/src -v "$PWD/test-data":/data -v /tmp:/tmp \\
        legal-case-management-ocr-paddle:latest \\
        /src/pruebas/fidelidad.py "/data/fallo-suris (1).pdf" 0-2 /tmp/texto_ocr.txt

    Argumentos: PDF original, rango de páginas (desde 0), texto del OCR.

Imprime la similitud de caracteres, el porcentaje de palabras correctas y
las diferencias, para ver QUÉ se pierde además de cuánto.
"""
import difflib
import re
import sys

import pypdfium2 as pdfium


def normalizar(texto):
    texto = texto.replace("\r", " ")
    texto = re.sub(r"-\s*\n\s*", "", texto)  # palabras partidas al final de línea
    return re.sub(r"\s+", " ", texto).strip()


ruta_original, rango, ruta_ocr = sys.argv[1], sys.argv[2], sys.argv[3]
desde, _, hasta = rango.partition("-")
paginas = range(int(desde), int(hasta or desde) + 1)

original = pdfium.PdfDocument(ruta_original)
verdad = normalizar("\n".join(original[i].get_textpage().get_text_bounded() for i in paginas))
ocr = normalizar(open(ruta_ocr, encoding="utf-8").read())

similitud = difflib.SequenceMatcher(None, verdad, ocr, autojunk=False).ratio()
pv, po = verdad.lower().split(), ocr.lower().split()
palabras = difflib.SequenceMatcher(None, pv, po, autojunk=False)
correctas = sum(b.size for b in palabras.get_matching_blocks())

print(f"fidelidad caracteres: {similitud * 100:.1f}%   palabras: {correctas}/{len(pv)} = {correctas / len(pv) * 100:.1f}%")
print("diferencias:")
for tipo, i1, i2, j1, j2 in palabras.get_opcodes():
    if tipo != "equal":
        print(f"  {tipo:7} verdad={' '.join(pv[i1:i2])[:55]!r:60} ocr={' '.join(po[j1:j2])[:45]!r}")
