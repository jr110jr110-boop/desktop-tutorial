using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using FinSheet = Services.NasbNiro.Finance.XlsxSheet;
using SimpleXlsx = Services.NasbNiro.Finance.SimpleXlsx;
using ReadSheet = Services.NasbNiro.Office.XlsxSheet;
using XlsxReader = Services.NasbNiro.Office.XlsxReader;
using XlsxRow = Services.NasbNiro.Office.XlsxRow;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    // The shop-floor viewer and the controlled copy (E6, E11) and the MDR Excel template / import / export (item 8).
    public partial class EdmsService
    {
        public async Task<ResponseDto<List<PcShopDocumentDto>>> GetShopDocumentsAsync(int orderId, int? operationId, CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.ShopFloor + "," + EdmsRoles.Readers)) return Bad<List<PcShopDocumentDto>>(PcText.NotAllowed);
            MfProductionOrder order = await _uow.Set<MfProductionOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order == null) return PcText.NotFound<List<PcShopDocumentDto>>("دستور تولید پیدا نشد.");
            int? template = null;
            if (operationId.HasValue) template = await _uow.Set<MfOperation>().AsNoTracking().Where(o => o.Id == operationId.Value && o.OrderId == orderId).Select(o => o.OperationTemplateId).FirstOrDefaultAsync(ct);
            List<EdmsFoundDocument> docs = await EdmsResolver.ForOrderAsync(_uow, order, template, operationId.HasValue, true, ct);
            // only released revisions are shown; earlier revisions and drafts never reach the floor
            return ResponseDtoHelper.Ok(docs.Where(f => f.Released != null).Select(f => new PcShopDocumentDto
            {
                DeliverableId = f.Deliverable.Id, RevisionId = f.Released.Id, DocumentNumber = f.Number, Title = f.Title, DocumentType = f.DocumentType, Code = f.Released.Code, ReleasedAt = f.Released.ReleasedAt, ViaText = f.ViaText,
                Files = f.Files.Select(x => new PcRevisionFileDto { Id = x.Id, DocumentId = x.DocumentId, Role = x.Role, FileName = x.FileName, Title = x.Title }).ToList(),
            }).ToList());
        }

        /// <summary>The stamp for a print of a released revision. Returns the stamp only; the page wraps the file with it.</summary>
        public async Task<ResponseDto<PcControlledCopyDto>> ControlledCopyAsync(string source, int revisionId, Guid? documentId, bool print, CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.ShopFloor + "," + EdmsRoles.Readers)) return Bad<PcControlledCopyDto>(PcText.NotAllowed);
            PcDocumentRevision r = await _uow.Set<PcDocumentRevision>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == revisionId && !x.Deleted, ct);
            if (r == null) return PcText.NotFound<PcControlledCopyDto>("بازنگری پیدا نشد.");
            if (r.Status != PcDocStatus.Released) return Bad<PcControlledCopyDto>("فقط نسخه منتشرشده‌ی سند چاپ کنترل‌شده دارد.");
            var d = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(x => x.Id == r.DeliverableId).Select(x => new { x.DocumentNumber, x.Title }).FirstAsync(ct);
            PcDocumentRevisionFile f = await _uow.Set<PcDocumentRevisionFile>().AsNoTracking().Where(x => x.RevisionId == r.Id && !x.Deleted && (documentId == null || x.DocumentId == documentId)).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (f == null) return PcText.NotFound<PcControlledCopyDto>("فایلی برای این بازنگری ثبت نشده است.");
            DateTimeOffset now = DateTimeOffset.Now;
            string name = NameOf(await NamesAsync(new int?[] { Me }, ct), Me) ?? ("#" + Me);
            if (print) { Log(r.DeliverableId, r.Id, PcDocEventKind.Printed, f.FileName, now); await _uow.SaveChangesAsync(ct); }
            return ResponseDtoHelper.Ok(new PcControlledCopyDto
            {
                RevisionId = r.Id, DocumentNumber = d.DocumentNumber, Title = d.Title, Code = r.Code, PrintedAt = now, PrintedByUserId = Me, PrintedByName = name,
                StampText = EdmsRules.StampText(d.DocumentNumber, r.Code, now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), name), DocumentId = f.DocumentId, FileName = f.FileName,
            });
        }

        // ============================================================================ Excel

        private static readonly string[] MdrColumns = { "DocumentNumber", "Title", "Discipline", "DocumentType", "ClientSubmittal", "Required", "PlannedDate", "ProgressWeight", "StandardSource" };

        public async Task<ResponseDto<byte[]>> ExportMdrAsync(int contractId, bool clientOnly, int? workOrderId, bool templateOnly, CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.Readers)) return Bad<byte[]>(PcText.NotAllowed);
            var sheet = new FinSheet { Name = "MDR", Header = MdrColumns.ToList() };
            if (templateOnly) sheet.Rows.Add(new object[] { "PRJ-001-001", "Sample title", "Mechanical", "Drawing", 0, 1, "2030-01-31", 1, "" });
            else
            {
                IQueryable<PcEngineeringDeliverable> q = _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => !d.Deleted && d.ContractId == contractId);
                if (clientOnly) q = q.Where(d => d.ClientSubmittal);
                if (workOrderId.HasValue) q = q.Where(d => d.WorkOrderId == workOrderId.Value);
                foreach (PcEngineeringDeliverable d in await q.OrderBy(d => d.DocumentNumber).Take(20000).ToListAsync(ct))
                    sheet.Rows.Add(new object[] { d.DocumentNumber, d.Title, d.Discipline, d.DocumentType, d.ClientSubmittal ? 1 : 0, d.Required ? 1 : 0, d.PlannedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", d.ProgressWeight, d.StandardSource });
            }
            return ResponseDtoHelper.Ok(SimpleXlsx.Build(new List<FinSheet> { sheet }));
        }

        public async Task<ResponseDto<PcMdrImportResultDto>> ImportMdrAsync(int contractId, byte[] file, CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.Preparers)) return Bad<PcMdrImportResultDto>(PcText.NotAllowed);
            if (file == null || file.Length == 0) return Bad<PcMdrImportResultDto>("فایل خالی است.");
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == contractId && !c.Deleted, ct)) return PcText.NotFound<PcMdrImportResultDto>(PcText.ContractNotFound);
            ReadSheet sh;
            try { sh = XlsxReader.Read(file, null); }
            catch (Exception) { return Bad<PcMdrImportResultDto>("فایل اکسل خوانده نشد؛ قالب الگو را به‌کار ببرید."); }
            var result = new PcMdrImportResultDto();
            if (sh.Rows.Count == 0) return Bad<PcMdrImportResultDto>("فایل ردیفی ندارد.");
            var head = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in sh.Rows[0].Cells) { string h = Clean(kv.Value.AsText()); if (h != null && !head.ContainsKey(h)) head[h] = kv.Key; }
            if (!head.ContainsKey("DocumentNumber") || !head.ContainsKey("Title")) return Bad<PcMdrImportResultDto>("ستون‌های DocumentNumber و Title لازم است.");
            string Cell(XlsxRow row, string col) => head.TryGetValue(col, out int ix) ? Clean(row.Cell(ix)?.AsText()) : null;
            Dictionary<string, PcEngineeringDeliverable> existing = (await _uow.Set<PcEngineeringDeliverable>().Where(d => !d.Deleted && d.ContractId == contractId).ToListAsync(ct)).GroupBy(d => d.DocumentNumber).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            DateTimeOffset now = DateTimeOffset.Now;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (XlsxRow row in sh.Rows.Skip(1))
            {
                string no = Cell(row, "DocumentNumber"), title = Cell(row, "Title");
                if (no == null && title == null) continue;
                string at = "ردیف " + row.RowNumber + ": ";
                if (no == null || title == null) { result.Errors.Add(at + "شماره و عنوان سند الزامی است."); result.Skipped++; continue; }
                if (!seen.Add(no)) { result.Errors.Add(at + "شماره سند در فایل تکراری است."); result.Skipped++; continue; }
                DateTime? planned = null;
                string pd = Cell(row, "PlannedDate");
                if (pd != null)
                {
                    if (DateTime.TryParse(pd, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt)) planned = dt.Date;
                    else if (double.TryParse(pd, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial) && serial > 20000 && serial < 80000) planned = new DateTime(1899, 12, 30).AddDays(serial);
                    else { result.Errors.Add(at + "تاریخ برنامه نامعتبر است (قالب yyyy-MM-dd)."); result.Skipped++; continue; }
                }
                decimal weight = 1m;
                string w = Cell(row, "ProgressWeight");
                if (w != null && (!decimal.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out weight) || weight <= 0 || weight > 100)) { result.Errors.Add(at + "وزن پیشرفت باید بین 0 و 100 باشد."); result.Skipped++; continue; }
                bool client = Cell(row, "ClientSubmittal") == "1", required = Cell(row, "Required") != "0";
                if (existing.TryGetValue(no, out PcEngineeringDeliverable d))
                {
                    if (d.CheckedOutByUserId.HasValue && d.CheckedOutByUserId != Me) { result.Errors.Add(at + LockedBy); result.Skipped++; continue; }
                    d.Title = title; d.Discipline = Cell(row, "Discipline") ?? d.Discipline; d.DocumentType = Cell(row, "DocumentType") ?? d.DocumentType; d.Required = required; d.PlannedDate = planned ?? d.PlannedDate; d.ProgressWeight = weight;
                    d.StandardSource = Cell(row, "StandardSource") ?? d.StandardSource; d.ModifyDate = now; d.ModifierUserId = Me;
                    result.Updated++;
                }
                else
                {
                    var n = new PcEngineeringDeliverable
                    {
                        ContractId = contractId, DocumentNumber = no, Title = title, ClientSubmittal = client, Discipline = Cell(row, "Discipline"), DocumentType = Cell(row, "DocumentType"), Required = required, PlannedDate = planned,
                        ProgressWeight = weight, StandardSource = Cell(row, "StandardSource"), IssueState = PcIssueState.NotIssued, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
                    };
                    _uow.Set<PcEngineeringDeliverable>().Add(n);
                    existing[no] = n;
                    result.Created++;
                }
            }
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(result);
        }
    }
}
