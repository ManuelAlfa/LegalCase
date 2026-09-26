using MudBlazor.Services;
using Web.Components;
using Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

var apiBaseUrl = builder.Configuration["ApiBaseUrl"]
    ?? throw new InvalidOperationException("Falta la configuración 'ApiBaseUrl'.");

// TokenProvider en memoria (tarea 6.1): Scoped porque en Blazor Server cada
// circuito de usuario tiene su propio contenedor de servicios con ámbito.
builder.Services.AddScoped<TokenProvider>();

// El JWT se añade dentro de cada cliente tipado (constructor), no vía
// DelegatingHandler: los handlers de IHttpClientFactory se resuelven en un
// ámbito de DI propio, distinto del circuito de Blazor Server, así que un
// TokenProvider Scoped inyectado ahí no era el mismo que el de la pantalla
// de login (bug real encontrado 2026-09-22, causaba 401 en toda llamada
// autenticada) — ver el comentario de ExpedientesApiClient.
builder.Services.AddHttpClient<AuthApiClient>(c => c.BaseAddress = new Uri(apiBaseUrl));
builder.Services.AddHttpClient<ExpedientesApiClient>(c => c.BaseAddress = new Uri(apiBaseUrl));
builder.Services.AddHttpClient<PlazosApiClient>(c => c.BaseAddress = new Uri(apiBaseUrl));
builder.Services.AddHttpClient<MateriasApiClient>(c => c.BaseAddress = new Uri(apiBaseUrl));
builder.Services.AddHttpClient<ClientesApiClient>(c => c.BaseAddress = new Uri(apiBaseUrl));
builder.Services.AddHttpClient<UsuariosApiClient>(c => c.BaseAddress = new Uri(apiBaseUrl));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
