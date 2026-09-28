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

        // Listado para la pantalla de OCR / Foliado. Se separa del listado
        // plano de arriba porque añade dos datos que no están en la fila del
        // documento y hay que calcular sobre sus fragmentos: cuántas páginas
        // tiene y con qué confianza media se reconoció su texto. Va en una
        // sola consulta agrupada, no una por documento.
        //
        // La ruta no choca con la de {id:guid} de abajo: "resumen" no es un
        // GUID y la restricción de ruta lo descarta.
        app.MapGet("/api/documentos-adjuntos/resumen", async (AppDbContext db, CancellationToken ct) =>
            {
                var porDocumento = await db.FragmentosDocumento
                    .GroupBy(f => f.DocumentoAdjuntoId)
                    .Select(g => new
                    {
                        DocumentoId = g.Key,
                        Paginas = g.Select(f => f.Pagina).Distinct().Count(),
                        ConfianzaMedia = g.Average(f => f.ConfianzaOcr)
                    })
                    .ToDictionaryAsync(x => x.DocumentoId, ct);

                var documentos = await db.DocumentosAdjuntos
                    .OrderByDescending(d => d.FechaSubida)
                    .ToListAsync(ct);

                return documentos.Select(d =>
                {
                    porDocumento.TryGetValue(d.Id, out var agregado);
                    return new DocumentoResumenResponse(
                        d.Id,
                        d.ExpedienteId,
                        d.NombreArchivo,
                        d.ContentType,
                        d.TamanoBytes,
                        d.TipoDocumento,
                        d.TipoDocumentoConfianza,
                        d.FechaSubida,
                        d.EstadoProcesamiento,
                        d.MensajeError,
                        d.FolioInicio,
                        d.FolioFin,
                        agregado?.Paginas,
                        agregado?.ConfianzaMedia,
                        d.RutaAlmacenamientoProcesado is not null);
                });
            })
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

                // Por defecto se entrega el PDF procesado (sello de folio y
                // capa de texto buscable), que es el que le sirve al
                // abogado. El original se pide expresamente por la ruta de
                // abajo: se conserva intacto, nunca se sobrescribe.
                var ruta = documento.RutaAlmacenamientoProcesado ?? documento.RutaAlmacenamiento;
                var nombre = documento.RutaAlmacenamientoProcesado is null
                    ? documento.NombreArchivo
                    : NombresDocumento.Procesado(documento.NombreArchivo);

                var contenido = await storage.DescargarAsync(ruta);

                return Results.File(contenido, documento.ContentType, nombre);
            })
            .RequireAuthorization();

        // El escaneo tal y como lo subió el usuario, sin sello ni capa de
        // texto. En un despacho puede ser la pieza con valor probatorio, así
        // que tiene que poder recuperarse siempre.
        app.MapGet("/api/documentos-adjuntos/{id:guid}/descargar/original", async (Guid id, AppDbContext db, IDocumentStorage storage) =>
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


/// <summary>
/// Un documento tal y como lo necesita la pantalla de OCR / Foliado:
/// la fila del documento más lo que hay que calcular sobre sus fragmentos.
/// Paginas y ConfianzaOcrMedia son null mientras no se ha procesado.
/// </summary>
public static class NombresDocumento
{
    /// <summary>"Demanda.pdf" -> "Demanda_procesado.pdf".</summary>
    public static string Procesado(string nombreArchivo)
    {
        var punto = nombreArchivo.LastIndexOf('.');
        return punto > 0
            ? $"{nombreArchivo[..punto]}_procesado{nombreArchivo[punto..]}"
            : $"{nombreArchivo}_procesado";
    }
}

public record DocumentoResumenResponse(
    Guid Id,
    Guid ExpedienteId,
    string NombreArchivo,
    string ContentType,
    long TamanoBytes,
    string TipoDocumento,
    double? TipoDocumentoConfianza,
    DateTime FechaSubida,
    EstadoProcesamientoDocumento EstadoProcesamiento,
    string? MensajeError,
    int? FolioInicio,
    int? FolioFin,
    int? Paginas,
    double? ConfianzaOcrMedia,
    bool TienePdfProcesado);
