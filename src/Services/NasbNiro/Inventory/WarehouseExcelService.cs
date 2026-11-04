using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.InventoryDtos;
using FinanceXlsxSheet = Services.NasbNiro.Finance.XlsxSheet;

namespace Services.NasbNiro.Inventory
{
    public interface IWarehouseExcelService
    {
        /// <summary>The empty template of receipts, issues, stock counts, items or opening balances (header row and one sample row).</summary>
        ResponseDto<byte[]> Template(string kind);

        /// <summary>Reads the file, checks every row and either shows the preview or writes ONE draft (a receipt, an issue or a stock count). Nothing is ever confirmed by the import.</summary>
        Task<ResponseDto<ExcelImportResultDto>> ImportAsync(ExcelImportRequestDto request, byte[] file, CancellationToken cancellationToken);
    }

    /// <summary>The rules that read the cells of an import file, free of the database so they can be tested alone.</summary>
    public static class ExcelImportRules
    {
        public const int MaxRows = 5000;

        public static readonly Dictionary<string, string[]> Headers = new Dictionary<string, string[]>
        {
            ["receipt"] = new[] { "کد کالا", "مقدار", "قیمت واحد (ریال)", "شماره بچ", "شماره حرارت", "کد محل", "توضیح" },
            ["issue"] = new[] { "کد کالا", "مقدار", "شماره بچ", "کد محل", "توضیح" },
            ["count"] = new[] { "کد کالا", "مقدار شمارش‌شده", "شماره بچ", "کد محل", "توضیح" },
            ["items"] = new[] { "کد کالا", "عنوان", "کد گروه", "عنوان گروه", "واحد", "نوع ردیابی (none/lot/serial)", "کد فنی", "جنس", "استاندارد", "مشخصات", "وزن واحد", "حداقل موجودی", "کد سپیدار", "کد قدیم", "کد MESC" },
            ["opening"] = new[] { "کد کالا", "کد انبار", "کد محل", "مقدار", "شماره بچ", "شماره حرارت", "شماره گواهی", "شماره برچسب", "شماره سریال", "قیمت واحد (ریال)", "توضیح" },
        };

        public sealed class Row
        {
            public int Number { get; set; }
            public string ItemCode { get; set; }
            public decimal? Quantity { get; set; }
            public decimal? UnitPrice { get; set; }
            public string Lot { get; set; }
            public string Heat { get; set; }
            public string Location { get; set; }
            public string Description { get; set; }
            public string Error { get; set; }
        }

        /// <summary>Persian and Arabic digits and separators to the ASCII ones; empty text to null.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text.Trim())
            {
                if (c >= '۰' && c <= '۹') sb.Append((char)('0' + (c - '۰')));
                else if (c >= '٠' && c <= '٩') sb.Append((char)('0' + (c - '٠')));
                else if (c == '٫') sb.Append('.');
                else if (c == '٬' || c == ',' || c == '،') continue;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static bool TryNumber(string text, out decimal value)
        {
            value = 0;
            string clean = Clean(text);
            return clean != null && decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Checks the cells of one row of a receipt, issue or count file; the first problem is written to <see cref="Row.Error"/>.</summary>
        public static List<Row> Parse(string kind, IReadOnlyList<string[]> cells)
        {
            bool receipt = kind == "receipt", count = kind == "count";
            var rows = new List<Row>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cells.Count; i++)
            {
                string[] c = cells[i];
                string At(int k) => k < c.Length ? c[k] : null;
                var row = new Row { Number = i + 2, ItemCode = Clean(At(0))?.ToUpperInvariant() };
                if (row.ItemCode == null && string.IsNullOrWhiteSpace(At(1))) continue; // an empty line
                if (row.ItemCode == null) row.Error = "کد کالا خالی است.";
                else if (!TryNumber(At(1), out decimal q)) row.Error = "مقدار عدد نیست.";
                else if (q < 0 || (q == 0 && !count)) row.Error = "مقدار باید بیشتر از صفر باشد.";
                else row.Quantity = q;
                int next = 2;
                if (receipt)
                {
                    string price = Clean(At(2));
                    if (price != null) { if (!TryNumber(price, out decimal p) || p < 0) row.Error ??= "قیمت واحد نامعتبر است."; else row.UnitPrice = Math.Round(p, 0, MidpointRounding.AwayFromZero); }
                    next = 3;
                }
                row.Lot = Clean(At(next));
                if (receipt) { row.Heat = Clean(At(next + 1)); row.Location = Clean(At(next + 2)); row.Description = Clean(At(next + 3)); }
                else { row.Location = Clean(At(next + 1)); row.Description = Clean(At(next + 2)); }
                if (row.Error == null)
                {
                    string key = row.ItemCode + "|" + row.Lot + "|" + row.Location + (receipt ? "|" + row.UnitPrice : "");
                    if (!seen.Add(key)) row.Error = "ردیف تکراری (کالا، بچ و محل یکسان)؛ مقدارها را در یک ردیف جمع کنید.";
                }
                rows.Add(row);
            }
            return rows;
        }
    }

