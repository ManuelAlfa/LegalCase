# Estado del proyecto — Legal Case Management

Última actualización: 2026-08-28. Este documento existe para que cualquier
persona o IA que retome el proyecto (en esta sesión de Claude Code, en otra
herramienta, o directamente un humano) entienda en qué punto está todo sin
tener que reconstruirlo leyendo el historial de conversaciones. Complementa
(no sustituye) a:

- `DESPLIEGUE.md` — dimensionado de recursos, timeouts, checklist para un
  host nuevo. Léelo si la pregunta es "¿cómo despliego esto?".
- `AVISO_EMBEDDINGS_DESACTIVADOS.md` — detalle del único bloqueo externo
  real (falta clave de pago de OpenAI).
- `README.md` — desfasado (habla de "siguiente paso: JWT" cuando JWT ya
  existe); no fiarse de él para el estado actual, solo para los comandos
  de arranque básicos, que siguen siendo válidos.

## Qué es esto

Aplicación de gestión de casos legales (expedientes, clientes, documentos,
plazos, facturación), multi-tenant, pensada para escalar de "despacho
pequeño" a cliente enterprise sin rehacer el modelo de datos. Backend en
.NET 10; sin interfaz de usuario todavía (todo se ha construido y probado
por API — Swagger, `curl`, tokens de desarrollo).

## Arquitectura

**Proyectos .NET** (`src/`):

- `Domain` — entidades puras (`Tenant`, `Expediente`, `Cliente`,
  `DocumentoAdjunto`, `FragmentoDocumento`, `Factura`, `Plazo`,
  `ProvisionDeFondos`, `ParteContraria`, `Usuario`, `AuditLog`,
  `EventoCronologia`, `EntidadExtraida`).
- `Infrastructure` — EF Core/Npgsql (`AppDbContext`, snake_case), cliente
  de OCR (`OcrClient`), extracción de texto directo
  (`PdfDocxTextExtractor`), fragmentación (`FragmentadorTexto`), sellado de
  folios + capa de texto invisible (`FoliadorService`, PdfSharp),
  almacenamiento de objetos (`IDocumentStorage`, SeaweedFS vía API S3),
  cliente de embeddings OpenAI, cliente del clasificador de documentos.
- `Api` — ASP.NET Core Minimal API, JWT Bearer, Swagger, endpoints REST.
- `Worker` — consumidor MassTransit/RabbitMQ (`DocumentoSubidoConsumer`),
  procesa documentos en background tras la subida.
- `Contracts` — mensajes compartidos entre Api y Worker (p. ej.
  `DocumentoSubido`).

**Servicios en `docker-compose.yml`**:

- `postgres` — Postgres + pgvector (para embeddings).
- `rabbitmq` — colas (`management.load_definitions` para usuarios
  declarativos, `hostname: rabbitmq` fijo para estabilidad del nodo).
- `seaweedfs` — almacenamiento de objetos (S3-compatible), guarda los
  binarios de los documentos adjuntos.
- `ocr-paddle` — microservicio Python/FastAPI propio, OCR con PaddleOCR
  (ver sección dedicada abajo — es la pieza más compleja y más trabajada).
- `doc-classifier` — microservicio Python separado, clasifica el tipo de
  documento a partir del texto ya extraído/OCRizado.

**Motor de contenedores**: Docker Engine nativo dentro de WSL2 (no Docker
Desktop — migrado el 2026-08-24 tras una caída real de Docker Desktop que
tumbó toda la sesión de trabajo). Nota: si un contenedor recién migrado no
resuelve DNS o no publica puertos, la primera causa a comprobar es
`iptables-legacy` vs `iptables-nft` (ver README).

## Flujo real de un documento, de principio a fin

1. `POST /api/expedientes/{id}/documentos` (multipart) — sube el binario a
   SeaweedFS, crea `DocumentoAdjunto` en estado `Procesando`, publica el
   evento `DocumentoSubido` (RabbitMQ) y devuelve 201 sin esperar al
   procesado (asíncrono).
