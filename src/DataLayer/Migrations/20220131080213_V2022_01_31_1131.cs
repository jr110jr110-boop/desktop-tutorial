using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_01_31_1131 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .Sql(
                @"
                        CREATE FUNCTION [dbo].[GetWorkOrderDoneAssembliesCount]
	                        (@workOrderId int)
                        RETURNS INT
                        AS
                        BEGIN
	                        DECLARE @count INT
	                        SET @count = (
	                        select count(*)
	                        from [Assemblies] as a
		                        where a.WorkOrderId = @workOrderId and 
			                        a.Deleted = 0 and
			                        [dbo].[CalculateAssemblyProgressLevel](a.Id) = 100.0
		                        )

	                        RETURN @count
                        END
                        go

                        CREATE FUNCTION [dbo].[GetWorkOrderAssembliesCount]
	                        (@workOrderId int)
                        RETURNS INT
                        AS
                        BEGIN
	                        DECLARE @count INT
	                        SET @count = (
	                        select count(*)
	                        from [Assemblies] as a
		                        where a.WorkOrderId = @workOrderId and 
			                        a.Deleted = 0
		                        )

	                        RETURN @count
                        END
                        go

                        CREATE FUNCTION [dbo].[CalculateWorkOrderProgressLevel]
	                        (@workOrderId int)
                        RETURNS decimal
                        AS
                        BEGIN
	                        DECLARE @done INT
	                        DECLARE @all INT

	                        SET @done = [dbo].[GetWorkOrderDoneAssembliesCount](@workOrderId)
	                        SET @all = [dbo].[GetWorkOrderAssembliesCount](@workOrderId)

	                        return 
		                        case @all
		                        when 0 then 0.0
		                        else 100.0 * @done / @all
		                        end
                        END
                        go
                    "
                );

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "57f95aa3-9d89-407b-962b-006fa4fe3b97");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "a169ea39-d532-40ae-9841-2698bc50cc86");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "7c0f2d25-715e-4130-8725-03618174ae54");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "b6a5078e-b4da-4508-923a-9c425fbf7409");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "19fc5514-06cc-448f-b5ca-7a12eaa6bd42");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "1a967ff4-99f1-46b2-b762-8fa9b753e36d");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "32bd25de-5b13-4711-b3f2-11a161f303c9");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "658ea60c-1c6d-4d24-9b9d-305cc830bb71");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .Sql(
                @"
                        drop function [dbo].[CalculateWorkOrderProgressLevel]
                        go

                        drop function [dbo].[GetWorkOrderAssembliesCount]
                        go

                        drop function [dbo].[GetWorkOrderDoneAssembliesCount]
                        go
                     "
                );

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "da34114f-d277-40d7-950b-a49529e9336b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "bf2965d3-a651-4d33-8de8-5178d0fcfa5f");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "b6368133-6c25-4515-9726-9170d60ad322");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "40572558-b5f5-4289-b003-5db600c5e3eb");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "01fc1f4c-ab59-4a4b-9dbb-15703f4f7a80");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "31929ec7-c388-4bbb-9497-7dd0f408de57");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "c9ee186e-baf6-46d9-8f74-a05233037c65");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "b0125213-2615-44d3-88a2-f51a7f2e6f04");
        }
    }
}
