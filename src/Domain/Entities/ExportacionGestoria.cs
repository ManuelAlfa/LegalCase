namespace LegalCaseManagement.Domain.Entities;

/// <summary>
/// Una exportación de lo facturable para la gestoría del despacho (D.5).
/// Queda registrada para saber qué se mandó y cuándo, y para que lo ya
/// exportado no se vuelva a incluir en la siguiente.
/// </summary>
public class ExportacionGestoria : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public DateTime PeriodoDesde { get; set; }
    public DateTime PeriodoHasta { get; set; }
    public DateTime FechaGeneracion { get; set; } = DateTime.UtcNow;

    /// <summary>Quién la generó (null mientras no haya login real, tarea 3.1).</summary>
    public Guid? UsuarioId { get; set; }

    public int NumeroLineas { get; set; }
}
