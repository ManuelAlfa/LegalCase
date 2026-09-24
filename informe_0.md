# Informe 0 — Estado del proyecto (src/Api, Program.cs, migraciones, base de datos)

Fecha: 2026-07-29

## 1) Contenido de `src/Api`

Archivos fuente (excluyendo `bin`/`obj`):

- `Api.csproj` — SDK Web, net8.0, referencia al proyecto `Infrastructure`, paquetes `Microsoft.EntityFrameworkCore.Design` y `Swashbuckle.AspNetCore`.
- `Program.cs`
- `Middleware/HeaderTenantProvider.cs` — única carpeta de tipo middleware/auth existente; **no hay carpeta `Auth`**.
- `Properties/launchSettings.json`
- `appsettings.json`
- `appsettings.Development.json`

`HeaderTenantProvider` lee el tenant del header `X-Tenant-Id` (o `Guid.Empty` si falta o es inválido). El propio código lo marca con un comentario explícito: es un **placeholder de desarrollo**, sin autenticación real — el tenant lo decide libremente el cliente, no un claim JWT. Está pendiente sustituirlo por autenticación real (JWT + claim de tenant).

## 2) `src/Api/Program.cs` (contenido íntegro)

```csharp
using LegalCaseManagement.Api.Middleware;
using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenantProvider, HeaderTenantProvider>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/api/expedientes", async (AppDbContext db) =>
    await db.Expedientes
        .OrderByDescending(e => e.FechaApertura)
        .ToListAsync());

app.MapPost("/api/expedientes", async (AppDbContext db, ICurrentTenantProvider tenant, ExpedienteCreateRequest request) =>
{
    var expediente = new Expediente
    {
        TenantId = tenant.TenantId,
        Titulo = request.Titulo,
        Cliente = request.Cliente
    };

    db.Expedientes.Add(expediente);
    await db.SaveChangesAsync();

    return Results.Created($"/api/expedientes/{expediente.Id}", expediente);
});

app.Run();

public record ExpedienteCreateRequest(string Titulo, string Cliente);
```

Observaciones:
- Dos endpoints minimal API: `GET /api/expedientes` (lista todos los expedientes, sin filtrar por tenant) y `POST /api/expedientes` (crea un expediente asignando `TenantId` del `ICurrentTenantProvider` actual).
- No hay ningún middleware de autenticación/autorización registrado (`UseAuthentication`/`UseAuthorization` ausentes).

## 3) `src/Infrastructure/Migrations`

La carpeta existe y contiene una única migración:

- `20260725223408_InitialCreate.cs` (+ `.Designer.cs`) — crea las tablas.
- `AppDbContextModelSnapshot.cs`

## 4) Base de datos de desarrollo

El contenedor `legalcase-postgres` (definido en `docker-compose.yml`, Postgres 16) está **levantado** (`docker ps` lo muestra `Up`). Se conectó con `psql` y se confirmó:

- Tablas presentes: `__EFMigrationsHistory`, `expedientes`, `tenants`.
- Migración `20260725223408_InitialCreate` ya aplicada.
- **No está vacía**: contiene 1 fila en `expedientes` (tenant `11111111-1111-1111-1111-111111111111`, "Demanda X" / "Acme SL") y 0 filas en `tenants`.

## Conclusión

No se realizó ningún cambio en el proyecto; este documento es solo un informe de reconocimiento. Pendiente de indicaciones para continuar (por ejemplo, implementar autenticación real para reemplazar `HeaderTenantProvider`).
