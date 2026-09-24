using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalCaseManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClienteIdMateriaIdExpediente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cliente_id",
                table: "expedientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "materia_id",
                table: "expedientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_expedientes_cliente_id",
                table: "expedientes",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_expedientes_materia_id",
                table: "expedientes",
                column: "materia_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_expedientes_cliente_id",
                table: "expedientes");

            migrationBuilder.DropIndex(
                name: "ix_expedientes_materia_id",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "cliente_id",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "materia_id",
                table: "expedientes");
        }
    }
}
