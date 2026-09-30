"""Verifica la capa de texto invisible de un PDF foliado.

Tres comprobaciones, las tres objetivas:

1. POSICIÓN: para cada renglón que dio el OCR, el texto seleccionable que
   hay dentro de su caja en el PDF debe ser ese renglón. Es lo que decide si
   al seleccionar en Acrobat se copia lo que se ve.
2. INVISIBILIDAD: la página foliada renderizada debe ser idéntica a la
   original salvo el sello de folio. Si el texto se pintara (el fallo crítico
   del 2026-09-05 con PDF de versión 1.3), aparecerían píxeles distintos.
3. MODO INVISIBLE: en el flujo de contenido de la capa oculta, cada bloque
   de texto ("BT") debe llevar el modo de renderizado 3 ("3 Tr").

    docker run --rm --entrypoint python -v "$PWD/services/ocr-paddle":/src -v <dir>:/c \\
        legal-case-management-ocr-paddle:latest /src/pruebas/verificar_capa_texto.py \\
        /c/original.pdf /c/foliado.pdf /c/ocr.json

`ocr.json` es la respuesta del servicio de OCR para ese documento
({"paginas": [{"pagina": 1, "lineas": [{"texto", "x0", "y0", "x1", "y1"}]}]}).
Sale con código 1 si alguna comprobación falla.
"""
import difflib
import json
import re
import sys
import zlib

import numpy as np
import pypdfium2 as pdfium

ruta_original, ruta_foliado, ruta_ocr = sys.argv[1:4]
original = pdfium.PdfDocument(ruta_original)
foliado = pdfium.PdfDocument(ruta_foliado)
paginas_ocr = {p["pagina"]: p for p in json.load(open(ruta_ocr, encoding="utf-8"))["paginas"]}
fallos = []


def norm(t):
    return re.sub(r"\s+", " ", t).strip().lower()


# ---------------------------------------------------------------------------
print("1. Posición de cada renglón")
# Dos medidas distintas, porque las cajas del detector se solapan en
# vertical (añaden margen alrededor del renglón, y en texto apretado la de un
# renglón empieza antes de que acabe la del anterior):
#  - CENTRO: lo que hay en la franja central de la caja (el 40% de su altura).
#    Es lo que obtiene quien pincha o arrastra sobre el renglón que ve. Buscar
#    en la caja entera mezclaría el final del renglón vecino en la zona de
#    solape, y penalizaría una colocación que es correcta.
#  - ENTERO: el texto del renglón está completo dentro de su caja.
centro_ok, entero_ok, completos, total = 0, 0, 0, 0
for n in range(len(foliado)):
    pagina = foliado[n]
    ancho, alto = pagina.get_size()
    textpage = pagina.get_textpage()
    texto_visible = norm(textpage.get_text_bounded(0, 0, ancho, alto))
    for linea in paginas_ocr.get(n + 1, {}).get("lineas", []):
        esperado = norm(linea["texto"])
        izq, der = linea["x0"] * ancho, linea["x1"] * ancho
        arriba, abajo = alto - linea["y0"] * alto, alto - linea["y1"] * alto
        mitad, franja = (arriba + abajo) / 2, 0.2 * (arriba - abajo)
        en_centro = norm(textpage.get_text_bounded(izq, mitad - franja, der, mitad + franja))
        en_caja = norm(textpage.get_text_bounded(izq, abajo, der, arriba))
        centro_ok += difflib.SequenceMatcher(None, esperado, en_centro).ratio() >= 0.9
        entero_ok += esperado in en_caja
        completos += esperado in texto_visible
        total += 1
if total:
    print(f"   {total} renglones")
    print(f"   seleccionando en su franja central se obtiene ese renglón: {centro_ok} ({centro_ok / total * 100:.0f}%)")
    print(f"   su texto está entero dentro de su caja:                    {entero_ok} ({entero_ok / total * 100:.0f}%)")
    print(f"   completos dentro de la página (no se salen):               {completos} ({completos / total * 100:.0f}%)")
    if centro_ok / total < 0.9 or entero_ok / total < 0.95:
        fallos.append("posición")
    if completos < total:
        fallos.append("renglones cortados")

# ---------------------------------------------------------------------------
print("2. Invisibilidad (página foliada frente a la original)")
for n in range(len(foliado)):
    a = np.asarray(original[n].render(scale=100 / 72, grayscale=True).to_pil(), dtype=np.int16)
    b = np.asarray(foliado[n].render(scale=100 / 72, grayscale=True).to_pil(), dtype=np.int16)
    distinto = np.abs(a - b) > 40
    # Fuera de la zona del sello de folio (esquina inferior derecha).
    alto_px, ancho_px = distinto.shape
    distinto[int(alto_px * 0.9):, int(ancho_px * 0.6):] = False
    pixeles = int(distinto.sum())
    print(f"   página {n + 1}: {pixeles} píxeles distintos fuera del sello")
    if pixeles > 50:
        fallos.append(f"texto visible en página {n + 1}")

# ---------------------------------------------------------------------------
print("3. Modo invisible en los bloques de texto")
datos = open(ruta_foliado, "rb").read()
capas = 0
for m in re.finditer(rb"stream\r?\n", datos):
    fin = datos.find(b"endstream", m.end())
    crudo = datos[m.end():fin]
    try:
        # Tolerante con los bytes de fin de línea que quedan antes de
        # "endstream": zlib.decompress a secas falla con ellos.
        contenido = zlib.decompressobj().decompress(crudo)
    except zlib.error:
        continue  # imágenes y otros flujos que no son de contenido
    # Solo los flujos de contenido con texto dibujado de verdad: un "BT"
    # suelto puede aparecer por azar en los bytes de una imagen.
    if b" Tf" not in contenido or b"Tj" not in contenido:
        continue
    bloques = len(re.findall(rb"(?m)^BT\s*$", contenido))
    invisibles = len(re.findall(rb"(?m)^BT\s*\n3 Tr\s*$", contenido))
    if invisibles == 0 and bloques <= 1:
        continue  # el sello de folio: un bloque, visible a propósito
    capas += 1
    print(f"   capa oculta: {bloques} bloques de texto, {invisibles} con 3 Tr, "
          f"{contenido.count(b'Tj')} renglones dibujados")
    if invisibles != bloques:
        fallos.append("bloques sin modo invisible")
if capas == 0:
    # Silencio no es éxito: si no se encuentra ninguna capa, la comprobación
    # no ha verificado nada.
    print("   NO se encontró ninguna capa de texto oculta")
    fallos.append("capa oculta no encontrada")

print()
if fallos:
    print("FALLA:", ", ".join(dict.fromkeys(fallos)))
    sys.exit(1)
print("Capa de texto correcta.")
