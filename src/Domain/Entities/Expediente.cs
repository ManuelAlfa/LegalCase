namespace LegalCaseManagement.Domain.Entities;

public enum EstadoExpediente
{
    Abierto,
    EnTramite,
    Cerrado,
    Archivado
}

public class Expediente : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public EstadoExpediente Estado { get; set; } = EstadoExpediente.Abierto;
    public DateTime FechaApertura { get; set; } = DateTime.UtcNow;
    public DateTime? FechaCierre { get; set; }

    // Enlace opcional a la ficha completa del cliente (DNI/CIF, email,
    // teléfono) y a la materia, más allá del nombre libre de arriba —
    // necesario para la generación de documentos por plantilla (ver
    // Infrastructure/Plantillas), que necesita datos fiables del cliente y
    // no solo el nombre escrito a mano en `Cliente`.
    public Guid? ClienteId { get; set; }
    public Guid? MateriaId { get; set; }

    // Contador atómico para asignar rangos de folio (Bates) a los
    // DocumentoAdjunto del expediente sin colisiones entre documentos
    // procesados en paralelo (ver DocumentoSubidoConsumer): se incrementa
    // con un UPDATE ... RETURNING de una sola sentencia, atómico en Postgres.
    public int UltimoFolio { get; set; } = 0;
}
