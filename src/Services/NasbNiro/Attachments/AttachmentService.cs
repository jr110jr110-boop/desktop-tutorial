using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Services.Contracts;
using Services.Identity.AccountServices;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.AttachmentDtos;
using ViewModels.NasbNiro.DocumentDtos;

namespace Services.NasbNiro.Attachments
{
    public interface IAttachmentService
    {
        /// <summary>The attachments of one record (history included) and, on a record downstream of a purchase request, the technical documents of that request (read-only).</summary>
        Task<ResponseDto<AttachmentPanelDto>> GetPanelAsync(AttachmentOwnerType type, int ownerId, CancellationToken ct);

        Task<ResponseDto<AttachmentDto>> UploadAsync(UploadAttachmentForm form, CancellationToken ct);

        /// <summary>The same allow-list, size cap and magic-byte check for the generic Documents upload. Null when the file may be stored, otherwise the Persian refusal.</summary>
        Task<ResponseDto<string>> CheckUploadAsync(IFormFile file, CancellationToken ct, bool allowArchive = false);

        /// <summary>Void with a reason. Allowed at any time to the write roles of the owning module; nothing is deleted.</summary>
        Task<ResponseDto<AttachmentDto>> VoidAsync(VoidAttachmentDto dto, CancellationToken ct);

        /// <summary>The uploader takes his file away while the owner is not approved (the row is hidden, the file and the log stay). After approval: refused.</summary>
        Task<ResponseDto<bool>> RemoveAsync(int id, CancellationToken ct);

        /// <summary>
        /// For the legacy detach actions (contract, tender, buy order, process, equipment process): keeps the file as a VOIDED attachment of the owner instead of deleting it.
        /// It only adds the row to the unit of work; the caller removes its own link row and saves both together. Returns a refusal when the reason is missing.
        /// </summary>
        Task<ResponseDto<bool>> VoidLegacyFileAsync(AttachmentOwnerType type, int ownerId, Guid documentId, string reason, CancellationToken ct);

        /// <summary>The attachments of many records at once (for the request dossier), key "type:id". A record the caller may not read is left out; money-carrying kinds only for those who see prices.</summary>
        Task<Dictionary<string, List<AttachmentDto>>> ForOwnersAsync(IEnumerable<KeyValuePair<AttachmentOwnerType, int>> owners, CancellationToken ct);
    }

    public sealed class AttachmentService : IAttachmentService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IDocumentService _documents;
        private readonly IHttpContextAccessor _http;
        private readonly IAttachmentRecordRules _rules;

        public AttachmentService(IUnitOfWork uow, IInventoryUserContext user, IDocumentService documents, IHttpContextAccessor http, IAttachmentRecordRules rules = null)
        {
            _rules = rules;
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _documents = documents ?? throw new ArgumentNullException(nameof(documents));
            _http = http;
        }

