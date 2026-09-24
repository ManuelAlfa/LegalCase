namespace LegalCaseManagement.Infrastructure.Documents;

public record Fragmento(int Pagina, int Parrafo, string Texto, double? ConfianzaOcr = null);

public static class FragmentadorTexto
{
    public static List<Fragmento> Fragmentar(IReadOnlyList<PaginaTexto> paginas)
    {
        var fragmentos = new List<Fragmento>();

        foreach (var pagina in paginas)
        {
            var parrafos = pagina.Texto
                .Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            // Sin separación clara en párrafos (habitual en texto salido de
            // OCR): se trata toda la página como un único párrafo en vez de
            // descartar su contenido.
            if (parrafos.Count == 0 && !string.IsNullOrWhiteSpace(pagina.Texto))
                parrafos.Add(pagina.Texto.Trim());

            // Página sin ningún texto reconocido por el OCR (confianza_ocr
            // suele venir en 0 para estos casos, ver _extraer_texto_y_confianza
            // en ocr-paddle/main.py): se guarda igualmente UN fragmento vacío
            // en vez de omitir la página por completo. Sin esto, la página
            // desaparece sin rastro de FragmentoDocumento — no aparece en
            // búsquedas, en el chat, ni en ningún futuro informe de "páginas
            // con confianza baja que necesitan revisión" que itere fragmentos
            // en vez de páginas — comprobado en pruebas reales: 6 de 928
            // páginas de la Gaceta de Madrid quedaban así, invisibles.
            if (parrafos.Count == 0)
                parrafos.Add(string.Empty);

            // La confianza es por página (una sola pasada de OCR), no por
            // párrafo: todos los fragmentos de una misma página heredan el
            // mismo valor.
            for (var i = 0; i < parrafos.Count; i++)
                fragmentos.Add(new Fragmento(pagina.Pagina, i + 1, parrafos[i], pagina.ConfianzaOcr));
        }

        return fragmentos;
    }
}
