using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalCaseManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LoFacturableParaLaGestoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "exportacion_gestoria_id",
                table: "provisiones_de_fondos",
                type: "uuid",
                nullable: true);

            // EF avisa de posible pérdida de datos: el importe pasa a dos
            // decimales. Es un importe en euros, que no lleva más; comprobado
            // al crear la migración sobre los datos existentes (5 conceptos,
            // ninguno con más de dos decimales).
            migrationBuilder.AlterColumn<decimal>(
                name: "importe",
                table: "facturas",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddColumn<int>(
                name: "estado",
                table: "facturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "exportacion_gestoria_id",
                table: "facturas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_emision_gestoria",
                table: "facturas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "numero_factura_gestoria",
                table: "facturas",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_factura_gestoria",
                table: "facturas",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tarifa_hora",
                table: "expedientes",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "domicilio",
                table: "clientes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tipo_cliente",
                table: "clientes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "exportaciones_gestoria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo_desde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    periodo_hasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_generacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    numero_lineas = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exportaciones_gestoria", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registro_horas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    horas = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    tarifa_hora = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    exportacion_gestoria_id = table.Column<Guid>(type: "uuid", nullable: true),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registro_horas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_facturas_expediente_id",
                table: "facturas",
                column: "expediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_exportaciones_gestoria_tenant_id",
                table: "exportaciones_gestoria",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_registro_horas_expediente_id",
                table: "registro_horas",
                column: "expediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_registro_horas_factura_id",
                table: "registro_horas",
                column: "factura_id");

            migrationBuilder.CreateIndex(
                name: "ix_registro_horas_tenant_id",
                table: "registro_horas",
                column: "tenant_id");

            // -------------------------------------------------------------
            // Las facturas que ya existían eran facturas EMITIDAS: antes de D.5
            // una Factura significaba eso. Ahora la entidad pasa a ser un
            // "concepto facturable" con estado, y por defecto quedaría como
            // pendiente de exportar a la gestoría, lo que haría exportarlas
            // otra vez. Se marcan como ya emitidas por la gestoría.
            // -------------------------------------------------------------
            migrationBuilder.Sql("UPDATE facturas SET estado = 2;  -- EstadoFactura.EmitidaPorGestoria");

            // -------------------------------------------------------------
            // Row-Level Security de las tablas nuevas, igual que el resto
            // (ver Sql/002_extend_row_level_security.sql). Va dentro de la
            // migración y no en un script aparte para que no pueda quedar sin
            // aplicar: sin estas políticas, el rol de la aplicación vería las
            // horas y las exportaciones de todos los despachos.
            // -------------------------------------------------------------
            foreach (var tabla in new[] { "registro_horas", "exportaciones_gestoria" })
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {tabla} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {tabla} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation_{tabla} ON {tabla}
                        USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exportaciones_gestoria");

            migrationBuilder.DropTable(
                name: "registro_horas");

            migrationBuilder.DropIndex(
                name: "ix_facturas_expediente_id",
                table: "facturas");

            migrationBuilder.DropColumn(
                name: "exportacion_gestoria_id",
                table: "provisiones_de_fondos");

            migrationBuilder.DropColumn(
                name: "estado",
                table: "facturas");

            migrationBuilder.DropColumn(
                name: "exportacion_gestoria_id",
                table: "facturas");

            migrationBuilder.DropColumn(
                name: "fecha_emision_gestoria",
                table: "facturas");

            migrationBuilder.DropColumn(
                name: "numero_factura_gestoria",
                table: "facturas");

            migrationBuilder.DropColumn(
                name: "total_factura_gestoria",
                table: "facturas");

            migrationBuilder.DropColumn(
                name: "tarifa_hora",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "domicilio",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "tipo_cliente",
                table: "clientes");

            migrationBuilder.AlterColumn<decimal>(
                name: "importe",
                table: "facturas",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);
        }
    }
}
