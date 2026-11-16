using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.EngineeringDtos;

namespace Services.NasbNiro.Engineering
{
    public interface IEnOverviewService
    {
        Task<ResponseDto<EnOverviewDto>> GetOverviewAsync(CancellationToken ct);
    }

    /// <summary>Counts for the engineering manager, the PMO and the CEO. Read only.</summary>
    public class EnOverviewService : IEnOverviewService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public EnOverviewService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        public async Task<ResponseDto<EnOverviewDto>> GetOverviewAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnOverviewDto>(EnText.NotAllowed);
            DateTimeOffset? start = await EnControl.StartAsync(_uow, ct);
            DateTime today = DateTime.Today;
            EnOverviewDto o = new EnOverviewDto { ControlStarted = start.HasValue, ControlStartText = EnControl.Text(start) };
            var byStatus = await _uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted).GroupBy(c => c.Status).Select(g => new { Key = g.Key, Count = g.Count() }).OrderBy(g => g.Key).ToListAsync(ct);
            o.CasesByStatus = byStatus.Select(g => new EnCountDto { Key = (int)g.Key, Text = EnText.Case(g.Key), Count = g.Count }).ToList();
            var byClass = await _uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted).GroupBy(c => c.Classification).Select(g => new { Key = g.Key, Count = g.Count() }).OrderBy(g => g.Key).ToListAsync(ct);
            o.CasesByClassification = byClass.Select(g => new EnCountDto { Key = (int)g.Key, Text = EnText.Classification(g.Key), Count = g.Count }).ToList();

            o.PlatformsTotal = await _uow.Set<PbProduct>().AsNoTracking().CountAsync(p => !p.Deleted && p.IsActive, ct);
            var dossiers = await _uow.Set<EnPlatformDossier>().AsNoTracking().Where(d => !d.Deleted && _uow.Set<PbProduct>().Any(p => p.Id == d.ProductId && p.IsActive && !p.Deleted))
                .GroupBy(d => d.Status).Select(g => new { Key = g.Key, Count = g.Count() }).OrderBy(g => g.Key).ToListAsync(ct);
            var missing = await _uow.Set<EnPlatformDocument>().AsNoTracking().Where(x => x.Required && x.State == EnLineState.Missing && !x.Dossier.Deleted && _uow.Set<PbProduct>().Any(p => p.Id == x.Dossier.ProductId && p.IsActive && !p.Deleted))
                .GroupBy(x => x.Dossier.Status).Select(g => new { Key = g.Key, Count = g.Count() }).ToListAsync(ct);
            o.PlatformsByDossierStatus = dossiers.Select(g => new EnPlatformCountDto { Status = (int)g.Key, StatusText = EnText.Dossier(g.Key), Count = g.Count, MissingRequired = missing.Where(m => m.Key == g.Key).Sum(m => m.Count) }).ToList();
            int complete = await _uow.Set<PbProduct>().AsNoTracking().CountAsync(p => !p.Deleted && p.IsActive && _uow.Set<EnPlatformDossier>().Any(x => x.ProductId == p.Id && !x.Deleted)
                && !_uow.Set<EnPlatformDossier>().Any(x => x.ProductId == p.Id && !x.Deleted && x.Status != EnDossierStatus.Complete), ct);   // a product is complete when every dossier of it (one per standard) is Complete
            o.PlatformsIncomplete = Math.Max(0, o.PlatformsTotal - complete);

            o.OpenClarifications = await _uow.Set<EnClarification>().AsNoTracking().CountAsync(x => !x.Deleted && (x.Status == EnClarStatus.Draft || x.Status == EnClarStatus.WithCommercial || x.Status == EnClarStatus.SentToClient), ct);
            var waiting = await _uow.Set<EnHandover>().AsNoTracking().Where(h => !h.Deleted && (h.Status == EnHandoverStatus.Draft || h.Status == EnHandoverStatus.Submitted || h.Status == EnHandoverStatus.Accepted))
                .GroupBy(h => h.Status).Select(g => new { Key = g.Key, Count = g.Count() }).OrderBy(g => g.Key).ToListAsync(ct);
            o.HandoversWaiting = waiting.Select(g => new EnCountDto { Key = (int)g.Key, Text = EnText.Handover(g.Key), Count = g.Count }).ToList();
            o.OpenDebts = await _uow.Set<EnDocumentDebt>().AsNoTracking().CountAsync(d => !d.Deleted && d.Status == EnDebtStatus.Open, ct);
            o.OverdueDebts = await _uow.Set<EnDocumentDebt>().AsNoTracking().CountAsync(d => !d.Deleted && d.Status == EnDebtStatus.Open && d.DueAt < DateTimeOffset.Now, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            o.WaiversAwaitingCeo = await _uow.Set<EnWaiver>().AsNoTracking().CountAsync(w => !w.Deleted && w.Status == EnWaiverStatus.Requested, ct);
            DateTimeOffset? oldest = await _uow.Set<EnWaiver>().AsNoTracking().Where(w => !w.Deleted && w.Status == EnWaiverStatus.Requested).OrderBy(w => w.RequestedAt).Select(w => (DateTimeOffset?)w.RequestedAt).FirstOrDefaultAsync(ct);
            o.OldestWaiverWaitingHours = oldest.HasValue ? Math.Max(0, (int)Math.Floor((now - oldest.Value).TotalHours)) : 0;   // a request never expires: it is an alarm until the CEO decides
            o.DistributionsUnacknowledged = await _uow.Set<EnDistribution>().AsNoTracking().CountAsync(d => !d.Deleted && d.AcknowledgedAt == null && d.Status == EnDistStatus.Notified, ct);
            List<EnRiskRow> risk = await EnRisk.ComputeAsync(_uow, ct);
            o.CasesAtDocumentRisk = risk.Count; o.AwardedCasesAtDocumentRisk = risk.Count(r => r.Awarded);
            o.DeviationsAwaiting = await _uow.Set<EnDeviation>().AsNoTracking().CountAsync(x => !x.Deleted && (x.Status == EnDeviationStatus.Requested || x.Status == EnDeviationStatus.ManagerApproved), ct);
            o.StandardsDeviationsOpen = o.DeviationsAwaiting + o.WaiversAwaitingCeo;
            o.StandardGapsOpen = await _uow.Set<EnStandardGap>().AsNoTracking().CountAsync(x => !x.Deleted && x.Status == EnGapStatus.Draft, ct);
            o.InfoRequestsOpen = await _uow.Set<EnInfoRequest>().AsNoTracking().CountAsync(x => !x.Deleted && x.Status == EnInfoStatus.Open, ct);
            o.RegistrationsOpen = await _uow.Set<EnPlatformRegistration>().AsNoTracking().CountAsync(x => !x.Deleted && x.Status == EnRegStatus.Proposed, ct);
            return ResponseDtoHelper.Ok(o);
        }
    }
}
