namespace LegalCaseManagement.Domain.Entities;

public class ParteContraria : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ExpedienteId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string RepresentanteLegal { get; set; } = string.Empty;
}