2. `DocumentoSubidoConsumer` (Worker) recoge el evento:
   a. Descarga el binario de SeaweedFS.
   b. Intenta extracción directa de texto (`PdfDocxTextExtractor` — PdfPig
      para PDF, Open XML SDK para DOCX). Si la media de caracteres por
      página es ≥20, se queda con ese texto y **no pasa por OCR**. ⚠️ Esto
      es solo un umbral de CANTIDAD, no de calidad — ver limitación
      conocida más abajo.
   c. Si no hay texto suficiente (o es una imagen), llama al microservicio
      `ocr-paddle` vía `OcrClient`.
   d. Asigna rango de folio/Bates (transacción propia, idempotente — no
      duplica el rango si el mensaje se reprocesa tras un fallo).
   e. Si es PDF: `FoliadorService.EstamparFolios` sella "Folio N" visible
      en cada página Y, en las páginas que vinieron de OCR, incrusta el
      texto reconocido como capa invisible pero seleccionable (ver sección
      dedicada abajo) — sube el PDF resultante de vuelta a SeaweedFS,
      sobrescribiendo el original.
   f. Clasifica el tipo de documento (`doc-classifier`), sin pisar un tipo
      puesto a mano vía `PATCH`.
   g. Fragmenta el texto por párrafo (`FragmentadorTexto`) y genera
      embeddings (OpenAI — ver aviso de bloqueo abajo), guarda
      `FragmentoDocumento` por fragmento.
   h. Marca `DocumentoAdjunto` como `Completado` (o `Error` con el mensaje,
      si algo falla).
3. `GET /api/documentos-adjuntos/{id}/descargar` — descarga el PDF ya
   procesado (folio + texto seleccionable si aplica).
4. `GET /api/documentos-adjuntos/{id}/fragmentos` — texto reconocido por
   página/párrafo, sin pasar por el PDF (útil para depurar sin acceso
   directo a la BBDD).

## Autenticación y multi-tenancy

JWT Bearer real (`JwtOptions`, claim `TenantId`). Para desarrollo existe
`POST /api/auth/dev-token` (solo registrado si `IsDevelopment()`) que emite
un JWT válido dado un `TenantId` — no hay login de usuario real todavía,
es un atajo deliberado para no bloquear el resto del trabajo en un flujo de
login completo.

Multi-tenancy: modelo *pooled* (todo en el mismo Postgres, `TenantId` en
cada fila + filtro global de EF Core vía `ICurrentTenantProvider`). Cuando
haga falta un tenant grande aislado, se mueve a instancia dedicada sin
cambiar el modelo de datos. Row-Level Security de Postgres como segunda
capa de defensa — ver `roles-postgres-rls.md`.

## Superficie de la API (todo bajo `/api`, `RequireAuthorization()` salvo
donde se indique)

- `expedientes` — GET/POST (en `Program.cs` directamente, no en
  `Endpoints/`, inconsistencia menor de organización, no funcional).
- `clientes`, `partes-contrarias`, `materias`, `plazos`,
  `provisiones-de-fondos`, `facturas`, `usuarios` — CRUD estándar
  (GET lista, GET por id, POST, PATCH) cada uno en su propio
  `*Endpoints.cs`.
- `documentos-adjuntos` — GET lista/por id, POST (crear fila sin subir
  archivo), PATCH, y los especiales: `POST /expedientes/{id}/documentos`
  (subida real), `GET /{id}/descargar`, `GET /{id}/fragmentos`.
- `plantillas/{clave}` y `expedientes/{id}/documentos/generar/{clave}` —
  generación de documentos (DOCX) a partir de plantilla + datos del
  expediente (Open XML SDK).
- `audit-logs` — por expediente, y verificación de integridad.
- `auth/dev-token` — solo desarrollo.

No hay endpoint de borrado (`DELETE`) en ningún recurso todavía.

## El OCR: estado actual y todo el recorrido (la parte más trabajada)

Microservicio Python (`services/ocr-paddle/main.py`) con PaddleOCR,
`ProcessPoolExecutor` para paralelizar entre páginas, reciclado periódico
de los workers (para acotar crecimiento de memoria en documentos largos),
auto-detección de CPUs vía cgroup (portable a cualquier host, no hardcoded).

**Motor actual (implementado 2026-08-30, IA.2c — pendiente validar a
escala, no la decisión de diseño)**: `paddleocr==3.7.0` /
`paddlepaddle==3.2.0` (PP-OCRv6). Pool "mobile" = tier `small` (rápido,
todas las páginas); pool "server" = tier `medium` (reprocesado dirigido).
`OCR_HABILITAR_SERVER=true` (reactivado — tenía sentido desactivarlo con
PP-OCRv5, ya no con PP-OCRv6 medium). Disparador de reprocesado:
confianza por debajo de `OCR_CONFIANZA_MINIMA` **o** texto reconocido con
algún carácter CJK detectado (`_contiene_cjk` en `main.py`) — no solo
confianza, ver más abajo por qué.

