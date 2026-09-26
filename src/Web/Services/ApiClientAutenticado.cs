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

    /// <summary>
    /// POST de un formulario. Distingue tres desenlaces, porque la pantalla
    /// tiene que reaccionar distinto a cada uno: creado, rechazado por
    /// validación (400, con el detalle por campo) o error de transporte.
    /// </summary>
    protected async Task<ResultadoApi<T>> CrearAsync<T>(string ruta, object cuerpo, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta)
        {
            Content = JsonContent.Create(cuerpo)
        };

        if (tokenProvider.AccessToken is { } token)
        {
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var respuesta = await http.SendAsync(peticion, ct);

        if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenProvider.CerrarSesion();
            return ResultadoApi<T>.DeError("La sesión ha caducado.");
        }

        // 400 con ProblemDetails: el servidor explica qué campo está mal y
        // ese detalle se muestra junto al campo correspondiente, en vez de
        // un mensaje genérico que obligue al usuario a adivinar.
        if (respuesta.StatusCode == HttpStatusCode.BadRequest)
        {
            var problema = await respuesta.Content
                .ReadFromJsonAsync<ProblemaValidacion>(cancellationToken: ct);
            return ResultadoApi<T>.DeValidacion(problema?.Errors ?? []);
        }

        if (!respuesta.IsSuccessStatusCode)
        {
            return ResultadoApi<T>.DeError(
                $"El servidor respondió {(int)respuesta.StatusCode}. Vuelve a intentarlo.");
        }

        var creado = await respuesta.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        return creado is null
            ? ResultadoApi<T>.DeError("El servidor no devolvió el elemento creado.")
            : ResultadoApi<T>.DeExito(creado);
    }

    private sealed record ProblemaValidacion(Dictionary<string, string[]> Errors);
}

/// <summary>Desenlace de una llamada de escritura a la Api.</summary>
public sealed class ResultadoApi<T>
{
    private ResultadoApi() { }

    public T? Valor { get; private init; }

    /// <summary>Errores por campo, con la misma clave que usa el formulario.</summary>
    public IReadOnlyDictionary<string, string[]> ErroresPorCampo { get; private init; }
        = new Dictionary<string, string[]>();

    /// <summary>Mensaje general cuando el fallo no es de un campo concreto.</summary>
    public string? ErrorGeneral { get; private init; }

    public bool EsCorrecto => Valor is not null;

    public static ResultadoApi<T> DeExito(T valor) => new() { Valor = valor };

    public static ResultadoApi<T> DeValidacion(Dictionary<string, string[]> errores) =>
        new() { ErroresPorCampo = errores };

    public static ResultadoApi<T> DeError(string mensaje) => new() { ErrorGeneral = mensaje };
}
