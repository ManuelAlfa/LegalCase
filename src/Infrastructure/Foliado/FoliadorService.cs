using System.Text;
using LegalCaseManagement.Infrastructure.Documents;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.IO;

namespace LegalCaseManagement.Infrastructure.Foliado;

// PdfSharp (MIT) en vez de PyMuPDF/iText7: ver memoria de proyecto
// "licencias-agpl-prohibidas" — AGPL/comercial no son opciones para un SaaS.
public class FoliadorService : IFoliadorService
{
    private static readonly XFont Fuente;
    private static readonly XFont FuenteTextoOculto;

    // Alpha 0: dibuja el texto de forma invisible con la misma API de alto
    // nivel (DrawString) que ya usa el sello de folio. Por sí solo esto NO
    // basta para que el texto sea seleccionable en Adobe Acrobat/Reader —
    // confirmado con un usuario real: el texto queda extraíble con pypdf o
    // poppler, pero Acrobat no lo trata como texto interactivo. Se mantiene
    // como salvaguarda: si el parche de ForzarModoTextoInvisible fallara
    // por lo que sea, el texto seguiría siendo invisible (aunque no
    // seleccionable), en vez de aparecer pintado en negro sobre el escaneo.
    private static readonly XBrush BrochaInvisible = new XSolidBrush(XColor.FromArgb(0, 0, 0, 0));

    static FoliadorService()
    {
        // Debe fijarse antes de crear cualquier XFont: PdfSharp 6.x no
        // resuelve fuentes del sistema operativo en Linux (ver FolioFontResolver).
        GlobalFontSettings.FontResolver = new FolioFontResolver();
        Fuente = new XFont("DejaVu Sans", 9, XFontStyleEx.Bold);
        FuenteTextoOculto = new XFont("DejaVu Sans", 10, XFontStyleEx.Regular);
    }

    public byte[] EstamparFolios(byte[] pdfOriginal, int folioInicio, IReadOnlyList<PaginaTexto>? paginasOcr = null)
    {
        using var streamEntrada = new MemoryStream(pdfOriginal);
        using var documento = PdfReader.Open(streamEntrada, PdfDocumentOpenMode.Modify);

        var textoPorPagina = paginasOcr?.ToDictionary(p => p.Pagina);

        var folio = folioInicio;
        for (var i = 0; i < documento.PageCount; i++)
        {
            var pagina = documento.Pages[i];

            using (var graficosFolio = XGraphics.FromPdfPage(pagina))
            {
                var texto = $"Folio {folio}";
                var tamano = graficosFolio.MeasureString(texto, Fuente);

                // Esquina inferior derecha, con margen: convención habitual de
                // un sello Bates en exhibits legales.
                var punto = new XPoint(pagina.Width.Point - tamano.Width - 24, pagina.Height.Point - 24);
                graficosFolio.DrawString(texto, Fuente, XBrushes.Black, punto);
            }

            // Bloque XGraphics SEPARADO a propósito del sello de folio de
            // arriba (antes compartían uno): cada using (XGraphics.
            // FromPdfPage(...)) crea su propio PdfContent independiente,
            // añadido al array /Contents de la página — así el texto oculto
            // queda garantizado como el ÚLTIMO elemento de ese array, sea
            // cual sea el comportamiento de PdfSharp con el ExtGState (ver
            // ForzarModoTextoInvisible más abajo, que depende de esto).
            if (textoPorPagina is not null
                && textoPorPagina.TryGetValue(i + 1, out var paginaTexto)
                && paginaTexto.ConfianzaOcr is not null
                && !string.IsNullOrWhiteSpace(paginaTexto.Texto))
            {
                bool tieneTextoOculto;
                using (var graficosOculto = XGraphics.FromPdfPage(pagina))
                {
                    tieneTextoOculto = EscribirCapaDeTextoOculta(graficosOculto, pagina, paginaTexto.Texto);
                }

                // Debe ejecutarse DESPUÉS de cerrar/disponer graficosOculto:
                // hasta entonces PdfSharp mantiene el content stream en un
                // renderer interno y no lo ha volcado todavía a pagina.Contents.
                if (tieneTextoOculto)
                {
                    ForzarModoTextoInvisible(pagina);
                }
            }

            folio++;
        }

        using var streamSalida = new MemoryStream();
        documento.Save(streamSalida);
        return streamSalida.ToArray();
    }

    // No hay coordenadas de línea/palabra disponibles desde el microservicio
    // de OCR (rec_polys se usa solo internamente para ordenar por lectura y
    // se descarta antes de responder — ver memoria de proyecto
    // "pdf-texto-buscable-pendiente"). Como aproximación razonable para una
    // primera versión, se reparten las líneas de forma uniforme en la altura
    // de la página: no reproduce la posición exacta de cada línea, pero deja
    // el texto completo seleccionable y en el orden de lectura correcto.
    private static bool EscribirCapaDeTextoOculta(XGraphics graficos, PdfPage pagina, string texto)
    {
        var lineas = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lineas.Length == 0) return false;

