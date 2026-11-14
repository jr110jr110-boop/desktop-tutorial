using System;
using A = Entities.Model.NasbNiro.Agents;

namespace DataLayer.Context
{
    /// <summary>
    /// Agent layer: the tasks of the process agents, their events and the step switches join the tamper-evident chain (Package D mechanism): who switched an agent step on or off, who sent a task back and why,
    /// and which agent closed what. The retry counter, the next attempt time and the last error are left out to keep the volume of the chain down (they change at every attempt). The tables are new, so they
    /// are not part of the go-live baseline. In its own file so that it merges cleanly with the other packages.
    /// </summary>
    internal static partial class FinanceAuditTrail
    {
        private static void AddAgents()
        {
            AuditPd(typeof(A.AgentTask), new[]
            {
                "StepCode", "Station", "AgentCode", "Kind", "State", "TargetType", "TargetId", "ContractId", "WorkOrderId", "GateRoles", "GateUserId", "IsFallback", "OutputType", "OutputId",
                "ReturnReason", "ReturnedByUserId", "ClosedAt", "ClosedReason", "Deleted",
            }, "کار ایجنت هوش مصنوعی");
            AuditPd(typeof(A.AgentTaskEvent), null, "رویداد کار ایجنت");
            AuditPd(typeof(A.AgentStepSetting), new[] { "StepCode", "Enabled", "MaxAttempts", "GateDueHours", "Deleted" }, "کلید گام ایجنت");
        }
    }
}
