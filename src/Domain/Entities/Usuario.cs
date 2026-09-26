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
    // Nombre y apellidos para mostrar ("Marta Alonso"). Sin esto, la interfaz
    // solo podía enseñar el correo, que ni es un nombre ni cabe en una
    // columna de tabla.
    public string Nombre { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }
}
