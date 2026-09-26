namespace LegalCaseManagement.Domain.Entities;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nombre { get; set; } = string.Empty;

    // "pooled"    -> comparte cluster con otros despachos pequeños (RLS a nivel de fila)
    // "dedicated" -> cluster/instancia propia (despachos grandes, tipo enterprise)
    public string Plan { get; set; } = "pooled";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Contador del número correlativo de expediente de este despacho. Vive
    // aquí, y no como un MAX() sobre expedientes, para poder incrementarlo
    // con un UPDATE ... RETURNING de una sola sentencia —atómico en
    // Postgres— y que dos altas simultáneas no obtengan el mismo número.
    // Es el mismo patrón que ya usa Expediente.UltimoFolio para el foliado.
    // AnioNumeracion permite reiniciar la serie cada año natural, como hace
    // un despacho real: 2026/0001 vuelve a empezar en enero.
    public int AnioNumeracion { get; set; }
    public int UltimoNumeroExpediente { get; set; }
}
