using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalCaseManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConservarOriginalYPdfProcesado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ruta_almacenamiento_procesado",
                table: "documentos_adjuntos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ruta_almacenamiento_procesado",
                table: "documentos_adjuntos");
        }
    }
}
