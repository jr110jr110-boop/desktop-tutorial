using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class _2024_11_17_1516 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HeightUnit",
                table: "EquipmentBoms",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LengthUnit",
                table: "EquipmentBoms",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NotWetWeightUnit",
                table: "EquipmentBoms",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WetWeightUnit",
                table: "EquipmentBoms",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WidthUnit",
                table: "EquipmentBoms",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeightUnit",
                table: "Boms",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LengthUnit",
                table: "Boms",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NotWetWeightUnit",
                table: "Boms",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WetWeightUnit",
                table: "Boms",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WidthUnit",
                table: "Boms",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "d1f12675-4110-46c3-bc43-50f0e0e34911");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "678f21c9-4bcd-40a8-a98a-19569f7ee967");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "d64cf362-fe5b-44ed-ba7b-ac83b6d72b54");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "6e5a9c02-c65f-4099-919b-9e1969449880");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "10108e35-3a83-4f07-9a00-aade7e53bf98");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "c16de170-e3d2-4f39-b393-71a22d39c649");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "e9b5f205-368c-46be-95f1-d740d0b0bf03");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "65555e13-26d9-4db4-8e0c-c22a21c2b42d");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeightUnit",
                table: "EquipmentBoms");

            migrationBuilder.DropColumn(
                name: "LengthUnit",
                table: "EquipmentBoms");

            migrationBuilder.DropColumn(
                name: "NotWetWeightUnit",
                table: "EquipmentBoms");

            migrationBuilder.DropColumn(
                name: "WetWeightUnit",
                table: "EquipmentBoms");

            migrationBuilder.DropColumn(
                name: "WidthUnit",
                table: "EquipmentBoms");

            migrationBuilder.DropColumn(
                name: "HeightUnit",
                table: "Boms");

            migrationBuilder.DropColumn(
                name: "LengthUnit",
                table: "Boms");

            migrationBuilder.DropColumn(
                name: "NotWetWeightUnit",
                table: "Boms");

            migrationBuilder.DropColumn(
                name: "WetWeightUnit",
                table: "Boms");

            migrationBuilder.DropColumn(
                name: "WidthUnit",
                table: "Boms");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "9432bb92-1fa9-4486-8940-bd88e10c59b2");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "c4d376e1-ac12-41ee-abea-22da268e3479");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "3b7e9121-bf44-478c-a6cc-79f41903601b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "d21f96f3-c705-4f97-8170-cd118c07b299");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "f5482bb1-b1f7-4cb3-99f4-076b4561630b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "934f01b7-c68b-4268-ae41-2633b76d9342");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "78830416-eda8-43ca-a529-8969d3f5d1b7");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "35f5e645-4215-4b93-8575-6ef2129770cd");
        }
    }
}
