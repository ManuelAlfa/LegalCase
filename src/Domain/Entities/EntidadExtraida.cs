namespace LegalCaseManagement.Domain.Entities;

public enum TipoEntidadExtraida
{
    Persona,
    Empresa,
    Fecha,
    Importe
}

public enum EstadoEntidadExtraida
{
    PendienteRevision,
    Confirmada,
    Descartada
}

public class EntidadExtraida : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ExpedienteId { get; set; }
    public Guid DocumentoAdjuntoId { get; set; }
    public Guid FragmentoId { get; set; }
    public TipoEntidadExtraida Tipo { get; set; }
    public string ValorTexto { get; set; } = string.Empty;
    public string ValorNormalizado { get; set; } = string.Empty;
    public double Confianza { get; set; }
    public EstadoEntidadExtraida Estado { get; set; } = EstadoEntidadExtraida.PendienteRevision;
}
