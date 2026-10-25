using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.ProcurementDtos;

namespace Services.NasbNiro.Ai
{
    // The seven seats. Each one runs as its own service user (see AiActing), so the services check its roles like anyone else's.
    // What a seat leaves is a draft (WH-REORDER) or a note (all the others); no seat submits, approves, signs or pays.

    public sealed class AiSeatRunContext
    {
        public AiSeat Seat { get; set; }
        public AiSeatDefinition Definition { get; set; }
        public int RunId { get; set; }

        /// <summary>The data day: today for a scheduled run, the day asked for on a run by hand.</summary>
        public DateTime AsOf { get; set; }
    }

    public sealed class AiSeatJobResult
    {
        public AiSeatRunStatus Status { get; set; }
        public string Summary { get; set; }
        public string Error { get; set; }

        public static AiSeatJobResult Done(string summary) => new AiSeatJobResult { Status = AiSeatRunStatus.Succeeded, Summary = summary };
        public static AiSeatJobResult Skip(string reason, string summary = null) => new AiSeatJobResult { Status = AiSeatRunStatus.Skipped, Error = reason, Summary = summary };
        public static AiSeatJobResult Fail(string error) => new AiSeatJobResult { Status = AiSeatRunStatus.Failed, Error = error };
    }

    public interface IAiSeatJob
    {
        string Code { get; }
        Task<AiSeatJobResult> RunAsync(AiSeatRunContext context, CancellationToken ct);
    }

    public sealed class AiSeatAnswer
    {
        public string Text { get; set; }
        public bool Verified { get; set; }
        public bool Degraded { get; set; }
        public string Model { get; set; }
        public string Reason { get; set; }
        public List<string> Tools { get; set; } = new List<string>();
    }

    /// <summary>One question to the model on behalf of a seat: the same tools, prompt and numeric check as the chat, but no conversation is kept.</summary>
    public interface IAiSeatAsk
    {
        /// <summary>
        /// <paramref name="withTools"/> false offers the model no tool: the question carries all the data (letters, minutes, reports), so no text of it can be copied into the arguments of a tool call
        /// (and from there into the audit).
        /// </summary>
        Task<AiSeatAnswer> AskAsync(string question, CancellationToken ct, bool withTools = true);
    }

    public class AiSeatAsk : IAiSeatAsk
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ILlmClient _llm;
        private readonly IToolRegistry _tools;
        private readonly IAnswerVerifier _verifier;
        private readonly IAiAudit _audit;
        private readonly IConfiguration _configuration;

