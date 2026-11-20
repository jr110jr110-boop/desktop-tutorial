using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class EngineeringE1d : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceDocumentId",
                table: "EnReferenceLists",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceFileName",
                table: "EnReferenceLists",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StandardDocumentId",
                table: "EnPlatformDossiers",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceDocumentId",
                table: "EnReferenceLists");

            migrationBuilder.DropColumn(
                name: "SourceFileName",
                table: "EnReferenceLists");

            migrationBuilder.DropColumn(
                name: "StandardDocumentId",
                table: "EnPlatformDossiers");
        }
    }
}
