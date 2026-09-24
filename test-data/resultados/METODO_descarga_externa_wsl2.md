# Método: descargar/instalar paquetes pesados fuera de WSL2 y traerlos aquí

Contexto: WSL2 tiene un problema de red conocido y documentado (no es un
fallo de vuestra conexión en sí) que corta descargas grandes y sostenidas
(cientos de MB, como PyTorch o TensorFlow) a media transferencia, aunque
los ficheros pequeños vayan bien. Ver `pendiente_super_resolucion_ocr_baja_calidad.md`
en la memoria del proyecto para el diagnóstico completo con fuentes.

Este documento explica cómo bajar cualquier paquete pesado desde una red
sin ese problema (p.ej. la del trabajo) y traerlo para instalarlo aquí
sin volver a tocar la red de WSL2 en absoluto.

## 1. En la red externa (con pip instalado — no hace falta Python 3.11 exacto)

`pip download` puede pedir la plataforma exacta que necesitamos aunque el
Python local sea distinto, usando `--platform`/`--python-version`/`--abi`.
Este proyecto usa **Python 3.11, Linux x86_64** (confirmado: mismo que el
contenedor `ocr-paddle`).

```bash
mkdir paquetes && cd paquetes

# Paquetes con partes compiladas (necesitan la plataforma exacta):
pip download --no-deps --only-binary=:all: \
  --platform manylinux2014_x86_64 --python-version 311 --implementation cp --abi cp311 \
  <nombre-paquete>==<version>

# Si el paquete tiene variante CPU-only más ligera (torch, tensorflow-cpu),
# usarla siempre que sea posible — mucho menos peso que la versión con CUDA:
pip download --no-deps --index-url https://download.pytorch.org/whl/cpu torch torchvision
pip download --no-deps tensorflow-cpu   # tensorflow-cpu ya tiene wheel manylinux estándar en PyPI

# Paquetes puro-Python (sin compilar, sin problema de plataforma):
pip download --no-deps <nombre-paquete>
```

Comprimir la carpeta `paquetes/` y traerla como sea más cómodo (USB, nube
personal, etc. — atención a la política de la red de origen sobre tráfico
saliente si aplica).

## 2. Aquí, instalación 100% sin red

```bash
pip install --no-index --find-links=/ruta/a/paquetes <nombre-paquete> [<otro-paquete> ...]
```

`--no-index` hace que pip NUNCA intente contactar con PyPI — si falta algo
en la carpeta, falla con un error claro en vez de intentar descargarlo
desde WSL2.

## 3. Para modelos de Hugging Face (no son paquetes pip, son ficheros de pesos)

Los modelos de Hugging Face (como `SBB/sbb_binarization`) se bajan aparte,
con `huggingface_hub`, y SÍ han funcionado bien desde WSL2 hasta ahora
(su CDN parece más tolerante con este problema que `files.pythonhosted.org`
o `download.pytorch.org`) — de momento no ha hecho falta aplicar este
método para ellos, solo para los paquetes pip pesados (PyTorch,
TensorFlow). Si en el futuro también dieran problemas:

```bash
# En la red externa:
python3 -c "from huggingface_hub import snapshot_download; print(snapshot_download('ORG/modelo'))"
# Copiar la carpeta resultante (normalmente en ~/.cache/huggingface/hub/)

# Aquí, apuntando HF_HOME a la carpeta traída:
export HF_HOME=/ruta/a/la/carpeta/traida
python3 -c "from huggingface_hub import snapshot_download; print(snapshot_download('ORG/modelo'))"  # la encuentra en caché, no descarga nada
```

## Nota práctica ya confirmada esta sesión (2026-09-08)

La descarga SÍ funciona a veces desde WSL2 directamente (no es un bloqueo
del 100%, es intermitencia) — `tensorflow-cpu` (273.8MB) se bajó bien al
segundo intento en 1m36s a 3.3MB/s, después de fallar en el primero. Por
eso, antes de recurrir a este método externo, vale la pena reintentar 2-3
veces con `--timeout 300 --retries 8` y una caché de pip persistente
(`--cache-dir` apuntando a una carpeta fuera del contenedor `--rm`, para
no perder lo ya descargado entre reintentos). Solo si eso falla varias
veces seguidas (como pasó con PyTorch/`basicsr`, 7 intentos fallidos en
total) merece la pena el método externo de este documento.

## Confirmado 2026-09-08 (noche): NO es un problema específico de PyTorch

Se repitió el mismo patrón con TensorFlow (para probar
`SBB/sbb_binarization`, ver `herramientas_degradacion_realista_ocr.md`
en memoria): el wheel principal de `tensorflow-cpu` (273.8MB) se
descargó bien de forma aislada, pero al instalarlo junto con sus
dependencias transitivas (`grpcio`, `keras`, `h5py`, `ml_dtypes`...) el
mismo `ReadTimeoutError` volvió a aparecer 3 veces seguidas, incluso con
`--timeout 400 --retries 15`. Confirma que el límite de WSL2 es general
para cualquier cadena de dependencias con paquetes grandes, no algo
propio de PyTorch/`basicsr`. El método de este documento (descargar
fuera, traer los ficheros, instalar con `--no-index`) es la solución de
fondo para esta clase de problema en general, no un parche puntual.

**Progreso ya guardado, fuera del scratchpad de la sesión (que no
sobrevive entre sesiones)**, en `~/.cache/legalcase-dev-wheels/`:
- `wheelcache/tensorflow_cpu-2.21.0-cp311-cp311-manylinux_2_27_x86_64.whl`
  (262MB) — reutilizar con
  `pip install --find-links=~/.cache/legalcase-dev-wheels/wheelcache tensorflow-cpu`.
- `hf-cache/` (149MB) — el modelo `SBB/sbb_binarization` completo,
  reutilizar con `HF_HOME=~/.cache/legalcase-dev-wheels/hf-cache`.

**Pendiente de validar mañana**: en vez de instalar `tensorflow-cpu`
solo (que arrastra la resolución de dependencias dentro de WSL2), usar
`pip download` (SIN `--no-deps`, para que resuelva la cadena completa de
una vez) desde una red externa, traer TODOS los wheels resultantes, e
instalar aquí con `--no-index --find-links=...` apuntando a esa carpeta
completa — así ninguna dependencia transitiva necesita tocar la red de
WSL2 en ningún momento. Aplicar el mismo flujo, una vez validado, a
Real-ESRGAN/`basicsr` (bloqueado en
`pendiente_super_resolucion_ocr_baja_calidad.md`).
