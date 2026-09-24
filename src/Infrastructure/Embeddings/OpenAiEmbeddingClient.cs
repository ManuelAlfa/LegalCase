using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace LegalCaseManagement.Infrastructure.Embeddings;

public class OpenAiEmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient _http;
    private readonly EmbeddingOptions _options;

    public OpenAiEmbeddingClient(HttpClient http, IOptions<EmbeddingOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<float[]>> GenerarAsync(IReadOnlyList<string> textos, CancellationToken cancellationToken = default)
    {
        if (textos.Count == 0)
            return [];

        // "sk-tu-clave..." es el valor de relleno documentado en README/memoria
        // del proyecto para desarrollo sin clave real de pago — se trata igual
        // que una clave vacía (sin configurar), no como una clave real
        // rechazada. DocumentoSubidoConsumer distingue esta excepción
        // concreta de un fallo real de la API (clave real inválida, límite de
        // tasa, etc.), que debe seguir marcando el documento como Error.
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || _options.ApiKey.StartsWith("sk-tu-clave", StringComparison.OrdinalIgnoreCase))
            throw new EmbeddingsNoConfiguradosException("Falta configurar Embeddings:ApiKey (user-secrets) con una clave real para poder llamar a la API de embeddings.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/embeddings")
        {
            Content = JsonContent.Create(new { model = _options.Model, input = textos })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var cuerpo = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"La API de embeddings devolvió {(int)response.StatusCode}: {cuerpo}");
        }

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponsePayload>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("La API de embeddings devolvió una respuesta vacía.");

        return payload.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToList();
    }

    private record EmbeddingResponsePayload(List<EmbeddingDataPayload> Data);
    private record EmbeddingDataPayload(float[] Embedding, int Index);
}

// Distinta de un fallo real de la API (InvalidOperationException): esta
// señala específicamente "no hay clave real configurada todavía", un estado
// esperado en desarrollo sin clave de pago, no un error de servicio.
public class EmbeddingsNoConfiguradosException : Exception
{
    public EmbeddingsNoConfiguradosException(string message) : base(message)
    {
    }
}
