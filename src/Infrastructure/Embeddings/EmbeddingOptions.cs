namespace LegalCaseManagement.Infrastructure.Embeddings;

public class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    // Se deja vacío a propósito: la clave real se configura vía
    // user-secrets/variable de entorno, nunca committeada (a diferencia de
    // otros secretos "solo de desarrollo local" de este repo, esta clave
    // pega contra una API de pago de un proveedor externo real).
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "text-embedding-3-small";
    public string BaseUrl { get; set; } = "https://api.openai.com";
}
