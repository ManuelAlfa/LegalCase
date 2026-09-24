using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Api.Endpoints;

public static class PlazosEndpoints
{
    public static WebApplication MapPlazosEndpoints(this WebApplication app)
    {
        app.MapGet("/api/plazos", async (AppDbContext db) =>
                await db.Plazos.OrderBy(p => p.FechaLimite).ToListAsync())
            .RequireAuthorization();

        app.MapGet("/api/plazos/{id:guid}", async (Guid id, AppDbContext db) =>
                await db.Plazos.FirstOrDefaultAsync(p => p.Id == id) is { } plazo
                    ? Results.Ok(plazo)
                    : Results.NotFound())
            .RequireAuthorization();

        app.MapGet("/api/plazos/expediente/{expedienteId:guid}", async (Guid expedienteId, AppDbContext db) =>
                await db.Plazos
                    .Where(p => p.ExpedienteId == expedienteId)
                    .OrderBy(p => p.FechaLimite)
                    .ToListAsync())
            .RequireAuthorization();

        app.MapPost("/api/plazos", async (PlazoCreateRequest request, AppDbContext db, ICurrentTenantProvider tenant) =>
            {
                if (!await db.Expedientes.AnyAsync(e => e.Id == request.ExpedienteId))
                    return Results.BadRequest("El expediente indicado no existe o no pertenece a este tenant.");

                var plazo = new Plazo
                {
                    TenantId = tenant.TenantId,
                    ExpedienteId = request.ExpedienteId,
                    Descripcion = request.Descripcion,
                    FechaLimite = request.FechaLimite,
                    Tipo = request.Tipo,
                    DiasAvisoPrevio = request.DiasAvisoPrevio
                };

                db.Plazos.Add(plazo);
                await db.SaveChangesAsync();

                return Results.Created($"/api/plazos/{plazo.Id}", plazo);
            })
            .RequireAuthorization();

        app.MapPatch("/api/plazos/{id:guid}", async (Guid id, PlazoUpdateRequest request, AppDbContext db) =>
            {
                var plazo = await db.Plazos.FirstOrDefaultAsync(p => p.Id == id);
                if (plazo is null)
                    return Results.NotFound();

                if (request.Descripcion is not null) plazo.Descripcion = request.Descripcion;
                if (request.FechaLimite is not null) plazo.FechaLimite = request.FechaLimite.Value;
                if (request.Tipo is not null) plazo.Tipo = request.Tipo;
                if (request.DiasAvisoPrevio is not null) plazo.DiasAvisoPrevio = request.DiasAvisoPrevio;

                await db.SaveChangesAsync();

                return Results.Ok(plazo);
            })
            .RequireAuthorization();

        return app;
    }
}

public record PlazoCreateRequest(Guid ExpedienteId, string Descripcion, DateTime FechaLimite, string Tipo, List<int> DiasAvisoPrevio);

public record PlazoUpdateRequest(string? Descripcion, DateTime? FechaLimite, string? Tipo, List<int>? DiasAvisoPrevio);
