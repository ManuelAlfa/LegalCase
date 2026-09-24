using LegalCaseManagement.Infrastructure.Documents;
using LegalCaseManagement.Infrastructure.Persistence;
using LegalCaseManagement.Infrastructure.Storage;
using LegalCaseManagement.Worker;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

// Igual que en la Api: el tenant aquí no sale de un HttpContext/JWT, sino
// del propio mensaje que procesa cada consumer (ver AmbientTenantProvider).
builder.Services.AddScoped<AmbientTenantProvider>();
builder.Services.AddScoped<ICurrentTenantProvider>(sp => sp.GetRequiredService<AmbientTenantProvider>());
builder.Services.AddScoped<TenantSessionInterceptor>();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseNpgsql(
            builder.Configuration.GetConnectionString("Default"),
            npgsqlOptions => npgsqlOptions.UseVector())
        .UseSnakeCaseNamingConvention()
        .AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()));

builder.Services.AddDocumentStorage(builder.Configuration);
builder.Services.AddDocumentPipeline(builder.Configuration);

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<DocumentoSubidoConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitMqSection = builder.Configuration.GetSection("RabbitMq");
        cfg.Host(rabbitMqSection["Host"] ?? "localhost", "/", host =>
        {
            host.Username(rabbitMqSection["Username"] ?? "admin");
            host.Password(rabbitMqSection["Password"] ?? "admin_dev");
        });

        // "DocumentoSubido": mismo nombre que generaba ConfigureEndpoints()
        // por convención (consumer/mensaje), para no crear una cola nueva y
        // dejar huérfanos los mensajes que ya hubiera en la actual.
        //
        // PrefetchCount/ConcurrentMessageLimit = 1: cada mensaje puede
        // tardar minutos u horas (OCR de documentos largos, ver
        // ocr-paddle/main.py). Sin este límite, RabbitMQ entrega hasta 16
        // mensajes sin confirmar a la vez (prefetch por defecto de
        // MassTransit) y varios documentos se procesarían en paralelo,
        // compitiendo por los mismos 2-3 workers del microservicio OCR
        // (OCR_PROCESOS/OCR_PROCESOS_SERVER en docker-compose.yml) y
        // aumentando el riesgo de agotar la memoria del host. Con esto, la
        // cola absorbe el pico de documentos subidos a la vez en vez de
        // lanzárselos todos juntos al servicio de OCR.
        cfg.ReceiveEndpoint("DocumentoSubido", e =>
        {
            e.PrefetchCount = 1;
            e.ConcurrentMessageLimit = 1;
            e.ConfigureConsumer<DocumentoSubidoConsumer>(context);
        });
    });
});

var host = builder.Build();
host.Run();
