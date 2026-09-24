using LegalCaseManagement.Contracts;
using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Domain.Foliado;
using LegalCaseManagement.Infrastructure.Classification;
using LegalCaseManagement.Infrastructure.Documents;
using LegalCaseManagement.Infrastructure.Embeddings;
using LegalCaseManagement.Infrastructure.Foliado;
using LegalCaseManagement.Infrastructure.Ocr;
using LegalCaseManagement.Infrastructure.Persistence;
using LegalCaseManagement.Infrastructure.Storage;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace LegalCaseManagement.Worker;

public class DocumentoSubidoConsumer : IConsumer<DocumentoSubido>
{
    private readonly AppDbContext _db;
    private readonly AmbientTenantProvider _tenantProvider;
    private readonly IDocumentStorage _storage;
    private readonly ITextExtractor _textExtractor;
    private readonly IOcrClient _ocrClient;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly IDocumentClassifierClient _classifierClient;
    private readonly IFoliadorService _foliador;
    private readonly ILogger<DocumentoSubidoConsumer> _logger;

    public DocumentoSubidoConsumer(
        AppDbContext db,
        AmbientTenantProvider tenantProvider,
        IDocumentStorage storage,
        ITextExtractor textExtractor,
        IOcrClient ocrClient,
        IEmbeddingClient embeddingClient,
        IDocumentClassifierClient classifierClient,
        IFoliadorService foliador,
        ILogger<DocumentoSubidoConsumer> logger)
    {
        _db = db;
        _tenantProvider = tenantProvider;
        _storage = storage;
        _textExtractor = textExtractor;
        _ocrClient = ocrClient;
        _embeddingClient = embeddingClient;
        _classifierClient = classifierClient;
        _foliador = foliador;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DocumentoSubido> context)
    {
        var mensaje = context.Message;

        // Debe fijarse antes de cualquier acceso a _db: tanto el HasQueryFilter
        // global como TenantSessionInterceptor leen este valor (ver
        // AmbientTenantProvider). Este consumer no tiene HttpContext, así que
        // el tenant sale del propio mensaje, no de un claim JWT.
        _tenantProvider.SetTenant(mensaje.TenantId);

        var documento = await _db.DocumentosAdjuntos
            .FirstOrDefaultAsync(d => d.Id == mensaje.DocumentoAdjuntoId, context.CancellationToken);

        if (documento is null)
        {
            _logger.LogWarning(
                "DocumentoAdjunto {DocumentoId} no encontrado para el tenant {TenantId}; se ignora el evento.",
                mensaje.DocumentoAdjuntoId, mensaje.TenantId);
            return;
        }

        // Idempotencia: el único sitio que publica DocumentoSubido es el
        // endpoint de subida (no existe un "reprocesar" legítimo que lo
        // reenvíe a un documento ya Completado). Si llega de todos modos, es
        // una redelivery de RabbitMQ tras un crash justo después de que el
        // SaveChangesAsync final (documento + fragmentos, ver más abajo) ya
        // se confirmó pero antes de que este consumer llegara a confirmar el
        // mensaje — sin este guard, se reprocesaría todo el documento desde
        // cero y los fragmentos de FragmentosDocumento.Add() más abajo
        // quedarían DUPLICADOS (nada en el pipeline borra los fragmentos
        // existentes antes de insertar los nuevos). Visto en la práctica
        // varias veces esta semana con documentos huérfanos retomados por
        // RabbitMQ (aunque hasta ahora ninguno había llegado a esta fase).
        if (documento.EstadoProcesamiento == EstadoProcesamientoDocumento.Completado)
        {
            _logger.LogInformation(
                "DocumentoAdjunto {DocumentoId} ya está Completado; se ignora la redelivery para no duplicar fragmentos.",
                documento.Id);
            return;
        }

        try
        {
            var descarga = await ExtraerPaginasAsync(documento, context.CancellationToken);
            var paginas = descarga.Paginas;

            // IA.2b: rango de folio (Bates) + sello visual en PDF. Guardado
            // idempotente: si el mensaje se reprocesa tras un crash, no se
            // vuelve a pedir un rango de folio (crearía un hueco duplicado).
            //
            // El UPDATE de ultimo_folio se confirma en su propia transacción
            // en cuanto se ejecuta — no espera al SaveChangesAsync final del
            // método. Por eso aquí se abre una transacción explícita y se
            // llama a SaveChangesAsync inmediatamente, para persistir
            // documento.FolioInicio/FolioFin en el mismo commit: si no,
            // un fallo posterior en el resto del pipeline (OCR, embeddings)
            // que impida llegar al SaveChangesAsync final dejaría el
            // contador de folios ya incrementado pero FolioInicio sin
            // guardar — al reprocesar el mensaje, el guardado "idempotente"
            // de arriba volvería a incrementar el contador, duplicando el
            // rango de folios del documento.
            if (documento.FolioInicio is null && paginas.Count > 0)
            {
                var numPaginas = paginas.Count;
                await using var transaccionFolio = await _db.Database.BeginTransactionAsync(context.CancellationToken);

                // UPDATE ... RETURNING no es SQL "componible": no se puede
                // usar SingleAsync() directamente (EF intenta envolverlo en
                // un SELECT externo). Se materializa con ToListAsync() y se
                // toma el único resultado en memoria, como sugiere el propio
                // error de EF Core ("Consider calling AsEnumerable...").
                var resultadosFolio = await _db.Database
                    .SqlQuery<int>($"UPDATE expedientes SET ultimo_folio = ultimo_folio + {numPaginas} WHERE id = {documento.ExpedienteId} RETURNING ultimo_folio")
                    .ToListAsync(context.CancellationToken);
                var nuevoUltimoFolio = resultadosFolio.Single();

                var rango = FolioRangeCalculator.Calcular(nuevoUltimoFolio - numPaginas, numPaginas);
                documento.FolioInicio = rango.FolioInicio;
                documento.FolioFin = rango.FolioFin;

                await _db.SaveChangesAsync(context.CancellationToken);
                await transaccionFolio.CommitAsync(context.CancellationToken);

                if (string.Equals(documento.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                {
                    // Solo PDF: para .docx u otros formatos se asigna igualmente
                    // el rango (la numeración del expediente no debe tener
                    // huecos), pero sin sello visual — la conversión a PDF es
                    // la tarea E.1, todavía no implementada.
                    var pdfSellado = _foliador.EstamparFolios(descarga.Bytes, rango.FolioInicio, paginas);
                    using var streamSellado = new MemoryStream(pdfSellado);
                    await _storage.SubirAsync(documento.RutaAlmacenamiento, streamSellado, documento.ContentType, context.CancellationToken);
                }
            }

            // IA.2b: clasificación automática de tipo de documento, sobre el
            // texto que ya extrajo/OCRizó IA.2. No pisa un TipoDocumento
            // puesto a mano vía PATCH mientras el documento se procesaba.
            if (paginas.Count > 0)
            {
                var textoCompleto = string.Join("\n", paginas.Select(p => p.Texto));
                var clasificacion = await _classifierClient.ClasificarAsync(textoCompleto, context.CancellationToken);
                if (string.IsNullOrEmpty(documento.TipoDocumento))
                {
                    documento.TipoDocumento = clasificacion.TipoDocumento;
                    // La confianza solo tiene sentido junto al tipo que la
                    // produjo: si TipoDocumento se conservó de un PATCH manual
                    // (rama de arriba no ejecutada), guardar aquí la
                    // confianza del clasificador automático dejaría un
                    // número que no corresponde al tipo mostrado.
                    documento.TipoDocumentoConfianza = clasificacion.Confianza;
                }
            }

            var fragmentos = FragmentadorTexto.Fragmentar(paginas);

            // Reprocesado (mismo documento vuelto a subir/procesar, por
            // cualquier usuario): sustituye los fragmentos anteriores por
            // los nuevos en vez de acumularlos. Se marca aquí pero se
            // confirma en el MISMO SaveChangesAsync final que el resto del
            // método (no ExecuteDeleteAsync, que iría contra la base al
            // instante) — si el reprocesado falla más adelante y salta al
            // catch de abajo, este RemoveRange nunca se confirma y los
            // fragmentos anteriores quedan intactos, no se pierden por un
            // intento fallido. Incondicional (no solo si fragmentos.Count >
            // 0): si el nuevo procesado no produce ningún fragmento, los
            // antiguos tampoco deben quedar huérfanos de un contenido que ya
            // no corresponde al documento actual.
            var fragmentosAnteriores = await _db.FragmentosDocumento
                .Where(f => f.DocumentoAdjuntoId == documento.Id)
                .ToListAsync(context.CancellationToken);
            if (fragmentosAnteriores.Count > 0)
            {
                _db.FragmentosDocumento.RemoveRange(fragmentosAnteriores);
            }

            if (fragmentos.Count > 0)
            {
                // Solo se manda a la API de embeddings el texto no vacío —
                // un fragmento vacío (página sin texto reconocido por el
                // OCR, ver FragmentadorTexto) no aporta nada a un embedding
                // y no tiene sentido gastar una llamada/cuota en él. Se
                // guarda el índice original de cada uno para volver a
                // emparejar el resultado, porque la lista que se manda a la
                // API ya no coincide 1:1 con `fragmentos`.
                var conTexto = fragmentos
                    .Select((f, indice) => (f, indice))
                    .Where(x => !string.IsNullOrWhiteSpace(x.f.Texto))
                    .ToList();

                IReadOnlyList<float[]>? embeddingsConTexto = null;
                if (conTexto.Count > 0)
                {
                    try
                    {
                        embeddingsConTexto = await _embeddingClient.GenerarAsync(
                            conTexto.Select(x => x.f.Texto).ToList(),
                            context.CancellationToken);
                    }
                    catch (EmbeddingsNoConfiguradosException ex)
                    {
                        // No es un fallo del documento: falta una clave real
                        // de pago de OpenAI, algo esperado en desarrollo. Se
                        // guardan los fragmentos sin vector (Embedding = null)
                        // en vez de marcar todo el documento como Error — la
                        // búsqueda semántica no encontrará estos fragmentos
                        // hasta que se reprocesen con una clave real
                        // configurada (ver aviso en
                        // AVISO_EMBEDDINGS_DESACTIVADOS.md, raíz del repo).
                        _logger.LogWarning(
                            "Documento {DocumentoId}: {Mensaje} Fragmentos guardados sin embedding.",
                            documento.Id, ex.Message);
                    }
                }

                var embeddingPorIndice = new Dictionary<int, float[]>();
                if (embeddingsConTexto is not null)
                {
                    for (var j = 0; j < conTexto.Count; j++)
                        embeddingPorIndice[conTexto[j].indice] = embeddingsConTexto[j];
                }

                for (var i = 0; i < fragmentos.Count; i++)
                {
                    var fragmento = fragmentos[i];
                    _db.FragmentosDocumento.Add(new FragmentoDocumento
                    {
                        TenantId = documento.TenantId,
                        DocumentoAdjuntoId = documento.Id,
                        Pagina = fragmento.Pagina,
                        Parrafo = fragmento.Parrafo,
                        TextoFragmento = fragmento.Texto,
                        ConfianzaOcr = fragmento.ConfianzaOcr,
                        Embedding = embeddingPorIndice.TryGetValue(i, out var vector) ? new Vector(vector) : null
                    });
                }
            }

            documento.EstadoProcesamiento = EstadoProcesamientoDocumento.Completado;
            documento.FechaProcesado = DateTime.UtcNow;
            documento.MensajeError = null;

            _logger.LogInformation(
                "Documento {DocumentoId} procesado: {Fragmentos} fragmentos generados.",
                documento.Id, fragmentos.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo procesando el documento {DocumentoId}", documento.Id);
            documento.EstadoProcesamiento = EstadoProcesamientoDocumento.Error;
            documento.FechaProcesado = DateTime.UtcNow;
            documento.MensajeError = ex.Message;
        }

        await _db.SaveChangesAsync(context.CancellationToken);
    }

    // Instrumentación TEMPORAL (2026-09-02): puntos de control de memoria
    // para localizar dónde exactamente salta el RSS del proceso al
    // procesar el documento real de 928 páginas (1.5GB) — confirmado por
    // separado que NO es "un solo byte[] de 1.5GB retenido" (el salto a
    // ~6.7GB pasa en segundos, con muy pocos bytes de E/S reportados por
    // el kernel, y no cambia con Server GC vs Workstation GC). Quitar en
    // cuanto se identifique la causa real — no es instrumentación
    // permanente del producto.
    private void LogPuntoDeControlMemoria(string etiqueta, Guid documentoId)
    {
        var workingSetMb = Environment.WorkingSet / 1024.0 / 1024.0;
        _logger.LogWarning(
            "[MEM-DEBUG] Documento {DocumentoId} — {Etiqueta}: WorkingSet={WorkingSetMb:F0} MB",
            documentoId, etiqueta, workingSetMb);
    }

    private async Task<DocumentoDescargado> ExtraerPaginasAsync(DocumentoAdjunto documento, CancellationToken cancellationToken)
    {
        LogPuntoDeControlMemoria("0. antes de descargar", documento.Id);

        var bytes = await _storage.DescargarAsync(documento.RutaAlmacenamiento, cancellationToken);
        LogPuntoDeControlMemoria("1. tras descargar (byte[] ya listo, sin copia redundante)", documento.Id);

        using var streamParaTexto = new MemoryStream(bytes);
        var textoDirecto = _textExtractor.ExtraerTextoSeleccionable(streamParaTexto, documento.ContentType, documento.NombreArchivo);
        LogPuntoDeControlMemoria("5. tras ExtraerTextoSeleccionable (PdfPig)", documento.Id);
        if (textoDirecto is not null)
        {
            _logger.LogInformation("Documento {DocumentoId}: texto seleccionable extraído directamente, sin OCR.", documento.Id);
            return new DocumentoDescargado(bytes, textoDirecto);
        }

        _logger.LogInformation("Documento {DocumentoId}: sin texto seleccionable suficiente, se envía a OCR.", documento.Id);
        using var streamParaOcr = new MemoryStream(bytes);
        LogPuntoDeControlMemoria("6. justo antes de mandar al OCR", documento.Id);
        var paginas = await _ocrClient.ReconocerAsync(streamParaOcr, documento.NombreArchivo, documento.ContentType, cancellationToken);
        LogPuntoDeControlMemoria("7. tras recibir la respuesta del OCR", documento.Id);
        return new DocumentoDescargado(bytes, paginas);
    }

    // Bytes originales descargados de SeaweedFS junto con el texto extraído/OCRizado
    // — se necesitan ambos: el texto para fragmentar/clasificar, los bytes
    // para estampar el sello de folio sin volver a descargar el objeto.
    private record DocumentoDescargado(byte[] Bytes, IReadOnlyList<PaginaTexto> Paginas);
}
