namespace LegalCaseManagement.Infrastructure.Documents;

public interface ITextExtractor
{
    // Devuelve null si el archivo no tiene texto seleccionable suficiente
    // (imagen, o PDF escaneado sin capa de texto): en ese caso el llamador
    // debe recurrir a OCR en vez de usar este resultado.
    IReadOnlyList<PaginaTexto>? ExtraerTextoSeleccionable(Stream contenido, string contentType, string nombreArchivo);
}
