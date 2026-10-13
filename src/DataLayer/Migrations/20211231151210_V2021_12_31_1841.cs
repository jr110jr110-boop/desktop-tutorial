using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2021_12_31_1841 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[,]
                {
                    { 107, "permission", "EquipmentProcess", null, null, null, null, null, null, null, null, 1 },
                    { 306, "permission", "Bom", null, null, null, null, null, null, null, null, 4 },
                    { 305, "permission", "Assembly", null, null, null, null, null, null, null, null, 4 },
                    { 304, "permission", "WorkOrder", null, null, null, null, null, null, null, null, 4 },
                    { 303, "permission", "EquipmentProcess", null, null, null, null, null, null, null, null, 4 },
                    { 302, "permission", "EquipmentBom", null, null, null, null, null, null, null, null, 4 },
                    { 301, "permission", "EquipmentAssembly", null, null, null, null, null, null, null, null, 4 },
                    { 300, "permission", "Equipment", null, null, null, null, null, null, null, null, 4 },
                    { 201, "permission", "Contract", null, null, null, null, null, null, null, null, 2 },
                    { 200, "permission", "Tender", null, null, null, null, null, null, null, null, 2 },
                    { 111, "permission", "Process", null, null, null, null, null, null, null, null, 1 },
                    { 110, "permission", "WorkOrder", null, null, null, null, null, null, null, null, 1 },
                    { 109, "permission", "Bom", null, null, null, null, null, null, null, null, 1 },
                    { 108, "permission", "Assembly", null, null, null, null, null, null, null, null, 1 },
                    { 307, "permission", "Process", null, null, null, null, null, null, null, null, 4 },
                    { 400, "permission", "Process", null, null, null, null, null, null, null, null, 8 },
                    { 105, "permission", "EquipmentAssembly", null, null, null, null, null, null, null, null, 1 },
                    { 104, "permission", "ContractEquipment", null, null, null, null, null, null, null, null, 1 },
                    { 103, "permission", "Equipment", null, null, null, null, null, null, null, null, 1 },
                    { 102, "permission", "Contract", null, null, null, null, null, null, null, null, 1 },
                    { 101, "permission", "Tender", null, null, null, null, null, null, null, null, 1 },
                    { 100, "permission", "Account", null, null, null, null, null, null, null, null, 1 },
                    { 106, "permission", "EquipmentBom", null, null, null, null, null, null, null, null, 1 }
                });

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "ace705a6-ac50-48a4-8410-26b2e426c0b7");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "d069e897-9951-4aff-b246-ab7ef0a0b801");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "aaaad0fc-ddf8-4c32-89ec-f77b7d6d4c92");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "5dbabe90-fc0e-45a4-9144-1fb8634a50d4");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "cde7b4cb-3606-4298-a7ab-62c371348b53");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "66b81699-54c8-4880-9b67-73070878e54c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "307a9040-d335-4319-b1c0-c86839991bb7");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "a6c11097-c67c-403d-bfca-faed36e7d14c");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 100);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 101);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 102);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 103);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 104);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 105);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 106);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 107);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 108);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 109);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 110);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 111);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 200);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 201);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 300);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 301);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 302);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 303);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 304);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 305);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 306);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 307);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 400);

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[,]
                {
                    { 2, "permission", "CanAddTenders", null, null, null, null, null, null, null, null, 2 },
                    { 9, "permission", "CanDeleteContracts", null, null, null, null, null, null, null, null, 2 },
                    { 8, "permission", "CanReadContracts", null, null, null, null, null, null, null, null, 2 },
                    { 7, "permission", "CanUpdateContracts", null, null, null, null, null, null, null, null, 2 },
                    { 6, "permission", "CanAddContracts", null, null, null, null, null, null, null, null, 2 },
                    { 5, "permission", "CanDeleteTenders", null, null, null, null, null, null, null, null, 2 },
                    { 4, "permission", "CanReadTenders", null, null, null, null, null, null, null, null, 2 },
                    { 3, "permission", "CanUpdateTenders", null, null, null, null, null, null, null, null, 2 },
                    { 10, "permission", "CanAttachFileToTenders", null, null, null, null, null, null, null, null, 2 },
                    { 11, "permission", "CanDetachFileToTenders", null, null, null, null, null, null, null, null, 2 },
                    { 1, "permission", "CanDoAnyThing", null, null, null, null, null, null, null, null, 1 }
                });

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "3949a6d6-c568-44de-83e8-32b0362b074c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "1baa477b-747e-4279-b304-43958ed13e93");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "43f40cf8-0d08-49b5-8a01-38606f81272c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "80f5e006-4f24-4d57-859a-4d23e510e3d1");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "68ecf2a0-b1d5-4971-9565-ce4ed9b4a5dd");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "c3a5e286-fb69-4f90-8ece-b783e1f9072b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "48404d10-5a05-42e8-b647-ab7732115814");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "72c043c5-bad0-4c79-bc57-3239c31e1bc6");
        }
    }
}
