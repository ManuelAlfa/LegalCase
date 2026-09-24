using System.Text;
using LegalCaseManagement.Api.Auth;
using LegalCaseManagement.Api.Endpoints;
using LegalCaseManagement.Domain.Entities;
using LegalCaseManagement.Infrastructure.Persistence;
using LegalCaseManagement.Infrastructure.Plantillas;
using LegalCaseManagement.Infrastructure.Storage;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Un expediente real puede traer documentos escaneados de cientos de
// páginas; el límite por defecto de Kestrel (~28.6 MB) y el de
// multipart/form-data (128 MB) rechazan esos archivos con 413 antes de que
// el código de la aplicación llegue a verlos. Subido de 1 GiB a 3 GiB el
// 2026-09-02: la Prueba 2 (928 páginas reales de la Gaceta de Madrid,
// convertidas a solo-imagen a 250 DPI para forzar el OCR) generó un PDF de
// ~1.5 GiB y el límite de 1 GiB anterior lo rechazaba con 413. Un PDF ya
// escaneado normal (JPEG, no PNG sin comprimir por página) pesa bastante
// menos para el mismo número de páginas — este límite deja margen real,
// no solo para el caso límite de esta prueba.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 3_221_225_472; // 3 GiB
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 3_221_225_472; // 3 GiB
});

builder.Services.AddHttpContextAccessor();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<ICurrentTenantProvider, JwtTenantProvider>();
builder.Services.AddScoped<TenantSessionInterceptor>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Falta la sección 'Jwt' en la configuración.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseNpgsql(
            builder.Configuration.GetConnectionString("Default"),
            npgsqlOptions => npgsqlOptions.UseVector())
        .UseSnakeCaseNamingConvention()
        .AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()));

builder.Services.AddDocumentStorage(builder.Configuration);
builder.Services.AddGeneracionDocumentos();

// Solo publisher: la Api publica DocumentoSubido, no consume nada (los
// consumers viven en Worker).
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitMqSection = builder.Configuration.GetSection("RabbitMq");
        cfg.Host(rabbitMqSection["Host"] ?? "localhost", "/", host =>
        {
            host.Username(rabbitMqSection["Username"] ?? "admin");
            host.Password(rabbitMqSection["Password"] ?? "admin_dev");
        });
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Pega aquí el JWT (sin el prefijo 'Bearer').",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
    });
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapDevTokenEndpoints();
}

app.MapGet("/api/expedientes", async (AppDbContext db) =>
    await db.Expedientes
        .OrderByDescending(e => e.FechaApertura)
        .ToListAsync())
    .RequireAuthorization();

app.MapPost("/api/expedientes", async (AppDbContext db, ICurrentTenantProvider tenant, ExpedienteCreateRequest request) =>
{
    var expediente = new Expediente
    {
        TenantId = tenant.TenantId,
        Titulo = request.Titulo,
        Cliente = request.Cliente,
        ClienteId = request.ClienteId,
        MateriaId = request.MateriaId
    };

    db.Expedientes.Add(expediente);
    await db.SaveChangesAsync();

    return Results.Created($"/api/expedientes/{expediente.Id}", expediente);
})
.RequireAuthorization();

app.MapClientesEndpoints();
app.MapPartesContrariasEndpoints();
app.MapMateriasEndpoints();
app.MapPlazosEndpoints();
app.MapProvisionesFondosEndpoints();
app.MapFacturasEndpoints();
app.MapDocumentosAdjuntosEndpoints();
app.MapPlantillasEndpoints();
app.MapUsuariosEndpoints();
app.MapAuditLogEndpoints();

app.Run();

public record ExpedienteCreateRequest(string Titulo, string Cliente, Guid? ClienteId = null, Guid? MateriaId = null);
