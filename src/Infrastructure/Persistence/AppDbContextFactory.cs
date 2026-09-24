using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LegalCaseManagement.Infrastructure.Persistence;

// Usado por las herramientas de diseño de EF Core (dotnet ef migrations add /
// database update), NO por la aplicación en tiempo de ejecución. Tiene sus
// propias credenciales porque legalcase_app (Sql/003_create_app_role.sql) no
// tiene permisos DDL para crear ni alterar tablas — las migraciones deben
// ejecutarse con el rol propietario (legalcase). Al existir esta factory,
// `dotnet ef` la usa en vez de arrancar Program.cs/appsettings.json, así que
// la connection string de aquí es independiente de la que usa la Api.
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("LEGALCASE_MIGRATIONS_CONNECTION")
            // Puerto 15432, no el 5432 estándar — ver docker-compose.yml.
            ?? "Host=localhost;Port=15432;Database=legalcase;Username=legalcase;Password=legalcase_dev";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseVector())
            .UseSnakeCaseNamingConvention();

        // TenantId no importa en tiempo de diseño (solo se usa para construir
        // el modelo/las migraciones, nunca para ejecutar una query real).
        return new AppDbContext(optionsBuilder.Options, new DesignTimeTenantProvider());
    }

    private class DesignTimeTenantProvider : ICurrentTenantProvider
    {
        public Guid TenantId => Guid.Empty;
    }
}
