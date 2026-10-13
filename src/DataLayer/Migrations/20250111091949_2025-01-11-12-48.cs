using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class _202501111248 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop existing foreign keys if they exist
            migrationBuilder.Sql("IF OBJECT_ID('FK_ProcessFiles_Processes_ProcessId', 'F') IS NOT NULL ALTER TABLE ProcessFiles DROP CONSTRAINT FK_ProcessFiles_Processes_ProcessId;");
            migrationBuilder.Sql("IF OBJECT_ID('FK_ProcessFiles_AppUsers_UserId', 'F') IS NOT NULL ALTER TABLE ProcessFiles DROP CONSTRAINT FK_ProcessFiles_AppUsers_UserId;");

            // Drop the existing primary key
            migrationBuilder.DropPrimaryKey(
                name: "PK_ProcessFiles",
                table: "ProcessFiles");

            // Drop the column and recreate it with the IDENTITY property
            migrationBuilder.DropColumn(
                name: "Id",
                table: "ProcessFiles");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "ProcessFiles",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            // Add the primary key to the newly created Id column
            migrationBuilder.AddPrimaryKey(
                name: "PK_ProcessFiles",
                table: "ProcessFiles",
                column: "Id");

            // Recreate indexes
            migrationBuilder.CreateIndex(
                name: "IX_ProcessFiles_ProcessId",
                table: "ProcessFiles",
                column: "ProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessFiles_UserId",
                table: "ProcessFiles",
                column: "UserId");

            // Add foreign keys
            migrationBuilder.AddForeignKey(
                name: "FK_ProcessFiles_Processes_ProcessId",
                table: "ProcessFiles",
                column: "ProcessId",
                principalTable: "Processes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProcessFiles_AppUsers_UserId",
                table: "ProcessFiles",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Update data in AppRoles table
            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "8485364b-62cd-406a-92bc-7adf19c9fbc9");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "185e2206-3ca0-4184-9b57-33393159595d");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "2040da8a-846e-43d4-a89c-66143e00cdf3");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "e1bb18e1-e299-4351-8e87-4ae47b8bf9fd");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "b39bf785-68ec-484d-a495-cf64a6e8d309");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "fd1d535b-55f5-4421-9bd0-211018285c55");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "12f618d9-d97a-49eb-99a5-aa5bb96428df");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "2f6020fa-8e37-4ed4-b394-ce55c4d5f87f");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse changes made in the Up method
            migrationBuilder.DropForeignKey(
                name: "FK_ProcessFiles_Processes_ProcessId",
                table: "ProcessFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_ProcessFiles_AppUsers_UserId",
                table: "ProcessFiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProcessFiles",
                table: "ProcessFiles");

            migrationBuilder.DropIndex(
                name: "IX_ProcessFiles_ProcessId",
                table: "ProcessFiles");

            migrationBuilder.DropIndex(
                name: "IX_ProcessFiles_UserId",
                table: "ProcessFiles");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "ProcessFiles");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "ProcessFiles",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProcessFiles",
                table: "ProcessFiles",
                columns: new[] { "ProcessId", "DocumentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ProcessFiles_Processes_ProcessId",
                table: "ProcessFiles",
                column: "ProcessId",
                principalTable: "Processes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
