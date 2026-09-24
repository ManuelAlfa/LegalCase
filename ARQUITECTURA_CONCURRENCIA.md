# Núcleos, workers y concurrencia — guía de referencia

Este documento explica los distintos conceptos de "núcleos", "workers" y
"procesos/hilos paralelos" que aparecen en el proyecto, y dónde exactamente
vive cada uno en el código. Es una guía de referencia técnica, no un plan
de dimensionado — para eso (cuántos núcleos/memoria dar en un host nuevo)
ver `DESPLIEGUE.md`, que ya lo cubre en detalle y no se repite aquí.

Hay **dos capas completamente distintas** que usan la palabra "worker" con
significados diferentes. Confundirlas es la fuente más habitual de
malentendidos:

1. **El microservicio de OCR** (`services/ocr-paddle/`, Python) — aquí
   "worker" significa un **proceso de sistema operativo real**, con su
   propia porción de CPU. Es la única capa donde "más núcleos" se traduce
   directamente en "más rápido".
2. **El Worker de .NET** (`src/Worker/`) — aquí "worker" es el nombre del
   propio proyecto/servicio (el consumidor de mensajes de RabbitMQ), no
   tiene relación con núcleos de CPU. Su configuración relevante es de
   **concurrencia de mensajes**, no de paralelismo de cómputo.

---

## 1. El microservicio de OCR — paralelismo real por núcleos

### 1.1 Cuántos núcleos hay disponibles: `_cpus_disponibles()`

**Fichero**: `services/ocr-paddle/main.py:45-75`

Antes de decidir cuántos procesos worker lanzar, el servicio necesita
saber cuántas CPUs tiene realmente asignadas. No usa `os.cpu_count()` (que
devolvería las CPUs del **host completo**, ignorando el límite del propio
contenedor) — en su lugar, lee directamente el límite de cgroup:

```python
def _cpus_disponibles() -> int:
    with open("/sys/fs/cgroup/cpu.max") as f:
        cuota, periodo = f.read().split()
    ...
    return max(1, int(cuota) // int(periodo))
```

Este límite de cgroup es exactamente el mismo que fija `cpus:` en
`docker-compose.yml:224` (`cpus: "5"`) — cambiar ese valor es la única
palanca manual que hay que tocar; todo lo demás se ajusta solo a partir de
ahí.

### 1.2 Cuántos procesos worker se lanzan: `OCR_PROCESOS` / `OCR_PROCESOS_SERVER`

**Fichero**: `services/ocr-paddle/main.py:78-100`

Hay **dos pools de procesos independientes**, uno por variante de motor de
OCR:

- `OCR_PROCESOS` (línea 83): procesos para la variante **"mobile"** (el
  motor rápido, PP-OCRv6 tier `small`, procesa todas las páginas por
  defecto). Se autocalcula como `CPUs disponibles − OCR_PROCESOS_SERVER`
  — no hay que fijarlo a mano salvo para forzar un valor concreto.
- `OCR_PROCESOS_SERVER` (línea 100, por defecto `"1"`): procesos
  dedicados para la variante **"server"** (el motor más pesado, PP-OCRv6
  tier `medium`, solo reprocesa las páginas de confianza baja o con
  alucinación CJK detectada — ver `_contiene_cjk`).

Ambos se configuran como variables de entorno en
`docker-compose.yml:122-128`.

### 1.3 Dónde se crean los procesos de verdad: `ProcessPoolExecutor`

**Fichero**: `services/ocr-paddle/main.py:487-514`

```python
def _crear_executor(variante: str) -> ProcessPoolExecutor:
    num_procesos = OCR_PROCESOS if variante == "mobile" else OCR_PROCESOS_SERVER
    return ProcessPoolExecutor(
        max_workers=num_procesos,
        mp_context=_contexto_mp,        # "spawn", no "fork" — ver comentario en el código
        initializer=_inicializar_worker,
        initargs=(variante,),
    )
```

Estos executors (uno por variante, guardados en el diccionario
`_executores`, línea 427) se crean **una sola vez**, al arrancar el
proceso FastAPI (función `lifespan`, línea 501), y viven mientras dure el
servicio — no se recrean por cada petición HTTP (recargar los modelos de
PaddleOCR es lento).

### 1.4 Hilos DENTRO de cada proceso: `OCR_HILOS_POR_PROCESO`

**Fichero**: `services/ocr-paddle/main.py:145-160, 244-268`

Esto es un nivel más de detalle, y una fuente histórica de bugs de
sobresuscripción de CPU (documentado en el propio código). Cada proceso
worker, además de ser "un proceso", internamente puede usar varios hilos
para su propio cómputo numérico. Hay **dos mecanismos distintos** que hay
que fijar por separado, porque uno no controla al otro:

- `cv2.setNumThreads(OCR_HILOS_POR_PROCESO)` (línea 245) — hilos internos
  de OpenCV (usado en el preprocesado de imagen).
