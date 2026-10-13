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

drop function [dbo].[GetBomProcessesCount]
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

drop function [dbo].[GetBomDoneProcessesCount]
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

drop function [dbo].[CalculateBomProgressLevel]
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

drop function [dbo].[GetAssemblyBomsCount]
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

drop function  [dbo].[GetAssemblyDoneBomsCount]
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

drop function  [dbo].[CalculateAssemblyProgressLevel]
go

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

drop function [dbo].[GetWorkOrderDoneAssembliesCount]
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

drop function [dbo].[GetWorkOrderAssembliesCount]
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

drop function [dbo].[CalculateWorkOrderProgressLevel]
go