using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Infrastructure.Expedientes;
using LegalCaseManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Api.Endpoints;

public static class ExpedientesEndpoints
{
    public static WebApplication MapExpedientesEndpoints(this WebApplication app)
    {
        app.MapGet("/api/expedientes", async (AppDbContext db, CancellationToken ct) =>
                await db.Expedientes
                    .OrderByDescending(e => e.FechaApertura)
                    .ToListAsync(ct))
            .RequireAuthorization();

        // Alta de expediente desde la interfaz (botón "Nuevo expediente").
        //
        // El número NO lo manda el cliente: lo asigna el servidor con el
        // contador atómico del despacho. Si lo eligiera el navegador, dos
        // usuarios dando de alta a la vez podrían proponer el mismo, y el
        // número de expediente es identificador de cara al juzgado.
        app.MapPost("/api/expedientes", async (
                ExpedienteCreateRequest request,
                AppDbContext db,
                ICurrentTenantProvider tenant,
                NumeradorExpedientes numerador,
                CancellationToken ct) =>
            {
                var errores = new Dictionary<string, string[]>();

                if (string.IsNullOrWhiteSpace(request.Titulo))
                    errores["titulo"] = ["Indica el asunto del expediente."];

                // Se comprueban las referencias contra la base de datos en
                // lugar de confiar en el formulario: las consultas llevan el
                // filtro de tenant, así que un id de otro despacho no aparece
                // y se rechaza como inexistente.
                var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Id == request.ClienteId, ct);
                if (cliente is null)
                    errores["clienteId"] = ["El cliente indicado no existe en este despacho."];

                if (request.MateriaId is { } materiaId
                    && !await db.Materias.AnyAsync(m => m.Id == materiaId, ct))
                    errores["materiaId"] = ["La materia indicada no existe en este despacho."];

                if (request.AbogadoResponsableId is { } abogadoId
                    && !await db.Usuarios.AnyAsync(u => u.Id == abogadoId, ct))
                    errores["abogadoResponsableId"] = ["El abogado indicado no existe en este despacho."];

                if (!Enum.IsDefined(request.Estado))
                    errores["estado"] = ["Estado inicial no válido."];

                if (errores.Count > 0)
                    return Results.ValidationProblem(errores);

                // Postgres guarda las fechas como timestamptz y Npgsql exige
                // que el DateTime venga en UTC. Del formulario llega solo una
                // fecha, sin hora ni zona (Kind = Unspecified), así que se
                // marca explícitamente como UTC en vez de dejar que Npgsql
                // lance una excepción en tiempo de ejecución.
                var fechaApertura = request.FechaApertura is { } fecha
                    ? DateTime.SpecifyKind(fecha.Date, DateTimeKind.Utc)
                    : DateTime.UtcNow;

                // La serie correlativa es la del año en curso, no la del año
                // de apertura: el número identifica cuándo se registra el
                // expediente en el sistema. Si se tomara el año de apertura,
                // dar de alta un caso antiguo reiniciaría el contador del
                // despacho a ese año y rompería la numeración en curso.
                var numero = await numerador.SiguienteNumeroAsync(
                    tenant.TenantId, DateTime.UtcNow.Year, ct);

                var expediente = new Expediente
                {
                    TenantId = tenant.TenantId,
                    Numero = numero,
                    Titulo = request.Titulo.Trim(),
                    // Nombre del cliente copiado en el expediente: lo usan el
                    // listado y las plantillas de documentos sin tener que
                    // resolver la ficha completa.
                    Cliente = cliente!.Nombre,
                    ClienteId = cliente.Id,
                    MateriaId = request.MateriaId,
                    AbogadoResponsableId = request.AbogadoResponsableId,
                    FechaApertura = fechaApertura,
                    Estado = request.Estado
                };

                db.Expedientes.Add(expediente);

                if (!string.IsNullOrWhiteSpace(request.ParteContraria))
                {
                    db.PartesContrarias.Add(new ParteContraria
                    {
                        TenantId = tenant.TenantId,
                        ExpedienteId = expediente.Id,
                        Nombre = request.ParteContraria.Trim()
                    });
                }

                // El expediente y su parte contraria se guardan en el mismo
                // SaveChanges, así que o entran los dos o no entra ninguno.
                await db.SaveChangesAsync(ct);

                return Results.Created($"/api/expedientes/{expediente.Id}", expediente);
            })
            .RequireAuthorization();

        return app;
    }
}

/// <summary>
/// Datos del formulario de alta. No incluye el número: lo asigna el servidor.
/// </summary>
public record ExpedienteCreateRequest(
    string Titulo,
    Guid ClienteId,
    Guid? MateriaId = null,
    Guid? AbogadoResponsableId = null,
    string? ParteContraria = null,
    DateTime? FechaApertura = null,
    EstadoExpediente Estado = EstadoExpediente.Abierto);
