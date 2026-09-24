namespace LegalCaseManagement.Infrastructure.Classification;

public record ClasificacionDocumento(string TipoDocumento, double Confianza);

public interface IDocumentClassifierClient
{
    Task<ClasificacionDocumento> ClasificarAsync(string texto, CancellationToken cancellationToken = default);
}
