using LegalCaseManagement.Infrastructure.Auditing;
using LegalCaseManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Api.Endpoints;

// Solo lectura: AuditLog se escribe desde el propio flujo que audita cada
// acción (todavía no implementado), no desde un endpoint de escritura genérico.
public static class AuditLogEndpoints
{
    public static WebApplication MapAuditLogEndpoints(this WebApplication app)
    {
        app.MapGet("/api/audit-logs/expediente/{expedienteId:guid}", async (Guid expedienteId, AppDbContext db) =>
                await db.AuditLogs
                    .Where(a => a.ExpedienteId == expedienteId)
                    .OrderBy(a => a.FechaUtc)
                    .ToListAsync())
            .RequireAuthorization();

        app.MapGet("/api/audit-logs/verificar-integridad", async (AppDbContext db) =>
            {
                // Orden cronológico: es el mismo orden en que se calculó cada
                // eslabón al insertar (ver AuditLogHasher.ComputeHashActualAsync).
                var registros = await db.AuditLogs
                    .OrderBy(a => a.FechaUtc)
                    .ToListAsync();

                var hashAnterior = string.Empty;
                foreach (var registro in registros)
                {
                    if (registro.HashAnterior != hashAnterior)
                        return Results.Ok(new AuditLogIntegridadResponse(false, registro.Id, "El HashAnterior almacenado no coincide con el HashActual de la fila previa."));

                    var hashEsperado = AuditLogHasher.ComputeHash(registro, hashAnterior);
                    if (registro.HashActual != hashEsperado)
                        return Results.Ok(new AuditLogIntegridadResponse(false, registro.Id, "El HashActual almacenado no coincide con el recalculado a partir de los campos de negocio."));

                    hashAnterior = registro.HashActual;
                }

                return Results.Ok(new AuditLogIntegridadResponse(true, null, null) { FilasVerificadas = registros.Count });
            })
            .RequireAuthorization();

        return app;
    }
}

public record AuditLogIntegridadResponse(bool Integra, Guid? PrimeraFilaRota, string? Motivo)
{
    public int FilasVerificadas { get; init; }
}
