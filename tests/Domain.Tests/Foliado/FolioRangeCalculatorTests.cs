using LegalCaseManagement.Domain.Foliado;

namespace LegalCaseManagement.Domain.Tests.Foliado;

public class FolioRangeCalculatorTests
{
    [Fact]
    public void Primer_documento_del_expediente_empieza_en_folio_1()
    {
        var rango = FolioRangeCalculator.Calcular(ultimoFolioPrevio: 0, numPaginas: 5);

        Assert.Equal(1, rango.FolioInicio);
        Assert.Equal(5, rango.FolioFin);
        Assert.Equal(5, rango.NuevoUltimoFolio);
    }

    [Fact]
    public void Documento_siguiente_continua_sin_solapar_ni_reiniciar()
    {
        var rango = FolioRangeCalculator.Calcular(ultimoFolioPrevio: 5, numPaginas: 3);

        Assert.Equal(6, rango.FolioInicio);
        Assert.Equal(8, rango.FolioFin);
        Assert.Equal(8, rango.NuevoUltimoFolio);
    }

    [Fact]
    public void Documento_de_una_sola_pagina_tiene_folioInicio_igual_a_folioFin()
    {
        var rango = FolioRangeCalculator.Calcular(ultimoFolioPrevio: 10, numPaginas: 1);

        Assert.Equal(11, rango.FolioInicio);
        Assert.Equal(11, rango.FolioFin);
    }

    [Fact]
    public void NumPaginas_cero_o_negativo_lanza_excepcion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FolioRangeCalculator.Calcular(ultimoFolioPrevio: 0, numPaginas: 0));
    }
}
