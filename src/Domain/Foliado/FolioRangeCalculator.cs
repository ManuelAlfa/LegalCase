namespace LegalCaseManagement.Domain.Foliado;

public record RangoFolio(int FolioInicio, int FolioFin, int NuevoUltimoFolio);

// Lógica pura, separada del acceso a datos: quien la llama (DocumentoSubidoConsumer)
// es responsable de leer ultimoFolioPrevio de forma atómica (UPDATE ... RETURNING)
// para que dos documentos del mismo expediente no puedan calcular el mismo rango.
public static class FolioRangeCalculator
{
    public static RangoFolio Calcular(int ultimoFolioPrevio, int numPaginas)
    {
        if (numPaginas <= 0)
            throw new ArgumentOutOfRangeException(nameof(numPaginas), "Un documento debe tener al menos una página para recibir folio.");

        var folioInicio = ultimoFolioPrevio + 1;
        var folioFin = ultimoFolioPrevio + numPaginas;
        return new RangoFolio(folioInicio, folioFin, folioFin);
    }
}
