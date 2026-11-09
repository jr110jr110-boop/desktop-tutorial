using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;
using FinanceXlsxSheet = Services.NasbNiro.Finance.XlsxSheet;

namespace Services.NasbNiro.Quality
{
    /// <summary>The certificate type an item needs and where it comes from.</summary>
    internal sealed class RequiredCertificate
    {
        public CertificateType? Type { get; set; }
        public List<string> Sources { get; } = new List<string>();
        public List<MaterialSpec> Specs { get; } = new List<MaterialSpec>();
    }

    /// <summary>Package C (H8): what a certificate must show and the server verdict on it. Shared by the certificate save, the QC verification and the receipt gate.</summary>
    internal static class CertificateEngine
    {
        /// <summary>The required EN 10204 type of an item: the highest of the old per-item field, the approved specifications the item belongs to and the approved specifications its released BOM lines name.</summary>
        public static async Task<RequiredCertificate> RequiredAsync(IUnitOfWork uow, int itemId, CancellationToken ct)
        {
            var result = new RequiredCertificate();
            CertificateType? legacy = await uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == itemId).Select(i => i.RequiredCertificateType).FirstOrDefaultAsync(ct);
            void Add(CertificateType type, string source)
            {
                if (!result.Type.HasValue || type > result.Type.Value) result.Type = type;
                result.Sources.Add(source);
            }
            if (legacy.HasValue) Add(legacy.Value, "تعریف کالا");

            List<MaterialSpec> linked = await uow.Set<MaterialSpec>().AsNoTracking().Include(s => s.Limits)
                .Where(s => !s.Deleted && s.Status == MatSpecStatus.Approved && s.Items.Any(i => i.ItemId == itemId)).ToListAsync(ct);
            foreach (MaterialSpec s in linked)
            {
                Add(s.RequiredCertificateType, $"مشخصات ماده {s.Grade} نسخه {s.Version}");
                result.Specs.Add(s);
            }

            var lines = await (from l in uow.Set<PbBomLine>().AsNoTracking()
                               join h in uow.Set<PbBomHeader>().AsNoTracking() on l.BomHeaderId equals h.Id
                               where !l.Deleted && !h.Deleted && h.Status == PbBomStatus.Released && l.ItemId == itemId
                               select new { l.Material, l.Standard }).Distinct().ToListAsync(ct);
            if (lines.Count > 0)
            {
                List<MaterialSpec> approved = await uow.Set<MaterialSpec>().AsNoTracking().Include(s => s.Limits).Where(s => !s.Deleted && s.Status == MatSpecStatus.Approved).ToListAsync(ct);
                foreach (var line in lines)
                {
                    string material = CharacteristicRules.Key(line.Material);
                    if (material.Length == 0) continue;
                    string standard = CharacteristicRules.Key(line.Standard);
                    foreach (MaterialSpec s in approved)
                    {
                        if (CharacteristicRules.Key(s.Grade) != material) continue;
                        if (standard.Length > 0 && CharacteristicRules.Key(s.Standard) != standard) continue;
                        if (result.Specs.Any(x => x.Id == s.Id)) continue;
                        Add(s.RequiredCertificateType, $"ردیف BOM ({line.Material}{(string.IsNullOrWhiteSpace(line.Standard) ? "" : " / " + line.Standard)}) و مشخصات ماده {s.Grade} نسخه {s.Version}");
                        result.Specs.Add(s);
                    }
                }
            }
            return result;
        }

        /// <summary>The approved specification for a grade (and standard when the certificate prints one); among the specs of the item first.</summary>
        public static async Task<MaterialSpec> FindSpecAsync(IUnitOfWork uow, int itemId, string grade, string standard, CancellationToken ct)
        {
            string g = CharacteristicRules.Key(grade);
            if (g.Length == 0) return null;
            string s = CharacteristicRules.Key(standard);
            List<MaterialSpec> approved = await uow.Set<MaterialSpec>().AsNoTracking().Include(x => x.Limits).Include(x => x.Items).Where(x => !x.Deleted && x.Status == MatSpecStatus.Approved).ToListAsync(ct);
            IEnumerable<MaterialSpec> match = approved.Where(x => CharacteristicRules.Key(x.Grade) == g && CharacteristicRules.Key(x.Standard) == s);   // Fix round 1 (H2): the standard matches exactly (a blank one matches only a spec without a standard)
            return match.OrderByDescending(x => x.Items.Any(i => i.ItemId == itemId)).ThenBy(x => x.Id).FirstOrDefault();
        }

