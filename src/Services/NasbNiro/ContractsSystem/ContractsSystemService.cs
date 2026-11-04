using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.ContractsSystem;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.ProcurementDtos;
using IdentityUserRole = Entities.Identity.UserRole;

namespace Services.NasbNiro.ContractsSystem
{
    public interface IContractsSystemService
    {
        Task<ResponseDto<List<DealRequestDto>>> GetDealsAsync(FlowQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<DealRequestDto>> GetDealAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<DealRequestDto>> SaveDealAsync(SaveDealRequestDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<DealRequestDto>> DealActionAsync(FlowActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<TenderRunDto>>> GetTendersAsync(FlowQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<TenderRunDto>> GetTenderAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<TenderRunDto>> SaveTenderAsync(SaveTenderRunDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<TenderRunDto>> SelectWinnerAsync(SelectWinnerDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<TenderRunDto>> TenderActionAsync(FlowActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<ContractDraftDto>>> GetDraftsAsync(FlowQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ContractDraftDto>> GetDraftAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<ContractDraftDto>> SaveDraftAsync(SaveContractDraftDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ContractDraftDto>> DraftActionAsync(FlowActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ContractDraftPrintDto>> GetDraftPrintAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<List<ContractChangeDto>>> GetChangesAsync(FlowQuery query, CancellationToken cancellationToken);
        ResponseDto<List<ContractMappingRowDto>> GetMapping();
    }

    /// <summary>
    /// PB6: the contracts system (پیوست ۶). Three cartables extend what exists: the deal request (before tender), the tender run with its winner, and the contract draft with its exchange with the other party.
    /// Maker-checker: the maker of a request never approves it; the person who selects a winner is not the one who confirms; a step may name the person who must decide and he must hold the role.
    /// </summary>
    public class ContractsSystemService : IContractsSystemService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public ContractsSystemService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private const string NotAllowed = "شما مجوز این کار را ندارید";

        private void History(string type, int id, string action, string note, int round)
        {
            _uow.Set<CtHistory>().Add(new CtHistory { OwnerType = type, OwnerId = id, Action = action, Note = note, UserId = Me, At = DateTimeOffset.Now, Round = round });
        }

        private async Task<List<HistoryDto>> HistoryOfAsync(string type, int id, CancellationToken cancellationToken)
        {
            List<CtHistory> rows = await _uow.Set<CtHistory>().AsNoTracking().Where(h => h.OwnerType == type && h.OwnerId == id).OrderBy(h => h.Id).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => (int?)r.UserId), cancellationToken);
            return rows.Select(h => new HistoryDto { Action = h.Action, Note = h.Note, UserName = names.Name(h.UserId), At = h.At, Round = h.Round }).ToList();
        }

        private async Task<bool> HoldsRoleAsync(int userId, string role, CancellationToken cancellationToken)
        {
            if (role == null) return true;
            string[] roles = role.Split(',');
            return await _uow.Set<IdentityUserRole>().AsNoTracking().AnyAsync(ur => ur.UserId == userId && roles.Contains(ur.Role.Name), cancellationToken);
        }

        // ============================================================================================ deal requests

        private static string[] ChainFor(CtDealLevel level) => level == CtDealLevel.Small ? new[] { "COMMERCIAL" }
            : level == CtDealLevel.Medium ? new[] { "COMMERCIAL", "FINANCEMANAGER" } : new[] { "COMMERCIAL", "FINANCEMANAGER", "CEO" };

        private async Task<List<DealRequestDto>> LoadDealsAsync(IQueryable<CtDealRequest> source, bool details, CancellationToken cancellationToken)
        {
            List<CtDealRequest> rows = await source.AsNoTracking().Include(d => d.Steps).Include(d => d.Contract).OrderByDescending(d => d.Id).Take(500).ToListAsync(cancellationToken);
            var userIds = rows.SelectMany(d => new int?[] { d.CreatorUserId }.Concat(d.Steps.SelectMany(s => new[] { s.DesignatedUserId, s.DecidedByUserId }))).ToList();
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, userIds, cancellationToken);
            List<int> ids = rows.Select(d => d.Id).ToList();
            var tenders = await _uow.Set<CtTenderRun>().AsNoTracking().Where(t => ids.Contains(t.DealRequestId) && t.Status != CtTenderStatus.Cancelled).Select(t => new { t.Id, t.DealRequestId }).ToListAsync(cancellationToken);
            var list = new List<DealRequestDto>();
            foreach (CtDealRequest d in rows)
            {
                var dto = new DealRequestDto
                {
                    Id = d.Id, Number = d.Number, FormKind = d.FormKind, Status = d.Status, Title = d.Title, Subject = d.Subject, FundingSource = d.FundingSource, OrganisationalUnit = d.OrganisationalUnit,
                    DealLevel = d.DealLevel, ContractTypeTitle = d.ContractTypeTitle, SelectionMethodTitle = d.SelectionMethodTitle, EstimatedAmount = d.EstimatedAmount, ContractId = d.ContractId,
                    ContractCode = d.Contract?.Code, PartyName = d.PartyName, EmployerRank = d.EmployerRank, EmployerField = d.EmployerField, Round = d.Round, SubmittedAt = d.SubmittedAt,
                    ReturnReason = d.ReturnReason, CreatorName = names.Name(d.CreatorUserId), CreatorUserId = d.CreatorUserId, CreateDate = d.CreateDate,
                    TenderRunId = tenders.FirstOrDefault(t => t.DealRequestId == d.Id)?.Id,
                };
                foreach (CtDealStep s in d.Steps.OrderBy(x => x.Round).ThenBy(x => x.Order))
                    dto.Steps.Add(new DealStepDto { Id = s.Id, Round = s.Round, Order = s.Order, Role = s.Role, DesignatedUserId = s.DesignatedUserId, DesignatedName = names.Name(s.DesignatedUserId), State = s.State, DecidedByName = names.Name(s.DecidedByUserId), DecidedAt = s.DecidedAt, Note = s.Note });
                if (details) dto.History = await HistoryOfAsync("deal", d.Id, cancellationToken);
                list.Add(dto);
            }
            return list;
        }

        public async Task<ResponseDto<List<DealRequestDto>>> GetDealsAsync(FlowQuery query, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(CtRoles.Readers)) return Bad<List<DealRequestDto>>(NotAllowed);
            query ??= new FlowQuery();
            IQueryable<CtDealRequest> q = _uow.Set<CtDealRequest>().Where(d => !d.Deleted);
            if (query.Status.HasValue) q = q.Where(d => (int)d.Status == query.Status.Value);
            if (query.ContractId.HasValue) q = q.Where(d => d.ContractId == query.ContractId.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(d => d.Number.Contains(text) || d.Title.Contains(text) || d.PartyName.Contains(text));
            return ResponseDtoHelper.Ok(await LoadDealsAsync(q, false, cancellationToken));
        }

        public async Task<ResponseDto<DealRequestDto>> GetDealAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(CtRoles.Readers)) return Bad<DealRequestDto>(NotAllowed);
            List<DealRequestDto> list = await LoadDealsAsync(_uow.Set<CtDealRequest>().Where(d => d.Id == id && !d.Deleted), true, cancellationToken);
            return list.Count == 0 ? ResponseDtoHelper.NotFound<DealRequestDto>("درخواست معامله پیدا نشد.") : ResponseDtoHelper.Ok(list[0]);
        }

        public async Task<ResponseDto<DealRequestDto>> SaveDealAsync(SaveDealRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<DealRequestDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(CtRoles.Requesters)) return Bad<DealRequestDto>(NotAllowed);
            if (Clean(dto.Title) == null || Clean(dto.Subject) == null) return Bad<DealRequestDto>("عنوان و موضوع معامله را بنویسید.");
            if (dto.EstimatedAmount <= 0) return Bad<DealRequestDto>("مبلغ برآوردی باید بیشتر از صفر باشد (ریال).");
            if (!Enum.IsDefined(typeof(CtDealFormKind), dto.FormKind) || !Enum.IsDefined(typeof(CtDealLevel), dto.DealLevel)) return Bad<DealRequestDto>("نوع فرم یا سطح معامله نامعتبر است.");
            if (dto.ContractId.HasValue && !await _uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().AnyAsync(c => c.Id == dto.ContractId.Value && !c.Deleted, cancellationToken)) return Bad<DealRequestDto>("قرارداد پیدا نشد.");
            string[] chain = ChainFor(dto.DealLevel);
            foreach (KeyValuePair<string, int> kv in dto.Designated ?? new Dictionary<string, int>())
            {
                if (!chain.Contains(kv.Key)) return Bad<DealRequestDto>($"نقش «{kv.Key}» در زنجیره تایید این سطح نیست.");
                if (kv.Value == Me) return Bad<DealRequestDto>("درخواست‌دهنده نمی‌تواند خودش را تاییدکننده معرفی کند.");
                if (!await HoldsRoleAsync(kv.Value, kv.Key, cancellationToken)) return Bad<DealRequestDto>($"کاربر انتخاب‌شده نقش {kv.Key} را ندارد.");
            }
            CtDealRequest d;
            if (dto.Id.HasValue)
            {
                d = await _uow.Set<CtDealRequest>().Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (d == null) return ResponseDtoHelper.NotFound<DealRequestDto>("درخواست معامله پیدا نشد.");
                if (d.Status != CtDealStatus.Draft && d.Status != CtDealStatus.Returned) return Bad<DealRequestDto>("فقط پیش‌نویس یا برگشتی ویرایش می‌شود.");
                if (d.CreatorUserId != Me) return Bad<DealRequestDto>(NotAllowed);
            }
            else
            {
                d = new CtDealRequest
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, Pb6Numbers.DealRequest, "DLR", DateTime.Today, cancellationToken), Status = CtDealStatus.Draft, Round = 1,
                    CreateDate = DateTimeOffset.Now, CreatorUserId = Me, Steps = new List<CtDealStep>(),
                };
                _uow.Set<CtDealRequest>().Add(d);
            }
            d.FormKind = dto.FormKind; d.Title = dto.Title.Trim(); d.Subject = dto.Subject.Trim(); d.FundingSource = Clean(dto.FundingSource); d.OrganisationalUnit = Clean(dto.OrganisationalUnit); d.DealLevel = dto.DealLevel;
            d.ContractTypeTitle = Clean(dto.ContractTypeTitle); d.SelectionMethodTitle = Clean(dto.SelectionMethodTitle); d.EstimatedAmount = Math.Round(dto.EstimatedAmount, 0); d.ContractId = dto.ContractId;
            d.PartyName = Clean(dto.PartyName); d.EmployerRank = Clean(dto.EmployerRank); d.EmployerField = Clean(dto.EmployerField);
            d.EditorUserIds = LedgerMath.WithEditor(d.EditorUserIds, Me);
            // the chain of the current round is rebuilt while the request is open for editing
            foreach (CtDealStep s in d.Steps.Where(s => s.Round == d.Round && s.State == CtStepState.Waiting).ToList()) _uow.Set<CtDealStep>().Remove(s);
            int order = 1;
            foreach (string role in chain)
                d.Steps.Add(new CtDealStep { Round = d.Round, Order = order++, Role = role, DesignatedUserId = dto.Designated != null && dto.Designated.TryGetValue(role, out int u) ? u : (int?)null, State = CtStepState.Waiting });
            d.ModifyDate = DateTimeOffset.Now; d.ModifierUserId = Me;
            if (dto.Submit) { d.Status = CtDealStatus.Submitted; d.SubmittedAt = DateTimeOffset.Now; d.ReturnReason = null; }
            await _uow.SaveChangesAsync(cancellationToken);
            if (dto.Submit) { History("deal", d.Id, "submit", null, d.Round); await _uow.SaveChangesAsync(cancellationToken); await SyncDealGateAsync(d.Id, cancellationToken); }
            return await GetDealAsync(d.Id, cancellationToken);
        }

        private async Task SyncDealGateAsync(int id, CancellationToken cancellationToken)
        {
            CtDealRequest d = await _uow.Set<CtDealRequest>().AsNoTracking().Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (d == null) return;
            CtDealStep next = d.Status == CtDealStatus.Submitted ? d.Steps.Where(s => s.Round == d.Round && s.State == CtStepState.Waiting).OrderBy(s => s.Order).FirstOrDefault() : null;
            await OfficeGates.SyncAsync(_notify, "ctdeal:" + id, next != null, "procurement", "ct-deal", id, "درخواست معامله " + d.Number, "/procurement/contracts-system?deal=" + id,
                next?.Role, cancellationToken);
            if (next?.DesignatedUserId != null && _notify != null)
                await _notify.NotifyAsync(new NotifyRequest { SourceKey = "ctdeal-user:" + id, Module = "procurement", SourceType = "ct-deal", SourceId = id, Title = "درخواست معامله " + d.Number + " منتظر تصمیم شماست", Link = "/procurement/contracts-system?deal=" + id, Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Alert, UserIds = new List<int> { next.DesignatedUserId.Value }, ReplaceRecipients = true }, cancellationToken);
        }

        public async Task<ResponseDto<DealRequestDto>> DealActionAsync(FlowActionDto dto, CancellationToken cancellationToken)
        {
            CtDealRequest d = await _uow.Set<CtDealRequest>().Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (d == null) return ResponseDtoHelper.NotFound<DealRequestDto>("درخواست معامله پیدا نشد.");
            string note = Clean(dto.Note);
            string action = dto.Action?.Trim().ToLowerInvariant();
            if (action == "submit")
            {
                if (d.Status != CtDealStatus.Draft && d.Status != CtDealStatus.Returned) return Bad<DealRequestDto>("درخواست قبلاً ارسال شده است.");
                if (d.CreatorUserId != Me) return Bad<DealRequestDto>(NotAllowed);
                if (!d.Steps.Any(s => s.Round == d.Round)) return Bad<DealRequestDto>("زنجیره تایید ساخته نشده؛ درخواست را ذخیره کنید.");
                d.Status = CtDealStatus.Submitted; d.SubmittedAt = DateTimeOffset.Now; d.ReturnReason = null; History("deal", d.Id, "submit", null, d.Round);
            }
            else if (action == "approve" || action == "return" || action == "reject")
            {
                if (d.Status != CtDealStatus.Submitted) return Bad<DealRequestDto>("درخواست در مرحله تایید نیست.");
                CtDealStep step = d.Steps.Where(s => s.Round == d.Round && s.State == CtStepState.Waiting).OrderBy(s => s.Order).FirstOrDefault();
                if (step == null) return Bad<DealRequestDto>("مرحله‌ای منتظر تصمیم نیست.");
                if (!_user.IsInRole(step.Role)) return Bad<DealRequestDto>(NotAllowed + " (" + step.Role + ")");
                if (step.DesignatedUserId.HasValue && step.DesignatedUserId.Value != Me) return Bad<DealRequestDto>("این مرحله را شخص تعیین‌شده تصمیم می‌گیرد.");
                if (LedgerMath.Touched(d.CreatorUserId, d.EditorUserIds, Me)) return Bad<DealRequestDto>("سازنده یا ویراستار درخواست آن را تایید نمی‌کند.");
                if (d.Steps.Any(s => s.Round == d.Round && s.DecidedByUserId == Me)) return Bad<DealRequestDto>("هر مرحله را شخص جداگانه‌ای تصمیم می‌گیرد.");
                if (action != "approve" && note == null) return Bad<DealRequestDto>("علت را بنویسید.");
                step.DecidedByUserId = Me; step.DecidedAt = DateTimeOffset.Now; step.Note = note;
                if (action == "approve")
                {
                    step.State = CtStepState.Approved;
                    if (!d.Steps.Any(s => s.Round == d.Round && s.State == CtStepState.Waiting)) d.Status = CtDealStatus.Approved;
                }
                else if (action == "return")
                {
                    step.State = CtStepState.Returned; d.Status = CtDealStatus.Returned; d.ReturnReason = note; d.Round++;
                }
                else { step.State = CtStepState.Rejected; d.Status = CtDealStatus.Rejected; d.ReturnReason = note; }
                History("deal", d.Id, action, note, step.Round);
            }
            else return Bad<DealRequestDto>("عمل نامعتبر است.");
            d.ModifyDate = DateTimeOffset.Now; d.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<DealRequestDto>("اطلاعات هم‌زمان تغییر کرد؛ دوباره تلاش کنید."); }
            await SyncDealGateAsync(d.Id, cancellationToken);
            if ((d.Status == CtDealStatus.Returned || d.Status == CtDealStatus.Rejected || d.Status == CtDealStatus.Approved) && _notify != null)
                await _notify.NotifyAsync(new NotifyRequest { SourceKey = "ctdeal-result:" + d.Id + ":" + d.Status, Module = "procurement", SourceType = "ct-deal", SourceId = d.Id, Title = "درخواست معامله " + d.Number + (d.Status == CtDealStatus.Approved ? " تایید شد" : d.Status == CtDealStatus.Returned ? " برگشت خورد" : " رد شد"), Link = "/procurement/contracts-system?deal=" + d.Id, Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Alert, UserIds = new List<int> { d.CreatorUserId } }, cancellationToken);
            return await GetDealAsync(d.Id, cancellationToken);
        }

