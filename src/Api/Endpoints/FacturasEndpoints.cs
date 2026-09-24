using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Api.Endpoints;

public static class FacturasEndpoints
{
    public static WebApplication MapFacturasEndpoints(this WebApplication app)
    {
        app.MapGet("/api/facturas", async (AppDbContext db) =>
                await db.Facturas.OrderByDescending(f => f.Fecha).ToListAsync())
            .RequireAuthorization();

        app.MapGet("/api/facturas/{id:guid}", async (Guid id, AppDbContext db) =>
                await db.Facturas.FirstOrDefaultAsync(f => f.Id == id) is { } factura
                    ? Results.Ok(factura)
                    : Results.NotFound())
            .RequireAuthorization();

        app.MapPost("/api/facturas", async (FacturaCreateRequest request, AppDbContext db, ICurrentTenantProvider tenant) =>
            {
                if (!await db.Expedientes.AnyAsync(e => e.Id == request.ExpedienteId))
                    return Results.BadRequest("El expediente indicado no existe o no pertenece a este tenant.");

                var factura = new Factura
                {
                    TenantId = tenant.TenantId,
                    ExpedienteId = request.ExpedienteId,
                    Concepto = request.Concepto,
                    Importe = request.Importe,
                    Fecha = request.Fecha,
                    Modo = request.Modo
                };

                db.Facturas.Add(factura);
                await db.SaveChangesAsync();

                return Results.Created($"/api/facturas/{factura.Id}", factura);
            })
            .RequireAuthorization();

        app.MapPatch("/api/facturas/{id:guid}", async (Guid id, FacturaUpdateRequest request, AppDbContext db) =>
            {
                var factura = await db.Facturas.FirstOrDefaultAsync(f => f.Id == id);
                if (factura is null)
                    return Results.NotFound();

                if (request.Concepto is not null) factura.Concepto = request.Concepto;
                if (request.Importe is not null) factura.Importe = request.Importe.Value;
                if (request.Fecha is not null) factura.Fecha = request.Fecha.Value;
                if (request.Modo is not null) factura.Modo = request.Modo.Value;

                await db.SaveChangesAsync();

                return Results.Ok(factura);
            })
            .RequireAuthorization();

        return app;
    }
}

public record FacturaCreateRequest(Guid ExpedienteId, string Concepto, decimal Importe, DateTime Fecha, ModoFactura Modo);

public record FacturaUpdateRequest(string? Concepto, decimal? Importe, DateTime? Fecha, ModoFactura? Modo);