**Por qué** (ver memoria `ocr_calidad_ppocrv6_gaceta`, detalle completo
ahí): comparando PP-OCRv5 mobile vs server sobre páginas reales del
documento "Gaceta de Madrid 1818-1819" (tipografía impresa antigua, no
manuscrita salvo la portada), se confirmó que la variante "server" de
PP-OCRv5 **no aportaba nada** (13/20 páginas idénticas a mobile, 3 peores,
2.8× más lento). El problema real: PP-OCRv5 (ambas variantes) usa un
vocabulario multilingüe que incluye chino/japonés, y cuando no reconoce
algo con confianza (orlas tipográficas, tablas numéricas) "alucina"
caracteres CJK sueltos (`喜`, `【。`, `中`, `ノ`) en vez de fallar
limpiamente. El umbral de confianza solo no bastaba como disparador de
reprocesado — una página con confianza 0.865 (por encima del umbral 0.80)
tenía igualmente alucinación CJK — de ahí la comprobación adicional.

**Resultado verificado (mismas 20 páginas reales)**: confianza 0.90-0.99
en las 20 páginas (antes 0.68-0.94). El disparador combinado reprocesó
5/20 páginas, incluida una (confianza 0.882, por encima del umbral) que
solo se detectó por CJK — confirma que el matiz era necesario. La
alucinación CJK bajó de 6/20 páginas con varios caracteres cada una a
2/20 páginas con un único carácter suelto — mejora drástica, no perfecta.

**VALIDADO A ESCALA REAL 2026-09-01 — Prueba 1 (500 páginas) repetida con
el motor híbrido**, mismo documento y mismas 32 páginas degradadas que la
Prueba 1 original. Completada con éxito:

| | PP-OCRv5 (2026-08-26) | PP-OCRv6 híbrido (2026-09-01) |
|---|---|---|
| Total | 2h1min38s | **1h55min28s** |

Más rápido en total (el reprocesado con medium fue más eficiente que con
PP-OCRv5 server, aunque la fase rápida en sí no lo fue — la duda de la
prueba de 20 páginas quedó resuelta a favor del híbrido a esta escala).

**Hallazgo que corrige una conclusión anterior**: las 32 páginas
degradadas daban confianza 0.000 (0 caracteres) con PP-OCRv5 en ambas
variantes — se había concluido que eran "ilegibles para cualquier
modelo". Con PP-OCRv6 medium dan confianza 0.66-0.75 y **texto real
recuperado** (verificado el contenido, no solo la confianza — es el mismo
texto de plantilla legal del resto del documento, degradado pero
legible). No eran irrecuperables: PP-OCRv5 simplemente no podía leerlas.

Verificado también que la capa de texto seleccionable (`3 Tr`) sigue
funcionando correctamente a 500 páginas. PDF persistido en
`test-data/resultados/prueba1_500_paginas_ppocrv6_hibrido_2026-09-01.pdf`.

Pendiente todavía: repetir con las 928 páginas reales de la Gaceta de
Madrid, y actualizar `DESPLIEGUE.md`/`docker-compose.yml` (siguen
describiendo PP-OCRv5).

**Historial de hitos ya cerrados** (no repetir estos diagnósticos, ver
memoria `ocr_dual_variant_performance_status` para el detalle completo):

- Prueba 1 (500 páginas sintéticas, 32 deliberadamente degradadas):
  completada con éxito el 2026-08-26 por el flujo .NET real completo.
  Tiempo total 2h1min38s (conversión 13min26s + OCR mobile 67min2s + OCR
  server 40min51s + resto ~11s). El objetivo original de <30min de OCR NO
  se cumple en el hardware de desarrollo (~1h48min real) — documentado
  honestamente, no forzado.
- Las 32 páginas degradadas dieron confianza 0.000 en ambas variantes
  (texto vacío, no hay nada que fragmentar ni incrustar — comportamiento
  correcto dado el texto vacío, no un bug).
- PDF resultante persistido en
  `test-data/resultados/prueba1_500_paginas_completado_2026-08-26.pdf`
  (⚠️ sin la capa de texto seleccionable — es de antes de ese fix).
