using LegalCaseManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LegalCaseManagement.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    private readonly ICurrentTenantProvider _currentTenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantProvider currentTenant)
        : base(options)
    {
        _currentTenant = currentTenant;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Expediente> Expedientes => Set<Expediente>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<ParteContraria> PartesContrarias => Set<ParteContraria>();
    public DbSet<Materia> Materias => Set<Materia>();
    public DbSet<Plazo> Plazos => Set<Plazo>();
    public DbSet<ProvisionDeFondos> ProvisionesDeFondos => Set<ProvisionDeFondos>();
    public DbSet<Factura> Facturas => Set<Factura>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<DocumentoAdjunto> DocumentosAdjuntos => Set<DocumentoAdjunto>();
    public DbSet<FragmentoDocumento> FragmentosDocumento => Set<FragmentoDocumento>();
    public DbSet<EntidadExtraida> EntidadesExtraidas => Set<EntidadExtraida>();
    public DbSet<EventoCronologia> EventosCronologia => Set<EventoCronologia>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.HasKey(t => t.Id);
        });

        modelBuilder.Entity<Expediente>(b =>
        {
            b.ToTable("expedientes");
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.TenantId);
            b.HasIndex(e => e.ClienteId);
            b.HasIndex(e => e.MateriaId);

            // Filtro global: toda consulta a Expedientes queda automáticamente
            // acotada al tenant actual. Es una red de seguridad a nivel de
            // aplicación; en el clúster compartido (pooled) se complementa
            // con Row-Level Security a nivel de Postgres más adelante.
            b.HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<Cliente>(b =>
        {
            b.ToTable("clientes");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId);
            b.HasQueryFilter(x => x.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<ParteContraria>(b =>
        {
            b.ToTable("partes_contrarias");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId);
            b.HasQueryFilter(x => x.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<Materia>(b =>
        {
            b.ToTable("materias");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId);
            b.HasQueryFilter(x => x.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<Plazo>(b =>
        {
            b.ToTable("plazos");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId);
            b.HasQueryFilter(x => x.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<ProvisionDeFondos>(b =>
        {
            b.ToTable("provisiones_de_fondos");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId);
            b.HasQueryFilter(x => x.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<Factura>(b =>
        {
            b.ToTable("facturas");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId);
            b.HasQueryFilter(x => x.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<Usuario>(b =>
        {
            b.ToTable("usuarios");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId);
            b.HasQueryFilter(x => x.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<DocumentoAdjunto>(b =>
        {
            b.ToTable("documentos_adjuntos");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId);
            b.HasQueryFilter(x => x.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<FragmentoDocumento>(b =>
        {
            b.ToTable("fragmentos_documento");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId);

            // Dimensión fijada a 1536 (estándar de los modelos de embedding tipo
            // text-embedding-3-small/ada-002); si se cambia de modelo de embedding
            // más adelante, esta dimensión tendrá que migrarse en consecuencia.
            b.Property(x => x.Embedding).HasColumnType("vector(1536)");

            b.HasQueryFilter(x => x.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<EntidadExtraida>(b =>
        {
            b.ToTable("entidades_extraidas");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId);
            b.HasQueryFilter(x => x.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<EventoCronologia>(b =>
        {
            b.ToTable("eventos_cronologia");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId);
            b.HasQueryFilter(x => x.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<AuditLog>(b =>
        {
            b.ToTable("audit_logs");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId);
            b.HasQueryFilter(x => x.TenantId == _currentTenant.TenantId);
        });
    }
}