    public class WarehouseExcelService : IWarehouseExcelService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IInventoryDocumentService _documents;
        private readonly IInventoryWorkflowService _workflow;

        public WarehouseExcelService(IUnitOfWork uow, IInventoryUserContext user, IInventoryDocumentService documents, IInventoryWorkflowService workflow)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _documents = documents ?? throw new ArgumentNullException(nameof(documents));
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        }

        private static readonly Dictionary<string, string> Titles = new Dictionary<string, string>
        {
            ["receipt"] = "الگوی رسید", ["issue"] = "الگوی حواله", ["count"] = "الگوی انبارگردانی", ["items"] = "الگوی کالاها", ["opening"] = "الگوی موجودی اول دوره",
        };

        public ResponseDto<byte[]> Template(string kind)
        {
            kind = (kind ?? "").Trim().ToLowerInvariant();
            if (!ExcelImportRules.Headers.TryGetValue(kind, out string[] header)) return ResponseDtoHelper.BadRequest<byte[]>("نوع الگو نامعتبر است (receipt، issue، count، items یا opening).");
            var sheet = new FinanceXlsxSheet { Name = Titles[kind], Header = header.ToList() };
            object[] sample = kind == "receipt" ? new object[] { "(کد کالا)", 10m, 1000000m, "LOT-001", "", "", "نمونه" }
                : kind == "issue" ? new object[] { "(کد کالا)", 2m, "", "", "نمونه" }
                : kind == "count" ? new object[] { "(کد کالا)", 8m, "", "", "نمونه" }
                : kind == "items" ? new object[] { "(کد کالا)", "(عنوان)", "01", "متریال", "kg", "lot", "", "", "", "", 1.5m, 0m, "", "", "" }
                : new object[] { "(کد کالا)", "(کد انبار)", "", 5m, "LOT-001", "", "", "", "", 1000000m, "نمونه" };
            sheet.Rows.Add(sample);
            var guide = new FinanceXlsxSheet { Name = "راهنما", Header = new List<string> { "راهنما" } };
            guide.Rows.Add(new object[] { "ردیف نمونه را پاک کنید و از ردیف دوم داده‌ها را بنویسید؛ ردیف عنوان ستون‌ها را تغییر ندهید." });
            guide.Rows.Add(new object[] { "فایل ابتدا بررسی و پیش‌نمایش می‌شود؛ سپس فقط یک سند پیش‌نویس ساخته می‌شود و هرگز خودکار تایید نمی‌شود." });
            guide.Rows.Add(new object[] { "کالای سریال‌دار با این فایل وارد نمی‌شود؛ آن را در خود سند ثبت کنید." });
            return ResponseDtoHelper.Ok(SimpleXlsx.Build(new List<FinanceXlsxSheet> { sheet, guide }, true));
        }

