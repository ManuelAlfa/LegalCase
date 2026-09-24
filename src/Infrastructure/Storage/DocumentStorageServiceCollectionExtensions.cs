using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LegalCaseManagement.Infrastructure.Storage;

public static class DocumentStorageServiceCollectionExtensions
{
    // Compartido entre Api (sube documentos) y Worker (los descarga para
    // procesarlos), para que ambos apunten siempre al mismo bucket/credenciales
    // sin duplicar la configuración del cliente de S3.
    public static IServiceCollection AddDocumentStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DocumentStorageOptions>(configuration.GetSection(DocumentStorageOptions.SectionName));

        services.AddSingleton<IAmazonS3>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DocumentStorageOptions>>().Value;
            var config = new AmazonS3Config
            {
                ServiceURL = options.Endpoint,
                ForcePathStyle = true, // requerido por SeaweedFS (y compatible con S3 real)
                UseHttp = options.Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            };

            return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
        });

        services.AddSingleton<IDocumentStorage, S3DocumentStorage>();

        return services;
    }
}
