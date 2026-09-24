# Guía de despliegue: dimensionado por host

Este documento reúne, en un solo sitio, las decisiones de configuración que
**dependen de la máquina concreta** donde se despliegue el stack — sobre todo
el microservicio de OCR (`ocr-paddle`), el más pesado de todos — y cómo
ajustarlas en un host nuevo sin tener que repetir el proceso de
diagnóstico de varios días que llevó a estos valores (sesiones del
2026-08-24 y 2026-08-25). El detalle técnico de cada decisión vive como
comentario junto al valor concreto en `docker-compose.yml`, `rabbitmq.conf`
y el código — este documento es el mapa que conecta esas piezas.

## 1. Motor de contenedores: Docker Engine nativo, no Docker Desktop

Requisito, no opcional — ver `README.md`, sección Requisitos, para la
instalación completa. Motivo: Docker Desktop puede caerse por su cuenta
(pasó en producción de desarrollo el 2026-08-25, tirando todo el stack a
mitad de una prueba), y los servicios con `restart: unless-stopped` bajo un
motor nativo sobreviven a cerrar la terminal/VS Code sin depender de una
aplicación de Windows.

**Gotcha conocido en WSL2**: si tras instalar el motor nativo los
contenedores no resuelven DNS o no publican puertos, comprobar
`update-alternatives --display iptables` — si está en modo `iptables-nft`,
cambiar a legacy:

```bash
sudo update-alternatives --set iptables /usr/sbin/iptables-legacy
sudo update-alternatives --set ip6tables /usr/sbin/ip6tables-legacy
sudo systemctl restart docker
```

y recrear los contenedores (`docker compose up -d --force-recreate`) — un
simple restart del daemon no repara el binding de puertos de contenedores ya
creados bajo el modo roto.

## 2. Dimensionado de CPU para `ocr-paddle`

**La única cifra que hay que decidir a mano en un host nuevo es `cpus:`**
en el servicio `ocr-paddle` de `docker-compose.yml`. Todo lo demás se ajusta
solo a partir de ahí:

- `OCR_PROCESOS` (nº de workers del pool "mobile") se auto-detecta en
  `services/ocr-paddle/main.py` (`_cpus_disponibles()`) leyendo el propio
  límite de cgroup del contenedor — **no** `nproc`/`os.cpu_count()`, que
  darían las CPUs del host completo, no las asignadas a este contenedor.
- `OCR_PROCESOS_SERVER` (pool dedicado a páginas degradadas) se resta de esa
  cuenta — por defecto 1.
- `OCR_HILOS_POR_PROCESO=1` debe mantenerse siempre así: es lo que hace que
  "nº de procesos = nº de CPUs asignadas" no sobresuscriba CPU, sea cual sea
  el nº de CPUs del host. Confirmado en pruebas (2026-08-24): PaddleX usa 8
  hilos por proceso por defecto si no se fija esto, causando una
  sobresuscripción real que hacía el OCR varias veces más lento de lo
  necesario.

**Fórmula para decidir `cpus:` en un host nuevo:**

```
cpus (ocr-paddle) = CPUs totales del host (nproc) − reserva para el resto del stack
```

Reserva recomendada para el resto del stack (Postgres, RabbitMQ, SeaweedFS,
doc-classifier, Api, Worker, sistema operativo): mínimo 2-4 núcleos —
observado en pruebas reales que ese conjunto usa poco en reposo, pero
conviene no dejarlo sin margen durante picos de subida/consulta simultáneos.

**Escalado incremental, no de golpe** — lección concreta del 2026-08-25: se
intentó subir de 2 a 8 procesos mobile de una vez y el contenedor entero
llegó al límite de memoria y el kernel mató un proceso por OOM en los
primeros minutos, sin completar ni una página. Subir de 2 en 2 (o similar),
validando con una prueba real a cada escala antes de seguir, es la forma
segura de encontrar el máximo real que un host concreto puede sostener.

## 3. Dimensionado de memoria

No se puede auto-detectar con la misma fiabilidad que las CPUs — depende de
la carga real, no solo del hardware. Fórmula aproximada, con datos reales
medidos en esta máquina de desarrollo:

