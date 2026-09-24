using System.Text.Json;
using Microsoft.JSInterop;

namespace Web.Services;

/// <summary>
/// Guarda el JWT de la sesión actual (tarea 6.1).
///
/// Registrado como Scoped: en Blazor Server cada circuito (pestaña) tiene su
/// propio contenedor con ámbito, así que el token de un usuario nunca se
/// filtra a otro. Pero el circuito muere al recargar la página, así que el
/// token se replica además en sessionStorage del navegador y se restaura al
/// abrir un circuito nuevo — sin esto, cada F5 devolvía al login.
///
/// sessionStorage y no localStorage a propósito: se borra al cerrar la
/// pestaña, que es el comportamiento razonable para un token de sesión. La
/// casilla «Recordarme» del login todavía no cambia esto (haría falta el
/// login real de la tarea 3.1, con su propio refresco de token).
///
/// Alimentado hoy por POST /api/auth/dev-token y, cuando exista, por el login
/// real — ambos flujos dejan el JWT aquí sin que el resto de la aplicación
/// necesite saber cuál de los dos lo generó.
/// </summary>
public class TokenProvider(IJSRuntime js)
{
    private const string ClaveAlmacenamiento = "compas.sesion";

    private bool _restaurado;

    public string? AccessToken { get; private set; }
    public DateTime? ExpiraUtc { get; private set; }

    public event Action? CambioSesion;

    public bool HaySesionValida =>
        AccessToken is not null && (ExpiraUtc is null || ExpiraUtc > DateTime.UtcNow);

    /// <summary>
    /// Recupera la sesión de sessionStorage la primera vez que se llama en
    /// este circuito. Requiere que el JS del navegador esté disponible, así
    /// que solo debe invocarse desde código interactivo (no prerenderizado).
    /// </summary>
    public async Task RestaurarAsync()
    {
        if (_restaurado)
        {
            return;
        }

        _restaurado = true;

        try
        {
            var guardado = await js.InvokeAsync<string?>("sessionStorage.getItem", ClaveAlmacenamiento);
            if (string.IsNullOrWhiteSpace(guardado))
            {
                return;
            }

            var sesion = JsonSerializer.Deserialize<SesionPersistida>(guardado);
            if (sesion?.AccessToken is null)
            {
                return;
            }

            AccessToken = sesion.AccessToken;
            ExpiraUtc = sesion.ExpiraUtc;

            // Un token ya caducado en el almacenamiento no sirve de nada:
            // se limpia para no arrastrarlo entre recargas.
            if (!HaySesionValida)
            {
                await CerrarSesionAsync();
            }
        }
        catch (JSException)
        {
            // sessionStorage puede no estar disponible (modo privado muy
            // restrictivo, permisos del navegador): se sigue con la sesión
            // solo en memoria, que es el comportamiento anterior.
        }
        catch (JsonException)
        {
            await CerrarSesionAsync();
        }
    }

    public async Task EstablecerTokenAsync(string accessToken, DateTime? expiraUtc)
    {
        AccessToken = accessToken;
        ExpiraUtc = expiraUtc;
        _restaurado = true;

        await GuardarAsync();
        CambioSesion?.Invoke();
    }

    public async Task CerrarSesionAsync()
    {
        AccessToken = null;
        ExpiraUtc = null;

        try
        {
            await js.InvokeVoidAsync("sessionStorage.removeItem", ClaveAlmacenamiento);
        }
        catch (JSException)
        {
            // Ver comentario de RestaurarAsync.
        }

        CambioSesion?.Invoke();
    }

    /// <summary>
    /// Cierre de sesión sin tocar el navegador, para llamarlo desde sitios
    /// donde no se puede esperar a JS (por ejemplo al detectar un 401 en
    /// mitad de una llamada a la Api). El borrado del almacenamiento lo hará
    /// la siguiente interacción; el token en memoria ya queda invalidado.
    /// </summary>
    public void CerrarSesion()
    {
        AccessToken = null;
        ExpiraUtc = null;
        _ = GuardarBorradoSinEsperarAsync();
        CambioSesion?.Invoke();
    }

    private async Task GuardarAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(new SesionPersistida(AccessToken, ExpiraUtc));
            await js.InvokeVoidAsync("sessionStorage.setItem", ClaveAlmacenamiento, json);
        }
        catch (JSException)
        {
            // Ver comentario de RestaurarAsync.
        }
    }

    private async Task GuardarBorradoSinEsperarAsync()
    {
        try
        {
            await js.InvokeVoidAsync("sessionStorage.removeItem", ClaveAlmacenamiento);
        }
        catch (JSException)
        {
            // Ver comentario de RestaurarAsync.
        }
        catch (InvalidOperationException)
        {
            // El circuito puede estar ya cerrándose: no es un error real.
        }
    }

    private sealed record SesionPersistida(string? AccessToken, DateTime? ExpiraUtc);
}
