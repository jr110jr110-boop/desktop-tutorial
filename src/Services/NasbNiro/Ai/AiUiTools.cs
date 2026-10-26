using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Services.NasbNiro.Ceo.Drills;
using ViewModels.NasbNiro.AiDtos;

namespace Services.NasbNiro.Ai
{
    /// <summary>
    /// The UI action tools of the voice mode (release D1): the assistant asks the front end to open a page, a detail table or an analysis, to read something aloud or to scroll to a section.
    /// They run no server code and change no data: a tool checks its arguments, hands the action to the registry (it ends up in <see cref="AiChatResponseDto.Actions"/> in the order the model
    /// called the tools) and answers {"ok":true,"message":...}; a refused call answers {"ok":false,"message":...} and records no action.
    /// Approving, finalising, paying and deleting are never actions: a person does them on the page of the gate, which the assistant may only open.
    /// Every signed-in user is offered the tools; the ones that belong to the CEO dashboard tell anyone but CEO and PMO that they cannot be used.
    /// </summary>
    public static class AiUiTools
    {
        public const string NavigateTool = "ui_navigate";
        public const string OpenDrillTool = "ui_open_drill";
        public const string AnalyzeTool = "ui_analyze";
        public const string ReadTool = "ui_read";
        public const string ScrollTool = "ui_scroll";

        public const string NavigateAction = "navigate";
        public const string OpenDrillAction = "openDrill";
        public const string AnalyzeAction = "analyze";
        public const string ReadAction = "read";
        public const string ScrollAction = "scroll";

        public const int MaxLinkLength = 300;

        /// <summary>The actions of one answer; more are dropped.</summary>
        public const int MaxActions = 8;

        public const string NotDashboardUser = "این کار فقط در داشبورد مدیرعامل و برای مدیرعامل یا مدیر پروژه‌ها ممکن است.";
        public const string NotCeo = "گزارش روزانه فقط برای مدیرعامل است.";

        public static readonly IReadOnlyList<string> ToolNames = new[] { NavigateTool, OpenDrillTool, AnalyzeTool, ReadTool, ScrollTool };

        /// <summary>The routes of the front end the assistant may open: the first segments of the path and the Persian name of the part of the system (the allow-list of <c>ui_navigate</c>).</summary>
        public static readonly IReadOnlyList<(string Prefix, string Title)> Routes = new[]
        {
            ("/ceo", "داشبورد مدیرعامل"), ("/finance", "مالی"), ("/fn", "مالی"), ("/sales", "فروش"), ("/service", "خدمات پس از فروش"), ("/production", "تولید"),
            ("/project-control", "کنترل پروژه"), ("/quality", "کیفیت"), ("/inventory", "انبار"), ("/procurement", "تدارکات"), ("/office", "اتوماسیون اداری"), ("/ai", "هوش مصنوعی"),
        };

        /// <summary>What <c>ui_read</c> can read aloud.</summary>
        public static readonly IReadOnlyList<string> ReadKinds = new[] { "section", "alerts", "brief", "headline" };

        public static bool IsUiTool(string name) => ToolNames.Contains(name);

        // ------------------------------------------------------------------------------------------ the allow-list

        /// <summary>
        /// True when <paramref name="link"/> is a route of the front end the assistant may open: one slash and a path that is a prefix of the allow-list or continues it after a slash
        /// (so /ceo, /ceo/ and /ceo?x=1 pass; /ceox, //evil.com, http://evil, javascript:alert(1) and paths with .. do not). <paramref name="clean"/> is the trimmed link.
        /// </summary>
        public static bool TryCheckLink(string link, out string clean, out string module)
        {
            clean = null;
            module = null;
            string text = link?.Trim();
            if (string.IsNullOrEmpty(text) || text.Length > MaxLinkLength || text[0] != '/') return false;
            if (text.Length > 1 && (text[1] == '/' || text[1] == '\\')) return false;
            foreach (char ch in text)
                if (char.IsControl(ch) || char.IsWhiteSpace(ch) || ch == '\\' || ch == '<' || ch == '>' || ch == '"' || ch == '\'' || ch == '`') return false;
            int cut = text.IndexOfAny(new[] { '?', '#' });
            string path = cut < 0 ? text : text.Substring(0, cut);
            if (path.IndexOf('%') >= 0 || path.Split('/').Any(segment => segment == "." || segment == "..")) return false;
            foreach ((string prefix, string title) in Routes)
                if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                {
                    clean = text;
                    module = title;
                    return true;
                }
            return false;
        }