- Prueba 2 (928 páginas reales, Gaceta de Madrid) — **intentada el
  2026-09-02, NO completada**. El PDF sin texto (generado para forzar OCR)
  pesa 1.57GB — mucho más que el sintético de 500p (252MB), por ser
  imágenes reales sin comprimir. El Worker .NET murió por **OOM del
  kernel** (4.6GB de RSS, confirmado en `dmesg`) mandando el fichero al
  microservicio de OCR — el documento queda huérfano en estado
  "Procesando" en la BBDD, nunca llegó a marcarse como error. Hipótesis:
  `ExtraerPaginasAsync` retiene el PDF entero en un `byte[]` durante todo
  el ciclo del mensaje; el propio microservicio de OCR ya resolvió este
  mismo problema hace tiempo (procesa página a página, no todo en
  memoria) pero el lado .NET nunca recibió el mismo tratamiento. Ver
  memoria de proyecto `prueba2_gaceta_928_estado` para el detalle
  completo. Conecta directamente con IA.2d (tomos): la solución real es
  probablemente trocear también la descarga/foliado, no solo la llamada
  de OCR — evita resolver el mismo problema dos veces. Fix ya persistido
  mientras tanto: `MaxRequestBodySize`/`MultipartBodyLengthLimit` en
  `src/Api/Program.cs` subido de 1GiB a 3GiB (el límite anterior daba 413
  con este fichero).

## PDF con texto seleccionable (capa OCR invisible)

Implementado en `FoliadorService.cs`
(`src/Infrastructure/Foliado/FoliadorService.cs`), usando PdfSharp (MIT).

- Cada página con `ConfianzaOcr` no nulo (vino de OCR) recibe una capa de
  texto invisible además del sello de folio visible.
- Posicionamiento de línea aproximado (reparto uniforme en la altura de
  página), no exacto — el microservicio de OCR no expone coordenadas de
  línea/palabra en su respuesta JSON (limitación conocida, aceptada para
  una primera versión).
- **Fix importante 2026-08-28**: la primera implementación usaba un brush
  con alpha=0 (transparente) — funcionaba con `pypdf`/`poppler` pero el
  usuario confirmó que **no era seleccionable en Adobe Acrobat real**. La
  causa: el truco estándar de los PDF "OCR searchable" es el modo de
  renderizado de texto invisible del propio PDF (operador `3 Tr`), no la
  transparencia. PdfSharp 6.2.4 no expone esto en su API pública, así que
  se parchea el content stream ya generado (`ForzarModoTextoInvisible`,
  usa `PdfSharp.Pdf.Content.ContentReader` + `PdfContents.ReplaceContent`)
  para insertar `3 Tr` justo donde empieza el texto oculto. Verificado de
  nuevo con `pypdf`/`poppler` tras el fix.
- ⚠️ **Confirmado en Adobe Acrobat real el 2026-09-01, con un problema
  real pendiente de arreglar** (documento de 500 páginas). El mecanismo
  `3 Tr` funciona: Ctrl+A selecciona el texto oculto, Ctrl+C + pegar en
  Word recupera las 500 páginas de texto completo y correcto. PERO el
  clic-y-arrastre normal (lo que hace un usuario de forma natural) da una
  experiencia rota — la mayoría del área de la página da cursor de imagen
  ("+", menú "Copiar/Editar/Censurar imagen"), solo franjas estrechas dan
  cursor de texto. Causa: `EscribirCapaDeTextoOculta` reparte las líneas
  de forma UNIFORME en toda la altura de la página (aprox. cada 127pt),
  mientras las líneas reales miden ~12-20pt — deja huecos enormes sin
  texto invisible superpuesto. Solución identificada, no implementada:
  exponer coordenadas reales de línea desde el microservicio de OCR
  (`_ordenar_por_lectura` ya las calcula vía `rec_polys`, pero las
  descarta) y usarlas para posicionar cada línea invisible en su Y real —
  toca ambos lados del pipeline (Python y .NET). Ver memoria de proyecto
  `pdf_texto_buscable_pendiente` para el detalle completo. No dar el
  feature por cerrado hasta implementarlo.
- Endpoint de descarga: `GET /api/documentos-adjuntos/{id}/descargar`.

## Limitación real descubierta y sin resolver: texto ya incrustado de mala calidad

Si un PDF subido YA tiene texto incrustado (p. ej., viene de un OCR previo
de mala calidad hecho por quien lo digitalizó), `PdfDocxTextExtractor` solo
comprueba CANTIDAD de caracteres por página (≥20 de media), no calidad —
así que un texto tan malo como `"GA'CéTAi;1«MADRID1819"` pasa el umbral y
el sistema **nunca vuelve a intentar OCR sobre ese documento**, aunque
nuestro pipeline (verificado, tuneado) probablemente lo haría mucho mejor.
Detectado el 2026-08-28 con el documento real de la Gaceta de Madrid. No
evaluado en profundidad ni implementada ninguna solución todavía (posible
vía: heurística de calidad — detectar palabras pegadas/sin espacios — no
solo cantidad).