        private int Me => _user.UserId;
        private static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);
        private static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private bool InRoles(string roles) => roles == null || _user.IsInRole(roles);
        private bool SeesPrices => _user.IsInRole(AttachmentRoles.Prices);

        /// <summary>A file that carries money (invoice, payment slip) is added to, voided, replaced and removed only by finance, whatever module's record it hangs on.</summary>
        private bool MayTouch(AttachmentKind kind) => !AttachmentRules.CarriesPrices(kind) || _user.IsInRole(AttachmentRoles.PaymentWrite);

        private async Task<long> MaxBytesAsync(CancellationToken ct)
        {
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, ct);
            decimal mb = s.TryGetValue(AttachmentSettingKeys.MaxMegabytes, out decimal v) && v > 0 ? v : AttachmentRules.DefaultMaxMegabytes;
            return (long)(mb * 1024m * 1024m);
        }

        // ------------------------------------------------------------------------------------------ read

        public async Task<ResponseDto<AttachmentPanelDto>> GetPanelAsync(AttachmentOwnerType type, int ownerId, CancellationToken ct)
        {
            if (!Enum.IsDefined(typeof(AttachmentOwnerType), type)) return Bad<AttachmentPanelDto>(AttachmentText.OwnerNotFound);
            OwnerFacts facts = await AttachmentOwners.FactsAsync(_uow, type, ownerId, ct, _rules);
            if (!facts.Exists) return ResponseDtoHelper.NotFound<AttachmentPanelDto>(AttachmentText.OwnerNotFound);
            if (!facts.CanRead(_user)) return ResponseDtoHelper.Forbidden<AttachmentPanelDto>(AttachmentText.NotAllowed);

            var panel = new AttachmentPanelDto
            {
                OwnerType = type, OwnerId = ownerId, OwnerTitle = AttachmentText.OwnerTitle(type), OwnerNumber = facts.Number, Locked = facts.Locked,
                CanWrite = facts.CanWrite(_user), MaxBytes = await MaxBytesAsync(ct), AllowedExtensions = AttachmentRules.AllowedExtensions.ToList(),
            };
            List<RecordAttachment> rows = await _uow.Set<RecordAttachment>().AsNoTracking()
                .Where(a => a.OwnerType == type && a.OwnerId == ownerId && !a.Deleted && a.RemovedAt == null).OrderBy(a => a.Id).ToListAsync(ct);
            List<AttachmentDto> items = await ToDtosAsync(rows, new Dictionary<string, OwnerFacts> { [Key(type, ownerId)] = facts }, ct);
            items.AddRange(await LegacyItemsAsync(type, ownerId, facts, rows, ct));
            panel.Items = items.Where(i => !AttachmentRules.CarriesPrices(i.Kind) || SeesPrices).ToList();

            if (facts.PurchaseRequestIds.Count > 0 && type != AttachmentOwnerType.PurchaseRequest && type != AttachmentOwnerType.PurchaseRequestLine && _user.IsInRole(AttachmentRoles.TechnicalChain))
                panel.Propagated = await PropagatedAsync(facts, ct);
            return ResponseDtoHelper.Ok(panel);
        }

        /// <summary>The technical documents of the purchase requests upstream of a record: the ones in force, read-only, no prices. They are links to the request's own attachments, never copies.</summary>
        private async Task<List<PropagatedAttachmentsDto>> PropagatedAsync(OwnerFacts facts, CancellationToken ct)
        {
            var result = new List<PropagatedAttachmentsDto>();
            foreach (int prId in facts.PurchaseRequestIds.OrderBy(x => x))
            {
                var pr = await _uow.Set<PurchaseRequest>().AsNoTracking().Where(r => r.Id == prId && !r.Deleted).Select(r => new { r.Number }).FirstOrDefaultAsync(ct);
                if (pr == null) continue;
                List<int> lineIds = await _uow.Set<PurchaseRequestLine>().AsNoTracking().Where(l => l.PurchaseRequestId == prId).Select(l => l.Id).ToListAsync(ct);
                List<int> shownLines = facts.PurchaseRequestLineIds.Count == 0 ? lineIds : lineIds.Where(facts.PurchaseRequestLineIds.Contains).ToList();
                List<RecordAttachment> rows = await _uow.Set<RecordAttachment>().AsNoTracking()
                    .Where(a => !a.Deleted && a.RemovedAt == null && !a.Voided && a.SupersededById == null && a.Kind != AttachmentKind.Invoice && a.Kind != AttachmentKind.PaymentSlip
                        && ((a.OwnerType == AttachmentOwnerType.PurchaseRequest && a.OwnerId == prId) || (a.OwnerType == AttachmentOwnerType.PurchaseRequestLine && shownLines.Contains(a.OwnerId))))
                    .OrderBy(a => a.OwnerType).ThenBy(a => a.Id).ToListAsync(ct);
                if (rows.Count == 0) continue;
                var cache = new Dictionary<string, OwnerFacts>();
                cache[Key(AttachmentOwnerType.PurchaseRequest, prId)] = await AttachmentOwners.FactsAsync(_uow, AttachmentOwnerType.PurchaseRequest, prId, ct, _rules);
                foreach (int lineId in rows.Where(r => r.OwnerType == AttachmentOwnerType.PurchaseRequestLine).Select(r => r.OwnerId).Distinct())
                    cache[Key(AttachmentOwnerType.PurchaseRequestLine, lineId)] = await AttachmentOwners.FactsAsync(_uow, AttachmentOwnerType.PurchaseRequestLine, lineId, ct, _rules);
                List<AttachmentDto> dtos = await ToDtosAsync(rows, cache, ct);
                // the propagated copy is read-only on this screen: the actions belong to the request's own screen
                foreach (AttachmentDto d in dtos) { d.CanRemove = false; d.CanVoid = false; d.CanSupersede = false; }
                result.Add(new PropagatedAttachmentsDto { SourceType = AttachmentOwnerType.PurchaseRequest, SourceId = prId, SourceNumber = pr.Number, SourceTitle = "مدارک فنی درخواست " + pr.Number, Items = dtos });
            }
            return result;
        }

        private static string Key(AttachmentOwnerType type, int id) => (int)type + ":" + id;

        private async Task<List<AttachmentDto>> ToDtosAsync(List<RecordAttachment> rows, Dictionary<string, OwnerFacts> factsCache, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.UploadedByUserId, r.VoidedByUserId }), ct);
            var list = new List<AttachmentDto>();
            foreach (RecordAttachment r in rows)
            {
                string key = Key(r.OwnerType, r.OwnerId);
                if (!factsCache.TryGetValue(key, out OwnerFacts facts)) { facts = await AttachmentOwners.FactsAsync(_uow, r.OwnerType, r.OwnerId, ct, _rules); factsCache[key] = facts; }
                bool superseded = r.SupersededById.HasValue;
                AttachmentActions actions = AttachmentRules.Actions(facts.CanWrite(_user) && MayTouch(r.Kind), r.UploadedByUserId == Me && Me > 0, facts.Locked, r.Voided, superseded, r.RemovedAt.HasValue);
                list.Add(new AttachmentDto
                {
                    Id = r.Id, OwnerType = r.OwnerType, OwnerId = r.OwnerId, OwnerTitle = AttachmentText.OwnerTitle(r.OwnerType), OwnerNumber = facts.Number, DocumentId = r.DocumentId,
                    Kind = r.Kind, KindTitle = AttachmentText.KindTitle(r.Kind), Title = r.Title, Revision = r.Revision, FileName = r.FileName, SizeBytes = r.SizeBytes,
                    UploadedByUserId = r.UploadedByUserId, UploadedByName = names.Name(r.UploadedByUserId), UploadedAt = r.UploadedAt,
                    Voided = r.Voided, VoidedByName = names.Name(r.VoidedByUserId), VoidedAt = r.VoidedAt, VoidReason = r.VoidReason, SupersededById = r.SupersededById,
                    Current = !r.Voided && !superseded, IsLink = r.LinkedFromAttachmentId.HasValue, LinkObsolete = r.LinkObsolete, Locked = facts.Locked, CanRemove = actions.CanRemove, CanVoid = actions.CanVoid, CanSupersede = actions.CanSupersede,
                });
            }
            return list;
        }

        /// <summary>The single-file columns that existed before PB3 (quote file, invoice image, inspection report, weighbridge photo, lot certificate), shown beside the new attachments until an upgrade copies them.</summary>
        private async Task<List<AttachmentDto>> LegacyItemsAsync(AttachmentOwnerType type, int ownerId, OwnerFacts facts, List<RecordAttachment> rows, CancellationToken ct)
        {
            var found = new List<(Guid Doc, AttachmentKind Kind, string Title)>();
            switch (type)
            {
                case AttachmentOwnerType.Quote:
                    Guid? q = await _uow.Set<SupplierQuote>().AsNoTracking().Where(x => x.Id == ownerId).Select(x => x.DocumentId).FirstOrDefaultAsync(ct);
                    if (q.HasValue) found.Add((q.Value, AttachmentKind.Other, "فایل پیشنهاد قیمت"));
                    break;
                case AttachmentOwnerType.SupplierInvoice:
                    Guid? v = await _uow.Set<SupplierInvoice>().AsNoTracking().Where(x => x.Id == ownerId).Select(x => x.DocumentId).FirstOrDefaultAsync(ct);
                    if (v.HasValue) found.Add((v.Value, AttachmentKind.Invoice, "تصویر فاکتور"));
                    break;
                case AttachmentOwnerType.QcInspection:
                    Guid? i = await _uow.Set<QcInspection>().AsNoTracking().Where(x => x.Id == ownerId).Select(x => x.ReportDocumentId).FirstOrDefaultAsync(ct);
                    if (i.HasValue) found.Add((i.Value, AttachmentKind.QcReport, "فایل گزارش بازرسی"));
                    break;
                case AttachmentOwnerType.WarehouseDocument:
                    Guid? w = await _uow.Set<InventoryDocument>().AsNoTracking().Where(x => x.Id == ownerId).Select(x => x.WeighbridgeDocumentId).FirstOrDefaultAsync(ct);
                    if (w.HasValue) found.Add((w.Value, AttachmentKind.Photo, "عکس باسکول"));
                    break;
                case AttachmentOwnerType.InventoryLot:
                    Guid? c = await _uow.Set<InventoryLot>().AsNoTracking().Where(x => x.Id == ownerId).Select(x => x.CertificateDocumentId).FirstOrDefaultAsync(ct);
                    if (c.HasValue) found.Add((c.Value, AttachmentKind.Certificate, "گواهی محموله"));
                    break;
            }
            var result = new List<AttachmentDto>();
            if (type == AttachmentOwnerType.PartOperation)
            {
                // the files of the traceability operation before PB3 (PartOperationFile): shown beside the new attachments
                foreach (var pf in await _uow.Set<PartOperationFile>().AsNoTracking().Where(x => x.PartOperationId == ownerId).OrderBy(x => x.Id).ToListAsync(ct))
                {
                    if (rows.Any(r => r.DocumentId == pf.DocumentId)) continue;
                    var pd = await _uow.Set<Document>().AsNoTracking().Where(d => d.Id == pf.DocumentId).Select(d => new { d.Name, d.MimType }).FirstOrDefaultAsync(ct);
                    result.Add(new AttachmentDto
                    {
                        Id = 0, OwnerType = type, OwnerId = ownerId, OwnerTitle = AttachmentText.OwnerTitle(type), OwnerNumber = facts.Number, DocumentId = pf.DocumentId, Kind = AttachmentKind.Other, KindTitle = AttachmentText.KindTitle(AttachmentKind.Other),
                        Title = pf.Title, FileName = pd == null ? pf.Title : (pd.Name ?? pf.Title) + pd.MimType, UploadedByUserId = pf.CreatedByUserId, UploadedAt = pf.CreatedAt, Current = true, Locked = facts.Locked, Legacy = true,
                    });
                }
            }
            foreach (var f in found)
            {
                if (rows.Any(r => r.DocumentId == f.Doc)) continue;
                var doc = await _uow.Set<Document>().AsNoTracking().Where(d => d.Id == f.Doc).Select(d => new { d.Name, d.MimType, d.RegisterDate, d.UploadedByUserId }).FirstOrDefaultAsync(ct);
                if (doc == null) continue;
                result.Add(new AttachmentDto
                {
                    Id = 0, OwnerType = type, OwnerId = ownerId, OwnerTitle = AttachmentText.OwnerTitle(type), OwnerNumber = facts.Number, DocumentId = f.Doc, Kind = f.Kind, KindTitle = AttachmentText.KindTitle(f.Kind),
                    Title = f.Title, FileName = (doc.Name ?? f.Title) + doc.MimType, UploadedByUserId = doc.UploadedByUserId ?? 0, UploadedAt = doc.RegisterDate, Current = true, Locked = facts.Locked, Legacy = true,
                });
            }
            return result;
        }

        /// <summary>
        /// The dossier's read-only view of many records at once: a constant number of queries however long the chain (one per owner type, one for the names, one for the old single-file columns, one for the documents).
        /// The owners come from the dossier, which has read them already; here only the read roles of each owner type and the price rule decide. No actions are offered (the dossier is for reading).
        /// </summary>
        public async Task<Dictionary<string, List<AttachmentDto>>> ForOwnersAsync(IEnumerable<KeyValuePair<AttachmentOwnerType, int>> owners, CancellationToken ct)
        {
            var result = new Dictionary<string, List<AttachmentDto>>();
            var rowsByKey = new List<RecordAttachment>();
            var legacy = new List<(AttachmentOwnerType Type, int Id, Guid Doc, AttachmentKind Kind, string Title)>();
            foreach (var group in owners.GroupBy(o => o.Key))
            {
                if (!InRoles(AttachmentOwners.ReadRolesOf(group.Key))) continue;
                List<int> ids = group.Select(o => o.Value).Where(v => v > 0).Distinct().ToList();
                if (ids.Count == 0) continue;
                foreach (int id in ids) result[Key(group.Key, id)] = new List<AttachmentDto>();
                rowsByKey.AddRange(await _uow.Set<RecordAttachment>().AsNoTracking().Where(a => a.OwnerType == group.Key && ids.Contains(a.OwnerId) && !a.Deleted && a.RemovedAt == null).OrderBy(a => a.Id).ToListAsync(ct));
                legacy.AddRange(await LegacyColumnsAsync(group.Key, ids, ct));
            }
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rowsByKey.SelectMany(r => new int?[] { r.UploadedByUserId, r.VoidedByUserId }), ct);
            foreach (RecordAttachment r in rowsByKey)
            {
                if (AttachmentRules.CarriesPrices(r.Kind) && !SeesPrices) continue;
                result[Key(r.OwnerType, r.OwnerId)].Add(new AttachmentDto
                {
                    Id = r.Id, OwnerType = r.OwnerType, OwnerId = r.OwnerId, OwnerTitle = AttachmentText.OwnerTitle(r.OwnerType), DocumentId = r.DocumentId,
                    Kind = r.Kind, KindTitle = AttachmentText.KindTitle(r.Kind), Title = r.Title, Revision = r.Revision, FileName = r.FileName, SizeBytes = r.SizeBytes,
                    UploadedByUserId = r.UploadedByUserId, UploadedByName = names.Name(r.UploadedByUserId), UploadedAt = r.UploadedAt,
                    Voided = r.Voided, VoidedByName = names.Name(r.VoidedByUserId), VoidedAt = r.VoidedAt, VoidReason = r.VoidReason, SupersededById = r.SupersededById,
                    Current = !r.Voided && !r.SupersededById.HasValue,
                });
            }
            legacy = legacy.Where(l => (!AttachmentRules.CarriesPrices(l.Kind) || SeesPrices) && !rowsByKey.Any(r => r.DocumentId == l.Doc)).ToList();
            if (legacy.Count > 0)
            {
                List<Guid> docIds = legacy.Select(l => l.Doc).Distinct().ToList();
                var docs = (await _uow.Set<Document>().AsNoTracking().Where(d => docIds.Contains(d.Id)).Select(d => new { d.Id, d.Name, d.MimType, d.RegisterDate, d.UploadedByUserId }).ToListAsync(ct)).ToDictionary(d => d.Id);
                foreach (var l in legacy)
                {
                    if (!docs.TryGetValue(l.Doc, out var doc)) continue;
                    result[Key(l.Type, l.Id)].Add(new AttachmentDto
                    {
                        Id = 0, OwnerType = l.Type, OwnerId = l.Id, OwnerTitle = AttachmentText.OwnerTitle(l.Type), DocumentId = l.Doc, Kind = l.Kind, KindTitle = AttachmentText.KindTitle(l.Kind),
                        Title = l.Title, FileName = (doc.Name ?? l.Title) + doc.MimType, UploadedByUserId = doc.UploadedByUserId ?? 0, UploadedAt = doc.RegisterDate, Current = true, Legacy = true,
                    });
                }
            }
            return result;
        }

        /// <summary>The single-file columns of many records of one type, in one query.</summary>
        private async Task<List<(AttachmentOwnerType Type, int Id, Guid Doc, AttachmentKind Kind, string Title)>> LegacyColumnsAsync(AttachmentOwnerType type, List<int> ids, CancellationToken ct)
        {
            var list = new List<(AttachmentOwnerType, int, Guid, AttachmentKind, string)>();
            switch (type)
            {
                case AttachmentOwnerType.Quote:
                    foreach (var x in await _uow.Set<SupplierQuote>().AsNoTracking().Where(q => ids.Contains(q.Id) && q.DocumentId != null).Select(q => new { q.Id, Doc = q.DocumentId.Value }).ToListAsync(ct)) list.Add((type, x.Id, x.Doc, AttachmentKind.Other, "فایل پیشنهاد قیمت"));
                    break;
                case AttachmentOwnerType.SupplierInvoice:
                    foreach (var x in await _uow.Set<SupplierInvoice>().AsNoTracking().Where(q => ids.Contains(q.Id) && q.DocumentId != null).Select(q => new { q.Id, Doc = q.DocumentId.Value }).ToListAsync(ct)) list.Add((type, x.Id, x.Doc, AttachmentKind.Invoice, "تصویر فاکتور"));
                    break;
                case AttachmentOwnerType.QcInspection:
                    foreach (var x in await _uow.Set<QcInspection>().AsNoTracking().Where(q => ids.Contains(q.Id) && q.ReportDocumentId != null).Select(q => new { q.Id, Doc = q.ReportDocumentId.Value }).ToListAsync(ct)) list.Add((type, x.Id, x.Doc, AttachmentKind.QcReport, "فایل گزارش بازرسی"));
                    break;
                case AttachmentOwnerType.WarehouseDocument:
                    foreach (var x in await _uow.Set<InventoryDocument>().AsNoTracking().Where(q => ids.Contains(q.Id) && q.WeighbridgeDocumentId != null).Select(q => new { q.Id, Doc = q.WeighbridgeDocumentId.Value }).ToListAsync(ct)) list.Add((type, x.Id, x.Doc, AttachmentKind.Photo, "عکس باسکول"));
                    break;
            }
            return list;
        }

        // ------------------------------------------------------------------------------------------ upload

        public async Task<ResponseDto<AttachmentDto>> UploadAsync(UploadAttachmentForm form, CancellationToken ct)
        {
            if (form == null || form.File == null || form.File.Length == 0) return Bad<AttachmentDto>(AttachmentText.NoFile);
            if (!Enum.IsDefined(typeof(AttachmentOwnerType), form.OwnerType)) return Bad<AttachmentDto>(AttachmentText.OwnerNotFound);
            if (!Enum.IsDefined(typeof(AttachmentKind), form.Kind)) return Bad<AttachmentDto>(AttachmentText.InvalidKind);

            OwnerFacts facts = await AttachmentOwners.FactsAsync(_uow, form.OwnerType, form.OwnerId, ct, _rules);
            if (!facts.Exists) return ResponseDtoHelper.NotFound<AttachmentDto>(AttachmentText.OwnerNotFound);
            if (!facts.CanRead(_user)) return ResponseDtoHelper.Forbidden<AttachmentDto>(AttachmentText.NotAllowed);
            if (facts.Closed) return Bad<AttachmentDto>(AttachmentText.OwnerCancelled);
            if (!facts.CanWrite(_user)) return ResponseDtoHelper.Forbidden<AttachmentDto>(facts.Locked ? AttachmentText.NotAllowed + " (پس از تایید فقط نقش‌های مجاز ماژول با ذکر دلیل پیوست را ابطال یا جایگزین می‌کنند)" : AttachmentText.NotAllowed);
            bool pettyHolderOwn = (form.OwnerType == AttachmentOwnerType.PettyCashSummaryLine || form.OwnerType == AttachmentOwnerType.PettyCashRequest || form.OwnerType == AttachmentOwnerType.PettyCashSummary) && facts.CreatorUserId == Me && !facts.Locked;
            if (form.Kind == AttachmentKind.PaymentSlip && !pettyHolderOwn && !_user.IsInRole(AttachmentRoles.PaymentWrite)) return ResponseDtoHelper.Forbidden<AttachmentDto>(AttachmentText.NotAllowed + " (فیش واریزی را حسابداری یا خزانه‌داری پیوست می‌کند)");

            ResponseDto<string> fileRefusal = await CheckFileAsync(form.File, ct);
            if (fileRefusal != null) return Bad<AttachmentDto>(fileRefusal.Message);

            RecordAttachment replaced = null;
            if (form.SupersedesId.HasValue)
            {
                replaced = await _uow.Set<RecordAttachment>().FirstOrDefaultAsync(a => a.Id == form.SupersedesId.Value && !a.Deleted, ct);
                if (replaced == null || replaced.OwnerType != form.OwnerType || replaced.OwnerId != form.OwnerId) return ResponseDtoHelper.NotFound<AttachmentDto>(AttachmentText.NotFound);
                if (replaced.Voided) return Bad<AttachmentDto>(AttachmentText.AlreadyVoided);
                if (replaced.SupersededById.HasValue) return Bad<AttachmentDto>(AttachmentText.AlreadySuperseded);
                // a file that carries money is replaced by finance only, and the new file keeps the kind (a payment slip cannot become «other» through a replacement)
                if (!MayTouch(replaced.Kind)) return ResponseDtoHelper.Forbidden<AttachmentDto>(AttachmentText.NotAllowed + " (فایل‌های مالی را فقط حسابداری یا خزانه‌داری ابطال یا جایگزین می‌کند)");
                form.Kind = replaced.Kind;
            }

            string title = Clean(form.Title);
            string fileName = Path.GetFileName(form.File.FileName);
            Guid documentId = await _documents.UploadDocumentAsync(new UploadFileDto
            {
                File = form.File, Name = Clip(title ?? Path.GetFileNameWithoutExtension(fileName), 250), Code = Clip(facts.Number ?? form.OwnerId.ToString(), 50),
                Description = Clip(AttachmentText.OwnerTitle(form.OwnerType) + " " + facts.Number, 500),
            });

            var row = new RecordAttachment
            {
                OwnerType = form.OwnerType, OwnerId = form.OwnerId, DocumentId = documentId, Kind = form.Kind, Title = Clip(title, 300), Revision = Clip(Clean(form.Revision), 30),
                FileName = Clip(fileName, 260), SizeBytes = form.File.Length, UploadedByUserId = Me, UploadedAt = DateTimeOffset.Now, CreateDate = DateTimeOffset.Now, CreatorUserId = Me,
            };
            _uow.Set<RecordAttachment>().Add(row);
            if (replaced != null)
            {
                // one save: the new row and the link on the old one go together, and the link is a concurrency token, so only one of two simultaneous replacements wins
                replaced.SupersededBy = row;
                await MarkLinksObsoleteAsync(replaced.Id, ct);
                Audit(replaced.Id, "Superseded", "جایگزینی پیوست «" + (replaced.Title ?? replaced.FileName) + "» با نسخه جدید در " + AttachmentText.OwnerTitle(form.OwnerType) + " " + facts.Number,
                    new { replaced.Id, replaced.DocumentId, replaced.Revision }, new { newDocumentId = row.DocumentId, row.Revision });
            }
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                return Bad<AttachmentDto>(AttachmentText.AlreadySuperseded);
            }

            Audit(row.Id, "Attached", "پیوست فایل «" + (row.Title ?? row.FileName) + "» به " + AttachmentText.OwnerTitle(form.OwnerType) + " " + facts.Number, null,
                new { row.Id, row.OwnerType, row.OwnerId, row.DocumentId, row.Kind, row.Revision, row.FileName });
            await _uow.SaveChangesAsync(ct);

            return ResponseDtoHelper.Ok((await ToDtosAsync(new List<RecordAttachment> { row }, new Dictionary<string, OwnerFacts> { [Key(row.OwnerType, row.OwnerId)] = facts }, ct))[0]);
        }

        /// <summary>
        /// A document that is voided or replaced at its source: every link made from it (on a purchase request of any status, submitted ones included, and so on every record downstream that shows the request's documents) is marked obsolete,
        /// so nobody works from a drawing that is no longer in force. The link row stays and is shown as obsolete.
        /// </summary>
        private async Task MarkLinksObsoleteAsync(int sourceAttachmentId, CancellationToken ct)
        {
            List<RecordAttachment> links = await _uow.Set<RecordAttachment>().Where(a => a.LinkedFromAttachmentId == sourceAttachmentId && !a.LinkObsolete && !a.Deleted).ToListAsync(ct);
            foreach (RecordAttachment link in links) { link.LinkObsolete = true; link.ModifyDate = DateTimeOffset.Now; link.ModifierUserId = Me; }
        }

        private static string Clip(string s, int max) => s == null ? null : (s.Length <= max ? s : s.Substring(0, max));

        /// <summary>The allow-list, the size cap and the magic bytes. Null when the file may be stored.</summary>
        public Task<ResponseDto<string>> CheckUploadAsync(IFormFile file, CancellationToken ct, bool allowArchive = false) => CheckFileAsync(file, ct, allowArchive);

        private async Task<ResponseDto<string>> CheckFileAsync(IFormFile file, CancellationToken ct, bool allowArchive = false)
        {
            if (file == null || file.Length == 0) return Bad<string>(AttachmentText.NoFile);
            if (!AttachmentRules.IsAllowed(file.FileName, allowArchive, out string extension)) return Bad<string>(AttachmentText.ExtensionRefused(extension));
            long max = await MaxBytesAsync(ct);
            if (file.Length > max) return Bad<string>(AttachmentText.TooLarge(max));
            var head = new byte[1100];
            int read;
            using (Stream stream = file.OpenReadStream()) read = await stream.ReadAsync(head, 0, head.Length, ct);
            if (!AttachmentRules.MatchesSignature(extension, head, read)) return Bad<string>(AttachmentText.SignatureMismatch);
            return null;
        }

        // ------------------------------------------------------------------------------------------ void, remove

        public async Task<ResponseDto<AttachmentDto>> VoidAsync(VoidAttachmentDto dto, CancellationToken ct)
        {
            if (dto == null || dto.Id <= 0) return Bad<AttachmentDto>(AttachmentText.NotFound);
            RecordAttachment row = await _uow.Set<RecordAttachment>().FirstOrDefaultAsync(a => a.Id == dto.Id && !a.Deleted && a.RemovedAt == null, ct);
            if (row == null) return ResponseDtoHelper.NotFound<AttachmentDto>(AttachmentText.NotFound);
            OwnerFacts facts = await AttachmentOwners.FactsAsync(_uow, row.OwnerType, row.OwnerId, ct, _rules);
            if (!facts.Exists) return ResponseDtoHelper.NotFound<AttachmentDto>(AttachmentText.OwnerNotFound);
            if (!facts.CanRead(_user) || !facts.CanWrite(_user) || !MayTouch(row.Kind)) return ResponseDtoHelper.Forbidden<AttachmentDto>(AttachmentText.NotAllowed);
            if (row.Voided) return Bad<AttachmentDto>(AttachmentText.AlreadyVoided);
            if (row.SupersededById.HasValue) return Bad<AttachmentDto>(AttachmentText.AlreadySuperseded);
            string reason = Clean(dto.Reason);
            if (reason == null || reason.Length < 3) return Bad<AttachmentDto>(AttachmentText.ReasonRequired);

            row.Voided = true; row.VoidedByUserId = Me; row.VoidedAt = DateTimeOffset.Now; row.VoidReason = Clip(reason, 1000);
            await MarkLinksObsoleteAsync(row.Id, ct);
            Audit(row.Id, "Voided", "ابطال پیوست «" + (row.Title ?? row.FileName) + "» از " + AttachmentText.OwnerTitle(row.OwnerType) + " " + facts.Number + ": " + reason,
                new { row.Id, row.DocumentId, voided = false }, new { row.Id, row.DocumentId, voided = true, reason });
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await ToDtosAsync(new List<RecordAttachment> { row }, new Dictionary<string, OwnerFacts> { [Key(row.OwnerType, row.OwnerId)] = facts }, ct))[0]);
        }

        public async Task<ResponseDto<bool>> RemoveAsync(int id, CancellationToken ct)
        {
            RecordAttachment row = await _uow.Set<RecordAttachment>().FirstOrDefaultAsync(a => a.Id == id && !a.Deleted && a.RemovedAt == null, ct);
            if (row == null) return ResponseDtoHelper.NotFound<bool>(AttachmentText.NotFound);
            OwnerFacts facts = await AttachmentOwners.FactsAsync(_uow, row.OwnerType, row.OwnerId, ct, _rules);
            if (!facts.Exists) return ResponseDtoHelper.NotFound<bool>(AttachmentText.OwnerNotFound);
            if (!facts.CanRead(_user)) return ResponseDtoHelper.Forbidden<bool>(AttachmentText.NotAllowed);
            // an approved record never loses a file: the answer says what to do instead (void with a reason, or supersede)
            if (facts.Locked) return Bad<bool>(AttachmentText.RemoveAfterApproval);
            if (!facts.CanWrite(_user) || !MayTouch(row.Kind)) return ResponseDtoHelper.Forbidden<bool>(AttachmentText.NotAllowed);
            if (row.Voided || row.SupersededById.HasValue) return Bad<bool>(AttachmentText.PhysicalDeleteRefused);
            if (row.UploadedByUserId != Me) return Bad<bool>(AttachmentText.RemoveNotUploader);

            row.RemovedAt = DateTimeOffset.Now; row.RemovedByUserId = Me;
            row.Delete();   // hidden from the lists; the row, the file and its hash stay
            Audit(row.Id, "Removed", "حذف پیوست «" + (row.Title ?? row.FileName) + "» پیش از تایید از " + AttachmentText.OwnerTitle(row.OwnerType) + " " + facts.Number,
                new { row.Id, row.DocumentId }, null);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(true);
        }

        public async Task<ResponseDto<bool>> VoidLegacyFileAsync(AttachmentOwnerType type, int ownerId, Guid documentId, string reason, CancellationToken ct)
        {
            reason = Clean(reason);
            if (reason == null || reason.Length < 3) return Bad<bool>(AttachmentText.ReasonRequired);
            var doc = await _uow.Set<Document>().AsNoTracking().Where(d => d.Id == documentId).Select(d => new { d.Name, d.MimType, d.UploadedByUserId, d.RegisterDate }).FirstOrDefaultAsync(ct);
            string fileName = doc == null ? null : (doc.Name ?? "فایل") + doc.MimType;
            var row = new RecordAttachment
            {
                OwnerType = type, OwnerId = ownerId, DocumentId = documentId, Kind = AttachmentKind.Other, Title = Clip(doc?.Name, 300), FileName = Clip(fileName, 260),
                UploadedByUserId = doc?.UploadedByUserId ?? Me, UploadedAt = doc != null && doc.RegisterDate != default ? doc.RegisterDate : DateTimeOffset.Now, CreateDate = DateTimeOffset.Now, CreatorUserId = Me,
                Voided = true, VoidedByUserId = Me, VoidedAt = DateTimeOffset.Now, VoidReason = Clip(reason, 1000),
            };
            _uow.Set<RecordAttachment>().Add(row);
            IdentityAudit.Add(_uow, _http, Me, type.ToString(), ownerId, "FileVoided", "ابطال فایل " + AttachmentText.OwnerTitle(type) + " " + ownerId + " (به‌جای حذف): " + reason, new { documentId, ownerType = type, ownerId }, new { voided = true, reason });
            return ResponseDtoHelper.Ok(true);
        }

        private void Audit(int? id, string action, string summary, object before, object after) =>
            IdentityAudit.Add(_uow, _http, Me, "RecordAttachment", id, action, summary, before, after);
    }
}
