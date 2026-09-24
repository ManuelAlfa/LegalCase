using LegalCaseManagement.Infrastructure.Persistence;

namespace LegalCaseManagement.Api.Auth;

public class JwtTenantProvider : ICurrentTenantProvider
{
    public Guid TenantId { get; }

    public JwtTenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;
        var claim = user?.Identity?.IsAuthenticated == true
            ? user.FindFirst(TenantClaimTypes.TenantId)?.Value
            : null;

        TenantId = Guid.TryParse(claim, out var parsed) ? parsed : Guid.Empty;
    }
}
