using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Production;

namespace ViewModels.NasbNiro.ProductionDtos
{
    public class MfPageQuery
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public string Contains { get; set; }
    }

    // ------------------------------------------------------------------------------------------ work centres and machines

    public class WorkCenterQuery : MfPageQuery
    {
        public MfWorkCenterKind? Kind { get; set; }
        public bool? IsActive { get; set; }
    }

    public class WorkCenterDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public MfWorkCenterKind Kind { get; set; }
        public int? CalendarId { get; set; }
        public string CalendarName { get; set; }
        public int ShiftsPerDay { get; set; }
        public decimal HoursPerShift { get; set; }
        public decimal EfficiencyPercent { get; set; }
        public decimal UtilisationPercent { get; set; }
        public int ParallelResources { get; set; }
        public decimal? MachineRateRial { get; set; }
        public decimal? LabourRateRial { get; set; }
        public string CostCenter { get; set; }
        public decimal QueueDaysDefault { get; set; }
        public bool IsActive { get; set; }
        public int MachineCount { get; set; }

        /// <summary>R1p D43: the terminal refuses to start an operation of this centre without a machine (while the centre has machines).</summary>
        public bool RequiresMachine { get; set; }

        public string RowVersion { get; set; }
    }

    public class SaveWorkCenterDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public MfWorkCenterKind Kind { get; set; } = MfWorkCenterKind.Machine;
        public int? CalendarId { get; set; }
        public int ShiftsPerDay { get; set; } = 1;
        public decimal HoursPerShift { get; set; } = 8;
        public decimal EfficiencyPercent { get; set; } = 100;
        public decimal UtilisationPercent { get; set; } = 85;
        public int ParallelResources { get; set; } = 1;
        public string CostCenter { get; set; }
        public decimal QueueDaysDefault { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>R1p D43 (★ default true): an operation of the centre starts on the terminal only with a machine, while the centre has machines.</summary>
        public bool RequiresMachine { get; set; } = true;

        public string RowVersion { get; set; }
    }

    /// <summary>The hourly rates of a work centre, set by finance only.</summary>
    public class SaveWorkCenterRatesDto
    {
        public int Id { get; set; }
        public decimal? MachineRateRial { get; set; }
        public decimal? LabourRateRial { get; set; }
    }

    public class MachineQuery : MfPageQuery
    {
        public int? WorkCenterId { get; set; }
        public MfMachineStatus? Status { get; set; }
    }

    public class MachineDto
    {
        public int MachineId { get; set; }
        public string Title { get; set; }
        public bool IsActive { get; set; }
        public int? InfoId { get; set; }
        public int? WorkCenterId { get; set; }
        public string WorkCenterCode { get; set; }
        public int? AssetId { get; set; }
        public string Maker { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string Capabilities { get; set; }
        public MfMachineStatus Status { get; set; }
    }

    public class SaveMachineInfoDto
    {
        public int MachineId { get; set; }
        public int? WorkCenterId { get; set; }
        public int? AssetId { get; set; }
        public string Maker { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string Capabilities { get; set; }
        public MfMachineStatus Status { get; set; } = MfMachineStatus.Available;
    }

    // ------------------------------------------------------------------------------------------ operation standards

    public class OperationStandardQuery : MfPageQuery
    {
        public int? ItemId { get; set; }
        public int? ItemGroupId { get; set; }
    }

    public class OperationStandardDto
    {
        public int OperationTemplateId { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public int? ItemGroupId { get; set; }
        public string ItemGroupCode { get; set; }
        public int Sequence { get; set; }
        public string TemplateTitle { get; set; }
        public bool RequiresQc { get; set; }

        /// <summary>Null when no standard has been entered for the step yet.</summary>
        public int? Id { get; set; }

        public int? WorkCenterId { get; set; }
        public string WorkCenterCode { get; set; }
        public decimal SetupHours { get; set; }
        public decimal RunHoursPerUnit { get; set; }
        public decimal MoveDays { get; set; }
        public decimal QueueDays { get; set; }
        public bool IsOutside { get; set; }
        public int? DefaultSupplierId { get; set; }
        public string DefaultSupplierName { get; set; }
        public int OutsideLeadDays { get; set; }
        public int? WorkInstructionDocumentId { get; set; }
        public string DrawingReferenceRule { get; set; }
        public string RequiredQualificationKind { get; set; }
        public string RequiredQualificationProcess { get; set; }
        public MfInspectionRequirement InspectionRequirement { get; set; }
        public int? ItpTestType { get; set; }
        public bool FirstArticleRequired { get; set; }

        /// <summary>R1p D12: a welding step (WPS and welder qualification are checked on the terminal).</summary>
        public bool IsWelding { get; set; }

        public int? WpsId { get; set; }
        public string WpsNumber { get; set; }
    }

    public class SaveOperationStandardDto
    {
        public int OperationTemplateId { get; set; }
        public int? WorkCenterId { get; set; }
        public decimal SetupHours { get; set; }
        public decimal RunHoursPerUnit { get; set; }
        public decimal MoveDays { get; set; }
        public decimal QueueDays { get; set; }
        public bool IsOutside { get; set; }
        public int? DefaultSupplierId { get; set; }
        public int OutsideLeadDays { get; set; }
        public int? WorkInstructionDocumentId { get; set; }
        public string DrawingReferenceRule { get; set; }
        public string RequiredQualificationKind { get; set; }
        public string RequiredQualificationProcess { get; set; }
        public MfInspectionRequirement InspectionRequirement { get; set; }
        public int? ItpTestType { get; set; }
        public bool FirstArticleRequired { get; set; }

        /// <summary>R1p D12: a welding step; then <see cref="WpsId"/> (an approved WPS) is mandatory.</summary>
        public bool IsWelding { get; set; }

        public int? WpsId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ routing revisions

    public class RoutingQuery : MfPageQuery
    {
        public int? ItemId { get; set; }
        public int? ItemGroupId { get; set; }
        public MfRoutingStatus? Status { get; set; }
    }

    public class RoutingStepDto
    {
        public int TemplateId { get; set; }
        public int Sequence { get; set; }
        public string Title { get; set; }
        public bool RequiresQc { get; set; }
        public int? StandardId { get; set; }
        public string WorkCenterCode { get; set; }
        public decimal SetupHours { get; set; }
        public decimal RunHoursPerUnit { get; set; }
        public bool IsOutside { get; set; }
        public MfInspectionRequirement InspectionRequirement { get; set; }
    }

    public class RoutingRevisionDto
    {
        public int Id { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public int? ItemGroupId { get; set; }
        public string ItemGroupCode { get; set; }
        public int Revision { get; set; }
        public MfRoutingStatus Status { get; set; }
        public string Note { get; set; }
        public string ContentHash { get; set; }

        /// <summary>The steps have not changed since the approval.</summary>
        public bool ContentMatches { get; set; }

        public string CreatedByName { get; set; }
        public string CreatedAtText { get; set; }
        public string ProductionApprovedByName { get; set; }
        public string ProductionApprovedAtText { get; set; }
        public string EngineeringApprovedByName { get; set; }
        public string EngineeringApprovedAtText { get; set; }
        public string ApprovedAtText { get; set; }
        public List<RoutingStepDto> Steps { get; set; } = new List<RoutingStepDto>();
        public string RowVersion { get; set; }
    }

    public class SaveRoutingRevisionDto
    {
        public int? ItemId { get; set; }
        public int? ItemGroupId { get; set; }
        public string Note { get; set; }
    }

    public class RoutingActionDto
    {
        public int Id { get; set; }

        /// <summary>approve-production, approve-engineering.</summary>
        public string Action { get; set; }

        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ reason codes

    public class ReasonCodeQuery : MfPageQuery
    {
        public MfReasonKind? Kind { get; set; }
        public bool? IsActive { get; set; }
    }

    public class ReasonCodeDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public MfReasonKind Kind { get; set; }
        public bool IsActive { get; set; }
    }

    public class SaveReasonCodeDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public MfReasonKind Kind { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
