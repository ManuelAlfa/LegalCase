using LegalCaseManagement.Domain.Entities;

namespace Web.Services;

/// <summary>Cliente tipado hacia /api/expedientes (tareas 6.1 y 6.3).</summary>
public class ExpedientesApiClient(HttpClient http, TokenProvider tokenProvider)
    : ApiClientAutenticado(http, tokenProvider)
{
    public Task<List<Expediente>> ListarAsync(CancellationToken ct = default) =>
        ObtenerListaAsync<Expediente>("/api/expedientes", ct);
}
