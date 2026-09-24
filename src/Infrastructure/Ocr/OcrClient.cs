using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LegalCaseManagement.Infrastructure.Documents;

namespace LegalCaseManagement.Infrastructure.Ocr;

public class OcrClient : IOcrClient
{
    // El microservicio (FastAPI/pydantic) devuelve los campos en snake_case
    // (p.ej. "confianza_ocr"). ReadFromJsonAsync sin opciones explícitas usa
    // JsonSerializerOptions por defecto, que compara nombres de forma
    // sensible a mayúsculas — sin esto, "pagina"/"texto"/"confianza_ocr" no
    // casan con Pagina/Texto/ConfianzaOcr y el payload se deserializa con
    // los valores por defecto en vez de los reales.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public OcrClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<PaginaTexto>> ReconocerAsync(
        Stream contenido,
        string nombreArchivo,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(contenido);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(fileContent, "file", nombreArchivo);

        using var response = await _http.PostAsync("/ocr", form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var cuerpo = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"El microservicio de OCR devolvió {(int)response.StatusCode}: {cuerpo}");
        }

        var payload = await response.Content.ReadFromJsonAsync<OcrResponsePayload>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("El microservicio de OCR devolvió una respuesta vacía.");

        return payload.Paginas.Select(p => new PaginaTexto(p.Pagina, p.Texto, p.ConfianzaOcr)).ToList();
    }

    private record OcrResponsePayload(List<OcrPaginaPayload> Paginas);
    private record OcrPaginaPayload(int Pagina, string Texto, double ConfianzaOcr, string VarianteOcr);
}
