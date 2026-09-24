using LegalCaseManagement.Domain.Entities;

namespace Web.Services;

/// <summary>
/// Cliente tipado hacia /api/plazos (tareas 6.1 y 6.3). El endpoint ya
/// devuelve los plazos del tenant ordenados por fecha_limite ascendente.
/// </summary>
public class PlazosApiClient(HttpClient http, TokenProvider tokenProvider)
    : ApiClientAutenticado(http, tokenProvider)
{
    public Task<List<Plazo>> ListarAsync(CancellationToken ct = default) =>
        ObtenerListaAsync<Plazo>("/api/plazos", ct);
}
