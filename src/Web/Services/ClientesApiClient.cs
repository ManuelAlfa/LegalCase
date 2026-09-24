using LegalCaseManagement.Domain.Entities;

namespace Web.Services;

/// <summary>Cliente tipado hacia /api/clientes (pantalla de Clientes).</summary>
public class ClientesApiClient(HttpClient http, TokenProvider tokenProvider)
    : ApiClientAutenticado(http, tokenProvider)
{
    public Task<List<Cliente>> ListarAsync(CancellationToken ct = default) =>
        ObtenerListaAsync<Cliente>("/api/clientes", ct);
}
