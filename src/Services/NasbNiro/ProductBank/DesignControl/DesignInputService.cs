using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductBankDtos;
using FinanceXlsxSheet = Services.NasbNiro.Finance.XlsxSheet;

namespace Services.NasbNiro.ProductBank
{
    public interface IDesignInputService
    {
        Task<ResponseDto<List<DesignInputDto>>> GetListAsync(DesignInputQuery query, CancellationToken ct);
        Task<ResponseDto<DesignInputDto>> GetAsync(int id, CancellationToken ct);
        Task<ResponseDto<DesignInputDto>> SaveAsync(SaveDesignInputDto dto, CancellationToken ct);
        Task<ResponseDto<DesignInputDto>> ActionAsync(DesignInputActionDto dto, CancellationToken ct);
        Task<ResponseDto<List<DesignInputDto>>> CopyFromRevisionAsync(int productId, int fromRevision, int toRevision, CancellationToken ct);
        ResponseDto<byte[]> Template();
        Task<ResponseDto<DesignInputExcelResultDto>> ImportAsync(DesignInputExcelRequestDto request, byte[] file, CancellationToken ct);
        Task<ResponseDto<byte[]>> ExportAsync(int productId, int revision, CancellationToken ct);
    }

    /// <summary>
    /// Package A1 (ISO 9001 8.3.3): the register of design inputs of a product revision. Every input has a source, an acceptance criterion and (by kind) an edition or a referenced document revision.
    /// An input that a review already refers to is never edited, it is replaced (the old one stays as history). A conflict between inputs stops the design until a second person resolves it.
    /// </summary>
    public class DesignInputService : PbServiceBase, IDesignInputService
    {
        public DesignInputService(IUnitOfWork uow, IInventoryUserContext user) : base(uow, user) { }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private int Me => User.UserId;

        private static readonly string[] Header =
        {
            "نوع ورودی", "الزام", "منبع", "معیار پذیرش", "ویرایش استاندارد", "طراحی قبلی", "مرجع مشخصات مشتری", "بازنگری مرجع", "پیامد خرابی",
        };

        // ------------------------------------------------------------------------------------------ reading

        private async Task<List<DesignInputDto>> ToDtosAsync(List<PbDesignInput> list, CancellationToken ct)
        {
            if (list.Count == 0) return new List<DesignInputDto>();
            Dictionary<int, string> names = await DesignQueries.NamesAsync(Uow, list.Select(i => (int?)i.CreatorUserId).Concat(list.Select(i => i.ConflictMarkedByUserId)).Concat(list.Select(i => i.ConflictResolvedByUserId)), ct);
            List<int> ids = list.Select(i => i.Id).ToList();
            HashSet<int> used = (await Uow.Set<PbDesignReviewInput>().AsNoTracking().Where(ri => !ri.Deleted && ids.Contains(ri.InputId) && !ri.Review.Deleted && ri.Review.Status != DesignReviewStatus.Void).Select(ri => ri.InputId).Distinct().ToListAsync(ct)).ToHashSet();
            List<int> products = list.Select(i => i.ProductId).Distinct().ToList();
            Dictionary<int, string> codes = await Uow.Set<PbProduct>().AsNoTracking().Where(p => products.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
            List<int> withIds = list.Where(i => i.ConflictWithInputId.HasValue).Select(i => i.ConflictWithInputId.Value).Distinct().ToList();
            Dictionary<int, string> withCodes = withIds.Count == 0 ? new Dictionary<int, string>() : await Uow.Set<PbDesignInput>().AsNoTracking().Where(i => withIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, ct);
            var docIds = list.Where(i => i.RefDeliverableId.HasValue).Select(i => i.RefDeliverableId.Value).Distinct().ToList();
            Dictionary<int, string> docs = docIds.Count == 0 ? new Dictionary<int, string>() : await Uow.Set<Entities.Model.NasbNiro.ProjectControl.PcEngineeringDeliverable>().AsNoTracking().Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.DocumentNumber, ct);
            return list.Select(i => new DesignInputDto
            {
                Id = i.Id, ProductId = i.ProductId, ProductCode = codes.TryGetValue(i.ProductId, out string pc) ? pc : null, ProductRevision = i.ProductRevision, Code = i.Code, Kind = i.Kind, KindText = DesignRules.KindText(i.Kind),
                Statement = i.Statement, Source = i.Source, AcceptanceCriterion = i.AcceptanceCriterion, StandardEdition = i.StandardEdition, PreviousDesignRef = i.PreviousDesignRef,
                RefDeliverableId = i.RefDeliverableId, RefRevisionId = i.RefRevisionId, RefDocumentText = i.RefDeliverableId.HasValue && docs.TryGetValue(i.RefDeliverableId.Value, out string dn) ? dn : null,
                RefText = i.RefText, RefRevisionText = i.RefRevisionText, FailureConsequence = i.FailureConsequence, Status = i.Status, StatusText = DesignRules.InputStatusText(i.Status),
                ConflictWithInputId = i.ConflictWithInputId, ConflictWithCode = i.ConflictWithInputId.HasValue && withCodes.TryGetValue(i.ConflictWithInputId.Value, out string wc) ? wc : null, ConflictNote = i.ConflictNote,
                ConflictMarkedByUserId = i.ConflictMarkedByUserId, ConflictMarkedByName = names.Name(i.ConflictMarkedByUserId), ConflictMarkedAt = i.ConflictMarkedAt,
                ConflictResolution = i.ConflictResolution, ConflictResolvedByUserId = i.ConflictResolvedByUserId, ConflictResolvedByName = names.Name(i.ConflictResolvedByUserId), ConflictResolvedAt = i.ConflictResolvedAt,
                SupersedesInputId = i.SupersedesInputId, VoidReason = i.VoidReason, CreatorUserId = i.CreatorUserId, CreatorName = names.Name(i.CreatorUserId), CreateDate = i.CreateDate,
                UsedByReview = used.Contains(i.Id), RowVersion = DesignQueries.B64(i.RowVersion),
            }).ToList();
        }

