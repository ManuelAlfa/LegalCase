# ocr-paddle

Microservicio Python (FastAPI) que expone `POST /ocr`: recibe un PDF o
imagen y devuelve el texto reconocido, por página, en JSON.

Usa **PaddleOCR** con el modelo **PP-OCRv5** en vez de Tesseract: su
precisión en documentos jurídicos reales (escaneos de juzgados, fotocopias
de fotocopias, anotaciones a mano — PP-OCRv5 reconoce manuscrito) es
notablemente mejor. **OpenCV** endereza páginas torcidas y reduce ruido
antes de pasarlas al modelo, lo que mejora la precisión sobre escaneos de
mala calidad.

## Por qué no Tesseract

Se descarta explícitamente como opción por defecto: su precisión cae mucho
en las condiciones reales de este dominio (papel fotocopiado varias veces,
inclinación al escanear a mano, anotaciones manuscritas).

## Licencias (uso comercial/SaaS)

| Componente | Licencia | Nota |
|---|---|---|
| PaddleOCR / PP-OCRv5 | Apache 2.0 | Segura para uso comercial/SaaS: solo exige conservar el aviso de licencia, sin cláusula de red ni obligación de liberar código. |
| PaddlePaddle | Apache 2.0 | Framework del que depende PaddleOCR. |
| OpenCV (`opencv-python-headless`) | Apache 2.0 | |
| FastAPI | MIT | |
| `pypdfium2` | Apache-2.0 / BSD-3-Clause | Bindings del motor PDFium (Google/Chromium) usados para rasterizar PDF→PNG. Sustituye a `pdf2image`/`poppler-utils` desde 2026-09-03: ~6x más rápido en pruebas reales (0.124s/página vs 0.735s/página sobre páginas reales de la Gaceta de Madrid a 300 DPI), sin GPL de por medio. |
| `poppler-utils` (binario `pdfunite`, instalado vía `apt`) | GPLv2 | Ya no forma parte del pipeline de OCR — se mantiene solo porque `_gen_test_pdf.py` (script de generación de datos de prueba, no se distribuye) lo invoca como **subproceso externo** para fusionar páginas. |

Deliberadamente **no** se usa PyMuPDF/`fitz` para rasterizar PDFs pese a ser
más simple de instalar: su licencia es AGPL-3.0 (o comercial de pago), y el
requisito explícito de este proyecto es evitar cualquier cláusula de red /
obligación de liberar código en las dependencias de este microservicio.

## Endpoint

```
POST /ocr
Content-Type: multipart/form-data
  file: <PDF o imagen>

200 OK
{
  "paginas": [
    { "pagina": 1, "texto": "..." },
    { "pagina": 2, "texto": "..." }
  ]
}
```

`GET /health` para comprobación de vida del contenedor.
