using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductBankDtos;

namespace Services.NasbNiro.ProductBank
{
    public interface IDesignPlanService
    {
        Task<ResponseDto<PagedList<DesignPlanListDto>>> GetListAsync(DesignPlanQuery query, CancellationToken ct);
        Task<ResponseDto<DesignPlanDto>> GetAsync(int id, CancellationToken ct);
        Task<ResponseDto<DesignPlanDto>> SaveAsync(SaveDesignPlanDto dto, CancellationToken ct);
        Task<ResponseDto<DesignPlanDto>> ActionAsync(DesignPlanActionDto dto, CancellationToken ct);
        Task<ResponseDto<DesignPlanDto>> StageActionAsync(DesignStageActionDto dto, CancellationToken ct);
        Task<ResponseDto<DesignDossierDto>> DossierAsync(int productId, CancellationToken ct);
        Task<ResponseDto<DesignGateStatusDto>> GateStatusAsync(string kind, int id, CancellationToken ct);
    }

    /// <summary>
    /// Package A1 (ISO 9001 8.3.2): the design plan of a product revision. Engineering prepares it (stages, responsibilities, interfaces, planned dates), the engineering manager or QA approves it
    /// (never the preparer, never an AI seat); a new version supersedes the old one. The stages follow the evidence: inputs complete, reviews closed Accepted, BOM released.
    /// </summary>
    public class DesignPlanService : PbServiceBase, IDesignPlanService
    {
        private readonly INotificationService _notify;

        public DesignPlanService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify) : base(uow, user)
        {
            _notify = notify;
        }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private int Me => User.UserId;

        // ------------------------------------------------------------------------------------------ reading

        public async Task<ResponseDto<PagedList<DesignPlanListDto>>> GetListAsync(DesignPlanQuery query, CancellationToken ct)
        {
            query ??= new DesignPlanQuery();
            IQueryable<PbDesignPlan> q = Uow.Set<PbDesignPlan>().AsNoTracking().Where(p => !p.Deleted);
            if (query.ProductId.HasValue) q = q.Where(p => p.ProductId == query.ProductId.Value);
            if (query.ProductRevision.HasValue) q = q.Where(p => p.ProductRevision == query.ProductRevision.Value);
            if (query.Status.HasValue) q = q.Where(p => p.Status == query.Status.Value);
            string text = PbText.Clean(query.Contains);
            if (text != null) q = q.Where(p => p.Number.Contains(text) || p.Title.Contains(text) || p.Product.Code.Contains(text) || p.Product.Title.Contains(text));
            IQueryable<DesignPlanListDto> projected = q.OrderByDescending(p => p.Id).Select(p => new DesignPlanListDto
            {
                Id = p.Id, ProductId = p.ProductId, ProductCode = p.Product.Code, ProductTitle = p.Product.Title, ProductRevision = p.ProductRevision, Version = p.Version, Number = p.Number, Title = p.Title,
                Status = p.Status, ValidationRequired = p.ValidationRequired, CreateDate = p.CreateDate, ApprovedAt = p.ApprovedAt,
            });
            PagedList<DesignPlanListDto> page = await PagedList<DesignPlanListDto>.CreateAsync(projected, query.PageNumber, query.PageSize, ct);
            foreach (DesignPlanListDto d in page) d.ProductRevisionText = DesignRules.Rev(d.ProductRevision);
            return ResponseDtoHelper.Ok(page);
        }

        private Task<PbDesignPlan> LoadAsync(int id, bool tracked, CancellationToken ct)
        {
            IQueryable<PbDesignPlan> q = Uow.Set<PbDesignPlan>().Where(p => p.Id == id && !p.Deleted).Include(p => p.Stages).Include(p => p.Interfaces).Include(p => p.Product);
            return (tracked ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct);
        }

