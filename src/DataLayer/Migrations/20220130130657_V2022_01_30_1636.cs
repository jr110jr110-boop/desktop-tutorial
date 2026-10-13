using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_01_30_1636 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"
                        CREATE FUNCTION [dbo].[GetBomProcessesCount]
	                        (@bomId int)
                        RETURNS INT
                        AS
                        BEGIN
	                        DECLARE @count INT
	                        SET @count = (
	                        select count(*)
	                        from [Processes] as p
		                        where p.BomId = @bomId and 
			                        p.Deleted = 0
		                        )

	                        RETURN @count
                        END
                        go

                        CREATE FUNCTION [dbo].[GetBomDoneProcessesCount]
	                        (@bomId int)
                        RETURNS INT
                        AS
                        BEGIN
	                        DECLARE @count INT
	                        SET @count = (
	                        select count(*)
	                        from [Processes] as p
		                        where p.BomId = @bomId and 
			                        p.Deleted = 0 and 
			                        p.ProcessStatus = 100
		                        )

	                        RETURN @count
                        END
                        go

                        CREATE FUNCTION [dbo].[CalculateBomProgressLevel]
	                        (@bomId int)
                        RETURNS decimal
                        AS
                        BEGIN
	                        DECLARE @done INT
	                        DECLARE @all INT

	                        SET @done = [dbo].[GetBomDoneProcessesCount](@bomId)
	                        SET @all = [dbo].[GetBomProcessesCount](@bomId)

	                        return 
		                        case @all
		                        when 0 then 0.0
		                        else 100.0 * @done / @all
		                        end
                        END
                        go

                        CREATE FUNCTION [dbo].[GetAssemblyBomsCount]
	                        (@assemblyId int)
                        RETURNS INT
                        AS
                        BEGIN
	                        DECLARE @count INT
	                        SET @count = (
	                        select count(*)
	                        from [Boms] as b
		                        where b.AssemblyId = @assemblyId and 
			                        b.Deleted = 0
		                        )

	                        RETURN @count
                        END
                        go

                        CREATE FUNCTION [dbo].[GetAssemblyDoneBomsCount]
	                        (@assemblyId int)
                        RETURNS INT
                        AS
                        BEGIN
	                        DECLARE @count INT
	                        SET @count = (
	                        select count(*)
	                        from [Boms] as b
		                        where b.AssemblyId = @assemblyId and 
			                        b.Deleted = 0 and
			                        [dbo].[CalculateBomProgressLevel](b.Id) = 100.0
		                        )

	                        RETURN @count
                        END
                        go

                        CREATE FUNCTION [dbo].[CalculateAssemblyProgressLevel]
	                        (@assemblyId int)
                        RETURNS decimal
                        AS
                        BEGIN
	                        DECLARE @done INT
	                        DECLARE @all INT

	                        SET @done = [dbo].[GetAssemblyDoneBomsCount](@assemblyId)
	                        SET @all = [dbo].[GetAssemblyBomsCount](@assemblyId)

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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"
                        drop function  [dbo].[CalculateAssemblyProgressLevel]
                        go

                        drop function [dbo].[GetAssemblyBomsCount]
                        go

                        drop function  [dbo].[GetAssemblyDoneBomsCount]
                        go

                        drop function [dbo].[CalculateBomProgressLevel]
                        go

                        drop function [dbo].[GetBomDoneProcessesCount]
                        go

                        drop function [dbo].[GetBomProcessesCount]
                        go
                    "
                );

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "aea72b38-3463-4a42-941f-0d83430ab9be");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "fa9a2b2b-138e-4b27-bc49-f0e81a8d57b3");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "2c6180c2-141b-4b2e-8ff9-fcf5baef130b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "91980c70-dd04-4e1b-9586-95d7febbf99b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "da9264cd-644a-4452-a76d-3c5fce3f50f8");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "25cb3e2e-05f9-4145-877f-63c44386123a");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "920d5c6d-3095-4cea-bda1-935671f28813");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "2d349106-c85d-49b9-a8ba-bfeb7accd86b");
        }
    }
}
