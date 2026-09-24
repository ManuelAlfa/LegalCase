using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace LegalCaseManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModeloConsolidado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accion = table.Column<string>(type: "text", nullable: false),
                    fecha_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    hash_actual = table.Column<string>(type: "text", nullable: false),
                    hash_anterior = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    dni_cif = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    telefono = table.Column<string>(type: "text", nullable: false),
                    canal_preferido = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clientes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documentos_adjuntos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_archivo = table.Column<string>(type: "text", nullable: false),
                    ruta_almacenamiento = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    tamano_bytes = table.Column<long>(type: "bigint", nullable: false),
                    tipo_documento = table.Column<string>(type: "text", nullable: false),
                    fecha_subida = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    estado_procesamiento = table.Column<int>(type: "integer", nullable: false),
                    fecha_procesado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentos_adjuntos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entidades_extraidas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_adjunto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fragmento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    valor_texto = table.Column<string>(type: "text", nullable: false),
                    valor_normalizado = table.Column<string>(type: "text", nullable: false),
                    confianza = table.Column<double>(type: "double precision", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entidades_extraidas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "eventos_cronologia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    documento_adjunto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fragmento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confianza = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_cronologia", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expedientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "text", nullable: false),
                    cliente = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    fecha_apertura = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_cierre = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expedientes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "facturas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concepto = table.Column<string>(type: "text", nullable: false),
                    importe = table.Column<decimal>(type: "numeric", nullable: false),
                    fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facturas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fragmentos_documento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_adjunto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pagina = table.Column<int>(type: "integer", nullable: false),
                    parrafo = table.Column<int>(type: "integer", nullable: false),
                    texto_fragmento = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fragmentos_documento", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "materias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_materias", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "partes_contrarias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    representante_legal = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partes_contrarias", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plazos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    fecha_limite = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    dias_aviso_previo = table.Column<List<int>>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plazos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "provisiones_de_fondos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe = table.Column<decimal>(type: "numeric", nullable: false),
                    fecha_solicitud = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    aplicada = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provisiones_de_fondos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    plan = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    rol = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id",
                table: "audit_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_clientes_tenant_id",
                table: "clientes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_adjuntos_tenant_id",
                table: "documentos_adjuntos",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_entidades_extraidas_tenant_id",
                table: "entidades_extraidas",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_cronologia_tenant_id",
                table: "eventos_cronologia",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_expedientes_tenant_id",
                table: "expedientes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_facturas_tenant_id",
                table: "facturas",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_fragmentos_documento_tenant_id",
                table: "fragmentos_documento",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_materias_tenant_id",
                table: "materias",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_partes_contrarias_tenant_id",
                table: "partes_contrarias",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_plazos_tenant_id",
                table: "plazos",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_provisiones_de_fondos_tenant_id",
                table: "provisiones_de_fondos",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_tenant_id",
                table: "usuarios",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "clientes");

            migrationBuilder.DropTable(
                name: "documentos_adjuntos");

            migrationBuilder.DropTable(
                name: "entidades_extraidas");

            migrationBuilder.DropTable(
                name: "eventos_cronologia");

            migrationBuilder.DropTable(
                name: "expedientes");

            migrationBuilder.DropTable(
                name: "facturas");

            migrationBuilder.DropTable(
                name: "fragmentos_documento");

            migrationBuilder.DropTable(
                name: "materias");

            migrationBuilder.DropTable(
                name: "partes_contrarias");

            migrationBuilder.DropTable(
                name: "plazos");

            migrationBuilder.DropTable(
                name: "provisiones_de_fondos");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
