using LegalCaseManagement.Domain.Entities;

namespace Web.Services;

/// <summary>Cliente tipado hacia /api/documentos-adjuntos (pantalla de OCR / Foliado).</summary>
public class DocumentosApiClient(HttpClient http, TokenProvider tokenProvider)
    : ApiClientAutenticado(http, tokenProvider)
{
    public Task<List<DocumentoResumen>> ListarAsync(CancellationToken ct = default) =>
        ObtenerListaAsync<DocumentoResumen>("/api/documentos-adjuntos/resumen", ct);

    /// <summary>
    /// Sube un documento a un expediente. La Api lo deja en Procesando y
    /// publica el mensaje que arranca el OCR: no hay que dispararlo aparte.
    /// </summary>
    public Task<ResultadoApi<DocumentoResumen>> SubirAsync(
        Guid expedienteId, Stream contenido, string nombreArchivo, string contentType,
        CancellationToken ct = default) =>
        SubirArchivoAsync<DocumentoResumen>(
            $"/api/expedientes/{expedienteId}/documentos", contenido, nombreArchivo, contentType, ct);

    /// <summary>
    /// Descarga el PDF ya procesado: el mismo documento con el sello de
    /// folio y la capa de texto invisible encima, que es lo que el Worker
    /// deja guardado al terminar. No hay una copia aparte del original.
    /// </summary>
    public Task<byte[]?> DescargarAsync(Guid documentoId, bool original = false, CancellationToken ct = default) =>
        DescargarArchivoAsync(
            original
                ? $"/api/documentos-adjuntos/{documentoId}/descargar/original"
                : $"/api/documentos-adjuntos/{documentoId}/descargar",
            ct);
}

/// <summary>Espejo de DocumentoResumenResponse de la Api.</summary>
public record DocumentoResumen(
    Guid Id,
    Guid ExpedienteId,
    string NombreArchivo,
    string ContentType,
    long TamanoBytes,
    string TipoDocumento,
    double? TipoDocumentoConfianza,
    DateTime FechaSubida,
    EstadoProcesamientoDocumento EstadoProcesamiento,
    string? MensajeError,
    int? FolioInicio,
    int? FolioFin,
    int? Paginas,
    double? ConfianzaOcrMedia,
    bool TienePdfProcesado);
