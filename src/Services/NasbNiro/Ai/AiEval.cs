using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.AiDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Ai
{
    // Release A1 section 8: the evaluation. A fixed list of questions is asked through the real chat loop as a user of its own (roles: CEO, so every tool is offered).
    // A case passes when the expected tools were called, every expected fact is in the answer and the answer passed the numeric check.
    // Facts are never typed in as numbers: "tool:NAME.path" is read from the tool itself when the run is made, "text:WORDS" is a literal.

    public sealed class AiEvalSeed
    {
        public string Code { get; set; }
        public string Question { get; set; }

        /// <summary>Tool names separated by commas; "none" means that no tool may be called.</summary>
        public string Tools { get; set; }
        public string Facts { get; set; }
    }

    public static class AiEvalCatalog
    {
        private static AiEvalSeed C(string n, string question, string tools, string facts = null) =>
            new AiEvalSeed { Code = "EV-" + n, Question = question, Tools = tools, Facts = facts };

        public static readonly IReadOnlyList<AiEvalSeed> Cases = new List<AiEvalSeed>
        {
            // management
            C("001", "داشبورد مدیرعامل را خلاصه کن", "ceo_dashboard", "tool:ceo_dashboard.stockValue"),
            C("002", "ارزش کل موجودی انبار طبق داشبورد چقدر است؟", "ceo_dashboard", "tool:ceo_dashboard.stockValue"),
            C("003", "در داشبورد چند کالا زیر حداقل موجودی است؟", "ceo_dashboard", "tool:ceo_dashboard.itemsBelowMinimum"),
            C("004", "داشبورد را ببین و بگو چند ردیف در قرنطینه است", "ceo_dashboard", "tool:ceo_dashboard.quarantineLines"),
            C("005", "وضعیت کلی شرکت در داشبورد چیست؟", "ceo_dashboard"),
            C("006", "هشدارهای داشبورد مدیرعامل چیست؟", "ceo_dashboard"),
            C("007", "ارزش موجودی و تعداد اقلام زیر حداقل را از داشبورد بگو", "ceo_dashboard", "tool:ceo_dashboard.stockValue;tool:ceo_dashboard.itemsBelowMinimum"),
            C("008", "پروژه‌های قرمز و زرد کدام‌اند؟", "project_portfolio"),
            C("009", "مدیرعامل امروز باید به چه چیزهایی توجه کند؟", "ceo_dashboard"),
            C("010", "هشدار /inventory/stock را توضیح بده", "explain_alert"),

            // finance
            C("011", "وضعیت نقدینگی شرکت چیست؟", "cash_position", "tool:cash_position.cash"),
            C("012", "موجودی حساب‌های بانکی چقدر است؟", "cash_position", "tool:cash_position.banks"),
            C("013", "نقدینگی صندوق و تنخواه را بگو", "cash_position", "tool:cash_position.cash;tool:cash_position.pettyCash"),
            C("014", "مجموع نقدینگی بانک‌ها چقدر است؟", "cash_position", "tool:cash_position.banks"),
            C("015", "پیش‌بینی نقدینگی هفته‌های آینده چگونه است؟", "cash_position"),
            C("016", "داشبورد مالی را نشان بده", "finance_dashboard"),
            C("017", "تراز آزمایشی امسال متوازن است؟", "trial_balance_summary"),
            C("018", "مطالبات سررسیدگذشته مشتریان چقدر است؟", "receivables_aging"),
            C("019", "بدهی‌های باز به تامین‌کنندگان چقدر است؟", "overdue_payables"),
            C("020", "کدام پرداخت‌ها منتظر خزانه هستند؟", "overdue_payables"),
            C("021", "چک‌های دریافتی در صندوق چقدر است؟", "cash_position"),

            // inventory
            C("022", "کدام اقلام زیر حداقل موجودی هستند؟", "items_below_minimum", "tool:items_below_minimum.totalCount"),
            C("023", "چند کالا از حداقل موجودی کمتر دارد؟", "items_below_minimum", "tool:items_below_minimum.totalCount"),
            C("024", "فهرست کالاهای کم‌موجودی را بده", "items_below_minimum"),
            C("025", "موجودی کالایی با کد 01 چقدر است؟", "item_stock"),
            C("026", "کاردکس کالای پیچ را خلاصه کن", "item_card_summary"),
            C("027", "کالاهای کمبود را برای خرید معرفی کن", "items_below_minimum"),

            // procurement
            C("028", "وضعیت خرید سفارش‌ها چیست؟", "purchase_status"),
            C("029", "پروفایل تامین‌کننده‌ها را بگو", "supplier_profile"),
            C("030", "کدام درخواست‌های خرید منتظر تأیید هستند؟", "purchase_status"),
            C("031", "تامین‌کننده‌های مسدود کدام‌اند؟", "supplier_profile"),

            // projects
            C("032", "سبد پروژه‌ها را خلاصه کن", "project_portfolio"),
            C("033", "وضعیت پروژه شماره یک چیست؟", "project_status"),
            C("034", "درخواست‌های تغییر باز کدام‌اند؟", "change_requests"),
            C("035", "کدام تضمین‌ها نزدیک انقضا هستند؟", "expiring_guarantees"),
            C("036", "وضعیت MDR پروژه چیست؟", "mdr_status"),

            // quality
            C("037", "داشبورد کیفیت را بگو", "quality_dashboard"),
            C("038", "چند NCR باز داریم؟", "open_ncr_capa"),
            C("039", "کالیبراسیون‌های سررسیدشده کدام‌اند؟", "calibration_due"),
            C("040", "وضعیت ITP دستور کار نمونه چیست؟", "itp_status"),
            C("041", "CAPAهای باز کدام‌اند؟", "open_ncr_capa"),

            // sales, service, production
            C("042", "داشبورد فروش را بگو", "sales_dashboard"),
            C("043", "داشبورد خدمات پس از فروش چه می‌گوید؟", "service_dashboard"),
            C("044", "داشبورد تولید را بگو", "production_dashboard"),
            C("045", "دستورهای تولید عقب‌افتاده چندتاست؟", "production_dashboard"),
            C("046", "نرخ موفقیت پیشنهادهای فروش چند درصد است؟", "sales_dashboard"),
            C("047", "پیشنهادهای فروش منتظر تصمیم چندتاست؟", "sales_dashboard"),

            // documents
            C("048", "روش اجرایی کنترل مدارک را پیدا کن", "search_documents"),
            C("049", "دستورالعمل بازرسی ورودی کالا کدام سند است؟", "search_documents"),
            C("050", "روش کار بازرسی نهایی چیست؟", "search_documents"),
            C("051", "فرم گزارش عدم انطباق کدام سند است؟", "search_documents"),
            C("052", "سند مربوط به ارزیابی تامین‌کنندگان را پیدا کن", "search_documents"),
            C("053", "رویه خرید در کدام سند آمده است؟", "search_documents"),
            C("054", "هشدار /finance/events را توضیح بده", "explain_alert"),

            // no tool needed
            C("055", "سلام", "none"),
            C("056", "تو کی هستی؟", "none"),
            C("057", "ممنون از کمکت", "none"),
            C("058", "یک جمله‌ی کوتاه انگیزشی بگو", "none"),
            C("059", "خداحافظ", "none"),
            C("060", "کمک می‌خواهم", "none"),
            C("061", "می‌توانی چه کارهایی انجام بدهی؟", "none"),

            // mixed and harder wording
            C("062", "خلاصه‌ای از وضعیت شرکت بده", "ceo_dashboard"),
            C("063", "الان چقدر پول در بانک‌ها داریم؟", "cash_position"),
            C("064", "وضعیت پروژه‌ها از نظر برنامه و هزینه چیست؟", "project_portfolio"),
            C("065", "کدام کالاها باید خریداری شوند؟", "items_below_minimum"),
            C("066", "عدم انطباق‌های باز کیفیت را بگو", "open_ncr_capa"),
        };
    }

    /// <summary>Reads the expected facts of a case. Pure helpers, so they are unit tested.</summary>
    public static class AiEvalFacts
    {
        public const string ToolPrefix = "tool:";
        public const string TextPrefix = "text:";

        public static List<string> Split(string facts) =>
            string.IsNullOrWhiteSpace(facts) ? new List<string>() : facts.Split(';').Select(f => f.Trim()).Where(f => f.Length > 0).ToList();

        public static List<string> SplitTools(string tools) =>
            string.IsNullOrWhiteSpace(tools) ? new List<string>() : tools.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

        /// <summary>The tools of a case are right: all expected ones were called, or none at all for "none".</summary>
        public static bool ToolsOk(string expectedTools, IReadOnlyCollection<string> called)
        {
            List<string> expected = SplitTools(expectedTools);
            if (expected.Count == 1 && expected[0].Equals("none", StringComparison.OrdinalIgnoreCase)) return called.Count == 0;
            return expected.All(e => called.Contains(e, StringComparer.Ordinal));
        }

        /// <summary>Follows "a.b[2].c" (property names in any case) in a JSON text. Null when the path leads nowhere.</summary>
        public static JsonElement? ResolvePath(JsonElement root, string path)
        {
            JsonElement current = root;
            foreach (string raw in (path ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                string name = raw;
                var indexes = new List<int>();
                int bracket = raw.IndexOf('[');
                if (bracket >= 0)
                {
                    name = raw.Substring(0, bracket);
                    foreach (string part in raw.Substring(bracket).Split(new[] { '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int idx)) return null;
                        indexes.Add(idx);
                    }
                }
                if (name.Length > 0)
                {
                    if (current.ValueKind != JsonValueKind.Object) return null;
                    bool found = false;
                    foreach (JsonProperty p in current.EnumerateObject())
                        if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { current = p.Value; found = true; break; }
                    if (!found) return null;
                }
                foreach (int idx in indexes)
                {
                    if (current.ValueKind != JsonValueKind.Array || idx >= current.GetArrayLength()) return null;
                    current = current[idx];
                }
            }
            return current;
        }

        /// <summary>Splits "tool:NAME.path" into the tool and the path.</summary>
        public static bool TryParseToolFact(string fact, out string tool, out string path)
        {
            tool = path = null;
            if (fact == null || !fact.StartsWith(ToolPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            string rest = fact.Substring(ToolPrefix.Length).Trim();
            int dot = rest.IndexOf('.');
            if (dot <= 0 || dot == rest.Length - 1) return false;
            tool = rest.Substring(0, dot);
            path = rest.Substring(dot + 1);
            return true;
        }

        /// <summary>
        /// Is the value of a tool result in the answer? A number is found by its canonical form (so 1,234 and ۱۲۳۴ are the same); text and booleans by their words.
        /// </summary>
        public static (bool Ok, string Reason) InAnswer(JsonElement value, string answer)
        {
            answer ??= string.Empty;
            switch (value.ValueKind)
            {
                case JsonValueKind.Number:
                    string canonical = AiNumbers.JsonNumber(value);
                    bool found = AiNumbers.Tokens(answer, false).Any(t => t.Canonical == canonical || t.Plain == canonical);
                    return (found, found ? null : "عدد " + canonical + " در پاسخ نیست");
                case JsonValueKind.String:
                    string s = AiText.YeKe(value.GetString())?.Trim();
                    if (string.IsNullOrEmpty(s)) return (false, "مقدار خالی است");
                    bool has = AiText.YeKe(answer).IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
                    return (has, has ? null : "متن «" + AiText.Cut(s, 60) + "» در پاسخ نیست");
                default:
                    return (false, "مقدار قابل مقایسه نیست");
            }
        }

        /// <summary>The literal of a "text:" fact is in the answer.</summary>
        public static (bool Ok, string Reason) TextInAnswer(string fact, string answer)
        {
            string literal = AiText.YeKe(fact.Substring(TextPrefix.Length)).Trim();
            if (literal.Length == 0) return (false, "متن واقعیت خالی است");
            bool has = AiText.YeKe(answer ?? string.Empty).IndexOf(literal, StringComparison.OrdinalIgnoreCase) >= 0;
            return (has, has ? null : "متن «" + AiText.Cut(literal, 60) + "» در پاسخ نیست");
        }

        /// <summary>Nearest-rank percentile of a list of values (0 for an empty list).</summary>
        public static int Percentile(IEnumerable<int> values, int percent)
        {
            int[] sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0) return 0;
            int rank = (int)Math.Ceiling(percent / 100.0 * sorted.Length);
            return sorted[Math.Min(sorted.Length, Math.Max(1, rank)) - 1];
        }
    }

    public interface IAiEvalService
    {
        Task<ResponseDto<AiEvalRunDto>> RunAsync(CancellationToken ct);
        Task<ResponseDto<PagedList<AiEvalRunDto>>> GetRunsAsync(AiEvalRunQuery query, CancellationToken ct);
        Task<ResponseDto<AiEvalRunDto>> GetRunAsync(int id, CancellationToken ct);
        Task<ResponseDto<List<AiEvalCaseDto>>> GetCasesAsync(CancellationToken ct);
    }

    public class AiEvalService : IAiEvalService
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IAiAudit _audit;
        private readonly IAiSeatProvisioner _provisioner;
        private readonly IServiceScopeFactory _scopes;
        private readonly IAiWorkQueue _queue;

        public AiEvalService(IUnitOfWork uow, IInventoryUserContext user, IAiAudit audit, IAiSeatProvisioner provisioner, IServiceScopeFactory scopes, IAiWorkQueue queue)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        private async Task EnsureCasesAsync(CancellationToken ct)
        {
            HashSet<string> have = (await _uow.Set<AiEvalCase>().AsNoTracking().Select(c => c.Code).ToListAsync(ct)).ToHashSet();
            bool added = false;
            foreach (AiEvalSeed s in AiEvalCatalog.Cases.Where(s => !have.Contains(s.Code)))
            {
                _uow.Set<AiEvalCase>().Add(new AiEvalCase { Code = s.Code, Question = s.Question, ExpectedTools = s.Tools, ExpectedFacts = s.Facts, Enabled = true });
                added = true;
            }
            if (added)
            {
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException) { foreach (var e in _uow.Set<AiEvalCase>().Local.Where(x => x.Id == 0).ToList()) _uow.Entry(e).State = EntityState.Detached; }
            }
        }

        private static AiEvalRunDto ToDto(AiEvalRun r, List<AiEvalCaseResultDto> cases) => new AiEvalRunDto
        {
            Id = r.Id, StartedAt = r.StartedAt, StartedAtText = AiText.Stamp(r.StartedAt), FinishedAt = r.FinishedAt, Total = r.Total, Passed = r.Passed,
            Running = r.FinishedAt == null, PassPercent = r.Total == 0 ? 0 : Math.Round(100m * r.Passed / r.Total, 1), NumericFailures = r.NumericFailures, P50Ms = r.P50Ms, P95Ms = r.P95Ms, Model = r.Model, Cases = cases,
        };

        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);
        public const string AlreadyRunning = "یک ارزیابی دیگر هنوز در حال اجراست.";

        /// <summary>Starts an evaluation in the background and answers at once with the run (Running = true); the run is read by its id until Running is false. Only one at a time.</summary>
        public async Task<ResponseDto<AiEvalRunDto>> RunAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Admins)) return AiText.Bad<AiEvalRunDto>(AiText.NotAllowed);
            await EnsureCasesAsync(ct);
            List<AiEvalCase> cases = await _uow.Set<AiEvalCase>().AsNoTracking().Where(c => c.Enabled).OrderBy(c => c.Code).ToListAsync(ct);
            if (cases.Count == 0) return AiText.Bad<AiEvalRunDto>("موردی برای ارزیابی فعال نیست.");
            Dictionary<string, AiActingUser> users = await _provisioner.EnsureAsync(ct);
            AiActingUser evalUser = users[AiSeatCodes.Eval];

            // an evaluation that has been running for too long is dead: it is closed first
            DateTimeOffset started = DateTimeOffset.Now;
            foreach (AiEvalRun old in (await _uow.Set<AiEvalRun>().Where(r => r.FinishedAt == null).ToListAsync(ct)).Where(r => started - r.StartedAt > StaleAfter))
                old.FinishedAt = started;
            await _uow.SaveChangesAsync(ct);

            var run = new AiEvalRun { StartedAt = started, FinishedAt = null };
            _uow.Set<AiEvalRun>().Add(run);
            _audit.Add(AiAuditKinds.Eval, $"evaluation started: {cases.Count} cases", seatCode: AiSeatCodes.Eval);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return AiText.Bad<AiEvalRunDto>(AlreadyRunning); }

            int runId = run.Id;
            _queue.Enqueue(stopping => ExecuteAsync(runId, cases, evalUser, stopping));
            return ResponseDtoHelper.Ok(ToDto(run, null));
        }

        private async Task ExecuteAsync(int runId, List<AiEvalCase> cases, AiActingUser evalUser, CancellationToken stopping)
        {
            List<AiEvalCaseResultDto> results = null;
            try
            {
                // the questions are asked by the evaluation user (not by the caller), so the answers do not depend on who started the run
                results = await AiActing.RunAsync(_scopes, evalUser, AiSeatCodes.Eval, sp => AskAllAsync(sp, cases, stopping));
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            catch (Exception) { results = null; }

            using IServiceScope scope = _scopes.CreateScope();
            IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            IAiAudit audit = scope.ServiceProvider.GetRequiredService<IAiAudit>();
            AiEvalRun run = await uow.Set<AiEvalRun>().FirstOrDefaultAsync(r => r.Id == runId, CancellationToken.None);
            if (run == null) return;
            results ??= new List<AiEvalCaseResultDto>();
            run.FinishedAt = DateTimeOffset.Now;
            run.Total = results.Count;
            run.Passed = results.Count(r => r.Passed);
            // an answer that was written by the model but failed the numeric check (a degraded answer has no model text and is not counted)
            run.NumericFailures = results.Count(r => !r.Degraded && !r.Verified);
            run.P50Ms = AiEvalFacts.Percentile(results.Select(r => r.Ms), 50);
            run.P95Ms = AiEvalFacts.Percentile(results.Select(r => r.Ms), 95);
            run.Model = results.Where(r => !string.IsNullOrEmpty(r.Model)).GroupBy(r => r.Model).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).FirstOrDefault();
            run.Detail = JsonSerializer.Serialize(results, Json);
            audit.Add(AiAuditKinds.Eval, $"evaluation finished: {run.Passed}/{run.Total} passed, numericFailures={run.NumericFailures}, p50={run.P50Ms} ms, p95={run.P95Ms} ms, model={run.Model ?? "-"}", run.Model, seatCode: AiSeatCodes.Eval);
            await uow.SaveChangesAsync(CancellationToken.None);
        }

        private static async Task<List<AiEvalCaseResultDto>> AskAllAsync(IServiceProvider sp, List<AiEvalCase> cases, CancellationToken ct)
        {
            var chat = sp.GetRequiredService<IAiChatService>();
            var tools = sp.GetRequiredService<IToolRegistry>();
            var audit = sp.GetRequiredService<IAiAudit>();
            var toolJson = new Dictionary<string, string>(StringComparer.Ordinal);
            var results = new List<AiEvalCaseResultDto>();
            foreach (AiEvalCase c in cases)
            {
                var r = new AiEvalCaseResultDto { Code = c.Code, Question = c.Question, ExpectedTools = c.ExpectedTools };
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var answer = await chat.ChatAsync(new AiChatRequestDto { Text = c.Question }, ct);
                clock.Stop();
                r.Ms = (int)clock.ElapsedMilliseconds;
                if (!answer.Success || answer.Data == null)
                {
                    r.Answer = AiText.Cut(answer.Message, 300);
                    results.Add(r);
                    continue;
                }
                r.Answer = AiText.Cut(answer.Data.Answer, 600);
                r.Verified = answer.Data.Verified;
                r.Degraded = answer.Data.Degraded;
                r.Model = answer.Data.Model;
                r.CalledTools = answer.Data.ToolCalls.Where(t => t.Allowed).Select(t => t.Name).Distinct().ToList();
                // for "none" a refused call counts as a call too
                List<string> expectedTools = AiEvalFacts.SplitTools(c.ExpectedTools);
                bool expectsNone = expectedTools.Count == 1 && expectedTools[0].Equals("none", StringComparison.OrdinalIgnoreCase);
                r.ToolsOk = expectsNone ? answer.Data.ToolCalls.Count == 0 : AiEvalFacts.ToolsOk(c.ExpectedTools, r.CalledTools);

                r.FactsOk = true;
                foreach (string fact in AiEvalFacts.Split(c.ExpectedFacts))
                {
                    var f = new AiEvalFactResultDto { Fact = fact };
                    if (fact.StartsWith(AiEvalFacts.TextPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        (f.Ok, f.Reason) = AiEvalFacts.TextInAnswer(fact, answer.Data.Answer);
                    }
                    else if (AiEvalFacts.TryParseToolFact(fact, out string toolName, out string path))
                    {
                        if (!toolJson.TryGetValue(toolName, out string json))
                        {
                            AiToolResult tr = await tools.ExecuteAsync(toolName, "{}", ct);
                            json = tr.Allowed ? tr.Json : null;
                            // the tool run for a fact is audited like any other tool run
                            audit.Add(AiAuditKinds.Tool, $"{tr.Name} {{}} allowed={tr.Allowed} chars={tr.Chars} hash={tr.Hash} (evaluation fact)", null, tr.DurationMs, seatCode: AiSeatCodes.Eval);
                            toolJson[toolName] = json;
                        }
                        if (json == null) { f.Ok = false; f.Reason = "ابزار در دسترس نیست"; }
                        else
                        {
                            try
                            {
                                using JsonDocument doc = JsonDocument.Parse(json);
                                JsonElement? value = AiEvalFacts.ResolvePath(doc.RootElement, path);
                                if (value == null) { f.Ok = false; f.Reason = "مسیر در نتیجه‌ی ابزار پیدا نشد"; }
                                else (f.Ok, f.Reason) = AiEvalFacts.InAnswer(value.Value, answer.Data.Answer);
                            }
                            catch (JsonException) { f.Ok = false; f.Reason = "نتیجه‌ی ابزار خوانده نشد"; }
                        }
                    }
                    else { f.Ok = false; f.Reason = "قالب واقعیت نامعتبر است"; }
                    if (!f.Ok) r.FactsOk = false;
                    r.Facts.Add(f);
                }
                r.Passed = r.ToolsOk && r.FactsOk && r.Verified && !r.Degraded;
                results.Add(r);
            }
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            return results;
        }

        public async Task<ResponseDto<PagedList<AiEvalRunDto>>> GetRunsAsync(AiEvalRunQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Readers)) return AiText.Bad<PagedList<AiEvalRunDto>>(AiText.NotAllowed);
            query ??= new AiEvalRunQuery();
            IQueryable<AiEvalRun> rows = _uow.Set<AiEvalRun>().AsNoTracking().OrderByDescending(r => r.Id);
            PagedList<AiEvalRun> page = await AiText.PageAsync(rows, query.PageNumber, query.PageSize, ct);
            PagedList<AiEvalRunDto> dtos = await PagedList<AiEvalRunDto>.CreateAsync(page.Select(r => ToDto(r, null)).ToList(), 1, 0, ct);
            dtos.TotalCount = page.TotalCount; dtos.CurrentPage = page.CurrentPage; dtos.PageSize = page.PageSize; dtos.TotalPages = page.TotalPages;
            return ResponseDtoHelper.Ok(dtos);
        }

        public async Task<ResponseDto<AiEvalRunDto>> GetRunAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Readers)) return AiText.Bad<AiEvalRunDto>(AiText.NotAllowed);
            AiEvalRun run = await _uow.Set<AiEvalRun>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run == null) return AiText.NotFound<AiEvalRunDto>("اجرای ارزیابی پیدا نشد.");
            List<AiEvalCaseResultDto> cases;
            try { cases = string.IsNullOrWhiteSpace(run.Detail) ? new List<AiEvalCaseResultDto>() : JsonSerializer.Deserialize<List<AiEvalCaseResultDto>>(run.Detail, Json); }
            catch (JsonException) { cases = new List<AiEvalCaseResultDto>(); }
            // the answers were written with the data of the CEO: only the CEO reads them (the PMO reads the counts)
            if (!_user.IsInRole("CEO"))
                foreach (AiEvalCaseResultDto c in cases) { c.Answer = null; foreach (AiEvalFactResultDto f in c.Facts) f.Reason = null; }
            return ResponseDtoHelper.Ok(ToDto(run, cases));
        }

        public async Task<ResponseDto<List<AiEvalCaseDto>>> GetCasesAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Readers)) return AiText.Bad<List<AiEvalCaseDto>>(AiText.NotAllowed);
            if (_user.IsInRole(AiRoles.Admins)) await EnsureCasesAsync(ct);
            List<AiEvalCaseDto> cases = await _uow.Set<AiEvalCase>().AsNoTracking().OrderBy(c => c.Code)
                .Select(c => new AiEvalCaseDto { Id = c.Id, Code = c.Code, Question = c.Question, ExpectedTools = c.ExpectedTools, ExpectedFacts = c.ExpectedFacts, Enabled = c.Enabled }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(cases);
        }
    }
}
