using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.ProcurementDtos;

namespace Services.NasbNiro.Procurement
{
    public interface IProcurementReportsService
    {
        ResponseDto<List<ReportColumnDto>> GetCatalog();
        Task<ResponseDto<ReportTableDto>> GetReportAsync(ProcurementReportQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<byte[]>> ExportAsync(ProcurementReportQuery query, CancellationToken cancellationToken);
    }

    /// <summary>A minimal .xlsx writer (one sheet, inline strings, RTL) so that no outside library is needed.</summary>
    public static class XlsxWriter
    {
        private static string Col(int i)
        {
            string s = "";
            for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s;
            return s;
        }

        public static byte[] Write(string sheetName, List<string> headers, List<List<object>> rows)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView rightToLeft=\"1\" workbookViewId=\"0\"/></sheetViews><sheetData>");
            void Row(int r, IList<object> cells, bool head)
            {
                sb.Append("<row r=\"").Append(r).Append("\">");
                for (int c = 0; c < cells.Count; c++)
                {
                    object v = cells[c];
                    string refc = Col(c) + r;
                    if (v == null) continue;
                    if (!head && (v is decimal || v is int || v is long || v is double))
                        sb.Append("<c r=\"").Append(refc).Append("\"><v>").Append(Convert.ToString(v, CultureInfo.InvariantCulture)).Append("</v></c>");
                    else
                        sb.Append("<c r=\"").Append(refc).Append("\" t=\"inlineStr\"><is><t>").Append(SecurityElement.Escape(Convert.ToString(v, CultureInfo.InvariantCulture))).Append("</t></is></c>");
                }
                sb.Append("</row>");
            }
            Row(1, headers.Cast<object>().ToList(), true);
            for (int i = 0; i < rows.Count; i++) Row(i + 2, rows[i], false);
            sb.Append("</sheetData></worksheet>");
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                void Add(string name, string content)
                {
                    ZipArchiveEntry e = zip.CreateEntry(name);
                    using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                    w.Write(content);
                }
                Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
                Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Add("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"" + SecurityElement.Escape(string.IsNullOrWhiteSpace(sheetName) ? "Sheet1" : sheetName.Length > 31 ? sheetName.Substring(0, 31) : sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
                Add("xl/worksheets/sheet1.xml", sb.ToString());
            }
            return ms.ToArray();
        }
    }

    /// <summary>PB6: the procurement reports (request progress, overdue requests, spend by supplier, orders, guarantees, addenda) with an Excel export. Reading only.</summary>
    public class ProcurementReportsService : IProcurementReportsService
    {
        private static readonly string[] Keys = { "requests", "overdue", "spend-by-supplier", "orders", "guarantees", "addenda" };
        private static readonly string[] Titles = { "پیشرفت درخواست‌های خرید", "درخواست‌های معوق بدون سفارش", "خرید به تفکیک تامین‌کننده", "سفارش‌های خرید", "ضمانت‌نامه‌ها", "الحاقیه‌ها" };

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IProcurementService _procurement;

        public ProcurementReportsService(IUnitOfWork uow, IInventoryUserContext user, IProcurementService procurement)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _procurement = procurement ?? throw new ArgumentNullException(nameof(procurement));
        }

        public ResponseDto<List<ReportColumnDto>> GetCatalog() =>
            ResponseDtoHelper.Ok(Keys.Select((k, i) => new ReportColumnDto { Key = k, Title = Titles[i], Type = "report" }).ToList());

        private static ReportColumnDto C(string key, string title, string type = "text") => new ReportColumnDto { Key = key, Title = title, Type = type };

        public async Task<ResponseDto<ReportTableDto>> GetReportAsync(ProcurementReportQuery query, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProcurementRoles.ReportReaders)) return ResponseDtoHelper.BadRequest<ReportTableDto>("شما مجوز گزارش‌های خرید را ندارید");
            query ??= new ProcurementReportQuery();
            int index = Array.IndexOf(Keys, (query.Report ?? "").Trim().ToLowerInvariant());
            if (index < 0) return ResponseDtoHelper.BadRequest<ReportTableDto>("گزارش نامعتبر است.");
            var t = new ReportTableDto { Key = Keys[index], Title = Titles[index] };
            DateTime? from = query.DateFrom?.Date, to = query.DateTo?.Date;
            switch (Keys[index])
            {
                case "requests":
                {
                    t.Columns.AddRange(new[] { C("number", "شماره"), C("date", "تاریخ", "date"), C("status", "وضعیت"), C("estimated", "برآورد (ریال)", "money"), C("orders", "تعداد سفارش", "number") });
                    IQueryable<PurchaseRequest> q = _uow.Set<PurchaseRequest>().AsNoTracking().Where(r => !r.Deleted);
                    if (from.HasValue) q = q.Where(r => r.RequestDate >= from.Value);
                    if (to.HasValue) q = q.Where(r => r.RequestDate <= to.Value);
                    if (query.ContractId.HasValue) q = q.Where(r => r.ContractId == query.ContractId.Value);
                    var rows = await q.OrderByDescending(r => r.Id).Take(2000).Select(r => new { r.Id, r.Number, r.RequestDate, r.Status, r.EstimatedAmount }).ToListAsync(cancellationToken);
                    List<int> ids = rows.Select(r => r.Id).ToList();
                    var orderLines = await _uow.Set<PurchaseOrderLine>().AsNoTracking().Where(l => l.RequestLineId != null && ids.Contains(l.RequestLine.PurchaseRequestId) && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled).Select(l => new { RequestId = l.RequestLine.PurchaseRequestId, l.PurchaseOrderId }).Distinct().ToListAsync(cancellationToken);
                    foreach (var r in rows)
                        t.Rows.Add(new Dictionary<string, object> { ["number"] = r.Number, ["date"] = r.RequestDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["status"] = r.Status.ToString(), ["estimated"] = r.EstimatedAmount, ["orders"] = orderLines.Count(o => o.RequestId == r.Id) });
                    t.Totals["estimated"] = rows.Sum(r => r.EstimatedAmount);
                    break;
                }
                case "overdue":
                {
                    t.Columns.AddRange(new[] { C("number", "شماره"), C("approved", "تاریخ تایید", "date"), C("days", "روز گذشته", "number"), C("estimated", "برآورد (ریال)", "money") });
                    Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
                    int days = query.OverdueDays ?? (int)s[ProcurementSettingKeys.RequestToOrderDays];
                    DateTimeOffset limit = DateTimeOffset.Now.AddDays(-days);
                    var rows = await _uow.Set<PurchaseRequest>().AsNoTracking().Where(r => !r.Deleted && r.Status == PurchaseRequestStatus.Approved && r.ManagementApprovedAt != null && r.ManagementApprovedAt < limit
                        && !_uow.Set<PurchaseOrderLine>().Any(l => l.RequestLine.PurchaseRequestId == r.Id && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled))
                        .OrderBy(r => r.ManagementApprovedAt).Take(1000).Select(r => new { r.Number, r.ManagementApprovedAt, r.EstimatedAmount }).ToListAsync(cancellationToken);
                    foreach (var r in rows)
                        t.Rows.Add(new Dictionary<string, object> { ["number"] = r.Number, ["approved"] = r.ManagementApprovedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["days"] = (int)(DateTimeOffset.Now - r.ManagementApprovedAt.Value).TotalDays, ["estimated"] = r.EstimatedAmount });
                    t.Totals["estimated"] = rows.Sum(r => r.EstimatedAmount);
                    break;
                }
                case "spend-by-supplier":
                case "orders":
                {
                    bool bySupplier = Keys[index] == "spend-by-supplier";
                    IQueryable<PurchaseOrder> q = _uow.Set<PurchaseOrder>().AsNoTracking().Where(o => !o.Deleted && o.Status != PurchaseOrderStatus.Cancelled);
                    if (from.HasValue) q = q.Where(o => o.OrderDate >= from.Value);
                    if (to.HasValue) q = q.Where(o => o.OrderDate <= to.Value);
                    if (query.ProviderId.HasValue) q = q.Where(o => o.ProviderId == query.ProviderId.Value);
                    if (query.ContractId.HasValue) q = q.Where(o => o.ContractId == query.ContractId.Value);
                    var rows = await q.OrderByDescending(o => o.Id).Take(3000).Select(o => new { o.Number, o.OrderDate, o.Status, o.TotalAmount, Provider = o.Provider.Name, o.ProviderId }).ToListAsync(cancellationToken);
                    if (bySupplier)
                    {
                        t.Columns.AddRange(new[] { C("provider", "تامین‌کننده"), C("count", "تعداد سفارش", "number"), C("total", "مبلغ (ریال)", "money") });
                        foreach (var g in rows.GroupBy(r => r.ProviderId).OrderByDescending(g => g.Sum(x => x.TotalAmount)))
                            t.Rows.Add(new Dictionary<string, object> { ["provider"] = g.First().Provider, ["count"] = g.Count(), ["total"] = g.Sum(x => x.TotalAmount) });
                    }
                    else
                    {
                        t.Columns.AddRange(new[] { C("number", "شماره"), C("date", "تاریخ", "date"), C("provider", "تامین‌کننده"), C("status", "وضعیت"), C("total", "مبلغ (ریال)", "money") });
                        foreach (var r in rows)
                            t.Rows.Add(new Dictionary<string, object> { ["number"] = r.Number, ["date"] = r.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["provider"] = r.Provider, ["status"] = r.Status.ToString(), ["total"] = r.TotalAmount });
                    }
                    t.Totals["total"] = rows.Sum(r => r.TotalAmount);
                    break;
                }
                case "guarantees":
                {
                    t.Columns.AddRange(new[] { C("number", "شماره"), C("provider", "تامین‌کننده"), C("kind", "نوع"), C("amount", "مبلغ (ریال)", "money"), C("expiry", "سررسید", "date"), C("status", "وضعیت") });
                    IQueryable<SupplierGuarantee> q = _uow.Set<SupplierGuarantee>().AsNoTracking().Where(g => !g.Deleted);
                    if (query.ProviderId.HasValue) q = q.Where(g => g.ProviderId == query.ProviderId.Value);
                    if (from.HasValue) q = q.Where(g => g.ExpiryDate >= from.Value);
                    if (to.HasValue) q = q.Where(g => g.ExpiryDate <= to.Value);
                    var rows = await q.OrderBy(g => g.ExpiryDate).Take(2000).Select(g => new { g.Number, Provider = g.Provider.Name, g.Kind, g.Amount, g.ExpiryDate, g.Status }).ToListAsync(cancellationToken);
                    foreach (var r in rows)
                        t.Rows.Add(new Dictionary<string, object> { ["number"] = r.Number, ["provider"] = r.Provider, ["kind"] = r.Kind.ToString(), ["amount"] = r.Amount, ["expiry"] = r.ExpiryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["status"] = r.Status.ToString() });
                    t.Totals["amount"] = rows.Sum(r => r.Amount);
                    break;
                }
                default:
                {
                    t.Columns.AddRange(new[] { C("number", "شماره"), C("order", "سفارش"), C("status", "وضعیت"), C("before", "مبلغ قبل (ریال)", "money"), C("after", "مبلغ بعد (ریال)", "money") });
                    IQueryable<PurchaseOrderAddendum> q = _uow.Set<PurchaseOrderAddendum>().AsNoTracking().Where(a => !a.Deleted);
                    if (query.ProviderId.HasValue) q = q.Where(a => a.PurchaseOrder.ProviderId == query.ProviderId.Value);
                    var rows = await q.OrderByDescending(a => a.Id).Take(2000).Select(a => new { a.Number, Order = a.PurchaseOrder.Number, a.Status, a.AmountBefore, a.AmountAfter }).ToListAsync(cancellationToken);
                    foreach (var r in rows)
                        t.Rows.Add(new Dictionary<string, object> { ["number"] = r.Number, ["order"] = r.Order, ["status"] = r.Status.ToString(), ["before"] = r.AmountBefore, ["after"] = r.AmountAfter });
                    t.Totals["after"] = rows.Sum(r => r.AmountAfter);
                    break;
                }
            }
            return ResponseDtoHelper.Ok(t);
        }

        public async Task<ResponseDto<byte[]>> ExportAsync(ProcurementReportQuery query, CancellationToken cancellationToken)
        {
            ResponseDto<ReportTableDto> r = await GetReportAsync(query, cancellationToken);
            if (!r.Success || r.Data == null) return ResponseDtoHelper.BadRequest<byte[]>(r.Message);
            ReportTableDto t = r.Data;
            List<List<object>> rows = t.Rows.Select(row => t.Columns.Select(c => row.TryGetValue(c.Key, out object v) ? v : null).ToList()).ToList();
            return ResponseDtoHelper.Ok(XlsxWriter.Write(t.Title, t.Columns.Select(c => c.Title).ToList(), rows));
        }
    }
}