        public AiSeatAsk(IUnitOfWork uow, IInventoryUserContext user, ILlmClient llm, IToolRegistry tools, IAnswerVerifier verifier, IAiAudit audit, IConfiguration configuration)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _configuration = configuration;
        }

        public async Task<AiSeatAnswer> AskAsync(string question, CancellationToken ct, bool withTools = true)
        {
            int timeout = int.TryParse(_configuration?[AiConfigKeys.ChatTimeoutSeconds], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s > 0 ? s : 60;
            var total = System.Diagnostics.Stopwatch.StartNew();
            AiPromptVersion prompt = await AiChatService.ActivePromptAsync(_uow, AiChatService.ChatPromptCode, _user.UserId, ct);
            string systemPrompt = prompt?.Text ?? AiChatService.SeedPromptText;
            string promptHash = AiText.Sha256Hex(systemPrompt + question);
            var messages = new List<LlmMessage> { new LlmMessage { Role = "system", Content = systemPrompt }, new LlmMessage { Role = "user", Content = question } };
            List<LlmToolSpec> specs = withTools ? _tools.Offered().Select(t => new LlmToolSpec { Name = t.Name, Description = t.Description, SchemaJson = t.SchemaJson }).ToList() : new List<LlmToolSpec>();

            AiLoopResult loop = await AiChatLoop.RunAsync(_llm, _tools, _verifier, messages, specs, question, new List<string>(), timeout, ct);
            foreach (var x in loop.Results)
                _audit.Add(AiAuditKinds.Tool, $"{x.Result.Name} {AiText.Cut(x.Arguments, 2000)} allowed={x.Result.Allowed} chars={x.Result.Chars} hash={x.Result.Hash}", loop.Model, x.Result.DurationMs, prompt?.Id, promptHash);
            total.Stop();
            if (loop.Degraded) _audit.Add(AiAuditKinds.Degraded, AiText.Cut(loop.DegradedReason, 1000), null, (int)total.ElapsedMilliseconds, prompt?.Id, promptHash);
            if (loop.Verification != null && !loop.Verification.Verified)
                _audit.Add(AiAuditKinds.Unverified, "tokens: " + string.Join(" ", loop.Verification.OffendingTokens), loop.Model, (int)total.ElapsedMilliseconds, prompt?.Id, promptHash);
            await _uow.SaveChangesAsync(ct);

            return new AiSeatAnswer
            {
                Text = loop.Degraded ? null : AiText.YeKe(loop.Answer), Verified = !loop.Degraded && loop.Verified, Degraded = loop.Degraded, Model = loop.Degraded ? null : loop.Model,
                Reason = loop.DegradedReason, Tools = loop.Results.Select(r => r.Result.Name).ToList(),
            };
        }
    }

    internal static class AiSeatTexts
    {
        public const string LlmDown = "تحلیل هوشمند در دسترس نیست";
        public const string Cancelled = "اجرا هنگام توقف سرویس لغو شد.";
        public const int MaxItemsPerRun = 10;

        public static int DayKey(DateTime d) => d.Year * 10000 + d.Month * 100 + d.Day;

        public static string StripHtml(string html, int max)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            string text = Regex.Replace(html, "<(br|/p|/div|/li)[^>]*>", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            text = Regex.Replace(text, "<[^>]+>", " ", RegexOptions.CultureInvariant);
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
            return AiText.Cut(text, max);
        }

        public static string Num(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    }

    // ============================================================================================ CEO-BRIEF

    public class CeoBriefSeatJob : IAiSeatJob
    {
        public const string Question = "گزارش روزانه مدیرعامل: خلاصه داشبورد، هشدارها، نقدینگی و پروژه‌های قرمز و زرد";
        private readonly IUnitOfWork _uow;
        private readonly IAiSeatAsk _ask;
        private readonly IAiNoteWriter _notes;

        public CeoBriefSeatJob(IUnitOfWork uow, IAiSeatAsk ask, IAiNoteWriter notes)
        {
            _uow = uow; _ask = ask; _notes = notes;
        }

        public string Code => AiSeatCodes.CeoBrief;

        public async Task<AiSeatJobResult> RunAsync(AiSeatRunContext context, CancellationToken ct)
        {
            int key = AiSeatTexts.DayKey(context.AsOf);
            if ((await _notes.ExistingTargetsAsync(Code, AiNoteTypes.CeoBrief, new[] { key }, ct)).Contains(key)) return AiSeatJobResult.Done("brief exists for " + key);
            AiSeatAnswer a = await _ask.AskAsync(Question, ct);
            if (a.Degraded) return AiSeatJobResult.Skip(AiSeatTexts.LlmDown + ": " + AiText.Cut(a.Reason, 300));
            bool created = await _notes.TryAddAsync(new AiNote
            {
                SeatCode = Code, TargetType = AiNoteTypes.CeoBrief, TargetId = key, Title = "گزارش روزانه مدیرعامل - " + InventoryText.PersianDate(context.AsOf),
                Text = a.Text, Verified = a.Verified, SeatRunId = context.RunId,
            }, ct);
            return AiSeatJobResult.Done($"notes={(created ? 1 : 0)}; verified={a.Verified.ToString().ToLowerInvariant()}; model={a.Model}");
        }
    }

    // ============================================================================================ PMO-WEEKLY

    public class PmoWeeklySeatJob : IAiSeatJob
    {
        private readonly IUnitOfWork _uow;
        private readonly IAiSeatAsk _ask;
        private readonly IAiNoteWriter _notes;

        public PmoWeeklySeatJob(IUnitOfWork uow, IAiSeatAsk ask, IAiNoteWriter notes)
        {
            _uow = uow; _ask = ask; _notes = notes;
        }

        public string Code => AiSeatCodes.PmoWeekly;

        private static decimal? Number(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object) return null;
            foreach (JsonProperty p in root.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDecimal(out decimal d)) return d;
            return null;
        }

        private static JsonElement Child(JsonElement root, string name)
        {
            if (root.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty p in root.EnumerateObject())
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
            return default;
        }

        /// <summary>The figures of a weekly report as a plain sentence (they go into the question, so the numeric check accepts them).</summary>
        public static string Digest(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "محتوای گزارش خالی است.";
            try
            {
                using JsonDocument doc = JsonDocument.Parse(content);
                JsonElement root = doc.RootElement;
                JsonElement evm = Child(root, "Evm");
                var parts = new List<string>();
                decimal? physical = Number(evm, "PhysicalPercent"), planned = Number(evm, "PlannedPercent"), spi = Number(evm, "Spi"), cpi = Number(evm, "Cpi");
                if (physical.HasValue) parts.Add("پیشرفت واقعی " + AiSeatTexts.Num(Math.Round(physical.Value, 1)) + " درصد");
                if (planned.HasValue) parts.Add("پیشرفت برنامه‌ای " + AiSeatTexts.Num(Math.Round(planned.Value, 1)) + " درصد");
                if (spi.HasValue) parts.Add("شاخص زمان SPI " + AiSeatTexts.Num(Math.Round(spi.Value, 2)));
                if (cpi.HasValue) parts.Add("شاخص هزینه CPI " + AiSeatTexts.Num(Math.Round(cpi.Value, 2)));
                JsonElement late = Child(root, "LateActivities");
                if (late.ValueKind == JsonValueKind.Array) parts.Add("فعالیت‌های عقب‌افتاده " + late.GetArrayLength().ToString(CultureInfo.InvariantCulture));
                decimal? changes = Number(root, "OpenChanges"), guarantees = Number(root, "GuaranteeAlerts");
                if (changes.HasValue) parts.Add("تغییرهای باز " + AiSeatTexts.Num(changes.Value));
                if (guarantees.HasValue) parts.Add("هشدار ضمانت‌نامه " + AiSeatTexts.Num(guarantees.Value));
                return parts.Count == 0 ? "گزارش داده‌ی عددی ندارد." : string.Join("؛ ", parts) + ".";
            }
            catch (JsonException)
            {
                return "محتوای گزارش خوانده نشد.";
            }
        }

        public async Task<AiSeatJobResult> RunAsync(AiSeatRunContext context, CancellationToken ct)
        {
            DateTime from = context.AsOf.Date.AddDays(-7);
            List<PcReportIssue> reports = await _uow.Set<PcReportIssue>().AsNoTracking()
                .Where(r => r.Kind == PcReportKind.InternalWeekly && r.DataDate <= context.AsOf.Date && r.DataDate >= from).OrderByDescending(r => r.Id).Take(50).ToListAsync(ct);
            HashSet<int> have = await _notes.ExistingTargetsAsync(Code, AiNoteTypes.PcWeeklyReport, reports.Select(r => r.Id), ct);
            List<PcReportIssue> todo = reports.Where(r => !have.Contains(r.Id)).Take(AiSeatTexts.MaxItemsPerRun).ToList();
            int created = 0;
            foreach (PcReportIssue r in todo)
            {
                string code = await _uow.Set<PcContractTerms>().AsNoTracking().Where(t => t.ContractId == r.ContractId && !t.Deleted).Select(t => t.ProjectCode).FirstOrDefaultAsync(ct);
                string question = $"روایت هفتگی پروژه {code ?? "بدون کد"} برای گزارش {r.Number} با تاریخ داده {InventoryText.PersianDate(r.DataDate)}: {Digest(r.Content)} " +
                                  "یک روایت کوتاه فارسی برای مدیر دفتر مدیریت پروژه بنویس: وضعیت، ریسک‌ها و پیشنهاد پیگیری. فقط از اعداد همین پرسش استفاده کن.";
                AiSeatAnswer a = await _ask.AskAsync(question, ct);
                if (a.Degraded) return AiSeatJobResult.Skip(AiSeatTexts.LlmDown + ": " + AiText.Cut(a.Reason, 300), $"notes={created}");
                if (await _notes.TryAddAsync(new AiNote
                {
                    SeatCode = Code, TargetType = AiNoteTypes.PcWeeklyReport, TargetId = r.Id, Title = $"روایت گزارش هفتگی {r.Number}", Text = a.Text, Verified = a.Verified, SeatRunId = context.RunId,
                }, ct)) created++;
            }
            return AiSeatJobResult.Done($"notes={created}");
        }
    }

    // ============================================================================================ WH-REORDER

    public class WhReorderSeatJob : IAiSeatJob
    {
        public const int MaxLines = 100;
        private readonly IUnitOfWork _uow;
        private readonly IInventoryStockService _stock;
        private readonly IProcurementService _procurement;

        public WhReorderSeatJob(IUnitOfWork uow, IInventoryStockService stock, IProcurementService procurement)
        {
            _uow = uow; _stock = stock; _procurement = procurement;
        }

        public string Code => AiSeatCodes.WhReorder;

        /// <summary>The quantity to buy: up to the minimum stock, less what is already on order. (★ the item master has no separate reorder quantity.)</summary>
        public static decimal ReorderQuantity(decimal minStock, decimal onHand, decimal onOrder = 0) => Math.Max(0, minStock - onHand - Math.Max(0, onOrder));

        public async Task<AiSeatJobResult> RunAsync(AiSeatRunContext context, CancellationToken ct)
        {
            var low = await _stock.GetItemStocksAsync(new StockQuery { LowStockOnly = true, PageSize = 0 }, ct);
            if (!low.Success) return AiSeatJobResult.Fail(low.Message);
            List<ItemStockDto> rows = low.Data.Where(i => ReorderQuantity(i.MinStock, i.OnHand) > 0).ToList();
            if (rows.Count == 0) return AiSeatJobResult.Done("drafts=0; lines=0");

            // an item on a request that is not final (draft, submitted, approved or ordered) is covered: it is not asked for again
            List<int> ids = rows.Select(r => r.ItemId).ToList();
            List<int> open = await _uow.Set<PurchaseRequestLine>().AsNoTracking()
                .Where(l => l.ItemId != null && ids.Contains(l.ItemId.Value) && !l.PurchaseRequest.Deleted
                            && (l.PurchaseRequest.Status == PurchaseRequestStatus.Draft || l.PurchaseRequest.Status == PurchaseRequestStatus.Submitted
                                || l.PurchaseRequest.Status == PurchaseRequestStatus.Approved || l.PurchaseRequest.Status == PurchaseRequestStatus.Ordered))
                .Select(l => l.ItemId.Value).Distinct().ToListAsync(ct);
            // what is on order: the part of an open purchase order line that has not been received yet
            var onOrderRows = await _uow.Set<PurchaseOrderLine>().AsNoTracking()
                .Where(l => l.ItemId != null && ids.Contains(l.ItemId.Value) && !l.PurchaseOrder.Deleted
                            && (l.PurchaseOrder.Status == PurchaseOrderStatus.Draft || l.PurchaseOrder.Status == PurchaseOrderStatus.Approved
                                || l.PurchaseOrder.Status == PurchaseOrderStatus.Released || l.PurchaseOrder.Status == PurchaseOrderStatus.PartlyReceived))
                .Select(l => new { ItemId = l.ItemId.Value, Left = l.Quantity - l.ReceivedQuantity }).ToListAsync(ct);
            Dictionary<int, decimal> onOrder = onOrderRows.Where(x => x.Left > 0).GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Left));
            decimal OnOrder(int itemId) => onOrder.TryGetValue(itemId, out decimal v) ? v : 0m;
            List<ItemStockDto> todo = rows.Where(r => !open.Contains(r.ItemId) && ReorderQuantity(r.MinStock, r.OnHand, OnOrder(r.ItemId)) > 0)
                .OrderBy(r => r.ItemCode, StringComparer.Ordinal).Take(MaxLines).ToList();
            if (todo.Count == 0) return AiSeatJobResult.Done("drafts=0; lines=0");

            var dto = new SavePurchaseRequestDto
            {
                Source = PurchaseRequestSource.ReorderPoint, Kind = PurchaseKind.Goods, RequestDate = context.AsOf.Date, Submit = false,
                Description = "پیش‌نویس خودکار صندلی انبار: موجودی کالاهای فهرست‌شده از حداقل موجودی کمتر است. مقدار پیشنهادی برابر حداقل موجودی منهای موجودی فعلی و منهای مقدار در راه است. بررسی، ویرایش و ارسال با واحد برنامه‌ریزی است.",
            };
            foreach (ItemStockDto i in todo)
                dto.Lines.Add(new SavePurchaseRequestLineDto
                {
                    ItemId = i.ItemId, Unit = i.Unit, Quantity = ReorderQuantity(i.MinStock, i.OnHand, OnOrder(i.ItemId)),
                    Description = $"{i.ItemTitle}: موجودی {AiSeatTexts.Num(i.OnHand)} {i.Unit} کمتر از حداقل {AiSeatTexts.Num(i.MinStock)} است{(OnOrder(i.ItemId) > 0 ? $"؛ {AiSeatTexts.Num(OnOrder(i.ItemId))} {i.Unit} در راه است" : string.Empty)}",
                });
            var saved = await _procurement.SaveRequestAsync(dto, ct);
            if (!saved.Success) return AiSeatJobResult.Fail(AiText.Cut(saved.Message, 500));
            return AiSeatJobResult.Done($"drafts=1; lines={todo.Count}; request={saved.Data.Number}");
        }
    }

    // ============================================================================================ PROC-COMPARE

    public class ProcCompareSeatJob : IAiSeatJob
    {
        public const string CommitteeSentence = "توجه: این یادداشت فقط کمک‌تحلیل است؛ تصمیم انتخاب تأمین‌کننده با کمیته انتخاب تأمین‌کننده است.";
        private readonly IUnitOfWork _uow;
        private readonly IAiSeatAsk _ask;
        private readonly IAiNoteWriter _notes;

        public ProcCompareSeatJob(IUnitOfWork uow, IAiSeatAsk ask, IAiNoteWriter notes)
        {
            _uow = uow; _ask = ask; _notes = notes;
        }

        public string Code => AiSeatCodes.ProcCompare;

        public async Task<AiSeatJobResult> RunAsync(AiSeatRunContext context, CancellationToken ct)
        {
            // an inquiry is complete when it is still open and has as many quotes as it requires (at least one)
            List<int> candidateIds = await _uow.Set<PurchaseInquiry>().AsNoTracking()
                .Where(i => !i.Deleted && i.Status == InquiryStatus.Open && i.Quotes.Count >= (i.RequiredQuotes < 1 ? 1 : i.RequiredQuotes))
                .OrderByDescending(i => i.Id).Select(i => i.Id).Take(200).ToListAsync(ct);
            HashSet<int> have = await _notes.ExistingTargetsAsync(Code, AiNoteTypes.PurchaseRfq, candidateIds, ct);
            List<int> todo = candidateIds.Where(i => !have.Contains(i)).Take(AiSeatTexts.MaxItemsPerRun).ToList();
            int created = 0;
            foreach (int id in todo)
            {
                PurchaseInquiry inq = await _uow.Set<PurchaseInquiry>().AsNoTracking().Include(i => i.PurchaseRequest).Include(i => i.Quotes).FirstOrDefaultAsync(i => i.Id == id, ct);
                if (inq == null) continue;
                List<int> providerIds = inq.Quotes.Select(q => q.ProviderId).Distinct().ToList();
                Dictionary<int, string> names = await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => providerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
                var lines = new StringBuilder();
                int n = 0;
                foreach (SupplierQuote q in inq.Quotes.OrderBy(q => q.TotalAmount).ThenBy(q => q.Id))
                {
                    n++;
                    lines.Append($"پیشنهاد {n}: تامین‌کننده «{(names.TryGetValue(q.ProviderId, out string nm) ? nm : "نامشخص")}»، مبلغ کل {q.TotalAmount.ToString("0.##", CultureInfo.InvariantCulture)} ریال، "
                                 + $"زمان تحویل {(q.DeliveryDays.HasValue ? q.DeliveryDays.Value.ToString(CultureInfo.InvariantCulture) + " روز" : "نامشخص")}، "
                                 + $"تأیید فنی {(q.TechnicallyAccepted ? "دارد" : "ندارد")}، شرایط پرداخت {(string.IsNullOrWhiteSpace(q.PaymentTerms) ? "نامشخص" : q.PaymentTerms)}. ");
                }
                string question = $"یادداشت مقایسه استعلام {inq.Number} برای درخواست خرید {inq.PurchaseRequest?.Number}: {lines}" +
                                  "یک یادداشت کوتاه فارسی برای کمیته انتخاب تأمین‌کننده بنویس: مقایسه قیمت، زمان تحویل و وضعیت فنی؛ تصمیم نگیر. فقط از اعداد همین پرسش استفاده کن.";
                AiSeatAnswer a = await _ask.AskAsync(question, ct, withTools: false);
                if (a.Degraded) return AiSeatJobResult.Skip(AiSeatTexts.LlmDown + ": " + AiText.Cut(a.Reason, 300), $"notes={created}");
                string text = a.Text + "\n\n" + CommitteeSentence;
                if (await _notes.TryAddAsync(new AiNote
                {
                    SeatCode = Code, TargetType = AiNoteTypes.PurchaseRfq, TargetId = inq.Id, Title = $"یادداشت مقایسه استعلام {inq.Number}", Text = text, Verified = a.Verified, SeatRunId = context.RunId,
                }, ct)) created++;
            }
            return AiSeatJobResult.Done($"notes={created}");
        }
    }

    // ============================================================================================ QC-NCR-ASSIST

    public class QcNcrAssistSeatJob : IAiSeatJob
    {
        public const int SimilarCount = 3;
        public const double MinSimilarity = 0.1;
        private readonly IUnitOfWork _uow;
        private readonly IAiSeatAsk _ask;
        private readonly IAiNoteWriter _notes;

        public QcNcrAssistSeatJob(IUnitOfWork uow, IAiSeatAsk ask, IAiNoteWriter notes)
        {
            _uow = uow; _ask = ask; _notes = notes;
        }

        public string Code => AiSeatCodes.QcNcrAssist;

        public async Task<AiSeatJobResult> RunAsync(AiSeatRunContext context, CancellationToken ct)
        {
            List<int> openIds = await _uow.Set<NonConformance>().AsNoTracking().Where(n => !n.Deleted && n.Status == NcrStatus.Open).OrderBy(n => n.Id).Select(n => n.Id).Take(200).ToListAsync(ct);
            HashSet<int> have = await _notes.ExistingTargetsAsync(Code, AiNoteTypes.QualityNcr, openIds, ct);
            List<int> todo = openIds.Where(i => !have.Contains(i)).Take(AiSeatTexts.MaxItemsPerRun).ToList();
            if (todo.Count == 0) return AiSeatJobResult.Done("notes=0");

            var past = await _uow.Set<NonConformance>().AsNoTracking().Where(n => !n.Deleted).OrderByDescending(n => n.Id).Take(300)
                .Select(n => new { n.Id, n.Number, n.ReportDate, n.Status, n.Severity, n.Description, ItemTitle = n.Item.Title }).ToListAsync(ct);
            int created = 0;
            foreach (int id in todo)
            {
                var ncr = await _uow.Set<NonConformance>().AsNoTracking().Where(n => n.Id == id).Select(n => new { n.Id, n.Number, n.Source, n.Quantity, n.Description, n.Requirement, ItemTitle = n.Item.Title, ItemCode = n.Item.Code }).FirstOrDefaultAsync(ct);
                if (ncr == null) continue;
                HashSet<string> mine = AiTextSimilarity.Words(ncr.Description + " " + ncr.Requirement + " " + ncr.ItemTitle);
                var similar = past.Where(p => p.Id != id)
                    .Select(p => new { Row = p, Score = AiTextSimilarity.Jaccard(mine, AiTextSimilarity.Words(p.Description + " " + p.ItemTitle)) })
                    .Where(x => x.Score >= MinSimilarity).OrderByDescending(x => x.Score).ThenByDescending(x => x.Row.Id).Take(SimilarCount).ToList();

                string question = $"پیشنهاد دسته‌بندی برای گزارش عدم انطباق {ncr.Number}: کالا «{ncr.ItemTitle}» ({ncr.ItemCode})، منبع {ncr.Source}، مقدار {AiSeatTexts.Num(ncr.Quantity)}، " +
                                  $"شرح: {AiText.Cut(AiSeatTexts.StripHtml(ncr.Description, 800), 800)}، الزام: {AiText.Cut(AiSeatTexts.StripHtml(ncr.Requirement, 400), 400)}. " +
                                  "دسته‌بندی را Minor یا Major پیشنهاد بده و دلیلش را بگو، یک روش ریشه‌یابی (مانند پنج چرا یا استخوان ماهی) پیشنهاد کن. تصمیم نهایی با کیفیت است. فقط از اعداد همین پرسش استفاده کن.";
                AiSeatAnswer a = await _ask.AskAsync(question, ct, withTools: false);
                if (a.Degraded) return AiSeatJobResult.Skip(AiSeatTexts.LlmDown + ": " + AiText.Cut(a.Reason, 300), $"notes={created}");

                var text = new StringBuilder(a.Text);
                text.Append("\n\nگزارش‌های مشابه گذشته: ");
                if (similar.Count == 0) text.Append("مورد مشابهی پیدا نشد.");
                else text.Append(string.Join("؛ ", similar.Select(x => $"{x.Row.Number} ({InventoryText.PersianDate(x.Row.ReportDate)}، {(x.Row.Severity == NcrSeverity.Major ? "Major" : x.Row.Severity == NcrSeverity.Minor ? "Minor" : "بدون دسته")})")));
                text.Append("\nاین پیشنهاد فقط راهنماست؛ دسته‌بندی و تصمیم با مسئول کیفیت است.");
                if (await _notes.TryAddAsync(new AiNote
                {
                    SeatCode = Code, TargetType = AiNoteTypes.QualityNcr, TargetId = id, Title = $"پیشنهاد برای گزارش عدم انطباق {ncr.Number}", Text = text.ToString(), Verified = a.Verified, SeatRunId = context.RunId,
                }, ct)) created++;
            }
            return AiSeatJobResult.Done($"notes={created}");
        }
    }

    // ============================================================================================ FIN-ANOMALY

    public class FinAnomalySeatJob : IAiSeatJob
    {
        private readonly IUnitOfWork _uow;
        private readonly IAiNoteWriter _notes;
        private readonly IConfiguration _configuration;

        public FinAnomalySeatJob(IUnitOfWork uow, IAiNoteWriter notes, IConfiguration configuration)
        {
            _uow = uow; _notes = notes; _configuration = configuration;
        }

        public string Code => AiSeatCodes.FinAnomaly;

        private decimal Setting(string key, decimal fallback) =>
            decimal.TryParse(_configuration?[key], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v) && v > 0 ? v : fallback;

        public async Task<AiSeatJobResult> RunAsync(AiSeatRunContext context, CancellationToken ct)
        {
            DateTime asOf = context.AsOf.Date;
            int lookback = (int)Setting(AiConfigKeys.FinLookbackDays, 60);
            decimal roundAmount = Setting(AiConfigKeys.FinRoundAmount, 1_000_000_000m);
            decimal roundUnit = Setting(AiConfigKeys.FinRoundUnit, 100_000_000m);
            DateTime from = asOf.AddDays(-lookback);

            // manual and system vouchers of the window (history imported from Sepidar and opening, closing and reversal vouchers are not looked at)
            var voucherTypes = new[] { FnVoucherType.Normal, FnVoucherType.Auto };
            List<FnVoucher> raw = await _uow.Set<FnVoucher>().AsNoTracking().Include(v => v.Lines).ThenInclude(l => l.Detail1).Include(v => v.Lines).ThenInclude(l => l.Detail2)
                .Where(v => !v.IsImported && voucherTypes.Contains(v.Type) && v.Date >= from && v.Date <= asOf).OrderBy(v => v.Id).ToListAsync(ct);

            // payroll privacy: whatever the payroll posted stays out as a whole (its source, the payroll accounts, an employee on either detail)
            List<string> payrollRoles = FinPayroll.AccountRoles.ToList();
            List<int> payrollAccounts = await _uow.Set<AccountMapping>().AsNoTracking().Where(m => payrollRoles.Contains(m.Role) && m.AccountId != null).Select(m => m.AccountId.Value).Distinct().ToListAsync(ct);
            // reversed vouchers and the reversal / correction pairs cannot be a duplicate (a reversal is the same amount on purpose)
            var pairedIds = new HashSet<int>(raw.Where(v => v.ReversesVoucherId.HasValue).SelectMany(v => new[] { v.Id, v.ReversesVoucherId.Value })
                .Concat(raw.Where(v => v.AutoReversalVoucherId.HasValue).SelectMany(v => new[] { v.Id, v.AutoReversalVoucherId.Value })));

            var vouchers = new List<FinVoucherInput>();
            foreach (FnVoucher v in raw)
            {
                if (FinPayroll.IsPayrollSource(v.SourceType)) continue;
                if (v.Lines.Any(l => payrollAccounts.Contains(l.AccountId) || FinPayroll.IsEmployee(l.Detail1) || FinPayroll.IsEmployee(l.Detail2))) continue;
                var input = new FinVoucherInput
                {
                    Id = v.Id, Number = v.Number, TemporaryNumber = v.TemporaryNumber, Date = v.Date, TotalAmount = v.TotalAmount,
                    ExcludeFromDuplicate = v.Status == FnVoucherStatus.Draft || pairedIds.Contains(v.Id),
                };
                foreach (FnVoucherLine l in v.Lines)
                {
                    if (l.Detail1 == null || (l.Detail1.Type != FnDetailType.Supplier && l.Detail1.Type != FnDetailType.Customer && l.Detail1.Type != FnDetailType.Person)) continue;
                    bool credit = l.Credit > 0;
                    input.Lines.Add(new FinLineInput { CounterpartyId = l.Detail1Id, CounterpartyTitle = l.Detail1.Title, Amount = credit ? l.Credit : l.Debit, Credit = credit });
                }
                vouchers.Add(input);
            }
            // the off-day and round-amount rules are for what a person posted by hand
            var manualIds = new HashSet<int>(raw.Where(v => v.Type == FnVoucherType.Normal).Select(v => v.Id));
            var duplicateInputs = vouchers;

            OfficeCalendar calendar = await OfficeCalendar.LoadAsync(_uow, ct);
            DateTime bankTo = asOf.AddDays(-FinAnomalyRules.UnmatchedDays);
            var bank = await _uow.Set<FnBankStatementLine>().AsNoTracking().Where(b => b.MatchKind == FnStatementMatch.None && b.Date >= from && b.Date <= bankTo && (b.Withdrawal > 0 || b.Deposit > 0))
                .OrderBy(b => b.Id).Select(b => new { b.Id, b.Date, b.Withdrawal, b.Deposit }).ToListAsync(ct);
            var bankLines = bank.Select(b => new FinBankLineInput { Id = b.Id, Date = b.Date, Amount = Math.Max(b.Withdrawal, b.Deposit), Withdrawal = b.Withdrawal >= b.Deposit }).ToList();

            DateTimeOffset ibanFrom = new DateTimeOffset(asOf.AddDays(-FinAnomalyRules.IbanDays), TimeZoneInfo.Local.GetUtcOffset(asOf.AddDays(-FinAnomalyRules.IbanDays)));
            DateTimeOffset ibanTo = new DateTimeOffset(asOf.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(asOf.AddDays(1)));
            var audits = await _uow.Set<FnAuditLog>().AsNoTracking().Where(a => a.EntityType == "SupplierProfile" && a.Action == "Modified" && a.EntityId != null && a.At >= ibanFrom && a.At < ibanTo)
                .OrderBy(a => a.Id).Select(a => new { a.Id, a.EntityId, a.At, a.BeforeJson, a.AfterJson }).ToListAsync(ct);
            // a change counts when there was an IBAN before (the first entry of an IBAN is not a change); the trail keeps a hash of the old and the new value
            var changedProfiles = audits.Where(a => a.BeforeJson != null && a.BeforeJson.Contains("\"IbanHash\":\"", StringComparison.Ordinal) && a.AfterJson != null && a.AfterJson.Contains("\"IbanHash\"", StringComparison.Ordinal)).ToList();
            List<int> profileIds = changedProfiles.Select(a => a.EntityId.Value).Distinct().ToList();
            var profiles = await _uow.Set<SupplierProfile>().AsNoTracking().Where(p => profileIds.Contains(p.Id)).Select(p => new { p.Id, p.ProviderId }).ToListAsync(ct);
            List<int> providerIds = profiles.Select(p => p.ProviderId).ToList();
            Dictionary<int, string> providerNames = await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => providerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            var ibanChanges = new List<FinIbanChangeInput>();
            foreach (var a in changedProfiles)
            {
                var profile = profiles.FirstOrDefault(p => p.Id == a.EntityId.Value);
                if (profile == null) continue;
                ibanChanges.Add(new FinIbanChangeInput { AuditId = a.Id, ProviderId = profile.ProviderId, ProviderName = providerNames.TryGetValue(profile.ProviderId, out string nm) ? nm : "نامشخص", At = a.At.LocalDateTime });
            }

            // the duplicate rule looks at every voucher of the window, the other two voucher rules at the manual ones only
            List<FinFinding> findings = FinAnomalyRules.Analyze(asOf, duplicateInputs, bankLines, ibanChanges, calendar.IsOff, roundAmount, roundUnit)
                .Where(f => (f.Rule != "ROUND" && f.Rule != "OFFDAY") || manualIds.Contains(f.TargetId)).ToList();
            var byId = vouchers.ToDictionary(v => v.Id);
            List<FinNoteDraft> drafts = FinAnomalyRules.ToNotes(findings, byId);

            int created = 0;
            foreach (FinNoteDraft d in drafts)
            {
                bool added = await _notes.TryAddAsync(new AiNote
                {
                    SeatCode = Code, TargetType = d.TargetType, TargetId = d.TargetId, RuleCode = d.RuleCode, Title = d.Title, Text = d.Text, Verified = true, SeatRunId = context.RunId,
                }, ct);
                if (added) created++;
            }
            return AiSeatJobResult.Done($"findings={findings.Count}; notes={created}");
        }
    }

    // ============================================================================================ OFFICE-DRAFT

    public class OfficeDraftSeatJob : IAiSeatJob
    {
        private readonly IUnitOfWork _uow;
        private readonly IAiSeatAsk _ask;
        private readonly IAiNoteWriter _notes;
        private readonly ILetterService _letters;
        private readonly IMeetingService _meetings;

        public OfficeDraftSeatJob(IUnitOfWork uow, IAiSeatAsk ask, IAiNoteWriter notes, ILetterService letters, IMeetingService meetings)
        {
            _uow = uow; _ask = ask; _notes = notes; _letters = letters; _meetings = meetings;
        }

        public string Code => AiSeatCodes.OfficeDraft;

        public async Task<AiSeatJobResult> RunAsync(AiSeatRunContext context, CancellationToken ct)
        {
            int created = 0;

            // 1. incoming letters of normal classification that wait for an answer (a confidential or secret letter is never selected)
            IQueryable<int> repliedTo = _uow.Set<OfLetter>().AsNoTracking().Where(r => r.ReplyToLetterId != null && !r.Deleted && r.Status != OfLetterStatus.Cancelled).Select(r => r.ReplyToLetterId.Value);
            List<int> candidateIds = await _uow.Set<OfLetter>().AsNoTracking()
                .Where(l => !l.Deleted && !l.Imported && l.Kind == OfLetterKind.Incoming && l.Status == OfLetterStatus.Received && l.Classification == OfClassification.Normal
                            && !repliedTo.Contains(l.Id))
                .OrderBy(l => l.Id).Select(l => l.Id).Take(200).ToListAsync(ct);
            HashSet<int> have = await _notes.ExistingTargetsAsync(Code, AiNoteTypes.OfficeLetter, candidateIds, ct);
            foreach (int id in candidateIds.Where(i => !have.Contains(i)).Take(AiSeatTexts.MaxItemsPerRun))
            {
                var read = await _letters.GetLetterAsync(id, ct);
                // the office service shows the secretariat only the register data of confidential and secret letters; the check is repeated here before any text goes to the model
                if (!read.Success || read.Data == null || read.Data.MetadataOnly || read.Data.Classification != OfClassification.Normal || read.Data.Kind != OfLetterKind.Incoming) continue;
                string body = AiSeatTexts.StripHtml(read.Data.BodyHtml, 2500);
                string question = $"پیش‌نویس پاسخ به نامه ورودی شماره {read.Data.Number} با موضوع «{read.Data.Subject}» از «{read.Data.SenderName}»: متن نامه: {body} " +
                                  "یک پیش‌نویس کوتاه، رسمی و مؤدبانه‌ی فارسی بنویس. امضا و ارسال با مدیر است. فقط از اعداد همین پرسش استفاده کن.";
                AiSeatAnswer a = await _ask.AskAsync(question, ct, withTools: false);
                if (a.Degraded) return AiSeatJobResult.Skip(AiSeatTexts.LlmDown + ": " + AiText.Cut(a.Reason, 300), $"notes={created}");
                if (await _notes.TryAddAsync(new AiNote
                {
                    SeatCode = Code, TargetType = AiNoteTypes.OfficeLetter, TargetId = id, Title = $"پیش‌نویس پاسخ به نامه {read.Data.Number}",
                    Text = a.Text + "\n\nاین فقط پیش‌نویس است؛ نامه‌ی پاسخ را شخص مجاز تنظیم، تأیید و امضا می‌کند.", Verified = a.Verified, SeatRunId = context.RunId,
                }, ct)) created++;
            }

            // 2. approved minutes of the meetings the seat may see through the meeting service (★ the chair, the secretary, the attendees and the owners of decisions; the seat user holds no position,
            //    so it summarises the minutes of a meeting only when someone has added it to the meeting)
            IQueryable<int> visibleMeetings = await _meetings.VisibleMeetingIdsAsync(ct);
            List<int> meetingIds = await _uow.Set<OfMeeting>().AsNoTracking().Where(m => !m.Deleted && m.Status == OfMeetingStatus.MinutesApproved && visibleMeetings.Contains(m.Id)).OrderBy(m => m.Id).Select(m => m.Id).Take(200).ToListAsync(ct);
            HashSet<int> haveMeetings = await _notes.ExistingTargetsAsync(Code, AiNoteTypes.OfficeMeeting, meetingIds, ct);
            foreach (int id in meetingIds.Where(i => !haveMeetings.Contains(i)).Take(AiSeatTexts.MaxItemsPerRun))
            {
                OfMeeting m = await _uow.Set<OfMeeting>().AsNoTracking().Include(x => x.Agenda).Include(x => x.Decisions).FirstOrDefaultAsync(x => x.Id == id, ct);
                if (m == null) continue;
                var text = new StringBuilder();
                foreach (OfMeetingAgenda g in m.Agenda.OrderBy(g => g.Seq)) text.Append($"بند {g.Seq}: {g.Title}. {AiSeatTexts.StripHtml(g.Minutes, 600)} ");
                foreach (OfDecision d in m.Decisions.OrderBy(d => d.Seq)) text.Append($"مصوبه {d.Seq}: {AiSeatTexts.StripHtml(d.Text, 300)} ");
                string question = $"خلاصه صورت‌جلسه {m.Number} با عنوان «{m.Title}» ({InventoryText.PersianDate(m.StartAt)}): {AiText.Cut(text.ToString(), 3500)} " +
                                  "یک خلاصه‌ی کوتاه فارسی از بندها و مصوبه‌ها بنویس. فقط از اعداد همین پرسش استفاده کن.";
                AiSeatAnswer a = await _ask.AskAsync(question, ct, withTools: false);
                if (a.Degraded) return AiSeatJobResult.Skip(AiSeatTexts.LlmDown + ": " + AiText.Cut(a.Reason, 300), $"notes={created}");
                if (await _notes.TryAddAsync(new AiNote
                {
                    SeatCode = Code, TargetType = AiNoteTypes.OfficeMeeting, TargetId = id, Title = $"خلاصه صورت‌جلسه {m.Number}", Text = a.Text, Verified = a.Verified, SeatRunId = context.RunId,
                }, ct)) created++;
            }
            return AiSeatJobResult.Done($"notes={created}");
        }
    }
}
