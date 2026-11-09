using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;
using FinanceXlsxSheet = Services.NasbNiro.Finance.XlsxSheet;

namespace Services.NasbNiro.Quality
{
    public interface ICharacteristicService
    {
        Task<ResponseDto<PagedList<CharListListDto>>> GetListsAsync(CharListQuery query, CancellationToken ct);
        Task<ResponseDto<CharListDto>> GetListAsync(int id, CancellationToken ct);
        Task<ResponseDto<CharListDto>> ForRevisionAsync(int revisionId, CancellationToken ct);
        Task<ResponseDto<CharListDto>> SaveAsync(SaveCharListDto dto, CancellationToken ct);
        Task<ResponseDto<CharListDto>> ActionAsync(CharListActionDto dto, CancellationToken ct);
        Task<ResponseDto<PartCharacteristicsDto>> ForItemAsync(int itemId, CancellationToken ct);
        ResponseDto<byte[]> Template();
        Task<ResponseDto<CharExcelResultDto>> ImportAsync(CharExcelRequestDto request, byte[] file, CancellationToken ct);
        Task<ResponseDto<byte[]>> ExportAsync(int id, CancellationToken ct);
    }

    /// <summary>
    /// Package B (H5): the characteristic list of a drawing revision. Engineering prepares it, a second person approves it (maker-checker, never an AI seat), it is released together with the drawing revision
    /// and does not change afterwards; a correction is a new version with a reason.
    /// </summary>
    public class CharacteristicService : ICharacteristicService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public CharacteristicService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static bool IsAi => Ai.AiActing.IsActing;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);

        private const string Makers = "ENGINEERING,ENGINEER"; // M4: the acceptance criteria are written by engineering only
        private const string Checkers = "QA,QC"; // and checked by quality (never the maker)

        private const int EmptyListReasonMin = 15;

        private static bool InForce(PcDocStatus s) => s == PcDocStatus.Released || s == PcDocStatus.AsBuilt;

        // ------------------------------------------------------------------ reading

        public async Task<ResponseDto<PagedList<CharListListDto>>> GetListsAsync(CharListQuery query, CancellationToken ct)
        {
            query ??= new CharListQuery();
            IQueryable<QcCharacteristicList> rows = _uow.Set<QcCharacteristicList>().Where(l => !l.Deleted);
            if (query.DeliverableId.HasValue) rows = rows.Where(l => l.DeliverableId == query.DeliverableId.Value);
            if (query.RevisionId.HasValue) rows = rows.Where(l => l.RevisionId == query.RevisionId.Value);
            if (query.Status.HasValue) rows = rows.Where(l => l.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(l => l.DocumentNumber.Contains(text));
            var projection = rows.OrderByDescending(l => l.Id).Select(l => new CharListListDto
            {
                Id = l.Id, DeliverableId = l.DeliverableId, RevisionId = l.RevisionId, DocumentNumber = l.DocumentNumber, RevisionCode = l.RevisionCode,
                Version = l.Version, Status = l.Status, ItemCount = l.Items.Count, ReleasedAt = l.ReleasedAt,
            });
            return ResponseDtoHelper.Ok(await PagedList<CharListListDto>.CreateAsync(projection, query.PageNumber, query.PageSize, ct));
        }

        private IQueryable<QcCharacteristicList> Full() => _uow.Set<QcCharacteristicList>().Include(l => l.Items);

        public async Task<ResponseDto<CharListDto>> GetListAsync(int id, CancellationToken ct)
        {
            QcCharacteristicList l = await Full().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (l == null) return QualityText.NotFound<CharListDto>("فهرست مشخصه پیدا نشد.");
            return ResponseDtoHelper.Ok(await BuildAsync(l, ct));
        }

        public async Task<ResponseDto<CharListDto>> ForRevisionAsync(int revisionId, CancellationToken ct)
        {
            QcCharacteristicList l = await Full().AsNoTracking().Where(x => x.RevisionId == revisionId && !x.Deleted && x.Status != CharListStatus.Void)
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
            if (l == null) return QualityText.NotFound<CharListDto>("برای این بازنگری هنوز فهرست مشخصه تهیه نشده است.");
            return ResponseDtoHelper.Ok(await BuildAsync(l, ct));
        }

        private static CharLineDto ToDto(QcCharacteristic c, QcCharacteristicList l) => new CharLineDto
        {
            Id = c.Id, RowNumber = c.RowNumber, No = c.No, Name = c.Name, Nominal = c.Nominal, TolPlus = c.TolPlus, TolMinus = c.TolMinus, Lower = c.Lower, Upper = c.Upper,
            Unit = c.Unit, Class = c.Class, ClassText = CharacteristicRules.ClassText(c.Class), InstrumentClass = c.InstrumentClass, InstrumentText = CharacteristicRules.InstrumentText(c.InstrumentClass),
            Method = c.Method, SamplePercent = c.SamplePercent, Required = CharacteristicRules.IsRequired(c.Class, c.SamplePercent), OperationSeq = c.OperationSeq, StageCode2 = c.StageCode2,
            ListId = c.ListId, DrawingNumber = l?.DocumentNumber, DrawingRevision = l?.RevisionCode,
        };

        private async Task<CharListDto> BuildAsync(QcCharacteristicList l, CancellationToken ct)
        {
            var rev = await _uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => r.Id == l.RevisionId).Select(r => new { r.Status, r.PreparedByUserId }).FirstOrDefaultAsync(ct);
            string title = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => d.Id == l.DeliverableId).Select(d => d.Title).FirstOrDefaultAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { l.CreatorUserId, l.ApprovedByUserId }, ct);
            var dto = new CharListDto
            {
                Id = l.Id, DeliverableId = l.DeliverableId, RevisionId = l.RevisionId, DocumentNumber = l.DocumentNumber, RevisionCode = l.RevisionCode, Version = l.Version, Status = l.Status,
                ItemCount = l.Items.Count, ReleasedAt = l.ReleasedAt, DocumentTitle = title, RevisionStatusText = rev == null ? null : PcStatusText(rev.Status), RevisionInForce = rev != null && InForce(rev.Status),
                Reason = l.Reason, SupersedesListId = l.SupersedesListId, PreparedByName = names.Name(l.CreatorUserId), CreatorUserId = l.CreatorUserId, SubmittedAt = l.SubmittedAt,
                ApprovedByName = names.Name(l.ApprovedByUserId), ApprovedAt = l.ApprovedAt, ContentHash = l.ContentHash, ReturnNote = l.ReturnNote, VoidReason = l.VoidReason,
                EditorUserIds = Editors(l), RowVersion = l.RowVersion == null ? null : Convert.ToBase64String(l.RowVersion),
                Items = l.Items.OrderBy(i => i.RowNumber).Select(i => ToDto(i, l)).ToList(),
            };
            bool maker = _user.IsInRole(Makers);
            bool checker = _user.IsInRole(Checkers) && !IsAi && !dto.EditorUserIds.Contains(Me) && l.CreatorUserId != Me;
            if (l.Status == CharListStatus.Draft && maker) { dto.Actions.Add("save"); dto.Actions.Add("submit"); dto.Actions.Add("void"); }
            if (l.Status == CharListStatus.Submitted) { if (checker) { dto.Actions.Add("approve"); } if (_user.IsInRole(Checkers)) dto.Actions.Add("return"); if (maker) dto.Actions.Add("void"); }
            if (l.Status == CharListStatus.Approved && maker) dto.Actions.Add("void");
            if ((l.Status == CharListStatus.Released) && maker) dto.Actions.Add("new-version");
            return dto;
        }

        private static string PcStatusText(PcDocStatus s)
        {
            switch (s)
            {
                case PcDocStatus.Draft: return "پیش‌نویس";
                case PcDocStatus.UnderReview: return "در بررسی";
                case PcDocStatus.Approved: return "تاییدشده";
                case PcDocStatus.Released: return "منتشرشده";
                case PcDocStatus.Superseded: return "جایگزین‌شده";
                case PcDocStatus.Obsolete: return "ابطال‌شده";
                default: return "ساخته‌شده";
            }
        }

        private static List<int> Editors(QcCharacteristicList l) =>
            (l.EditorUserIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out int v) ? v : 0).Where(v => v > 0).Distinct().ToList();

        private static string WithEditor(string existing, int id)
        {
            var ids = (existing ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!ids.Contains(id.ToString(CultureInfo.InvariantCulture))) ids.Add(id.ToString(CultureInfo.InvariantCulture));
            return "," + string.Join(",", ids) + ",";
        }

        // ------------------------------------------------------------------ writing

        public async Task<ResponseDto<CharListDto>> SaveAsync(SaveCharListDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<CharListDto>(QualityText.InvalidData);
            if (!_user.IsInRole(Makers)) return Bad<CharListDto>(QualityText.NotAllowed);
            PcDocumentRevision rev = await _uow.Set<PcDocumentRevision>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == dto.RevisionId && !r.Deleted, ct);
            if (rev == null) return QualityText.NotFound<CharListDto>("بازنگری سند (نقشه) پیدا نشد.");
            if (rev.Status == PcDocStatus.Superseded || rev.Status == PcDocStatus.Obsolete) return Bad<CharListDto>("این بازنگری نقشه جایگزین یا ابطال شده است؛ فهرست مشخصه برای آن ثبت نمی‌شود.");
            PcEngineeringDeliverable doc = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().FirstAsync(d => d.Id == rev.DeliverableId, ct);

            string problem = await ValidateItemsAsync(dto.Items, dto.Reason, ct);
            if (problem != null) return Bad<CharListDto>(problem);

            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            QcCharacteristicList list;
            if (dto.Id.HasValue)
            {
                list = await Full().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (list == null) return QualityText.NotFound<CharListDto>("فهرست مشخصه پیدا نشد.");
                if (list.RevisionId != rev.Id) return Bad<CharListDto>("فهرست مشخصه متعلق به این بازنگری نیست.");
                if (list.Status != CharListStatus.Draft)
                    return Bad<CharListDto>(list.Status == CharListStatus.Released || list.Status == CharListStatus.Superseded
                        ? "فهرست منتشرشده تغییر نمی‌کند؛ برای اصلاح، نسخه جدید (با دلیل) بسازید."
                        : "فقط پیش‌نویس ویرایش می‌شود؛ فهرست ارسال‌شده را کنترل‌کننده برگشت دهد.");
                if (!string.IsNullOrEmpty(dto.RowVersion))
                    try { _uow.Entry(list).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<CharListDto>(QualityText.InvalidData); }
                if (dto.Reason != null && list.Version > 1) list.Reason = Clean(dto.Reason) ?? list.Reason;
                _uow.RemoveRange(list.Items.ToList());
                list.Items.Clear();
            }
            else
            {
                List<QcCharacteristicList> existing = await _uow.Set<QcCharacteristicList>().AsNoTracking().Where(x => x.RevisionId == rev.Id && !x.Deleted && x.Status != CharListStatus.Void).ToListAsync(ct);
                if (existing.Any(x => x.Status == CharListStatus.Draft || x.Status == CharListStatus.Submitted || x.Status == CharListStatus.Approved))
                    return Bad<CharListDto>("برای این بازنگری فهرستی در جریان است؛ همان را ویرایش یا ابطال کنید.");
                int version = (await _uow.Set<QcCharacteristicList>().Where(x => x.RevisionId == rev.Id).Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1;
                string reason = Clean(dto.Reason);
                if (version > 1 && (reason == null || reason.Length < 5)) return Bad<CharListDto>("برای نسخه جدید فهرست مشخصه، دلیل اصلاح را بنویسید.");
                QcCharacteristicList previous = existing.Where(x => x.Status == CharListStatus.Released).OrderByDescending(x => x.Version).FirstOrDefault();
                list = new QcCharacteristicList
                {
                    DeliverableId = doc.Id, RevisionId = rev.Id, DocumentNumber = doc.DocumentNumber, RevisionCode = rev.Code, Version = version, Status = CharListStatus.Draft,
                    Reason = reason, SupersedesListId = previous?.Id, CreateDate = now, CreatorUserId = Me,
                };
                _uow.Set<QcCharacteristicList>().Add(list);
            }
            list.EditorUserIds = WithEditor(list.EditorUserIds, Me);
            list.ReturnNote = null;
            int row = 0;
            foreach (SaveCharLineDto c in dto.Items)
            {
                CharClass cls = c.Class;
                int sample = c.SamplePercent ?? DefaultSample(st, cls);
                list.Items.Add(new QcCharacteristic
                {
                    RowNumber = ++row, No = Clean(c.No), Name = Clean(c.Name), Nominal = c.Nominal, TolPlus = c.TolPlus, TolMinus = c.TolMinus, Unit = Clean(c.Unit), Class = cls,
                    InstrumentClass = Enum.IsDefined(typeof(EquipmentType), c.InstrumentClass) ? c.InstrumentClass : EquipmentType.Other, Method = Clean(c.Method), SamplePercent = sample,
                    OperationSeq = c.OperationSeq, StageCode2 = Clean(c.StageCode2)?.ToUpperInvariant(),
                });
            }
            string rule = ClassProblems(list.Items, st);
            if (rule != null) { if (list.Id == 0) _uow.Set<QcCharacteristicList>().Remove(list); return Bad<CharListDto>(rule); }
            list.ModifyDate = now; list.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<CharListDto>(QualityText.Concurrency); }
            catch (DbUpdateException) { return Bad<CharListDto>("برای این بازنگری هم‌زمان فهرست دیگری ثبت شد؛ صفحه را تازه کنید."); }
            return await GetListAsync(list.Id, ct);
        }

        private static int DefaultSample(Dictionary<string, decimal> st, CharClass cls) =>
            (int)(cls == CharClass.Critical ? st[QualitySettingKeys.PkbSampleCritical] : cls == CharClass.Major ? st[QualitySettingKeys.PkbSampleMajor] : st[QualitySettingKeys.PkbSampleMinor]);

        private static string ClassProblems(IEnumerable<QcCharacteristic> items, Dictionary<string, decimal> st)
        {
            int critical = (int)st[QualitySettingKeys.PkbSampleCritical], major = (int)st[QualitySettingKeys.PkbSampleMajor], minor = (int)st[QualitySettingKeys.PkbSampleMinor];
            foreach (QcCharacteristic c in items)
            {
                string p = CharacteristicRules.LineProblem(c.No, c.Name, c.TolPlus, c.TolMinus, c.Class, c.SamplePercent, critical, major, $"ردیف {c.RowNumber}: ", minor);
                if (p != null) return p;
                if (c.OperationSeq.HasValue && c.OperationSeq.Value <= 0) return $"ردیف {c.RowNumber}: شماره گام عملیات باید بزرگ‌تر از صفر باشد.";
                if ((c.Unit ?? string.Empty).Length > 20) return $"ردیف {c.RowNumber}: واحد بیش از ۲۰ نویسه است.";
                if ((c.Method ?? string.Empty).Length > 300) return $"ردیف {c.RowNumber}: روش بازرسی بیش از ۳۰۰ نویسه است.";
            }
            return null;
        }

        private Task<string> ValidateItemsAsync(List<SaveCharLineDto> items, string reason, CancellationToken ct)
        {
            // H2: a drawing without measurable characteristics is declared through the same workflow: an EMPTY list with a written reason (maker-checker like any list)
            if (items == null || items.Count == 0)
                return Task.FromResult((Clean(reason)?.Length ?? 0) >= EmptyListReasonMin ? null : $"حداقل یک مشخصه ثبت کنید؛ برای نقشه‌ای که مشخصه قابل اندازه‌گیری ندارد، فهرست خالی فقط با دلیل (دست‌کم {EmptyListReasonMin} نویسه) پذیرفته می‌شود.");
            if (items.Count > 500) return Task.FromResult("حداکثر ۵۰۰ مشخصه در هر فهرست.");
            var seen = new HashSet<string>();
            int row = 0;
            foreach (SaveCharLineDto c in items)
            {
                row++;
                if (c == null) return Task.FromResult(QualityText.InvalidData);
                if (!Enum.IsDefined(typeof(CharClass), c.Class)) return Task.FromResult($"ردیف {row}: رده مشخصه نامعتبر است.");
                if (!seen.Add(CharacteristicRules.Key(c.No))) return Task.FromResult($"ردیف {row}: شماره مشخصه «{c.No}» تکراری است.");
                if (decimal.Round(c.Nominal, 4) != c.Nominal || decimal.Round(c.TolPlus, 4) != c.TolPlus || decimal.Round(c.TolMinus, 4) != c.TolMinus)
                    return Task.FromResult($"ردیف {row}: مقدار اسمی و تلرانس‌ها حداکثر تا چهار رقم اعشار پذیرفته می‌شود (محتوای فهرست با همین دقت مهر می‌شود).");
            }
            return Task.FromResult<string>(null);
        }

        public async Task<ResponseDto<CharListDto>> ActionAsync(CharListActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<CharListDto>(QualityText.InvalidData);
            QcCharacteristicList l = await Full().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (l == null) return QualityText.NotFound<CharListDto>("فهرست مشخصه پیدا نشد.");
            if (!string.IsNullOrEmpty(dto.RowVersion))
                try { _uow.Entry(l).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<CharListDto>(QualityText.InvalidData); }
            string note = Clean(dto.Note);
            DateTimeOffset now = DateTimeOffset.Now;
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "submit":
                    if (!_user.IsInRole(Makers)) return Bad<CharListDto>(QualityText.NotAllowed);
                    if (l.Status != CharListStatus.Draft) return Bad<CharListDto>("فقط پیش‌نویس ارسال می‌شود.");
                    if (l.Items.Count == 0 && (Clean(l.Reason)?.Length ?? 0) < EmptyListReasonMin) return Bad<CharListDto>("فهرست بدون مشخصه فقط با دلیل «بدون مشخصه قابل اندازه‌گیری» ارسال می‌شود.");
                    string rule = ClassProblems(l.Items, st);
                    if (rule != null) return Bad<CharListDto>(rule);
                    l.Status = CharListStatus.Submitted; l.SubmittedAt = now; l.EditorUserIds = WithEditor(l.EditorUserIds, Me);
                    break;
                case "return":
                    if (!_user.IsInRole(Checkers)) return Bad<CharListDto>(QualityText.NotAllowed);
                    if (l.Status != CharListStatus.Submitted) return Bad<CharListDto>("فقط فهرست ارسال‌شده برگشت داده می‌شود.");
                    if (note == null) return Bad<CharListDto>("دلیل برگشت را بنویسید.");
                    l.Status = CharListStatus.Draft; l.ReturnNote = note; l.SubmittedAt = null;
                    break;
                case "approve":
                {
                    if (!_user.IsInRole(Checkers)) return Bad<CharListDto>(QualityText.NotAllowed);
                    if (IsAi) return Bad<CharListDto>("تایید فهرست مشخصه با انسان است؛ صندلی هوش مصنوعی آن را تایید نمی‌کند.");
                    if (l.Status != CharListStatus.Submitted) return Bad<CharListDto>("فقط فهرست ارسال‌شده تایید می‌شود.");
                    if (l.CreatorUserId == Me || Editors(l).Contains(Me)) return Bad<CharListDto>("تهیه‌کننده یا ویرایش‌کننده فهرست نمی‌تواند آن را تایید کند (تفکیک وظایف).");
                    PcDocStatus revStatus = await _uow.Set<PcDocumentRevision>().Where(r => r.Id == l.RevisionId).Select(r => r.Status).FirstAsync(ct);
                    if (InForce(revStatus))
                    {
                        // Package A2: a new version of the list of a drawing revision that is already in force is released at once by this approval, so it needs an approved engineering change order that names it
                        string ecProblem = await global::Services.NasbNiro.EngChange.EcGate.CharListVersionProblemAsync(_uow, l.Id, l.RevisionId, l.Version, ct);
                        if (ecProblem != null) return Bad<CharListDto>(ecProblem);
                    }
                    l.ContentHash = CharacteristicRules.Hash(l.Items.OrderBy(i => i.RowNumber).Select(CharacteristicResolver.ToLine));
                    l.Status = CharListStatus.Approved; l.ApprovedByUserId = Me; l.ApprovedAt = now;
                    if (InForce(revStatus)) await CharacteristicRelease.ReleaseAsync(_uow, l, now, ct);
                    break;
                }
                case "void":
                    if (!_user.IsInRole(Makers)) return Bad<CharListDto>(QualityText.NotAllowed);
                    if (l.Status == CharListStatus.Released || l.Status == CharListStatus.Superseded || l.Status == CharListStatus.Void)
                        return Bad<CharListDto>("فهرست منتشرشده یا جایگزین‌شده باطل نمی‌شود؛ نسخه جدید بسازید.");
                    if (note == null) return Bad<CharListDto>("دلیل ابطال را بنویسید.");
                    l.Status = CharListStatus.Void; l.VoidReason = note;
                    break;
                default:
                    return Bad<CharListDto>("عمل نامعتبر است (submit، return، approve یا void). برای نسخه جدید، ذخیره بدون شناسه را با دلیل بزنید.");
            }
            l.ModifyDate = now; l.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<CharListDto>(QualityText.Concurrency); }
            return await GetListAsync(l.Id, ct);
        }

        // ------------------------------------------------------------------ the characteristics of a part

        public async Task<ResponseDto<PartCharacteristicsDto>> ForItemAsync(int itemId, CancellationToken ct)
        {
            InventoryItem item = await _uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && !i.Deleted, ct);
            if (item == null) return QualityText.NotFound<PartCharacteristicsDto>("کالا پیدا نشد.");
            ResolvedCharacteristics r = await CharacteristicResolver.ForItemAsync(_uow, itemId, null, ct);
            var dto = new PartCharacteristicsDto
            {
                ItemId = itemId, ItemCode = item.Code, ItemTitle = item.Title, HasDrawing = r.HasDrawing, HasReleasedList = r.HasList, NoDrawingActive = r.NoDrawingActive, NoDrawingReason = r.NoDrawingReason,
            };
            foreach (PartDrawing d in r.Drawings)
            {
                QcCharacteristicList l = r.Lists.FirstOrDefault(x => x.RevisionId == d.RevisionId);
                dto.Drawings.Add(new PartDrawingDto
                {
                    DeliverableId = d.DeliverableId, RevisionId = d.RevisionId, Number = d.Number, Title = d.Title, RevisionCode = d.RevisionCode, ListId = l?.Id, ListVersion = l?.Version,
                    ListStatusText = l == null ? "فهرست منتشرشده ندارد" : "منتشرشده",
                });
            }
            foreach (CharLine c in r.Lines)
            {
                QcCharacteristicList l = r.ListOf(c);
                dto.Lines.Add(new CharLineDto
                {
                    Id = c.Id, No = c.No, Name = c.Name, Nominal = c.Nominal, TolPlus = c.TolPlus, TolMinus = c.TolMinus, Lower = c.Nominal - c.TolMinus, Upper = c.Nominal + c.TolPlus, Unit = c.Unit,
                    Class = c.Class, ClassText = CharacteristicRules.ClassText(c.Class), InstrumentClass = c.InstrumentClass, InstrumentText = CharacteristicRules.InstrumentText(c.InstrumentClass),
                    Method = c.Method, SamplePercent = c.SamplePercent, Required = CharacteristicRules.IsRequired(c), OperationSeq = c.OperationSeq, StageCode2 = c.StageCode2,
                    ListId = c.ListId, DrawingNumber = l?.DocumentNumber, DrawingRevision = l?.RevisionCode,
                });
            }
            dto.Message = r.HasList ? null
                : r.HasDrawing ? "نقشه منتشرشده دارد اما فهرست مشخصه‌های منتشرشده ندارد؛ مهندسی باید فهرست را تهیه، تایید و با نقشه منتشر کند."
                : r.NoDrawingActive ? "این قطعه با پرچم «بدون نقشه» مجاز شده است: " + r.NoDrawingReason
                : "این قطعه نقشه منتشرشده‌ای ندارد.";
            return ResponseDtoHelper.Ok(dto);
        }

        // ------------------------------------------------------------------ Excel

        private static readonly string[] Header =
        {
            "شماره مشخصه", "نام مشخصه", "مقدار اسمی", "تلرانس +", "تلرانس −", "واحد", "رده (بحرانی/عمده/جزئی)", "کلاس ابزار", "روش بازرسی", "درصد نمونه‌برداری", "گام عملیات (OPC)", "کد مرحله",
        };

        public ResponseDto<byte[]> Template()
        {
            var sheet = new FinanceXlsxSheet { Name = "مشخصه‌ها", Header = Header.ToList() };
            sheet.Rows.Add(new object[] { "1", "قطر بیرونی", 50m, 0.05m, 0.05m, "mm", "بحرانی", "ابزار دستی ابعادی", "کولیس", "", 20m, "" });
            var guide = new FinanceXlsxSheet { Name = "راهنما", Header = new List<string> { "راهنما" } };
            guide.Rows.Add(new object[] { "ردیف نمونه را پاک کنید و از ردیف دوم داده‌ها را بنویسید؛ ردیف عنوان ستون‌ها را تغییر ندهید." });
            guide.Rows.Add(new object[] { "تلرانس‌ها مقدار مثبت‌اند (مثلاً ۵۰ با +۰٫۰۵ و −۰٫۰۵). رده: بحرانی، عمده یا جزئی." });
            guide.Rows.Add(new object[] { "کلاس ابزار: ابزار دستی ابعادی، گیج، فشارسنج، دماسنج، ابزار الکتریکی، تجهیز NDT، ابزار میز آزمون یا هر نوع." });
            guide.Rows.Add(new object[] { "درصد نمونه‌برداری خالی = پیش‌فرض رده (بحرانی ۱۰۰٪). فایل ابتدا پیش‌نمایش می‌شود؛ فهرست همیشه به صورت پیش‌نویس ساخته می‌شود و پس از تایید نفر دوم با نقشه منتشر می‌شود." });
            return ResponseDtoHelper.Ok(SimpleXlsx.Build(new List<FinanceXlsxSheet> { sheet, guide }, true));
        }

        public async Task<ResponseDto<CharExcelResultDto>> ImportAsync(CharExcelRequestDto request, byte[] file, CancellationToken ct)
        {
            if (request == null || file == null || file.Length == 0) return Bad<CharExcelResultDto>("فایل را انتخاب کنید.");
            if (!_user.IsInRole(Makers)) return Bad<CharExcelResultDto>(QualityText.NotAllowed);
            var cells = new List<(int Row, string[] Cells)>();
            try
            {
                List<string> names = XlsxReader.SheetNames(file);
                Office.XlsxSheet sheet = XlsxReader.Read(file, names.FirstOrDefault(), 502);
                XlsxRow head = sheet.Rows.FirstOrDefault(x => x.RowNumber == 1);
                for (int c = 0; c < 3; c++)
                    if (head == null || CharacteristicRules.Key(head.Cell(c)?.AsText()) != CharacteristicRules.Key(Header[c])) return Bad<CharExcelResultDto>("سطر عنوان فایل با قالب فهرست مشخصه‌ها یکی نیست؛ قالب را از سامانه بگیرید.");
                foreach (XlsxRow r in sheet.Rows.Where(x => x.RowNumber > 1).OrderBy(x => x.RowNumber))
                {
                    var arr = new string[Header.Length];
                    for (int c = 0; c < arr.Length; c++) arr[c] = r.Cell(c)?.AsText();
                    if (arr.All(string.IsNullOrWhiteSpace)) continue;
                    cells.Add((r.RowNumber, arr));
                }
            }
            catch (XlsxFormatException ex) { return Bad<CharExcelResultDto>(ex.Message); }
            catch (Exception) { return Bad<CharExcelResultDto>("فایل xlsx خوانده نشد."); }
            if (cells.Count == 0) return Bad<CharExcelResultDto>("فایل هیچ ردیف داده‌ای ندارد.");
            if (cells.Count > 500) return Bad<CharExcelResultDto>("حداکثر ۵۰۰ ردیف در هر بار.");

            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            int critical = (int)st[QualitySettingKeys.PkbSampleCritical], major = (int)st[QualitySettingKeys.PkbSampleMajor];
            var result = new CharExcelResultDto { Preview = request.Preview, RowCount = cells.Count };
            var items = new List<SaveCharLineDto>();
            var seen = new HashSet<string>();
            foreach (var (row, a) in cells)
            {
                string message = ParseRow(a, st, critical, major, out SaveCharLineDto line);
                if (message == null && !seen.Add(CharacteristicRules.Key(line.No))) message = $"شماره مشخصه «{line.No}» تکراری است.";
                result.Rows.Add(new CharExcelRowDto { Row = row, No = a[0], Name = a[1], Ok = message == null, Message = message });
                if (message == null) items.Add(line);
            }
            result.ErrorRows = result.Rows.Count(r => !r.Ok);
            result.ValidRows = result.Rows.Count(r => r.Ok);
            if (request.Preview || result.ErrorRows > 0)
            {
                if (!request.Preview) result.Warnings.Add("به دلیل ردیف‌های ناموفق، هیچ فهرستی ساخته نشد؛ فایل را اصلاح کنید.");
                return ResponseDtoHelper.Ok(result);
            }
            ResponseDto<CharListDto> saved = await SaveAsync(new SaveCharListDto { Id = request.ListId, RevisionId = request.RevisionId, Reason = request.Reason, Items = items }, ct);
            if (!saved.Success) return Bad<CharExcelResultDto>(saved.Message);
            result.ListId = saved.Data.Id;
            return ResponseDtoHelper.Ok(result);
        }

        private static string ParseRow(string[] a, Dictionary<string, decimal> st, int critical, int major, out SaveCharLineDto line)
        {
            line = null;
            string no = Clean(a[0]), name = Clean(a[1]);
            if (no == null) return "شماره مشخصه خالی است.";
            if (name == null) return "نام مشخصه خالی است.";
            if (!TryNumber(a[2], out decimal nominal)) return "مقدار اسمی عدد نیست.";
            decimal plus = 0m, minus = 0m;
            if (Clean(a[3]) != null && !TryNumber(a[3], out plus)) return "تلرانس + عدد نیست.";
            if (Clean(a[4]) != null && !TryNumber(a[4], out minus)) return "تلرانس − عدد نیست.";
            if (plus < 0 || minus < 0) return "تلرانس‌ها را مقدار مثبت (+ و −) وارد کنید.";
            CharClass cls;
            string c = CharacteristicRules.Key(a[6]);
            if (c == CharacteristicRules.Key("بحرانی") || c == "CRITICAL" || c == "A") cls = CharClass.Critical;
            else if (c == CharacteristicRules.Key("عمده") || c == "MAJOR" || c == "B") cls = CharClass.Major;
            else if (c == CharacteristicRules.Key("جزئی") || c == CharacteristicRules.Key("جزیی") || c == "MINOR" || c == "C") cls = CharClass.Minor;
            else return "رده باید بحرانی، عمده یا جزئی باشد.";
            EquipmentType instrument = EquipmentType.Other;
            string ik = CharacteristicRules.Key(a[7]);
            if (ik.Length > 0)
            {
                bool found = false;
                foreach (EquipmentType t in Enum.GetValues(typeof(EquipmentType)))
                    if (ik == CharacteristicRules.Key(CharacteristicRules.InstrumentText(t)) || ik == CharacteristicRules.Key(t.ToString())) { instrument = t; found = true; break; }
                if (!found) return "کلاس ابزار نامعتبر است.";
            }
            int? sample = null;
            if (Clean(a[9]) != null) { if (!TryNumber(a[9], out decimal sp) || sp != Math.Floor(sp)) return "درصد نمونه‌برداری عدد صحیح نیست."; sample = (int)sp; }
            int? op = null;
            if (Clean(a[10]) != null) { if (!TryNumber(a[10], out decimal os) || os != Math.Floor(os) || os <= 0) return "گام عملیات عدد صحیح مثبت نیست."; op = (int)os; }
            int effective = sample ?? (int)(cls == CharClass.Critical ? st[QualitySettingKeys.PkbSampleCritical] : cls == CharClass.Major ? st[QualitySettingKeys.PkbSampleMajor] : st[QualitySettingKeys.PkbSampleMinor]);
            string problem = CharacteristicRules.LineProblem(no, name, plus, minus, cls, effective, critical, major, string.Empty, (int)st[QualitySettingKeys.PkbSampleMinor]);
            if (problem != null) return problem;
            line = new SaveCharLineDto { No = no, Name = name, Nominal = nominal, TolPlus = plus, TolMinus = minus, Unit = Clean(a[5]), Class = cls, InstrumentClass = instrument, Method = Clean(a[8]), SamplePercent = sample, OperationSeq = op, StageCode2 = Clean(a[11]) };
            return null;
        }

        private static bool TryNumber(string text, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var sb = new System.Text.StringBuilder();
            foreach (char ch in text.Trim())
            {
                if (ch >= '۰' && ch <= '۹') sb.Append((char)('0' + (ch - '۰')));
                else if (ch >= '٠' && ch <= '٩') sb.Append((char)('0' + (ch - '٠')));
                else if (ch == '٫') sb.Append('.');
                else if (ch == '،') return false; // an Arabic comma may be a thousands or a decimal separator: not guessed
                else if (ch == '−' || ch == '–') sb.Append('-');
                else sb.Append(ch);
            }
            return decimal.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public async Task<ResponseDto<byte[]>> ExportAsync(int id, CancellationToken ct)
        {
            QcCharacteristicList l = await Full().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (l == null) return QualityText.NotFound<byte[]>("فهرست مشخصه پیدا نشد.");
            var sheet = new FinanceXlsxSheet { Name = "مشخصه‌ها", Header = Header.ToList() };
            foreach (QcCharacteristic c in l.Items.OrderBy(i => i.RowNumber))
                sheet.Rows.Add(new object[] { c.No, c.Name, c.Nominal, c.TolPlus, c.TolMinus, c.Unit, CharacteristicRules.ClassText(c.Class), CharacteristicRules.InstrumentText(c.InstrumentClass), c.Method, c.SamplePercent, c.OperationSeq?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, c.StageCode2 });
            return ResponseDtoHelper.Ok(SimpleXlsx.Build(new List<FinanceXlsxSheet> { sheet }, true));
        }
    }

    /// <summary>The release of the characteristic lists together with the drawing revision.</summary>
    public static class CharacteristicRelease
    {
        /// <summary>
        /// Called from the release of an EDMS revision (same transaction, before the save): the approved list of the revision becomes Released; the earlier Released versions of the same revision and the Released lists of
        /// the revisions that this release supersedes become Superseded. A list whose hash no longer matches its content stays Approved (never released).
        /// </summary>
        public static async Task OnRevisionReleasedAsync(IUnitOfWork uow, int revisionId, IEnumerable<int> supersededRevisionIds, DateTimeOffset now, CancellationToken ct)
        {
            List<int> old = (supersededRevisionIds ?? Enumerable.Empty<int>()).ToList();
            if (old.Count > 0)
                foreach (QcCharacteristicList l in await uow.Set<QcCharacteristicList>().Where(x => !x.Deleted && x.Status == CharListStatus.Released && old.Contains(x.RevisionId)).ToListAsync(ct))
                    l.Status = CharListStatus.Superseded;
            QcCharacteristicList approved = await uow.Set<QcCharacteristicList>().Include(x => x.Items)
                .Where(x => !x.Deleted && x.RevisionId == revisionId && x.Status == CharListStatus.Approved).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
            if (approved != null) await ReleaseAsync(uow, approved, now, ct);
        }

        /// <summary>
        /// H2: null when the drawing revision may be released. A revision of a drawing that is linked to a part needs an Approved list (possibly the empty list «بدون مشخصه قابل اندازه‌گیری»); a list whose content
        /// no longer matches its approval hash is never skipped silently: the release is refused with the reason.
        /// </summary>
        public static async Task<string> ReleaseProblemAsync(IUnitOfWork uow, int deliverableId, int revisionId, CancellationToken ct)
        {
            QcCharacteristicList approved = await uow.Set<QcCharacteristicList>().Include(x => x.Items)
                .Where(x => !x.Deleted && x.RevisionId == revisionId && x.Status == CharListStatus.Approved).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
            if (approved == null)
            {
                bool partLinked = await uow.Set<Entities.Model.NasbNiro.ProjectControl.PcDocumentLink>().AnyAsync(l => !l.Deleted && l.UnlinkReason == null && l.DeliverableId == deliverableId
                    && (l.Kind == Entities.Model.NasbNiro.ProjectControl.PcDocLinkKind.Item || l.Kind == Entities.Model.NasbNiro.ProjectControl.PcDocLinkKind.PartNumber || l.Kind == Entities.Model.NasbNiro.ProjectControl.PcDocLinkKind.BomLine), ct);
                return partLinked
                    ? "انتشار نقشه ممکن نیست: این نقشه به یک قطعه وصل است و برای این بازنگری فهرست مشخصه‌های تاییدشده وجود ندارد. مهندسی فهرست مشخصه‌ها را تهیه کند و کنترل‌کننده تایید کند (برای نقشه بدون مشخصه قابل اندازه‌گیری: فهرست خالی با دلیل)."
                    : null;
            }
            string hash = CharacteristicRules.Hash(approved.Items.OrderBy(i => i.RowNumber).Select(CharacteristicResolver.ToLine));
            return string.Equals(hash, approved.ContentHash, StringComparison.Ordinal) ? null
                : "انتشار نقشه ممکن نیست: محتوای فهرست مشخصه‌های تاییدشده با مهر تایید آن (SHA-256) نمی‌خواند؛ فهرست دستکاری شده است. نسخه جدید فهرست را تهیه و تایید کنید.";
        }

        /// <summary>
        /// For a drawing revision that is already in force and is being linked to a part: null when it has an approved or released list whose content still matches its approval hash, else the Persian reason.
        /// </summary>
        public static async Task<string> LinkProblemAsync(IUnitOfWork uow, int revisionId, CancellationToken ct)
        {
            QcCharacteristicList list = await uow.Set<QcCharacteristicList>().Include(x => x.Items)
                .Where(x => !x.Deleted && x.RevisionId == revisionId && (x.Status == CharListStatus.Released || x.Status == CharListStatus.Approved)).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
            if (list == null) return "فهرست مشخصه‌های تاییدشده برای این بازنگری وجود ندارد";
            string hash = CharacteristicRules.Hash(list.Items.OrderBy(i => i.RowNumber).Select(CharacteristicResolver.ToLine));
            return string.Equals(hash, list.ContentHash, StringComparison.Ordinal) ? null : "محتوای فهرست مشخصه‌های تاییدشده با مهر تایید آن (SHA-256) نمی‌خواند";
        }

        public static async Task ReleaseAsync(IUnitOfWork uow, QcCharacteristicList list, DateTimeOffset now, CancellationToken ct)
        {
            if (list.Items == null || list.Items.Count == 0) list.Items = await uow.Set<QcCharacteristic>().Where(i => i.ListId == list.Id).ToListAsync(ct);
            string hash = CharacteristicRules.Hash(list.Items.OrderBy(i => i.RowNumber).Select(CharacteristicResolver.ToLine));
            if (!string.Equals(hash, list.ContentHash, StringComparison.Ordinal)) return;
            foreach (QcCharacteristicList prev in await uow.Set<QcCharacteristicList>().Where(x => !x.Deleted && x.RevisionId == list.RevisionId && x.Id != list.Id && x.Status == CharListStatus.Released).ToListAsync(ct))
                prev.Status = CharListStatus.Superseded;
            list.Status = CharListStatus.Released; list.ReleasedAt = now;
        }
    }
}
