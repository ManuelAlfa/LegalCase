# Documentos de prueba para el pipeline de OCR

## `resultados/prueba1_500_paginas_ppocrv6_hibrido_2026-09-01.pdf`

Repetición de la Prueba 1 con el motor híbrido PP-OCRv6 small/medium +
disparador confianza+CJK (IA.2c, ver memoria de proyecto
`ocr_calidad_ppocrv6_gaceta`). Mismo documento fuente
(`prueba_500_paginas.pdf`), mismas 32 páginas degradadas. Completado con
éxito el 2026-09-01, 1h55min28s totales (vs 2h1min38s de la Prueba 1
original con PP-OCRv5 — algo más rápido, con calidad muy superior).

Hallazgo relevante: las 32 páginas degradadas, que con PP-OCRv5 (mobile y
server) daban confianza 0.000 (0 caracteres, texto vacío), con este motor
dan confianza 0.66-0.75 y **texto real recuperado** (1000-1500 caracteres
por página, el mismo texto de plantilla legal que el resto del documento,
solo que degradado por el ruido sintético) — no eran ilegibles para
cualquier modelo como se pensaba, PP-OCRv5 simplemente no podía leerlas.
Verificado también que la capa de texto seleccionable (`3 Tr`) sigue
presente correctamente a esta escala (500 páginas).

## `resultados/prueba1_500_paginas_completado_2026-08-26.pdf`

PDF resultante de la Prueba 1 completada con éxito el 2026-08-26 (ver
`DESPLIEGUE.md` §8 y memoria de proyecto `ocr_dual_variant_performance_status`
para el detalle completo). Descargado de SeaweedFS tras el proceso, para
poder inspeccionarlo directamente (sello de folio 16-515, 500 páginas
verificadas) sin depender de que el objeto siga existiendo en el almacén de
objetos.

## `prueba_500_paginas.pdf` (prueba 1 — sintético, calibrado)

Generado con `services/ocr-paddle/_gen_test_pdf.py` (script de un solo uso,
no forma parte del producto — no está en el repo, solo el PDF resultante).
Semilla fija (`random.seed(20260812)`), así que es reproducible si hace
falta regenerarlo.

- 500 páginas, A4 real a 300 DPI (2481x3508 px).
- 32 páginas deliberadamente degradadas (desenfoque gaussiano k=15 sigma=5,
  ruido gaussiano std=35, bajo contraste alpha=0.38 beta=95 — calibrado
  empíricamente contra el generador real de páginas para que la confianza
  media de la variante mobile caiga a ~0.47-0.50, con margen claro por
  debajo del umbral por defecto de 0.80; con una degradación más suave la
  confianza se quedaba en ~0.97 y nunca disparaba el reprocesado), en
  bloques de 4, para simular manchas reales de escaneo por lotes:

  28, 29, 30, 31, 35, 36, 37, 38, 157, 158, 159, 160, 314, 315, 316, 317,
  327, 328, 329, 330, 388, 389, 390, 391, 439, 440, 441, 442, 458, 459,
  460, 461

## `documento_real_gaceta_madrid.pdf` (prueba 2 — documento público real)

Tomo encuadernado real de la *Gaceta de Madrid* (precursora histórica del
BOE), números no consecutivos de 1804, 1805, 1817, 1819 y 1820, editado por
la Imprenta Real. Escaneado real de papel de época (no born-digital, así
que sí exhibe defectos reales de escaneo: inclinación, manchas, tipografía
antigua) — de la colección de José Cecilio del Valle, Universidad Francisco
Marroquín, en dominio público. 930 páginas, 64 MB.

Fuente: https://archive.org/details/gacetademadrid00unguat
(descarga directa: https://archive.org/download/gacetademadrid00unguat/gacetademadrid00unguat.pdf)

Nota: 930 páginas es casi el doble del objetivo de ~500 de la prueba 1; se
usa igualmente porque es el documento real, público y voluminoso disponible
más cercano encontrado — la consigna original permite esto explícitamente
("legajo público real y voluminoso... por ejemplo una causa judicial
publicada") y pide documentar el tiempo real obtenido, no forzar 500
exactas. Se intentó primero encontrar una causa judicial española real
(caso Gürtel, sentencia AN 2018, 1.687 páginas) pero no se localizó un
enlace de descarga directa estable (el de prensa que circulaba dio 404, y
el buscador de CENDOJ es una interfaz JS no accesible por fetch directo).
