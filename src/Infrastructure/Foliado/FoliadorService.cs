using System.Text;
using LegalCaseManagement.Infrastructure.Documents;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
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
                    tieneTextoOculto = EscribirCapaDeTextoOculta(graficosOculto, pagina, paginaTexto);
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

    // Capa de texto invisible: el texto del OCR dibujado sobre el escaneo
    // para que el PDF se pueda buscar, seleccionar y copiar.
    private static bool EscribirCapaDeTextoOculta(XGraphics graficos, PdfPage pagina, PaginaTexto paginaTexto)
    {
        return paginaTexto.Lineas is { Count: > 0 } lineas
            ? EscribirRenglonesEnSuSitio(graficos, pagina, lineas)
            : EscribirRenglonesRepartidos(graficos, pagina, paginaTexto.Texto);
    }

    // Cada renglón se dibuja encima de su imagen, en la caja que da el OCR
    // (fracciones de la página original), con el tamaño de letra que hace que
    // ocupe exactamente el ancho de su caja. Así, al seleccionar en Acrobat,
    // la selección cae sobre el renglón que se ve y se copia entero.
    //
    // Antes de 2026-09-30 el OCR no daba posiciones y todos los renglones se
    // repartían uniformemente por la página con letra de 10 puntos: la
    // selección no coincidía con lo que se veía, y en páginas estrechas (la
    // Gaceta, 302 puntos de ancho) los renglones se salían por la derecha y
    // al copiar faltaba el final de cada uno.
    private static bool EscribirRenglonesEnSuSitio(XGraphics graficos, PdfPage pagina, IReadOnlyList<LineaTexto> lineas)
    {
        var anchoPagina = pagina.Width.Point;
        var altoPagina = pagina.Height.Point;
        var dibujadas = 0;

        foreach (var linea in lineas)
        {
            var texto = linea.Texto.Trim();
            var ancho = (linea.X1 - linea.X0) * anchoPagina;
            var alto = (linea.Y1 - linea.Y0) * altoPagina;
            if (texto.Length == 0 || ancho < 1 || alto < 1)
            {
                continue;
            }

            // El ancho del texto crece en proporción al tamaño de letra: se
            // mide una vez a 10 puntos y se escala para llenar la caja. Se
            // limita a la altura de la caja para que un renglón muy corto en
            // una caja ancha (un "- 1 -" centrado) no salga con letra enorme.
            var anchoA10 = graficos.MeasureString(texto, FuenteTextoOculto).Width;
            if (anchoA10 <= 0)
            {
                continue;
            }
            var tamano = Math.Clamp(10 * ancho / anchoA10, 1, alto);
            var fuente = new XFont("DejaVu Sans", tamano, XFontStyleEx.Regular);

            // Centrado en vertical dentro de la caja.
            var y = linea.Y0 * altoPagina + (alto - tamano) / 2;
            graficos.DrawString(texto, fuente, BrochaInvisible, new XPoint(linea.X0 * anchoPagina, y), XStringFormats.TopLeft);
            dibujadas++;
        }

        return dibujadas > 0;
    }

    // Sin posiciones (página girada por venir del revés, o respuesta de una
    // versión del servicio de OCR anterior a que las diera): los renglones se
    // reparten uniformemente en la altura de la página. No coincide con la
    // imagen, pero deja el texto completo seleccionable y en orden. La letra
    // se reduce lo necesario para que quepa el renglón más largo: antes era
    // de 10 puntos fijos y en páginas estrechas se cortaba por la derecha.
    private static bool EscribirRenglonesRepartidos(XGraphics graficos, PdfPage pagina, string texto)
    {
        var lineas = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lineas.Length == 0) return false;

        const double margenSuperior = 40;
        const double margenInferior = 40;
        const double margenIzquierdo = 40;
        var alturaUtil = pagina.Height.Point - margenSuperior - margenInferior;
        var anchoUtil = pagina.Width.Point - 2 * margenIzquierdo;
        var espaciado = lineas.Length > 1 ? alturaUtil / (lineas.Length - 1) : 0;

        var anchoMaximo = lineas.Max(l => graficos.MeasureString(l, FuenteTextoOculto).Width);
        var fuente = anchoMaximo > anchoUtil && anchoUtil > 0
            ? new XFont("DejaVu Sans", 10 * anchoUtil / anchoMaximo, XFontStyleEx.Regular)
            : FuenteTextoOculto;

        for (var j = 0; j < lineas.Length; j++)
        {
            var y = lineas.Length > 1 ? margenSuperior + j * espaciado : margenSuperior + alturaUtil / 2;
            graficos.DrawString(lineas[j], fuente, BrochaInvisible, new XPoint(margenIzquierdo, y));
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
        // La capa oculta es el ÚLTIMO elemento del array /Contents de la
        // página: se dibuja en su propia sesión de XGraphics, que añade su
        // propio flujo de contenido al final (ver EstamparFolios). Se parchea
        // solo ese flujo, y en TODOS sus bloques de texto.
        //
        // Antes se insertaba "3 Tr" solo tras el último "BT" de la página
        // entera. Bastaba porque todos los renglones iban en un único bloque
        // de texto. Ahora cada renglón lleva su tamaño de letra y su posición,
        // y no se puede depender de que PdfSharp los agrupe en un solo
        // bloque: si los partiera, los que quedaran sin "3 Tr" se verían
        // pintados en negro en PDF antiguos (versión 1.3), que es justo el
        // fallo crítico corregido el 2026-09-05.
        var elementos = pagina.Contents.Elements;
        if (elementos.Count == 0 || elementos.GetObject(elementos.Count - 1) is not PdfDictionary capaOculta
            || capaOculta.Stream is null)
        {
            throw new InvalidOperationException(
                "No se encontró el flujo de contenido de la capa de texto oculta; " +
                "el texto oculto podría quedar visible en la página.");
        }

        // Por si PdfSharp lo hubiera comprimido ya: se trabaja sobre el
        // contenido sin filtros.
        capaOculta.Stream.TryUncompress();

        // Latin1 (ISO-8859-1) hace un roundtrip byte a byte sin pérdidas.
        var contenido = Encoding.Latin1.GetString(capaOculta.Stream.Value);

        const string marcadorBt = "BT\n";
        var bloques = 0;
        var parcheado = new StringBuilder(contenido.Length + 64);
        var desde = 0;
        int posicion;
        while ((posicion = contenido.IndexOf(marcadorBt, desde, StringComparison.Ordinal)) >= 0)
        {
            parcheado.Append(contenido, desde, posicion + marcadorBt.Length - desde).Append("3 Tr\n");
            desde = posicion + marcadorBt.Length;
            bloques++;
        }
        parcheado.Append(contenido, desde, contenido.Length - desde);

        if (bloques == 0)
        {
            // No debería pasar nunca: solo se llama si se dibujó al menos un
            // renglón, y eso siempre produce un "BT". Fallar alto en vez de
            // seguir en silencio: mejor marcar el documento como Error (el
            // catch de DocumentoSubidoConsumer ya lo hace) que producir un PDF
            // con texto visible sin que nadie se entere.
            throw new InvalidOperationException(
                "No se encontró ningún operador BT en la capa de texto oculta; " +
                "el texto oculto podría quedar visible en la página.");
        }

        capaOculta.Stream.Value = Encoding.Latin1.GetBytes(parcheado.ToString());
    }
}
