namespace LegalCaseManagement.Domain.Entities;

// Catálogo de áreas de práctica por despacho (valores típicos de partida:
// Civil, Laboral, Familia, Penal, Mercantil, Extranjeria). Es una tabla, no
// un enum, para que cada despacho pueda ampliar su propio catálogo sin
// tocar código.
public class Materia : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
}
