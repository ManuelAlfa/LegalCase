using System.Globalization;
using LegalCaseManagement.Domain.Entities;

namespace Web;

/// <summary>
/// Traducciones de dominio a interfaz (texto visible y clase CSS de color)
/// en un único sitio, para que dos pantallas nunca muestren el mismo estado
/// con distinto nombre o color. El significado de cada color está fijado en
/// la tarea 6.0: verde = confirmado/activo, ámbar = próximo, rojo = urgente
/// o vencido, gris = sin urgencia o inactivo.
/// </summary>
public static class Presentacion
{
    public static readonly CultureInfo CulturaEs = new("es-ES");

    public static string TextoEstado(EstadoExpediente estado) => estado switch
    {
        EstadoExpediente.Abierto => "Activo",
        EstadoExpediente.EnTramite => "En trámite",
        EstadoExpediente.Cerrado => "Cerrado",
        EstadoExpediente.Archivado => "Archivado",
        _ => estado.ToString(),
    };

    public static string ClaseChipEstado(EstadoExpediente estado) => estado switch
    {
        EstadoExpediente.Abierto => "cps-chip-success",
        EstadoExpediente.EnTramite => "cps-chip-info",
        _ => "cps-chip-default",
    };

    /// <summary>Días que faltan para un plazo, contados en fechas completas.</summary>
    public static int DiasHasta(DateTime fechaLimite) =>
        (fechaLimite.Date - DateTime.UtcNow.Date).Days;

    public static string TextoUrgencia(int diasRestantes)
    {
        if (diasRestantes < 0) return "vencido";
        if (diasRestantes == 0) return "vence hoy";
        if (diasRestantes == 1) return "vence en 1 día";
        return $"vence en {diasRestantes} días";
    }

    public static string ClaseChipUrgencia(int diasRestantes)
    {
        if (diasRestantes <= 3) return "cps-chip-error";
        if (diasRestantes <= 7) return "cps-chip-warning";
        return "cps-chip-default";
    }

    public static string ColorPuntoUrgencia(int diasRestantes)
    {
        if (diasRestantes <= 3) return "var(--mud-error)";
        if (diasRestantes <= 7) return "var(--mud-warning)";
        return "var(--mud-text-disabled)";
    }

    public static string FechaCorta(DateTime fecha) => fecha.ToString("d MMM.", CulturaEs);

    public static string FechaLarga(DateTime fecha) =>
        fecha.ToString("dddd, d 'de' MMMM 'de' yyyy", CulturaEs);
}