        public async Task<ResponseDto<List<DesignInputDto>>> GetListAsync(DesignInputQuery query, CancellationToken ct)
        {
            query ??= new DesignInputQuery();
            IQueryable<PbDesignInput> q = Uow.Set<PbDesignInput>().AsNoTracking().Where(i => !i.Deleted);
            if (query.ProductId.HasValue) q = q.Where(i => i.ProductId == query.ProductId.Value);
            if (query.ProductRevision.HasValue) q = q.Where(i => i.ProductRevision == query.ProductRevision.Value);
            if (query.Kind.HasValue) q = q.Where(i => i.Kind == query.Kind.Value);
            if (query.Status.HasValue) q = q.Where(i => i.Status == query.Status.Value);
            else if (!query.IncludeInactive) q = q.Where(i => i.Status == DesignInputStatus.Open || i.Status == DesignInputStatus.Conflict);
            string text = PbText.Clean(query.Contains);
            if (text != null) q = q.Where(i => i.Code.Contains(text) || i.Statement.Contains(text) || i.Source.Contains(text));
            int total = await q.CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize;
            int number = query.PageSize <= 0 ? 1 : Math.Max(query.PageNumber, 1);
            List<PbDesignInput> rows = await q.OrderBy(i => i.ProductRevision).ThenBy(i => i.Code).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            List<DesignInputDto> dtos = await ToDtosAsync(rows, ct);
            return ResponseDtoHelper.Ok(dtos);
        }

        public async Task<ResponseDto<DesignInputDto>> GetAsync(int id, CancellationToken ct)
        {
            PbDesignInput i = await Uow.Set<PbDesignInput>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (i == null) return ResponseDtoHelper.NotFound<DesignInputDto>(PbMessages.NotFound);
            return ResponseDtoHelper.Ok((await ToDtosAsync(new List<PbDesignInput> { i }, ct))[0]);
        }

        // ------------------------------------------------------------------------------------------ save

        private async Task<string> RefProblemAsync(SaveDesignInputDto d, CancellationToken ct)
        {
            if (d.Kind != DesignInputKind.CustomerSpec || !d.RefDeliverableId.HasValue) return null;
            var rev = d.RefRevisionId.HasValue ? await Uow.Set<Entities.Model.NasbNiro.ProjectControl.PcDocumentRevision>().AsNoTracking().Where(r => r.Id == d.RefRevisionId.Value && !r.Deleted).Select(r => new { r.DeliverableId }).FirstOrDefaultAsync(ct) : null;
            if (rev == null || rev.DeliverableId != d.RefDeliverableId.Value) return "بازنگری انتخاب‌شده به مدرک EDMS مرجع تعلق ندارد.";
            return null;
        }

