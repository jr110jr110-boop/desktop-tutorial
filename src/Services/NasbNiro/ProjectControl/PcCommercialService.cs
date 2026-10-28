using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Entities.Model.NasbNiro.Tenders;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    public interface IPcCommercialService
    {
        Task<ResponseDto<PagedList<PcMilestoneDto>>> GetMilestonesAsync(PcContractQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcMilestoneDto>> SaveMilestoneAsync(PcMilestoneDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcMilestoneDto>> MilestoneActionAsync(PcMilestoneActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcMilestoneEvaluateResultDto>> EvaluateMilestonesAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteMilestoneAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcGuaranteeDto>>> GetGuaranteesAsync(PcGuaranteeQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcGuaranteeParseResultDto>> ParseGuaranteesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<PcGuaranteeDto>> SaveGuaranteeAsync(PcGuaranteeDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcGuaranteeDto>> AddGuaranteeEventAsync(PcGuaranteeEventSaveDto dto, CancellationToken cancellationToken);

        /// <summary>R1a: the release of a guarantee (COMMERCIAL, FINANCEMANAGER, PMO); the date and the note are required.</summary>
        Task<ResponseDto<PcGuaranteeDto>> ReleaseGuaranteeAsync(PcGuaranteeReleaseDto dto, CancellationToken cancellationToken);

        /// <summary>R1a: marks the guarantees whose expiry date has passed as Expired; the daily job calls it. Returns how many were marked.</summary>
        Task<int> ExpireGuaranteesAsync(CancellationToken cancellationToken);
        Task<List<PcGuaranteeAlertDto>> GuaranteeAlertsAsync(int? contractId, CancellationToken cancellationToken);

        Task<ResponseDto<List<PcPenaltyDto>>> ComputePenaltiesAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcPenaltyDto>>> GetPenaltiesAsync(PcContractQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcPenaltyDto>> DecidePenaltyAsync(PcPenaltyDecisionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcVariationDto>>> GetVariationsAsync(PcContractQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcVariationDto>> SaveVariationAsync(PcVariationDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcVariationDto>> VariationActionAsync(PcVariationActionDto dto, CancellationToken cancellationToken);
    }

    /// <summary>Payment milestones, guarantees (typed side data), delay penalties and variation orders.</summary>
    public class PcCommercialService : IPcCommercialService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IPcProgressService _progress;

        public PcCommercialService(IUnitOfWork uow, IInventoryUserContext user, IPcProgressService progress)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => PcText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => PcText.Bad<T>(m);

        private async Task<decimal> ContractValueAsync(int contractId, CancellationToken cancellationToken)
        {
            PcContractTerms t = await PcContext.TermsAsync(_uow, contractId, false, cancellationToken);
            decimal v = t?.ValueExVat ?? 0m;
            if (v == 0m)
            {
                decimal? price = await _uow.Set<ContractEntity>().Where(c => c.Id == contractId).Select(c => c.Price).FirstOrDefaultAsync(cancellationToken);
                v = price ?? 0m;
            }
            decimal variations = await _uow.Set<PcVariationOrder>().Where(x => x.ContractId == contractId && !x.Deleted && x.Status == PcVariationStatus.Approved).SumAsync(x => (decimal?)x.PriceDelta, cancellationToken) ?? 0m;
            return v + variations;
        }

        // ============================================================================ payment milestones

        private static PcMilestoneDto ToDto(PcPaymentMilestone m) => new PcMilestoneDto
        {
            Id = m.Id, ContractId = m.ContractId, Seq = m.Seq, Title = m.Title, Kind = m.Kind, Trigger = m.Trigger, TriggerPercent = m.TriggerPercent, WorkOrderId = m.WorkOrderId,
            ActivityId = m.ActivityId, PercentOfValue = m.PercentOfValue, Amount = m.Amount, PlannedDate = m.PlannedDate, Status = m.Status, DueAt = m.DueAt, PaidAt = m.PaidAt,
            ProvisionalStatementId = m.ProvisionalStatementId, FinanceRef = m.FinanceRef, ClientLetterRef = m.ClientLetterRef, RowVersion = m.RowVersion == null ? null : Convert.ToBase64String(m.RowVersion),
        };

        public async Task<ResponseDto<PagedList<PcMilestoneDto>>> GetMilestonesAsync(PcContractQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcContractQuery();
            IQueryable<PcPaymentMilestone> q = _uow.Set<PcPaymentMilestone>().AsNoTracking().Where(m => !m.Deleted);
            if (query.ContractId.HasValue) q = q.Where(m => m.ContractId == query.ContractId.Value);
            IQueryable<PcMilestoneDto> projection = q.OrderBy(m => m.ContractId).ThenBy(m => m.Seq).Select(m => new PcMilestoneDto
            {
                Id = m.Id, ContractId = m.ContractId, Seq = m.Seq, Title = m.Title, Kind = m.Kind, Trigger = m.Trigger, TriggerPercent = m.TriggerPercent, WorkOrderId = m.WorkOrderId,
                ActivityId = m.ActivityId, PercentOfValue = m.PercentOfValue, Amount = m.Amount, PlannedDate = m.PlannedDate, Status = m.Status, DueAt = m.DueAt, PaidAt = m.PaidAt,
                ProvisionalStatementId = m.ProvisionalStatementId, FinanceRef = m.FinanceRef, ClientLetterRef = m.ClientLetterRef,
            });
            return ResponseDtoHelper.Ok(await PagedList<PcMilestoneDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<PcMilestoneDto>> SaveMilestoneAsync(PcMilestoneDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Title) == null) return Bad<PcMilestoneDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Commercial)) return Bad<PcMilestoneDto>(PcText.NotAllowed);
            if (!Enum.IsDefined(typeof(PcMilestoneKind), dto.Kind) || !Enum.IsDefined(typeof(PcMilestoneTrigger), dto.Trigger)) return Bad<PcMilestoneDto>("نوع و محرک مرحله پرداخت را مشخص کنید.");
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken)) return PcText.NotFound<PcMilestoneDto>(PcText.ContractNotFound);
            if (dto.PercentOfValue.HasValue && (dto.PercentOfValue.Value <= 0 || dto.PercentOfValue.Value > 100)) return Bad<PcMilestoneDto>("درصد مبلغ قرارداد باید بین ۰ و ۱۰۰ باشد.");
            if (dto.Amount < 0) return Bad<PcMilestoneDto>("مبلغ منفی نیست.");
            if (dto.Trigger == PcMilestoneTrigger.ProgressPercent && (!dto.TriggerPercent.HasValue || dto.TriggerPercent.Value <= 0 || dto.TriggerPercent.Value > 100)) return Bad<PcMilestoneDto>("درصد پیشرفت محرک را وارد کنید.");
            if (dto.Trigger == PcMilestoneTrigger.Date && !dto.PlannedDate.HasValue) return Bad<PcMilestoneDto>("تاریخ برنامه‌ای را وارد کنید.");
            if ((dto.Trigger == PcMilestoneTrigger.ReleaseNote || dto.Trigger == PcMilestoneTrigger.ShipmentDelivered || dto.Trigger == PcMilestoneTrigger.HandoverMinutes) && !dto.WorkOrderId.HasValue) return Bad<PcMilestoneDto>("برای این محرک دستور کار را مشخص کنید.");
            if (dto.Trigger == PcMilestoneTrigger.MilestoneDone && !dto.ActivityId.HasValue) return Bad<PcMilestoneDto>("فعالیت نقطه عطف را مشخص کنید.");
            if (dto.WorkOrderId.HasValue && !await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && w.ContractId == dto.ContractId && !w.Deleted, cancellationToken)) return Bad<PcMilestoneDto>("دستور کار متعلق به این قرارداد نیست.");
            if (dto.ActivityId.HasValue && !await _uow.Set<PcActivity>().AnyAsync(a => a.Id == dto.ActivityId.Value && a.ContractId == dto.ContractId && !a.Deleted, cancellationToken)) return Bad<PcMilestoneDto>("فعالیت متعلق به این قرارداد نیست.");

            decimal amount = dto.Amount;
            if (dto.PercentOfValue.HasValue)
            {
                decimal value = await ContractValueAsync(dto.ContractId, cancellationToken);
                if (value <= 0) return Bad<PcMilestoneDto>("برای محاسبه از درصد، ابتدا مبلغ قرارداد را در شرایط قرارداد ثبت کنید.");
                amount = PcText.Round2(value * dto.PercentOfValue.Value / 100m);
            }
            DateTimeOffset now = DateTimeOffset.Now;
            PcPaymentMilestone m;
            if (dto.Id.HasValue)
            {
                m = await _uow.Set<PcPaymentMilestone>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (m == null) return PcText.NotFound<PcMilestoneDto>("مرحله پرداخت پیدا نشد.");
                if (m.Status != PcMilestoneStatus.Planned) return Bad<PcMilestoneDto>("مرحله‌ای که سررسید شده تغییر نمی‌کند.");
            }
            else
            {
                int seq = (await _uow.Set<PcPaymentMilestone>().Where(x => x.ContractId == dto.ContractId).MaxAsync(x => (int?)x.Seq, cancellationToken) ?? 0) + 1;
                m = new PcPaymentMilestone { ContractId = dto.ContractId, Seq = seq, CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcPaymentMilestone>().Add(m);
            }
            m.Title = Clean(dto.Title); m.Kind = dto.Kind; m.Trigger = dto.Trigger; m.TriggerPercent = dto.TriggerPercent; m.WorkOrderId = dto.WorkOrderId; m.ActivityId = dto.ActivityId;
            m.PercentOfValue = dto.PercentOfValue; m.Amount = amount; m.PlannedDate = dto.PlannedDate?.Date;
            m.ModifyDate = now; m.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcMilestoneDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(m));
        }

        public async Task<ResponseDto<bool>> DeleteMilestoneAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProjectControlRoles.Commercial)) return Bad<bool>(PcText.NotAllowed);
            PcPaymentMilestone m = await _uow.Set<PcPaymentMilestone>().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (m == null) return PcText.NotFound<bool>("مرحله پرداخت پیدا نشد.");
            if (m.Status != PcMilestoneStatus.Planned) return Bad<bool>("مرحله سررسیدشده حذف نمی‌شود.");
            m.Delete(); m.ModifyDate = DateTimeOffset.Now; m.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(true);
        }

        private async Task<bool> TriggerReachedAsync(PcPaymentMilestone m, DateTime today, CancellationToken cancellationToken)
        {
            switch (m.Trigger)
            {
                case PcMilestoneTrigger.Date:
                    return m.PlannedDate.HasValue && m.PlannedDate.Value.Date <= today;
                case PcMilestoneTrigger.ProgressPercent:
                    decimal p = await _progress.PercentAsync(m.ContractId, m.WorkOrderId, cancellationToken);
                    return m.TriggerPercent.HasValue && p >= m.TriggerPercent.Value;
                case PcMilestoneTrigger.MilestoneDone:
                    return m.ActivityId.HasValue && await _uow.Set<PcActivity>().AnyAsync(a => a.Id == m.ActivityId.Value && a.Status == PcActivityStatus.Done, cancellationToken);
                case PcMilestoneTrigger.ReleaseNote:
                    return m.WorkOrderId.HasValue && await _uow.Set<ReleaseNote>().AnyAsync(r => r.WorkOrderId == m.WorkOrderId.Value && !r.Deleted && r.Status == ReleaseNoteStatus.Released, cancellationToken);
                case PcMilestoneTrigger.ShipmentDelivered:
                    return m.WorkOrderId.HasValue && await _uow.Set<PcShipment>().AnyAsync(s => s.WorkOrderId == m.WorkOrderId.Value && !s.Deleted && s.Status >= PcShipmentStatus.Delivered, cancellationToken);
                case PcMilestoneTrigger.HandoverMinutes:
                    return m.WorkOrderId.HasValue && await _uow.Set<PcShipment>().AnyAsync(s => s.WorkOrderId == m.WorkOrderId.Value && !s.Deleted && s.Status == PcShipmentStatus.HandoverReceived, cancellationToken);
                case PcMilestoneTrigger.ClientLetter:
                    return Clean(m.ClientLetterRef) != null;
                default:
                    return false;
            }
        }

        public async Task<ResponseDto<PcMilestoneEvaluateResultDto>> EvaluateMilestonesAsync(int contractId, CancellationToken cancellationToken)
        {
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == contractId && !c.Deleted, cancellationToken)) return PcText.NotFound<PcMilestoneEvaluateResultDto>(PcText.ContractNotFound);
            List<PcPaymentMilestone> list = await _uow.Set<PcPaymentMilestone>().Where(m => m.ContractId == contractId && !m.Deleted).OrderBy(m => m.Seq).ToListAsync(cancellationToken);
            DateTime today = DateTime.Today;
            DateTimeOffset now = DateTimeOffset.Now;
            int became = 0;
            foreach (PcPaymentMilestone m in list.Where(x => x.Status == PcMilestoneStatus.Planned))
            {
                if (!await TriggerReachedAsync(m, today, cancellationToken)) continue;
                m.Status = PcMilestoneStatus.Due; m.DueAt = now; m.ModifyDate = now; m.ModifierUserId = Me;
                became++;
            }
            if (became > 0) await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(new PcMilestoneEvaluateResultDto { ContractId = contractId, BecameDue = became, Milestones = list.Select(ToDto).ToList() });
        }

        public async Task<ResponseDto<PcMilestoneDto>> MilestoneActionAsync(PcMilestoneActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcMilestoneDto>(PcText.InvalidData);
            if (!_user.IsInRole("COMMERCIAL,FINANCIAL,PMO")) return Bad<PcMilestoneDto>(PcText.NotAllowed);
            PcPaymentMilestone m = await _uow.Set<PcPaymentMilestone>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (m == null) return PcText.NotFound<PcMilestoneDto>("مرحله پرداخت پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "client-letter":
                    if (Clean(dto.Reference) == null) return Bad<PcMilestoneDto>("شماره نامه کارفرما را وارد کنید.");
                    m.ClientLetterRef = Clean(dto.Reference);
                    if (m.Trigger == PcMilestoneTrigger.ClientLetter && m.Status == PcMilestoneStatus.Planned) { m.Status = PcMilestoneStatus.Due; m.DueAt = now; }
                    break;
                case "statement":
                    if (m.Status != PcMilestoneStatus.Due) return Bad<PcMilestoneDto>("فقط مرحله سررسیدشده صورت‌وضعیت می‌گیرد.");
                    if (dto.ProvisionalStatementId.HasValue && !await _uow.Set<ProvisionalStatement>().AnyAsync(s => s.Id == dto.ProvisionalStatementId.Value && s.ContractId == m.ContractId, cancellationToken)) return Bad<PcMilestoneDto>("صورت‌وضعیت پیدا نشد.");
                    m.ProvisionalStatementId = dto.ProvisionalStatementId; m.Status = PcMilestoneStatus.StatementIssued;
                    break;
                case "invoiced":
                    if (m.Status != PcMilestoneStatus.StatementIssued) return Bad<PcMilestoneDto>("ابتدا صورت‌وضعیت باید صادر شود.");
                    m.FinanceRef = Clean(dto.Reference); m.Status = PcMilestoneStatus.Invoiced;
                    break;
                case "paid":
                    if (m.Status != PcMilestoneStatus.Invoiced) return Bad<PcMilestoneDto>("ابتدا فاکتور باید ثبت شود.");
                    m.PaidAt = now; m.Status = PcMilestoneStatus.Paid;
                    break;
                default:
                    return Bad<PcMilestoneDto>("عملیات نامعتبر است.");
            }
            m.ModifyDate = now; m.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcMilestoneDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(m));
        }

        // ============================================================================ guarantees

        private static readonly Regex NumberRegex = new Regex(@"[0-9]+(?:\.[0-9]+)?", RegexOptions.Compiled);

        internal static string NormalizeDigits(string text)
        {
            if (text == null) return null;
            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c >= '۰' && c <= '۹') chars[i] = (char)('0' + (c - '۰'));
                else if (c >= '٠' && c <= '٩') chars[i] = (char)('0' + (c - '٠'));
                else if (c == '٫') chars[i] = '.';
                else if (c == ',' || c == '٬' || c == '،' || c == ' ' || c == '‌') chars[i] = '\u0000';
            }
            return new string(chars.Where(c => c != '\u0000').ToArray());
        }

        /// <summary>Reads the old free-text amount (Persian digits, separators, words around it). Null when no number is found.</summary>
        internal static decimal? ParseAmount(string text)
        {
            string s = NormalizeDigits(text);
            if (string.IsNullOrWhiteSpace(s)) return null;
            Match m = NumberRegex.Match(s);
            if (!m.Success) return null;
            return decimal.TryParse(m.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v) && v > 0 ? v : (decimal?)null;
        }

        private static PcGuaranteeType GuessType(string title)
        {
            string t = title ?? string.Empty;
            if (t.Contains("پیش")) return PcGuaranteeType.Advance;
            if (t.Contains("حسن")) return PcGuaranteeType.Performance;
            if (t.Contains("مناقصه") || t.Contains("شرکت")) return PcGuaranteeType.Bid;
            if (t.Contains("گارانتی") || t.Contains("ضمانت کیفیت")) return PcGuaranteeType.Warranty;
            return PcGuaranteeType.Other;
        }

        private PcGuaranteeInfo InfoFromLegacy(PcGuaranteeSource kind, int sourceId, int? contractId, string title, string bank, string price, DateTime? regDate, DateTime? dueDate, DateTime? cancelDate, DateTimeOffset now)
        {
            decimal? amount = ParseAmount(price);
            return new PcGuaranteeInfo
            {
                SourceKind = kind, SourceId = sourceId, ContractId = contractId, GuaranteeType = GuessType(title), AmountDecimal = amount, CurrencyCode = "IRR",
                IssuerKind = PcGuaranteeIssuer.Bank, ValidFrom = regDate?.Date, ExpiryDate = dueDate?.Date, ReleaseTrigger = PcGuaranteeRelease.FinalDelivery,
                Status = cancelDate.HasValue ? PcGuaranteeStatus.Cancelled : PcGuaranteeStatus.Active, ParseFailed = amount == null && !string.IsNullOrWhiteSpace(price),
                ParseNote = amount == null && !string.IsNullOrWhiteSpace(price) ? "مبلغ متنی خوانده نشد: " + price.Trim() : null, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
            };
        }

        public async Task<ResponseDto<PcGuaranteeParseResultDto>> ParseGuaranteesAsync(CancellationToken cancellationToken)
        {
            if (!_user.IsInRole("COMMERCIAL,FINANCIAL,PMO")) return Bad<PcGuaranteeParseResultDto>(PcText.NotAllowed);
            DateTimeOffset now = DateTimeOffset.Now;
            var result = new PcGuaranteeParseResultDto();
            HashSet<int> haveContract = (await _uow.Set<PcGuaranteeInfo>().Where(i => i.SourceKind == PcGuaranteeSource.ContractGuaranty).Select(i => i.SourceId).ToListAsync(cancellationToken)).ToHashSet();
            HashSet<int> haveTender = (await _uow.Set<PcGuaranteeInfo>().Where(i => i.SourceKind == PcGuaranteeSource.TenderGuaranty).Select(i => i.SourceId).ToListAsync(cancellationToken)).ToHashSet();
            List<ContractGuaranty> cg = await _uow.Set<ContractGuaranty>().AsNoTracking().Where(g => !g.Deleted).ToListAsync(cancellationToken);
            foreach (ContractGuaranty g in cg.Where(g => !haveContract.Contains(g.Id)))
            {
                PcGuaranteeInfo info = InfoFromLegacy(PcGuaranteeSource.ContractGuaranty, g.Id, g.ContractEntityId, g.Title, g.Bank, g.Price, g.RegDate, g.DueDate, g.CancelDate, now);
                _uow.Set<PcGuaranteeInfo>().Add(info);
                result.Created++; if (info.ParseFailed) result.Failed++; else result.Parsed++;
            }
            List<TenderGuaranty> tg = await _uow.Set<TenderGuaranty>().AsNoTracking().Where(g => !g.Deleted).ToListAsync(cancellationToken);
            foreach (TenderGuaranty g in tg.Where(g => !haveTender.Contains(g.Id)))
            {
                PcGuaranteeInfo info = InfoFromLegacy(PcGuaranteeSource.TenderGuaranty, g.Id, null, g.Title, g.Bank, g.Price, g.RegDate, g.DueDate, g.CancelDate, now);
                _uow.Set<PcGuaranteeInfo>().Add(info);
                result.Created++; if (info.ParseFailed) result.Failed++; else result.Parsed++;
            }
            if (result.Created > 0) await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(result);
        }

        private async Task<List<PcGuaranteeDto>> GuaranteeDtosAsync(List<PcGuaranteeInfo> infos, bool withEvents, CancellationToken cancellationToken)
        {
            List<int> contractIds = infos.Where(i => i.SourceKind == PcGuaranteeSource.ContractGuaranty).Select(i => i.SourceId).ToList();
            List<int> tenderIds = infos.Where(i => i.SourceKind == PcGuaranteeSource.TenderGuaranty).Select(i => i.SourceId).ToList();
            var contractRows = await _uow.Set<ContractGuaranty>().AsNoTracking().Where(g => contractIds.Contains(g.Id)).Select(g => new { g.Id, g.Title, g.Bank, g.No, g.Price }).ToListAsync(cancellationToken);
            var tenderRows = await _uow.Set<TenderGuaranty>().AsNoTracking().Where(g => tenderIds.Contains(g.Id)).Select(g => new { g.Id, g.Title, g.Bank, g.No, g.Price }).ToListAsync(cancellationToken);
            List<int> infoIds = infos.Select(i => i.Id).ToList();
            List<PcGuaranteeEvent> events = withEvents ? await _uow.Set<PcGuaranteeEvent>().AsNoTracking().Where(e => infoIds.Contains(e.GuaranteeInfoId)).OrderBy(e => e.Id).ToListAsync(cancellationToken) : new List<PcGuaranteeEvent>();
            DateTime today = DateTime.Today;
            var list = new List<PcGuaranteeDto>();
            foreach (PcGuaranteeInfo i in infos)
            {
                string title = null, bank = null, no = null, price = null;
                if (i.SourceKind == PcGuaranteeSource.ContractGuaranty) { var r = contractRows.FirstOrDefault(x => x.Id == i.SourceId); title = r?.Title; bank = r?.Bank; no = r?.No; price = r?.Price; }
                else { var r = tenderRows.FirstOrDefault(x => x.Id == i.SourceId); title = r?.Title; bank = r?.Bank; no = r?.No; price = r?.Price; }
                list.Add(new PcGuaranteeDto
                {
                    Id = i.Id, SourceKind = i.SourceKind, SourceId = i.SourceId, ContractId = i.ContractId, Title = title, Bank = bank, No = no, LegacyPriceText = price,
                    GuaranteeType = i.GuaranteeType, AmountDecimal = i.AmountDecimal, CurrencyCode = i.CurrencyCode, IssuerKind = i.IssuerKind, ValidFrom = i.ValidFrom, ExpiryDate = i.ExpiryDate,
                    ReleaseTrigger = i.ReleaseTrigger, Status = i.Status, ParseFailed = i.ParseFailed, ParseNote = i.ParseNote,
                    DaysToExpiry = i.ExpiryDate.HasValue ? (int)(i.ExpiryDate.Value.Date - today).TotalDays : (int?)null,
                    ReleaseRequestedByUserId = i.ReleaseRequestedByUserId, ReleaseRequestedAt = i.ReleaseRequestedAt,
                    Events = events.Where(e => e.GuaranteeInfoId == i.Id).Select(e => new PcGuaranteeEventDto { Id = e.Id, Kind = e.Kind, EventDate = e.EventDate, NewExpiryDate = e.NewExpiryDate, Fee = e.Fee, DocumentId = e.DocumentId, Note = e.Note }).ToList(),
                });
            }
            return list;
        }

        public async Task<ResponseDto<PagedList<PcGuaranteeDto>>> GetGuaranteesAsync(PcGuaranteeQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcGuaranteeQuery();
            IQueryable<PcGuaranteeInfo> q = _uow.Set<PcGuaranteeInfo>().AsNoTracking().Where(i => !i.Deleted);
            if (query.ContractId.HasValue) q = q.Where(i => i.ContractId == query.ContractId.Value);
            if (query.Status.HasValue) q = q.Where(i => i.Status == query.Status.Value);
            if (query.OnlyUnparsed) q = q.Where(i => i.ParseFailed);
            PagedList<PcGuaranteeInfo> page = await PagedList<PcGuaranteeInfo>.CreateAsync(q.OrderBy(i => i.ExpiryDate == null).ThenBy(i => i.ExpiryDate).ThenBy(i => i.Id), query.PageNumber, query.PageSize, cancellationToken);
            List<PcGuaranteeDto> dtos = await GuaranteeDtosAsync(page.ToList(), true, cancellationToken);
            return ResponseDtoHelper.Ok((PagedList<PcGuaranteeDto>)new PcPage<PcGuaranteeDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcGuaranteeDto>> SaveGuaranteeAsync(PcGuaranteeDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcGuaranteeDto>(PcText.InvalidData);
            if (!_user.IsInRole("COMMERCIAL,FINANCIAL,PMO")) return Bad<PcGuaranteeDto>(PcText.NotAllowed);
            if (!Enum.IsDefined(typeof(PcGuaranteeType), dto.GuaranteeType) || !Enum.IsDefined(typeof(PcGuaranteeIssuer), dto.IssuerKind) || !Enum.IsDefined(typeof(PcGuaranteeRelease), dto.ReleaseTrigger)) return Bad<PcGuaranteeDto>("نوع، صادرکننده و شرط آزادسازی را مشخص کنید.");
            if (dto.AmountDecimal.HasValue && dto.AmountDecimal.Value < 0) return Bad<PcGuaranteeDto>("مبلغ منفی نیست.");
            if (dto.ValidFrom.HasValue && dto.ExpiryDate.HasValue && dto.ExpiryDate.Value < dto.ValidFrom.Value) return Bad<PcGuaranteeDto>("تاریخ انقضا قبل از شروع اعتبار است.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcGuaranteeInfo info;
            if (dto.Id.HasValue) info = await _uow.Set<PcGuaranteeInfo>().FirstOrDefaultAsync(i => i.Id == dto.Id.Value && !i.Deleted, cancellationToken);
            else info = await _uow.Set<PcGuaranteeInfo>().FirstOrDefaultAsync(i => i.SourceKind == dto.SourceKind && i.SourceId == dto.SourceId && !i.Deleted, cancellationToken);
            // R1a (finding M7): Released and Expired are never set from here; the release has its own action and the expiry is automatic
            string statusProblem = StatusChangeProblem(info?.Status ?? PcGuaranteeStatus.Requested, dto.Status);
            if (statusProblem != null) return Bad<PcGuaranteeDto>(statusProblem);
            // R1a2 (M2): once the guarantee is issued its expiry date is what the bank holds; it changes only through an extension event that carries its evidence (a note and, when there is one, the letter).
            // A date that is still missing (an old row the parser could not read) may be filled in once.
            if (info != null && info.Status != PcGuaranteeStatus.Requested && info.ExpiryDate.HasValue && dto.ExpiryDate?.Date != info.ExpiryDate.Value.Date)
                return Bad<PcGuaranteeDto>("پس از صدور ضمانت‌نامه، تاریخ انقضا فقط با رویداد «تمدید» (با توضیح و در صورت وجود، فایل نامه) تغییر می‌کند.");
            if (info == null)
            {
                bool exists = dto.SourceKind == PcGuaranteeSource.ContractGuaranty
                    ? await _uow.Set<ContractGuaranty>().AnyAsync(g => g.Id == dto.SourceId && !g.Deleted, cancellationToken)
                    : await _uow.Set<TenderGuaranty>().AnyAsync(g => g.Id == dto.SourceId && !g.Deleted, cancellationToken);
                if (!exists) return PcText.NotFound<PcGuaranteeDto>("ضمانت‌نامه پیدا نشد.");
                int? contractId = null;
                if (dto.SourceKind == PcGuaranteeSource.ContractGuaranty) contractId = await _uow.Set<ContractGuaranty>().Where(g => g.Id == dto.SourceId).Select(g => (int?)g.ContractEntityId).FirstOrDefaultAsync(cancellationToken);
                info = new PcGuaranteeInfo { SourceKind = dto.SourceKind, SourceId = dto.SourceId, ContractId = contractId, CreateDate = now, CreatorUserId = Me, Status = PcGuaranteeStatus.Requested };
                _uow.Set<PcGuaranteeInfo>().Add(info);
            }
            info.GuaranteeType = dto.GuaranteeType; info.AmountDecimal = dto.AmountDecimal; info.CurrencyCode = PcText.Upper(dto.CurrencyCode) ?? "IRR"; info.IssuerKind = dto.IssuerKind;
            info.ValidFrom = dto.ValidFrom?.Date; info.ExpiryDate = dto.ExpiryDate?.Date; info.ReleaseTrigger = dto.ReleaseTrigger;
            if (Enum.IsDefined(typeof(PcGuaranteeStatus), dto.Status) && dto.Status != 0) info.Status = dto.Status;   // only a status the rule above allowed (or the current one)
            if (dto.AmountDecimal.HasValue) { info.ParseFailed = false; info.ParseNote = null; }
            info.ModifyDate = now; info.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Bad<PcGuaranteeDto>("برای این ضمانت‌نامه قبلاً اطلاعات ثبت شده است."); }
            return ResponseDtoHelper.Ok((await GuaranteeDtosAsync(new List<PcGuaranteeInfo> { info }, true, cancellationToken)).First());
        }

        /// <summary>
        /// The status a person may ask for through the save call. A status that does not change is always accepted (the form sends the current one back);
        /// Released comes only from the release action, Expired only from the daily job, and a released guarantee is not opened again.
        /// </summary>
        internal static string StatusChangeProblem(PcGuaranteeStatus current, PcGuaranteeStatus requested)
        {
            if (requested == 0 || !Enum.IsDefined(typeof(PcGuaranteeStatus), requested) || requested == current) return null;
            if (requested == PcGuaranteeStatus.Released) return "آزادسازی ضمانت‌نامه از این مسیر ثبت نمی‌شود؛ از عمل «آزادسازی» (با تاریخ و توضیح) استفاده کنید.";
            if (requested == PcGuaranteeStatus.Expired) return "انقضای ضمانت‌نامه پس از گذشتن تاریخ انقضا به‌صورت خودکار ثبت می‌شود؛ تاریخ انقضا را اصلاح یا ضمانت‌نامه را تمدید کنید.";
            // R1a2 (M2): the cancellation is an event of its own (date, note, two people), not a status that one person types in
            if (requested == PcGuaranteeStatus.Cancelled) return "ابطال ضمانت‌نامه از این مسیر ثبت نمی‌شود؛ از رویداد «ابطال» (با تاریخ، توضیح و تأیید شخص دوم) استفاده کنید.";
            if (current == PcGuaranteeStatus.Released) return "ضمانت‌نامه آزادشده دوباره باز نمی‌شود.";
            if (current == PcGuaranteeStatus.Cancelled) return "ضمانت‌نامه ابطال‌شده دوباره باز نمی‌شود.";
            // an expired guarantee leaves that status only through an extension event (evidence and a new date) or the release; any other status would hide it from the alerts
            if (current == PcGuaranteeStatus.Expired)
                return "ضمانت‌نامه منقضی‌شده با تغییر وضعیت دوباره فعال نمی‌شود؛ آن را با رویداد «تمدید» (با توضیح و تاریخ جدید) تمدید کنید.";
            return null;
        }

        /// <summary>Roles of the release and the cancellation: the money side signs these too, so the list differs from the other guarantee actions (★ to be confirmed by the company).</summary>
        private const string CloseRoles = "COMMERCIAL,FINANCEMANAGER,PMO";

        /// <summary>A request that nobody confirmed within this many days is forgotten: the next call records a new one (the older values stay in the audit trail).</summary>
        private const int RequestValidDays = 7;

        /// <summary>
        /// R1a2 (M2): the release and the cancellation of a guarantee need two people. The first call records who asked and when and changes nothing else; a call by a different user of
        /// <see cref="CloseRoles"/> confirms, and the action then runs with the date and the note of the confirming call. Returns null when the caller confirms (the action goes on);
        /// otherwise the answer to give: the request that was recorded, or the refusal of a confirmation by the one who asked.
        /// </summary>
        private async Task<ResponseDto<PcGuaranteeDto>> FirstOfTwoAsync(PcGuaranteeInfo info, string what, DateTimeOffset now, CancellationToken cancellationToken)
        {
            bool open = info.ReleaseRequestedByUserId != null && info.ReleaseRequestedAt != null && info.ReleaseRequestedAt.Value.AddDays(RequestValidDays) >= now.DateTime;
            if (!open)
            {
                info.ReleaseRequestedByUserId = Me; info.ReleaseRequestedAt = now.DateTime;
                info.ModifyDate = now; info.ModifierUserId = Me;
                try { await _uow.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateConcurrencyException) { return Bad<PcGuaranteeDto>(PcText.Concurrency); }
                PcGuaranteeDto recorded = (await GuaranteeDtosAsync(new List<PcGuaranteeInfo> { info }, true, cancellationToken)).First();
                return ResponseDtoHelper.Ok(recorded, $"درخواست {what} ثبت شد و منتظر تأیید شخص دیگری از بازرگانی، مدیر مالی یا PMO است (تفکیک وظایف)؛ این درخواست {RequestValidDays} روز معتبر است و تا آن زمان ضمانت‌نامه تغییر نمی‌کند.");
            }
            if (info.ReleaseRequestedByUserId == Me)
                return Bad<PcGuaranteeDto>($"درخواست {what} را خودتان ثبت کرده‌اید؛ تأیید آن باید از شخص دیگری باشد (تفکیک وظایف).");
            return null;
        }

        public async Task<ResponseDto<PcGuaranteeDto>> AddGuaranteeEventAsync(PcGuaranteeEventSaveDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || !Enum.IsDefined(typeof(PcGuaranteeEventKind), dto.Kind)) return Bad<PcGuaranteeDto>(PcText.InvalidData);
            bool cancellation = dto.Kind == PcGuaranteeEventKind.Cancellation;
            if (!_user.IsInRole(cancellation ? CloseRoles : "COMMERCIAL,FINANCIAL,PMO"))
                return Bad<PcGuaranteeDto>(cancellation ? PcText.NotAllowed + " (ابطال ضمانت‌نامه با بازرگانی، مدیر مالی یا PMO است)" : PcText.NotAllowed);
            // R1a: these two have their own paths (the release action with its date and note, and the daily expiry job); here they would only leave a row without a state change
            if (dto.Kind == PcGuaranteeEventKind.Release) return Bad<PcGuaranteeDto>("آزادسازی ضمانت‌نامه با عمل «آزادسازی» (guarantees/release) ثبت می‌شود.");
            if (dto.Kind == PcGuaranteeEventKind.Expiry) return Bad<PcGuaranteeDto>("انقضای ضمانت‌نامه به‌صورت خودکار ثبت می‌شود.");
            PcGuaranteeInfo info = await _uow.Set<PcGuaranteeInfo>().FirstOrDefaultAsync(i => i.Id == dto.GuaranteeId && !i.Deleted, cancellationToken);
            if (info == null) return PcText.NotFound<PcGuaranteeDto>("اطلاعات ضمانت‌نامه پیدا نشد.");
            if (info.Status == PcGuaranteeStatus.Cancelled || info.Status == PcGuaranteeStatus.Released) return Bad<PcGuaranteeDto>("ضمانت‌نامه بسته شده است.");
            if (dto.Fee.HasValue && dto.Fee.Value < 0) return Bad<PcGuaranteeDto>("کارمزد منفی نیست.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Kind)
            {
                case PcGuaranteeEventKind.Issue:
                    info.Status = PcGuaranteeStatus.Active;
                    break;
                case PcGuaranteeEventKind.Extension:
                    if (!dto.NewExpiryDate.HasValue || (info.ExpiryDate.HasValue && dto.NewExpiryDate.Value.Date <= info.ExpiryDate.Value.Date)) return Bad<PcGuaranteeDto>("تاریخ جدید انقضا باید بعد از تاریخ فعلی باشد.");
                    // R1a2 (M2): the extension is the only way the expiry date changes after the issue, so it carries its evidence: a note, and the letter when there is one
                    if (Clean(dto.Note) == null) return Bad<PcGuaranteeDto>("برای تمدید ضمانت‌نامه، توضیح (مثلاً شماره و تاریخ نامه یا الحاقیه‌ی بانک) را بنویسید.");
                    if (dto.DocumentId.HasValue && !await _uow.Set<Entities.Model.Document>().AnyAsync(d => d.Id == dto.DocumentId.Value, cancellationToken)) return Bad<PcGuaranteeDto>("فایل نامه‌ی پیوست پیدا نشد.");
                    info.ExpiryDate = dto.NewExpiryDate.Value.Date; info.Status = PcGuaranteeStatus.Active;
                    break;
                case PcGuaranteeEventKind.Cancellation:
                    // R1a2 (M2): the same rules as the release: allowed roles, a date that is not in the future, a note, and a second person
                    if (dto.EventDate == default) return Bad<PcGuaranteeDto>("تاریخ ابطال را وارد کنید.");
                    if (dto.EventDate.Date > DateTime.Today) return Bad<PcGuaranteeDto>("تاریخ ابطال نمی‌تواند در آینده باشد.");
                    if (Clean(dto.Note) == null) return Bad<PcGuaranteeDto>("برای ابطال ضمانت‌نامه، توضیح (مثلاً شماره و تاریخ نامه کارفرما یا بانک) را بنویسید.");
                    ResponseDto<PcGuaranteeDto> firstOfTwo = await FirstOfTwoAsync(info, "ابطال", now, cancellationToken);
                    if (firstOfTwo != null) return firstOfTwo;
                    info.Status = PcGuaranteeStatus.Cancelled;
                    break;
            }
            _uow.Set<PcGuaranteeEvent>().Add(new PcGuaranteeEvent
            {
                GuaranteeInfoId = info.Id, Kind = dto.Kind, EventDate = dto.EventDate == default ? DateTime.Today : dto.EventDate.Date, NewExpiryDate = dto.NewExpiryDate?.Date, Fee = dto.Fee,
                DocumentId = dto.DocumentId, Note = Clean(dto.Note), CreatedAt = now, CreatedByUserId = Me,
            });
            info.ModifyDate = now; info.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcGuaranteeDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok((await GuaranteeDtosAsync(new List<PcGuaranteeInfo> { info }, true, cancellationToken)).First());
        }

        public async Task<ResponseDto<PcGuaranteeDto>> ReleaseGuaranteeAsync(PcGuaranteeReleaseDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcGuaranteeDto>(PcText.InvalidData);
            // the money side signs the release too, so this list differs from the other guarantee actions (★ to be confirmed by the company)
            if (!_user.IsInRole(CloseRoles)) return Bad<PcGuaranteeDto>(PcText.NotAllowed + " (آزادسازی ضمانت‌نامه با بازرگانی، مدیر مالی یا PMO است)");
            if (dto.ReleaseDate == default) return Bad<PcGuaranteeDto>("تاریخ آزادسازی را وارد کنید.");
            if (dto.ReleaseDate.Date > DateTime.Today) return Bad<PcGuaranteeDto>("تاریخ آزادسازی نمی‌تواند در آینده باشد.");
            string note = Clean(dto.Note);
            if (note == null) return Bad<PcGuaranteeDto>("برای آزادسازی ضمانت‌نامه، توضیح (مثلاً شماره و تاریخ نامه کارفرما یا بانک) را بنویسید.");
            PcGuaranteeInfo info = await _uow.Set<PcGuaranteeInfo>().FirstOrDefaultAsync(i => i.Id == dto.GuaranteeId && !i.Deleted, cancellationToken);
            if (info == null) return PcText.NotFound<PcGuaranteeDto>("اطلاعات ضمانت‌نامه پیدا نشد.");
            if (info.Status == PcGuaranteeStatus.Released) return Bad<PcGuaranteeDto>("این ضمانت‌نامه قبلاً آزاد شده است.");
            if (info.Status == PcGuaranteeStatus.Cancelled) return Bad<PcGuaranteeDto>("ضمانت‌نامه ابطال‌شده آزاد نمی‌شود.");
            if (info.Status == PcGuaranteeStatus.Requested) return Bad<PcGuaranteeDto>("ضمانت‌نامه‌ای که هنوز صادر نشده آزاد نمی‌شود.");
            DateTimeOffset now = DateTimeOffset.Now;
            // R1a2 (M2): a second person confirms; the first call only records the request
            ResponseDto<PcGuaranteeDto> firstOfTwo = await FirstOfTwoAsync(info, "آزادسازی", now, cancellationToken);
            if (firstOfTwo != null) return firstOfTwo;
            info.Status = PcGuaranteeStatus.Released;
            info.ModifyDate = now; info.ModifierUserId = Me;
            _uow.Set<PcGuaranteeEvent>().Add(new PcGuaranteeEvent
            {
                GuaranteeInfoId = info.Id, Kind = PcGuaranteeEventKind.Release, EventDate = dto.ReleaseDate.Date, DocumentId = dto.DocumentId, Note = note, CreatedAt = now, CreatedByUserId = Me,
            });
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcGuaranteeDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok((await GuaranteeDtosAsync(new List<PcGuaranteeInfo> { info }, true, cancellationToken)).First());
        }

        public async Task<int> ExpireGuaranteesAsync(CancellationToken cancellationToken)
        {
            // the server job calls this without a user; a person needs one of the guarantee roles (the result is the same for everybody: a date has passed)
            if (Me != 0 && !_user.IsInRole("COMMERCIAL,FINANCIAL,FINANCEMANAGER,PMO")) return 0;
            DateTime today = DateTime.Today;
            List<PcGuaranteeInfo> due = await _uow.Set<PcGuaranteeInfo>()
                .Where(i => !i.Deleted && i.ExpiryDate != null && i.ExpiryDate < today
                    && (i.Status == PcGuaranteeStatus.Issued || i.Status == PcGuaranteeStatus.Active || i.Status == PcGuaranteeStatus.ExtensionPending))
                .ToListAsync(cancellationToken);
            if (due.Count == 0) return 0;
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (PcGuaranteeInfo info in due)
            {
                info.Status = PcGuaranteeStatus.Expired;
                info.ModifyDate = now; info.ModifierUserId = Me;
                _uow.Set<PcGuaranteeEvent>().Add(new PcGuaranteeEvent
                {
                    GuaranteeInfoId = info.Id, Kind = PcGuaranteeEventKind.Expiry, EventDate = info.ExpiryDate.Value.Date, Note = "انقضای خودکار: تاریخ انقضا گذشته است.", CreatedAt = now, CreatedByUserId = Me,
                });
            }
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return 0; }   // somebody changed one of them at the same time; the next run takes what is left
            return due.Count;
        }

        public async Task<List<PcGuaranteeAlertDto>> GuaranteeAlertsAsync(int? contractId, CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, cancellationToken);
            int[] steps = new[] { (int)settings[PcSettingKeys.GuaranteeAlert1], (int)settings[PcSettingKeys.GuaranteeAlert2], (int)settings[PcSettingKeys.GuaranteeAlert3] }.OrderBy(x => x).ToArray();
            int widest = steps.Max();
            DateTime today = DateTime.Today;
            DateTime limit = today.AddDays(widest);
            IQueryable<PcGuaranteeInfo> q = _uow.Set<PcGuaranteeInfo>().AsNoTracking().Where(i => !i.Deleted && i.ExpiryDate != null && i.ExpiryDate <= limit
                && (i.Status == PcGuaranteeStatus.Active || i.Status == PcGuaranteeStatus.Issued || i.Status == PcGuaranteeStatus.ExtensionPending || i.Status == PcGuaranteeStatus.Expired));   // R1a: an expired guarantee that was not released or extended stays an alert (level 0)
            if (contractId.HasValue) q = q.Where(i => i.ContractId == contractId.Value);
            List<PcGuaranteeInfo> infos = await q.OrderBy(i => i.ExpiryDate).ToListAsync(cancellationToken);
            List<PcGuaranteeDto> dtos = await GuaranteeDtosAsync(infos, false, cancellationToken);
            return dtos.Select(d =>
            {
                int days = d.DaysToExpiry ?? 0;
                int level = days < 0 ? 0 : steps.First(s => days <= s);
                return new PcGuaranteeAlertDto { GuaranteeId = d.Id.Value, ContractId = d.ContractId, Title = d.Title, No = d.No, ExpiryDate = d.ExpiryDate.Value, DaysToExpiry = days, AlertLevel = level };
            }).ToList();
        }

        // ============================================================================ penalties

        private static PcPenaltyDto ToDto(PcPenaltyAssessment p) => new PcPenaltyDto
        {
            Id = p.Id, ContractId = p.ContractId, MilestoneId = p.MilestoneId, WorkOrderId = p.WorkOrderId, Subject = p.Subject, DueDate = p.DueDate, ActualDate = p.ActualDate, LateDays = p.LateDays,
            GraceDays = p.GraceDays, RatePercentPerWeek = p.RatePercentPerWeek, CapPercent = p.CapPercent, BaseAmount = p.BaseAmount, Amount = p.Amount, Status = p.Status, DecisionReason = p.DecisionReason, ComputedAt = p.ComputedAt,
        };

        /// <summary>Delay penalty: base x weekly rate x (late days / 7), after the grace days, never above the cap (WI-PL-005 contract terms).</summary>
        internal static decimal PenaltyAmount(decimal baseAmount, decimal ratePercentPerWeek, int lateDays, decimal? capPercent)
        {
            if (lateDays <= 0 || ratePercentPerWeek <= 0 || baseAmount <= 0) return 0m;
            decimal amount = baseAmount * ratePercentPerWeek / 100m * lateDays / 7m;
            if (capPercent.HasValue) amount = Math.Min(amount, baseAmount * capPercent.Value / 100m);
            return PcText.Round2(amount);
        }

        public async Task<ResponseDto<List<PcPenaltyDto>>> ComputePenaltiesAsync(int contractId, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole("COMMERCIAL,PMO,PROJECTMANAGER,PLANNING,CEO")) return Bad<List<PcPenaltyDto>>(PcText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == contractId && !c.Deleted, cancellationToken)) return PcText.NotFound<List<PcPenaltyDto>>(PcText.ContractNotFound);
            PcContractTerms terms = await PcContext.TermsAsync(_uow, contractId, false, cancellationToken);
            if (terms == null || !terms.LatePenaltyPercentPerWeek.HasValue || terms.LatePenaltyPercentPerWeek.Value <= 0) return Bad<List<PcPenaltyDto>>("نرخ جریمه تاخیر در شرایط قرارداد ثبت نشده است.");
            decimal value = await ContractValueAsync(contractId, cancellationToken);
            if (value <= 0) return Bad<List<PcPenaltyDto>>("مبلغ قرارداد ثبت نشده است.");
            int variationDays = await _uow.Set<PcVariationOrder>().Where(v => v.ContractId == contractId && !v.Deleted && v.Status == PcVariationStatus.Approved).SumAsync(v => (int?)v.DaysDelta, cancellationToken) ?? 0;
            DateTime today = DateTime.Today;
            DateTimeOffset now = DateTimeOffset.Now;

            var subjects = new List<(int? WorkOrderId, string Subject, DateTime Due, DateTime? Actual, decimal Base)>();
            List<PcWorkOrderInfo> infos = await _uow.Set<PcWorkOrderInfo>().AsNoTracking().Where(i => i.ContractId == contractId && !i.Deleted && i.PlannedDeliveryDate != null).ToListAsync(cancellationToken);
            List<WorkOrder> orders = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => w.ContractId == contractId && !w.Deleted).ToListAsync(cancellationToken);
            List<PcShipment> shipments = await _uow.Set<PcShipment>().AsNoTracking().Where(s => s.ContractId == contractId && !s.Deleted && s.DeliveredAt != null).ToListAsync(cancellationToken);
            foreach (PcWorkOrderInfo i in infos)
            {
                WorkOrder wo = orders.FirstOrDefault(w => w.Id == i.WorkOrderId);
                if (wo == null) continue;
                decimal share = wo.WeightPercent > 0 ? wo.WeightPercent / 100m : 1m / Math.Max(1, orders.Count);
                DateTime? actual = shipments.Where(s => s.WorkOrderId == wo.Id).Select(s => s.DeliveredAt).Max();
                subjects.Add((wo.Id, "تحویل دستور کار " + wo.Code, i.PlannedDeliveryDate.Value.Date.AddDays(variationDays), actual?.Date, PcText.Round2(value * share)));
            }
            if (terms.ContractDeliveryDate.HasValue)
            {
                bool allDelivered = orders.Count > 0 && orders.All(w => shipments.Any(s => s.WorkOrderId == w.Id));
                DateTime? actual = allDelivered ? shipments.Select(s => s.DeliveredAt).Max()?.Date : null;
                subjects.Add((null, "تحویل کل قرارداد", terms.ContractDeliveryDate.Value.Date.AddDays(variationDays), actual, value));
            }
            if (subjects.Count == 0) return Bad<List<PcPenaltyDto>>("تاریخ تحویل قرارداد یا دستور کار ثبت نشده است.");

            List<PcPenaltyAssessment> existing = await _uow.Set<PcPenaltyAssessment>().Where(p => p.ContractId == contractId && !p.Deleted).ToListAsync(cancellationToken);
            foreach (var s in subjects)
            {
                DateTime end = s.Actual ?? today;
                int late = Math.Max(0, (int)(end - s.Due).TotalDays - terms.GraceDays);
                decimal amount = PenaltyAmount(s.Base, terms.LatePenaltyPercentPerWeek.Value, late, terms.PenaltyCapPercent);
                PcPenaltyAssessment row = existing.FirstOrDefault(p => p.WorkOrderId == s.WorkOrderId && p.Subject == s.Subject);
                if (row == null)
                {
                    if (late == 0) continue;
                    row = new PcPenaltyAssessment { ContractId = contractId, WorkOrderId = s.WorkOrderId, Subject = s.Subject, CreateDate = now, CreatorUserId = Me };
                    _uow.Set<PcPenaltyAssessment>().Add(row);
                    existing.Add(row);
                }
                else if (row.Status != PcPenaltyStatus.Computed) continue;
                row.DueDate = s.Due; row.ActualDate = s.Actual; row.LateDays = late; row.GraceDays = terms.GraceDays; row.RatePercentPerWeek = terms.LatePenaltyPercentPerWeek.Value;
                row.CapPercent = terms.PenaltyCapPercent; row.BaseAmount = s.Base; row.Amount = amount; row.ComputedAt = now; row.ModifyDate = now; row.ModifierUserId = Me;
            }
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(existing.OrderBy(p => p.Id).Select(ToDto).ToList());
        }

        public async Task<ResponseDto<PagedList<PcPenaltyDto>>> GetPenaltiesAsync(PcContractQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcContractQuery();
            IQueryable<PcPenaltyAssessment> q = _uow.Set<PcPenaltyAssessment>().AsNoTracking().Where(p => !p.Deleted);
            if (query.ContractId.HasValue) q = q.Where(p => p.ContractId == query.ContractId.Value);
            IQueryable<PcPenaltyDto> projection = q.OrderByDescending(p => p.Id).Select(p => new PcPenaltyDto
            {
                Id = p.Id, ContractId = p.ContractId, MilestoneId = p.MilestoneId, WorkOrderId = p.WorkOrderId, Subject = p.Subject, DueDate = p.DueDate, ActualDate = p.ActualDate, LateDays = p.LateDays,
                GraceDays = p.GraceDays, RatePercentPerWeek = p.RatePercentPerWeek, CapPercent = p.CapPercent, BaseAmount = p.BaseAmount, Amount = p.Amount, Status = p.Status, DecisionReason = p.DecisionReason, ComputedAt = p.ComputedAt,
            });
            return ResponseDtoHelper.Ok(await PagedList<PcPenaltyDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<PcPenaltyDto>> DecidePenaltyAsync(PcPenaltyDecisionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Reason) == null) return Bad<PcPenaltyDto>("دلیل تصمیم را بنویسید.");
            if (!_user.IsInRole(ProjectControlRoles.Ceo)) return Bad<PcPenaltyDto>(PcText.NotAllowed + " (بخشودگی یا مصالحه جریمه با مدیرعامل است)");
            if (dto.Status != PcPenaltyStatus.Waived && dto.Status != PcPenaltyStatus.Negotiated) return Bad<PcPenaltyDto>("وضعیت باید بخشودگی یا مصالحه باشد.");
            PcPenaltyAssessment p = await _uow.Set<PcPenaltyAssessment>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (p == null) return PcText.NotFound<PcPenaltyDto>("جریمه پیدا نشد.");
            if (dto.Status == PcPenaltyStatus.Negotiated)
            {
                if (!dto.Amount.HasValue || dto.Amount.Value < 0 || dto.Amount.Value > p.Amount) return Bad<PcPenaltyDto>("مبلغ مصالحه باید بین صفر و مبلغ محاسبه‌شده باشد.");
                p.Amount = PcText.Round2(dto.Amount.Value);
            }
            else p.Amount = 0m;
            p.Status = dto.Status; p.DecisionReason = Clean(dto.Reason); p.DecidedByUserId = Me; p.ModifyDate = DateTimeOffset.Now; p.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(p));
        }

        // ============================================================================ variation orders

        private static PcVariationDto ToDto(PcVariationOrder v) => new PcVariationDto
        {
            Id = v.Id, Number = v.Number, ContractId = v.ContractId, ChangeRequestId = v.ChangeRequestId, AmendmentId = v.AmendmentId, Description = v.Description, PriceDelta = v.PriceDelta,
            DaysDelta = v.DaysDelta, ClientApprovalReference = v.ClientApprovalReference, ClientApprovalDate = v.ClientApprovalDate, Status = v.Status, RowVersion = v.RowVersion == null ? null : Convert.ToBase64String(v.RowVersion),
        };

        public async Task<ResponseDto<PagedList<PcVariationDto>>> GetVariationsAsync(PcContractQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcContractQuery();
            IQueryable<PcVariationOrder> q = _uow.Set<PcVariationOrder>().AsNoTracking().Where(v => !v.Deleted);
            if (query.ContractId.HasValue) q = q.Where(v => v.ContractId == query.ContractId.Value);
            IQueryable<PcVariationDto> projection = q.OrderByDescending(v => v.Id).Select(v => new PcVariationDto
            {
                Id = v.Id, Number = v.Number, ContractId = v.ContractId, ChangeRequestId = v.ChangeRequestId, AmendmentId = v.AmendmentId, Description = v.Description, PriceDelta = v.PriceDelta,
                DaysDelta = v.DaysDelta, ClientApprovalReference = v.ClientApprovalReference, ClientApprovalDate = v.ClientApprovalDate, Status = v.Status,
            });
            return ResponseDtoHelper.Ok(await PagedList<PcVariationDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<PcVariationDto>> SaveVariationAsync(PcVariationDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Description) == null) return Bad<PcVariationDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Commercial + ",PROJECTMANAGER")) return Bad<PcVariationDto>(PcText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken)) return PcText.NotFound<PcVariationDto>(PcText.ContractNotFound);
            if (dto.ChangeRequestId.HasValue && !await _uow.Set<PcChangeRequest>().AnyAsync(c => c.Id == dto.ChangeRequestId.Value && c.ContractId == dto.ContractId && !c.Deleted, cancellationToken)) return Bad<PcVariationDto>("درخواست تغییر متعلق به این قرارداد نیست.");
            if (dto.AmendmentId.HasValue && !await _uow.Set<PcContractAmendment>().AnyAsync(c => c.Id == dto.AmendmentId.Value && c.ContractId == dto.ContractId && !c.Deleted, cancellationToken)) return Bad<PcVariationDto>("الحاقیه متعلق به این قرارداد نیست.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcVariationOrder v;
            if (dto.Id.HasValue)
            {
                v = await _uow.Set<PcVariationOrder>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (v == null) return PcText.NotFound<PcVariationDto>("دستور تغییر پیدا نشد.");
                if (v.Status != PcVariationStatus.Draft) return Bad<PcVariationDto>("دستور تغییر ارسال‌شده تغییر نمی‌کند.");
            }
            else
            {
                v = new PcVariationOrder { ContractId = dto.ContractId, Number = await InventoryNumbers.NextNumberAsync(_uow, PcNumbers.Variation, "VO", DateTime.Today, cancellationToken), CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcVariationOrder>().Add(v);
            }
            v.ChangeRequestId = dto.ChangeRequestId; v.AmendmentId = dto.AmendmentId; v.Description = Clean(dto.Description); v.PriceDelta = dto.PriceDelta; v.DaysDelta = dto.DaysDelta;
            v.ModifyDate = now; v.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcVariationDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(v));
        }

        public async Task<ResponseDto<PcVariationDto>> VariationActionAsync(PcVariationActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcVariationDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Commercial + ",PROJECTMANAGER")) return Bad<PcVariationDto>(PcText.NotAllowed);
            PcVariationOrder v = await _uow.Set<PcVariationOrder>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (v == null) return PcText.NotFound<PcVariationDto>("دستور تغییر پیدا نشد.");
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "submit":
                    if (v.Status != PcVariationStatus.Draft) return Bad<PcVariationDto>("فقط پیش‌نویس ارسال می‌شود.");
                    v.Status = PcVariationStatus.Submitted;
                    break;
                case "approve":
                    if (v.Status != PcVariationStatus.Submitted) return Bad<PcVariationDto>("ابتدا دستور تغییر باید برای کارفرما ارسال شود.");
                    if (Clean(dto.Reference) == null) return Bad<PcVariationDto>("شماره نامه تایید کارفرما را وارد کنید.");
                    v.ClientApprovalReference = Clean(dto.Reference); v.ClientApprovalDate = (dto.Date ?? DateTime.Today).Date; v.Status = PcVariationStatus.Approved;
                    break;
                case "reject":
                    if (v.Status != PcVariationStatus.Submitted) return Bad<PcVariationDto>("فقط دستور ارسال‌شده رد می‌شود.");
                    v.Status = PcVariationStatus.Rejected;
                    break;
                default:
                    return Bad<PcVariationDto>("عملیات نامعتبر است.");
            }
            v.ModifyDate = DateTimeOffset.Now; v.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcVariationDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(v));
        }
    }
}
