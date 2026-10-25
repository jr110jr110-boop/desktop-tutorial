using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.SalesDtos;

namespace Services.NasbNiro.Sales
{
    public interface ISlDashboardService
    {
        /// <param name="asOf">The day the week and the expiry windows are counted from; today when empty (tests and what-if views).</param>
        Task<ResponseDto<SlDashboardDto>> GetAsync(DateTime? asOf, CancellationToken ct);
    }

    /// <summary>The sales dashboard and the sales numbers of the CEO dashboard.</summary>
    public class SlDashboardService : ISlDashboardService
    {
        private static readonly SlInquiryStatus[] OpenStages = { SlInquiryStatus.Received, SlInquiryStatus.UnderReview, SlInquiryStatus.Bidding, SlInquiryStatus.Submitted };
        private static readonly SlInquiryStatus[] PreparingStages = { SlInquiryStatus.Received, SlInquiryStatus.UnderReview, SlInquiryStatus.Bidding };

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public SlDashboardService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        /// <summary>The value of an open inquiry: the latest live proposal, else the latest estimate with a price, else zero.</summary>
        private static async Task<Dictionary<int, decimal>> ValuesAsync(IUnitOfWork uow, List<int> inquiryIds, CancellationToken ct)
        {
            var proposals = await uow.Set<SlProposal>().AsNoTracking()
                .Where(p => !p.Deleted && inquiryIds.Contains(p.InquiryId) && (p.Status == SlProposalStatus.Draft || p.Status == SlProposalStatus.Approved || p.Status == SlProposalStatus.Submitted))
                .Select(p => new { p.Id, p.InquiryId, p.Price }).ToListAsync(ct);
            var estimates = await uow.Set<SlEstimate>().AsNoTracking()
                .Where(e => !e.Deleted && inquiryIds.Contains(e.InquiryId) && e.Status != SlEstimateStatus.Superseded && e.Price != null)
                .Select(e => new { e.Id, e.InquiryId, e.Price }).ToListAsync(ct);
            var result = new Dictionary<int, decimal>();
            foreach (int id in inquiryIds)
            {
                var p = proposals.Where(x => x.InquiryId == id).OrderByDescending(x => x.Id).FirstOrDefault();
                if (p != null) { result[id] = p.Price; continue; }
                var e = estimates.Where(x => x.InquiryId == id).OrderByDescending(x => x.Id).FirstOrDefault();
                result[id] = e?.Price ?? 0m;
            }
            return result;
        }

        private static async Task<(int Won, int Lost)> ResultsAsync(IUnitOfWork uow, CancellationToken ct)
        {
            int won = await uow.Set<SlProposal>().AsNoTracking().CountAsync(p => !p.Deleted && p.Status == SlProposalStatus.Won, ct);
            int lost = await uow.Set<SlProposal>().AsNoTracking().CountAsync(p => !p.Deleted && p.Status == SlProposalStatus.Lost, ct);
            return (won, lost);
        }

