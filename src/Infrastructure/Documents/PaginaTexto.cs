namespace LegalCaseManagement.Infrastructure.Documents;

// ConfianzaOcr es nula cuando el texto viene de extracción directa (PDF/DOCX
// con capa de texto seleccionable, sin pasar por OCR); tiene valor cuando
// viene del microservicio de OCR (ver OcrClient).
//
// Lineas trae la posición de cada renglón en la página, cuando el OCR la
// conoce. La usa el foliador para colocar la capa de texto invisible del PDF
// encima de su renglón, en vez de repartirla de forma aproximada. Es nula en
// la extracción directa y vacía en páginas sin posiciones fiables.
public record PaginaTexto(
    int Pagina,
    string Texto,
    double? ConfianzaOcr = null,
    IReadOnlyList<LineaTexto>? Lineas = null);

/// <summary>
/// Un renglón y su caja en la página original, en fracciones (0 a 1) del
/// ancho y del alto, con el origen arriba a la izquierda. Relativas y no en
/// píxeles porque la resolución a la que se rasteriza cada página varía (ver
/// OCR_DPI_MODO en el servicio de OCR).
/// </summary>
public record LineaTexto(string Texto, double X0, double Y0, double X1, double Y1);
