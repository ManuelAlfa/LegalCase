using LegalCaseManagement.Domain.Entities;

namespace Web.Services;

/// <summary>Cliente tipado hacia /api/clientes (pantalla de Clientes).</summary>
public class ClientesApiClient(HttpClient http, TokenProvider tokenProvider)
    : ApiClientAutenticado(http, tokenProvider)
{
    public Task<List<Cliente>> ListarAsync(CancellationToken ct = default) =>
        ObtenerListaAsync<Cliente>("/api/clientes", ct);

    public Task<ResultadoApi<Cliente>> CrearAsync(NuevoCliente datos, CancellationToken ct = default) =>
        CrearAsync<Cliente>("/api/clientes", datos, ct);
}

public record NuevoCliente(
    string Nombre,
    string DniCif,
    string Email,
    string Telefono,
    string CanalPreferido);
