namespace LegalCaseManagement.Domain.Entities;

public class Plazo : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ExpedienteId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public DateTime FechaLimite { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public List<int> DiasAvisoPrevio { get; set; } = new();
}
