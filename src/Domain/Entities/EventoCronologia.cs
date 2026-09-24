namespace LegalCaseManagement.Domain.Entities;

public class EventoCronologia : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ExpedienteId { get; set; }
    public DateTime Fecha { get; set; }
    public string Descripcion { get; set; } = string.Empty;

    // Nulos cuando el evento se añade a mano en vez de extraerse de un documento.
    public Guid? DocumentoAdjuntoId { get; set; }
    public Guid? FragmentoId { get; set; }
    public double? Confianza { get; set; }
}
