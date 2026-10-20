using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Quality
{
    public interface IDocumentControlService
    {
        Task<ResponseDto<PagedList<ControlledDocumentListDto>>> GetDocumentsAsync(ControlledDocumentQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<ControlledDocumentListDto>>> GetMasterListAsync(ControlledDocumentQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ControlledDocumentDto>> GetDocumentAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<CodeCheckDto>> CheckCodeAsync(ControlledDocumentType type, string department, int? serial, CancellationToken cancellationToken);
        Task<ResponseDto<ControlledDocumentDto>> SaveDocumentAsync(SaveControlledDocumentDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ControlledDocumentDto>> SaveRevisionAsync(SaveRevisionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ControlledDocumentDto>> RevisionActionAsync(RevisionActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<RetentionRuleDto>>> GetRetentionAsync(CancellationToken cancellationToken);
        Task<ResponseDto<List<RetentionRuleDto>>> SaveRetentionAsync(SaveRetentionRuleDto dto, CancellationToken cancellationToken);
    }

    /// <summary>Controlled documents and records retention (ISO 9001 7.5, ISO 15489). A revision needs three different people.</summary>
    public class DocumentControlService : IDocumentControlService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public DocumentControlService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);

        private static string Code(ControlledDocumentType type, string department, int serial) => $"{type}-{department}-{serial:000}";

        // -------------------------------------------------------------------------------- retention

        private static readonly (string Type, int? Years, bool Permanent, string Source)[] RetentionSeed =
        {
            ("NCR", 10, false, "★ تصمیم موقت"),
            ("CAPA", 10, false, "★ تصمیم موقت"),
            ("TestReport", null, true, "★ عمر محصول"),
            ("InspectionRecord", null, true, "★ عمر محصول"),
            ("FinalDataBook", null, true, "★ عمر محصول"),
            ("Calibration", 10, false, "★ تصمیم موقت"),
            ("Audit", 10, false, "★ تصمیم موقت"),
            ("ManagementReview", 10, false, "★ تصمیم موقت"),
            ("DCF", null, true, "★ تصمیم موقت"),
            ("Drawing", null, true, "★ تصمیم موقت"),
        };

        private async Task EnsureRetentionAsync(CancellationToken cancellationToken)
        {
            DbSet<RetentionRule> set = _uow.Set<RetentionRule>();
            List<string> have = await set.Select(r => r.RecordType).ToListAsync(cancellationToken);
            bool added = false;
            foreach (var seed in RetentionSeed.Where(s => !have.Contains(s.Type)))
            {
                set.Add(new RetentionRule { RecordType = seed.Type, Years = seed.Years, Permanent = seed.Permanent, Source = seed.Source });
                added = true;
            }
            if (added) await _uow.SaveChangesAsync(cancellationToken);
        }

        private async Task<List<RetentionRuleDto>> RulesAsync(CancellationToken cancellationToken) =>
            await _uow.Set<RetentionRule>().OrderBy(r => r.RecordType)
                .Select(r => new RetentionRuleDto { Id = r.Id, RecordType = r.RecordType, Years = r.Years, Permanent = r.Permanent, Source = r.Source }).ToListAsync(cancellationToken);

        public async Task<ResponseDto<List<RetentionRuleDto>>> GetRetentionAsync(CancellationToken cancellationToken)
        {
            await EnsureRetentionAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await RulesAsync(cancellationToken));
        }

        public async Task<ResponseDto<List<RetentionRuleDto>>> SaveRetentionAsync(SaveRetentionRuleDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.RecordType) == null) return Bad<List<RetentionRuleDto>>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.DocumentApprovers)) return Bad<List<RetentionRuleDto>>(QualityText.NotAllowed);
            if (!dto.Permanent && (!dto.Years.HasValue || dto.Years.Value < 1 || dto.Years.Value > 100)) return Bad<List<RetentionRuleDto>>("مدت نگهداری را به سال (۱ تا ۱۰۰) یا «دائمی» مشخص کنید.");
            await EnsureRetentionAsync(cancellationToken);
            string type = dto.RecordType.Trim();
            RetentionRule rule = await _uow.Set<RetentionRule>().FirstOrDefaultAsync(r => r.RecordType == type, cancellationToken);
            if (rule == null) { rule = new RetentionRule { RecordType = type }; _uow.Set<RetentionRule>().Add(rule); }
            rule.Permanent = dto.Permanent; rule.Years = dto.Permanent ? (int?)null : dto.Years; rule.Source = Clean(dto.Source) ?? "تعیین‌شده توسط شرکت";
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await RulesAsync(cancellationToken));
        }

        // -------------------------------------------------------------------------------- documents

        private static ControlledDocumentListDto ToList(ControlledDocument d) => new ControlledDocumentListDto
        {
            Id = d.Id, Code = d.Code, Type = d.Type, Department = d.Department, Title = d.Title, CurrentRevision = d.CurrentRevision, Status = d.Status,
            EffectiveDate = d.EffectiveDate, RetentionClass = d.RetentionClass, ResponsibleUnit = d.ResponsibleUnit,
        };

        private IQueryable<ControlledDocument> Filter(ControlledDocumentQuery query)
        {
            IQueryable<ControlledDocument> rows = _uow.Set<ControlledDocument>().Where(d => !d.Deleted);
            if (query.Type.HasValue) rows = rows.Where(d => d.Type == query.Type.Value);
            if (query.Status.HasValue) rows = rows.Where(d => d.Status == query.Status.Value);
            string dept = QualityText.Upper(query.Department);
            if (dept != null) rows = rows.Where(d => d.Department == dept);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(d => d.Code.Contains(text) || d.Title.Contains(text));
            return rows;
        }

        private async Task<ResponseDto<PagedList<ControlledDocumentListDto>>> PageAsync(IQueryable<ControlledDocument> rows, ControlledDocumentQuery query, CancellationToken cancellationToken)
        {
            IQueryable<ControlledDocumentListDto> projection = rows.OrderBy(d => d.Code).Select(d => new ControlledDocumentListDto
            {
                Id = d.Id, Code = d.Code, Type = d.Type, Department = d.Department, Title = d.Title, CurrentRevision = d.CurrentRevision, Status = d.Status,
                EffectiveDate = d.EffectiveDate, RetentionClass = d.RetentionClass, ResponsibleUnit = d.ResponsibleUnit,
            });
            return ResponseDtoHelper.Ok(await PagedList<ControlledDocumentListDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public Task<ResponseDto<PagedList<ControlledDocumentListDto>>> GetDocumentsAsync(ControlledDocumentQuery query, CancellationToken cancellationToken) =>
            PageAsync(Filter(query ??= new ControlledDocumentQuery()), query, cancellationToken);

        /// <summary>The master list: documents with an effective revision (the only ones to be used on the floor).</summary>
        public Task<ResponseDto<PagedList<ControlledDocumentListDto>>> GetMasterListAsync(ControlledDocumentQuery query, CancellationToken cancellationToken) =>
            PageAsync(Filter(query ??= new ControlledDocumentQuery()).Where(d => d.Status == ControlledDocumentStatus.Effective), query, cancellationToken);

        public async Task<ResponseDto<ControlledDocumentDto>> GetDocumentAsync(int id, CancellationToken cancellationToken)
        {
            ControlledDocument d = await _uow.Set<ControlledDocument>().Include(x => x.Revisions).ThenInclude(r => r.Distributions)
                .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (d == null) return QualityText.NotFound<ControlledDocumentDto>("سند پیدا نشد.");
            var userIds = new List<int?>();
            foreach (DocumentRevisionRecord r in d.Revisions)
            {
                userIds.Add(r.PreparedByUserId); userIds.Add(r.ReviewedByUserId); userIds.Add(r.ApprovedByUserId);
                userIds.AddRange(r.Distributions.Select(x => x.RecipientUserId));
            }
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, userIds, cancellationToken);
            ControlledDocumentListDto l = ToList(d);
            return ResponseDtoHelper.Ok(new ControlledDocumentDto
            {
                Id = l.Id, Code = l.Code, Type = l.Type, Department = l.Department, Title = l.Title, CurrentRevision = l.CurrentRevision, Status = l.Status,
                EffectiveDate = l.EffectiveDate, RetentionClass = l.RetentionClass, ResponsibleUnit = l.ResponsibleUnit,
                Revisions = d.Revisions.OrderBy(r => r.Id).Select(r => new DocumentRevisionDto
                {
                    Id = r.Id, DocumentId = r.DocumentId, Revision = r.Revision, ChangeSummary = r.ChangeSummary, FileDocumentId = r.FileDocumentId, Status = r.Status,
                    PreparedByUserId = r.PreparedByUserId, PreparedByName = names.Name(r.PreparedByUserId), ReviewedByName = names.Name(r.ReviewedByUserId), ReviewedAt = r.ReviewedAt,
                    ApprovedByName = names.Name(r.ApprovedByUserId), ApprovedAt = r.ApprovedAt, EffectiveDate = r.EffectiveDate,
                    Distributions = r.Distributions.OrderBy(x => x.Id).Select(x => new DistributionDto
                    {
                        Id = x.Id, RecipientUserId = x.RecipientUserId, RecipientName = names.Name(x.RecipientUserId), RecipientUnit = x.RecipientUnit, SentAt = x.SentAt, AcknowledgedAt = x.AcknowledgedAt,
                    }).ToList(),
                }).ToList(),
            });
        }

        public async Task<ResponseDto<CodeCheckDto>> CheckCodeAsync(ControlledDocumentType type, string department, int? serial, CancellationToken cancellationToken)
        {
            string dept = QualityText.Upper(department);
            if (dept == null) return Bad<CodeCheckDto>("واحد (مثلاً QC) را وارد کنید.");
            List<string> codes = await _uow.Set<ControlledDocument>().Where(d => d.Type == type && d.Department == dept && !d.Deleted).Select(d => d.Code).ToListAsync(cancellationToken);
            int next = codes.Select(c => int.TryParse(c.Substring(c.LastIndexOf('-') + 1), out int n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
            string code = Code(type, dept, serial ?? next);
            return ResponseDtoHelper.Ok(new CodeCheckDto { Code = code, Exists = codes.Contains(code), NextFreeCode = Code(type, dept, next) });
        }

        public async Task<ResponseDto<ControlledDocumentDto>> SaveDocumentAsync(SaveControlledDocumentDto dto, CancellationToken cancellationToken)
        {
            string dept = dto == null ? null : QualityText.Upper(dto.Department);
            if (dto == null || dept == null || Clean(dto.Title) == null || !Enum.IsDefined(typeof(ControlledDocumentType), dto.Type)) return Bad<ControlledDocumentDto>("نوع، واحد و عنوان سند را وارد کنید.");
            if (!_user.IsInRole(QualityRoles.DocumentReviewers)) return Bad<ControlledDocumentDto>(QualityText.NotAllowed);
            if (dept.Length > 8 || dept.Any(ch => !char.IsLetterOrDigit(ch))) return Bad<ControlledDocumentDto>("کد واحد باید حروف انگلیسی کوتاه باشد (مثلاً QC).");
            DateTimeOffset now = DateTimeOffset.Now;
            ControlledDocument d;
            if (dto.Id.HasValue)
            {
                d = await _uow.Set<ControlledDocument>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (d == null) return QualityText.NotFound<ControlledDocumentDto>("سند پیدا نشد.");
            }
            else
            {
                CodeCheckDto check = (await CheckCodeAsync(dto.Type, dept, dto.Serial, cancellationToken)).Data;
                if (check.Exists) return Bad<ControlledDocumentDto>($"کد {check.Code} قبلاً ثبت شده است؛ کد آزاد بعدی: {check.NextFreeCode}.");
                d = new ControlledDocument { Code = check.Code, Type = dto.Type, Department = dept, Status = ControlledDocumentStatus.Draft, CreateDate = now, CreatorUserId = Me, Revisions = new List<DocumentRevisionRecord>() };
                _uow.Set<ControlledDocument>().Add(d);
            }
            d.Title = Clean(dto.Title); d.RetentionClass = Clean(dto.RetentionClass); d.ResponsibleUnit = Clean(dto.ResponsibleUnit);
            d.ModifyDate = now; d.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetDocumentAsync(d.Id, cancellationToken);
        }

        public async Task<ResponseDto<ControlledDocumentDto>> SaveRevisionAsync(SaveRevisionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Revision) == null || Clean(dto.ChangeSummary) == null) return Bad<ControlledDocumentDto>("شماره بازنگری و شرح تغییرات را وارد کنید.");
            if (!_user.IsInRole(QualityRoles.DocumentReviewers)) return Bad<ControlledDocumentDto>(QualityText.NotAllowed);
            ControlledDocument d = await _uow.Set<ControlledDocument>().Include(x => x.Revisions).FirstOrDefaultAsync(x => x.Id == dto.DocumentId && !x.Deleted, cancellationToken);
            if (d == null) return QualityText.NotFound<ControlledDocumentDto>("سند پیدا نشد.");
            string revision = dto.Revision.Trim();
            if (d.Revisions.Any(r => r.Revision == revision && !r.Deleted)) return Bad<ControlledDocumentDto>("این شماره بازنگری قبلاً ثبت شده است.");
            if (d.Revisions.Any(r => !r.Deleted && (r.Status == ControlledDocumentStatus.Draft || r.Status == ControlledDocumentStatus.InReview)))
                return Bad<ControlledDocumentDto>("یک بازنگری در جریان تایید دارد؛ ابتدا آن را تکمیل کنید.");
            DateTimeOffset now = DateTimeOffset.Now;
            d.Revisions.Add(new DocumentRevisionRecord
            {
                Revision = revision, ChangeSummary = Clean(dto.ChangeSummary), FileDocumentId = dto.FileDocumentId, Status = ControlledDocumentStatus.Draft,
                PreparedByUserId = Me, Distributions = new List<DocumentDistribution>(), CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            });
            d.ModifyDate = now; d.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetDocumentAsync(d.Id, cancellationToken);
        }

        public async Task<ResponseDto<ControlledDocumentDto>> RevisionActionAsync(RevisionActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<ControlledDocumentDto>(QualityText.InvalidData);
            DocumentRevisionRecord rev = await _uow.Set<DocumentRevisionRecord>().Include(r => r.Distributions).FirstOrDefaultAsync(r => r.Id == dto.Id && !r.Deleted, cancellationToken);
            if (rev == null) return QualityText.NotFound<ControlledDocumentDto>("بازنگری پیدا نشد.");
            ControlledDocument doc = await _uow.Set<ControlledDocument>().Include(d => d.Revisions).FirstAsync(d => d.Id == rev.DocumentId, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "review":
                    if (!_user.IsInRole(QualityRoles.DocumentReviewers)) return Bad<ControlledDocumentDto>(QualityText.NotAllowed);
                    if (rev.Status != ControlledDocumentStatus.Draft) return Bad<ControlledDocumentDto>("فقط بازنگری پیش‌نویس بررسی می‌شود.");
                    if (rev.PreparedByUserId == Me) return Bad<ControlledDocumentDto>(QualityText.SameUser + " (بررسی‌کننده نباید تهیه‌کننده باشد)");
                    rev.Status = ControlledDocumentStatus.InReview; rev.ReviewedByUserId = Me; rev.ReviewedAt = now;
                    doc.Status = doc.CurrentRevision == null ? ControlledDocumentStatus.InReview : doc.Status;
                    break;
                case "approve":
                    if (!_user.IsInRole(QualityRoles.DocumentApprovers)) return Bad<ControlledDocumentDto>(QualityText.NotAllowed + " (تایید سند با تضمین کیفی یا مدیریت است)");
                    if (rev.Status != ControlledDocumentStatus.InReview) return Bad<ControlledDocumentDto>("ابتدا بازنگری باید بررسی شود.");
                    if (rev.PreparedByUserId == Me || rev.ReviewedByUserId == Me) return Bad<ControlledDocumentDto>(QualityText.SameUser + " (تهیه، بررسی و تایید باید توسط سه نفر متفاوت باشد)");
                    if (rev.FileDocumentId == null) return Bad<ControlledDocumentDto>("فایل بازنگری را پیوست کنید.");
                    foreach (DocumentRevisionRecord old in doc.Revisions.Where(r => r.Id != rev.Id && r.Status == ControlledDocumentStatus.Effective))
                    {
                        old.Status = ControlledDocumentStatus.Obsolete; old.ModifyDate = now; old.ModifierUserId = Me;
                    }
                    rev.Status = ControlledDocumentStatus.Effective; rev.ApprovedByUserId = Me; rev.ApprovedAt = now; rev.EffectiveDate = (dto.EffectiveDate ?? DateTime.Today).Date;
                    doc.CurrentRevision = rev.Revision; doc.Status = ControlledDocumentStatus.Effective; doc.EffectiveDate = rev.EffectiveDate;
                    break;
                case "obsolete":
                    if (!_user.IsInRole(QualityRoles.DocumentApprovers)) return Bad<ControlledDocumentDto>(QualityText.NotAllowed);
                    if (rev.Status != ControlledDocumentStatus.Effective) return Bad<ControlledDocumentDto>("فقط بازنگری معتبر منسوخ می‌شود.");
                    rev.Status = ControlledDocumentStatus.Obsolete; doc.Status = ControlledDocumentStatus.Obsolete;
                    break;
                case "distribute":
                    if (!_user.IsInRole(QualityRoles.DocumentReviewers)) return Bad<ControlledDocumentDto>(QualityText.NotAllowed);
                    if (rev.Status != ControlledDocumentStatus.Effective) return Bad<ControlledDocumentDto>("فقط بازنگری معتبر توزیع می‌شود.");
                    List<int> userIds = (dto.RecipientUserIds ?? new List<int>()).Distinct().ToList();
                    List<string> units = (dto.RecipientUnits ?? new List<string>()).Select(Clean).Where(u => u != null).Distinct().ToList();
                    if (userIds.Count + units.Count == 0) return Bad<ControlledDocumentDto>("گیرنده را مشخص کنید.");
                    if (userIds.Count > 0 && await _uow.Set<User>().CountAsync(u => userIds.Contains(u.Id), cancellationToken) != userIds.Count) return Bad<ControlledDocumentDto>("کاربر گیرنده پیدا نشد.");
                    foreach (int id in userIds.Where(id => !rev.Distributions.Any(x => x.RecipientUserId == id)))
                        rev.Distributions.Add(new DocumentDistribution { RecipientUserId = id, SentAt = now });
                    foreach (string unit in units.Where(u => !rev.Distributions.Any(x => x.RecipientUnit == u)))
                        rev.Distributions.Add(new DocumentDistribution { RecipientUnit = unit, SentAt = now });
                    break;
                case "acknowledge":
                    DocumentDistribution row = rev.Distributions.FirstOrDefault(x => dto.DistributionId.HasValue ? x.Id == dto.DistributionId.Value : x.RecipientUserId == Me);
                    if (row == null) return QualityText.NotFound<ControlledDocumentDto>("توزیع پیدا نشد.");
                    if (row.RecipientUserId.HasValue && row.RecipientUserId.Value != Me) return Bad<ControlledDocumentDto>(QualityText.NotAllowed);
                    if (row.AcknowledgedAt.HasValue) return Bad<ControlledDocumentDto>("دریافت قبلاً تایید شده است.");
                    row.AcknowledgedAt = now; row.AcknowledgedByUserId = Me;
                    break;
                default:
                    return Bad<ControlledDocumentDto>("عملیات نامعتبر است.");
            }
            rev.ModifyDate = now; rev.ModifierUserId = Me; doc.ModifyDate = now; doc.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetDocumentAsync(doc.Id, cancellationToken);
        }
    }
}