```
memory (ocr-paddle) ≈ (OCR_PROCESOS × ~1 GB de base+margen) + colchón para picos del pool server
```

Historial de mediciones reales:

| Config | Host | Resultado |
|---|---|---|
| 2 mobile + 1 server, 8g | 16 CPUs / 15.85 GB | Estable tras el reciclado periódico (ver §4) |
| 4 mobile + 1 server, 10g | 16 CPUs / 15.85 GB | Estable, validado con 100 páginas reales (memoria osciló 5-9 GB) |
| 8 mobile + 1 server, 10g | 16 CPUs / 15.85 GB | **OOM en los primeros ~4 min** — memoria insuficiente para 8 workers activos a la vez |
| **8 mobile + 1 server, 16g** | 14 CPUs / 20 GB (`.wslconfig` ajustado el 2026-09-02) | **Estable a escala completa** — 928 páginas reales sin OOM (ver §8) |

El salto de "8 workers = OOM" a "8 workers = estable" no es una
contradicción: son dos configuraciones distintas de memoria total del host
(15.85 GB → 20 GB, ver `wsl-memory` en memoria de proyecto) y de límite del
contenedor (10g → 16g), no el mismo experimento con resultado distinto.

**Regla de oro**: nunca confiar en la fórmula sin más — validar siempre con
una prueba real (un documento de varias decenas/cientos de páginas, no solo
el healthcheck) a la escala nueva antes de dar la configuración por buena en
producción.

## 4. Reciclado periódico de memoria (`OCR_RECICLAR_CADA_N_PAGINAS`)

Un worker de PaddleOCR de larga vida, procesando páginas sucesivas dentro
del mismo proceso, acumula memoria de forma no acotada hasta el OOM —
confirmado dos veces con `dmesg` (2026-08-24 y 2026-08-25), entre 84 y 140
páginas según la prueba, con un solo worker llegando a 4+ GB de RSS. La
mitigación (`services/ocr-paddle/main.py`, `_ejecutar_lote`) recicla
(recrea) el pool mobile completo cada `OCR_RECICLAR_CADA_N_PAGINAS` páginas
(40 por defecto) — el pool server, al tener normalmente un solo proceso, se
recicla cada página (`_ejecutar_lote_server_reciclando`).

Este valor no necesita tocarse al cambiar de host salvo que aparezcan
nuevos OOM en pruebas reales a una escala de procesos distinta — con más
workers mobile, cada uno procesa MENOS páginas entre reciclados para el
mismo total (40 páginas repartidas entre más procesos), así que el riesgo
tiende a bajar, no a subir, al escalar `OCR_PROCESOS`.

## 5. Los tres timeouts acoplados

Tres timeouts independientes protegen contra un documento colgado en
distintos puntos del pipeline. **Deben subir juntos**, en este orden
creciente, o el más corto corta la petición antes de que el más largo
pueda actuar:

1. `OCR_TIMEOUT_SEGUNDOS` (`docker-compose.yml`, servicio `ocr-paddle`) —
   plazo interno del propio microservicio para UN documento completo.
2. `client.Timeout` del `HttpClient` hacia OCR (`src/Infrastructure/Documents/DocumentPipelineServiceCollectionExtensions.cs`)
   — debe quedar por ENCIMA del anterior con margen.
3. `consumer_timeout` (`rabbitmq.conf`) — debe quedar por ENCIMA de los dos
   anteriores más el tiempo de conversión PDF→imagen (con pypdfium2, ya
   despreciable — unos pocos minutos incluso para 928 páginas, ver §8;
   la cifra vieja de ~16 min para 500 páginas era de la época de
   pdftoppm) y el resto del pipeline (foliado, clasificación,
   fragmentación, embeddings) — si no, RabbitMQ corta el canal a mitad de
   una petición que sí iba a terminar bien, y se ve como
   `TaskCanceledException`/"response ended prematurely" en el Worker,
   pareciendo un fallo del microservicio OCR cuando no lo es.