        /// <summary>The section key in lower case when it is one of the nine sections, else null.</summary>
        public static string Section(string key)
        {
            string text = key?.Trim().ToLowerInvariant();
            return text != null && CeoModules.Sections.Contains(text) ? text : null;
        }

        // ------------------------------------------------------------------------------------------ the tools

        private static string Schema(IEnumerable<(string Name, string Type, string Description, bool Required)> properties)
        {
            var props = new JsonObject();
            var required = new JsonArray();
            foreach (var p in properties)
            {
                props[p.Name] = new JsonObject { ["type"] = p.Type, ["description"] = p.Description };
                if (p.Required) required.Add(p.Name);
            }
            var schema = new JsonObject { ["type"] = "object", ["properties"] = props };
            if (required.Count > 0) schema["required"] = required;
            return schema.ToJsonString();
        }

        private static AiToolOutput Done(AiActionDto action, string message) =>
            new AiToolOutput { Data = new Dictionary<string, object> { ["ok"] = true, ["message"] = message }, Action = action };

        private static AiToolOutput Refuse(string message) =>
            new AiToolOutput { Data = new Dictionary<string, object> { ["ok"] = false, ["message"] = message } };

        private static string SectionList => string.Join(", ", CeoModules.Sections);

        public static List<AiToolDefinition> Build()
        {
            var tools = new List<AiToolDefinition>();

            tools.Add(new AiToolDefinition
            {
                Name = NavigateTool, Title = "باز کردن صفحه",
                Description = "صفحه‌ای از سامانه را برای کاربر باز می‌کند (فقط جابه‌جایی؛ هیچ داده‌ای تغییر نمی‌کند و هیچ تأیید یا پرداختی انجام نمی‌شود). link مسیر صفحه است و باید با یکی از این‌ها شروع شود: "
                    + string.Join(", ", Routes.Select(r => r.Prefix)) + "؛ مثلا /production/orders.",
                SchemaJson = Schema(new[] { ("link", "string", "مسیر صفحه، مثلا /production/orders", true) }),
                Run = (c, a, ct) =>
                {
                    string link = AiTools.Str(a, "link");
                    if (!TryCheckLink(link, out string clean, out string module))
                        return Task.FromResult<object>(Refuse("این مسیر در فهرست صفحه‌های مجاز نیست؛ مسیر باید با یکی از این‌ها شروع شود: " + string.Join("، ", Routes.Select(r => r.Prefix)) + "."));
                    return Task.FromResult<object>(Done(new AiActionDto { Type = NavigateAction, Link = clean, Title = "باز کردن صفحه‌ی " + module }, "صفحه‌ی " + module + " (" + clean + ") باز می‌شود."));
                },
            });

            tools.Add(new AiToolDefinition
            {
                Name = OpenDrillTool, Title = "باز کردن جدول جزئیات",
                Description = "جدول جزئیات داشبورد مدیرعامل را در صفحه باز می‌کند (فقط نمایش؛ هیچ داده‌ای تغییر نمی‌کند). key کلید جزئیات است (از ceo_drills؛ مثلا qc.ncr_open) و params پارامترهای اختیاری همان جزئیات، مثلا {\"severity\":\"Major\"}.",
                SchemaJson = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["key"] = new JsonObject { ["type"] = "string", ["description"] = "کلید جزئیات، مثلا qc.ncr_open" },
                        ["params"] = new JsonObject { ["type"] = "object", ["description"] = "پارامترهای اختیاری جزئیات (نام و مقدار)، مثلا {\"severity\":\"Major\"} یا {\"top\":10}" },
                    },
                    ["required"] = new JsonArray { "key" },
                }.ToJsonString(),
                Run = (c, a, ct) => Task.FromResult<object>(OpenDrill(c, a)),
            });

