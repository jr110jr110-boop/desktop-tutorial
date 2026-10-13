using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_08_26_2254 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "Providers");

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "Providers",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProviderCategories",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Category = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCategories", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "41a1838b-980f-4f88-8c0a-bd7c48a6cd27");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "2887976c-d48d-415e-839f-3ea67c778167");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "c904e496-48f7-4999-b7ce-ddf36bea343d");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "3e8fc22e-6636-4684-bd3c-7faba22bb4b0");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "aa6839b9-d923-4e60-9676-091cda697638");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "6458fd79-af84-4dfe-aa0d-81f4ea1c1b8f");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "9997d164-1a98-42e4-af59-ca1a94e6317c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "e14e64f5-fe37-4ec0-84ea-5efbaaa6eb3c");

            migrationBuilder.CreateIndex(
                name: "IX_Providers_CategoryId",
                table: "Providers",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Providers_ProviderCategories_CategoryId",
                table: "Providers",
                column: "CategoryId",
                principalTable: "ProviderCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Providers_ProviderCategories_CategoryId",
                table: "Providers");

            migrationBuilder.DropTable(
                name: "ProviderCategories");

            migrationBuilder.DropIndex(
                name: "IX_Providers_CategoryId",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Providers");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Providers",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "447076b3-12d1-4c98-ba2d-e80f06182ca5");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "de3b12c7-3fb9-40ca-951c-f5760399ed6a");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "5b2cd858-639d-42bc-87de-706d7569e81c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "40a459cf-b77b-4f3c-a0f3-133b015f2771");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "2e339a35-dce3-4b1c-bc3d-3fa44035ecc2");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "232d4d18-c3c0-4047-a70e-ecf8dfacac43");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "942eae23-bd24-4b84-8107-5cb347c8ca92");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "1fdb6abe-6b4e-4d15-8540-d707a1f38b35");
        }
    }
}
