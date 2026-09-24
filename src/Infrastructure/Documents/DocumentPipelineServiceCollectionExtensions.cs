using LegalCaseManagement.Infrastructure.Classification;
using LegalCaseManagement.Infrastructure.Embeddings;
using LegalCaseManagement.Infrastructure.Foliado;
using LegalCaseManagement.Infrastructure.Ocr;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LegalCaseManagement.Infrastructure.Documents;

public static class DocumentPipelineServiceCollectionExtensions
{
    // Usado solo por Worker (DocumentoSubidoConsumer): extracción de texto
    // directo, cliente HTTP al microservicio de OCR, cliente HTTP a la API
    // de embeddings, cliente HTTP al microservicio de clasificación de tipo
    // de documento (IA.2b) y foliador (sello Bates) local.
    public static IServiceCollection AddDocumentPipeline(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ITextExtractor, PdfDocxTextExtractor>();
        services.AddSingleton<IFoliadorService, FoliadorService>();

        services.Configure<OcrOptions>(configuration.GetSection(OcrOptions.SectionName));
        services.AddHttpClient<IOcrClient, OcrClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OcrOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            // El propio microservicio se rinde solo tras OCR_TIMEOUT_SEGUNDOS
            // (240 min desde 2026-09-03, ver docker-compose.yml — subido de
            // 150 min tras el primer intento real con 928 páginas de la
            // Gaceta: la fase mobile sola ya tarda ~95 min con páginas
            // reales, dejando margen insuficiente para el reprocesado
            // "server" bajo el plazo compartido anterior). Este timeout debe
            // quedar por encima de ese plazo con margen, para que sea el
            // microservicio quien falle limpio primero, no el cliente HTTP a
            // mitad de una subida que sí iba a terminar bien. Acoplado
            // también con consumer_timeout de RabbitMQ (rabbitmq.conf) —
            // debe quedar por ENCIMA de este valor, no al revés.
            client.Timeout = TimeSpan.FromMinutes(260);
        });

        services.Configure<EmbeddingOptions>(configuration.GetSection(EmbeddingOptions.SectionName));
        services.AddHttpClient<IEmbeddingClient, OpenAiEmbeddingClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        services.Configure<ClassifierOptions>(configuration.GetSection(ClassifierOptions.SectionName));
        services.AddHttpClient<IDocumentClassifierClient, DocumentClassifierClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ClassifierOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            // A diferencia de /ocr, /clasificar solo procesa texto y responde
            // en milisegundos (ver services/doc-classifier/README.md) — un
            // timeout corto es intencional para detectar fallos rápido.
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