            tools.Add(new AiToolDefinition
            {
                Name = AnalyzeTool, Title = "اجرای تحلیل",
                Description = "تحلیل هوشمند یک بخش داشبورد مدیرعامل را در صفحه اجرا و نمایش می‌دهد (برای کل شرکت sectionKey را نگذار). sectionKey یکی از: " + SectionList + ".",
                SchemaJson = Schema(new[] { ("sectionKey", "string", "کلید بخش: " + SectionList + "؛ خالی = کل شرکت", false) }),
                Run = (c, a, ct) =>
                {
                    if (!c.User.IsInRole(CeoDrillRoles.Readers)) return Task.FromResult<object>(Refuse(NotDashboardUser));
                    string given = AiTools.Str(a, "sectionKey");
                    string section = given == null || string.Equals(given, "all", StringComparison.OrdinalIgnoreCase) ? null : Section(given);
                    if (given != null && section == null && !string.Equals(given, "all", StringComparison.OrdinalIgnoreCase)) return Task.FromResult<object>(Refuse(UnknownSection));
                    string what = section == null ? "کل شرکت" : "بخش " + CeoModules.Title(section);
                    return Task.FromResult<object>(Done(new AiActionDto { Type = AnalyzeAction, SectionKey = section, Title = "تحلیل " + what }, "تحلیل " + what + " اجرا می‌شود."));
                },
            });

            tools.Add(new AiToolDefinition
            {
                Name = ReadTool, Title = "خواندن با صدا",
                Description = "مطلبی از صفحه را با صدا برای کاربر می‌خواند. what یکی از: section (یک بخش؛ sectionKey لازم است)، alerts (هشدارها)، brief (گزارش روزانه‌ی مدیرعامل) یا headline (خلاصه‌ی وضعیت کل شرکت).",
                SchemaJson = Schema(new[]
                {
                    ("what", "string", "section، alerts، brief یا headline", true),
                    ("sectionKey", "string", "فقط برای what=section؛ یکی از: " + SectionList, false),
                }),
                Run = (c, a, ct) =>
                {
                    if (!c.User.IsInRole(CeoDrillRoles.Readers)) return Task.FromResult<object>(Refuse(NotDashboardUser));
                    string what = AiTools.Str(a, "what")?.ToLowerInvariant();
                    if (what == null || !ReadKinds.Contains(what)) return Task.FromResult<object>(Refuse("مقدار what باید یکی از این‌ها باشد: " + string.Join("، ", ReadKinds) + "."));
                    if (what == "brief" && !c.User.IsInRole("CEO")) return Task.FromResult<object>(Refuse(NotCeo));
                    string section = null;
                    if (what == "section")
                    {
                        section = Section(AiTools.Str(a, "sectionKey"));
                        if (section == null) return Task.FromResult<object>(Refuse(UnknownSection));
                    }
                    string title = what switch
                    {
                        "section" => "بخش " + CeoModules.Title(section),
                        "alerts" => "هشدارها",
                        "brief" => "گزارش روزانه",
                        _ => "خلاصه‌ی وضعیت",
                    };
                    return Task.FromResult<object>(Done(new AiActionDto { Type = ReadAction, What = what, SectionKey = section, Title = "خواندن " + title }, title + " خوانده می‌شود."));
                },
            });

