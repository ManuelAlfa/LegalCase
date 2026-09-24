using Microsoft.Extensions.DependencyInjection;

namespace LegalCaseManagement.Infrastructure.Plantillas;

public static class PlantillasServiceCollectionExtensions
{
    public static IServiceCollection AddGeneracionDocumentos(this IServiceCollection services)
    {
        services.AddSingleton<IFusionadorDocumentos, FusionadorDocumentosOpenXml>();
        return services;
    }
}
