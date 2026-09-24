using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Api.Endpoints;

public static class ClientesEndpoints
{
    public static WebApplication MapClientesEndpoints(this WebApplication app)
    {
        app.MapGet("/api/clientes", async (AppDbContext db) =>
                await db.Clientes.OrderBy(c => c.Nombre).ToListAsync())
            .RequireAuthorization();

        app.MapGet("/api/clientes/{id:guid}", async (Guid id, AppDbContext db) =>
                await db.Clientes.FirstOrDefaultAsync(c => c.Id == id) is { } cliente
                    ? Results.Ok(cliente)
                    : Results.NotFound())
            .RequireAuthorization();

        app.MapPost("/api/clientes", async (ClienteCreateRequest request, AppDbContext db, ICurrentTenantProvider tenant) =>
            {
                var cliente = new Cliente
                {
                    TenantId = tenant.TenantId,
                    Nombre = request.Nombre,
                    DniCif = request.DniCif,
                    Email = request.Email,
                    Telefono = request.Telefono,
                    CanalPreferido = request.CanalPreferido
                };

                db.Clientes.Add(cliente);
                await db.SaveChangesAsync();

                return Results.Created($"/api/clientes/{cliente.Id}", cliente);
            })
            .RequireAuthorization();

        app.MapPatch("/api/clientes/{id:guid}", async (Guid id, ClienteUpdateRequest request, AppDbContext db) =>
            {
                var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Id == id);
                if (cliente is null)
                    return Results.NotFound();

                if (request.Nombre is not null) cliente.Nombre = request.Nombre;
                if (request.DniCif is not null) cliente.DniCif = request.DniCif;
                if (request.Email is not null) cliente.Email = request.Email;
                if (request.Telefono is not null) cliente.Telefono = request.Telefono;
                if (request.CanalPreferido is not null) cliente.CanalPreferido = request.CanalPreferido;

                await db.SaveChangesAsync();

                return Results.Ok(cliente);
            })
            .RequireAuthorization();

        return app;
    }
}

public record ClienteCreateRequest(string Nombre, string DniCif, string Email, string Telefono, string CanalPreferido);

public record ClienteUpdateRequest(string? Nombre, string? DniCif, string? Email, string? Telefono, string? CanalPreferido);