        // ============================================================================================ tender runs

        private async Task<List<TenderRunDto>> LoadTendersAsync(IQueryable<CtTenderRun> source, bool details, CancellationToken cancellationToken)
        {
            List<CtTenderRun> rows = await source.AsNoTracking().Include(t => t.Participants).ThenInclude(p => p.Provider).Include(t => t.DealRequest).OrderByDescending(t => t.Id).Take(500).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(t => new[] { t.WinnerSelectedByUserId, t.WinnerConfirmedByUserId }), cancellationToken);
            var list = new List<TenderRunDto>();
            foreach (CtTenderRun t in rows)
            {
                var dto = new TenderRunDto
                {
                    Id = t.Id, Number = t.Number, DealRequestId = t.DealRequestId, DealRequestNumber = t.DealRequest?.Number, DealTitle = t.DealRequest?.Title, Status = t.Status, Method = t.Method,
                    GuaranteeType = t.GuaranteeType, GuaranteeAmount = t.GuaranteeAmount, CallDate = t.CallDate, OpeningDate = t.OpeningDate, Notes = t.Notes, WinnerParticipantId = t.WinnerParticipantId,
                    WinnerReason = t.WinnerReason, WinnerSelectedAt = t.WinnerSelectedAt, WinnerSelectedByName = names.Name(t.WinnerSelectedByUserId), WinnerConfirmedAt = t.WinnerConfirmedAt,
                    WinnerConfirmedByName = names.Name(t.WinnerConfirmedByUserId), CancelReason = t.CancelReason,
                };
                decimal? lowest = t.Participants.Where(p => !p.Disqualified && p.OfferAmount > 0).Select(p => (decimal?)p.OfferAmount).Min();
                foreach (CtTenderParticipant p in t.Participants.OrderBy(x => x.Id))
                    dto.Participants.Add(new ParticipantDto { Id = p.Id, ProviderId = p.ProviderId, CompanyName = p.CompanyName, OfferAmount = p.OfferAmount, TechnicalScore = p.TechnicalScore, Notes = p.Notes, Disqualified = p.Disqualified, DisqualifyReason = p.DisqualifyReason, ProviderName = p.Provider?.Name, Lowest = !p.Disqualified && lowest.HasValue && p.OfferAmount == lowest });
                if (details) dto.History = await HistoryOfAsync("tender", t.Id, cancellationToken);
                list.Add(dto);
            }
            return list;
        }

