using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalCaseManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConfianzaOcrFragmentoDocumento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "confianza_ocr",
                table: "fragmentos_documento",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "confianza_ocr",
                table: "fragmentos_documento");
        }
    }
}
