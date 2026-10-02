using LegalCaseManagement.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LegalCaseManagement.Api.Auth;

public static class DevTokenEndpoints
{
    // Endpoint de solo-desarrollo para emitir JWTs de prueba sin montar
    // todavía un flujo de login real. El propio método no comprueba el
    // entorno; es responsabilidad de quien lo registra (Program.cs) llamarlo
    // solo dentro de IsDevelopment().
    public static WebApplication MapDevTokenEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/dev-token", (DevTokenRequest request, IOptions<JwtOptions> jwtOptionsAccessor) =>
        {
            var options = jwtOptionsAccessor.Value;
            var signingCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(TenantClaimTypes.TenantId, request.TenantId.ToString())
            };

            // Usuario y rol opcionales: sin ellos el token es como hasta ahora
            // (solo despacho). Con ellos se pueden probar los permisos por rol
            // (D.5: exportar a la gestoría, solo Socio y Administrativo)
            // mientras no exista el login real de la tarea 3.1. Se confía en
            // lo que dice la petición, igual que con el tenantId: por eso este
            // endpoint solo existe en Development.
            if (request.UsuarioId is { } usuarioId)
            {
                claims.Add(new Claim(TenantClaimTypes.UsuarioId, usuarioId.ToString()));
            }
            if (!string.IsNullOrWhiteSpace(request.Rol))
            {
                if (!Enum.TryParse<RolUsuario>(request.Rol, ignoreCase: true, out var rol))
                {
                    return Results.BadRequest($"Rol desconocido: {request.Rol}.");
                }
                claims.Add(new Claim(TenantClaimTypes.Rol, rol.ToString()));
            }

            var token = new JwtSecurityToken(
                issuer: options.Issuer,
                audience: options.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(options.AccessTokenMinutes),
                signingCredentials: signingCredentials);

            var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

            return Results.Ok(new { accessToken, expiresAtUtc = token.ValidTo });
        });

        return app;
    }
}

public record DevTokenRequest(Guid TenantId, Guid? UsuarioId = null, string? Rol = null);
