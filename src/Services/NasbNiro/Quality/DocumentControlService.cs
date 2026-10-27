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

        /// <summary>
        /// R1q D41: the revisions approved with a future effective date become effective on that day (the old revision becomes obsolete then, not before).
        /// Called by the nightly quality job and by every read of the documents, so the master list is right even if the server was off at night. Returns the number promoted.
        /// </summary>
        Task<int> PromoteScheduledAsync(CancellationToken cancellationToken);
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

        // Reading never writes (review L26): a revision whose day has come is shown as the one in force at once (below), but it is saved as effective by the nightly job only,
        // in a system context, not under the name of whoever opened the page.

        /// <summary>For the documents given, the scheduled revision whose effective day has come (the earliest one), which a reader sees as the revision in force.</summary>
        private async Task<Dictionary<int, DocumentRevisionRecord>> DueRevisionsAsync(IEnumerable<int> documentIds, CancellationToken cancellationToken)
        {
            DateTime today = DateTime.Today;
            List<int> ids = documentIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, DocumentRevisionRecord>();
            List<DocumentRevisionRecord> due = await _uow.Set<DocumentRevisionRecord>().AsNoTracking()
                .Where(r => ids.Contains(r.DocumentId) && !r.Deleted && r.Status == ControlledDocumentStatus.Scheduled && r.EffectiveDate != null && r.EffectiveDate <= today)
                .ToListAsync(cancellationToken);
            return due.Where(r => RevisionRules.TakesEffectNow(r.EffectiveDate.Value, today)).GroupBy(r => r.DocumentId)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.EffectiveDate).ThenBy(r => r.Id).First());
        }

        private async Task ShowDueRevisionsAsync(IEnumerable<ControlledDocumentListDto> rows, CancellationToken cancellationToken)
        {
            List<ControlledDocumentListDto> list = rows.ToList();
            Dictionary<int, DocumentRevisionRecord> due = await DueRevisionsAsync(list.Select(d => d.Id), cancellationToken);
            foreach (ControlledDocumentListDto row in list)
                if (due.TryGetValue(row.Id, out DocumentRevisionRecord rev))
                {
                    row.CurrentRevision = rev.Revision; row.Status = ControlledDocumentStatus.Effective; row.EffectiveDate = rev.EffectiveDate;
                }
        }

        public async Task<ResponseDto<PagedList<ControlledDocumentListDto>>> GetDocumentsAsync(ControlledDocumentQuery query, CancellationToken cancellationToken)
        {
            ResponseDto<PagedList<ControlledDocumentListDto>> page = await PageAsync(Filter(query ??= new ControlledDocumentQuery()), query, cancellationToken);
            if (page.Success) await ShowDueRevisionsAsync(page.Data, cancellationToken);
            return page;
        }

        /// <summary>The master list: documents with an effective revision (the only ones to be used on the floor).</summary>
        public async Task<ResponseDto<PagedList<ControlledDocumentListDto>>> GetMasterListAsync(ControlledDocumentQuery query, CancellationToken cancellationToken)
        {
            DateTime today = DateTime.Today;
            IQueryable<ControlledDocument> rows = Filter(query ??= new ControlledDocumentQuery()).Where(d => d.Status == ControlledDocumentStatus.Effective ||
                d.Revisions.Any(r => !r.Deleted && r.Status == ControlledDocumentStatus.Scheduled && r.EffectiveDate != null && r.EffectiveDate <= today));
            ResponseDto<PagedList<ControlledDocumentListDto>> page = await PageAsync(rows, query, cancellationToken);
            if (page.Success) await ShowDueRevisionsAsync(page.Data, cancellationToken);
            return page;
        }

        /// <summary>Saves the scheduled revisions whose day has come as effective (and the revision they replace as obsolete). The nightly job calls it; no read does (review L26).</summary>
        public async Task<int> PromoteScheduledAsync(CancellationToken cancellationToken)
        {
            DateTime today = DateTime.Today;
            List<int> documentIds = await _uow.Set<DocumentRevisionRecord>()
                .Where(r => !r.Deleted && r.Status == ControlledDocumentStatus.Scheduled && r.EffectiveDate != null && r.EffectiveDate <= today)
                .Select(r => r.DocumentId).Distinct().ToListAsync(cancellationToken);
            if (documentIds.Count == 0) return 0;
            DateTimeOffset now = DateTimeOffset.Now;
            int promoted = 0;
            foreach (ControlledDocument doc in await _uow.Set<ControlledDocument>().Include(d => d.Revisions).Where(d => documentIds.Contains(d.Id)).AsSplitQuery().ToListAsync(cancellationToken))
            {
                // a document has at most one scheduled revision (approval and new drafts are refused otherwise); the earliest one wins if data say otherwise
                DocumentRevisionRecord rev = doc.Revisions.Where(r => !r.Deleted && r.Status == ControlledDocumentStatus.Scheduled && r.EffectiveDate.HasValue && RevisionRules.TakesEffectNow(r.EffectiveDate.Value, today))
                    .OrderBy(r => r.EffectiveDate).ThenBy(r => r.Id).FirstOrDefault();
                if (rev == null) continue;
                foreach (DocumentRevisionRecord old in doc.Revisions.Where(r => r.Id != rev.Id && r.Status == ControlledDocumentStatus.Effective))
                {
                    old.Status = ControlledDocumentStatus.Obsolete; old.ModifyDate = now;
                }
                rev.Status = ControlledDocumentStatus.Effective; rev.ModifyDate = now;
                doc.CurrentRevision = rev.Revision; doc.Status = ControlledDocumentStatus.Effective; doc.EffectiveDate = rev.EffectiveDate; doc.ModifyDate = now;
                promoted++;
            }
            if (promoted > 0) await _uow.SaveChangesAsync(cancellationToken);
            return promoted;
        }

        public async Task<ResponseDto<ControlledDocumentDto>> GetDocumentAsync(int id, CancellationToken cancellationToken)
        {
            // untracked: the effective view below changes the loaded copy only, it is never saved
            ControlledDocument d = await _uow.Set<ControlledDocument>().AsNoTracking().Include(x => x.Revisions).ThenInclude(r => r.Distributions)
                .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (d == null) return QualityText.NotFound<ControlledDocumentDto>("سند پیدا نشد.");
            DateTime today = DateTime.Today;
            DocumentRevisionRecord due = d.Revisions.Where(r => !r.Deleted && r.Status == ControlledDocumentStatus.Scheduled && r.EffectiveDate.HasValue && RevisionRules.TakesEffectNow(r.EffectiveDate.Value, today))
                .OrderBy(r => r.EffectiveDate).ThenBy(r => r.Id).FirstOrDefault();
            if (due != null)
            {
                foreach (DocumentRevisionRecord old in d.Revisions.Where(r => r.Id != due.Id && r.Status == ControlledDocumentStatus.Effective)) old.Status = ControlledDocumentStatus.Obsolete;
                due.Status = ControlledDocumentStatus.Effective;
                d.CurrentRevision = due.Revision; d.Status = ControlledDocumentStatus.Effective; d.EffectiveDate = due.EffectiveDate;
            }
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
            DocumentRevisionRecord waiting = d.Revisions.FirstOrDefault(r => !r.Deleted && r.Status == ControlledDocumentStatus.Scheduled);
            if (waiting != null)
                return Bad<ControlledDocumentDto>($"بازنگری {waiting.Revision} برای تاریخ {(waiting.EffectiveDate.HasValue ? global::Services.NasbNiro.Finance.FiscalCalendars.Text(waiting.EffectiveDate.Value) : "-")} برنامه‌ریزی شده است؛ تا اجرای آن (یا لغو آن با «منسوخ») بازنگری تازه‌ای ثبت نمی‌شود.");
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
                    {
                        DateTime effectiveDate = (dto.EffectiveDate ?? DateTime.Today).Date;
                        rev.ApprovedByUserId = Me; rev.ApprovedAt = now; rev.EffectiveDate = effectiveDate;
                        if (RevisionRules.TakesEffectNow(effectiveDate, DateTime.Today))
                        {
                            foreach (DocumentRevisionRecord old in doc.Revisions.Where(r => r.Id != rev.Id && r.Status == ControlledDocumentStatus.Effective))
                            {
                                old.Status = ControlledDocumentStatus.Obsolete; old.ModifyDate = now; old.ModifierUserId = Me;
                            }
                            rev.Status = ControlledDocumentStatus.Effective;
                            doc.CurrentRevision = rev.Revision; doc.Status = ControlledDocumentStatus.Effective; doc.EffectiveDate = rev.EffectiveDate;
                        }
                        else
                        {
                            // R1q D41: a future effective date does not switch the floor to the new revision today: the current revision stays in force
                            // (and stays on the master list) until that day; the nightly job promotes the new one then (a reader sees it as in force from that day).
                            rev.Status = ControlledDocumentStatus.Scheduled;
                            if (doc.CurrentRevision == null) doc.Status = ControlledDocumentStatus.Scheduled;
                        }
                    }
                    break;
                case "obsolete":
                    if (!_user.IsInRole(QualityRoles.DocumentApprovers)) return Bad<ControlledDocumentDto>(QualityText.NotAllowed);
                    if (rev.Status == ControlledDocumentStatus.Scheduled)
                    {
                        // withdrawing a revision that never took effect: the current revision (if any) simply stays
                        rev.Status = ControlledDocumentStatus.Obsolete;
                        if (doc.CurrentRevision == null) doc.Status = ControlledDocumentStatus.Obsolete;
                        break;
                    }
                    if (rev.Status != ControlledDocumentStatus.Effective) return Bad<ControlledDocumentDto>("فقط بازنگری معتبر (یا برنامه‌ریزی‌شده) منسوخ می‌شود.");
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
