using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>
    /// R1c (H1, H5): the one-time, idempotent upgrade of an install that already holds project-control data. It runs once (a marker row in <c>PcSettings</c>, which the settings screen does not list),
    /// at start-up and before the first manual cost entry, and it changes only what the new rules would otherwise read wrongly:
    /// tender reviews (no contract) get the stage Tender; reviews that were already accepted freeze the terms of their contract; the response code D resets progress;
    /// the manual cost entries that exist now are marked as checked by the system (they counted as actual cost before the second check was introduced; only entries made afterwards need the second person).
    /// </summary>
    internal static class PcR1Upgrade
    {
        public const string MarkerKey = "_R1cUpgradeApplied";

        public static async Task<bool> EnsureAsync(IUnitOfWork uow, CancellationToken cancellationToken)
        {
            if (await uow.Set<PcSetting>().AnyAsync(s => s.Key == MarkerKey, cancellationToken)) return false;
            DateTimeOffset now = DateTimeOffset.Now;

            // H1: tender reviews are those without a contract (the column default gives every old row the stage Contract)
            foreach (PcContractReview r in await uow.Set<PcContractReview>().Where(r => r.ContractId == null && r.Stage != PcReviewStage.Tender).ToListAsync(cancellationToken))
                r.Stage = PcReviewStage.Tender;

            // H1: the terms of a contract whose review was accepted before the upgrade are frozen from the day of the acceptance
            var accepted = await uow.Set<PcContractReview>().AsNoTracking()
                .Where(r => r.ContractId != null && !r.Deleted && r.Decision == PcReviewDecision.Accept && r.Stage == PcReviewStage.Contract)
                .Select(r => new { r.ContractId, r.DecidedAt, r.ModifyDate }).ToListAsync(cancellationToken);
            if (accepted.Count > 0)
            {
                List<int> ids = accepted.Select(a => a.ContractId.Value).Distinct().ToList();
                foreach (PcContractTerms t in await uow.Set<PcContractTerms>().Where(t => ids.Contains(t.ContractId) && !t.Deleted && t.TermsFrozenAt == null).ToListAsync(cancellationToken))
                {
                    var a = accepted.Where(x => x.ContractId == t.ContractId).OrderBy(x => x.DecidedAt).First();
                    t.TermsFrozenAt = a.DecidedAt ?? (a.ModifyDate == default ? now : a.ModifyDate);
                }
            }

            // H1: a rejected (D) document earns no progress; the table is seeded only when empty, so an existing install keeps the old flag
            foreach (PcClientResponseCode c in await uow.Set<PcClientResponseCode>().Where(c => c.Code == "D" && !c.ResetsProgress && !c.CountsAsApproved).ToListAsync(cancellationToken))
                c.ResetsProgress = true;

            // H5: the manual cost entries that exist now keep counting; the system is the checker (user 0), the note says so
            List<int> unchecked_ = await uow.Set<CostEntry>().AsNoTracking()
                .Where(e => e.SourceType == PcProgressService.ManualCostSource && !uow.Set<PcCostCheck>().Any(c => c.CostEntryId == e.Id)).Select(e => e.Id).ToListAsync(cancellationToken);
            foreach (int id in unchecked_)
                uow.Set<PcCostCheck>().Add(new PcCostCheck { CostEntryId = id, CheckedByUserId = 0, CheckedAt = now, Note = "بررسی سیستمی هنگام ارتقا: هزینه پیش از قاعده بررسی دوم ثبت شده است" });

            // M8: before the flag existed the highest sequence of a contract was its final statement; that milestone is flagged once (when the contract has no flagged one)
            var finals = await uow.Set<PcPaymentMilestone>().Where(m => !m.Deleted).ToListAsync(cancellationToken);
            foreach (var g in finals.GroupBy(m => m.ContractId).Where(g => !g.Any(m => m.IsFinal)))
                g.OrderByDescending(m => m.Seq).First().IsFinal = true;

            uow.Set<PcSetting>().Add(new PcSetting { Key = MarkerKey, Value = 1m, Title = "ارتقای R1c انجام شد", Source = "سیستم" });
            await uow.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
