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

        // Todo lo que cuelga de un cliente, para el menú contextual de la
        // pantalla de Clientes. La relación va a través de sus expedientes:
        // documentos y facturas pertenecen a un expediente, no al cliente
        // directamente. Tres consultas en vez de una por expediente.
        app.MapGet("/api/clientes/{id:guid}/relacionados", async (Guid id, AppDbContext db, CancellationToken ct) =>
            {
                if (!await db.Clientes.AnyAsync(c => c.Id == id, ct))
                    return Results.NotFound();

                var expedientes = await db.Expedientes
                    .Where(e => e.ClienteId == id)
                    .OrderByDescending(e => e.FechaApertura)
                    .Select(e => new ExpedienteRelacionado(e.Id, e.Numero, e.Titulo, e.Estado))
                    .ToListAsync(ct);
                var numeros = expedientes.ToDictionary(e => e.Id, e => e.Numero);
                var ids = numeros.Keys.ToList();

                var documentos = await db.DocumentosAdjuntos
                    .Where(d => ids.Contains(d.ExpedienteId))
                    .OrderByDescending(d => d.FechaSubida)
                    .Select(d => new { d.Id, d.NombreArchivo, d.ExpedienteId, d.EstadoProcesamiento })
                    .ToListAsync(ct);

                var facturas = await db.Facturas
                    .Where(f => ids.Contains(f.ExpedienteId))
                    .OrderByDescending(f => f.Fecha)
                    .Select(f => new { f.Id, f.Concepto, f.Importe, f.Fecha, f.ExpedienteId })
                    .ToListAsync(ct);

                return Results.Ok(new ClienteRelacionados(
                    expedientes,
                    documentos.Select(d => new DocumentoRelacionado(
                        d.Id, d.NombreArchivo, d.ExpedienteId, numeros[d.ExpedienteId], d.EstadoProcesamiento)).ToList(),
                    facturas.Select(f => new FacturaRelacionada(
                        f.Id, f.Concepto, f.Importe, f.Fecha, f.ExpedienteId, numeros[f.ExpedienteId])).ToList()));
            })
            .RequireAuthorization();

        app.MapPost("/api/clientes", async (ClienteCreateRequest request, AppDbContext db, ICurrentTenantProvider tenant, CancellationToken ct) =>
            {
                var errores = new Dictionary<string, string[]>();

                if (string.IsNullOrWhiteSpace(request.Nombre))
                    errores["nombre"] = ["Indica el nombre o razón social del cliente."];

                // El DNI/CIF no lleva índice único en la base de datos a
                // propósito: hay clientes legítimos sin él (todavía sin
                // identificar, extranjeros en trámite), y un índice único
                // trataría todas esas cadenas vacías como duplicados. Se
                // comprueba aquí, que es donde sí se puede distinguir entre
                // "vacío" y "repetido". La consulta lleva el filtro de
                // tenant, así que solo mira dentro del propio despacho.
                if (!string.IsNullOrWhiteSpace(request.DniCif))
                {
                    var dni = request.DniCif.Trim();
                    if (await db.Clientes.AnyAsync(c => c.DniCif == dni, ct))
                        errores["dniCif"] = ["Ya hay un cliente con ese DNI/CIF en el despacho."];
                }

                if (!string.IsNullOrWhiteSpace(request.Email) && !request.Email.Contains('@'))
                    errores["email"] = ["El correo no parece válido."];

                if (errores.Count > 0)
                    return Results.ValidationProblem(errores);

                var cliente = new Cliente
                {
                    TenantId = tenant.TenantId,
                    Nombre = request.Nombre.Trim(),
                    DniCif = request.DniCif.Trim(),
                    Email = request.Email.Trim(),
                    Telefono = request.Telefono.Trim(),
                    CanalPreferido = request.CanalPreferido,
                    Domicilio = string.IsNullOrWhiteSpace(request.Domicilio) ? null : request.Domicilio.Trim(),
                    TipoCliente = request.TipoCliente
                };

                db.Clientes.Add(cliente);
                await db.SaveChangesAsync(ct);

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
                if (request.Domicilio is not null) cliente.Domicilio = request.Domicilio.Trim();
                if (request.TipoCliente is not null) cliente.TipoCliente = request.TipoCliente.Value;

                await db.SaveChangesAsync();

                return Results.Ok(cliente);
            })
            .RequireAuthorization();

        return app;
    }
}

public record ClienteCreateRequest(string Nombre, string DniCif, string Email, string Telefono, string CanalPreferido,
    string? Domicilio = null, TipoCliente TipoCliente = TipoCliente.Particular);

public record ClienteUpdateRequest(string? Nombre, string? DniCif, string? Email, string? Telefono, string? CanalPreferido,
    string? Domicilio = null, TipoCliente? TipoCliente = null);

public record ClienteRelacionados(
    IReadOnlyList<ExpedienteRelacionado> Expedientes,
    IReadOnlyList<DocumentoRelacionado> Documentos,
    IReadOnlyList<FacturaRelacionada> Facturas);

public record ExpedienteRelacionado(Guid Id, string Numero, string Titulo, EstadoExpediente Estado);

public record DocumentoRelacionado(
    Guid Id, string NombreArchivo, Guid ExpedienteId, string NumeroExpediente, EstadoProcesamientoDocumento Estado);

public record FacturaRelacionada(
    Guid Id, string Concepto, decimal Importe, DateTime Fecha, Guid ExpedienteId, string NumeroExpediente);
