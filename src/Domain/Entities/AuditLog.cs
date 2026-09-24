namespace LegalCaseManagement.Domain.Entities;

public class AuditLog : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid ExpedienteId { get; set; }
    public string Accion { get; set; } = string.Empty;
    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
    public string HashActual { get; set; } = string.Empty;
    public string HashAnterior { get; set; } = string.Empty;
}
