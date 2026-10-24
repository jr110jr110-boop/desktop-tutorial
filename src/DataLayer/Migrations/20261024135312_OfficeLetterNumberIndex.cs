using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class OfficeLetterNumberIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OfLetters_Number",
                table: "OfLetters");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetters_Kind_Number",
                table: "OfLetters",
                columns: new[] { "Kind", "Number" },
                unique: true,
                filter: "[Deleted] = 0 AND [Number] IS NOT NULL AND [Imported] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OfLetters_Kind_Number",
                table: "OfLetters");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetters_Number",
                table: "OfLetters",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0 AND [Number] IS NOT NULL AND [Imported] = 0");
        }
    }
}
