using LegalCaseManagement.Contracts;
using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Infrastructure.Persistence;
using LegalCaseManagement.Infrastructure.Storage;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Api.Endpoints;

public static class DocumentosAdjuntosEndpoints
{
    public static WebApplication MapDocumentosAdjuntosEndpoints(this WebApplication app)
    {
        // Subida real del fichero (Fase 2.4): guarda el binario en
        // almacenamiento de objetos, crea el DocumentoAdjunto en Procesando y
        // publica DocumentoSubido para que DocumentoSubidoConsumer (Worker)
        // haga OCR/extracción de texto, fragmentación y embeddings en
        // background — la petición HTTP no espera a que eso termine.
        app.MapPost("/api/expedientes/{expedienteId:guid}/documentos", async (
                Guid expedienteId,
                IFormFile file,
                AppDbContext db,
                ICurrentTenantProvider tenant,
                IDocumentStorage storage,
                IPublishEndpoint publishEndpoint) =>
            {
                if (!await db.Expedientes.AnyAsync(e => e.Id == expedienteId))
                    return Results.BadRequest("El expediente indicado no existe o no pertenece a este tenant.");

                if (file.Length == 0)
                    return Results.BadRequest("El archivo está vacío.");

                var documento = new DocumentoAdjunto
                {
                    TenantId = tenant.TenantId,
                    ExpedienteId = expedienteId,
                    NombreArchivo = file.FileName,
                    ContentType = file.ContentType,
                    TamanoBytes = file.Length,
                    TipoDocumento = string.Empty,
                    EstadoProcesamiento = EstadoProcesamientoDocumento.Procesando
                };
                documento.RutaAlmacenamiento = $"{documento.TenantId}/{documento.ExpedienteId}/{documento.Id}/{documento.NombreArchivo}";

                // Se sube antes de guardar la fila en Postgres: si la subida
                // falla, no queremos un DocumentoAdjunto apuntando a una clave
                // que no existe en el almacenamiento de objetos.
                await using (var stream = file.OpenReadStream())
                {
                    await storage.SubirAsync(documento.RutaAlmacenamiento, stream, documento.ContentType);
                }

                db.DocumentosAdjuntos.Add(documento);
                await db.SaveChangesAsync();

                await publishEndpoint.Publish(new DocumentoSubido(documento.Id, documento.TenantId, documento.RutaAlmacenamiento));

                return Results.Created($"/api/documentos-adjuntos/{documento.Id}", documento);
            })
            .DisableAntiforgery()
            .RequireAuthorization();

        app.MapGet("/api/documentos-adjuntos", async (AppDbContext db) =>
                await db.DocumentosAdjuntos.OrderByDescending(d => d.FechaSubida).ToListAsync())
            .RequireAuthorization();

        app.MapGet("/api/documentos-adjuntos/{id:guid}", async (Guid id, AppDbContext db) =>
                await db.DocumentosAdjuntos.FirstOrDefaultAsync(d => d.Id == id) is { } documento
                    ? Results.Ok(documento)
                    : Results.NotFound())
            .RequireAuthorization();

        // Texto reconocido (OCR o extracción directa) del documento, en el
        // orden en que aparece en el propio documento — mientras no exista
        // la capa de texto incrustada en el PDF (ver memoria de proyecto
        // "pdf-texto-buscable-pendiente"), esta es la única forma de
        // comprobar qué texto se reconoció de verdad sin acceder a la BBDD
        // directamente.
        app.MapGet("/api/documentos-adjuntos/{id:guid}/fragmentos", async (Guid id, AppDbContext db) =>
                await db.DocumentosAdjuntos.AnyAsync(d => d.Id == id)
                    ? Results.Ok(await db.FragmentosDocumento
                        .Where(f => f.DocumentoAdjuntoId == id)
                        .OrderBy(f => f.Pagina).ThenBy(f => f.Parrafo)
                        .Select(f => new { f.Pagina, f.Parrafo, f.TextoFragmento, f.ConfianzaOcr })
                        .ToListAsync())
                    : Results.NotFound())
            .RequireAuthorization();

        // Descarga del PDF ya procesado (sello de folio + capa de texto OCR
        // seleccionable cuando aplica, ver FoliadorService) para que el
        // usuario haga lo que quiera con él localmente.
        app.MapGet("/api/documentos-adjuntos/{id:guid}/descargar", async (Guid id, AppDbContext db, IDocumentStorage storage) =>
            {
                var documento = await db.DocumentosAdjuntos.FirstOrDefaultAsync(d => d.Id == id);
                if (documento is null)
                    return Results.NotFound();

                var contenido = await storage.DescargarAsync(documento.RutaAlmacenamiento);

                return Results.File(contenido, documento.ContentType, documento.NombreArchivo);
            })
            .RequireAuthorization();

        app.MapPost("/api/documentos-adjuntos", async (DocumentoAdjuntoCreateRequest request, AppDbContext db, ICurrentTenantProvider tenant) =>
            {
                if (!await db.Expedientes.AnyAsync(e => e.Id == request.ExpedienteId))
                    return Results.BadRequest("El expediente indicado no existe o no pertenece a este tenant.");

                var documento = new DocumentoAdjunto
                {
                    TenantId = tenant.TenantId,
                    ExpedienteId = request.ExpedienteId,
                    NombreArchivo = request.NombreArchivo,
                    RutaAlmacenamiento = request.RutaAlmacenamiento,
                    ContentType = request.ContentType,
                    TamanoBytes = request.TamanoBytes,
                    TipoDocumento = request.TipoDocumento
                };

                db.DocumentosAdjuntos.Add(documento);
                await db.SaveChangesAsync();

                return Results.Created($"/api/documentos-adjuntos/{documento.Id}", documento);
            })
            .RequireAuthorization();

        app.MapPatch("/api/documentos-adjuntos/{id:guid}", async (Guid id, DocumentoAdjuntoUpdateRequest request, AppDbContext db) =>
            {
                var documento = await db.DocumentosAdjuntos.FirstOrDefaultAsync(d => d.Id == id);
                if (documento is null)
                    return Results.NotFound();

                if (request.TipoDocumento is not null) documento.TipoDocumento = request.TipoDocumento;
                if (request.EstadoProcesamiento is not null) documento.EstadoProcesamiento = request.EstadoProcesamiento.Value;
                if (request.FechaProcesado is not null) documento.FechaProcesado = request.FechaProcesado;

                await db.SaveChangesAsync();

                return Results.Ok(documento);
            })
            .RequireAuthorization();

        return app;
    }
}

public record DocumentoAdjuntoCreateRequest(
    Guid ExpedienteId,
    string NombreArchivo,
    string RutaAlmacenamiento,
    string ContentType,
    long TamanoBytes,
    string TipoDocumento);

public record DocumentoAdjuntoUpdateRequest(
    string? TipoDocumento,
    EstadoProcesamientoDocumento? EstadoProcesamiento,
    DateTime? FechaProcesado);
