namespace LegalCaseManagement.Contracts;

// Publicado por el endpoint POST /api/expedientes/{id}/documentos justo
// después de guardar el documento en almacenamiento de objetos y crear la
// fila de DocumentoAdjunto (con EstadoProcesamiento en Procesando).
// Consumido por DocumentoSubidoConsumer (Worker) para lanzar OCR/extracción
// de texto, fragmentación y embeddings sin bloquear la petición HTTP.
public record DocumentoSubido(Guid DocumentoAdjuntoId, Guid TenantId, string ClaveAlmacenamiento);
