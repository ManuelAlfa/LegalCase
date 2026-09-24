namespace LegalCaseManagement.Domain.Entities;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nombre { get; set; } = string.Empty;

    // "pooled"    -> comparte cluster con otros despachos pequeños (RLS a nivel de fila)
    // "dedicated" -> cluster/instancia propia (despachos grandes, tipo enterprise)
    public string Plan { get; set; } = "pooled";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