        public async Task<ResponseDto<DesignInputDto>> SaveAsync(SaveDesignInputDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<DesignInputDto>(DesignRules.AiNever);
            if (!InRole(DesignRoles.InputWriters)) return Bad<DesignInputDto>(DesignRules.NotAllowed);
            if (dto == null) return Bad<DesignInputDto>(PbMessages.InvalidData);
            string problem = DesignRules.InputProblem(dto.Kind, dto.Statement, dto.Source, dto.AcceptanceCriterion, dto.StandardEdition, dto.PreviousDesignRef, dto.RefDeliverableId, dto.RefRevisionId, dto.RefText, dto.RefRevisionText);
            if (problem != null) return Bad<DesignInputDto>(problem);
            if (PbText.Clean(dto.FailureConsequence) != null && dto.FailureConsequence.Trim().Length > 1000) return Bad<DesignInputDto>("پیامد خرابی بیش از ۱۰۰۰ نویسه است.");
            string refProblem = await RefProblemAsync(dto, ct);
            if (refProblem != null) return Bad<DesignInputDto>(refProblem);
            if (dto.ProductRevision < 0 || dto.ProductRevision > 99) return Bad<DesignInputDto>("بازنگری محصول بین ۰ و ۹۹ است.");
            if (!await Uow.Set<PbProduct>().AnyAsync(p => p.Id == dto.ProductId && !p.Deleted, ct)) return Bad<DesignInputDto>("محصول پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            PbDesignInput old = null;
            if (dto.Id.HasValue)
            {
                old = await Uow.Set<PbDesignInput>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (old == null) return ResponseDtoHelper.NotFound<DesignInputDto>(PbMessages.NotFound);
                if (old.Status != DesignInputStatus.Open) return Bad<DesignInputDto>(old.Status == DesignInputStatus.Conflict ? "ورودی در تعارض است؛ ابتدا تعارض را رفع کنید." : "ورودی جایگزین یا باطل‌شده تغییر نمی‌کند.");
                if (old.ProductId != dto.ProductId || old.ProductRevision != dto.ProductRevision) return Bad<DesignInputDto>("محصول و بازنگری یک ورودی تغییر نمی‌کند.");
                if (!string.IsNullOrEmpty(dto.RowVersion))
                    try { Uow.Entry(old).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<DesignInputDto>(PbMessages.InvalidData); }
            }
            bool used = old != null && await Uow.Set<PbDesignReviewInput>().AnyAsync(ri => !ri.Deleted && ri.InputId == old.Id && !ri.Review.Deleted && ri.Review.Status != DesignReviewStatus.Void, ct);
            PbDesignInput target;
            if (old != null && !used)
            {
                if (old.CreatorUserId != Me && !InRole(DesignRoles.Resolvers)) return Bad<DesignInputDto>("ورودی را سازنده آن یا مدیر مهندسی/QA ویرایش می‌کند.");
                target = old;
            }
            else
            {
                target = new PbDesignInput
                {
                    ProductId = dto.ProductId, ProductRevision = dto.ProductRevision, Status = DesignInputStatus.Open, SupersedesInputId = old?.Id,
                    Code = await InventoryNumbers.NextNumberAsync(Uow, DesignNumbers.Input, "DI", now.Date, ct),
                };
                Stamp(target, true);
                Uow.Set<PbDesignInput>().Add(target);
                if (old != null) { old.Status = DesignInputStatus.Superseded; Stamp(old, false); }
            }
            Apply(target, dto);
            Stamp(target, false);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<DesignInputDto>(conflict);
            return await GetAsync(target.Id, ct);
        }

        private static void Apply(PbDesignInput t, SaveDesignInputDto d)
        {
            t.Kind = d.Kind;
            t.Statement = d.Statement.Trim(); t.Source = d.Source.Trim(); t.AcceptanceCriterion = d.AcceptanceCriterion.Trim();
            t.StandardEdition = PbText.Clean(d.StandardEdition); t.PreviousDesignRef = PbText.Clean(d.PreviousDesignRef);
            t.RefDeliverableId = d.RefDeliverableId; t.RefRevisionId = d.RefRevisionId; t.RefText = PbText.Clean(d.RefText); t.RefRevisionText = PbText.Clean(d.RefRevisionText);
            t.FailureConsequence = PbText.Clean(d.FailureConsequence);
        }

        // ------------------------------------------------------------------------------------------ actions

        public async Task<ResponseDto<DesignInputDto>> ActionAsync(DesignInputActionDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<DesignInputDto>(DesignRules.AiNever);
            if (dto == null || PbText.Clean(dto.Action) == null) return Bad<DesignInputDto>(PbMessages.InvalidData);
            PbDesignInput i = await Uow.Set<PbDesignInput>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (i == null) return ResponseDtoHelper.NotFound<DesignInputDto>(PbMessages.NotFound);
            int reasonMin = (int)(await DesignGate.SettingsAsync(Uow, ct))[DesignSettingKeys.ReasonMinChars];
            string note = PbText.Clean(dto.Note);
            if (DesignLimits.Over(note, DesignLimits.Note, "شرح") is string longNote) return Bad<DesignInputDto>(longNote);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "conflict":
                    if (!InRole(DesignRoles.InputWriters)) return Bad<DesignInputDto>(DesignRules.NotAllowed);
                    if (i.Status != DesignInputStatus.Open) return Bad<DesignInputDto>("فقط ورودی معتبر در تعارض قرار می‌گیرد.");
                    if (note == null || note.Length < reasonMin) return Bad<DesignInputDto>($"شرح تعارض دست‌کم {reasonMin} نویسه باشد.");
                    if (dto.WithInputId.HasValue)
                    {
                        if (dto.WithInputId.Value == i.Id) return Bad<DesignInputDto>("ورودی با خودش در تعارض نیست.");
                        PbDesignInput other = await Uow.Set<PbDesignInput>().FirstOrDefaultAsync(x => x.Id == dto.WithInputId.Value && !x.Deleted && x.ProductId == i.ProductId && x.ProductRevision == i.ProductRevision && (x.Status == DesignInputStatus.Open || x.Status == DesignInputStatus.Conflict), ct);
                        if (other == null) return Bad<DesignInputDto>("ورودی دوم باید از همین محصول و بازنگری و معتبر باشد.");
                        i.ConflictWithInputId = other.Id;
                        if (other.Status == DesignInputStatus.Open)
                        {
                            other.Status = DesignInputStatus.Conflict; other.ConflictWithInputId = i.Id; other.ConflictNote = note; other.ConflictMarkedByUserId = Me; other.ConflictMarkedAt = now;
                            other.ConflictResolution = null; other.ConflictResolvedByUserId = null; other.ConflictResolvedAt = null;
                            Stamp(other, false);
                        }
                    }
                    i.Status = DesignInputStatus.Conflict; i.ConflictNote = note; i.ConflictMarkedByUserId = Me; i.ConflictMarkedAt = now;
                    i.ConflictResolution = null; i.ConflictResolvedByUserId = null; i.ConflictResolvedAt = null;
                    break;
                case "resolve":
                    if (!InRole(DesignRoles.Resolvers)) return Bad<DesignInputDto>(DesignRules.NotAllowed);
                    if (i.Status != DesignInputStatus.Conflict) return Bad<DesignInputDto>("این ورودی در تعارض نیست.");
                    if (i.ConflictMarkedByUserId == Me) return Bad<DesignInputDto>("تفکیک وظایف: تعارض را نفر دوم، غیر از ثبت‌کننده تعارض، رفع می‌کند.");
                    if (note == null || note.Length < reasonMin) return Bad<DesignInputDto>($"شرح رفع تعارض دست‌کم {reasonMin} نویسه باشد.");
                    i.Status = DesignInputStatus.Open; i.ConflictResolution = note; i.ConflictResolvedByUserId = Me; i.ConflictResolvedAt = now;
                    if (i.ConflictWithInputId.HasValue)
                    {
                        PbDesignInput other = await Uow.Set<PbDesignInput>().FirstOrDefaultAsync(x => x.Id == i.ConflictWithInputId.Value && !x.Deleted && x.Status == DesignInputStatus.Conflict && x.ConflictWithInputId == i.Id, ct);
                        if (other != null)
                        {
                            other.Status = DesignInputStatus.Open; other.ConflictResolution = note; other.ConflictResolvedByUserId = Me; other.ConflictResolvedAt = now;
                            Stamp(other, false);
                        }
                    }
                    break;
                case "void":
                    if (!(i.CreatorUserId == Me || InRole(DesignRoles.Resolvers)) || !InRole(DesignRoles.InputWriters)) return Bad<DesignInputDto>(DesignRules.NotAllowed);
                    if (i.Status == DesignInputStatus.Void || i.Status == DesignInputStatus.Superseded) return Bad<DesignInputDto>("این ورودی پیش‌تر کنار گذاشته شده است.");
                    if (i.Status == DesignInputStatus.Conflict) return Bad<DesignInputDto>("ورودی در تعارض باطل نمی‌شود؛ تعارض را نفر دوم رفع می‌کند و پس از آن ورودی باطل یا جایگزین می‌شود (تفکیک وظایف).");
                    if (note == null || note.Length < reasonMin) return Bad<DesignInputDto>($"دلیل ابطال دست‌کم {reasonMin} نویسه باشد.");
                    if (await Uow.Set<PbDesignReviewInput>().AnyAsync(ri => !ri.Deleted && ri.InputId == i.Id && !ri.Review.Deleted && ri.Review.Status != DesignReviewStatus.Void, ct))
                        return Bad<DesignInputDto>("بازبینی طراحی به این ورودی ارجاع دارد؛ ابتدا بازبینی را باطل کنید یا ورودی را با ورودی دیگر جایگزین کنید.");
                    i.Status = DesignInputStatus.Void; i.VoidReason = note;
                    break;
                default: return Bad<DesignInputDto>("کار نامعتبر است.");
            }
            Stamp(i, false);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<DesignInputDto>(conflict);
            return await GetAsync(i.Id, ct);
        }

