using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace LegalCaseManagement.Infrastructure.Plantillas;

// DocumentFormat.OpenXml (MIT) en vez de EPPlus/DocX/NPOI: ver memoria de
// proyecto "licencias-agpl-prohibidas" — EPPlus es Polyform Noncommercial
// desde 2020 (no gratis para uso comercial), la versión gratuita de
// DocX/Xceed.Words.NET es solo para uso no comercial, y NPOI añadió en 2026
// una cuota de mantenimiento sobre el paquete NuGet binario (aunque el
// código fuente siga Apache-2.0).
public class FusionadorDocumentosOpenXml : IFusionadorDocumentos
{
    private static readonly Regex MarcadorRegex = new(@"\{\{\s*(.+?)\s*\}\}", RegexOptions.Compiled);

    public byte[] Fusionar(byte[] plantilla, IReadOnlyDictionary<string, string> valores)
    {
        using var memoria = new MemoryStream();
        memoria.Write(plantilla, 0, plantilla.Length);
        memoria.Position = 0;

        using (var documento = WordprocessingDocument.Open(memoria, true))
        {
            var mainPart = documento.MainDocumentPart
                ?? throw new InvalidOperationException("La plantilla no tiene parte de documento principal.");
            var documentoXml = mainPart.Document
                ?? throw new InvalidOperationException("La plantilla no tiene documento principal.");
            var body = documentoXml.Body
                ?? throw new InvalidOperationException("La plantilla no tiene cuerpo de documento.");

            var raices = new List<OpenXmlElement> { body };
            raices.AddRange(mainPart.HeaderParts.Select(h => h.Header).Where(h => h is not null)!);
            raices.AddRange(mainPart.FooterParts.Select(f => f.Footer).Where(f => f is not null)!);

            foreach (var raiz in raices)
            {
                FusionarRunsAdyacentes(raiz);
                SustituirMarcadores(raiz, valores);
            }

            documentoXml.Save();
            foreach (var header in mainPart.HeaderParts) header.Header?.Save();
            foreach (var footer in mainPart.FooterParts) footer.Footer?.Save();
        }

        return memoria.ToArray();
    }

    // Word (y cualquier herramienta que edite el XML crudo de OOXML) parte
    // un mismo marcador visual en varios <w:r> cuando el corrector
    // ortográfico o el control de cambios tocan el texto de por medio — no
    // es un defecto de esta librería en particular, es una característica
    // del formato. Fusionar los runs adyacentes con el mismo formato antes
    // de buscar corrige el problema de raíz, en vez de tener que parchear
    // una búsqueda que cruce límites de <w:r> caso por caso.
    private static void FusionarRunsAdyacentes(OpenXmlElement raiz)
    {
        foreach (var parrafo in raiz.Descendants<Paragraph>())
        {
            Run? anterior = null;

            foreach (var run in parrafo.Elements<Run>().ToList())
            {
                var textosRun = run.Elements<Text>().ToList();

                // Un run sin <w:t> (tab, salto de línea, imagen...) rompe la
                // fusión: lo que venga después ya no es visualmente adyacente
                // al texto anterior.
                if (textosRun.Count == 0)
                {
                    anterior = null;
                    continue;
                }

                if (anterior is not null && MismoFormato(anterior, run))
                {
                    var textoDestino = anterior.Elements<Text>().Last();
                    textoDestino.Text += string.Concat(textosRun.Select(t => t.Text));
                    textoDestino.Space = SpaceProcessingModeValues.Preserve;
                    run.Remove();
                }
                else
                {
                    anterior = run;
                }
            }
        }
    }

    private static bool MismoFormato(Run a, Run b)
    {
        var propsA = a.RunProperties?.OuterXml ?? string.Empty;
        var propsB = b.RunProperties?.OuterXml ?? string.Empty;
        return propsA == propsB;
    }

    private static void SustituirMarcadores(OpenXmlElement raiz, IReadOnlyDictionary<string, string> valores)
    {
        foreach (var texto in raiz.Descendants<Text>())
        {
            if (!texto.Text.Contains("{{")) continue;

            texto.Text = MarcadorRegex.Replace(texto.Text, m =>
            {
                var clave = m.Groups[1].Value;
                return valores.TryGetValue(clave, out var valor) ? valor : m.Value;
            });
            texto.Space = SpaceProcessingModeValues.Preserve;
        }
    }
}