## Otras limitaciones deliberadas, documentadas y aceptadas

- **Un solo documento OCR a la vez**: `PrefetchCount=1` /
  `ConcurrentMessageLimit=1` en `Worker/Program.cs`, a propósito — el pool
  de `ocr-paddle` es compartido y su reciclado periódico interrumpiría el
  trabajo de cualquier otro documento concurrente que lo usara. Escalar a
  concurrencia real necesitaría una instancia de `ocr-paddle` dedicada por
  documento en paralelo, no una compartida (ver memoria
  `limitacion_concurrencia_ocr_multidocumento`). Relevante si la futura UI
  permite subir varios documentos a la vez.
- **Variante "server" de PaddleOCR**: con PP-OCRv5 no aportaba nada
  (confirmado dos veces, sintético y real) y estuvo desactivada; reactivada
  el 2026-08-30 ahora que carga PP-OCRv6 medium (sí mejora la calidad, ver
  sección de OCR arriba) — pendiente de validar su coste de tiempo a
  escala real, no solo en 20 páginas.
- **GPU de alquiler para OCR**: solo investigado, no decidido ni
  implementado (memoria `pendiente_evaluar_gpu_alquilada_ocr`). Candidato
  concreto identificado el 2026-08-28: **PaddleOCR-VL-1.6** (modelo de
  visión-lenguaje, no un ajuste de PP-OCR — Apache 2.0, GPU obligatoria en
  la práctica, 11.9-80GB VRAM) — podría resolver de raíz la alucinación
  CJK, hipótesis razonable sin probar todavía. Requeriría una integración
  nueva en `services/ocr-paddle/` (otro pipeline de serving), no un cambio
  de nombre de modelo.
- **Optimizaciones ONNX Runtime / MKL-DNN**: evaluadas y descartadas el
  2026-08-28. ONNX Runtime (`Microsoft.ML.OnnxRuntime`) no aplica — es
  para .NET, y nuestro OCR corre en Python/PaddlePaddle nativo, no ONNX.
  El equivalente real en PaddleOCR (MKL-DNN, `run_mode: "mkldnn"`) tiene
  fugas de memoria documentadas y activas hasta mediados de 2025 (reportes
  de hasta 25GB de RAM en imágenes normales) — riesgo directo contra la
  prioridad de "nunca tumbar el host", descartado sin aplicar.
- **Revisión manual de páginas reprocesadas**: idea de UX futura (dejar
  que el usuario elija página a página si reprocesar con el modelo
  pesado) — explícitamente no implementar todavía.
- **Embeddings/búsqueda semántica**: sin clave real de OpenAI configurada
  (`sk-tu-clave-real-de-openai` es un valor de relleno). El pipeline no se
  bloquea por esto — los documentos se procesan igual, pero
  `FragmentoDocumento.Embedding` queda `null` y no aparecen en búsqueda
  semántica. Ver `AVISO_EMBEDDINGS_DESACTIVADOS.md` para el procedimiento
  de activación + backfill cuando haya clave real.

## Condiciones no negociables del producto (fijadas explícitamente por el usuario)

Dos ejes, ninguno subordinado al otro:

1. **Disponibilidad real de los documentos para el usuario** — no basta
   con verificar con herramientas propias (`pypdf`, `poppler`); hay que
   confirmar con las herramientas reales del usuario (Adobe Acrobat, en el
   caso del PDF seleccionable) antes de dar algo por resuelto.
2. **Velocidad y eficiencia del proceso** — cualquier mejora de calidad
   que empeore sustancialmente el tiempo (como PP-OCRv6 medio a escala
   completa) necesita sopesarse contra este eje, no adoptarse solo porque
   mejora la calidad.

## Restricciones de licencias (no negociable, aplica a todo el proyecto)

Nunca AGPL ni herramientas sin licencia comercial gratuita: **prohibido**
PyMuPDF/fitz, iText7, EPPlus, Xceed.Words.NET, NPOI (paquete NuGet
binario). Usar en su lugar: PdfSharp (MIT, ya en uso), Open XML SDK (MIT),
OfficeIMO (MIT), pypdf/pikepdf+reportlab si hiciera falta en Python.
Extendido el 2026-08-28 a cualquier motor de OCR/HTR alternativo: debe ser
gratis **e ilimitado** (sin cuotas de pago por uso) — PaddleOCR y Kraken
(Apache 2.0) cumplen; Transkribus fue descartado por no ser open source
completo y depender de créditos de pago.