        // ------------------------------------------------------------------------------------------ copy from previous revision

        public async Task<ResponseDto<List<DesignInputDto>>> CopyFromRevisionAsync(int productId, int fromRevision, int toRevision, CancellationToken ct)
        {
            if (IsAi) return Bad<List<DesignInputDto>>(DesignRules.AiNever);
            if (!InRole(DesignRoles.InputWriters)) return Bad<List<DesignInputDto>>(DesignRules.NotAllowed);
            if (fromRevision == toRevision || toRevision < 0 || toRevision > 99 || fromRevision < 0) return Bad<List<DesignInputDto>>("بازنگری مبدا و مقصد باید متفاوت باشند.");
            List<PbDesignInput> src = await DesignQueries.LiveInputsAsync(Uow, productId, fromRevision, false, ct);
            src = src.Where(s => s.Status == DesignInputStatus.Open).ToList();
            if (src.Count == 0) return Bad<List<DesignInputDto>>("بازنگری مبدا ورودی معتبری ندارد (ورودی‌های در تعارض کپی نمی‌شوند).");
            List<string> existing = await Uow.Set<PbDesignInput>().AsNoTracking().Where(x => !x.Deleted && x.ProductId == productId && x.ProductRevision == toRevision && (x.Status == DesignInputStatus.Open || x.Status == DesignInputStatus.Conflict)).Select(x => x.Statement).ToListAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            var created = new List<PbDesignInput>();
            foreach (PbDesignInput s in src.Where(s => !existing.Contains(s.Statement)))
            {
                var n = new PbDesignInput
                {
                    ProductId = productId, ProductRevision = toRevision, Kind = s.Kind, Statement = s.Statement, Source = s.Source, AcceptanceCriterion = s.AcceptanceCriterion, StandardEdition = s.StandardEdition,
                    PreviousDesignRef = s.Kind == DesignInputKind.PreviousDesign ? s.PreviousDesignRef : s.PreviousDesignRef, RefDeliverableId = s.RefDeliverableId, RefRevisionId = s.RefRevisionId, RefText = s.RefText, RefRevisionText = s.RefRevisionText,
                    FailureConsequence = s.FailureConsequence, Status = DesignInputStatus.Open,
                    Code = await InventoryNumbers.NextNumberAsync(Uow, DesignNumbers.Input, "DI", now.Date, ct),
                };
                Stamp(n, true);
                Uow.Set<PbDesignInput>().Add(n);
                created.Add(n);
            }
            if (created.Count == 0) return Bad<List<DesignInputDto>>("همه ورودی‌های مبدا در بازنگری مقصد هست.");
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<List<DesignInputDto>>(conflict);
            return ResponseDtoHelper.Ok(await ToDtosAsync(created, ct));
        }

