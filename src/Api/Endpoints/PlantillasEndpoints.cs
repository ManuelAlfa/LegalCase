using Amazon.S3;
using LegalCaseManagement.Infrastructure.Persistence;
using LegalCaseManagement.Infrastructure.Plantillas;
using LegalCaseManagement.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Api.Endpoints;

public static class PlantillasEndpoints
{
    private const string ContentTypeDocx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public static WebApplication MapPlantillasEndpoints(this WebApplication app)
    {
        // Sube (o reemplaza) una plantilla .docx del despacho. `clave` es un
        // identificador libre elegido por quien sube la plantilla (p.ej.
        // "poder-pleitos"), usado luego para generar documentos a partir de
        // ella — ver el endpoint de generación más abajo.
        app.MapPost("/api/plantillas/{clave}", async (
                string clave,
                IFormFile file,
                ICurrentTenantProvider tenant,
                IDocumentStorage storage) =>
            {
                if (file.Length == 0)
                    return Results.BadRequest("El archivo está vacío.");

                var ruta = RutaPlantilla(tenant.TenantId, clave);
                await using (var stream = file.OpenReadStream())
                {
                    await storage.SubirAsync(ruta, stream, ContentTypeDocx);
                }

                return Results.Ok(new { clave, ruta });
            })
            .DisableAntiforgery()
            .RequireAuthorization();

        // Genera un .docx a partir de la plantilla `clave` y los datos del
        // expediente indicado (más su Cliente/Materia enlazados, si los
        // tiene) y lo devuelve para descarga directa — no se persiste en
        // almacenamiento de objetos, es un documento de salida bajo demanda.
        app.MapPost("/api/expedientes/{expedienteId:guid}/documentos/generar/{clave}", async (
                Guid expedienteId,
                string clave,
                AppDbContext db,
                ICurrentTenantProvider tenant,
                IDocumentStorage storage,
                IFusionadorDocumentos fusionador) =>
            {
                var expediente = await db.Expedientes.FirstOrDefaultAsync(e => e.Id == expedienteId);
                if (expediente is null)
                    return Results.BadRequest("El expediente indicado no existe o no pertenece a este tenant.");

                var cliente = expediente.ClienteId is { } clienteId
                    ? await db.Clientes.FirstOrDefaultAsync(c => c.Id == clienteId)
                    : null;
                var materia = expediente.MateriaId is { } materiaId
                    ? await db.Materias.FirstOrDefaultAsync(m => m.Id == materiaId)
                    : null;

                byte[] plantilla;
                try
                {
                    plantilla = await storage.DescargarAsync(RutaPlantilla(tenant.TenantId, clave));
                }
                catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return Results.NotFound($"No existe la plantilla '{clave}'.");
                }

                var valores = ValoresPlantilla.Construir(expediente, cliente, materia);
                var resultado = fusionador.Fusionar(plantilla, valores);

                var nombreArchivo = $"{clave}-{expediente.Titulo}.docx";
                return Results.File(resultado, ContentTypeDocx, nombreArchivo);
            })
            .RequireAuthorization();

        return app;
    }

    private static string RutaPlantilla(Guid tenantId, string clave) => $"{tenantId}/plantillas/{clave}.docx";
}