Valores actuales (subidos el 2026-09-03 tras la Prueba 2 de 928 páginas
reales): `OCR_TIMEOUT_SEGUNDOS=14400` (240 min), `HttpClient.Timeout=260
min`, `consumer_timeout` de RabbitMQ a 290 min — dimensionados con margen
real sobre el peor caso medido con server activado (~160 min, 928
páginas). Con `OCR_HABILITAR_SERVER=false` (decisión actual, ver §8) el
tiempo real baja a ~80 min para el mismo volumen, así que estos timeouts
quedan con bastante más margen del estrictamente necesario — no hace falta
bajarlos, pero si se reactivara `OCR_HABILITAR_SERVER` en el futuro con un
documento sustancialmente más grande, revisar que sigan cubriendo el peor
caso real, no solo el de 928 páginas.

**El objetivo de <30 min aplica solo al OCR puro** (imagen→texto), no a la
conversión PDF→imagen previa (aclaración explícita del usuario, 2026-08-24)
— pero los tres timeouts de arriba sí tienen que cubrir el proceso
COMPLETO (conversión + OCR + resto del pipeline), porque es el tiempo real
que tarda el documento en confirmarse (ack) ante RabbitMQ.

## 6. RabbitMQ: usuarios y concurrencia

- **Usuarios declarativos, no un script**: `rabbitmq-init.sh` (montado en
  `/docker-entrypoint-init.d/`) nunca se ejecutó realmente — esa imagen no
  soporta ese hook (es una convención de la imagen de Postgres). Los
  usuarios (`legalcase`, `admin`) se declaran ahora en
  `rabbitmq-definitions.json`, cargado vía `management.load_definitions` en
  `rabbitmq.conf` — se aplica en cada arranque, de forma idempotente, sin
  depender de que el volumen ya tuviera el usuario.
- **`hostname: rabbitmq` fijo** en `docker-compose.yml`: RabbitMQ nombra su
  nodo Erlang por el hostname del contenedor — sin fijarlo, cada
  `--force-recreate` genera un nodo "nuevo" y las colas/usuarios
  persistentes de Mnesia quedan huérfanos aunque el volumen se conserve.
- **`PrefetchCount`/`ConcurrentMessageLimit = 1`** en el Worker
  (`src/Worker/Program.cs`): cada documento puede tardar minutos u horas —
  sin este límite, RabbitMQ entregaría varios documentos a la vez al
  Worker, que competirían por los mismos workers del microservicio OCR. Con
  el límite, la cola absorbe el pico de documentos subidos a la vez en vez
  de lanzárselos todos juntos al servicio de OCR.

## 7. Checklist para desplegar en un host nuevo

1. Instalar Docker Engine nativo (§1) — comprobar `iptables-legacy` si hay
   problemas de red tras instalar.
2. `nproc` y `free -h` en el host destino.
3. Decidir la reserva de CPU para el resto del stack (§2) y fijar `cpus:`
   de `ocr-paddle` — el resto (`OCR_PROCESOS`) se ajusta solo.
4. Fijar `memory:` de `ocr-paddle` con la fórmula aproximada (§3) — pero
   validar SIEMPRE con una prueba real antes de confiar en el número.
5. Si el hardware es sustancialmente distinto al de desarrollo (más lento o
   más rápido), revisar si los tres timeouts acoplados (§5) siguen dando
   margen suficiente — o si el hardware es mucho más rápido, si se puede
   bajarlos para fallar limpio antes.
6. Si se migra desde otro host: backup explícito de los 4 volúmenes
   (`postgres_data`, `rabbitmq_data`, `seaweedfs_data`, `ocr_paddle_models`)
   antes de tocar nada — ver memoria de migración del 2026-08-25 para el
   procedimiento exacto (contenedor `alpine` intermedio con `tar`).
7. Validar con un documento de prueba real (no solo el healthcheck) a la
   escala de producción antes de dar el despliegue por cerrado.

## 8. Resultado real medido — motor actual (PP-OCRv6, `OCR_HABILITAR_SERVER=false`)

