using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production.Dnc
{
    internal static class ExtrasShared
    {
        public static async Task<Dictionary<int, string>> UserNamesAsync(IUnitOfWork uow, IEnumerable<int?> ids, CancellationToken ct)
        {
            List<int> list = ids.Where(i => i.HasValue && i.Value > 0).Select(i => i.Value).Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, string>();
            var rows = await uow.Set<User>().AsNoTracking().Where(u => list.Contains(u.Id)).Select(u => new { u.Id, u.Name, u.LastName, u.UserName }).ToListAsync(ct);
            return rows.ToDictionary(u => u.Id, u => { string n = ((u.Name ?? "") + " " + (u.LastName ?? "")).Trim(); return n.Length > 0 ? n : u.UserName; });
        }

        public static string NameOf(Dictionary<int, string> names, int? id) => id.HasValue && names.TryGetValue(id.Value, out string n) ? n : null;

        public static PagedList<T> Page<T>(List<T> rows, int total, int number, int size)
        {
            PagedList<T> page = PagedList<T>.CreateAsync(rows, 1, int.MaxValue, CancellationToken.None).GetAwaiter().GetResult();
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size;
            page.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return page;
        }
    }

    // ================================================================================== in-process checklists

    /// <summary>The pure rules of a checklist answer.</summary>
    public static class MfChecklistRules
    {
        public static bool Passed(MfChecklistItem item, bool? yesNo, decimal? value, string text) => item.Kind switch
        {
            MfChecklistItemKind.YesNo => yesNo == true,
            MfChecklistItemKind.Measure => value.HasValue && (!item.Min.HasValue || value.Value >= item.Min.Value) && (!item.Max.HasValue || value.Value <= item.Max.Value),
            _ => !string.IsNullOrWhiteSpace(text),
        };

        public static bool Answered(MfChecklistItem item, bool? yesNo, decimal? value, string text) => item.Kind switch
        {
            MfChecklistItemKind.YesNo => yesNo.HasValue,
            MfChecklistItemKind.Measure => value.HasValue,
            _ => !string.IsNullOrWhiteSpace(text),
        };

        /// <summary>Every active mandatory item answered and passing; an optional item that was answered must pass too.</summary>
        public static bool AllPassed(IEnumerable<MfChecklistItem> items, IEnumerable<MfChecklistAnswer> answers)
        {
            var byItem = (answers ?? Enumerable.Empty<MfChecklistAnswer>()).GroupBy(a => a.ItemId).ToDictionary(g => g.Key, g => g.First());
            foreach (MfChecklistItem i in items.Where(x => x.IsActive))
            {
                byItem.TryGetValue(i.Id, out MfChecklistAnswer a);
                bool answered = a != null && Answered(i, a.YesNo, a.Value, a.Text);
                if (i.Mandatory && !answered) return false;
                if (answered && !Passed(i, a.YesNo, a.Value, a.Text)) return false;
            }
            return true;
        }
    }

    /// <summary>The checklist check at the finish of an operation on the terminal (called by MfShopFloorService.FinishAsync). Null or the Persian reason.</summary>
    public static class MfChecklistGate
    {
        public static async Task<string> FinishProblemAsync(IUnitOfWork uow, MfOperation op, CancellationToken ct)
        {
            if (op?.OperationTemplateId == null) return null;
            MfChecklistTemplate t = await uow.Set<MfChecklistTemplate>().AsNoTracking().FirstOrDefaultAsync(x => !x.Deleted && x.IsActive && x.RequiredToFinish && x.OperationTemplateId == op.OperationTemplateId, ct);
            if (t == null) return null;
            MfChecklistResult r = await uow.Set<MfChecklistResult>().AsNoTracking().FirstOrDefaultAsync(x => !x.Deleted && x.OperationId == op.Id, ct);
            if (r == null || r.Status == MfChecklistStatus.Draft) return $"پیش از پایان، چک‌لیست «{t.Title}» این مرحله را تکمیل و ارسال کنید.";
            if (r.Status == MfChecklistStatus.Rejected) return $"چک‌لیست «{t.Title}» در بازبینی رد شد؛ آن را اصلاح و دوباره ارسال کنید.";
            if (!r.AllPassed) return $"همه بندهای چک‌لیست «{t.Title}» قبول نیستند؛ برای قطعه درخواست بازرسی یا گزارش عدم انطباق ثبت کنید.";
            return null;
        }
    }

    public interface IMfChecklistService
    {
        Task<ResponseDto<ChecklistTemplateDto>> GetTemplateAsync(int operationTemplateId, CancellationToken ct);
        Task<ResponseDto<ChecklistTemplateDto>> SaveTemplateAsync(ChecklistTemplateDto dto, CancellationToken ct);
        Task<ResponseDto<OperationChecklistDto>> GetForOperationAsync(int operationId, CancellationToken ct);
        Task<ResponseDto<OperationChecklistDto>> SaveAnswersAsync(SaveChecklistAnswersDto dto, CancellationToken ct);
        Task<ResponseDto<OperationChecklistDto>> ReviewAsync(ChecklistReviewDto dto, CancellationToken ct);
    }

    /// <summary>In-process checklists: a template per routing step (production and engineering write it, QC too), filled at the terminal, reviewed by QC or the supervisor (never the person who filled it).</summary>
    public class MfChecklistService : IMfChecklistService
    {
        public const string TemplateEditors = ProductionRoles.StandardEditors + ",QC,QA";
        public const string Reviewers = "QC," + ProductionRoles.Supervisors;
        public const string Fillers = ProductionRoles.Terminal + ",QC";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public MfChecklistService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static ResponseDto<T> Bad<T>(string m) => MfText.Bad<T>(m);
        private static bool IsAi => global::Services.NasbNiro.Ai.AiActing.IsActing;

        private async Task<ChecklistTemplateDto> TemplateDtoAsync(MfChecklistTemplate t, int operationTemplateId, CancellationToken ct) => new ChecklistTemplateDto
        {
            Id = t?.Id, OperationTemplateId = operationTemplateId, Title = t?.Title, RequiredToFinish = t?.RequiredToFinish ?? true, IsActive = t?.IsActive ?? true,
            OperationTitle = await _uow.Set<OperationTemplate>().AsNoTracking().Where(o => o.Id == operationTemplateId).Select(o => o.Title).FirstOrDefaultAsync(ct),
            Items = (t?.Items ?? new List<MfChecklistItem>()).Where(i => i.IsActive).OrderBy(i => i.Sequence).Select(i => new ChecklistItemDto
            {
                Id = i.Id, Sequence = i.Sequence, Text = i.Text, Kind = i.Kind, Nominal = i.Nominal, Min = i.Min, Max = i.Max, Unit = i.Unit, Mandatory = i.Mandatory,
            }).ToList(),
            RowVersion = MfConcurrency.Text(t?.RowVersion),
        };

        public async Task<ResponseDto<ChecklistTemplateDto>> GetTemplateAsync(int operationTemplateId, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<ChecklistTemplateDto>(MfText.NotAllowed);
            MfChecklistTemplate t = await _uow.Set<MfChecklistTemplate>().AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => !x.Deleted && x.OperationTemplateId == operationTemplateId, ct);
            return ResponseDtoHelper.Ok(await TemplateDtoAsync(t, operationTemplateId, ct));
        }

        public async Task<ResponseDto<ChecklistTemplateDto>> SaveTemplateAsync(ChecklistTemplateDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(TemplateEditors) || IsAi) return Bad<ChecklistTemplateDto>(MfText.NotAllowed);
            if (dto == null || string.IsNullOrWhiteSpace(dto.Title)) return Bad<ChecklistTemplateDto>("عنوان چک‌لیست را وارد کنید.");
            if (!await _uow.Set<OperationTemplate>().AnyAsync(o => o.Id == dto.OperationTemplateId && !o.Deleted, ct)) return Bad<ChecklistTemplateDto>("مرحله مسیر ساخت پیدا نشد.");
            var items = dto.Items ?? new List<ChecklistItemDto>();
            if (items.Count == 0 || items.Count > 100) return Bad<ChecklistTemplateDto>("چک‌لیست باید بین ۱ و ۱۰۰ بند داشته باشد.");
            foreach (var i in items)
            {
                if (string.IsNullOrWhiteSpace(i.Text) || i.Text.Length > 1000 || !Enum.IsDefined(typeof(MfChecklistItemKind), i.Kind)) return Bad<ChecklistTemplateDto>("متن و نوع همه بندها را درست وارد کنید.");
                if (i.Kind == MfChecklistItemKind.Measure && !i.Min.HasValue && !i.Max.HasValue) return Bad<ChecklistTemplateDto>($"برای بند اندازه‌گیری «{i.Text}» حد پایین یا بالا را وارد کنید.");
                if (i.Min.HasValue && i.Max.HasValue && i.Min > i.Max) return Bad<ChecklistTemplateDto>($"حد پایین بند «{i.Text}» از حد بالای آن بیشتر است.");
            }
            MfChecklistTemplate t = await _uow.Set<MfChecklistTemplate>().Include(x => x.Items).FirstOrDefaultAsync(x => !x.Deleted && x.OperationTemplateId == dto.OperationTemplateId, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            if (t == null)
            {
                t = new MfChecklistTemplate { OperationTemplateId = dto.OperationTemplateId, CreateDate = now, CreatorUserId = Me };
                _uow.Set<MfChecklistTemplate>().Add(t);
            }
            else if (!MfConcurrency.Apply(_uow, t, dto.RowVersion)) return Bad<ChecklistTemplateDto>(MfText.Concurrency);
            t.Title = dto.Title.Trim(); t.RequiredToFinish = dto.RequiredToFinish; t.IsActive = dto.IsActive; t.ModifyDate = now; t.ModifierUserId = Me;
            // an item keeps its id (answers point at it); a removed item is only switched off
            var keep = new HashSet<int>();
            int seq = 0;
            foreach (var i in items)
            {
                seq += 10;
                MfChecklistItem row = i.Id.HasValue ? t.Items.FirstOrDefault(x => x.Id == i.Id.Value) : null;
                if (row == null) { row = new MfChecklistItem(); t.Items.Add(row); }
                else keep.Add(row.Id);
                row.Sequence = seq; row.Text = i.Text.Trim(); row.Kind = i.Kind; row.Nominal = i.Nominal; row.Min = i.Min; row.Max = i.Max; row.Unit = MfText.Clean(i.Unit); row.Mandatory = i.Mandatory; row.IsActive = true;
            }
            foreach (MfChecklistItem old in t.Items.Where(x => x.Id > 0 && !keep.Contains(x.Id))) old.IsActive = false;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<ChecklistTemplateDto>(MfText.Concurrency); }
            catch (DbUpdateException) { return Bad<ChecklistTemplateDto>("برای این مرحله هم‌زمان چک‌لیست دیگری ثبت شد."); }
            return await GetTemplateAsync(dto.OperationTemplateId, ct);
        }

        public async Task<ResponseDto<OperationChecklistDto>> GetForOperationAsync(int operationId, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<OperationChecklistDto>(MfText.NotAllowed);
            MfOperation op = await _uow.Set<MfOperation>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == operationId && !o.Deleted, ct);
            if (op == null) return MfText.NotFound<OperationChecklistDto>("عملیات پیدا نشد.");
            var dto = new OperationChecklistDto { OperationId = op.Id };
            if (op.OperationTemplateId == null) return ResponseDtoHelper.Ok(dto);
            MfChecklistTemplate t = await _uow.Set<MfChecklistTemplate>().AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => !x.Deleted && x.IsActive && x.OperationTemplateId == op.OperationTemplateId, ct);
            if (t == null) return ResponseDtoHelper.Ok(dto);
            dto.HasTemplate = true;
            dto.Template = await TemplateDtoAsync(t, op.OperationTemplateId.Value, ct);
            MfChecklistResult r = await _uow.Set<MfChecklistResult>().AsNoTracking().Include(x => x.Answers).FirstOrDefaultAsync(x => !x.Deleted && x.OperationId == op.Id, ct);
            bool filler = _user.IsInRole(Fillers) && !IsAi, reviewer = _user.IsInRole(Reviewers) && !IsAi;
            if (r != null)
            {
                var names = await ExtrasShared.UserNamesAsync(_uow, new int?[] { r.FilledByUserId, r.ReviewedByUserId }, ct);
                dto.ResultId = r.Id; dto.Status = r.Status; dto.AllPassed = r.AllPassed; dto.FilledByName = ExtrasShared.NameOf(names, r.FilledByUserId);
                dto.SubmittedAtText = MfText.Stamp(r.SubmittedAt); dto.ReviewedByName = ExtrasShared.NameOf(names, r.ReviewedByUserId); dto.ReviewedAtText = MfText.Stamp(r.ReviewedAt);
                dto.ReviewNote = r.ReviewNote; dto.RowVersion = MfConcurrency.Text(r.RowVersion);
                dto.Answers = r.Answers.Select(a => new ChecklistAnswerDto { ItemId = a.ItemId, YesNo = a.YesNo, Value = a.Value, Text = a.Text, Passed = a.Passed }).ToList();
                if (r.Status == MfChecklistStatus.Submitted && reviewer && r.FilledByUserId != Me) { dto.AllowedActions.Add("approve"); dto.AllowedActions.Add("reject"); }
            }
            if (filler && (r == null || r.Status == MfChecklistStatus.Draft || r.Status == MfChecklistStatus.Rejected) && op.Status != MfOperationStatus.Done && op.Status != MfOperationStatus.Skipped)
            {
                dto.AllowedActions.Add("save");
                dto.AllowedActions.Add("submit");
            }
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<OperationChecklistDto>> SaveAnswersAsync(SaveChecklistAnswersDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(Fillers) || IsAi) return Bad<OperationChecklistDto>(MfText.NotAllowed);
            if (dto == null) return Bad<OperationChecklistDto>(MfText.InvalidData);
            MfOperation op = await _uow.Set<MfOperation>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == dto.OperationId && !o.Deleted, ct);
            if (op == null) return MfText.NotFound<OperationChecklistDto>("عملیات پیدا نشد.");
            if (op.Status == MfOperationStatus.Done || op.Status == MfOperationStatus.Skipped) return Bad<OperationChecklistDto>("این عملیات تمام شده است.");
            MfChecklistTemplate t = op.OperationTemplateId == null ? null
                : await _uow.Set<MfChecklistTemplate>().AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => !x.Deleted && x.IsActive && x.OperationTemplateId == op.OperationTemplateId, ct);
            if (t == null) return Bad<OperationChecklistDto>("این مرحله چک‌لیست ندارد.");
            MfChecklistResult r = await _uow.Set<MfChecklistResult>().Include(x => x.Answers).FirstOrDefaultAsync(x => !x.Deleted && x.OperationId == op.Id, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            if (r == null)
            {
                r = new MfChecklistResult { OperationId = op.Id, TemplateId = t.Id, Status = MfChecklistStatus.Draft, CreateDate = now, CreatorUserId = Me };
                _uow.Set<MfChecklistResult>().Add(r);
            }
            else
            {
                if (r.Status == MfChecklistStatus.Submitted || r.Status == MfChecklistStatus.Approved) return Bad<OperationChecklistDto>("چک‌لیست ارسال شده است و تغییر نمی‌کند.");
                if (!MfConcurrency.Apply(_uow, r, dto.RowVersion)) return Bad<OperationChecklistDto>(MfText.Concurrency);
            }
            var items = t.Items.Where(i => i.IsActive).ToDictionary(i => i.Id);
            foreach (var a in dto.Answers ?? new List<ChecklistAnswerDto>())
            {
                if (!items.TryGetValue(a.ItemId, out MfChecklistItem item)) return Bad<OperationChecklistDto>("بند چک‌لیست پیدا نشد.");
                if (a.Text != null && a.Text.Length > 1000) return Bad<OperationChecklistDto>("متن پاسخ حداکثر ۱۰۰۰ نویسه است.");
                MfChecklistAnswer row = r.Answers.FirstOrDefault(x => x.ItemId == a.ItemId);
                if (row == null) { row = new MfChecklistAnswer { ItemId = a.ItemId }; r.Answers.Add(row); }
                row.YesNo = a.YesNo; row.Value = a.Value; row.Text = MfText.Clean(a.Text); row.Passed = MfChecklistRules.Passed(item, row.YesNo, row.Value, row.Text);
            }
            r.AllPassed = MfChecklistRules.AllPassed(items.Values, r.Answers);
            r.FilledByUserId = Me; r.ModifyDate = now; r.ModifierUserId = Me;
            if (dto.Submit)
            {
                bool missing = items.Values.Any(i => i.Mandatory && !r.Answers.Any(x => x.ItemId == i.Id && MfChecklistRules.Answered(i, x.YesNo, x.Value, x.Text)));
                if (missing) return Bad<OperationChecklistDto>("همه بندهای الزامی را پاسخ دهید.");
                r.Status = MfChecklistStatus.Submitted; r.SubmittedAt = now; r.ReviewedByUserId = null; r.ReviewedAt = null; r.ReviewNote = null;
            }
            else r.Status = MfChecklistStatus.Draft;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<OperationChecklistDto>(MfText.Concurrency); }
            catch (DbUpdateException) { return Bad<OperationChecklistDto>("چک‌لیست این عملیات هم‌زمان ثبت شد؛ صفحه را دوباره باز کنید."); }
            await GateAsync(r, t.Title, ct);
            return await GetForOperationAsync(op.Id, ct);
        }

        private Task GateAsync(MfChecklistResult r, string title, CancellationToken ct) =>
            OfficeGates.SyncAsync(_notify, "mf:checklist:" + r.Id, r.Status == MfChecklistStatus.Submitted && !r.Deleted, "production", "checklist", r.Id,
                $"چک‌لیست «{title}» منتظر بازبینی است" + (r.AllPassed ? "" : " (بند ردشده دارد)"), "/production/operations/" + r.OperationId, Reviewers, ct);

        public async Task<ResponseDto<OperationChecklistDto>> ReviewAsync(ChecklistReviewDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(Reviewers) || IsAi) return Bad<OperationChecklistDto>(MfText.NotAllowed);
            if (dto == null || (dto.Action != "approve" && dto.Action != "reject")) return Bad<OperationChecklistDto>(MfText.InvalidData);
            MfChecklistResult r = await _uow.Set<MfChecklistResult>().FirstOrDefaultAsync(x => !x.Deleted && x.OperationId == dto.OperationId, ct);
            if (r == null || r.Status != MfChecklistStatus.Submitted) return Bad<OperationChecklistDto>("چک‌لیستی منتظر بازبینی نیست.");
            if (r.FilledByUserId == Me) return Bad<OperationChecklistDto>(MfText.SameUser);
            if (!MfConcurrency.Apply(_uow, r, dto.RowVersion)) return Bad<OperationChecklistDto>(MfText.Concurrency);
            string note = MfText.Clean(dto.Note);
            if (dto.Action == "reject" && note == null) return Bad<OperationChecklistDto>("دلیل رد را بنویسید.");
            if (dto.Action == "approve" && !r.AllPassed) return Bad<OperationChecklistDto>("چک‌لیست بند ردشده دارد و تأیید نمی‌شود؛ آن را رد کنید و برای قطعه عدم انطباق ثبت کنید.");
            DateTimeOffset now = DateTimeOffset.Now;
            r.Status = dto.Action == "approve" ? MfChecklistStatus.Approved : MfChecklistStatus.Rejected;
            r.ReviewedByUserId = Me; r.ReviewedAt = now; r.ReviewNote = note; r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<OperationChecklistDto>(MfText.Concurrency); }
            string title = await _uow.Set<MfChecklistTemplate>().AsNoTracking().Where(x => x.Id == r.TemplateId).Select(x => x.Title).FirstOrDefaultAsync(ct);
            await GateAsync(r, title, ct);
            return await GetForOperationAsync(dto.OperationId, ct);
        }
    }

    // ================================================================================== staff performance events

    public interface IHrEventService
    {
        Task<ResponseDto<PagedList<HrEventDto>>> GetEventsAsync(HrEventQuery query, CancellationToken ct);
        Task<ResponseDto<HrEventDto>> SaveAsync(SaveHrEventDto dto, CancellationToken ct);
        Task<ResponseDto<HrEventDto>> ActionAsync(HrEventActionDto dto, CancellationToken ct);
        Task<ResponseDto<List<HrEmployeeSummaryDto>>> GetSummaryAsync(DateTime from, DateTime to, CancellationToken ct);
    }

    /// <summary>
    /// Performance events of the staff: written by a supervisor (QC writes quality feedback only), approved or rejected by HR or the production manager (never the writer, never an AI seat).
    /// The readers see every event; an employee sees his own approved events (the personnel file linked to his user).
    /// </summary>
    public class HrEventService : IHrEventService
    {
        public const string Writers = "SHOPSUPERVISOR,PRODUCTIONMANAGER,HR,QC";
        public const string Approvers = "HR,PRODUCTIONMANAGER";
        public const string Readers = Writers + ",CEO,PMO";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public HrEventService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static ResponseDto<T> Bad<T>(string m) => MfText.Bad<T>(m);
        private static bool IsAi => global::Services.NasbNiro.Ai.AiActing.IsActing;
        private bool OnlyQuality => _user.IsInRole("QC") && !_user.IsInRole("SHOPSUPERVISOR,PRODUCTIONMANAGER,HR");

        public static string KindText(HrEventKind k) => k switch
        {
            HrEventKind.Evaluation => "ارزیابی", HrEventKind.Disciplinary => "انضباطی", HrEventKind.QualityFeedback => "بازخورد کیفی", HrEventKind.Goal => "هدف", HrEventKind.Commendation => "تشویق", _ => k.ToString(),
        };

        private async Task<List<HrEventDto>> DtosAsync(List<HrPerformanceEvent> rows, CancellationToken ct)
        {
            List<int> empIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
            Dictionary<int, string> employees = MfNames.ToDictionary(await _uow.Set<FnEmployee>().AsNoTracking().Where(e => empIds.Contains(e.Id)).ToListAsync(ct));
            var names = await ExtrasShared.UserNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.CreatorUserId, r.DecidedByUserId }), ct);
            bool approver = _user.IsInRole(Approvers) && !IsAi;
            return rows.Select(r =>
            {
                var d = new HrEventDto
                {
                    Id = r.Id, EmployeeId = r.EmployeeId, EmployeeName = employees.TryGetValue(r.EmployeeId, out string en) ? en : null, Kind = r.Kind, EventDate = r.EventDate,
                    EventDateText = MfText.Date(r.EventDate), Title = r.Title, Description = r.Description, Score = r.Score, NcrId = r.NcrId, OperationId = r.OperationId, Status = r.Status,
                    CreatedByName = ExtrasShared.NameOf(names, r.CreatorUserId), CreatedAtText = MfText.Stamp(r.CreateDate), DecidedByName = ExtrasShared.NameOf(names, r.DecidedByUserId),
                    DecidedAtText = MfText.Stamp(r.DecidedAt), DecisionNote = r.DecisionNote, RowVersion = MfConcurrency.Text(r.RowVersion),
                };
                bool mine = r.CreatorUserId == Me;
                if (r.Status == HrEventStatus.Draft && mine) { d.AllowedActions.Add("edit"); d.AllowedActions.Add("submit"); d.AllowedActions.Add("delete"); }
                if (r.Status == HrEventStatus.Submitted && approver && !mine) { d.AllowedActions.Add("approve"); d.AllowedActions.Add("reject"); }
                return d;
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<HrEventDto>>> GetEventsAsync(HrEventQuery query, CancellationToken ct)
        {
            query ??= new HrEventQuery();
            IQueryable<HrPerformanceEvent> q = _uow.Set<HrPerformanceEvent>().AsNoTracking().Where(e => !e.Deleted);
            bool reader = _user.IsInRole(Readers);
            if (query.Mine == true || !reader)
            {
                // an employee: his own approved events only
                List<int> own = await _uow.Set<FnEmployee>().AsNoTracking().Where(e => e.UserId == Me).Select(e => e.Id).ToListAsync(ct);
                if (own.Count == 0 && !reader) return Bad<PagedList<HrEventDto>>(MfText.NotAllowed);
                q = q.Where(e => own.Contains(e.EmployeeId) && e.Status == HrEventStatus.Approved);
            }
            else if (OnlyQuality) q = q.Where(e => e.Kind == HrEventKind.QualityFeedback);
            if (query.EmployeeId.HasValue) q = q.Where(e => e.EmployeeId == query.EmployeeId.Value);
            if (query.Kind.HasValue) q = q.Where(e => e.Kind == query.Kind.Value);
            if (query.Status.HasValue) q = q.Where(e => e.Status == query.Status.Value);
            if (query.From.HasValue) q = q.Where(e => e.EventDate >= query.From.Value);
            if (query.To.HasValue) q = q.Where(e => e.EventDate < query.To.Value);
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize), number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            var rows = await q.OrderByDescending(e => e.EventDate).ThenByDescending(e => e.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(ExtrasShared.Page(await DtosAsync(rows, ct), total, number, size));
        }

        public async Task<ResponseDto<HrEventDto>> SaveAsync(SaveHrEventDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(Writers) || IsAi) return Bad<HrEventDto>(MfText.NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(HrEventKind), dto.Kind) || string.IsNullOrWhiteSpace(dto.Title)) return Bad<HrEventDto>("نوع و عنوان رویداد را وارد کنید.");
            if (OnlyQuality && dto.Kind != HrEventKind.QualityFeedback) return Bad<HrEventDto>("کنترل کیفیت فقط بازخورد کیفی ثبت می‌کند.");
            if (dto.Kind == HrEventKind.Evaluation && dto.Score is not (>= 1 and <= 5)) return Bad<HrEventDto>("امتیاز ارزیابی بین ۱ و ۵ است.");
            if (dto.Kind != HrEventKind.Evaluation && dto.Score.HasValue) return Bad<HrEventDto>("امتیاز فقط برای ارزیابی ثبت می‌شود.");
            if (dto.EventDate == default || dto.EventDate.Date > DateTime.Today.AddDays(400) || dto.EventDate.Year < 2000) return Bad<HrEventDto>("تاریخ رویداد نامعتبر است.");
            if (dto.Title.Length > 300 || (dto.Description?.Length ?? 0) > 4000) return Bad<HrEventDto>("عنوان حداکثر ۳۰۰ و شرح حداکثر ۴۰۰۰ نویسه است.");
            FnEmployee emp = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct);
            if (emp == null) return Bad<HrEventDto>("کارمند پیدا نشد.");
            if (emp.UserId == Me) return Bad<HrEventDto>("برای خودتان رویداد عملکرد ثبت نمی‌شود.");
            DateTimeOffset now = DateTimeOffset.Now;
            HrPerformanceEvent row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<HrPerformanceEvent>().FirstOrDefaultAsync(e => e.Id == dto.Id.Value && !e.Deleted, ct);
                if (row == null) return MfText.NotFound<HrEventDto>("رویداد پیدا نشد.");
                if (row.CreatorUserId != Me || row.Status != HrEventStatus.Draft) return Bad<HrEventDto>("فقط پیش‌نویسِ خودتان ویرایش می‌شود.");
                if (!MfConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<HrEventDto>(MfText.Concurrency);
            }
            else
            {
                row = new HrPerformanceEvent { Status = HrEventStatus.Draft, CreateDate = now, CreatorUserId = Me };
                _uow.Set<HrPerformanceEvent>().Add(row);
            }
            row.EmployeeId = dto.EmployeeId; row.Kind = dto.Kind; row.EventDate = dto.EventDate.Date; row.Title = dto.Title.Trim(); row.Description = MfText.Clean(dto.Description);
            row.Score = dto.Score; row.NcrId = dto.NcrId; row.OperationId = dto.OperationId; row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<HrEventDto>(MfText.Concurrency); }
            catch (DbUpdateException) { return Bad<HrEventDto>("عدم انطباق یا عملیات انتخاب‌شده معتبر نیست."); }
            return ResponseDtoHelper.Ok((await DtosAsync(new List<HrPerformanceEvent> { row }, ct))[0]);
        }

        public async Task<ResponseDto<HrEventDto>> ActionAsync(HrEventActionDto dto, CancellationToken ct)
        {
            if (dto == null || IsAi) return Bad<HrEventDto>(MfText.NotAllowed);
            HrPerformanceEvent row = await _uow.Set<HrPerformanceEvent>().FirstOrDefaultAsync(e => e.Id == dto.Id && !e.Deleted, ct);
            if (row == null) return MfText.NotFound<HrEventDto>("رویداد پیدا نشد.");
            if (!MfConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<HrEventDto>(MfText.Concurrency);
            DateTimeOffset now = DateTimeOffset.Now;
            bool mine = row.CreatorUserId == Me;
            switch (dto.Action)
            {
                case "submit":
                    if (!mine || row.Status != HrEventStatus.Draft) return Bad<HrEventDto>("فقط پیش‌نویسِ خودتان ارسال می‌شود.");
                    row.Status = HrEventStatus.Submitted; row.SubmittedAt = now;
                    break;
                case "delete":
                    if (!mine || row.Status != HrEventStatus.Draft) return Bad<HrEventDto>("فقط پیش‌نویسِ خودتان حذف می‌شود.");
                    row.Delete();
                    break;
                case "approve":
                case "reject":
                    if (!_user.IsInRole(Approvers)) return Bad<HrEventDto>(MfText.NotAllowed);
                    if (mine) return Bad<HrEventDto>(MfText.SameUser);
                    if (await _uow.Set<FnEmployee>().AnyAsync(e => e.Id == row.EmployeeId && e.UserId == Me, ct)) return Bad<HrEventDto>("درباره رویداد خودتان تصمیم نمی‌گیرید.");
                    if (row.Status != HrEventStatus.Submitted) return Bad<HrEventDto>("این رویداد منتظر تصمیم نیست.");
                    string note = MfText.Clean(dto.Note);
                    if (dto.Action == "reject" && note == null) return Bad<HrEventDto>("دلیل رد را بنویسید.");
                    row.Status = dto.Action == "approve" ? HrEventStatus.Approved : HrEventStatus.Rejected; row.DecidedByUserId = Me; row.DecidedAt = now; row.DecisionNote = note;
                    break;
                default:
                    return Bad<HrEventDto>(MfText.InvalidData);
            }
            row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<HrEventDto>(MfText.Concurrency); }
            await OfficeGates.SyncAsync(_notify, "hr:event:" + row.Id, row.Status == HrEventStatus.Submitted && !row.Deleted, "hr", "performance-event", row.Id,
                $"رویداد عملکرد «{row.Title}» ({KindText(row.Kind)}) منتظر تصمیم است", "/production/staff-events", Approvers, ct);
            return ResponseDtoHelper.Ok((await DtosAsync(new List<HrPerformanceEvent> { row }, ct))[0]);
        }

        public async Task<ResponseDto<List<HrEmployeeSummaryDto>>> GetSummaryAsync(DateTime from, DateTime to, CancellationToken ct)
        {
            if (!_user.IsInRole(Readers) || OnlyQuality) return Bad<List<HrEmployeeSummaryDto>>(MfText.NotAllowed);
            if (to <= from) return Bad<List<HrEmployeeSummaryDto>>("بازه زمانی نامعتبر است.");
            var rows = await _uow.Set<HrPerformanceEvent>().AsNoTracking().Where(e => !e.Deleted && e.Status == HrEventStatus.Approved && e.EventDate >= from && e.EventDate < to)
                .Select(e => new { e.EmployeeId, e.Kind, e.Score }).ToListAsync(ct);
            List<int> empIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
            Dictionary<int, string> employees = MfNames.ToDictionary(await _uow.Set<FnEmployee>().AsNoTracking().Where(e => empIds.Contains(e.Id)).ToListAsync(ct));
            return ResponseDtoHelper.Ok(rows.GroupBy(r => r.EmployeeId).Select(g => new HrEmployeeSummaryDto
            {
                EmployeeId = g.Key, EmployeeName = employees.TryGetValue(g.Key, out string n) ? n : null,
                Evaluations = g.Count(x => x.Kind == HrEventKind.Evaluation),
                AverageScore = g.Any(x => x.Kind == HrEventKind.Evaluation && x.Score.HasValue) ? Math.Round((decimal)g.Where(x => x.Kind == HrEventKind.Evaluation && x.Score.HasValue).Average(x => x.Score.Value), 2) : null,
                Disciplinary = g.Count(x => x.Kind == HrEventKind.Disciplinary), QualityFeedback = g.Count(x => x.Kind == HrEventKind.QualityFeedback),
                Goals = g.Count(x => x.Kind == HrEventKind.Goal), Commendations = g.Count(x => x.Kind == HrEventKind.Commendation),
            }).OrderBy(x => x.EmployeeName).ToList());
        }
    }

    // ================================================================================== cameras

    public interface IMfCameraService
    {
        Task<ResponseDto<List<CameraDto>>> GetCamerasAsync(CancellationToken ct);
        Task<ResponseDto<CameraDto>> SaveAsync(CameraDto dto, CancellationToken ct);
        Task<ResponseDto<CameraMediaDto>> SnapshotAsync(int id, CancellationToken ct);
        Task<ResponseDto<CameraMediaDto>> StreamAsync(int id, CancellationToken ct);
    }

    /// <summary>
    /// Live view of the shop cameras: the server fetches the picture (or the MJPEG stream) from the camera on the LAN and passes it on, so the browsers never reach the camera and the camera login stays
    /// in the server settings. Only private LAN addresses are accepted (no address on the internet can be named).
    /// </summary>
    public class MfCameraService : IMfCameraService
    {
        public const string Readers = ProductionRoles.Readers;
        public const string Editors = ProductionRoles.MasterEditors;

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IConfiguration _configuration;

        public MfCameraService(IUnitOfWork uow, IInventoryUserContext user, IConfiguration configuration = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _configuration = configuration;
        }

        private static ResponseDto<T> Bad<T>(string m) => MfText.Bad<T>(m);

        /// <summary>An http(s) address whose host is a private IPv4 address of the LAN (10/8, 172.16/12, 192.168/16, 169.254/16) or the loopback.</summary>
        public static bool LanUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(u.UserInfo)) return false;
            if (!IPAddress.TryParse(u.Host, out IPAddress ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }

        public async Task<ResponseDto<List<CameraDto>>> GetCamerasAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(Readers)) return Bad<List<CameraDto>>(MfText.NotAllowed);
            var rows = await _uow.Set<MfCamera>().AsNoTracking().Where(c => !c.Deleted).OrderBy(c => c.Title).ToListAsync(ct);
            List<int> mIds = rows.Where(r => r.MachineId.HasValue).Select(r => r.MachineId.Value).ToList(), wIds = rows.Where(r => r.WorkCenterId.HasValue).Select(r => r.WorkCenterId.Value).ToList();
            var machines = await _uow.Set<Machine>().AsNoTracking().Where(m => mIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Title, ct);
            var centres = await _uow.Set<MfWorkCenter>().AsNoTracking().Where(w => wIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Code, ct);
            bool editor = _user.IsInRole(Editors);
            return ResponseDtoHelper.Ok(rows.Select(c => new CameraDto
            {
                Id = c.Id, Title = c.Title, MachineId = c.MachineId, MachineTitle = c.MachineId.HasValue && machines.TryGetValue(c.MachineId.Value, out string mt) ? mt : null,
                WorkCenterId = c.WorkCenterId, WorkCenterCode = c.WorkCenterId.HasValue && centres.TryGetValue(c.WorkCenterId.Value, out string wc) ? wc : null,
                SnapshotUrl = editor ? c.SnapshotUrl : null, StreamUrl = editor ? c.StreamUrl : null, CredentialKey = editor ? c.CredentialKey : null,
                HasStream = !string.IsNullOrEmpty(c.StreamUrl), IsActive = c.IsActive, RowVersion = MfConcurrency.Text(c.RowVersion),
            }).ToList());
        }

        public async Task<ResponseDto<CameraDto>> SaveAsync(CameraDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(Editors) || global::Services.NasbNiro.Ai.AiActing.IsActing) return Bad<CameraDto>(MfText.NotAllowed);
            if (dto == null || string.IsNullOrWhiteSpace(dto.Title)) return Bad<CameraDto>("عنوان دوربین را وارد کنید.");
            string snap = MfText.Clean(dto.SnapshotUrl), stream = MfText.Clean(dto.StreamUrl), key = MfText.Clean(dto.CredentialKey);
            if (snap == null || !LanUrl(snap)) return Bad<CameraDto>("نشانی تصویر دوربین باید http یا https با نشانی IP شبکه داخلی باشد (بدون نام کاربری در نشانی).");
            if (stream != null && !LanUrl(stream)) return Bad<CameraDto>("نشانی پخش زنده باید http یا https با نشانی IP شبکه داخلی باشد.");
            if (key != null && !System.Text.RegularExpressions.Regex.IsMatch(key, @"^[A-Za-z0-9_\-]{1,50}$")) return Bad<CameraDto>("نام کلید اعتبارنامه فقط حروف لاتین، رقم، خط تیره و زیرخط است.");
            if (dto.MachineId.HasValue && !await _uow.Set<Machine>().AnyAsync(m => m.Id == dto.MachineId.Value, ct)) return Bad<CameraDto>("ماشین پیدا نشد.");
            if (dto.WorkCenterId.HasValue && !await _uow.Set<MfWorkCenter>().AnyAsync(w => w.Id == dto.WorkCenterId.Value && !w.Deleted, ct)) return Bad<CameraDto>("مرکز کار پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            MfCamera row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<MfCamera>().FirstOrDefaultAsync(c => c.Id == dto.Id.Value && !c.Deleted, ct);
                if (row == null) return MfText.NotFound<CameraDto>("دوربین پیدا نشد.");
                if (!MfConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<CameraDto>(MfText.Concurrency);
            }
            else
            {
                row = new MfCamera { CreateDate = now, CreatorUserId = _user.UserId };
                _uow.Set<MfCamera>().Add(row);
            }
            row.Title = dto.Title.Trim(); row.MachineId = dto.MachineId; row.WorkCenterId = dto.WorkCenterId; row.SnapshotUrl = snap; row.StreamUrl = stream; row.CredentialKey = key;
            row.IsActive = dto.IsActive; row.ModifyDate = now; row.ModifierUserId = _user.UserId;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<CameraDto>(MfText.Concurrency); }
            return ResponseDtoHelper.Ok((await GetCamerasAsync(ct)).Data.First(c => c.Id == row.Id));
        }

        /// <summary>One client per camera setting (reused: the handler pools the connections; a changed camera row gets a new client).</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HttpClient> Clients = new System.Collections.Concurrent.ConcurrentDictionary<string, HttpClient>();

        private HttpClient ClientFor(MfCamera c)
        {
            string user = string.IsNullOrEmpty(c.CredentialKey) ? null : _configuration?["Dnc:Credentials:" + c.CredentialKey + ":User"];
            string password = user == null ? null : _configuration?["Dnc:Credentials:" + c.CredentialKey + ":Password"];
            string key = c.Id + "|" + c.ModifyDate.UtcTicks + "|" + user + "|" + (password == null ? 0 : password.GetHashCode());
            return Clients.GetOrAdd(key, _ =>
            {
                var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
                if (user != null) handler.Credentials = new NetworkCredential(user, password);   // basic or digest, as the camera asks
                return new HttpClient(handler, true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            });
        }

        private async Task<(MfCamera Camera, string Error)> CameraAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(Readers)) return (null, MfText.NotAllowed);
            MfCamera c = await _uow.Set<MfCamera>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (c == null || !c.IsActive) return (null, "دوربین پیدا نشد یا غیرفعال است.");
            return (c, null);
        }

        public async Task<ResponseDto<CameraMediaDto>> SnapshotAsync(int id, CancellationToken ct)
        {
            var (c, error) = await CameraAsync(id, ct);
            if (error != null) return Bad<CameraMediaDto>(error);
            if (!LanUrl(c.SnapshotUrl)) return Bad<CameraMediaDto>("نشانی دوربین معتبر نیست.");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                using HttpResponseMessage r = await ClientFor(c).GetAsync(c.SnapshotUrl, timeout.Token);
                if (!r.IsSuccessStatusCode) return Bad<CameraMediaDto>("دوربین پاسخ نداد (" + (int)r.StatusCode + ").");
                string type = r.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return Bad<CameraMediaDto>("پاسخ دوربین تصویر نیست.");
                byte[] bytes = await r.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length > 10 * 1024 * 1024) return Bad<CameraMediaDto>("تصویر دوربین بیش از حد بزرگ است.");
                return ResponseDtoHelper.Ok(new CameraMediaDto { ContentType = type, Bytes = bytes });
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Bad<CameraMediaDto>("دوربین در دسترس نیست: " + NcLinkText.Network(ex));
            }
        }

        public async Task<ResponseDto<CameraMediaDto>> StreamAsync(int id, CancellationToken ct)
        {
            var (c, error) = await CameraAsync(id, ct);
            if (error != null) return Bad<CameraMediaDto>(error);
            if (string.IsNullOrEmpty(c.StreamUrl) || !LanUrl(c.StreamUrl)) return Bad<CameraMediaDto>("این دوربین پخش زنده تعریف‌شده ندارد.");
            try
            {
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connect.CancelAfter(TimeSpan.FromSeconds(8));
                HttpResponseMessage r = await ClientFor(c).GetAsync(c.StreamUrl, HttpCompletionOption.ResponseHeadersRead, connect.Token);
                string type = r.Content.Headers.ContentType?.ToString() ?? "";
                if (!r.IsSuccessStatusCode || !type.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
                {
                    r.Dispose();
                    return Bad<CameraMediaDto>("دوربین پخش زنده MJPEG نداد.");
                }
                // the controller copies the stream to the browser until it disconnects and then disposes the response
                return ResponseDtoHelper.Ok(new CameraMediaDto { ContentType = type, Stream = await r.Content.ReadAsStreamAsync(ct), Response = r });
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Bad<CameraMediaDto>("دوربین در دسترس نیست: " + NcLinkText.Network(ex));
            }
        }
    }
}
