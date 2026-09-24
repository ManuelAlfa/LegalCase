using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Api.Endpoints;

public static class ProvisionesFondosEndpoints
{
    public static WebApplication MapProvisionesFondosEndpoints(this WebApplication app)
    {
        app.MapGet("/api/provisiones-de-fondos", async (AppDbContext db) =>
                await db.ProvisionesDeFondos.OrderByDescending(p => p.FechaSolicitud).ToListAsync())
            .RequireAuthorization();

        app.MapGet("/api/provisiones-de-fondos/{id:guid}", async (Guid id, AppDbContext db) =>
                await db.ProvisionesDeFondos.FirstOrDefaultAsync(p => p.Id == id) is { } provision
                    ? Results.Ok(provision)
                    : Results.NotFound())
            .RequireAuthorization();

        app.MapPost("/api/provisiones-de-fondos", async (ProvisionDeFondosCreateRequest request, AppDbContext db, ICurrentTenantProvider tenant) =>
            {
                if (!await db.Expedientes.AnyAsync(e => e.Id == request.ExpedienteId))
                    return Results.BadRequest("El expediente indicado no existe o no pertenece a este tenant.");

                var provision = new ProvisionDeFondos
                {
                    TenantId = tenant.TenantId,
                    ExpedienteId = request.ExpedienteId,
                    Importe = request.Importe,
                    FechaSolicitud = request.FechaSolicitud,
                    Aplicada = request.Aplicada
                };

                db.ProvisionesDeFondos.Add(provision);
                await db.SaveChangesAsync();

                return Results.Created($"/api/provisiones-de-fondos/{provision.Id}", provision);
            })
            .RequireAuthorization();

        app.MapPatch("/api/provisiones-de-fondos/{id:guid}", async (Guid id, ProvisionDeFondosUpdateRequest request, AppDbContext db) =>
            {
                var provision = await db.ProvisionesDeFondos.FirstOrDefaultAsync(p => p.Id == id);
                if (provision is null)
                    return Results.NotFound();

                if (request.Importe is not null) provision.Importe = request.Importe.Value;
                if (request.FechaSolicitud is not null) provision.FechaSolicitud = request.FechaSolicitud.Value;
                if (request.Aplicada is not null) provision.Aplicada = request.Aplicada.Value;

                await db.SaveChangesAsync();

                return Results.Ok(provision);
            })
            .RequireAuthorization();

        return app;
    }
}

public record ProvisionDeFondosCreateRequest(Guid ExpedienteId, decimal Importe, DateTime FechaSolicitud, bool Aplicada);

public record ProvisionDeFondosUpdateRequest(decimal? Importe, DateTime? FechaSolicitud, bool? Aplicada);
