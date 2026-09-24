namespace LegalCaseManagement.Domain.Entities;

// Cualquier entidad que pertenezca a un despacho debe implementar esto.
// El filtro global de EF Core (ver AppDbContext.OnModelCreating) se aplica
// a todo lo que lo implemente, para que sea imposible olvidarse de filtrar
// por tenant en una consulta y filtrar datos entre despachos por error.
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}
