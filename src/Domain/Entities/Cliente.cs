namespace LegalCaseManagement.Domain.Entities;

/// <summary>
/// Tipo de cliente, para que la gestoría decida si aplica retención de IRPF
/// al emitir la factura (D.5). La aplicación no calcula impuestos.
/// </summary>
public enum TipoCliente
{
    Particular,
    EmpresarioOProfesional,
    Entidad
}

public class Cliente : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string DniCif { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string CanalPreferido { get; set; } = string.Empty;

    /// <summary>Lo necesita la gestoría para emitir una factura completa (D.5).</summary>
    public string? Domicilio { get; set; }

    public TipoCliente TipoCliente { get; set; } = TipoCliente.Particular;
}
