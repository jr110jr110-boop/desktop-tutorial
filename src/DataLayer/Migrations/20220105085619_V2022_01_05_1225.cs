using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_01_05_1225 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "Documents",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "UploaderIp",
                table: "Documents",
                newName: "Code");

            migrationBuilder.AddColumn<int>(
                name: "ProcessEntityId",
                table: "OrderProcesses",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "14bb1086-13b2-46a0-93b9-b5a6b3b30a8a");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "f43e58fb-2736-408e-a729-dd4ee7972cbe");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "756ad0b7-1b61-4c6b-9c6f-8e3f367ed599");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "1b426ec3-4bb8-4455-87ad-8727f97e6405");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "f481b42c-f890-476e-951c-db8f5fc94614");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "7bdbaaea-5f0c-4be3-85c7-528991e79b1a");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "3d222177-0995-43f5-868c-2d851dfb249d");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "37cfb9cd-8eef-4189-b576-67d1af113bbd");

            migrationBuilder.CreateIndex(
                name: "IX_OrderProcesses_ProcessEntityId",
                table: "OrderProcesses",
                column: "ProcessEntityId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderProcesses_Processes_ProcessEntityId",
                table: "OrderProcesses",
                column: "ProcessEntityId",
                principalTable: "Processes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderProcesses_Processes_ProcessEntityId",
                table: "OrderProcesses");

            migrationBuilder.DropIndex(
                name: "IX_OrderProcesses_ProcessEntityId",
                table: "OrderProcesses");

            migrationBuilder.DropColumn(
                name: "ProcessEntityId",
                table: "OrderProcesses");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Documents",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "Documents",
                newName: "UploaderIp");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "278ea00c-2590-432f-b3db-cb5e1886cdd2");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "80a6115e-e005-44d7-904d-3ecca8aa2a9d");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "6abfc208-722f-4452-abb2-9753c3a2db9d");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "ee05a098-5dc8-4c5a-a63d-928d93bec83b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "be215ba0-dc4b-4683-9abf-0d4dc6827a66");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "667fea59-e6d1-4ccc-ae7d-a3e543f1a03e");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "496abdbd-b169-4ef9-bacc-13a2758a7704");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "e1cb0ba7-31bd-4822-b881-fc5ba31f221d");
        }
    }
}