        public async Task<ResponseDto<List<TenderRunDto>>> GetTendersAsync(FlowQuery query, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(CtRoles.Readers)) return Bad<List<TenderRunDto>>(NotAllowed);
            query ??= new FlowQuery();
            IQueryable<CtTenderRun> q = _uow.Set<CtTenderRun>().Where(t => !t.Deleted);
            if (query.Status.HasValue) q = q.Where(t => (int)t.Status == query.Status.Value);
            return ResponseDtoHelper.Ok(await LoadTendersAsync(q, false, cancellationToken));
        }

        public async Task<ResponseDto<TenderRunDto>> GetTenderAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(CtRoles.Readers)) return Bad<TenderRunDto>(NotAllowed);
            List<TenderRunDto> list = await LoadTendersAsync(_uow.Set<CtTenderRun>().Where(t => t.Id == id && !t.Deleted), true, cancellationToken);
            return list.Count == 0 ? ResponseDtoHelper.NotFound<TenderRunDto>("مناقصه پیدا نشد.") : ResponseDtoHelper.Ok(list[0]);
        }

        public async Task<ResponseDto<TenderRunDto>> SaveTenderAsync(SaveTenderRunDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<TenderRunDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(CtRoles.Commercial)) return Bad<TenderRunDto>(NotAllowed + " (بازرگانی)");
            if (!Enum.IsDefined(typeof(CtTenderMethod), dto.Method) || !Enum.IsDefined(typeof(CtGuaranteeType), dto.GuaranteeType)) return Bad<TenderRunDto>("روش یا نوع تضمین نامعتبر است.");
            CtDealRequest deal = await _uow.Set<CtDealRequest>().FirstOrDefaultAsync(x => x.Id == dto.DealRequestId && !x.Deleted, cancellationToken);
            if (deal == null) return ResponseDtoHelper.NotFound<TenderRunDto>("درخواست معامله پیدا نشد.");
            if (deal.Status != CtDealStatus.Approved && deal.Status != CtDealStatus.InTender) return Bad<TenderRunDto>("مناقصه فقط برای درخواست معامله تاییدشده برگزار می‌شود.");
            List<SaveParticipantDto> parts = (dto.Participants ?? new List<SaveParticipantDto>()).Where(p => p != null).ToList();
            if (parts.Any(p => (p.ProviderId == null && Clean(p.CompanyName) == null) || p.OfferAmount < 0)) return Bad<TenderRunDto>("نام یا تامین‌کننده و مبلغ پیشنهاد هر شرکت‌کننده را درست بنویسید.");
            if (parts.Any(p => p.Disqualified && Clean(p.DisqualifyReason) == null)) return Bad<TenderRunDto>("علت رد صلاحیت را بنویسید.");
            if (parts.Where(p => p.ProviderId.HasValue).GroupBy(p => p.ProviderId).Any(g => g.Count() > 1)) return Bad<TenderRunDto>("یک تامین‌کننده دوبار ثبت شده است.");
            CtTenderRun t;
            if (dto.Id.HasValue)
            {
                t = await _uow.Set<CtTenderRun>().Include(x => x.Participants).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (t == null) return ResponseDtoHelper.NotFound<TenderRunDto>("مناقصه پیدا نشد.");
                if (t.Status != CtTenderStatus.Open) return Bad<TenderRunDto>("پس از انتخاب برنده مناقصه ویرایش نمی‌شود.");
                if (t.DealRequestId != dto.DealRequestId) return Bad<TenderRunDto>("درخواست معامله مناقصه تغییر نمی‌کند.");
            }
            else
            {
                if (await _uow.Set<CtTenderRun>().AnyAsync(x => x.DealRequestId == dto.DealRequestId && x.Status != CtTenderStatus.Cancelled && !x.Deleted, cancellationToken)) return Bad<TenderRunDto>("برای این درخواست مناقصه فعال هست.");
                t = new CtTenderRun { Number = await InventoryNumbers.NextNumberAsync(_uow, Pb6Numbers.TenderRun, "TND", DateTime.Today, cancellationToken), DealRequestId = dto.DealRequestId, Status = CtTenderStatus.Open, CreateDate = DateTimeOffset.Now, CreatorUserId = Me, Participants = new List<CtTenderParticipant>() };
                _uow.Set<CtTenderRun>().Add(t);
                deal.Status = CtDealStatus.InTender;
            }
            t.Method = dto.Method; t.GuaranteeType = dto.GuaranteeType; t.GuaranteeAmount = dto.GuaranteeAmount; t.CallDate = dto.CallDate?.Date; t.OpeningDate = dto.OpeningDate?.Date; t.Notes = Clean(dto.Notes);
            foreach (CtTenderParticipant old in t.Participants.Where(o => !parts.Any(p => p.Id == o.Id)).ToList()) { _uow.Set<CtTenderParticipant>().Remove(old); t.Participants.Remove(old); }
            foreach (SaveParticipantDto p in parts)
            {
                CtTenderParticipant row = p.Id.HasValue ? t.Participants.FirstOrDefault(x => x.Id == p.Id.Value) : null;
                if (row == null) { row = new CtTenderParticipant(); t.Participants.Add(row); }
                row.ProviderId = p.ProviderId; row.CompanyName = Clean(p.CompanyName); row.OfferAmount = Math.Round(p.OfferAmount, 0); row.TechnicalScore = p.TechnicalScore; row.Notes = Clean(p.Notes);
                row.Disqualified = p.Disqualified; row.DisqualifyReason = p.Disqualified ? Clean(p.DisqualifyReason) : null;
            }
            t.ModifyDate = DateTimeOffset.Now; t.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            History("tender", t.Id, dto.Id.HasValue ? "update" : "create", null, 1);
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetTenderAsync(t.Id, cancellationToken);
        }

        public async Task<ResponseDto<TenderRunDto>> SelectWinnerAsync(SelectWinnerDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<TenderRunDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(CtRoles.Commercial)) return Bad<TenderRunDto>(NotAllowed + " (بازرگانی)");
            CtTenderRun t = await _uow.Set<CtTenderRun>().Include(x => x.Participants).FirstOrDefaultAsync(x => x.Id == dto.TenderRunId && !x.Deleted, cancellationToken);
            if (t == null) return ResponseDtoHelper.NotFound<TenderRunDto>("مناقصه پیدا نشد.");
            if (t.Status != CtTenderStatus.Open) return Bad<TenderRunDto>("برنده قبلاً انتخاب شده است.");
            CtTenderParticipant p = t.Participants.FirstOrDefault(x => x.Id == dto.ParticipantId);
            if (p == null) return Bad<TenderRunDto>("شرکت‌کننده پیدا نشد.");
            if (p.Disqualified) return Bad<TenderRunDto>("شرکت‌کننده رد صلاحیت شده است.");
            string reason = Clean(dto.Reason);
            decimal? lowest = t.Participants.Where(x => !x.Disqualified && x.OfferAmount > 0).Select(x => (decimal?)x.OfferAmount).Min();
            if (lowest.HasValue && p.OfferAmount > lowest.Value && reason == null) return Bad<TenderRunDto>("انتخاب پیشنهاد غیر از کمترین قیمت علت می‌خواهد.");
            t.WinnerParticipantId = p.Id; t.WinnerReason = reason; t.WinnerSelectedAt = DateTimeOffset.Now; t.WinnerSelectedByUserId = Me; t.Status = CtTenderStatus.WinnerSelected;
            t.ModifyDate = DateTimeOffset.Now; t.ModifierUserId = Me;
            History("tender", t.Id, "select-winner", reason, 1);
            await _uow.SaveChangesAsync(cancellationToken);
            await OfficeGates.SyncAsync(_notify, "cttender:" + t.Id, true, "procurement", "ct-tender", t.Id, "تایید برنده مناقصه " + t.Number, "/procurement/contracts-system?tender=" + t.Id, "FINANCEMANAGER,CEO", cancellationToken);
            return await GetTenderAsync(t.Id, cancellationToken);
        }

        public async Task<ResponseDto<TenderRunDto>> TenderActionAsync(FlowActionDto dto, CancellationToken cancellationToken)
        {
            CtTenderRun t = await _uow.Set<CtTenderRun>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (t == null) return ResponseDtoHelper.NotFound<TenderRunDto>("مناقصه پیدا نشد.");
            string note = Clean(dto.Note);
            switch (dto.Action?.Trim().ToLowerInvariant())
            {
                case "confirm-winner":
                    if (t.Status != CtTenderStatus.WinnerSelected) return Bad<TenderRunDto>("برنده هنوز انتخاب نشده است.");
                    if (!_user.IsInRole("FINANCEMANAGER,CEO")) return Bad<TenderRunDto>(NotAllowed);
                    if (t.WinnerSelectedByUserId == Me || t.CreatorUserId == Me) return Bad<TenderRunDto>("انتخاب‌کننده برنده آن را تایید نمی‌کند.");
                    t.Status = CtTenderStatus.WinnerConfirmed; t.WinnerConfirmedAt = DateTimeOffset.Now; t.WinnerConfirmedByUserId = Me;
                    break;
                case "reopen":
                    if (t.Status != CtTenderStatus.WinnerSelected) return Bad<TenderRunDto>("فقط انتخاب تاییدنشده برمی‌گردد.");
                    if (!_user.IsInRole("FINANCEMANAGER,CEO")) return Bad<TenderRunDto>(NotAllowed);
                    if (t.WinnerSelectedByUserId == Me) return Bad<TenderRunDto>("انتخاب‌کننده برنده آن را برنمی‌گرداند.");
                    if (note == null) return Bad<TenderRunDto>("علت را بنویسید.");
                    t.Status = CtTenderStatus.Open; t.WinnerParticipantId = null; t.WinnerReason = null; t.WinnerSelectedAt = null; t.WinnerSelectedByUserId = null;
                    break;
                case "cancel":
                    if (t.Status == CtTenderStatus.Cancelled || t.Status == CtTenderStatus.WinnerConfirmed) return Bad<TenderRunDto>("این مناقصه لغو نمی‌شود.");
                    if (!_user.IsInRole(CtRoles.Commercial)) return Bad<TenderRunDto>(NotAllowed);
                    if (note == null) return Bad<TenderRunDto>("علت لغو را بنویسید.");
                    t.Status = CtTenderStatus.Cancelled; t.CancelReason = note;
                    CtDealRequest deal = await _uow.Set<CtDealRequest>().FirstOrDefaultAsync(x => x.Id == t.DealRequestId, cancellationToken);
                    if (deal != null && deal.Status == CtDealStatus.InTender) deal.Status = CtDealStatus.Approved;
                    break;
                default:
                    return Bad<TenderRunDto>("عمل نامعتبر است.");
            }
            t.ModifyDate = DateTimeOffset.Now; t.ModifierUserId = Me;
            History("tender", t.Id, dto.Action.Trim().ToLowerInvariant(), note, 1);
            await _uow.SaveChangesAsync(cancellationToken);
            await OfficeGates.SyncAsync(_notify, "cttender:" + t.Id, t.Status == CtTenderStatus.WinnerSelected, "procurement", "ct-tender", t.Id, "تایید برنده مناقصه " + t.Number, "/procurement/contracts-system?tender=" + t.Id, "FINANCEMANAGER,CEO", cancellationToken);
            return await GetTenderAsync(t.Id, cancellationToken);
        }

        // ============================================================================================ contract drafts

        private async Task<List<ContractDraftDto>> LoadDraftsAsync(IQueryable<CtContractDraft> source, bool details, CancellationToken cancellationToken)
        {
            List<CtContractDraft> rows = await source.AsNoTracking().Include(d => d.Provider).OrderByDescending(d => d.Id).Take(500).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => (int?)r.CreatorUserId), cancellationToken);
            var list = new List<ContractDraftDto>();
            foreach (CtContractDraft d in rows)
            {
                var dto = new ContractDraftDto
                {
                    Id = d.Id, DealRequestId = d.DealRequestId, TenderRunId = d.TenderRunId, ProviderId = d.ProviderId, CounterpartyName = d.CounterpartyName, ContractKindTitle = d.ContractKindTitle,
                    TemplateCode = d.TemplateCode, Title = d.Title, Subject = d.Subject, ContractAmount = d.ContractAmount, StartDate = d.StartDate, EndDate = d.EndDate, DurationDays = d.DurationDays,
                    AdvancePercent = d.AdvancePercent, RetentionPercent = d.RetentionPercent, PerformanceGuaranteePercent = d.PerformanceGuaranteePercent, PaymentTerms = d.PaymentTerms,
                    SpecialConditions = d.SpecialConditions, Number = d.Number, Status = d.Status, ProviderName = d.Provider?.Name, ContractNumber = d.ContractNumber, SignedDate = d.SignedDate,
                    ExchangeRound = d.ExchangeRound, CounterpartyComments = d.CounterpartyComments, PurchaseOrderId = d.PurchaseOrderId, ContractId = d.ContractId, CreatorName = names.Name(d.CreatorUserId), CreateDate = d.CreateDate,
                };
                if (details) dto.History = await HistoryOfAsync("draft", d.Id, cancellationToken);
                list.Add(dto);
            }
            return list;
        }

        public async Task<ResponseDto<List<ContractDraftDto>>> GetDraftsAsync(FlowQuery query, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(CtRoles.Readers)) return Bad<List<ContractDraftDto>>(NotAllowed);
            query ??= new FlowQuery();
            IQueryable<CtContractDraft> q = _uow.Set<CtContractDraft>().Where(d => !d.Deleted);
            if (query.Status.HasValue) q = q.Where(d => (int)d.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(d => d.Number.Contains(text) || d.Title.Contains(text) || d.CounterpartyName.Contains(text));
            return ResponseDtoHelper.Ok(await LoadDraftsAsync(q, false, cancellationToken));
        }

        public async Task<ResponseDto<ContractDraftDto>> GetDraftAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(CtRoles.Readers)) return Bad<ContractDraftDto>(NotAllowed);
            List<ContractDraftDto> list = await LoadDraftsAsync(_uow.Set<CtContractDraft>().Where(d => d.Id == id && !d.Deleted), true, cancellationToken);
            return list.Count == 0 ? ResponseDtoHelper.NotFound<ContractDraftDto>("پیش‌نویس قرارداد پیدا نشد.") : ResponseDtoHelper.Ok(list[0]);
        }

        public async Task<ResponseDto<ContractDraftDto>> SaveDraftAsync(SaveContractDraftDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<ContractDraftDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(CtRoles.Commercial)) return Bad<ContractDraftDto>(NotAllowed + " (بازرگانی)");
            if (Clean(dto.Title) == null || Clean(dto.Subject) == null) return Bad<ContractDraftDto>("عنوان و موضوع قرارداد را بنویسید.");
            if (dto.ContractAmount <= 0) return Bad<ContractDraftDto>("مبلغ قرارداد باید بیشتر از صفر باشد (ریال).");
            if (dto.StartDate.HasValue && dto.EndDate.HasValue && dto.EndDate < dto.StartDate) return Bad<ContractDraftDto>("تاریخ پایان پیش از شروع است.");
            if ((dto.AdvancePercent ?? 0) < 0 || (dto.AdvancePercent ?? 0) > 100 || (dto.RetentionPercent ?? 0) < 0 || (dto.RetentionPercent ?? 0) > 100 || (dto.PerformanceGuaranteePercent ?? 0) < 0 || (dto.PerformanceGuaranteePercent ?? 0) > 100) return Bad<ContractDraftDto>("درصدها باید بین ۰ و ۱۰۰ باشد.");
            // a draft that rests on a tender needs a confirmed winner
            if (dto.TenderRunId.HasValue)
            {
                CtTenderRun tr = await _uow.Set<CtTenderRun>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.TenderRunId.Value && !x.Deleted, cancellationToken);
                if (tr == null) return ResponseDtoHelper.NotFound<ContractDraftDto>("مناقصه پیدا نشد.");
                if (tr.Status != CtTenderStatus.WinnerConfirmed) return Bad<ContractDraftDto>("برنده مناقصه هنوز تایید نشده است.");
            }
            CtContractDraft d;
            if (dto.Id.HasValue)
            {
                d = await _uow.Set<CtContractDraft>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (d == null) return ResponseDtoHelper.NotFound<ContractDraftDto>("پیش‌نویس قرارداد پیدا نشد.");
                if (d.Status != CtDraftStatus.Draft && d.Status != CtDraftStatus.CommentsReceived) return Bad<ContractDraftDto>("پیش‌نویس در این وضعیت ویرایش نمی‌شود.");
            }
            else
            {
                d = new CtContractDraft { Number = await InventoryNumbers.NextNumberAsync(_uow, Pb6Numbers.ContractDraft, "CDR", DateTime.Today, cancellationToken), Status = CtDraftStatus.Draft, CreateDate = DateTimeOffset.Now, CreatorUserId = Me };
                _uow.Set<CtContractDraft>().Add(d);
            }
            d.DealRequestId = dto.DealRequestId; d.TenderRunId = dto.TenderRunId; d.ProviderId = dto.ProviderId; d.CounterpartyName = Clean(dto.CounterpartyName); d.ContractKindTitle = Clean(dto.ContractKindTitle);
            d.TemplateCode = Clean(dto.TemplateCode); d.Title = dto.Title.Trim(); d.Subject = dto.Subject.Trim(); d.ContractAmount = Math.Round(dto.ContractAmount, 0); d.StartDate = dto.StartDate?.Date; d.EndDate = dto.EndDate?.Date;
            d.DurationDays = dto.DurationDays; d.AdvancePercent = dto.AdvancePercent; d.RetentionPercent = dto.RetentionPercent; d.PerformanceGuaranteePercent = dto.PerformanceGuaranteePercent;
            d.PaymentTerms = Clean(dto.PaymentTerms); d.SpecialConditions = Clean(dto.SpecialConditions);
            d.EditorUserIds = LedgerMath.WithEditor(d.EditorUserIds, Me);
            d.ModifyDate = DateTimeOffset.Now; d.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetDraftAsync(d.Id, cancellationToken);
        }

        public async Task<ResponseDto<ContractDraftDto>> DraftActionAsync(FlowActionDto dto, CancellationToken cancellationToken)
        {
            CtContractDraft d = await _uow.Set<CtContractDraft>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (d == null) return ResponseDtoHelper.NotFound<ContractDraftDto>("پیش‌نویس قرارداد پیدا نشد.");
            string note = Clean(dto.Note);
            string action = dto.Action?.Trim().ToLowerInvariant();
            switch (action)
            {
                case "exchange":
                    if (d.Status != CtDraftStatus.Draft && d.Status != CtDraftStatus.CommentsReceived) return Bad<ContractDraftDto>("پیش‌نویس آماده ارسال نیست.");
                    if (!_user.IsInRole(CtRoles.Commercial)) return Bad<ContractDraftDto>(NotAllowed);
                    d.Status = CtDraftStatus.Exchanged; d.ExchangeRound++;
                    break;
                case "comments":
                    if (d.Status != CtDraftStatus.Exchanged) return Bad<ContractDraftDto>("پیش‌نویس هنوز برای طرف مقابل ارسال نشده است.");
                    if (!_user.IsInRole(CtRoles.Commercial)) return Bad<ContractDraftDto>(NotAllowed);
                    if (note == null) return Bad<ContractDraftDto>("نظرات طرف مقابل را بنویسید.");
                    d.Status = CtDraftStatus.CommentsReceived; d.CounterpartyComments = note;
                    break;
                case "agree":
                    if (d.Status != CtDraftStatus.Exchanged && d.Status != CtDraftStatus.CommentsReceived) return Bad<ContractDraftDto>("پیش‌نویس در مرحله تبادل نیست.");
                    if (!_user.IsInRole("FINANCEMANAGER,CEO")) return Bad<ContractDraftDto>(NotAllowed);
                    if (LedgerMath.Touched(d.CreatorUserId, d.EditorUserIds, Me)) return Bad<ContractDraftDto>("تهیه‌کننده پیش‌نویس توافق آن را تایید نمی‌کند.");
                    d.Status = CtDraftStatus.Agreed;
                    break;
                case "sign":
                    if (d.Status != CtDraftStatus.Agreed) return Bad<ContractDraftDto>("پیش‌نویس هنوز توافق نشده است.");
                    if (!_user.IsInRole("CEO")) return Bad<ContractDraftDto>(NotAllowed + " (مدیرعامل)");
                    if (LedgerMath.Touched(d.CreatorUserId, d.EditorUserIds, Me)) return Bad<ContractDraftDto>("تهیه‌کننده پیش‌نویس آن را امضا نمی‌کند.");
                    d.Status = CtDraftStatus.Signed; d.SignedDate = (dto.Date ?? DateTime.Today).Date; d.SignedByUserId = Me;
                    d.ContractNumber = Clean(dto.Reference) ?? d.Number;
                    break;
                case "notify":
                    if (d.Status != CtDraftStatus.Signed) return Bad<ContractDraftDto>("قرارداد هنوز امضا نشده است.");
                    if (!_user.IsInRole(CtRoles.Commercial)) return Bad<ContractDraftDto>(NotAllowed);
                    d.Status = CtDraftStatus.Notified;
                    break;
                case "cancel":
                    if (d.Status == CtDraftStatus.Signed || d.Status == CtDraftStatus.Notified || d.Status == CtDraftStatus.Cancelled) return Bad<ContractDraftDto>("قرارداد امضاشده لغو نمی‌شود.");
                    if (!_user.IsInRole(CtRoles.Commercial)) return Bad<ContractDraftDto>(NotAllowed);
                    if (note == null) return Bad<ContractDraftDto>("علت لغو را بنویسید.");
                    d.Status = CtDraftStatus.Cancelled;
                    break;
                default:
                    return Bad<ContractDraftDto>("عمل نامعتبر است.");
            }
            d.ModifyDate = DateTimeOffset.Now; d.ModifierUserId = Me;
            History("draft", d.Id, action, note, d.ExchangeRound);
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<ContractDraftDto>("اطلاعات هم‌زمان تغییر کرد؛ دوباره تلاش کنید."); }
            bool waiting = d.Status == CtDraftStatus.Exchanged || d.Status == CtDraftStatus.CommentsReceived || d.Status == CtDraftStatus.Agreed;
            await OfficeGates.SyncAsync(_notify, "ctdraft:" + d.Id, waiting, "procurement", "ct-draft", d.Id, "پیش‌نویس قرارداد " + d.Number, "/procurement/contracts-system?draft=" + d.Id,
                d.Status == CtDraftStatus.Agreed ? "CEO" : "FINANCEMANAGER,CEO", cancellationToken);
            if (d.Status == CtDraftStatus.Notified && _notify != null)
                await _notify.NotifyAsync(new NotifyRequest { SourceKey = "ctdraft-notified:" + d.Id, Module = "procurement", SourceType = "ct-draft", SourceId = d.Id, Title = "قرارداد " + d.ContractNumber + " ابلاغ شد", Link = "/procurement/contracts-system?draft=" + d.Id, Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Alert, UserIds = new List<int> { d.CreatorUserId } }, cancellationToken);
            return await GetDraftAsync(d.Id, cancellationToken);
        }

        public async Task<ResponseDto<ContractDraftPrintDto>> GetDraftPrintAsync(int id, CancellationToken cancellationToken)
        {
            ResponseDto<ContractDraftDto> d = await GetDraftAsync(id, cancellationToken);
            if (!d.Success || d.Data == null) return Bad<ContractDraftPrintDto>(d.Message);
            var print = new ContractDraftPrintDto { Draft = d.Data, BuyerName = "خریدار", AmountInWords = PersianNumber.ToWords(d.Data.ContractAmount) + " ریال", TemplateTitle = d.Data.TemplateCode ?? "قرارداد" };
            print.Clauses.Add(new ContractPrintClauseDto { Number = "۱", Title = "موضوع قرارداد", Text = d.Data.Subject });
            print.Clauses.Add(new ContractPrintClauseDto { Number = "۲", Title = "مبلغ قرارداد", Text = $"مبلغ قرارداد {d.Data.ContractAmount:N0} ریال ({print.AmountInWords}) است." });
            if (d.Data.StartDate.HasValue || d.Data.DurationDays.HasValue)
                print.Clauses.Add(new ContractPrintClauseDto { Number = "۳", Title = "مدت قرارداد", Text = d.Data.DurationDays.HasValue ? $"مدت قرارداد {d.Data.DurationDays} روز است." : "مدت قرارداد از تاریخ شروع تا تاریخ پایان است." });
            if (Clean(d.Data.PaymentTerms) != null) print.Clauses.Add(new ContractPrintClauseDto { Number = "۴", Title = "شرایط پرداخت", Text = d.Data.PaymentTerms });
            if (Clean(d.Data.SpecialConditions) != null) print.Clauses.Add(new ContractPrintClauseDto { Number = "۵", Title = "شرایط خصوصی", Text = d.Data.SpecialConditions });
            return ResponseDtoHelper.Ok(print);
        }

        // ============================================================================================ changes cartable and mapping

        public async Task<ResponseDto<List<ContractChangeDto>>> GetChangesAsync(FlowQuery query, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(CtRoles.Readers)) return Bad<List<ContractChangeDto>>(NotAllowed);
            query ??= new FlowQuery();
            IQueryable<PurchaseOrderAddendum> q = _uow.Set<PurchaseOrderAddendum>().AsNoTracking().Where(a => !a.Deleted);
            if (query.PurchaseOrderId.HasValue) q = q.Where(a => a.PurchaseOrderId == query.PurchaseOrderId.Value);
            if (query.Status.HasValue) q = q.Where(a => (int)a.Status == query.Status.Value);
            var rows = await q.OrderByDescending(a => a.Id).Take(300).Select(a => new { a.Id, a.Number, a.Status, a.Reason, a.AmountBefore, a.AmountAfter, a.DeliveryDateBefore, a.DeliveryDateAfter, a.CreateDate, a.PurchaseOrderId, OrderNumber = a.PurchaseOrder.Number }).ToListAsync(cancellationToken);
            var list = rows.Select(a => new ContractChangeDto
            {
                Kind = "addendum", Id = a.Id, Number = a.Number, ContractTitle = a.OrderNumber, Status = a.Status.ToString(), Reason = a.Reason, AmountBefore = a.AmountBefore, AmountAfter = a.AmountAfter,
                DeliveryDateBefore = a.DeliveryDateBefore, DeliveryDateAfter = a.DeliveryDateAfter, CreateDate = a.CreateDate, Route = "/procurement/orders?id=" + a.PurchaseOrderId,
            }).ToList();
            return ResponseDtoHelper.Ok(list);
        }

        public ResponseDto<List<ContractMappingRowDto>> GetMapping() => ResponseDtoHelper.Ok(new List<ContractMappingRowDto>
        {
            new ContractMappingRowDto { Need = "درخواست معامله (کارتابل ۱)", ExistingModule = "پیشنهاد فروش / قرارداد پروژه", Extension = "CtDealRequest با زنجیره تایید بر پایه سطح معامله" },
            new ContractMappingRowDto { Need = "برگزاری مناقصه و برنده (کارتابل ۲)", ExistingModule = "مناقصه قدیمی (Tender) و استعلام خرید", Extension = "CtTenderRun و شرکت‌کنندگان؛ برنده را یک نفر انتخاب و نفر دیگر تایید می‌کند" },
            new ContractMappingRowDto { Need = "پیش‌نویس و تبادل قرارداد (کارتابل ۳)", ExistingModule = "سفارش خرید به‌عنوان قرارداد تامین‌کننده و شرایط پروژه", Extension = "CtContractDraft با دور تبادل، امضا و ابلاغ" },
            new ContractMappingRowDto { Need = "تغییرات قرارداد", ExistingModule = "الحاقیه سفارش خرید", Extension = "کارتابل یکپارچه تغییرات" },
            new ContractMappingRowDto { Need = "CBS", ExistingModule = "WBS کنترل پروژه", Extension = "CtCbsItem متصل به گره WBS؛ پیشرفت از فعالیت‌ها" },
            new ContractMappingRowDto { Need = "صورت‌وضعیت", ExistingModule = "درخواست پرداخت نوع Progress", Extension = "PsStatement با تایید دفتر فنی، نظارت و مدیر پروژه" },
        });
    }
}