## Planificación completa: plan_maestro_actualizado_v3.docx

Existe, en la raíz del repo, un documento de planificación mucho más
completo y detallado que este (`plan_maestro_actualizado_v3.docx`) —
tareas con nombre (IA.0 a IA.6, D.x, C.x, E.x, P.x, Fase 6...), cada una
con "por qué", alcance, criterio de terminado y un "prompt para el agente"
listo para ejecutar. **Es la fuente de planificación autoritativa; este
documento (`ESTADO_PROYECTO.md`) es un resumen del estado de
implementación, no la sustituye.** Léelo primero si necesitas planificar
qué hacer a continuación, no solo entender qué existe ya.

## Próximos pasos (por orden de dependencia, no de fecha)

1. **Confirmar en Adobe Acrobat real** que el fix `3 Tr` del texto
   seleccionable funciona — bloquea dar por cerrado ese tema.
2. **IA.2c (motor de OCR) — implementado 2026-08-30, pendiente validar a
   escala**: híbrido PP-OCRv6 small/medium + disparador confianza+CJK ya
   en código (ver sección de OCR arriba). Falta: (a) repetir la prueba de
   500 y/o 928 páginas reales con este motor, (b) investigar por qué
   reprocesar solo el 25% de páginas no fue más rápido que reprocesarlas
   todas (paralelismo del pool "server", solo 1 proceso), (c) revisar
   dimensionado de CPU/memoria en `docker-compose.yml` para el modelo
   nuevo.
3. **Actualizar `DESPLIEGUE.md`** con el resultado real del motor de OCR
   una vez validado a escala (ahora mismo sigue describiendo PP-OCRv5).
4. **IA.2d (arquitectura de tomos)** — no implementada todavía (sin
   entidad `TomoDocumento` en el código). Trocea documentos grandes en
   tomos procesados/persistidos de forma independiente, con tolerancia a
   fallos por página y progreso visible. El plan maestro trae un prompt
   completo listo para esta tarea. **Más urgente desde el 2026-09-02**: el
   intento de Prueba 2 (928p reales) hizo que el kernel matara al Worker
   por OOM (4.6GB) con un PDF de 1.5GB — ver `prueba2_gaceta_928_estado`.
   Al implementar IA.2d, revisar si el troceo debe cubrir también la
   descarga del fichero y el foliado (no solo la llamada a OCR, que es lo
   único que describe el alcance actual) — si no, este mismo OOM podría
   seguir ocurriendo.
5. **IA.2b (foliado bajo demanda)** — tampoco implementada todavía (sin
   campo `tiene_foliado_propio`, el foliado sigue siendo automático en
   `DocumentoSubidoConsumer`). Requiere: botón "Foliar expediente" en vez
   de automático tras cada OCR, con vista previa del orden; documentos ya
   numerados por terceros excluidos del foliado propio del despacho; series
   de foliado agrupables dentro del mismo expediente. Su corrección de
   orden real de páginas depende de que IA.2d ya exista.
6. **Empezar la interfaz de usuario** (Fase 6, Blazor Server según el plan
   maestro) — sin abandonar el trabajo de rendimiento en paralelo.
7. **Evaluar Kubernetes u otra opción de orquestación** para escalar más
   allá de un solo host — relevante en particular para la limitación de
   "un documento a la vez" si la UI lo expone a varios usuarios reales.
8. **Prueba 2 real** (928 páginas completas de la Gaceta de Madrid) —
   pendiente, buen candidato para combinarla con la validación a escala del
   punto 2.
9. Clave real de OpenAI + backfill de embeddings — bloqueado externamente
   (no técnico), sin fecha.

## Cómo arrancar el entorno de desarrollo

```bash
cd legal-case-management
docker compose up -d                    # Postgres/RabbitMQ/SeaweedFS/OCR/clasificador
cd src/Api && dotnet run                # API en background si hace falta: nohup ... &
cd src/Worker && dotnet run             # Worker, igual
```

Token de desarrollo (sustituye a login real):

```bash
curl -X POST http://localhost:5000/api/auth/dev-token \
  -H "Content-Type: application/json" \
  -d '{"tenantId":"<guid-de-un-tenant-existente>"}'
```

Ver `DESPLIEGUE.md` para timeouts, dimensionado de CPU/memoria del OCR, y
checklist completo para un host nuevo.
