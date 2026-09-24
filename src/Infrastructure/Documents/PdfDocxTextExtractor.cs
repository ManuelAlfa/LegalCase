using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace LegalCaseManagement.Infrastructure.Documents;

public class PdfDocxTextExtractor : ITextExtractor
{
    // Umbral heurístico: por debajo de esta media de caracteres por página se
    // considera que no hay texto seleccionable real (ruido/metadatos, no
    // contenido legible) y se debe recurrir a OCR en su lugar.
    private const int CaracteresMinimosPorPagina = 20;

    public IReadOnlyList<PaginaTexto>? ExtraerTextoSeleccionable(Stream contenido, string contentType, string nombreArchivo)
    {
        var esPdf = contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            || nombreArchivo.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        var esDocx = contentType.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase)
            || nombreArchivo.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

        List<PaginaTexto> paginas;
        if (esPdf)
        {
            paginas = ExtraerDePdf(contenido);
        }
        else if (esDocx)
        {
            paginas = ExtraerDeDocx(contenido);
        }
        else
        {
            // Imágenes (jpg/png/tiff/...) nunca tienen texto seleccionable:
            // van siempre por OCR.
            return null;
        }

        var mediaPorPagina = paginas.Count == 0
            ? 0
            : paginas.Sum(p => p.Texto.Trim().Length) / (double)paginas.Count;

        return mediaPorPagina >= CaracteresMinimosPorPagina ? paginas : null;
    }

    private static List<PaginaTexto> ExtraerDePdf(Stream contenido)
    {
        using var documento = PdfDocument.Open(contenido);
        var paginas = new List<PaginaTexto>();
        var numero = 0;
        foreach (var pagina in documento.GetPages())
        {
            numero++;
            paginas.Add(new PaginaTexto(numero, pagina.Text));
        }
        return paginas;
    }

    private static List<PaginaTexto> ExtraerDeDocx(Stream contenido)
    {
        using var documento = WordprocessingDocument.Open(contenido, false);
        var texto = documento.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;

        // DOCX no tiene concepto nativo de "página" (el salto de página
        // depende del renderizador, no del propio documento); se trata como
        // una única página lógica — la fragmentación posterior por párrafo
        // funciona igual.
        return [new PaginaTexto(1, texto)];
    }
}
