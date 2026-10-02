using LegalCaseManagement.Domain.Entities;

namespace LegalCaseManagement.Api.Auth;

/// <summary>
/// Quién hace la petición dentro del despacho, según el token. Id y Rol son
/// null con un token que no los lleva (dev-token sin usuario): mientras no
/// exista el login real (tarea 3.1), no todas las sesiones identifican al
/// usuario.
/// </summary>
public class UsuarioActual
{
    public Guid? Id { get; }
    public RolUsuario? Rol { get; }

    public UsuarioActual(IHttpContextAccessor httpContextAccessor)
    {
        var usuario = httpContextAccessor.HttpContext?.User;
        if (usuario?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (Guid.TryParse(usuario.FindFirst(TenantClaimTypes.UsuarioId)?.Value, out var id))
        {
            Id = id;
        }
        if (Enum.TryParse<RolUsuario>(usuario.FindFirst(TenantClaimTypes.Rol)?.Value, out var rol))
        {
            Rol = rol;
        }
    }

    /// <summary>Exportar a la gestoría y anotar sus facturas (D.5).</summary>
    public bool GestionaFacturacion => Rol is RolUsuario.Socio or RolUsuario.Administrativo;
}

/// <summary>Nombres de las políticas de autorización.</summary>
public static class Politicas
{
    /// <summary>Socio o Administrativo: exportar a la gestoría y anotar facturas (D.5).</summary>
    public const string GestionFacturacion = "GestionFacturacion";
}
