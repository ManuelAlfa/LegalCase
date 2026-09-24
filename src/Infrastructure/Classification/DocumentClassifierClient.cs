using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LegalCaseManagement.Infrastructure.Classification;

public class DocumentClassifierClient : IDocumentClassifierClient
{
    private readonly HttpClient _http;

    public DocumentClassifierClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ClasificacionDocumento> ClasificarAsync(string texto, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("/clasificar", new ClasificarRequestPayload(texto), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var cuerpo = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"El microservicio de clasificación devolvió {(int)response.StatusCode}: {cuerpo}");
        }

        var payload = await response.Content.ReadFromJsonAsync<ClasificarResponsePayload>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("El microservicio de clasificación devolvió una respuesta vacía.");

        return new ClasificacionDocumento(payload.TipoDocumento, payload.Confianza);
    }

    // Nombres de propiedad explícitos: el microservicio (pydantic) espera/
    // devuelve snake_case ("texto", "tipo_documento", "confianza") y el
    // matching por defecto de System.Text.Json no traduce snake_case, así
    // que hay que fijarlo a mano en vez de confiar en el comportamiento por
    // defecto del serializador.
    private record ClasificarRequestPayload([property: JsonPropertyName("texto")] string Texto);

    private record ClasificarResponsePayload(
        [property: JsonPropertyName("tipo_documento")] string TipoDocumento,
        [property: JsonPropertyName("confianza")] double Confianza);
}
