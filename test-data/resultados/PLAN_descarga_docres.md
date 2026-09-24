# Plan: conseguir DocRes funcionando (restauración de imagen para OCR)

Contexto: DocRes ([ZZZHANG-jx/DocRes](https://github.com/ZZZHANG-jx/DocRes),
MIT, libre y comercial) une 5 tareas de restauración de documento
(desenfoque, sombras, dewarping, binarización, realce) en un solo modelo.
Necesita PyTorch, que no se ha podido instalar dentro de WSL2 por un
problema de red conocido (ver `pendiente_super_resolucion_ocr_baja_calidad.md`
en memoria). Plan: descargar todo desde una red externa (p.ej. el trabajo)
y traerlo, siguiendo el método ya validado con TensorFlow
(`METODO_descarga_externa_wsl2.md`).

**Aviso real de riesgo, no descubierto todavía**: DocRes se escribió para
`torch==1.11.0` (2022). Aquí se propone instalar `torch 2.9.1` (la
versión actual con wheel CPU para Python 3.11) porque la versión antigua
ya ni siquiera tiene wheel disponible para Python 3.11. Es un salto
grande (7 versiones mayores) — hay riesgo real de que el código de
DocRes use alguna función de PyTorch ya eliminada o con comportamiento
distinto en la versión moderna. No se sabrá con certeza hasta probarlo.
Si falla por esto, la alternativa ya identificada y más sencilla es
`realesrgan-ncnn-vulkan` (binario precompilado, sin PyTorch en absoluto,
descarga directa de ~47MB desde GitHub Releases, sin este problema).

## 1. Descargar los paquetes Python (en la red externa)

```bash
mkdir -p docres-wheels && cd docres-wheels

# PyTorch CPU-only (el pin original de DocRes es una versión con CUDA de
# 2022 que ya no tiene wheel para Python 3.11 — se usa la CPU actual)
pip download --timeout 300 --retries 5 \
  --only-binary=:all: --platform manylinux_2_28_x86_64 --python-version 311 \
  --implementation cp --abi cp311 \
  --index-url https://download.pytorch.org/whl/cpu \
  torch torchvision \
  -d .

# Resto de dependencias de DocRes (numpy sin fijar a la versión vieja
# 1.21.6 del requirements.txt original, que no tiene wheel para 3.11)
pip download --timeout 300 --retries 5 \
  --only-binary=:all: --platform manylinux2014_x86_64 --python-version 311 \
  --implementation cp --abi cp311 \
  numpy opencv-python-headless scikit-image scikit-learn scipy pandas \
  matplotlib pillow timm \
  -d .

# Paquetes puro-Python (sin problema de plataforma)
pip download --timeout 300 --retries 5 \
  einops tqdm omegaconf six loguru albumentations imageio lpips \
  -d .
```

Comprimir `docres-wheels/` y traerla (USB, nube personal, lo que resulte
más cómodo).

## 2. Descargar el código y los pesos del modelo

El código en sí es pequeño (clonar o descargar zip de
[github.com/ZZZHANG-jx/DocRes](https://github.com/ZZZHANG-jx/DocRes) —
sin problema, GitHub funciona bien).

**Los pesos SOLO están en Microsoft OneDrive** (no hay alternativa en
GitHub/Hugging Face, confirmado 2026-09-16):
[enlace OneDrive](https://1drv.ms/f/s!Ak15mSdV3Wy4iahoKckhDPVP5e2Czw?e=iClwdK)
— descarga manual desde el navegador (no es fácilmente scriptable), dos
carpetas a bajar:

- Modelo **MBD** → colocar en `./data/MBD/checkpoint/`
- Modelo **DocRes** → colocar en `./checkpoints/`

Traer también estos ficheros junto con los wheels.

## 3. Instalar aquí, sin tocar la red de WSL2

```bash
pip install --no-index --find-links=/ruta/a/docres-wheels \
  torch torchvision numpy opencv-python-headless scikit-image \
  scikit-learn scipy pandas matplotlib pillow timm \
  einops tqdm omegaconf six loguru albumentations imageio lpips
```

Copiar el código de DocRes y los pesos ya descargados a sus rutas
correspondientes, y probar la inferencia contra nuestros documentos de
prueba ya degradados (`test-data/fallo_suris_degradado_96dpi_real.pdf`,
o las imágenes ya generadas con Augraphy en `test-data/resultados/`).

## Si algo de esto falla (API de PyTorch incompatible, etc.)

Plan B ya identificado y validado en cuanto a disponibilidad: instalar
`realesrgan-ncnn-vulkan` (binario Linux precompilado, ~47MB, GitHub
Releases de xinntao/Real-ESRGAN) — sin PyTorch, sin pip, mismo modelo
Real-ESRGAN. Más simple de integrar como paso, aunque hay que llamarlo
como proceso externo en vez de librería Python.