**Estado actual, vigente desde el 2026-09-05**: el motor es PP-OCRv6
(`paddleocr==3.7.0`), la conversión PDF→imagen usa **pypdfium2** (no
pdftoppm/poppler), y **`OCR_HABILITAR_SERVER` está desactivado de forma
permanente** — ver el comentario extenso junto a esa variable en
`docker-compose.yml` y la memoria de proyecto `prueba2_gaceta_928_estado`
para el razonamiento completo. Resumen: se ejecutó el mismo documento de
928 páginas reales dos veces (con y sin reprocesado server) y se comparó
el TEXTO real, no solo la confianza numérica — en ningún caso revisado
server mejoró el resultado; en páginas de prosa el texto es prácticamente
idéntico, y en páginas de tablas numéricas densas ambas variantes
producen texto igual de ininteligible pese a que server reporta más
confianza. Desactivarlo no cuesta calidad medible y ahorra ~43% del
tiempo total.

**Prueba a escala completa (928 páginas reales, Gaceta de Madrid
1818-1819), 2026-09-04/05, solo mobile**:

| Fase | Duración | Detalle |
|---|---|---|
| Arranque de pools + conversión (pypdfium2, 928 páginas) | ~2-4 min | Antes (pdftoppm) rondaba 13-25 min para este volumen — ver memoria `pendiente_pypdfium2_conversion` |
| OCR fase mobile (928 páginas, 8 procesos, `cpus: "9"`) | ~80 min | ~5.2s/página agregado |
| Resto del pipeline .NET (folio, clasificación, fragmentación) | segundos | Insignificante frente al resto |
| **Total end-to-end** | **~83.7 min** | Desde la subida hasta `estado_procesamiento=Completado`, sin error |

Verificado no solo el tiempo: el PDF final persistido tiene las 928
páginas con texto real extraíble (`pypdf`) y la capa de texto invisible
(`3 Tr`) presente para búsqueda/selección — comprobado descargando el
objeto real de SeaweedFS, no solo el log del proceso. Copia local en
`test-data/resultados/prueba_928p_mobileonly_2026-09-05.pdf`, para
tenerla a mano sin depender de que el contenedor esté arrancado.

**El objetivo de <30 min de OCR puro sigue sin cumplirse** en el hardware
de desarrollo (14 CPUs / 20 GB de la VM WSL2) — pero la brecha se redujo
sustancialmente frente a la Prueba 1 original (ver historial más abajo):
de una proyección de ~200 min (con server, antes de estas optimizaciones)
a ~80 min reales (sin server, con pypdfium2 y `cpus: 9`). El tema de
velocidad de cara al objetivo de producto se revisará específicamente al
planificar el despliegue comercial (más núcleos dedicados, posible GPU —
ver memoria de proyecto `pendiente_evaluar_gpu_alquilada_ocr`, marcada
explícitamente como tema a tratar en profundidad antes de desplegar, con
su componente de coste Y de privacidad).

**Hallazgo de calidad pendiente, no resuelto**: las páginas con tablas
numéricas densas (aduanas, listados de cifras) no las lee bien el motor
actual en ninguna variante — no es un problema de mobile vs. server, es
un límite del reconocimiento con este tipo de layout. Ver memoria de
proyecto `pendiente_ocr_tablas_numericas_densas` y la tarea `IA.2e`
(reconocimiento de tablas con PP-StructureV3, CPU, ya especificada en el
plan maestro) como vía a evaluar — pendiente de decidir primero si este
tipo de contenido es relevante para los documentos reales de los
clientes.

### Historial: Prueba 1 (500 páginas sintéticas, PP-OCRv5, con server) — superada

Completada con éxito el 2026-08-26, con el motor e infraestructura de
entonces (PP-OCRv5, `cpus: "5"`, server activado): 2h1min38s totales
(~1h47min53s de OCR puro), muy por encima del objetivo de <30 min de
aquel momento. Esta prueba fue el terreno donde se encontraron y
corrigieron 2 bugs reales de fiabilidad (ver `ocr_dual_variant_performance_status`
en memoria de proyecto): un `BrokenProcessPool` que escapaba sin capturar
en varios puntos, y el pool mobile no reciclándose tras su último chunk.
Ambos corregidos y persistidos en `services/ocr-paddle/main.py`. Repetida
después con PP-OCRv6 híbrido (2026-09-01, 1h55min28s) y superada de nuevo
por el resultado actual de arriba tras desactivar server.