        // ------------------------------------------------------------------------------------------ Excel

        public ResponseDto<byte[]> Template()
        {
            var sheet = new FinanceXlsxSheet { Name = "ورودی‌های طراحی", Header = Header.ToList() };
            sheet.Rows.Add(new object[] { DesignRules.KindText(DesignInputKind.Functional), "فشار کاری ۱۰ بار", "قرارداد بند ۳", "آزمون فشار ۱۵ بار بدون نشتی", "", "", "", "", "نشتی سیال" });
            var guide = new FinanceXlsxSheet { Name = "راهنما", Header = new List<string> { "راهنما" } };
            guide.Rows.Add(new object[] { "ردیف نمونه را پاک کنید و از ردیف دوم بنویسید؛ سطر عنوان را تغییر ندهید." });
            guide.Rows.Add(new object[] { "نوع ورودی یکی از این‌هاست: " + string.Join("، ", DesignRules.AllKinds.Select(DesignRules.KindText)) });
            guide.Rows.Add(new object[] { "برای استاندارد، ویرایش لازم است؛ برای طراحی قبلی، ستون طراحی قبلی؛ برای مشخصات مشتری، مرجع و بازنگری مرجع." });
            guide.Rows.Add(new object[] { "فایل ابتدا پیش‌نمایش می‌شود؛ اگر یک ردیف خطا داشته باشد هیچ ورودی ساخته نمی‌شود." });
            return ResponseDtoHelper.Ok(SimpleXlsx.Build(new List<FinanceXlsxSheet> { sheet, guide }, true));
        }