        private async Task<DesignPlanDto> ToDtoAsync(PbDesignPlan p, CancellationToken ct)
        {
            Dictionary<int, string> names = await DesignQueries.NamesAsync(Uow, new int?[] { p.PreparedByUserId, p.ApprovedByUserId, p.InputsCompleteByUserId }
                .Concat(p.Stages.Select(s => s.ResponsibleUserId)).Concat(p.Interfaces.Select(i => i.ResponsibleUserId)), ct);
            List<int> na = DesignRules.ParseKinds(p.NotApplicableKinds);
            List<PbDesignInput> live = await DesignQueries.LiveInputsAsync(Uow, p.ProductId, p.ProductRevision, false, ct);
            var dto = new DesignPlanDto
            {
                Id = p.Id, ProductId = p.ProductId, ProductCode = p.Product?.Code, ProductTitle = p.Product?.Title, ProductRevision = p.ProductRevision, ProductRevisionText = DesignRules.Rev(p.ProductRevision), Version = p.Version,
                Number = p.Number, Title = p.Title, Status = p.Status, ValidationRequired = p.ValidationRequired, CreateDate = p.CreateDate, ApprovedAt = p.ApprovedAt, Scope = p.Scope, ValidationWaiverReason = p.ValidationWaiverReason,
                PreparedByUserId = p.PreparedByUserId, PreparedByName = names.Name(p.PreparedByUserId), SubmittedAt = p.SubmittedAt, ApprovedByUserId = p.ApprovedByUserId, ApprovedByName = names.Name(p.ApprovedByUserId), ReturnNote = p.ReturnNote,
                InputsCompleteByUserId = p.InputsCompleteByUserId, InputsCompleteByName = names.Name(p.InputsCompleteByUserId), InputsCompleteAt = p.InputsCompleteAt, InputsCompleteNote = p.InputsCompleteNote, NotApplicableKinds = na,
                SupersedesPlanId = p.SupersedesPlanId, VoidReason = p.VoidReason, ContentHash = p.ContentHash, RowVersion = DesignQueries.B64(p.RowVersion),
                ConflictCount = live.Count(i => i.Status == DesignInputStatus.Conflict),
            };
            DateTime today = DateTime.Today;
            foreach (PbDesignStage s in p.Stages.Where(x => !x.Deleted).OrderBy(x => x.Seq))
                dto.Stages.Add(new DesignStageDto
                {
                    Id = s.Id, Seq = s.Seq, Kind = s.Kind, KindText = DesignRules.StageText(s.Kind), Title = s.Title, ResponsibleRole = s.ResponsibleRole, ResponsibleUserId = s.ResponsibleUserId, ResponsibleName = names.Name(s.ResponsibleUserId),
                    PlannedStart = s.PlannedStart, PlannedEnd = s.PlannedEnd, Status = s.Status, StatusText = DesignRules.StageStatusText(s.Status), ActualEnd = s.ActualEnd, Note = s.Note,
                    Late = s.PlannedEnd.HasValue && s.PlannedEnd.Value.Date < today && s.Status != DesignStageStatus.Done && s.Status != DesignStageStatus.NotRequired && p.Status == DesignPlanStatus.Approved,
                });
            foreach (PbDesignInterface i in p.Interfaces.Where(x => !x.Deleted).OrderBy(x => x.Id))
                dto.Interfaces.Add(new DesignInterfaceDto { Id = i.Id, FromGroup = i.FromGroup, ToGroup = i.ToGroup, Subject = i.Subject, ResponsibleRole = i.ResponsibleRole, ResponsibleUserId = i.ResponsibleUserId, ResponsibleName = names.Name(i.ResponsibleUserId) });
            dto.Versions = await Uow.Set<PbDesignPlan>().AsNoTracking().Where(x => !x.Deleted && x.ProductId == p.ProductId && x.ProductRevision == p.ProductRevision).OrderBy(x => x.Version)
                .Select(x => new DesignPlanVersionDto { Id = x.Id, Version = x.Version, Number = x.Number, Status = x.Status, CreateDate = x.CreateDate }).ToListAsync(ct);
            foreach (DesignInputKind k in DesignRules.AllKinds)
                dto.Kinds.Add(new DesignKindCountDto { Kind = k, KindText = DesignRules.KindText(k), Total = live.Count(i => i.Kind == k), Conflicts = live.Count(i => i.Kind == k && i.Status == DesignInputStatus.Conflict), NotApplicable = na.Contains((int)k) });
            dto.MissingKinds = DesignRules.MissingKinds(live, na).Select(DesignRules.KindText).ToList();
            bool human = !IsAi;
            dto.CanEdit = human && p.Status == DesignPlanStatus.Draft && p.PreparedByUserId == Me && InRole(DesignRoles.PlanMakers);
            dto.CanSubmit = dto.CanEdit;
            dto.CanApprove = human && p.Status == DesignPlanStatus.Submitted && p.PreparedByUserId != Me && InRole(p.ValidationRequired ? DesignRoles.PlanApprovers : DesignRoles.QaOnly);
            dto.CanDeclareInputsComplete = human && p.Status == DesignPlanStatus.Approved && p.PreparedByUserId != Me && InRole(DesignRoles.Resolvers);
            dto.CanNewVersion = human && p.Status == DesignPlanStatus.Approved && InRole(DesignRoles.PlanMakers) && !await Uow.Set<PbDesignPlan>().AnyAsync(x => !x.Deleted && x.ProductId == p.ProductId && x.ProductRevision == p.ProductRevision && (x.Status == DesignPlanStatus.Draft || x.Status == DesignPlanStatus.Submitted), ct);
            return dto;
        }

        public async Task<ResponseDto<DesignPlanDto>> GetAsync(int id, CancellationToken ct)
        {
            PbDesignPlan p = await LoadAsync(id, false, ct);
            if (p == null) return ResponseDtoHelper.NotFound<DesignPlanDto>(PbMessages.NotFound);
            return ResponseDtoHelper.Ok(await ToDtoAsync(p, ct));
        }

        // ------------------------------------------------------------------------------------------ save

        private static string DatesProblem(DateTime? start, DateTime? end, string where) =>
            start.HasValue && end.HasValue && end.Value.Date < start.Value.Date ? where + "تاریخ پایان برنامه‌ای از تاریخ شروع زودتر است." : null;

