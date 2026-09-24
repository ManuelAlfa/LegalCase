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

            var claims = new[]
            {
                new Claim(TenantClaimTypes.TenantId, request.TenantId.ToString())
            };

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

public record DevTokenRequest(Guid TenantId);