        public async Task<ResponseDto<SlDashboardDto>> GetAsync(DateTime? asOf, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return SlText.Bad<SlDashboardDto>(SlText.NotAllowed);
            DateTime today = (asOf ?? DateTime.Today).Date;
            var dto = new SlDashboardDto { AsOf = today, AsOfText = SlText.Date(today) };

            List<SlInquiry> open = await _uow.Set<SlInquiry>().AsNoTracking().Where(i => !i.Deleted && OpenStages.Contains(i.Status)).ToListAsync(ct);
            Dictionary<int, decimal> values = await ValuesAsync(_uow, open.Select(i => i.Id).ToList(), ct);
            dto.OpenInquiries = open.Count;
            dto.PipelineValue = values.Values.Sum();
            dto.PipelineByStage = OpenStages.Select(s => new SlStageValueDto
            {
                Name = SlText.InquiryStatus(s), Count = open.Count(i => i.Status == s), Value = open.Where(i => i.Status == s).Sum(i => values[i.Id]),
            }).ToList();
            dto.PipelineByProduct = open.GroupBy(i => i.ProductType).OrderBy(g => (int)g.Key).Select(g => new SlStageValueDto
            {
                Name = SlText.Product(g.Key), Count = g.Count(), Value = g.Sum(i => values[i.Id]),
            }).ToList();

            var (won, lost) = await ResultsAsync(_uow, ct);
            dto.Won = won;
            dto.Lost = lost;
            dto.HitRatePercent = SlProposalService.Percent(won, lost);

            DateTime weekEnd = today.AddDays(7);
            List<SlInquiry> due = open.Where(i => PreparingStages.Contains(i.Status) && i.ProposalDueDate.HasValue && i.ProposalDueDate.Value.Date >= today && i.ProposalDueDate.Value.Date <= weekEnd)
                .OrderBy(i => i.ProposalDueDate).ThenBy(i => i.Id).ToList();
            Dictionary<int, string> customers = await CustomerNamesAsync(due.Select(i => i.CustomerId), ct);
            dto.ProposalsDueThisWeekCount = due.Count;
            dto.ProposalsDueThisWeek = due.Select(i => new SlProposalDueDto
            {
                InquiryId = i.Id, InquiryNumber = i.Number, Title = i.Title, CustomerName = customers.GetValueOrDefault(i.CustomerId), DueDate = i.ProposalDueDate.Value.Date,
                DueDateText = SlText.Date(i.ProposalDueDate), Status = i.Status,
            }).ToList();

            List<SlQuotation> expiring = await _uow.Set<SlQuotation>().AsNoTracking().Where(q => !q.Deleted && q.Status == SlQuotationStatus.Sent && q.ValidUntil >= today && q.ValidUntil <= weekEnd)
                .OrderBy(q => q.ValidUntil).ThenBy(q => q.Id).ToListAsync(ct);
            Dictionary<int, string> quotationCustomers = await CustomerNamesAsync(expiring.Select(q => q.CustomerId), ct);
            dto.QuotationsExpiringCount = expiring.Count;
            dto.QuotationsExpiring = expiring.Select(q => new SlQuotationExpiringDto
            {
                QuotationId = q.Id, Number = q.Number, CustomerName = quotationCustomers.GetValueOrDefault(q.CustomerId), ValidUntil = q.ValidUntil, ValidUntilText = SlText.Date(q.ValidUntil), NetAmount = q.NetAmount,
            }).ToList();

            dto.BidDecisionsWaiting = await _uow.Set<SlBidDecision>().AsNoTracking().CountAsync(d => !d.Deleted && d.Status == SlBidStatus.WaitingDecision, ct);
            dto.ProposalsWaitingApproval = await _uow.Set<SlProposal>().AsNoTracking().CountAsync(p => !p.Deleted && p.Status == SlProposalStatus.Draft, ct);
            dto.OrdersWaitingCredit = await WaitingCreditAsync(_uow, ct);
            dto.OpenOrders = await _uow.Set<SlSalesOrder>().AsNoTracking().CountAsync(o => !o.Deleted && (o.Status == SlOrderStatus.Confirmed || o.Status == SlOrderStatus.PartlyDelivered), ct);
            return ResponseDtoHelper.Ok(dto);
        }

        private async Task<Dictionary<int, string>> CustomerNamesAsync(IEnumerable<int> ids, CancellationToken ct)
        {
            List<int> list = ids.Distinct().ToList();
            return list.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<FnCustomer>().AsNoTracking().Where(c => list.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        }

        private static Task<int> WaitingCreditAsync(IUnitOfWork uow, CancellationToken ct) =>
            uow.Set<SlSalesOrder>().AsNoTracking().CountAsync(o => !o.Deleted && o.Status == SlOrderStatus.Draft && o.CreditApprovalRequired && o.CreditApprovedNetAmount != o.NetAmount, ct);

        /// <summary>The sales numbers of the CEO dashboard. "Due soon": the proposal deadline is within three days (or past) and the proposal is not sent.</summary>
        public static async Task<SlCeoCountsDto> CeoCountsAsync(IUnitOfWork uow, DateTime asOf, CancellationToken ct)
        {
            DateTime today = asOf.Date;
            var c = new SlCeoCountsDto();
            List<SlInquiry> open = await uow.Set<SlInquiry>().AsNoTracking().Where(i => !i.Deleted && OpenStages.Contains(i.Status)).ToListAsync(ct);
            Dictionary<int, decimal> values = await ValuesAsync(uow, open.Select(i => i.Id).ToList(), ct);
            c.OpenInquiries = open.Count;
            c.PipelineValue = values.Values.Sum();
            c.ProposalsDueSoon = open.Count(i => PreparingStages.Contains(i.Status) && i.ProposalDueDate.HasValue && i.ProposalDueDate.Value.Date <= today.AddDays(3));
            c.BidDecisionsWaiting = await uow.Set<SlBidDecision>().AsNoTracking().CountAsync(d => !d.Deleted && d.Status == SlBidStatus.WaitingDecision, ct);
            c.ProposalsWaitingApproval = await uow.Set<SlProposal>().AsNoTracking().CountAsync(p => !p.Deleted && p.Status == SlProposalStatus.Draft, ct);
            c.BelowCostProposals = await uow.Set<SlProposal>().AsNoTracking().CountAsync(p => !p.Deleted && p.Status == SlProposalStatus.Draft && p.BelowCost, ct);
            c.OrdersWaitingCredit = await WaitingCreditAsync(uow, ct);
            var (won, lost) = await ResultsAsync(uow, ct);
            c.HitRatePercent = SlProposalService.Percent(won, lost);
            return c;
        }
    }
}
