using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Api.Endpoints;

public static class PartesContrariasEndpoints
{
    public static WebApplication MapPartesContrariasEndpoints(this WebApplication app)
    {
        app.MapGet("/api/partes-contrarias", async (AppDbContext db) =>
                await db.PartesContrarias.OrderBy(p => p.Nombre).ToListAsync())
            .RequireAuthorization();

        app.MapGet("/api/partes-contrarias/{id:guid}", async (Guid id, AppDbContext db) =>
                await db.PartesContrarias.FirstOrDefaultAsync(p => p.Id == id) is { } parte
                    ? Results.Ok(parte)
                    : Results.NotFound())
            .RequireAuthorization();

        app.MapPost("/api/partes-contrarias", async (ParteContrariaCreateRequest request, AppDbContext db, ICurrentTenantProvider tenant) =>
            {
                if (!await db.Expedientes.AnyAsync(e => e.Id == request.ExpedienteId))
                    return Results.BadRequest("El expediente indicado no existe o no pertenece a este tenant.");

                var parte = new ParteContraria
                {
                    TenantId = tenant.TenantId,
                    ExpedienteId = request.ExpedienteId,
                    Nombre = request.Nombre,
                    Tipo = request.Tipo,
                    RepresentanteLegal = request.RepresentanteLegal
                };

                db.PartesContrarias.Add(parte);
                await db.SaveChangesAsync();

                return Results.Created($"/api/partes-contrarias/{parte.Id}", parte);
            })
            .RequireAuthorization();

        app.MapPatch("/api/partes-contrarias/{id:guid}", async (Guid id, ParteContrariaUpdateRequest request, AppDbContext db) =>
            {
                var parte = await db.PartesContrarias.FirstOrDefaultAsync(p => p.Id == id);
                if (parte is null)
                    return Results.NotFound();

                if (request.Nombre is not null) parte.Nombre = request.Nombre;
                if (request.Tipo is not null) parte.Tipo = request.Tipo;
                if (request.RepresentanteLegal is not null) parte.RepresentanteLegal = request.RepresentanteLegal;

                await db.SaveChangesAsync();

                return Results.Ok(parte);
            })
            .RequireAuthorization();

        return app;
    }
}

public record ParteContrariaCreateRequest(Guid ExpedienteId, string Nombre, string Tipo, string RepresentanteLegal);

public record ParteContrariaUpdateRequest(string? Nombre, string? Tipo, string? RepresentanteLegal);
