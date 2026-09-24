using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Web.Services;

/// <summary>
/// Base común de los clientes tipados que llaman a endpoints protegidos de
/// la Api (tarea 6.1). Centraliza aquí el envío del JWT para no repetirlo en
/// cada cliente.
///
/// El token se añade POR PETICIÓN (HttpRequestMessage) y no en
/// HttpClient.DefaultRequestHeaders: el HttpClient de un cliente tipado se
/// reutiliza, así que dejar la cabecera fijada en el objeto arrastraría el
/// token de una sesión ya cerrada a peticiones posteriores.
///
/// Tampoco se usa un DelegatingHandler, que sería lo habitual fuera de
/// Blazor: los handlers que crea IHttpClientFactory se resuelven en un
/// ámbito de inyección de dependencias propio, distinto del circuito de
/// Blazor Server, así que el TokenProvider (Scoped) inyectado ahí NO es la
/// misma instancia que actualiza la pantalla de login — el token nunca
/// llegaba a la cabecera y toda llamada autenticada respondía 401 (bug real
/// encontrado y corregido el 2026-09-22). El constructor de un cliente
/// tipado, en cambio, sí se resuelve en el ámbito correcto del circuito.
/// </summary>
public abstract class ApiClientAutenticado(HttpClient http, TokenProvider tokenProvider)
{
    protected async Task<List<T>> ObtenerListaAsync<T>(string ruta, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);

        if (tokenProvider.AccessToken is { } token)
        {
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var respuesta = await http.SendAsync(peticion, ct);

        // 401 = el token ya no vale (caducado a los AccessTokenMinutes, o
        // revocado). Cerrar la sesión aquí hace que el layout redirija al
        // login, en vez de dejar al usuario ante un error que no puede
        // resolver desde la propia pantalla.
        if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenProvider.CerrarSesion();
            return [];
        }

        respuesta.EnsureSuccessStatusCode();

        return await respuesta.Content.ReadFromJsonAsync<List<T>>(cancellationToken: ct) ?? [];
    }
}
