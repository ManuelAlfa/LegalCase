using System.Reflection;
using PdfSharp.Fonts;

namespace LegalCaseManagement.Infrastructure.Foliado;

// PdfSharp 6.x (núcleo independiente de GDI) no tiene acceso a fuentes del
// sistema operativo en Linux, así que incluso una fuente "estándar" como
// Helvetica necesita un IFontResolver explícito. Se resuelve siempre a
// DejaVu Sans Bold (recurso incrustado, ver Infrastructure.csproj): es la
// única fuente que usa FoliadorService, no hace falta un resolver genérico.
public class FolioFontResolver : IFontResolver
{
    private const string FaceName = "DejaVuSans-Bold";
    private static readonly byte[] FontBytes = CargarFuenteIncrustada();

    public byte[] GetFont(string faceName) => FontBytes;

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new(FaceName);

    private static byte[] CargarFuenteIncrustada()
    {
        var assembly = typeof(FolioFontResolver).Assembly;
        const string resourceName = "LegalCaseManagement.Infrastructure.Foliado.Fonts.DejaVuSans-Bold.ttf";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"No se encontró el recurso incrustado '{resourceName}'.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
