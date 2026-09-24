# AVISO: embeddings sin clave real de OpenAI

`Embeddings:ApiKey` (user-secrets de `src/Worker`) sigue siendo el valor de
relleno `sk-tu-clave-real-de-openai`, no una clave real de pago.

Para que esto no bloquee el resto del pipeline (OCR, folios, clasificación)
mientras no haya clave real, `OpenAiEmbeddingClient` y
`DocumentoSubidoConsumer` (Worker) tratan ese valor de relleno como
"embeddings no configurados": los documentos se procesan y marcan
**Completado** igualmente, pero sus `FragmentoDocumento` se guardan con
`Embedding = null`.

**Consecuencia real:** la búsqueda semántica (pgvector) no encuentra estos
fragmentos — no falla, simplemente no hay nada que buscar en ellos.

## Cuándo hace falta actuar

En cuanto se configure una clave real de OpenAI:

1. `dotnet user-secrets set "Embeddings:ApiKey" "sk-..." ` desde `src/Worker`.
2. Los documentos ya procesados con `Embedding = null` **no se reprocesan
   solos** — hace falta un backfill explícito (volver a publicar
   `DocumentoSubido` para esos IDs, o un script aparte) si se quiere que
   también sean buscables semánticamente.
3. Este aviso deja de aplicar y puede borrarse.

Ver también memoria de proyecto `fase_2_4_ocr_embeddings_status` (mismo
pendiente ya identificado el 2026-08-09).
