namespace LegalCaseManagement.Domain.Entities;

/// <summary>Estado de unas horas registradas respecto a la gestoría (D.5).</summary>
public enum EstadoFacturable
{
    /// <summary>Registradas; todavía no han salido en ninguna exportación.</summary>
    SinExportar,

    /// <summary>Incluidas en una exportación para la gestoría.</summary>
    Exportado,

    /// <summary>La gestoría ya emitió la factura que las incluye.</summary>
    Facturado
}

/// <summary>
/// Horas trabajadas en un expediente, para facturar por horas.
///
/// Compás NO expide facturas (decisión del 2026-10-01, ver D.5 en
/// plan_maestro_actualizado.docx): registra lo facturable y lo exporta a la
/// gestoría del despacho, que emite la factura con su propio programa. Así
/// queda fuera del reglamento VeriFactu.
/// </summary>
public class RegistroHoras : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ExpedienteId { get; set; }

    /// <summary>Abogado que hizo el trabajo.</summary>
    public Guid UsuarioId { get; set; }

    public DateTime Fecha { get; set; }

    /// <summary>Admite fracciones (2,5 h).</summary>
    public decimal Horas { get; set; }

    /// <summary>
    /// Tarifa aplicada a estas horas. Se copia de Expediente.TarifaHora al
    /// registrarlas y se guarda aquí: si después cambia la tarifa del
    /// expediente, las horas ya registradas conservan la suya.
    /// </summary>
    public decimal TarifaHora { get; set; }

    public string Descripcion { get; set; } = string.Empty;

    public EstadoFacturable Estado { get; set; } = EstadoFacturable.SinExportar;

    /// <summary>Exportación a la gestoría en la que salieron.</summary>
    public Guid? ExportacionGestoriaId { get; set; }

    /// <summary>
    /// Concepto facturable (Factura) en el que se agruparon al exportarlas.
    /// Hace falta para que, al anotar la factura que emite la gestoría, sus
    /// horas pasen a Facturado: sin este enlace no habría forma de saber qué
    /// horas incluía cada factura.
    /// </summary>
    public Guid? FacturaId { get; set; }
}
