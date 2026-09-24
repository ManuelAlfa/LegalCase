FROM python:3.11-slim

# poppler-utils: ya NO lo usa el pipeline de OCR en sí (main.py rasteriza
# con pypdfium2 desde 2026-09-03, ~6x más rápido que pdftoppm en pruebas
# reales) — se mantiene solo porque _gen_test_pdf.py (generador del
# documento de prueba, se ejecuta dentro de este contenedor) llama a
# `pdfunite` para fusionar páginas.
# libgl1 / libglib2.0-0: dependencias de runtime de OpenCV.
RUN apt-get update && apt-get install -y --no-install-recommends \
    poppler-utils \
    libgl1 \
    libglib2.0-0 \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt .
# --mount=type=cache: caché de descargas de pip persistida por BuildKit
# FUERA de las capas de la imagen — sobrevive aunque cambie este Dockerfile
# (invalidando la capa) o el build falle a mitad, así que un paquete ya
# descargado con éxito (paddlepaddle sola pesa ~193 MB) no se vuelve a
# descargar en el siguiente intento. Comprobado en pruebas reales
# (2026-08-25): sin esto, cada reintento tras un corte de red a mitad de
# descarga volvía a empezar desde cero. Va junto con quitar --no-cache-dir
# (que desactivaba esta misma caché de pip) — el tamaño final de la imagen
# no cambia, la caché vive fuera de las capas, no dentro.
# --timeout 300 + --retries 5: margen adicional para conexiones lentas o
# con cortes intermitentes (comprobado: el timeout de lectura por defecto
# de pip, unos 15-60s, se agotaba a mitad de la descarga de paddlepaddle).
RUN --mount=type=cache,target=/root/.cache/pip \
    pip install --timeout 300 --retries 5 -r requirements.txt

COPY main.py .

EXPOSE 8000

CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
