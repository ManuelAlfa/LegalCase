namespace LegalCaseManagement.Infrastructure.Embeddings;

public interface IEmbeddingClient
{
    Task<IReadOnlyList<float[]>> GenerarAsync(IReadOnlyList<string> textos, CancellationToken cancellationToken = default);
}