        const double margenSuperior = 40;
        const double margenInferior = 40;
        const double margenIzquierdo = 40;
        var alturaUtil = pagina.Height.Point - margenSuperior - margenInferior;
        var espaciado = lineas.Length > 1 ? alturaUtil / (lineas.Length - 1) : 0;

        for (var j = 0; j < lineas.Length; j++)
        {
            var y = lineas.Length > 1 ? margenSuperior + j * espaciado : margenSuperior + alturaUtil / 2;
            graficos.DrawString(lineas[j], FuenteTextoOculto, BrochaInvisible, new XPoint(margenIzquierdo, y));
        }

        return true;
    }

    // PdfSharp 6.2.4 no expone en su API pública el modo de renderizado de
    // texto (operador PDF "Tr") — el truco estándar de los PDF "OCR
    // searchable" (Tesseract, ABBYY, el propio Adobe) para que el texto sea
    // real e interactivo sin pintarse es "3 Tr" (ni rellenar ni trazar), no
    // la transparencia de BrochaInvisible. Como no hay forma de pedírselo a
    // XGraphics directamente, se parchea el content stream ya generado.
    //
    // CORREGIDO 2026-09-05 (bug crítico, ver memoria de proyecto
    // "pdf_texto_visible_pdf_antiguo_bug"): la versión anterior localizaba
    // el ExtGState de alpha 0 que se ESPERABA que PdfSharp creara para
    // BrochaInvisible y le insertaba "3 Tr" al lado. Confirmado en el
    // código fuente de PdfSharp (PdfGraphicsState.RealizeFillColor) que ese
    // "gs" SOLO se emite si el PDF de entrada declara versión >= 1.4 — con
    // un PDF real de un escáner/fotocopiadora antiguo (%PDF-1.3, nada
    // raro), PdfSharp nunca crea el ExtGState, este método no encontraba
    // nada que parchear y se rendía en silencio, dejando el texto pintado
    // en negro opaco visible sobre el escaneo real (confirmado
    // visualmente, no solo en el content stream).
    //
    // Ahora se localiza el ÚLTIMO operador "BT" del content stream
    // completo de la página en vez de depender del ExtGState. Es correcto
    // por construcción, no una heurística: EstamparFolios dibuja el sello
    // de folio y el texto oculto en dos bloques XGraphics SEPARADOS (ver
    // más arriba) — cada `using (XGraphics.FromPdfPage(...))` crea su
    // propio PdfContent independiente, añadido al final del array
    // /Contents de la página, y cada sesión de renderizado de PdfSharp
    // cierra su propio "ET"/"Q" antes de terminar. El texto oculto es
    // siempre el último bloque dibujado, así que su "BT" es siempre el
    // último de todo el content stream — sin depender de si PdfSharp
    // decide o no crear un ExtGState.
    private static void ForzarModoTextoInvisible(PdfPage pagina)
    {
        var contenido = pagina.Contents.CreateSingleContent();
        if (contenido.Stream is null)
        {
            throw new InvalidOperationException(
                "No se pudo generar el content stream de la página al forzar el modo de texto invisible.");
        }

        // Latin1 (ISO-8859-1) hace un roundtrip byte a byte sin pérdidas:
        // necesario porque el content stream es binario (incluye la imagen
        // escaneada), no texto Unicode.
        var textoContenido = Encoding.Latin1.GetString(contenido.Stream.Value);

        const string marcadorBt = "BT\n";
        var posicion = textoContenido.LastIndexOf(marcadorBt, StringComparison.Ordinal);
        if (posicion < 0)
        {
            // No debería pasar nunca: este método solo se llama si
            // EscribirCapaDeTextoOculta devolvió true (ver EstamparFolios),
            // que a su vez solo dibuja si hay al menos una línea de texto —
            // eso siempre implica un "BT". Fallar alto en vez de continuar
            // en silencio: preferible marcar el documento como Error (el
            // catch de DocumentoSubidoConsumer ya lo hace) que producir un
            // PDF con texto visible sin que nadie se entere.
            throw new InvalidOperationException(
                "No se encontró un operador BT en el content stream al forzar el modo de texto invisible; " +
                "el texto oculto podría quedar visible en la página.");
        }

        var textoParcheado = textoContenido.Insert(posicion + marcadorBt.Length, "3 Tr\n");
        var secuencia = ContentReader.ReadContent(Encoding.Latin1.GetBytes(textoParcheado));
        pagina.Contents.ReplaceContent(secuencia);
    }
}
