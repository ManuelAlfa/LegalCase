using LegalCaseManagement.Infrastructure.Documents;

namespace LegalCaseManagement.Infrastructure.Foliado;

public interface IFoliadorService
{
    // Estampa "Folio {n}" en cada página del PDF, empezando en folioInicio,
    // y devuelve el PDF resultante. El folio final (folioInicio + número de
    // páginas - 1) lo calcula quien llama, a partir de PaginaTexto.Count,
    // antes de invocar este método.
    //
    // paginasOcr es opcional: si se aporta, además del sello de folio se
    // incrusta el texto reconocido como capa invisible (seleccionable y
    // buscable) sobre cada página con ConfianzaOcr no nulo — las páginas con
    // texto ya extraíble directamente (ConfianzaOcr nulo) no la necesitan.
    byte[] EstamparFolios(byte[] pdfOriginal, int folioInicio, IReadOnlyList<PaginaTexto>? paginasOcr = null);
}
