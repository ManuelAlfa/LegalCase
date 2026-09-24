using MudBlazor;

namespace Web;

/// <summary>
/// Tema visual de la aplicación (tarea 6.0), calcado de los valores reales
/// definidos en mockup_pantallas_compas_mudblazor.html (la versión definitiva
/// del mockup, confirmada 2026-09-19) — no de los valores de ejemplo del plan
/// de desarrollo, que quedaron desactualizados frente al mockup ya aprobado.
/// </summary>
public static class CompasTheme
{
    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1F6F64",
            PrimaryDarken = "#175349",
            PrimaryLighten = "#E4F0EC",
            Dark = "#16233A",
            Success = "#2E7D5B",
            SuccessLighten = "#E3F3EA",
            Warning = "#B9762B",
            WarningLighten = "#F8ECDC",
            Error = "#B3402D",
            ErrorLighten = "#F8E2DD",
            Info = "#3B6EA5",
            InfoLighten = "#E4ECF5",
            Surface = "#FFFFFF",
            Background = "#F4F6F5",
            DrawerBackground = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            TextPrimary = "rgba(0,0,0,.87)",
            TextSecondary = "rgba(0,0,0,.60)",
            TextDisabled = "rgba(0,0,0,.38)",
            Divider = "rgba(0,0,0,.10)",
            LinesDefault = "rgba(0,0,0,.10)",
        },
        LayoutProperties = new LayoutProperties
        {
            AppbarHeight = "64px",
            DrawerWidthLeft = "260px",
            DefaultBorderRadius = "6px",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Public Sans", "-apple-system", "Segoe UI", "Roboto", "Arial", "sans-serif"],
            },
            H1 = new H1Typography { FontFamily = ["Fraunces", "Georgia", "serif"], FontWeight = "600" },
            H2 = new H2Typography { FontFamily = ["Fraunces", "Georgia", "serif"], FontWeight = "600" },
            H3 = new H3Typography { FontFamily = ["Fraunces", "Georgia", "serif"], FontWeight = "600" },
            H4 = new H4Typography { FontFamily = ["Fraunces", "Georgia", "serif"], FontWeight = "600" },
            H5 = new H5Typography { FontFamily = ["Fraunces", "Georgia", "serif"], FontWeight = "600" },
            H6 = new H6Typography { FontFamily = ["Fraunces", "Georgia", "serif"], FontWeight = "600" },
            Body1 = new Body1Typography { FontFamily = ["Public Sans", "-apple-system", "Segoe UI", "Roboto", "Arial", "sans-serif"] },
            Body2 = new Body2Typography { FontFamily = ["Public Sans", "-apple-system", "Segoe UI", "Roboto", "Arial", "sans-serif"] },
            Button = new ButtonTypography { FontFamily = ["Public Sans", "-apple-system", "Segoe UI", "Roboto", "Arial", "sans-serif"] },
            Caption = new CaptionTypography { FontFamily = ["Public Sans", "-apple-system", "Segoe UI", "Roboto", "Arial", "sans-serif"] },
            Overline = new OverlineTypography { FontFamily = ["Public Sans", "-apple-system", "Segoe UI", "Roboto", "Arial", "sans-serif"] },
            Subtitle1 = new Subtitle1Typography { FontFamily = ["Public Sans", "-apple-system", "Segoe UI", "Roboto", "Arial", "sans-serif"] },
            Subtitle2 = new Subtitle2Typography { FontFamily = ["Public Sans", "-apple-system", "Segoe UI", "Roboto", "Arial", "sans-serif"] },
        },
    };

    /// <summary>
    /// Las cifras grandes de las tarjetas de estadística usan Fraunces
    /// (tarea 6.0), pero no son un H1-H6 real — se aplica con la clase
    /// "compas-stat-value" en CSS propio (ver app.css) en vez de un token
    /// de MudTheme, porque no hay una variante de Typography dedicada a esto.
    /// </summary>
    public const string StatValueCssClass = "compas-stat-value";
}
