namespace LegalCaseManagement.Domain.Entities;

public enum RolUsuario
{
    Socio,
    Abogado,
    ProcuradorGraduadoSocial,
    Administrativo
}

public class Usuario : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }
}
