using LegalCaseManagement.Infrastructure.Documents;

namespace LegalCaseManagement.Infrastructure.Ocr;

public interface IOcrClient
{
    Task<IReadOnlyList<PaginaTexto>> ReconocerAsync(
        Stream contenido,
        string nombreArchivo,
        string contentType,
        CancellationToken cancellationToken = default);
}