        public async Task<ResponseDto<DesignPlanDto>> SaveAsync(SaveDesignPlanDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<DesignPlanDto>(DesignRules.AiNever);
            if (!InRole(DesignRoles.PlanMakers)) return Bad<DesignPlanDto>(DesignRules.NotAllowed);
            if (dto == null) return Bad<DesignPlanDto>(PbMessages.InvalidData);
            if (dto.ProductRevision < 0 || dto.ProductRevision > 99) return Bad<DesignPlanDto>("بازنگری محصول بین ۰ و ۹۹ است.");
            Dictionary<string, decimal> st = await DesignGate.SettingsAsync(Uow, ct);
            int reasonMin = (int)st[DesignSettingKeys.ReasonMinChars];
            string waiver = PbText.Clean(dto.ValidationWaiverReason);
            if (!dto.ValidationRequired && (waiver == null || waiver.Length < reasonMin)) return Bad<DesignPlanDto>($"برای معاف کردن محصول از صحه‌گذاری طراحی دلیل دست‌کم {reasonMin} نویسه بنویسید (QA تایید می‌کند).");
            if (waiver != null && waiver.Length > 1000) return Bad<DesignPlanDto>("دلیل معافیت بیش از ۱۰۰۰ نویسه است.");
            string scope = PbText.Clean(dto.Scope);
            if (scope != null && scope.Length > 2000) return Bad<DesignPlanDto>("دامنه برنامه بیش از ۲۰۰۰ نویسه است.");

            List<SaveDesignStageDto> stages = (dto.Stages != null && dto.Stages.Count > 0) ? dto.Stages : DesignRules.DefaultStages().Select(s => new SaveDesignStageDto { Kind = s.Kind, ResponsibleRole = s.Role }).ToList();
            if (stages.Select(s => s.Kind).Distinct().Count() != stages.Count || DesignRules.DefaultStages().Any(d => stages.All(s => s.Kind != d.Kind)) || stages.Count != 6)
                return Bad<DesignPlanDto>("برنامه طراحی هر شش مرحله را دارد، هر کدام یک بار: ورودی‌ها، طراحی، بازبینی، تایید، صحه‌گذاری و انتشار.");
            foreach (SaveDesignStageDto s in stages)
            {
                string d = DatesProblem(s.PlannedStart, s.PlannedEnd, DesignRules.StageText(s.Kind) + ": ");
                if (d != null) return Bad<DesignPlanDto>(d);
                if (PbText.Clean(s.ResponsibleRole) != null && PbText.Clean(s.ResponsibleRole).Length > 40) return Bad<DesignPlanDto>("نقش مسئول بیش از ۴۰ نویسه است.");
                if (PbText.Clean(s.Note) != null && PbText.Clean(s.Note).Length > 1000) return Bad<DesignPlanDto>("یادداشت مرحله بیش از ۱۰۰۰ نویسه است.");
            }
            List<SaveDesignInterfaceDto> interfaces = dto.Interfaces ?? new List<SaveDesignInterfaceDto>();
            if (interfaces.Count > 50) return Bad<DesignPlanDto>("حداکثر ۵۰ ارتباط بین گروه‌ها.");
            foreach (SaveDesignInterfaceDto i in interfaces)
            {
                if (PbText.Clean(i.FromGroup) == null || PbText.Clean(i.ToGroup) == null || PbText.Clean(i.Subject) == null) return Bad<DesignPlanDto>("برای هر ارتباط بین گروه‌ها، گروه مبدا، گروه مقصد و موضوع را بنویسید.");
                if (i.FromGroup.Trim().Length > 100 || i.ToGroup.Trim().Length > 100 || i.Subject.Trim().Length > 500) return Bad<DesignPlanDto>("متن یکی از ارتباط‌ها بیش از اندازه مجاز است.");
            }
            string person = await DesignQueries.PersonProblemAsync(Uow, stages.Where(s => s.ResponsibleUserId.HasValue).Select(s => s.ResponsibleUserId.Value).Concat(interfaces.Where(i => i.ResponsibleUserId.HasValue).Select(i => i.ResponsibleUserId.Value)), ct);
            if (person != null) return Bad<DesignPlanDto>(person);

            DateTimeOffset now = DateTimeOffset.Now;
            PbDesignPlan p;
            if (dto.Id.HasValue)
            {
                p = await LoadAsync(dto.Id.Value, true, ct);
                if (p == null) return ResponseDtoHelper.NotFound<DesignPlanDto>(PbMessages.NotFound);
                if (p.Status != DesignPlanStatus.Draft) return Bad<DesignPlanDto>("فقط برنامه پیش‌نویس ویرایش می‌شود؛ برای تغییر «نسخه جدید» بسازید.");
                if (p.PreparedByUserId != Me) return Bad<DesignPlanDto>("پیش‌نویس را فقط تهیه‌کننده آن ویرایش می‌کند.");
                if (p.ProductId != dto.ProductId || p.ProductRevision != dto.ProductRevision) return Bad<DesignPlanDto>("محصول و بازنگری یک برنامه تغییر نمی‌کند.");
                if (!string.IsNullOrEmpty(dto.RowVersion))
                    try { Uow.Entry(p).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<DesignPlanDto>(PbMessages.InvalidData); }
            }
            else
            {
                PbProduct product = await Uow.Set<PbProduct>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.ProductId && !x.Deleted, ct);
                if (product == null) return Bad<DesignPlanDto>("محصول پیدا نشد.");
                if (await Uow.Set<PbDesignPlan>().AnyAsync(x => !x.Deleted && x.ProductId == dto.ProductId && x.ProductRevision == dto.ProductRevision && x.Status != DesignPlanStatus.Void && x.Status != DesignPlanStatus.Superseded, ct))
                    return Bad<DesignPlanDto>("برای این محصول و بازنگری برنامه طراحی در جریان یا تاییدشده هست؛ از «نسخه جدید» همان برنامه استفاده کنید.");
                int version = (await Uow.Set<PbDesignPlan>().Where(x => x.ProductId == dto.ProductId && x.ProductRevision == dto.ProductRevision).Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1;
                p = new PbDesignPlan
                {
                    ProductId = dto.ProductId, ProductRevision = dto.ProductRevision, Version = version, Status = DesignPlanStatus.Draft, PreparedByUserId = Me,
                    Number = await InventoryNumbers.NextNumberAsync(Uow, DesignNumbers.Plan, "DP", now.Date, ct),
                };
                Stamp(p, true);
                Uow.Set<PbDesignPlan>().Add(p);
                p.Product = product;
            }
            p.Title = PbText.Clean(dto.Title) ?? $"برنامه طراحی {p.Product?.Code} بازنگری {DesignRules.Rev(dto.ProductRevision)}";
            if (p.Title.Length > 300) return Bad<DesignPlanDto>("عنوان بیش از ۳۰۰ نویسه است.");
            p.Scope = scope;
            p.ValidationRequired = dto.ValidationRequired;
            p.ValidationWaiverReason = dto.ValidationRequired ? null : waiver;
            Stamp(p, false);
            ReconcileStages(p, stages, now);
            ReconcileInterfaces(p, interfaces, now);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<DesignPlanDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(await LoadAsync(p.Id, false, ct), ct));
        }

        private void ReconcileStages(PbDesignPlan p, List<SaveDesignStageDto> input, DateTimeOffset now)
        {
            int seq = 0;
            foreach (DesignStageKind kind in DesignRules.DefaultStages().Select(d => d.Kind))
            {
                SaveDesignStageDto s = input.First(x => x.Kind == kind);
                seq++;
                PbDesignStage row = p.Stages.FirstOrDefault(x => !x.Deleted && x.Kind == kind);
                if (row == null)
                {
                    row = new PbDesignStage { Kind = kind, Seq = seq, Title = DesignRules.StageText(kind), Status = DesignStageStatus.NotStarted };
                    Stamp(row, true);
                    p.Stages.Add(row);
                }
                row.Seq = seq;
                row.ResponsibleRole = PbText.Clean(s.ResponsibleRole) ?? DesignRules.DefaultStages().First(d => d.Kind == kind).Role;
                row.ResponsibleUserId = s.ResponsibleUserId;
                row.PlannedStart = s.PlannedStart?.Date;
                row.PlannedEnd = s.PlannedEnd?.Date;
                row.Note = PbText.Clean(s.Note);
                Stamp(row, false);
            }
        }

        private void ReconcileInterfaces(PbDesignPlan p, List<SaveDesignInterfaceDto> input, DateTimeOffset now)
        {
            var keep = new HashSet<int>();
            foreach (SaveDesignInterfaceDto s in input)
            {
                PbDesignInterface row = s.Id.HasValue ? p.Interfaces.FirstOrDefault(x => !x.Deleted && x.Id == s.Id.Value) : null;
                if (row == null)
                {
                    row = new PbDesignInterface();
                    Stamp(row, true);
                    p.Interfaces.Add(row);
                }
                else keep.Add(row.Id);
                row.FromGroup = s.FromGroup.Trim(); row.ToGroup = s.ToGroup.Trim(); row.Subject = s.Subject.Trim();
                row.ResponsibleRole = PbText.Clean(s.ResponsibleRole); row.ResponsibleUserId = s.ResponsibleUserId;
                Stamp(row, false);
            }
            foreach (PbDesignInterface old in p.Interfaces.Where(x => !x.Deleted && x.Id != 0 && !keep.Contains(x.Id)).ToList())
            {
                old.Delete();
                Stamp(old, false);
            }
        }

        // ------------------------------------------------------------------------------------------ actions

        public async Task<ResponseDto<DesignPlanDto>> ActionAsync(DesignPlanActionDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<DesignPlanDto>(DesignRules.AiNever);
            if (dto == null || PbText.Clean(dto.Action) == null) return Bad<DesignPlanDto>(PbMessages.InvalidData);
            PbDesignPlan p = await LoadAsync(dto.Id, true, ct);
            if (p == null) return ResponseDtoHelper.NotFound<DesignPlanDto>(PbMessages.NotFound);
            Dictionary<string, decimal> st = await DesignGate.SettingsAsync(Uow, ct);
            int reasonMin = (int)st[DesignSettingKeys.ReasonMinChars];
            string note = PbText.Clean(dto.Note);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "submit": return await SubmitAsync(p, now, ct);
                case "return":
                    if (p.Status != DesignPlanStatus.Submitted) return Bad<DesignPlanDto>("فقط برنامه ارسال‌شده برگردانده می‌شود.");
                    if (!InRole(DesignRoles.PlanApprovers)) return Bad<DesignPlanDto>(DesignRules.NotAllowed);
                    if (p.PreparedByUserId == Me) return Bad<DesignPlanDto>(DesignRules.SamePerson);
                    if (note == null || note.Length < 5) return Bad<DesignPlanDto>("دلیل برگرداندن را بنویسید.");
                    p.Status = DesignPlanStatus.Draft; p.ReturnNote = note; p.SubmittedAt = null; p.ContentHash = null;
                    Stamp(p, false);
                    return await SaveAndReturnAsync(p, ct);
                case "approve": return await ApproveAsync(p, now, ct);
                case "new-version": return await NewVersionAsync(p, now, ct);
                case "void":
                    if (p.Status != DesignPlanStatus.Draft && p.Status != DesignPlanStatus.Submitted) return Bad<DesignPlanDto>("فقط برنامه پیش‌نویس یا ارسال‌شده باطل می‌شود؛ برنامه تاییدشده را نسخه جدید جایگزین می‌کند.");
                    if (!(p.PreparedByUserId == Me || InRole(DesignRoles.PlanApprovers))) return Bad<DesignPlanDto>(DesignRules.NotAllowed);
                    if (note == null || note.Length < reasonMin) return Bad<DesignPlanDto>($"دلیل ابطال دست‌کم {reasonMin} نویسه باشد.");
                    p.Status = DesignPlanStatus.Void; p.VoidReason = note;
                    Stamp(p, false);
                    return await SaveAndReturnAsync(p, ct);
                case "inputs-complete": return await InputsCompleteAsync(p, dto, now, reasonMin, ct);
                default: return Bad<DesignPlanDto>("کار نامعتبر است.");
            }
        }

        private async Task<ResponseDto<DesignPlanDto>> SaveAndReturnAsync(PbDesignPlan p, CancellationToken ct)
        {
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<DesignPlanDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(await LoadAsync(p.Id, false, ct), ct));
        }

        private async Task<ResponseDto<DesignPlanDto>> SubmitAsync(PbDesignPlan p, DateTimeOffset now, CancellationToken ct)
        {
            if (p.Status != DesignPlanStatus.Draft) return Bad<DesignPlanDto>("فقط پیش‌نویس ارسال می‌شود.");
            if (p.PreparedByUserId != Me || !InRole(DesignRoles.PlanMakers)) return Bad<DesignPlanDto>("پیش‌نویس را فقط تهیه‌کننده ارسال می‌کند.");
            var problems = new List<string>();
            foreach (PbDesignStage s in p.Stages.Where(x => !x.Deleted).OrderBy(x => x.Seq))
            {
                string name = DesignRules.StageText(s.Kind);
                if (s.Kind == DesignStageKind.Validation && !p.ValidationRequired) continue;
                if (string.IsNullOrWhiteSpace(s.ResponsibleRole) || !s.ResponsibleUserId.HasValue) problems.Add($"{name}: مسئول (نقش و فرد) مشخص نشده است");
                if (!s.PlannedStart.HasValue || !s.PlannedEnd.HasValue) problems.Add($"{name}: تاریخ شروع و پایان برنامه‌ای مشخص نشده است");
            }
            if (problems.Count > 0) return Bad<DesignPlanDto>("برنامه کامل نیست: " + string.Join("؛ ", problems) + ".");
            p.ContentHash = DesignRules.PlanHash(p, p.Stages, p.Interfaces);
            p.Status = DesignPlanStatus.Submitted; p.SubmittedAt = now; p.ReturnNote = null;
            Stamp(p, false);
            ResponseDto<DesignPlanDto> r = await SaveAndReturnAsync(p, ct);
            if (r.Success) await _notify.NotifyAsync(new NotifyRequest { SourceKey = "design-plan:" + p.Id, Module = "productbank", SourceType = "design-plan", SourceId = p.Id, Title = $"برنامه طراحی {p.Number} برای تایید", Link = "/product-bank/design/plans/" + p.Id, Roles = "ENGINEERING,QA" }, ct);
            return r;
        }

        private async Task<ResponseDto<DesignPlanDto>> ApproveAsync(PbDesignPlan p, DateTimeOffset now, CancellationToken ct)
        {
            if (p.Status != DesignPlanStatus.Submitted) return Bad<DesignPlanDto>("فقط برنامه ارسال‌شده تایید می‌شود.");
            if (!InRole(p.ValidationRequired ? DesignRoles.PlanApprovers : DesignRoles.QaOnly))
                return Bad<DesignPlanDto>(p.ValidationRequired ? DesignRules.NotAllowed : "برنامه‌ای که صحه‌گذاری طراحی را معاف می‌کند فقط QA تایید می‌کند.");
            if (p.PreparedByUserId == Me) return Bad<DesignPlanDto>(DesignRules.SamePerson);
            if (DesignRules.PlanHash(p, p.Stages, p.Interfaces) != p.ContentHash) return Bad<DesignPlanDto>("محتوای برنامه بعد از ارسال تغییر کرده است؛ برگردانده شود و دوباره ارسال شود.");
            string conflicts = await DesignQueries.ConflictTextAsync(Uow, p.ProductId, p.ProductRevision, ct);
            if (conflicts != null) return Bad<DesignPlanDto>(conflicts);
            return await InTransactionAsync(async () =>
            {
                PbDesignPlan plan = await LoadAsync(p.Id, true, ct);
                if (plan == null || plan.Status != DesignPlanStatus.Submitted) return Bad<DesignPlanDto>(DesignRules.Concurrent);
                List<PbDesignPlan> old = await Uow.Set<PbDesignPlan>().Where(x => !x.Deleted && x.ProductId == plan.ProductId && x.ProductRevision == plan.ProductRevision && x.Status == DesignPlanStatus.Approved && x.Id != plan.Id).ToListAsync(ct);
                foreach (PbDesignPlan o in old) { o.Status = DesignPlanStatus.Superseded; o.SupersededAt = now; Stamp(o, false); }
                if (old.Count > 0) { string c1 = await TrySaveAsync(ct); if (c1 != null) return Bad<DesignPlanDto>(c1); }
                plan.Status = DesignPlanStatus.Approved; plan.ApprovedByUserId = Me; plan.ApprovedAt = now;
                Stamp(plan, false);
                string c2 = await TrySaveAsync(ct);
                if (c2 != null) return Bad<DesignPlanDto>(c2);
                return ResponseDtoHelper.Ok(await ToDtoAsync(await LoadAsync(plan.Id, false, ct), ct));
            }, r => r.Success, ct);
        }

        private async Task<ResponseDto<DesignPlanDto>> NewVersionAsync(PbDesignPlan from, DateTimeOffset now, CancellationToken ct)
        {
            if (!InRole(DesignRoles.PlanMakers)) return Bad<DesignPlanDto>(DesignRules.NotAllowed);
            if (from.Status != DesignPlanStatus.Approved) return Bad<DesignPlanDto>("نسخه جدید از برنامه تاییدشده ساخته می‌شود.");
            if (await Uow.Set<PbDesignPlan>().AnyAsync(x => !x.Deleted && x.ProductId == from.ProductId && x.ProductRevision == from.ProductRevision && (x.Status == DesignPlanStatus.Draft || x.Status == DesignPlanStatus.Submitted), ct))
                return Bad<DesignPlanDto>("نسخه دیگری از این برنامه در جریان است.");
            int version = (await Uow.Set<PbDesignPlan>().Where(x => x.ProductId == from.ProductId && x.ProductRevision == from.ProductRevision).Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1;
            var n = new PbDesignPlan
            {
                ProductId = from.ProductId, ProductRevision = from.ProductRevision, Version = version, Title = from.Title, Scope = from.Scope, ValidationRequired = from.ValidationRequired, ValidationWaiverReason = from.ValidationWaiverReason,
                Status = DesignPlanStatus.Draft, PreparedByUserId = Me, SupersedesPlanId = from.Id, NotApplicableKinds = from.NotApplicableKinds,
                Number = await InventoryNumbers.NextNumberAsync(Uow, DesignNumbers.Plan, "DP", now.Date, ct),
            };
            Stamp(n, true);
            foreach (PbDesignStage s in from.Stages.Where(x => !x.Deleted).OrderBy(x => x.Seq))
            {
                var c = new PbDesignStage { Seq = s.Seq, Kind = s.Kind, Title = s.Title, ResponsibleRole = s.ResponsibleRole, ResponsibleUserId = s.ResponsibleUserId, PlannedStart = s.PlannedStart, PlannedEnd = s.PlannedEnd, Status = s.Status, ActualEnd = s.ActualEnd, CompletedByUserId = s.CompletedByUserId, Note = s.Note };
                Stamp(c, true);
                n.Stages.Add(c);
            }
            foreach (PbDesignInterface i in from.Interfaces.Where(x => !x.Deleted))
            {
                var c = new PbDesignInterface { FromGroup = i.FromGroup, ToGroup = i.ToGroup, Subject = i.Subject, ResponsibleRole = i.ResponsibleRole, ResponsibleUserId = i.ResponsibleUserId };
                Stamp(c, true);
                n.Interfaces.Add(c);
            }
            Uow.Set<PbDesignPlan>().Add(n);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<DesignPlanDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(await LoadAsync(n.Id, false, ct), ct));
        }

        private async Task<ResponseDto<DesignPlanDto>> InputsCompleteAsync(PbDesignPlan p, DesignPlanActionDto dto, DateTimeOffset now, int reasonMin, CancellationToken ct)
        {
            if (p.Status != DesignPlanStatus.Approved) return Bad<DesignPlanDto>("کامل بودن ورودی‌ها برای برنامه تاییدشده اعلام می‌شود.");
            if (!InRole(DesignRoles.Resolvers)) return Bad<DesignPlanDto>(DesignRules.NotAllowed);
            if (p.PreparedByUserId == Me) return Bad<DesignPlanDto>("اعلام کامل بودن ورودی‌ها را نفر دوم، غیر از تهیه‌کننده برنامه، انجام می‌دهد.");
            string note = PbText.Clean(dto.Note);
            if (note == null || note.Length < 5) return Bad<DesignPlanDto>("توضیح بررسی کامل بودن ورودی‌ها را بنویسید (چه منابعی بررسی شد).");
            if (note.Length > 1000) return Bad<DesignPlanDto>("توضیح بیش از ۱۰۰۰ نویسه است.");
            List<int> na = (dto.NotApplicableKinds ?? new List<int>()).Distinct().ToList();
            if (na.Any(k => !Enum.IsDefined(typeof(DesignInputKind), k))) return Bad<DesignPlanDto>("نوع ورودی معتبر نیست.");
            if (na.Count > 0 && note.Length < reasonMin) return Bad<DesignPlanDto>($"برای انواعی که «لازم نیست» اعلام می‌شوند، توضیح (دلیل) دست‌کم {reasonMin} نویسه باشد.");
            string conflicts = await DesignQueries.ConflictTextAsync(Uow, p.ProductId, p.ProductRevision, ct);
            if (conflicts != null) return Bad<DesignPlanDto>(conflicts);
            List<PbDesignInput> live = await DesignQueries.LiveInputsAsync(Uow, p.ProductId, p.ProductRevision, false, ct);
            if (live.Count == 0) return Bad<DesignPlanDto>("هنوز هیچ ورودی طراحی ثبت نشده است.");
            List<DesignInputKind> missing = DesignRules.MissingKinds(live, na);
            if (missing.Count > 0) return Bad<DesignPlanDto>("برای این انواع ورودی نه ورودی ثبت شده و نه «لازم نیست» اعلام شده است: " + string.Join("، ", missing.Select(DesignRules.KindText)) + ".");
            p.InputsCompleteByUserId = Me; p.InputsCompleteAt = now.AddMilliseconds(1); p.InputsCompleteNote = note; p.NotApplicableKinds = DesignRules.Join(na);
            Stamp(p, false);
            return await SaveAndReturnAsync(p, ct);
        }

        // ------------------------------------------------------------------------------------------ stages

        public async Task<ResponseDto<DesignPlanDto>> StageActionAsync(DesignStageActionDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<DesignPlanDto>(DesignRules.AiNever);
            if (dto == null || PbText.Clean(dto.Action) == null) return Bad<DesignPlanDto>(PbMessages.InvalidData);
            PbDesignPlan p = await LoadAsync(dto.PlanId, true, ct);
            if (p == null) return ResponseDtoHelper.NotFound<DesignPlanDto>(PbMessages.NotFound);
            if (p.Status != DesignPlanStatus.Approved) return Bad<DesignPlanDto>("مراحل فقط برای برنامه تاییدشده به‌روز می‌شود.");
            PbDesignStage s = p.Stages.FirstOrDefault(x => x.Id == dto.StageId && !x.Deleted);
            if (s == null) return ResponseDtoHelper.NotFound<DesignPlanDto>("مرحله پیدا نشد.");
            if (!(InRole(DesignRoles.ReviewMakers) || s.ResponsibleUserId == Me)) return Bad<DesignPlanDto>(DesignRules.NotAllowed);
            string action = dto.Action.Trim().ToLowerInvariant();
            string note = PbText.Clean(dto.Note);
            List<PbDesignStage> ordered = p.Stages.Where(x => !x.Deleted).OrderBy(x => x.Seq).ToList();
            PbDesignStage before = ordered.LastOrDefault(x => x.Seq < s.Seq);
            DateTimeOffset now = DateTimeOffset.Now;
            if (action == "not-required")
            {
                if (s.Kind != DesignStageKind.Validation || p.ValidationRequired) return Bad<DesignPlanDto>("فقط مرحله صحه‌گذاری، آن هم در برنامه‌ای که صحه‌گذاری را معاف کرده، «لازم نیست» می‌شود.");
                s.Status = DesignStageStatus.NotRequired; s.CompletedByUserId = Me; s.ActualEnd = now.Date; if (note != null) s.Note = note;
            }
            else if (action == "start" || action == "complete")
            {
                if (s.Status == DesignStageStatus.Done || s.Status == DesignStageStatus.NotRequired) return Bad<DesignPlanDto>("این مرحله پایان یافته است.");
                if (before != null && before.Status != DesignStageStatus.Done && before.Status != DesignStageStatus.NotRequired) return Bad<DesignPlanDto>($"مرحله پیش از آن «{DesignRules.StageText(before.Kind)}» هنوز پایان نیافته است.");
                if (s.Kind != DesignStageKind.Inputs)
                {
                    string conflicts = await DesignQueries.ConflictTextAsync(Uow, p.ProductId, p.ProductRevision, ct);
                    if (conflicts != null) return Bad<DesignPlanDto>(conflicts);
                }
                if (action == "start")
                {
                    if (s.Status != DesignStageStatus.NotStarted) return Bad<DesignPlanDto>("این مرحله قبلاً شروع شده است.");
                    if (s.Kind == DesignStageKind.Design)
                    {
                        string incomplete = await DesignQueries.InputsIncompleteReasonAsync(Uow, p, ct);
                        if (incomplete != null) return Bad<DesignPlanDto>("طراحی شروع نمی‌شود: " + incomplete);
                    }
                    s.Status = DesignStageStatus.InProgress;
                }
                else
                {
                    string problem = await CompleteProblemAsync(p, s, ct);
                    if (problem != null) return Bad<DesignPlanDto>(problem);
                    s.Status = DesignStageStatus.Done; s.ActualEnd = now.Date; s.CompletedByUserId = Me;
                    if (note != null) s.Note = note;
                }
            }
            else return Bad<DesignPlanDto>("کار نامعتبر است.");
            Stamp(s, false);
            Stamp(p, false);
            return await SaveAndReturnAsync(p, ct);
        }

        private async Task<string> CompleteProblemAsync(PbDesignPlan p, PbDesignStage s, CancellationToken ct)
        {
            async Task<bool> Closed(DesignReviewType t) => await Uow.Set<PbDesignReview>().AnyAsync(r => !r.Deleted && r.ProductId == p.ProductId && r.ProductRevision == p.ProductRevision && r.Type == t && r.Status == DesignReviewStatus.Closed && r.Outcome == DesignReviewOutcome.Accepted, ct);
            switch (s.Kind)
            {
                case DesignStageKind.Inputs:
                    string incomplete = await DesignQueries.InputsIncompleteReasonAsync(Uow, p, ct);
                    return incomplete == null ? null : "مرحله ورودی‌ها پایان نمی‌یابد: " + incomplete;
                case DesignStageKind.DesignReview: return await Closed(DesignReviewType.Review) ? null : "بازبینی طراحی بسته‌شده با نتیجه «پذیرفته» ثبت نشده است.";
                case DesignStageKind.Verification: return await Closed(DesignReviewType.Verification) ? null : "تایید طراحی (Verification) بسته‌شده با نتیجه «پذیرفته» ثبت نشده است.";
                case DesignStageKind.Validation: return await Closed(DesignReviewType.Validation) ? null : "صحه‌گذاری طراحی (Validation) بسته‌شده با نتیجه «پذیرفته» ثبت نشده است.";
                case DesignStageKind.Release:
                    return await Uow.Set<PbBomHeader>().AnyAsync(h => !h.Deleted && h.ProductId == p.ProductId && h.Revision == p.ProductRevision && h.Status == PbBomStatus.Released, ct) ? null
                        : $"BOM منتشرشده بازنگری {DesignRules.Rev(p.ProductRevision)} برای این محصول وجود ندارد.";
                default: return null;
            }
        }

        // ------------------------------------------------------------------------------------------ dossier and gate status

        private async Task<DesignGateStatusDto> GateDtoAsync(string subject, DesignCoverage c, bool required, CancellationToken ct)
        {
            var g = new DesignGateStatusDto { Subject = subject, Required = required, Passed = !required || c.Passed };
            if (required && !c.Passed) g.Missing.AddRange(c.Missing);
            Dictionary<int, string> plans = new Dictionary<int, string>();
            foreach (PbDesignReview r in c.Related)
                g.Reviews.Add(new DesignReviewListDto { Id = r.Id, Number = r.Number, ProductId = r.ProductId, ProductRevision = r.ProductRevision, PlanId = r.PlanId, Type = r.Type, TypeText = DesignRules.TypeText(r.Type), Title = r.Title, Status = r.Status, Outcome = r.Outcome, ScheduledDate = r.ScheduledDate, HeldDate = r.HeldDate, ClosedAt = r.ClosedAt, CreateDate = r.CreateDate });
            await Task.CompletedTask;
            return g;
        }

        public async Task<ResponseDto<DesignGateStatusDto>> GateStatusAsync(string kind, int id, CancellationToken ct)
        {
            bool required = await DesignGate.VerificationRequiredAsync(Uow, ct);
            if (string.Equals(kind, "bom", StringComparison.OrdinalIgnoreCase))
            {
                PbBomHeader h = await Uow.Set<PbBomHeader>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
                if (h == null) return ResponseDtoHelper.NotFound<DesignGateStatusDto>(PbMessages.NotFound);
                return ResponseDtoHelper.Ok(await GateDtoAsync($"BOM {h.Code} بازنگری {DesignRules.Rev(h.Revision)}", await DesignGate.BomCoverageAsync(Uow, h, DesignReviewType.Verification, ct), required, ct));
            }
            if (string.Equals(kind, "drawing", StringComparison.OrdinalIgnoreCase))
            {
                var r = await Uow.Set<Entities.Model.NasbNiro.ProjectControl.PcDocumentRevision>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Id, x.Code, x.DeliverableId }).FirstOrDefaultAsync(ct);
                if (r == null) return ResponseDtoHelper.NotFound<DesignGateStatusDto>(PbMessages.NotFound);
                Entities.Model.NasbNiro.ProjectControl.PcEngineeringDeliverable d = await Uow.Set<Entities.Model.NasbNiro.ProjectControl.PcEngineeringDeliverable>().AsNoTracking().FirstAsync(x => x.Id == r.DeliverableId, ct);
                bool output = await DesignGate.IsDesignOutputAsync(Uow, d, ct);
                DesignGateStatusDto g = await GateDtoAsync($"نقشه {d.DocumentNumber} بازنگری {r.Code}", await DesignGate.DrawingCoverageAsync(Uow, r.Id, $"نقشه {d.DocumentNumber} بازنگری {r.Code}", ct), required && output, ct);
                return ResponseDtoHelper.Ok(g);
            }
            return Bad<DesignGateStatusDto>("نوع خروجی «bom» یا «drawing» است.");
        }

        public async Task<ResponseDto<DesignDossierDto>> DossierAsync(int productId, CancellationToken ct)
        {
            PbProduct product = await Uow.Set<PbProduct>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == productId && !x.Deleted, ct);
            if (product == null) return ResponseDtoHelper.NotFound<DesignDossierDto>("محصول پیدا نشد.");
            Dictionary<string, decimal> st = await DesignGate.SettingsAsync(Uow, ct);
            var dossier = new DesignDossierDto { ProductId = product.Id, ProductCode = product.Code, ProductTitle = product.Title, VerificationRequired = st[DesignSettingKeys.VerificationRequired] >= 1, ValidationRequired = st[DesignSettingKeys.ValidationRequired] >= 1 };
            dossier.Plans = await Uow.Set<PbDesignPlan>().AsNoTracking().Where(p => !p.Deleted && p.ProductId == productId).OrderByDescending(p => p.ProductRevision).ThenByDescending(p => p.Version)
                .Select(p => new DesignPlanListDto { Id = p.Id, ProductId = p.ProductId, ProductCode = product.Code, ProductTitle = product.Title, ProductRevision = p.ProductRevision, Version = p.Version, Number = p.Number, Title = p.Title, Status = p.Status, ValidationRequired = p.ValidationRequired, CreateDate = p.CreateDate, ApprovedAt = p.ApprovedAt }).ToListAsync(ct);
            foreach (DesignPlanListDto d in dossier.Plans) d.ProductRevisionText = DesignRules.Rev(d.ProductRevision);
            List<PbDesignInput> inputs = await Uow.Set<PbDesignInput>().AsNoTracking().Where(i => !i.Deleted && i.ProductId == productId && (i.Status == DesignInputStatus.Open || i.Status == DesignInputStatus.Conflict)).OrderByDescending(i => i.ProductRevision).ThenBy(i => i.Code).ToListAsync(ct);
            dossier.Inputs = inputs.Select(i => new DesignInputDto { Id = i.Id, ProductId = i.ProductId, ProductCode = product.Code, ProductRevision = i.ProductRevision, Code = i.Code, Kind = i.Kind, KindText = DesignRules.KindText(i.Kind), Statement = i.Statement, Source = i.Source, AcceptanceCriterion = i.AcceptanceCriterion, Status = i.Status, StatusText = DesignRules.InputStatusText(i.Status) }).ToList();
            dossier.ConflictCount = inputs.Count(i => i.Status == DesignInputStatus.Conflict);
            List<PbDesignReview> reviews = await Uow.Set<PbDesignReview>().AsNoTracking().Include(r => r.Findings).Where(r => !r.Deleted && r.ProductId == productId).OrderByDescending(r => r.Id).ToListAsync(ct);
            dossier.Reviews = reviews.Select(r => new DesignReviewListDto
            {
                Id = r.Id, Number = r.Number, ProductId = r.ProductId, ProductCode = product.Code, ProductRevision = r.ProductRevision, PlanId = r.PlanId, Type = r.Type, TypeText = DesignRules.TypeText(r.Type), Title = r.Title, Status = r.Status, Outcome = r.Outcome,
                ScheduledDate = r.ScheduledDate, HeldDate = r.HeldDate, ClosedAt = r.ClosedAt, CreateDate = r.CreateDate, OpenFindings = r.Findings.Count(f => !f.Deleted && (f.Status == DesignFindingStatus.Open || f.Status == DesignFindingStatus.Done)),
            }).ToList();
            dossier.OpenFindings = dossier.Reviews.Where(r => r.Status != DesignReviewStatus.Void).Sum(r => r.OpenFindings);
            List<PbBomHeader> boms = await Uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.ProductId == productId && h.Status != PbBomStatus.Obsolete).OrderByDescending(h => h.Revision).ToListAsync(ct);
            foreach (PbBomHeader h in boms)
            {
                var row = new DesignDossierBomDto { BomHeaderId = h.Id, Code = h.Code, Revision = h.Revision, RevisionText = DesignRules.Rev(h.Revision), Status = h.Status.ToString() };
                row.Verification = await GateDtoAsync("Verification", await DesignGate.BomCoverageAsync(Uow, h, DesignReviewType.Verification, ct), dossier.VerificationRequired, ct);
                row.Validation = await GateDtoAsync("Validation", await DesignGate.BomCoverageAsync(Uow, h, DesignReviewType.Validation, ct), dossier.ValidationRequired, ct);
                dossier.Boms.Add(row);
            }
            return ResponseDtoHelper.Ok(dossier);
        }
    }
}
