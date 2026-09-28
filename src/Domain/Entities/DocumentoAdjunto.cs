namespace LegalCaseManagement.Domain.Entities;

public enum EstadoProcesamientoDocumento
{
    Pendiente,
    Procesando,
    Completado,
    Error
}

// Metadatos del adjunto; el fichero en sí vive en almacenamiento de objetos
// (S3/SeaweedFS), no en Postgres (ver decisiones de arquitectura en README).
public class DocumentoAdjunto : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ExpedienteId { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    // Clave del archivo TAL COMO LO SUBIÓ EL USUARIO. No se toca nunca
    // después de subirlo: en un despacho el escaneo original puede ser la
    // pieza con valor probatorio, así que el procesado no lo sobrescribe.
    public string RutaAlmacenamiento { get; set; } = string.Empty;

    // Clave del PDF ya procesado (sello de folio + capa de texto buscable).
    // Null mientras no se ha generado: documentos en cola, con error, o de
    // un formato que todavía no se sella (ver DocumentoSubidoConsumer).
    public string? RutaAlmacenamientoProcesado { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public string TipoDocumento { get; set; } = string.Empty;
    public DateTime FechaSubida { get; set; } = DateTime.UtcNow;
    public EstadoProcesamientoDocumento EstadoProcesamiento { get; set; } = EstadoProcesamientoDocumento.Pendiente;
    public DateTime? FechaProcesado { get; set; }

    // Solo se rellena cuando EstadoProcesamiento == Error (ver DocumentoSubidoConsumer).
    public string? MensajeError { get; set; }

    // Rango de folio (Bates) dentro del expediente, asignado por
    // DocumentoSubidoConsumer a partir de Expediente.UltimoFolio. Null hasta
    // que el procesamiento asigna el rango (idempotencia: si ya está
    // asignado, no se vuelve a pedir un rango nuevo en un reintento).
    public int? FolioInicio { get; set; }
    public int? FolioFin { get; set; }

    // Confianza (0-1) de la clasificación automática de TipoDocumento hecha
    // por el microservicio doc-classifier. TipoDocumento solo se sobrescribe
    // automáticamente si estaba vacío (no pisa un valor puesto a mano).
    public double? TipoDocumentoConfianza { get; set; }
}
