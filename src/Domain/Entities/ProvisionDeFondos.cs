namespace LegalCaseManagement.Domain.Entities;

public class ProvisionDeFondos : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ExpedienteId { get; set; }
    public decimal Importe { get; set; }
    public DateTime FechaSolicitud { get; set; } = DateTime.UtcNow;
    public bool Aplicada { get; set; }

    /// <summary>
    /// Exportación a la gestoría en la que salió (D.5): las provisiones van en
    /// el CSV, y sin esto se repetirían en cada exportación.
    /// </summary>
    public Guid? ExportacionGestoriaId { get; set; }
}