        private static bool TryKind(string text, out DesignInputKind kind)
        {
            string k = (text ?? string.Empty).Trim().Replace('ي', 'ی').Replace('ك', 'ک');
            foreach (DesignInputKind x in DesignRules.AllKinds)
                if (k == DesignRules.KindText(x) || string.Equals(k, x.ToString(), StringComparison.OrdinalIgnoreCase)) { kind = x; return true; }
            kind = DesignInputKind.Functional;
            return false;
        }

        public async Task<ResponseDto<DesignInputExcelResultDto>> ImportAsync(DesignInputExcelRequestDto request, byte[] file, CancellationToken ct)
        {
            if (request == null || file == null || file.Length == 0) return Bad<DesignInputExcelResultDto>("فایل را انتخاب کنید.");
            if (IsAi) return Bad<DesignInputExcelResultDto>(DesignRules.AiNever);
            if (!InRole(DesignRoles.InputWriters)) return Bad<DesignInputExcelResultDto>(DesignRules.NotAllowed);
            if (request.ProductRevision < 0 || request.ProductRevision > 99) return Bad<DesignInputExcelResultDto>("بازنگری محصول بین ۰ و ۹۹ است.");
            if (!await Uow.Set<PbProduct>().AnyAsync(p => p.Id == request.ProductId && !p.Deleted, ct)) return Bad<DesignInputExcelResultDto>("محصول پیدا نشد.");
            var cells = new List<(int Row, string[] Cells)>();
            try
            {
                List<string> names = XlsxReader.SheetNames(file);
                Office.XlsxSheet sheet = XlsxReader.Read(file, names.FirstOrDefault(), 502);
                XlsxRow head = sheet.Rows.FirstOrDefault(x => x.RowNumber == 1);
                for (int c = 0; c < Header.Length; c++)
                    if (head == null || (head.Cell(c)?.AsText() ?? string.Empty).Trim() != Header[c]) return Bad<DesignInputExcelResultDto>("سطر عنوان فایل با قالب ورودی‌های طراحی یکی نیست؛ قالب را از سامانه بگیرید.");
                foreach (XlsxRow r in sheet.Rows.Where(x => x.RowNumber > 1).OrderBy(x => x.RowNumber))
                {
                    var arr = new string[Header.Length];
                    for (int c = 0; c < arr.Length; c++) arr[c] = r.Cell(c)?.AsText();
                    if (arr.All(string.IsNullOrWhiteSpace)) continue;
                    cells.Add((r.RowNumber, arr));
                }
            }
            catch (XlsxFormatException ex) { return Bad<DesignInputExcelResultDto>(ex.Message); }
            catch (Exception) { return Bad<DesignInputExcelResultDto>("فایل xlsx خوانده نشد."); }
            if (cells.Count == 0) return Bad<DesignInputExcelResultDto>("فایل هیچ ردیف داده‌ای ندارد.");
            if (cells.Count > 500) return Bad<DesignInputExcelResultDto>("حداکثر ۵۰۰ ردیف در هر بار.");
            var result = new DesignInputExcelResultDto { Preview = request.Preview, RowCount = cells.Count };
            var lines = new List<SaveDesignInputDto>();
            var seen = new HashSet<string>();
            HashSet<string> existingStatements = (await Uow.Set<PbDesignInput>().AsNoTracking().Where(x => !x.Deleted && x.ProductId == request.ProductId && x.ProductRevision == request.ProductRevision && (x.Status == DesignInputStatus.Open || x.Status == DesignInputStatus.Conflict)).Select(x => x.Statement).ToListAsync(ct)).Select(x => x.Trim()).ToHashSet();
            foreach (var (row, a) in cells)
            {
                string message = null;
                SaveDesignInputDto line = null;
                if (!TryKind(a[0], out DesignInputKind kind)) message = "نوع ورودی شناخته نشد.";
                else
                {
                    line = new SaveDesignInputDto
                    {
                        ProductId = request.ProductId, ProductRevision = request.ProductRevision, Kind = kind, Statement = a[1], Source = a[2], AcceptanceCriterion = a[3],
                        StandardEdition = a[4], PreviousDesignRef = a[5], RefText = a[6], RefRevisionText = a[7], FailureConsequence = a[8],
                    };
                    message = DesignRules.InputProblem(kind, line.Statement, line.Source, line.AcceptanceCriterion, line.StandardEdition, line.PreviousDesignRef, null, null, line.RefText, line.RefRevisionText);
                    if (message == null && PbText.Clean(line.FailureConsequence) != null && line.FailureConsequence.Trim().Length > 1000) message = "پیامد خرابی بیش از ۱۰۰۰ نویسه است.";
                    if (message == null && existingStatements.Contains(line.Statement.Trim())) message = "این الزام در ثبت ورودی‌های همین محصول و بازنگری هست.";
                    if (message == null && !seen.Add(line.Statement.Trim())) message = "الزام در همین فایل تکراری است.";
                }
                result.Rows.Add(new DesignInputExcelRowDto { Row = row, Statement = a[1], Ok = message == null, Message = message });
                if (message == null) lines.Add(line);
            }
            result.ErrorRows = result.Rows.Count(r => !r.Ok);
            result.ValidRows = result.Rows.Count(r => r.Ok);
            if (request.Preview || result.ErrorRows > 0)
            {
                if (!request.Preview) result.Warnings.Add("به دلیل ردیف‌های ناموفق، هیچ ورودی ساخته نشد؛ فایل را اصلاح کنید.");
                return ResponseDtoHelper.Ok(result);
            }
            DateTimeOffset now = DateTimeOffset.Now;
            var added = new List<PbDesignInput>();
            foreach (SaveDesignInputDto l in lines)
            {
                var n = new PbDesignInput { ProductId = l.ProductId, ProductRevision = l.ProductRevision, Status = DesignInputStatus.Open, Code = await InventoryNumbers.NextNumberAsync(Uow, DesignNumbers.Input, "DI", now.Date, ct) };
                Stamp(n, true);
                Apply(n, l);
                Uow.Set<PbDesignInput>().Add(n);
                added.Add(n);
            }
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<DesignInputExcelResultDto>(conflict);
            result.Created = added.Count;
            for (int k = 0; k < added.Count; k++) result.Rows[k < result.Rows.Count ? k : 0].Code = added[k].Code;
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<byte[]>> ExportAsync(int productId, int revision, CancellationToken ct)
        {
            List<PbDesignInput> list = await DesignQueries.LiveInputsAsync(Uow, productId, revision, false, ct);
            var sheet = new FinanceXlsxSheet { Name = "ورودی‌های طراحی", Header = Header.ToList() };
            foreach (PbDesignInput i in list)
                sheet.Rows.Add(new object[] { DesignRules.KindText(i.Kind), i.Statement, i.Source, i.AcceptanceCriterion, i.StandardEdition ?? string.Empty, i.PreviousDesignRef ?? string.Empty, i.RefText ?? string.Empty, i.RefRevisionText ?? string.Empty, i.FailureConsequence ?? string.Empty });
            return ResponseDtoHelper.Ok(SimpleXlsx.Build(new List<FinanceXlsxSheet> { sheet }, true));
        }
    }
}
