using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Engineering
{
    /// <summary>One controlled case whose documents are at risk: the early warning (an emergency should never happen).</summary>
    public sealed class EnRiskRow
    {
        public int CaseId { get; set; }
        public string Number { get; set; }
        public int? ContractId { get; set; }

        /// <summary>The case is Awarded or later (in engineering): the CEO is told while the risk exists.</summary>
        public bool Awarded { get; set; }

        public int Missing { get; set; }
        public int? DaysLeft { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();

        public string Summary()
        {
            string days = DaysLeft.HasValue ? (DaysLeft.Value >= 0 ? EnText.Num(DaysLeft.Value) + " روز تا موعد تحویل" : "موعد تحویل " + EnText.Num(-DaysLeft.Value) + " روز گذشته") : "موعد تحویل مشخص نیست";
            return "پرونده " + Number + ": " + string.Join("؛ ", Reasons) + " (" + EnText.Num(Missing) + " مدرک الزامی کم است، " + days + ").";
        }
    }

    /// <summary>The document risk of the controlled cases, computed from the records (nothing is stored). A case is at risk while a platform dossier of its standard is not confirmed Complete, its draft handover misses required documents, or its standard has no Approved reference list. Legacy records have no case and so no risk.</summary>
    internal static class EnRisk
    {
        public const string RiskKeyPrefix = "EN-RISK-";
        public const string DebtKeyPrefix = "EN-DEBT-";

        public static async Task<List<EnRiskRow>> ComputeAsync(IUnitOfWork uow, CancellationToken ct)
        {
            List<EnCase> cases = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.CeoApprovedAt != null
                && (c.Status == EnCaseStatus.TenderPreparation || c.Status == EnCaseStatus.TenderIssued || c.Status == EnCaseStatus.Awarded || c.Status == EnCaseStatus.InEngineering)).OrderBy(c => c.Id).Take(500).ToListAsync(ct);
            List<string> approved = await EnReferenceRules.ApprovedCodesAsync(uow, ct);
            List<int> contractIds = cases.Where(c => c.ContractId.HasValue).Select(c => c.ContractId.Value).ToList();
            Dictionary<int, DateTime?> delivery = (await uow.Set<PcContractTerms>().AsNoTracking().Where(t => contractIds.Contains(t.ContractId) && !t.Deleted).Select(t => new { t.ContractId, t.ContractDeliveryDate }).ToListAsync(ct))
                .GroupBy(t => t.ContractId).ToDictionary(g => g.Key, g => g.First().ContractDeliveryDate);
            List<EnRiskRow> rows = new List<EnRiskRow>();
            foreach (EnCase c in cases)
            {
                EnRiskRow row = new EnRiskRow { CaseId = c.Id, Number = c.Number, ContractId = c.ContractId, Awarded = c.Status == EnCaseStatus.Awarded || c.Status == EnCaseStatus.InEngineering };
                int missing = 0;
                if (c.Classification == EnClassification.Platform && c.ProductId.HasValue && !string.IsNullOrWhiteSpace(c.ReferenceCode))
                {
                    if (!await EnPlatformRules.IsCompleteAsync(uow, c.ProductId.Value, c.ReferenceCode, ct))
                    {
                        row.Reasons.Add("مدارک پلتفرم برای استاندارد " + c.ReferenceCode + " کامل و تایید نشده است");
                        missing = Math.Max(missing, await uow.Set<EnPlatformDocument>().AsNoTracking().CountAsync(d => d.Dossier.ProductId == c.ProductId.Value && d.Dossier.ReferenceCode == c.ReferenceCode && !d.Dossier.Deleted && d.Required && d.State == EnLineState.Missing, ct));
                    }
                }
                if (row.Awarded)
                {
                    int draft = await uow.Set<EnHandoverLine>().AsNoTracking().CountAsync(l => l.Handover.CaseId == c.Id && !l.Handover.Deleted && l.Handover.Status == EnHandoverStatus.Draft && l.Required && l.State == EnLineState.Missing, ct);
                    if (draft > 0) { row.Reasons.Add("تحویل مدارک پیش‌نویس مدرک الزامی کم دارد"); missing = Math.Max(missing, draft); }
                }
                if (!string.IsNullOrWhiteSpace(c.ReferenceCode) && !approved.Contains(c.ReferenceCode)) row.Reasons.Add("برای استاندارد " + c.ReferenceCode + " فهرست مدارک مرجع تاییدشده‌ای نیست");
                if (row.Reasons.Count == 0) continue;
                row.Missing = missing;
                if (c.ContractId.HasValue && delivery.TryGetValue(c.ContractId.Value, out DateTime? due) && due.HasValue) row.DaysLeft = (int)Math.Floor((due.Value.Date - DateTime.Today).TotalDays);
                rows.Add(row);
            }
            return rows;
        }

        public static async Task<List<(int Id, string Title)>> OverdueDebtsAsync(IUnitOfWork uow, CancellationToken ct) =>
            (await uow.Set<EnDocumentDebt>().AsNoTracking().Where(d => !d.Deleted && d.Status == EnDebtStatus.Open && d.DueAt < DateTimeOffset.Now).OrderBy(d => d.Id).Take(500).Select(d => new { d.Id, d.Title }).ToListAsync(ct)).Select(d => (d.Id, d.Title)).ToList();

        /// <summary>The source keys of the inbox items of the CEO that should be open right now: a case at risk from Awarded on, an overdue document debt.</summary>
        public static async Task<HashSet<string>> DesiredKeysAsync(IUnitOfWork uow, CancellationToken ct)
        {
            HashSet<string> keys = new HashSet<string>();
            foreach (EnRiskRow r in (await ComputeAsync(uow, ct)).Where(r => r.Awarded)) keys.Add(RiskKeyPrefix + r.CaseId.ToString());
            foreach ((int id, string _) in await OverdueDebtsAsync(uow, ct)) keys.Add(DebtKeyPrefix + id.ToString());
            return keys;
        }

        public static async Task<List<string>> OpenKeysAsync(IUnitOfWork uow, CancellationToken ct) =>
            await uow.Set<OfInboxItem>().AsNoTracking().Where(i => (i.SourceKey.StartsWith(RiskKeyPrefix) || i.SourceKey.StartsWith(DebtKeyPrefix)) && i.Status < OfInboxStatus.Done).Select(i => i.SourceKey).Distinct().ToListAsync(ct);
    }
}