        public async Task<ResponseDto<ExcelImportResultDto>> ImportAsync(ExcelImportRequestDto request, byte[] file, CancellationToken ct)
        {
            if (request == null || file == null || file.Length == 0) return ResponseDtoHelper.BadRequest<ExcelImportResultDto>("فایل را انتخاب کنید.");
            string kind = (request.Kind ?? "").Trim().ToLowerInvariant();
            if (kind != "receipt" && kind != "issue" && kind != "count") return ResponseDtoHelper.BadRequest<ExcelImportResultDto>("نوع ورود فقط رسید، حواله یا انبارگردانی است.");
            if (!_user.IsInRole(kind == "count" ? InventoryRoles.Counts : InventoryRoles.Documents)) return ResponseDtoHelper.BadRequest<ExcelImportResultDto>(InventoryMessages.NotAllowed);
            Warehouse warehouse = await _uow.Set<Warehouse>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == request.WarehouseId && !w.Deleted, ct);
            if (warehouse == null) return ResponseDtoHelper.BadRequest<ExcelImportResultDto>(InventoryMessages.WarehouseNotFound);
            if (kind == "issue" && !request.MaterialRequestId.HasValue) return ResponseDtoHelper.BadRequest<ExcelImportResultDto>("حواله فقط بر اساس درخواست کالای تاییدشده صادر می‌شود؛ درخواست را انتخاب کنید.");

            List<string[]> cells;
            try
            {
                List<string> names = XlsxReader.SheetNames(file);
                Office.XlsxSheet sheet = XlsxReader.Read(file, names.FirstOrDefault(), ExcelImportRules.MaxRows + 2);
                cells = sheet.Rows.Where(r => r.RowNumber > 1).OrderBy(r => r.RowNumber).Select(r =>
                {
                    int width = Math.Max(1, Math.Min(sheet.MaxColumn + 1, 12));
                    var arr = new string[width];
                    for (int c = 0; c < width; c++) arr[c] = r.Cell(c)?.AsText();
                    return arr;
                }).ToList();
            }
            catch (XlsxFormatException ex) { return ResponseDtoHelper.BadRequest<ExcelImportResultDto>(ex.Message); }
            catch (Exception) { return ResponseDtoHelper.BadRequest<ExcelImportResultDto>("فایل xlsx خوانده نشد."); }
            if (cells.Count > ExcelImportRules.MaxRows) return ResponseDtoHelper.BadRequest<ExcelImportResultDto>($"حداکثر {ExcelImportRules.MaxRows} ردیف در هر بار.");

            List<ExcelImportRules.Row> rows = ExcelImportRules.Parse(kind, cells);
            if (rows.Count == 0) return ResponseDtoHelper.BadRequest<ExcelImportResultDto>("فایل هیچ ردیف داده‌ای ندارد.");

