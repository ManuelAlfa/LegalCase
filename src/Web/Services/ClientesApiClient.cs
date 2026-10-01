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

    /// <summary>Expedientes, documentos y facturas de un cliente (menú contextual).</summary>
    public Task<ClienteRelacionados?> RelacionadosAsync(Guid clienteId, CancellationToken ct = default) =>
        ObtenerAsync<ClienteRelacionados>($"/api/clientes/{clienteId}/relacionados", ct);
}

public record ClienteRelacionados(
    List<ExpedienteRelacionado> Expedientes,
    List<DocumentoRelacionado> Documentos,
    List<FacturaRelacionada> Facturas);

public record ExpedienteRelacionado(Guid Id, string Numero, string Titulo, EstadoExpediente Estado);

public record DocumentoRelacionado(
    Guid Id, string NombreArchivo, Guid ExpedienteId, string NumeroExpediente, EstadoProcesamientoDocumento Estado);

public record FacturaRelacionada(
    Guid Id, string Concepto, decimal Importe, DateTime Fecha, Guid ExpedienteId, string NumeroExpediente);

public record NuevoCliente(
    string Nombre,
    string DniCif,
    string Email,
    string Telefono,
    string CanalPreferido);
