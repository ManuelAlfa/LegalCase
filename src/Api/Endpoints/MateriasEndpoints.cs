using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Api.Endpoints;

public static class MateriasEndpoints
{
    // Catálogo por tenant: solo listado y alta. Sin PATCH ni GET por id
    // porque, a diferencia del resto de entidades, no hay caso de uso todavía
    // para editar o consultar una materia suelta fuera del listado completo.
    public static WebApplication MapMateriasEndpoints(this WebApplication app)
    {
        app.MapGet("/api/materias", async (AppDbContext db) =>
                await db.Materias.OrderBy(m => m.Nombre).ToListAsync())
            .RequireAuthorization();

        app.MapPost("/api/materias", async (MateriaCreateRequest request, AppDbContext db, ICurrentTenantProvider tenant) =>
            {
                var materia = new Materia
                {
                    TenantId = tenant.TenantId,
                    Nombre = request.Nombre
                };

                db.Materias.Add(materia);
                await db.SaveChangesAsync();

                return Results.Created($"/api/materias/{materia.Id}", materia);
            })
            .RequireAuthorization();

        return app;
    }
}

public record MateriaCreateRequest(string Nombre);
