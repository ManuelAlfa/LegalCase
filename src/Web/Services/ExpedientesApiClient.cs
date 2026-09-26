using LegalCaseManagement.Domain.Entities;

namespace Web.Services;

/// <summary>Cliente tipado hacia /api/expedientes (tareas 6.1 y 6.3).</summary>
public class ExpedientesApiClient(HttpClient http, TokenProvider tokenProvider)
    : ApiClientAutenticado(http, tokenProvider)
{
    public Task<List<Expediente>> ListarAsync(CancellationToken ct = default) =>
        ObtenerListaAsync<Expediente>("/api/expedientes", ct);

    public Task<ResultadoApi<Expediente>> CrearAsync(NuevoExpediente datos, CancellationToken ct = default) =>
        CrearAsync<Expediente>("/api/expedientes", datos, ct);
}

/// <summary>
/// Datos del formulario de alta. El número no va aquí: lo asigna el servidor
/// con el contador correlativo del despacho.
/// </summary>
public record NuevoExpediente(
    string Titulo,
    Guid ClienteId,
    Guid? MateriaId,
    Guid? AbogadoResponsableId,
    string? ParteContraria,
    DateTime? FechaApertura,
    EstadoExpediente Estado);
