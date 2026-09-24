namespace LegalCaseManagement.Domain.Entities;

public enum ModoFactura
{
    TantoAlzado,
    PorHoras
}

public class Factura : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ExpedienteId { get; set; }
    public string Concepto { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public ModoFactura Modo { get; set; }
}
