using System;
using Pc = Entities.Model.NasbNiro.ProjectControl;

namespace DataLayer.Context
{
    /// <summary>
    /// Project control priority 4 (resource pool, standard rates, resource plan): the new record types join the tamper-evident chain of Package D (and so the nightly record-drift check).
    /// A standard rate is written as a "changed" marker only, never as a value: the trail is read by the auditor too, and the rate of a small group could give away one person's cost (project rule 6).
    /// The tables are new, so they are not part of the go-live baseline. In its own file so that it merges cleanly with the other packages.
    /// </summary>
    internal static partial class FinanceAuditTrail
    {
        private static void AddResources()
        {
            AuditPd(typeof(Pc.PcResource), new[]
            {
                "Code", "Title", "Kind", "Unit", "Capacity", "CalendarId", "ResponsibleUnit", "EmployeeId", "WorkCenterId", "ProviderId", "RateResourceId", "IsActive", "Deleted",
            }, "منبع پروژه");
            AuditPd(typeof(Pc.PcResourceRate), new[]
            {
                "ResourceId", "Rate", "EffectiveFrom", "Status", "MakerUserId", "SubmittedAt", "ApprovedByUserId", "ApprovedAt", "RejectedByUserId", "RejectedAt", "RejectReason", "Deleted",
            }, "نرخ استاندارد منبع");
            SpecialProperties[(typeof(Pc.PcBaselineResource), "RateAtBaseline")] = Special.Marker;   // the rate frozen with a baseline is as private as the rate itself
            SpecialProperties[(typeof(Pc.PcResourceRate), "Rate")] = Special.Marker;   // the amount is hidden from most roles: the trail shows that it changed, never what it was
            AuditPd(typeof(Pc.PcResourcePlan), new[]
            {
                "ContractId", "Version", "Status", "MakerUserId", "PreparedByAgent", "ChangeRequestId", "SubmittedAt", "ApprovedAt", "ApprovedByUserId", "ReturnedAt", "ReturnedByUserId", "ReturnReason", "EditorUserIds", "Deleted",
            }, "طرح منابع پروژه");
            AuditPd(typeof(Pc.PcActivityResource), new[]
            {
                "PlanId", "ActivityId", "ResourceId", "Quantity", "Distribution", "ResponsibleRole", "Source", "EditedByUserId",
            }, "منبع فعالیت");
            AuditPd(typeof(Pc.PcResourcePlanSkip), new[] { "PlanId", "ActivityId", "MarkedByUserId" }, "فعالیت بدون منبع در طرح منابع");
            AuditPd(typeof(Pc.PcBaselineResource), new[] { "BaselineId", "ActivityId", "ResourceId", "Quantity", "Distribution", "RateAtBaseline" }, "منبع ثبت‌شده در خط‌مبنا");
        }
    }
}
