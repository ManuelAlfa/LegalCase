namespace LegalCaseManagement.Infrastructure.Persistence;

// ICurrentTenantProvider para contextos sin HttpContext (consumers de
// MassTransit en Worker): el tenant no sale de un claim JWT, sino del
// propio mensaje que se está procesando. Debe registrarse Scoped (una
// instancia por mensaje/scope de MassTransit) y SetTenant() debe llamarse
// al principio de Consume(), antes de que AppDbContext o
// TenantSessionInterceptor lean TenantId.
public class AmbientTenantProvider : ICurrentTenantProvider
{
    public Guid TenantId { get; private set; }

    public void SetTenant(Guid tenantId)
    {
        TenantId = tenantId;
    }
}