            // the database checks: item, tracking, location, lot
            List<string> codes = rows.Where(r => r.ItemCode != null).Select(r => r.ItemCode).Distinct().ToList();
            var items = (await _uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && codes.Contains(i.Code)).Select(i => new { i.Id, i.Code, i.Title, i.TrackingMode, i.IsActive }).ToListAsync(ct))
                .ToDictionary(i => i.Code, StringComparer.OrdinalIgnoreCase);
            var locations = (await _uow.Set<WarehouseLocation>().AsNoTracking().Where(l => l.WarehouseId == warehouse.Id && !l.Deleted).Select(l => new { l.Id, l.Code }).ToListAsync(ct))
                .ToDictionary(l => l.Code, l => l.Id, StringComparer.OrdinalIgnoreCase);
            List<int> itemIds = items.Values.Select(i => i.Id).ToList();
            var lots = await _uow.Set<InventoryLot>().AsNoTracking().Where(l => !l.Deleted && itemIds.Contains(l.ItemId)).Select(l => new { l.Id, l.ItemId, l.LotNumber }).ToListAsync(ct);

            var result = new ExcelImportResultDto { Kind = kind, Preview = request.Preview, RowCount = rows.Count };
            var lines = new List<SaveDocumentLineDto>();
            var counts = new List<CountLineEntryDto>();
            foreach (ExcelImportRules.Row r in rows)
            {
                string message = r.Error;
                int itemId = 0; int? locationId = null; int? lotId = null;
                if (message == null)
                {
                    if (!items.TryGetValue(r.ItemCode, out var item)) message = "کالایی با این کد پیدا نشد.";
                    else if (!item.IsActive) message = "کالا غیرفعال است.";
                    else if (item.TrackingMode == ItemTrackingMode.Serial) message = "کالای سریال‌دار با فایل وارد نمی‌شود؛ در سند ثبت کنید.";
                    else
                    {
                        itemId = item.Id;
                        if (r.Location != null) { if (locations.TryGetValue(r.Location, out int lid)) locationId = lid; else message = "کد محل در این انبار نیست."; }
                        if (message == null && item.TrackingMode == ItemTrackingMode.Lot && kind != "receipt" && r.Lot == null) message = "برای کالای بچ‌دار شماره بچ لازم است.";
                        if (message == null && kind != "receipt" && r.Lot != null)
                        {
                            var lot = lots.FirstOrDefault(l => l.ItemId == itemId && string.Equals(l.LotNumber, r.Lot, StringComparison.OrdinalIgnoreCase));
                            if (lot == null) message = "بچ با این شماره برای کالا پیدا نشد."; else lotId = lot.Id;
                        }
                        if (message == null && kind == "receipt" && item.TrackingMode == ItemTrackingMode.Lot && r.Lot == null) message = "برای کالای بچ‌دار شماره بچ لازم است.";
                    }
                }
                result.Rows.Add(new ExcelPreviewRowDto
                {
                    Row = r.Number, ItemCode = r.ItemCode, ItemTitle = r.ItemCode != null && items.TryGetValue(r.ItemCode, out var it) ? it.Title : null, Quantity = r.Quantity, UnitPrice = r.UnitPrice,
                    Extra = string.Join(" | ", new[] { r.Lot, r.Location }.Where(x => x != null)), Ok = message == null, Message = message,
                });
                if (message != null) continue;
                if (kind == "count")
                    counts.Add(new CountLineEntryDto { ItemId = itemId, LocationId = locationId, LotId = lotId, CountedQuantity = r.Quantity, Note = r.Description });
                else
                    lines.Add(new SaveDocumentLineDto { ItemId = itemId, Quantity = r.Quantity.Value, UnitPrice = r.UnitPrice, LotId = lotId, LotNumber = kind == "receipt" ? r.Lot : null, HeatNumber = r.Heat, LocationId = locationId, Description = r.Description });
            }
            result.ValidRows = result.Rows.Count(x => x.Ok);
            result.ErrorRows = result.Rows.Count - result.ValidRows;
            if (kind == "receipt" && result.Rows.Any(x => x.Ok && x.UnitPrice == null)) result.Warnings.Add("بعضی ردیف‌ها قیمت واحد ندارند؛ حسابدار پیش از تایید باید قیمت را کامل کند.");
            if (request.Preview) return ResponseDtoHelper.Ok(result);
            if (result.ErrorRows > 0) return ResponseDtoHelper.BadRequest<ExcelImportResultDto>($"{result.ErrorRows} ردیف خطا دارد؛ فایل را اصلاح کنید و دوباره بفرستید (هیچ سندی ساخته نشد).");

            DateTime date = (request.DocumentDate ?? DateTime.Today).Date;
            if (kind == "count")
            {
                var started = await _workflow.StartCountAsync(new StartStockCountDto { Kind = StockCountKind.Cycle, CountDate = date, WarehouseId = warehouse.Id, Description = request.Description ?? "ورود از اکسل" }, ct);
                if (!started.Success) return ResponseDtoHelper.BadRequest<ExcelImportResultDto>(started.Message);
                var saved = await _workflow.SaveCountLinesAsync(new SaveCountLinesDto { Id = started.Data.Id, Lines = counts }, ct);
                if (!saved.Success) return ResponseDtoHelper.BadRequest<ExcelImportResultDto>(saved.Message);
                result.DraftId = started.Data.Id;
                result.DraftKind = "count";
            }
            else
            {
                var dto = new SaveDocumentDto
                {
                    Type = kind == "receipt" ? InventoryDocumentType.PurchaseReceipt : InventoryDocumentType.Issue, DocumentDate = date, WarehouseId = warehouse.Id,
                    ProviderId = kind == "receipt" ? request.ProviderId : null, MaterialRequestId = kind == "issue" ? request.MaterialRequestId : null,
                    Description = request.Description ?? "ورود از اکسل", Lines = lines,
                };
                var saved = await _documents.SaveDraftAsync(dto, ct);
                if (!saved.Success) return ResponseDtoHelper.BadRequest<ExcelImportResultDto>(saved.Message);
                result.DraftId = saved.Data.Id;
                result.DraftKind = "document";
            }
            return ResponseDtoHelper.Ok(result);
        }
    }
}
