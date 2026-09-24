namespace LegalCaseManagement.Infrastructure.Persistence;

public interface ICurrentTenantProvider
{
    Guid TenantId { get; }
}
