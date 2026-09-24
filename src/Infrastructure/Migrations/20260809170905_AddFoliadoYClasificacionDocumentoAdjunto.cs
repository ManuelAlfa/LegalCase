using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalCaseManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFoliadoYClasificacionDocumentoAdjunto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ultimo_folio",
                table: "expedientes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "folio_fin",
                table: "documentos_adjuntos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "folio_inicio",
                table: "documentos_adjuntos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "tipo_documento_confianza",
                table: "documentos_adjuntos",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ultimo_folio",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "folio_fin",
                table: "documentos_adjuntos");

            migrationBuilder.DropColumn(
                name: "folio_inicio",
                table: "documentos_adjuntos");

            migrationBuilder.DropColumn(
                name: "tipo_documento_confianza",
                table: "documentos_adjuntos");
        }
    }
}