        /// <summary>Judges the certificate (its values must be loaded) and stores the verdict on it and on every value row. The caller saves.</summary>
        public static async Task<CertificateEvaluation> EvaluateAsync(IUnitOfWork uow, MaterialCertificate cert, InventoryLot lot, CancellationToken ct)
        {
            RequiredCertificate required = CertificateRules.NeedsValues(cert.Type) ? await RequiredAsync(uow, lot.ItemId, ct) : null;
            MaterialSpec spec = null;
            if (required != null)
            {
                string g = CharacteristicRules.Key(cert.Grade), st = CharacteristicRules.Key(cert.Standard);
                // the required specifications first (the certificate must be one of them); only an item without any requirement falls back to the approved specification of the printed grade and standard
                spec = required.Specs.Count > 0
                    ? (g.Length == 0 ? null : required.Specs.FirstOrDefault(x => CharacteristicRules.Key(x.Grade) == g && CharacteristicRules.Key(x.Standard) == st))
                    : await FindSpecAsync(uow, lot.ItemId, cert.Grade, cert.Standard, ct);
            }
            var values = cert.Values.Select(v => new JudgeValue { Name = v.Name, Kind = v.Kind, Value = v.Value }).ToList();
            CertificateEvaluation ev = CertificateRules.Evaluate(cert.Type, cert.HeatNumber, lot.HeatNumber, cert.Grade, cert.Standard, spec, values,
                cert.InspectorOrganisation, cert.InspectorName, cert.InspectorStampNo, cert.ManufacturerSignatory, required?.Specs);
            cert.SpecId = spec?.Id;
            cert.Evaluation = ev.Status;
            cert.EvaluationText = ev.Text.Length > 1000 ? ev.Text.Substring(0, 1000) : ev.Text;
            cert.EvaluatedAt = DateTimeOffset.Now;
            foreach (CertificateValue row in cert.Values)
            {
                JudgedRow judged = ev.Judgement?.Rows.FirstOrDefault(r => r.Kind == row.Kind && CharacteristicRules.Key(r.Name) == CharacteristicRules.Key(row.Name));
                row.Min = judged?.Min; row.Max = judged?.Max;
                row.Status = judged == null ? CertRowStatus.NotJudged : judged.Pass ? CertRowStatus.Pass : CertRowStatus.Fail;
            }
            return ev;
        }
    }

    public interface IMaterialSpecService
    {
        Task<ResponseDto<PagedList<MaterialSpecDto>>> GetAsync(MaterialSpecQuery query, CancellationToken ct);
        Task<ResponseDto<MaterialSpecDto>> GetOneAsync(int id, CancellationToken ct);
        Task<ResponseDto<MaterialSpecDto>> SaveAsync(SaveMaterialSpecDto dto, CancellationToken ct);
        Task<ResponseDto<MaterialSpecDto>> ActionAsync(SpActionDto dto, CancellationToken ct);
        Task<ResponseDto<RequiredCertificateDto>> RequiredAsync(int itemId, CancellationToken ct);
        Task<ResponseDto<CertificateSheetDto>> SheetAsync(int certificateId, CancellationToken ct);
        ResponseDto<byte[]> CertificateTemplate();
        Task<ResponseDto<CertExcelResultDto>> ImportValuesAsync(CertExcelRequestDto request, byte[] file, CancellationToken ct);
    }

    /// <summary>
    /// Package C (H8): the material specification (grade + standard) with its limits, the required certificate type and the items bought to it. Engineering writes it, a second person (QA or QC, never an editor,
    /// never an AI seat) approves it, a correction is a new version. Also the derived certificate requirement of an item, the verification sheet of a certificate and the Excel import of certificate values.
    /// </summary>
    public class MaterialSpecService : IMaterialSpecService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IMaterialService _material;

        public const string Makers = "ENGINEERING,ENGINEER";
        public const string Checkers = "QA,QC";
        public const string Voiders = "QA,PMO,ENGINEERING,ENGINEER";

        public MaterialSpecService(IUnitOfWork uow, IInventoryUserContext user, IMaterialService material)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _material = material ?? throw new ArgumentNullException(nameof(material));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);
        private static bool IsAi => Ai.AiActing.IsActing;

        internal static List<int> Editors(string text) =>
            (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out int v) ? v : 0).Where(v => v > 0).Distinct().ToList();

        internal static string WithEditor(string existing, int id)
        {
            var ids = (existing ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            string s = id.ToString(CultureInfo.InvariantCulture);
            if (!ids.Contains(s)) ids.Add(s);
            return string.Join(",", ids);
        }

        internal static string Hash(MaterialSpec spec)
        {
            var sb = new StringBuilder();
            sb.Append(spec.SpecKey).Append('|').Append((int)spec.RequiredCertificateType).Append('|').Append(spec.Version);
            foreach (MaterialSpecLimit l in spec.Limits.OrderBy(x => x.Kind).ThenBy(x => CharacteristicRules.Key(x.Name)))
                sb.Append('|').Append((int)l.Kind).Append(':').Append(CharacteristicRules.Key(l.Name)).Append(':').Append(l.Min?.ToString("0.####", CultureInfo.InvariantCulture)).Append(':')
                  .Append(l.Max?.ToString("0.####", CultureInfo.InvariantCulture)).Append(':').Append(l.Required ? 1 : 0);
            foreach (int id in spec.Items.Select(x => x.ItemId).OrderBy(x => x)) sb.Append("|i").Append(id);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        private async Task<MaterialSpecDto> ToDtoAsync(MaterialSpec s, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { s.CreatorUserId, s.ApprovedByUserId }, ct);
            List<int> itemIds = s.Items.Select(i => i.ItemId).ToList();
            var items = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.Code, i.Title }).ToListAsync(ct);
            var dto = new MaterialSpecDto
            {
                Id = s.Id, Grade = s.Grade, Standard = s.Standard, SpecKey = s.SpecKey, Title = s.Title, RequiredCertificateType = s.RequiredCertificateType,
                RequiredCertificateTypeText = CertificateRules.TypeText(s.RequiredCertificateType), Version = s.Version, Status = s.Status, Reason = s.Reason, SupersedesId = s.SupersedesId,
                CreatorUserId = s.CreatorUserId, CreatorName = names.Name(s.CreatorUserId), CreateDate = s.CreateDate, SubmittedAt = s.SubmittedAt, ApprovedByName = names.Name(s.ApprovedByUserId),
                ApprovedAt = s.ApprovedAt, VoidReason = s.VoidReason, ContentHash = s.ContentHash, RowVersion = s.RowVersion == null ? null : Convert.ToBase64String(s.RowVersion),
                Limits = s.Limits.OrderBy(l => l.SortOrder).Select(l => new MaterialSpecLimitDto { Id = l.Id, SortOrder = l.SortOrder, Kind = l.Kind, Name = l.Name, Unit = l.Unit, Min = l.Min, Max = l.Max, Required = l.Required }).ToList(),
                Items = s.Items.Select(i => new MaterialSpecItemDto { ItemId = i.ItemId, ItemCode = items.FirstOrDefault(x => x.Id == i.ItemId)?.Code, ItemTitle = items.FirstOrDefault(x => x.Id == i.ItemId)?.Title }).ToList(),
            };
            bool editor = Editors(s.EditorUserIds).Contains(Me) || s.CreatorUserId == Me;
            if (s.Status == MatSpecStatus.Draft && _user.IsInRole(Makers)) { dto.Actions.Add("edit"); dto.Actions.Add("submit"); }
            if (s.Status == MatSpecStatus.Submitted && _user.IsInRole(Checkers) && !editor && !IsAi) { dto.Actions.Add("approve"); dto.Actions.Add("return"); }
            if ((s.Status == MatSpecStatus.Draft || s.Status == MatSpecStatus.Submitted || s.Status == MatSpecStatus.Approved) && _user.IsInRole(Voiders)) dto.Actions.Add("void");
            if (s.Status == MatSpecStatus.Approved && _user.IsInRole(Makers)) dto.Actions.Add("new-version");
            return dto;
        }

        private IQueryable<MaterialSpec> Full() => _uow.Set<MaterialSpec>().Include(s => s.Limits).Include(s => s.Items);

        public async Task<ResponseDto<PagedList<MaterialSpecDto>>> GetAsync(MaterialSpecQuery query, CancellationToken ct)
        {
            query ??= new MaterialSpecQuery();
            IQueryable<MaterialSpec> rows = Full().AsNoTracking().Where(s => !s.Deleted);
            if (query.Status.HasValue) rows = rows.Where(s => s.Status == query.Status.Value);
            if (query.ItemId.HasValue) rows = rows.Where(s => s.Items.Any(i => i.ItemId == query.ItemId.Value));
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(s => s.Grade.Contains(text) || s.Standard.Contains(text) || s.Title.Contains(text));
            int size = query.PageSize <= 0 ? 50 : Math.Min(query.PageSize, 200), number = Math.Max(1, query.PageNumber);
            int total = await rows.CountAsync(ct);
            List<MaterialSpec> list = await rows.OrderBy(s => s.Grade).ThenByDescending(s => s.Version).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            var dtos = new List<MaterialSpecDto>();
            foreach (MaterialSpec s in list) dtos.Add(await ToDtoAsync(s, ct));
            PagedList<MaterialSpecDto> page = await PagedList<MaterialSpecDto>.CreateAsync(dtos, 1, 0, ct);
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size;
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<MaterialSpecDto>> GetOneAsync(int id, CancellationToken ct)
        {
            MaterialSpec s = await Full().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (s == null) return QualityText.NotFound<MaterialSpecDto>("مشخصات ماده پیدا نشد.");
            return ResponseDtoHelper.Ok(await ToDtoAsync(s, ct));
        }

        private static string ValidateLimits(List<MaterialSpecLimitDto> limits)
        {
            if (limits == null || limits.Count == 0) return "دست‌کم یک حد شیمیایی یا مکانیکی لازم است.";
            if (limits.Count > 80) return "حداکثر ۸۰ ردیف حد.";
            var seen = new HashSet<string>();
            foreach (MaterialSpecLimitDto l in limits)
            {
                if (!Enum.IsDefined(typeof(CertValueKind), l.Kind)) return "نوع ردیف (شیمیایی یا مکانیکی) نامعتبر است.";
                string name = Clean(l.Name);
                if (name == null || name.Length > 60) return "نام هر ردیف الزامی است (حداکثر ۶۰ نویسه).";
                if (!seen.Add((int)l.Kind + "|" + CharacteristicRules.Key(name))) return $"ردیف «{name}» تکراری است.";
                if (!l.Min.HasValue && !l.Max.HasValue) return $"ردیف «{name}» نه حداقل دارد نه حداکثر؛ حد را از استاندارد بنویسید.";
                if (l.Min.HasValue && l.Max.HasValue && l.Min.Value > l.Max.Value) return $"حداقل «{name}» از حداکثر آن بیشتر است.";
                if ((l.Unit ?? string.Empty).Length > 30) return $"واحد «{name}» بلند است.";
            }
            return null;
        }

        public async Task<ResponseDto<MaterialSpecDto>> SaveAsync(SaveMaterialSpecDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<MaterialSpecDto>(QualityText.InvalidData);
            if (!_user.IsInRole(Makers)) return Bad<MaterialSpecDto>(QualityText.NotAllowed + " (مشخصات ماده را مهندسی می‌نویسد)");
            if (IsAi) return Bad<MaterialSpecDto>("مشخصات ماده را انسان ثبت می‌کند؛ صندلی هوش مصنوعی آن را نمی‌نویسد.");
            if (!Enum.IsDefined(typeof(CertificateType), dto.RequiredCertificateType)) return Bad<MaterialSpecDto>("نوع گواهی نامعتبر است.");
            string grade = Clean(dto.Grade), standard = Clean(dto.Standard);
            if (grade == null || grade.Length > 100) return Bad<MaterialSpecDto>("گرید ماده را بنویسید.");
            if (standard != null && standard.Length > 100) return Bad<MaterialSpecDto>("استاندارد بلند است.");
            string problem = ValidateLimits(dto.Limits);
            if (problem != null) return Bad<MaterialSpecDto>(problem);
            List<int> itemIds = (dto.ItemIds ?? new List<int>()).Distinct().ToList();
            if (itemIds.Count > 0)
            {
                int found = await _uow.Set<InventoryItem>().CountAsync(i => itemIds.Contains(i.Id) && !i.Deleted, ct);
                if (found != itemIds.Count) return Bad<MaterialSpecDto>("یکی از کالاهای فهرست پیدا نشد.");
            }
            string key = SpecialProcessRules.SpecKey(grade, standard);
            DateTimeOffset now = DateTimeOffset.Now;
            MaterialSpec spec;
            if (dto.Id.HasValue)
            {
                spec = await Full().FirstOrDefaultAsync(s => s.Id == dto.Id.Value && !s.Deleted, ct);
                if (spec == null) return QualityText.NotFound<MaterialSpecDto>("مشخصات ماده پیدا نشد.");
                if (spec.Status != MatSpecStatus.Draft) return Bad<MaterialSpecDto>("فقط پیش‌نویس ویرایش می‌شود؛ برای اصلاح، نسخه تازه بسازید.");
                if (key != spec.SpecKey) return Bad<MaterialSpecDto>("گرید و استاندارد نسخه ثبت‌شده تغییر نمی‌کند؛ مشخصات تازه بسازید.");
                if (!string.IsNullOrEmpty(dto.RowVersion))
                    try { _uow.Entry(spec).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<MaterialSpecDto>(QualityText.InvalidData); }
                _uow.Set<MaterialSpecLimit>().RemoveRange(spec.Limits.ToList());
                _uow.Set<MaterialSpecItem>().RemoveRange(spec.Items.ToList());
                spec.Limits.Clear(); spec.Items.Clear();
            }
            else
            {
                int version = 1;
                int? supersedes = null;
                string reason = Clean(dto.Reason);
                if (dto.NewVersionOfId.HasValue)
                {
                    MaterialSpec old = await _uow.Set<MaterialSpec>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == dto.NewVersionOfId.Value && !s.Deleted, ct);
                    if (old == null) return QualityText.NotFound<MaterialSpecDto>("نسخه پایه پیدا نشد.");
                    if (old.SpecKey != key) return Bad<MaterialSpecDto>("نسخه تازه باید همان گرید و استاندارد باشد.");
                    if (reason == null || reason.Length < 10) return Bad<MaterialSpecDto>("دلیل نسخه تازه را بنویسید (دست‌کم ۱۰ نویسه).");
                    supersedes = old.Id;
                }
                if (await _uow.Set<MaterialSpec>().AnyAsync(s => !s.Deleted && s.SpecKey == key && (s.Status == MatSpecStatus.Draft || s.Status == MatSpecStatus.Submitted), ct))
                    return Bad<MaterialSpecDto>("برای این گرید و استاندارد یک نسخه در جریان (پیش‌نویس یا در انتظار تایید) هست.");
                int? last = await _uow.Set<MaterialSpec>().Where(s => s.SpecKey == key).Select(s => (int?)s.Version).MaxAsync(ct);
                version = (last ?? 0) + 1;
                if (version > 1 && !dto.NewVersionOfId.HasValue) return Bad<MaterialSpecDto>("برای این گرید و استاندارد مشخصات ثبت شده است؛ نسخه تازه بسازید.");
                spec = new MaterialSpec { SpecKey = key, Version = version, SupersedesId = supersedes, Status = MatSpecStatus.Draft, CreateDate = now, CreatorUserId = Me };
                _uow.Set<MaterialSpec>().Add(spec);
            }
            spec.Grade = grade; spec.Standard = standard; spec.Title = Clean(dto.Title); spec.RequiredCertificateType = dto.RequiredCertificateType;
            if (Clean(dto.Reason) != null) spec.Reason = Clean(dto.Reason);
            int order = 0;
            foreach (MaterialSpecLimitDto l in dto.Limits)
                spec.Limits.Add(new MaterialSpecLimit { SortOrder = order++, Kind = l.Kind, Name = Clean(l.Name), Unit = Clean(l.Unit), Min = l.Min, Max = l.Max, Required = l.Required });
            foreach (int id in itemIds) spec.Items.Add(new MaterialSpecItem { ItemId = id });
            spec.EditorUserIds = WithEditor(spec.EditorUserIds, Me);
            spec.ContentHash = Hash(spec);
            spec.ModifyDate = now; spec.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<MaterialSpecDto>(QualityText.Concurrency); }
            catch (DbUpdateException) { return Bad<MaterialSpecDto>("برای این گرید و استاندارد هم‌زمان نسخه دیگری ثبت شد."); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(spec, ct));
        }

        public async Task<ResponseDto<MaterialSpecDto>> ActionAsync(SpActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<MaterialSpecDto>(QualityText.InvalidData);
            MaterialSpec s = await Full().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (s == null) return QualityText.NotFound<MaterialSpecDto>("مشخصات ماده پیدا نشد.");
            if (!string.IsNullOrEmpty(dto.RowVersion))
                try { _uow.Entry(s).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<MaterialSpecDto>(QualityText.InvalidData); }
            string note = Clean(dto.Note);
            DateTimeOffset now = DateTimeOffset.Now;
            bool editor = Editors(s.EditorUserIds).Contains(Me) || s.CreatorUserId == Me;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "submit":
                    if (!_user.IsInRole(Makers)) return Bad<MaterialSpecDto>(QualityText.NotAllowed);
                    if (s.Status != MatSpecStatus.Draft) return Bad<MaterialSpecDto>("فقط پیش‌نویس ارسال می‌شود.");
                    if (ValidateLimits(s.Limits.Select(l => new MaterialSpecLimitDto { Kind = l.Kind, Name = l.Name, Unit = l.Unit, Min = l.Min, Max = l.Max }).ToList()) is string bad) return Bad<MaterialSpecDto>(bad);
                    s.Status = MatSpecStatus.Submitted; s.SubmittedAt = now;
                    break;
                case "approve":
                case "return":
                    bool approve = dto.Action.Trim().Equals("approve", StringComparison.OrdinalIgnoreCase);
                    if (!_user.IsInRole(Checkers)) return Bad<MaterialSpecDto>(QualityText.NotAllowed);
                    if (IsAi) return Bad<MaterialSpecDto>("تایید مشخصات ماده با انسان است؛ صندلی هوش مصنوعی آن را تایید نمی‌کند.");
                    if (s.Status != MatSpecStatus.Submitted) return Bad<MaterialSpecDto>("فقط مشخصات ارسال‌شده تصمیم می‌گیرد.");
                    if (editor) return Bad<MaterialSpecDto>(QualityText.SameUser + " (نویسنده یا ویرایشگر مشخصات نمی‌تواند آن را تایید کند)");
                    if (!approve && note == null) return Bad<MaterialSpecDto>("دلیل بازگشت را بنویسید.");
                    if (approve) return await ApproveSpecAsync(s.Id, now, ct);
                    else { s.Status = MatSpecStatus.Draft; s.Reason = note; }
                    break;
                case "void":
                    if (!_user.IsInRole(Voiders)) return Bad<MaterialSpecDto>(QualityText.NotAllowed);
                    if (s.Status != MatSpecStatus.Draft && s.Status != MatSpecStatus.Submitted && s.Status != MatSpecStatus.Approved) return Bad<MaterialSpecDto>("این نسخه باطل نمی‌شود.");
                    if (note == null) return Bad<MaterialSpecDto>("دلیل ابطال را بنویسید.");
                    s.Status = MatSpecStatus.Void; s.VoidReason = note;
                    break;
                default:
                    return Bad<MaterialSpecDto>("عمل نامعتبر است (submit، approve، return یا void).");
            }
            s.ModifyDate = now; s.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<MaterialSpecDto>(QualityText.Concurrency); }
            catch (DbUpdateException) { return Bad<MaterialSpecDto>("برای این گرید و استاندارد هم‌زمان نسخه تاییدشده دیگری ثبت شد."); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(s, ct));
        }

        /// <summary>
        /// Fix round 1 (M5): the old approved version becomes Superseded and the new one Approved as one unit; a failure between the two saves never leaves the grade without an approved specification (every receipt of it would block).
        /// </summary>
        private async Task<ResponseDto<MaterialSpecDto>> ApproveSpecAsync(int id, DateTimeOffset now, CancellationToken ct)
        {
            return await QualityChecks.InTransactionAsync(_uow, async () =>
            {
                MaterialSpec s = await Full().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
                if (s == null) return QualityText.NotFound<MaterialSpecDto>("مشخصات ماده پیدا نشد.");
                if (s.Status != MatSpecStatus.Submitted) return Bad<MaterialSpecDto>("فقط مشخصات ارسال‌شده تصمیم می‌گیرد.");
                try
                {
                    s.ContentHash = Hash(s);
                    List<MaterialSpec> olds = await _uow.Set<MaterialSpec>().Where(x => !x.Deleted && x.SpecKey == s.SpecKey && x.Status == MatSpecStatus.Approved && x.Id != s.Id).ToListAsync(ct);
                    if (olds.Count > 0)
                    {
                        foreach (MaterialSpec old in olds) { old.Status = MatSpecStatus.Superseded; old.ModifyDate = now; old.ModifierUserId = Me; }
                        await _uow.SaveChangesAsync(ct);
                    }
                    s.Status = MatSpecStatus.Approved; s.ApprovedByUserId = Me; s.ApprovedAt = now; s.ModifyDate = now; s.ModifierUserId = Me;
                    await _uow.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException) { return Bad<MaterialSpecDto>(QualityText.Concurrency); }
                catch (DbUpdateException) { return Bad<MaterialSpecDto>("برای این گرید و استاندارد هم‌زمان نسخه تاییدشده دیگری ثبت شد."); }
                return ResponseDtoHelper.Ok(await ToDtoAsync(s, ct));
            }, r => r.Success, ct);
        }

        public async Task<ResponseDto<RequiredCertificateDto>> RequiredAsync(int itemId, CancellationToken ct)
        {
            if (!await _uow.Set<InventoryItem>().AnyAsync(i => i.Id == itemId && !i.Deleted, ct)) return QualityText.NotFound<RequiredCertificateDto>("کالا پیدا نشد.");
            RequiredCertificate required = await CertificateEngine.RequiredAsync(_uow, itemId, ct);
            var dto = new RequiredCertificateDto { ItemId = itemId, Type = required.Type, TypeText = required.Type.HasValue ? CertificateRules.TypeText(required.Type.Value) : null, Sources = required.Sources };
            foreach (MaterialSpec s in required.Specs) dto.Specs.Add(await ToDtoAsync(s, ct));
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<CertificateSheetDto>> SheetAsync(int certificateId, CancellationToken ct)
        {
            ResponseDto<MaterialCertificateDto> cert = await _material.GetCertificateAsync(certificateId, ct);
            if (!cert.Success || cert.Data == null) return Bad<CertificateSheetDto>(cert.Message ?? "گواهی پیدا نشد.");
            var sheet = new CertificateSheetDto { Certificate = cert.Data };
            if (cert.Data.SpecId.HasValue)
            {
                ResponseDto<MaterialSpecDto> spec = await GetOneAsync(cert.Data.SpecId.Value, ct);
                sheet.Spec = spec.Data;
            }
            ResponseDto<RequiredCertificateDto> required = await RequiredAsync(cert.Data.ItemId, ct);
            sheet.Required = required.Data;
            sheet.Receipts = await (from d in _uow.Set<InventoryDocument>().AsNoTracking()
                                    join l in _uow.Set<InventoryDocumentLine>().AsNoTracking() on d.Id equals l.DocumentId
                                    where !d.Deleted && l.LotId == cert.Data.LotId
                                    select d.Number).Distinct().Take(10).ToListAsync(ct);
            if (cert.Data.Evaluation != CertRowStatus.Pass && CertificateRules.NeedsValues(cert.Data.Type)) sheet.Problems.Add(cert.Data.EvaluationText ?? "گواهی هنوز سنجیده نشده است.");
            if (required.Data?.Type != null && cert.Data.Type < required.Data.Type.Value) sheet.Problems.Add($"نوع گواهی ({CertificateRules.TypeText(cert.Data.Type)}) کمتر از نوع لازم ({required.Data.TypeText}) است.");
            if (cert.Data.Result != CertificateResult.Accepted) sheet.Problems.Add("گواهی هنوز توسط QC تایید نشده است.");
            sheet.ReceiptAcceptable = sheet.Problems.Count == 0;
            return ResponseDtoHelper.Ok(sheet);
        }

        // ------------------------------------------------------------------ Excel

        private static readonly string[] Header = { "نوع (شیمیایی یا مکانیکی)", "نام", "واحد", "مقدار" };

        public ResponseDto<byte[]> CertificateTemplate()
        {
            var sheet = new FinanceXlsxSheet { Name = "مقادیر گواهی", Header = Header.ToList() };
            sheet.Rows.Add(new object[] { "شیمیایی", "C", "%", 0.18m });
            sheet.Rows.Add(new object[] { "مکانیکی", "Yield", "MPa", 360m });
            var guide = new FinanceXlsxSheet { Name = "راهنما", Header = new List<string> { "راهنما" } };
            guide.Rows.Add(new object[] { "ردیف‌های نمونه را پاک کنید و مقادیر چاپ‌شده روی گواهی را از ردیف دوم بنویسید؛ ردیف عنوان را تغییر ندهید." });
            guide.Rows.Add(new object[] { "نام باید همان نام ردیف مشخصات ماده باشد (مثلاً C، Mn، Yield، Tensile). سامانه خودش حد را از مشخصات تاییدشده می‌گیرد و قبول یا رد را حساب می‌کند." });
            return ResponseDtoHelper.Ok(SimpleXlsx.Build(new List<FinanceXlsxSheet> { sheet, guide }, true));
        }

        private static bool TryNumber(string text, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var sb = new StringBuilder();
            foreach (char ch in text.Trim())
            {
                if (ch >= '۰' && ch <= '۹') sb.Append((char)('0' + (ch - '۰')));
                else if (ch >= '٠' && ch <= '٩') sb.Append((char)('0' + (ch - '٠')));
                else if (ch == '٫') sb.Append('.');
                else if (ch == '،') return false;
                else if (ch == '−' || ch == '–') sb.Append('-');
                else sb.Append(ch);
            }
            return decimal.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryKind(string text, out CertValueKind kind)
        {
            string k = CharacteristicRules.Key(text);
            kind = CertValueKind.Chemistry;
            if (k == CharacteristicRules.Key("شیمیایی") || k == CharacteristicRules.Key("chemistry") || k == CharacteristicRules.Key("chemical")) return true;
            if (k == CharacteristicRules.Key("مکانیکی") || k == CharacteristicRules.Key("mechanical")) { kind = CertValueKind.Mechanical; return true; }
            return false;
        }

        public async Task<ResponseDto<CertExcelResultDto>> ImportValuesAsync(CertExcelRequestDto request, byte[] file, CancellationToken ct)
        {
            if (request == null || file == null || file.Length == 0) return Bad<CertExcelResultDto>("فایل را انتخاب کنید.");
            if (!_user.IsInRole(QualityRoles.CertificateWriters)) return Bad<CertExcelResultDto>(QualityText.NotAllowed);
            MaterialCertificate cert = await _uow.Set<MaterialCertificate>().Include(c => c.Values).Include(c => c.Lot).FirstOrDefaultAsync(c => c.Id == request.CertificateId && !c.Deleted, ct);
            if (cert == null) return QualityText.NotFound<CertExcelResultDto>("گواهی پیدا نشد.");
            if (cert.Result != CertificateResult.Pending) return Bad<CertExcelResultDto>("گواهی بررسی‌شده ویرایش نمی‌شود؛ گواهی جدید ثبت کنید.");
            var cells = new List<(int Row, string[] Cells)>();
            try
            {
                List<string> names = XlsxReader.SheetNames(file);
                Office.XlsxSheet sheet = XlsxReader.Read(file, names.FirstOrDefault(), 202);
                XlsxRow head = sheet.Rows.FirstOrDefault(x => x.RowNumber == 1);
                for (int c = 0; c < Header.Length; c++)
                    if (head == null || CharacteristicRules.Key(head.Cell(c)?.AsText()) != CharacteristicRules.Key(Header[c])) return Bad<CertExcelResultDto>("سطر عنوان فایل با قالب مقادیر گواهی یکی نیست؛ قالب را از سامانه بگیرید.");
                foreach (XlsxRow r in sheet.Rows.Where(x => x.RowNumber > 1).OrderBy(x => x.RowNumber))
                {
                    var arr = new string[Header.Length];
                    for (int c = 0; c < arr.Length; c++) arr[c] = r.Cell(c)?.AsText();
                    if (arr.All(string.IsNullOrWhiteSpace)) continue;
                    cells.Add((r.RowNumber, arr));
                }
            }
            catch (XlsxFormatException ex) { return Bad<CertExcelResultDto>(ex.Message); }
            catch (Exception) { return Bad<CertExcelResultDto>("فایل xlsx خوانده نشد."); }
            if (cells.Count == 0) return Bad<CertExcelResultDto>("فایل هیچ ردیف داده‌ای ندارد.");
            if (cells.Count > 200) return Bad<CertExcelResultDto>("حداکثر ۲۰۰ ردیف در هر بار.");

            var result = new CertExcelResultDto { Preview = request.Preview, RowCount = cells.Count };
            var good = new List<CertificateValue>();
            var seen = new HashSet<string>();
            foreach ((int row, string[] a) in cells)
            {
                string name = Clean(a[1]);
                string message = null;
                if (!TryKind(a[0], out CertValueKind kind)) message = "نوع باید «شیمیایی» یا «مکانیکی» باشد.";
                else if (name == null || name.Length > 60) message = "نام ردیف الزامی است (حداکثر ۶۰ نویسه).";
                else if (!TryNumber(a[3], out decimal value)) message = "مقدار عدد نیست.";
                else if (!seen.Add((int)kind + "|" + CharacteristicRules.Key(name))) message = "این ردیف در فایل تکراری است.";
                else
                {
                    TryNumber(a[3], out value);
                    good.Add(new CertificateValue { Kind = kind, Name = name, Unit = Clean(a[2]), Value = value });
                }
                result.Rows.Add(new CertExcelRowDto { Row = row, Name = name, Ok = message == null, Message = message });
            }
            if (good.Any(g => Math.Abs(g.Value) >= SpecialProcessRules.MaxNumber)) return Bad<CertExcelResultDto>("یکی از مقدارهای فایل بیش از حد مجاز بزرگ است.");   // Fix round 1 (Low)
            result.ValidRows = result.Rows.Count(r => r.Ok);
            result.ErrorRows = result.Rows.Count - result.ValidRows;
            if (result.ErrorRows > 0) { result.Warnings.Add("فایل ردیف نامعتبر دارد؛ هیچ مقداری ثبت نشد."); return ResponseDtoHelper.Ok(result); }
            if (request.Preview) return ResponseDtoHelper.Ok(result);

            _uow.Set<CertificateValue>().RemoveRange(cert.Values.ToList());
            cert.Values.Clear();
            int order = 0;
            foreach (CertificateValue v in good) { v.SortOrder = order++; cert.Values.Add(v); }
            await CertificateEngine.EvaluateAsync(_uow, cert, cert.Lot, ct);
            cert.EditorUserIds = WithEditor(cert.EditorUserIds, Me);   // Fix round 1 (M6)
            cert.ModifyDate = DateTimeOffset.Now; cert.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<CertExcelResultDto>(QualityText.Concurrency); }
            ResponseDto<MaterialCertificateDto> dto = await _material.GetCertificateAsync(cert.Id, ct);
            result.Certificate = dto.Data;
            return ResponseDtoHelper.Ok(result);
        }
    }
}