- `cpu_threads=OCR_HILOS_POR_PROCESO` pasado al constructor de
  `PaddleOCR(...)` (línea 264) — el propio parámetro de PaddleX, que por
  defecto usa 8 hilos **e ignora las variables de entorno estándar**
  (`OMP_NUM_THREADS` etc.) si no se le pasa explícitamente.

Además, las propias librerías de álgebra lineal (BLAS/OpenMP/MKL) tienen
su tercer mecanismo de threading, fijado como variables de entorno en
`docker-compose.yml:151-153`:

```yaml
OMP_NUM_THREADS: "1"
OPENBLAS_NUM_THREADS: "1"
MKL_NUM_THREADS: "1"
```

Con `OCR_PROCESOS` procesos en paralelo, cada uno multiplicando por sus
propios hilos internos si estos tres mecanismos no están todos fijados a
1, el número real de hilos compitiendo por CPU puede ser muchas veces
mayor que `cpus:` — la causa raíz de una sobresuscripción real
diagnosticada en una sesión anterior.

### 1.5 Ciclo de vida de los workers: reciclado periódico

**Fichero**: `services/ocr-paddle/main.py:180-183, 627-688`

`OCR_RECICLAR_CADA_N_PAGINAS` (por defecto `20`) no cambia CUÁNTOS
procesos hay, sino que los **recrea periódicamente** (destruir y volver a
lanzar) cada N páginas procesadas, para acotar el crecimiento de memoria
de un proceso de larga vida (ver `_ejecutar_lote`, que reparte el trabajo
en chunks de este tamaño). El pool "server" (normalmente 1 solo proceso)
se recicla tras **cada página individual**, no por chunks — ver
`_ejecutar_lote_server_reciclando`, línea 691.

### 1.6 Límites del contenedor que lo sostienen todo

**Fichero**: `docker-compose.yml:199-285`

```yaml
deploy:
  resources:
    limits:
      cpus: "5"
      memory: "10g"
```

Este es el único sitio donde se fija un número "a mano" — todo lo de
arriba (`_cpus_disponibles()`, `OCR_PROCESOS`) se deriva de `cpus: "5"`.
El historial de por qué `memory: "10g"` y no otro valor (incluye un
intento fallido con 8 workers que hizo OOM) está documentado en los
comentarios del propio fichero, líneas 260-285.

---

## 2. El Worker de .NET — concurrencia de mensajes, no de CPU

**Fichero**: `src/Worker/Program.cs:44-59`

```csharp
cfg.ReceiveEndpoint("DocumentoSubido", e =>
{
    e.PrefetchCount = 1;
    e.ConcurrentMessageLimit = 1;
    e.ConfigureConsumer<DocumentoSubidoConsumer>(context);
});
```

Esto **no tiene nada que ver con núcleos de CPU** del proceso .NET en sí
— es un límite de **cuántos documentos se procesan a la vez**, deliberado
y necesario porque el pool de `ocr-paddle` (sección 1) es un recurso
compartido y finito: si el Worker aceptara varios mensajes en paralelo
(el valor por defecto de MassTransit permite hasta 16 sin confirmar),
varios documentos competirían por los mismos `OCR_PROCESOS` procesos y el
reciclado periódico (sección 1.5) interrumpiría el trabajo de cualquier
documento que no fuera el que disparó el reciclado.

Es decir: la capa 1 (OCR) tiene paralelismo real dentro de un documento
(varias páginas a la vez, con varios procesos); la capa 2 (Worker) NO
tiene paralelismo entre documentos distintos — a propósito, por ahora (ver
memoria de proyecto `limitacion_concurrencia_ocr_multidocumento` para el
razonamiento completo y qué haría falta para cambiarlo).

---

## 3. Resumen: qué mirar según la pregunta

| Pregunta | Fichero y sección |
|---|---|
| ¿Cuántas CPUs ve el contenedor? | `main.py:45-75` (`_cpus_disponibles`) |
| ¿Cuántos procesos de OCR se lanzan? | `main.py:78-100` (`OCR_PROCESOS`/`OCR_PROCESOS_SERVER`) |
| ¿Dónde se crean esos procesos? | `main.py:487-514` (`_crear_executor`, `lifespan`) |
| ¿Cuántos hilos usa cada proceso por dentro? | `main.py:145-160,244-268` + `docker-compose.yml:151-153` |
| ¿Cuándo se reinician los procesos? | `main.py:180-183,627-688` |
| ¿Cuántas CPUs/memoria tiene el contenedor de verdad? | `docker-compose.yml:199-285` |
| ¿Cuántos documentos se procesan a la vez? | `src/Worker/Program.cs:44-59` |
| ¿Cómo dimensionar todo esto en un host nuevo? | `DESPLIEGUE.md` (no repetido aquí) |
