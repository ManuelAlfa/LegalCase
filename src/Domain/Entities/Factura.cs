namespace LegalCaseManagement.Domain.Entities;

public enum ModoFactura
{
    TantoAlzado,
    PorHoras
}

/// <summary>Estado de un concepto facturable respecto a la gestoría (D.5).</summary>
public enum EstadoFactura
{
    PendienteExportar,
    Exportada,
    EmitidaPorGestoria
}

/// <summary>
/// Concepto facturable de un expediente: lo que la gestoría tendrá que
/// facturar, NO una factura.
///
/// Desde el 2026-10-01 (D.5) Compás no expide facturas: registra lo
/// facturable, lo exporta a la gestoría, y la gestoría emite la factura
/// oficial con su programa; aquí solo se anota a mano su número, fecha y
/// total. Por eso esta entidad no tiene numeración propia ni impuestos, y
/// nada en la aplicación debe generar un documento titulado "factura": es la
/// frontera que la mantiene fuera del reglamento VeriFactu (RD 1007/2023).
/// El nombre de la clase se conserva para no romper la tabla y la Api.
/// </summary>
public class Factura : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ExpedienteId { get; set; }
    public string Concepto { get; set; } = string.Empty;
    /// <summary>Base sin impuestos. La aplicación no calcula IVA ni retenciones: lo hace la gestoría.</summary>
    public decimal Importe { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public ModoFactura Modo { get; set; }

    public EstadoFactura Estado { get; set; } = EstadoFactura.PendienteExportar;

    /// <summary>Exportación a la gestoría en la que salió.</summary>
    public Guid? ExportacionGestoriaId { get; set; }

    // Datos de la factura que emite la GESTORÍA, anotados a mano al recibirla.
    // No los genera Compás.
    public string? NumeroFacturaGestoria { get; set; }
    public DateTime? FechaEmisionGestoria { get; set; }
    public decimal? TotalFacturaGestoria { get; set; }
}
