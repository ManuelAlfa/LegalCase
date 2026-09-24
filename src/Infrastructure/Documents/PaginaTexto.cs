namespace LegalCaseManagement.Infrastructure.Documents;

// ConfianzaOcr es nula cuando el texto viene de extracción directa (PDF/DOCX
// con capa de texto seleccionable, sin pasar por OCR); tiene valor cuando
// viene del microservicio de OCR (ver OcrClient).
public record PaginaTexto(int Pagina, string Texto, double? ConfianzaOcr = null);
