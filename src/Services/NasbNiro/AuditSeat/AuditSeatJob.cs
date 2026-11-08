using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.AuditSeat;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.AuditSeatDtos;

namespace Services.NasbNiro.AuditSeat
{
    /// <summary>
    /// AUDIT (05:30): the internal auditor of the company (ISO 19011). It runs every rule of the catalogue over the data, writes findings, re-checks the actions of the owners, alerts the managers to every major finding and prepares the
    /// daily digest, the weekly report (PMO, QA) and the monthly report (CEO). Rule-based: it works without the language model; the model may only write the narrative of a report and only when the numeric check accepts it.
    /// The seat has the read-only AUDITOR role: it never approves, closes, edits or deletes anything outside its own findings and runs.
    /// </summary>
    public class AuditSeatJob : IAiSeatJob
    {
        private readonly IAusRunService _runs;
        private readonly IAusReportService _reports;
        private readonly IInventoryUserContext _user;

        public AuditSeatJob(IAusRunService runs, IAusReportService reports, IInventoryUserContext user)
        {
            _runs = runs; _reports = reports; _user = user;
        }

        public string Code => AiSeatCodes.Audit;

        public async Task<AiSeatJobResult> RunAsync(AiSeatRunContext context, CancellationToken ct)
        {
            AusRunDto run = await _runs.RunAsync(AusRunKind.Daily, _user.UserId, ct);
            if (run.Status == "Disabled") return AiSeatJobResult.Done("disabled");
            await _reports.GenerateDueAsync(context.AsOf, true, ct);
            return AiSeatJobResult.Done(run.Message);
        }
    }

    /// <summary>The audit numbers for the CEO brief and the assistant: only what the findings hold (verified by the numeric check of the answer).</summary>
    public static class AiAuditTools
    {
        public const string StatusTool = "audit_status";

        public static System.Collections.Generic.List<AiToolDefinition> Build() => new System.Collections.Generic.List<AiToolDefinition>
        {
            new AiToolDefinition
            {
                Name = StatusTool, Title = "وضعیت ممیزی داخلی", RequiredRoles = AusRoles.Readers, CitationType = "Dashboard", PageLink = "/audit",
                Description = "شمار یافته‌های باز ممیزی داخلی به تفکیک شدت (عمده، جزئی، مشاهده)، از مهلت گذشته‌ها، منتظر QA، PMO و مدیرعامل، زمان آخرین اجرا و شمار قاعده‌های فعال.",
                Run = async (c, a, ct) =>
                {
                    ResponseDto<AusStatusDto> r = await c.Get<IAusFindingService>().GetStatusAsync(ct);
                    if (!r.Success) throw new AiToolFailureException(r.Message);
                    return r.Data;
                },
            },
        };
    }
}