            tools.Add(new AiToolDefinition
            {
                Name = ScrollTool, Title = "رفتن به یک بخش",
                Description = "صفحه‌ی داشبورد مدیرعامل را به یک بخش می‌برد. sectionKey یکی از: " + SectionList + ".",
                SchemaJson = Schema(new[] { ("sectionKey", "string", "کلید بخش: " + SectionList, true) }),
                Run = (c, a, ct) =>
                {
                    if (!c.User.IsInRole(CeoDrillRoles.Readers)) return Task.FromResult<object>(Refuse(NotDashboardUser));
                    string section = Section(AiTools.Str(a, "sectionKey"));
                    if (section == null) return Task.FromResult<object>(Refuse(UnknownSection));
                    return Task.FromResult<object>(Done(new AiActionDto { Type = ScrollAction, SectionKey = section, Title = "رفتن به بخش " + CeoModules.Title(section) }, "صفحه به بخش " + CeoModules.Title(section) + " می‌رود."));
                },
            });
            return tools;
        }

        private static string UnknownSection => "کلید بخش ناشناخته است؛ یکی از این‌ها را بدهید: " + string.Join("، ", CeoModules.Sections) + ".";

        // ------------------------------------------------------------------------------------------ ui_open_drill

        private static AiToolOutput OpenDrill(AiToolContext c, JsonElement a)
        {
            if (!c.User.IsInRole(CeoDrillRoles.Readers)) return Refuse(NotDashboardUser);
            string key = AiTools.Str(a, "key");
            if (key == null) return Refuse(CeoDrillText.KeyRequired);
            DrillDefinition drill = CeoDrillCatalog.Find(key);
            if (drill == null) return Refuse(CeoDrillText.UnknownKey + ".");

            Dictionary<string, string> given = DrillParameters(a);
            var declared = drill.Parameters.Select(p => p.Name).Concat(new[] { "top", "page" }).ToList();
            string unknown = given.Keys.FirstOrDefault(k => !declared.Contains(k, StringComparer.OrdinalIgnoreCase));
            if (unknown != null) return Refuse("پارامتر «" + unknown + "» برای این جزئیات تعریف نشده است؛ پارامترها: " + string.Join("، ", declared) + ".");
            given.TryGetValue("top", out string top);
            given.TryGetValue("page", out string page);
            string error = DrillBinder.BindPaging(top, page, DrillBinder.DefaultTop, out _, out _) ?? DrillBinder.Bind(drill.Parameters, given, DateTime.Today, out _);
            if (error != null) return Refuse(error);

            // the names as the catalogue writes them
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kv in given)
                parameters[declared.First(d => string.Equals(d, kv.Key, StringComparison.OrdinalIgnoreCase))] = kv.Value;
            var action = new AiActionDto { Type = OpenDrillAction, DrillKey = drill.Key, Params = parameters.Count == 0 ? null : parameters, Title = "جزئیات: " + drill.Title };
            return Done(action, "جدول «" + drill.Title + "» باز می‌شود.");
        }

        /// <summary>The parameters the model gave, from <c>params</c> or flat next to the key (models do both): name and text of every scalar.</summary>
        private static Dictionary<string, string> DrillParameters(JsonElement a)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (a.ValueKind != JsonValueKind.Object) return result;
            void Take(JsonElement obj, bool flat)
            {
                foreach (JsonProperty p in obj.EnumerateObject())
                {
                    if (flat && (p.NameEquals("key") || p.NameEquals("params"))) continue;
                    string text = p.Value.ValueKind switch
                    {
                        JsonValueKind.String => p.Value.GetString(),
                        JsonValueKind.Number => p.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => null,
                    };
                    if (!string.IsNullOrWhiteSpace(text)) result[p.Name] = text.Trim();
                }
            }
            Take(a, true);
            if (a.TryGetProperty("params", out JsonElement nested) && nested.ValueKind == JsonValueKind.Object) Take(nested, false);
            return result;
        }

        // ------------------------------------------------------------------------------------------ the actions of an answer

        /// <summary>The actions of the successful ui_* calls of a turn, in the order of the calls; an identical action is kept once and at most <see cref="MaxActions"/> are kept.</summary>
        public static List<AiActionDto> Collect(IEnumerable<AiToolResult> results)
        {
            var actions = new List<AiActionDto>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AiToolResult r in results ?? Enumerable.Empty<AiToolResult>())
            {
                if (r?.Action == null) continue;
                if (!seen.Add(Signature(r.Action))) continue;
                actions.Add(r.Action);
                if (actions.Count >= MaxActions) break;
            }
            return actions;
        }

        private static string Signature(AiActionDto a) =>
            string.Join("|", a.Type, a.Link, a.DrillKey, a.SectionKey, a.What, a.Params == null ? string.Empty : string.Join(",", a.Params.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)));

        /// <summary>The audit text of the actions (after "actions:"): type and target of each, separated by commas.</summary>
        public static string AuditText(IEnumerable<AiActionDto> actions) => string.Join(",", (actions ?? Enumerable.Empty<AiActionDto>()).Select(a => a.Type + ":" + Target(a)));

        private static string Target(AiActionDto a) => a.Type switch
        {
            NavigateAction => a.Link,
            OpenDrillAction => a.DrillKey,
            AnalyzeAction => a.SectionKey ?? "all",
            ReadAction => a.SectionKey == null ? a.What : a.What + "/" + a.SectionKey,
            _ => a.SectionKey,
        };

        // ------------------------------------------------------------------------------------------ the system prompt

        /// <summary>The paragraph added to the system prompt of the chat and of the analysis (whatever version of the prompt is active), so a database that was seeded earlier gets it too.</summary>
        public const string PromptParagraph =
            "کارهای روی صفحه: اگر کاربر خواست صفحه‌ای را باز کنی، به جایی بروی، جزئیاتی را نشان بدهی، تحلیلی را اجرا کنی یا بخشی، هشدارها یا گزارش روزانه را بخوانی، ابزار مناسب را صدا بزن "
            + "(ui_navigate برای باز کردن صفحه، ui_open_drill برای جدول جزئیات، ui_analyze برای تحلیل، ui_read برای خواندن با صدا، ui_scroll برای رفتن به یک بخش) "
            + "و فقط با یک جمله‌ی تأییدی کوتاه پاسخ بده، مثلا «صفحه‌ی سفارش‌های تولید را باز می‌کنم». این ابزارها فقط صفحه‌ی کاربر را عوض می‌کنند و چیزی را تغییر نمی‌دهند؛ "
            + "تأیید، نهایی‌کردن، پرداخت و حذف همیشه با خود کاربر در صفحه‌ی مربوط است و تو فقط می‌توانی همان صفحه را باز کنی. "
            + "هرگز نگو کاری را انجام می‌دهی یا انجام دادی مگر اینکه ابزارش را صدا زده باشی و نتیجه‌اش ok باشد؛ اگر نتیجه‌ی ابزار ok نبود، فقط دلیلش را کوتاه بگو.";

        /// <summary>The text appended to the system prompt: the paragraph and, when the front end sent one, a line about the page. Only known values reach the prompt.</summary>
        public static string PromptAddition(AiUiContextDto ui)
        {
            var sb = new StringBuilder("\n\n").Append(PromptParagraph);
            string line = ContextLine(ui);
            if (line.Length > 0) sb.Append('\n').Append(line);
            return sb.ToString();
        }

        public static string ContextLine(AiUiContextDto ui)
        {
            if (ui == null) return string.Empty;
            string page = null;
            if (TryCheckLink(ui.Page, out string clean, out _))
            {
                int cut = clean.IndexOfAny(new[] { '?', '#' });
                page = cut < 0 ? clean : clean.Substring(0, cut);
            }
            List<string> sections = (ui.Sections ?? new List<string>()).Select(Section).Where(s => s != null).Distinct().ToList();
            string theme = ui.Theme?.Trim().ToLowerInvariant();
            theme = theme == "dark" || theme == "light" ? theme : null;
            if (page == null && sections.Count == 0 && theme == null) return string.Empty;

            var sb = new StringBuilder("صفحه‌ی کاربر:");
            if (page != null) sb.Append(" مسیر ").Append(page).Append('.');
            if (sections.Count > 0) sb.Append(" بخش‌های همین صفحه (sectionKey): ").Append(string.Join("، ", sections.Select(s => s + " (" + CeoModules.Title(s) + ")"))).Append('.');
            if (theme != null) sb.Append(" پوسته: ").Append(theme == "dark" ? "تیره" : "روشن").Append('.');
            return sb.ToString();
        }
    }
}
