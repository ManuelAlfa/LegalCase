using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalCaseManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AltaExpedienteNumeroYResponsable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "nombre",
                table: "usuarios",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "anio_numeracion",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ultimo_numero_expediente",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "abogado_responsable_id",
                table: "expedientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "numero",
                table: "expedientes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // -------------------------------------------------------------
            // Relleno de los datos ya existentes.
            //
            // Sin esto la migración falla: `numero` se acaba de añadir con
            // valor por defecto '' para todas las filas, y justo debajo se
            // crea un índice ÚNICO sobre (tenant_id, numero) — con más de un
            // expediente por despacho, el índice no se puede crear.
            //
            // Las migraciones las ejecuta el rol `legalcase` (propietario),
            // que se salta la Row-Level Security, así que estas sentencias
            // ven y corrigen las filas de todos los despachos.
            // -------------------------------------------------------------

            // 1. El número vivía incrustado al principio del título
            //    ("2026/0143 — Rodríguez Marín vs. Ayuntamiento"). Se extrae
            //    a su propia columna. Lo que no encaje con el formato recibe
            //    un valor único derivado del id, para no romper el índice.
            migrationBuilder.Sql(@"
                UPDATE expedientes
                SET numero = COALESCE(
                        substring(titulo from '^\s*([0-9]{4}/[0-9]+)'),
                        'S/N-' || left(id::text, 8));
            ");

            // 2. Y se quita del título, que pasa a ser solo la descripción:
            //    si no, la interfaz mostraría el número dos veces, una en su
            //    columna y otra dentro del texto.
            migrationBuilder.Sql(@"
                UPDATE expedientes
                SET titulo = btrim(regexp_replace(titulo, '^\s*[0-9]{4}/[0-9]+\s*[—–-]?\s*', ''))
                WHERE titulo ~ '^\s*[0-9]{4}/[0-9]+';
            ");

            // 3. Se coloca el contador correlativo de cada despacho en el
            //    último número de su año más reciente, para que la próxima
            //    alta continúe la serie en vez de chocar con lo ya existente.
            migrationBuilder.Sql(@"
                WITH ultimo_anio AS (
                    SELECT tenant_id, MAX((split_part(numero, '/', 1))::int) AS anio
                    FROM expedientes
                    WHERE numero ~ '^[0-9]{4}/[0-9]+$'
                    GROUP BY tenant_id
                ), contador AS (
                    SELECT a.tenant_id,
                           a.anio,
                           MAX((split_part(e.numero, '/', 2))::int) AS ultimo
                    FROM ultimo_anio a
                    JOIN expedientes e
                      ON e.tenant_id = a.tenant_id
                     AND e.numero ~ '^[0-9]{4}/[0-9]+$'
                     AND (split_part(e.numero, '/', 1))::int = a.anio
                    GROUP BY a.tenant_id, a.anio
                )
                UPDATE tenants t
                SET anio_numeracion = c.anio,
                    ultimo_numero_expediente = c.ultimo
                FROM contador c
                WHERE c.tenant_id = t.id;
            ");

            // 4. Los usuarios existentes no tenían nombre, solo correo. Se
            //    deriva del correo como mejor aproximación disponible
            //    ("marta.alonso@..." -> "Marta Alonso"); a partir de ahora el
            //    nombre se introduce de verdad al dar de alta un usuario.
            migrationBuilder.Sql(@"
                UPDATE usuarios
                SET nombre = initcap(replace(split_part(email, '@', 1), '.', ' '))
                WHERE nombre = '';
            ");

            migrationBuilder.CreateIndex(
                name: "ix_expedientes_abogado_responsable_id",
                table: "expedientes",
                column: "abogado_responsable_id");

            migrationBuilder.CreateIndex(
                name: "ix_expedientes_tenant_id_numero",
                table: "expedientes",
                columns: new[] { "tenant_id", "numero" },
                unique: true);
        }

        /// <inheritdoc />
        // Ojo: revertir devuelve el esquema, pero NO vuelve a meter el número
        // dentro del título (paso 2 del Up). Si hiciera falta deshacerlo del
        // todo, habría que recomponer titulo = numero || ' — ' || titulo
        // antes de aplicar esta reversión.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_expedientes_abogado_responsable_id",
                table: "expedientes");

            migrationBuilder.DropIndex(
                name: "ix_expedientes_tenant_id_numero",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "nombre",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "anio_numeracion",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "ultimo_numero_expediente",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "abogado_responsable_id",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "numero",
                table: "expedientes");
        }
    }
}
