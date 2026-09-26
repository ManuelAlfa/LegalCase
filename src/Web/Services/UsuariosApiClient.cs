using LegalCaseManagement.Domain.Entities;

namespace Web.Services;

/// <summary>Cliente tipado hacia /api/usuarios: el equipo del despacho.</summary>
public class UsuariosApiClient(HttpClient http, TokenProvider tokenProvider)
    : ApiClientAutenticado(http, tokenProvider)
{
    public Task<List<UsuarioResumen>> ListarAsync(CancellationToken ct = default) =>
        ObtenerListaAsync<UsuarioResumen>("/api/usuarios", ct);
}

/// <summary>
/// Espejo de UsuarioResponse de la Api. No incluye el hash de contraseña,
/// que la Api tampoco serializa nunca.
/// </summary>
public record UsuarioResumen(Guid Id, string Nombre, string Email, RolUsuario Rol);
