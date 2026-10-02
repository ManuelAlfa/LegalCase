namespace LegalCaseManagement.Api.Auth;

public static class TenantClaimTypes
{
    public const string TenantId = "tenant_id";

    // Identidad del usuario dentro del despacho. Opcionales mientras el único
    // emisor de tokens sea dev-token: un token sin ellos sigue valiendo para
    // todo lo que no exige saber quién es el usuario.
    public const string UsuarioId = "usuario_id";
    public const string Rol = "rol";
}
