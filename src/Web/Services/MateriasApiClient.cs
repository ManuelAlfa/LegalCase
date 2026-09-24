using LegalCaseManagement.Domain.Entities;

namespace Web.Services;

/// <summary>
/// Cliente tipado hacia /api/materias. Se usa para resolver el nombre de la
/// materia de cada expediente (el expediente solo guarda MateriaId).
/// </summary>
public class MateriasApiClient(HttpClient http, TokenProvider tokenProvider)
    : ApiClientAutenticado(http, tokenProvider)
{
    public Task<List<Materia>> ListarAsync(CancellationToken ct = default) =>
        ObtenerListaAsync<Materia>("/api/materias", ct);
}
