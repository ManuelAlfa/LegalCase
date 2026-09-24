using System.Net.Http.Json;

namespace Web.Services;

public record DevTokenResponse(string AccessToken, DateTime ExpiresAtUtc);

/// <summary>
/// Cliente tipado hacia POST /api/auth/dev-token (tarea 6.1/6.2). Sin
/// AuthHeaderHandler: este endpoint no requiere token, es el que lo emite.
/// </summary>
public class AuthApiClient(HttpClient http)
{
    public async Task<DevTokenResponse?> ObtenerDevTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        var respuesta = await http.PostAsJsonAsync("/api/auth/dev-token", new { tenantId }, ct);
        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadFromJsonAsync<DevTokenResponse>(cancellationToken: ct);
    }
}
