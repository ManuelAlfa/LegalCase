using Pgvector;

namespace LegalCaseManagement.Domain.Entities;

public class FragmentoDocumento : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DocumentoAdjuntoId { get; set; }
    public int Pagina { get; set; }
    public int Parrafo { get; set; }
    public string TextoFragmento { get; set; } = string.Empty;

    // Nulo si el texto vino de extracción directa (PDF/DOCX con capa de
    // texto seleccionable); tiene valor si vino del microservicio de OCR
    // (confianza media de PaddleOCR para esa página, ver OcrClient).
    public double? ConfianzaOcr { get; set; }

    // Nulo hasta que el pipeline de embeddings procese el fragmento.
    public Vector? Embedding { get; set; }
}
