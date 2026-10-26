using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Ceo.Drills;
using ViewModels.NasbNiro.AiDtos;
using ViewModels.NasbNiro.CeoDtos;

namespace Services.NasbNiro.Ai
{
    /// <summary>
    /// The tools of the CEO dashboard (release D1): the section numbers of the overview, the drill catalogue and one drill. Roles CEO and PMO like the dashboard.
    /// The drill goes through <see cref="ICeoDrillService"/>, which calls the existing module services as the signed-in user.
    /// </summary>
    public static class AiCeoTools
    {
        public const string OverviewTool = "ceo_overview";
        public const string DrillsTool = "ceo_drills";
        public const string DrillTool = "ceo_drill";

        /// <summary>The rows the tool asks for when the model gives no <c>top</c>.</summary>
        public const int DefaultTop = 20;

        /// <summary>The page of the overview in the front end (the same as the existing CEO dashboard).</summary>
        public const string OverviewLink = "/finance/ceo-dashboard";

        /// <summary>Every parameter name a drill declares, with its JSON type and a Persian description for the model (a test checks that the catalogue uses no other name).</summary>
        public static readonly IReadOnlyDictionary<string, (string Type, string Description)> ParameterSchema = new Dictionary<string, (string, string)>
        {
            ["top"] = ("integer", "تعداد ردیف‌ها (پیش‌فرض 20، حداکثر 200)"),
            ["page"] = ("integer", "شماره‌ی صفحه (پیش‌فرض 1)"),
            ["from"] = ("string", "از تاریخ (شمسی 1405/08/01 یا میلادی 2026-10-23) برای کلیدهایی که بازه دارند"),
            ["to"] = ("string", "تا تاریخ (شمسی یا میلادی) برای کلیدهایی که بازه دارند"),
            ["days"] = ("integer", "تعداد روز برای کلیدهایی که سررسید، مهلت یا پنجره‌ی روزانه دارند (مثلا fin.cheques_due، qc.calibration_due، inv.slow_moving، sv.ltsa_expiring، ai.seat_runs)"),
            ["kind"] = ("string", "فقط برای fin.cheques_due: issued (صادرشده) یا received (دریافتی)"),
            ["by"] = ("string", "گروه‌بندی؛ برای sl.pipeline_by_stage: stage یا product، برای of.attendance_month: day یا unit یا person"),
            ["onlyCritical"] = ("boolean", "فقط برای pc.late_activities: فقط فعالیت‌های مسیر بحرانی"),
            ["weeks"] = ("integer", "فقط برای mf.work_center_load: تعداد هفته‌های پیش‌رو (پیش‌فرض 4)"),
            ["threshold"] = ("integer", "فقط برای mf.work_center_load: آستانه‌ی اضافه‌بار به درصد ظرفیت (پیش‌فرض 110)"),
            ["severity"] = ("string", "فقط برای qc.ncr_open: Major، Minor یا NotClassified"),
            ["overdueOnly"] = ("boolean", "فقط برای qc.complaints_open: فقط شکایت‌های گذشته از مهلت پاسخ"),
            ["source"] = ("string", "فقط برای qc.documents_waiting: all، inventory یا controlled"),
            ["priority"] = ("string", "فقط برای sv.requests_open: Critical، High، Normal یا Low"),
            ["goodwillOnly"] = ("boolean", "فقط برای sv.orders_open: فقط دستورهای منتظر تصمیم حسن نیت"),
            ["mineOnly"] = ("boolean", "فقط برای of.letters_waiting: فقط نامه‌های منتظر تایید یا امضای خود کاربر"),
            ["type"] = ("string", "فقط برای ai.notes_new: نوع یادداشت، مثلا OfficeLetter، QualityNcr، CeoBrief"),
            ["seat"] = ("string", "برای ai.notes_new و ai.seat_runs: کد صندلی هوش مصنوعی"),
            ["status"] = ("string", "فقط برای ai.seat_runs: Running، Succeeded، Failed یا Skipped"),
        };

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

        public static List<AiToolDefinition> Build()
        {
            var tools = new List<AiToolDefinition>();

            tools.Add(new AiToolDefinition
            {
                Name = OverviewTool, Title = "نمای کلی مدیرعامل", RequiredRoles = CeoDrillRoles.Readers, CitationType = "Dashboard", PageLink = OverviewLink,
                Description = "اعداد کلیدی داشبورد مدیرعامل به تفکیک بخش‌ها (مالی، فروش، پروژه‌ها، تولید، کیفیت، انبار، تدارکات، خدمات پس از فروش، دفتر): عنوان فارسی، مقدار، واحد و پیوند هر شاخص؛ بدون پارامتر همه‌ی بخش‌ها.",
                SchemaJson = Schema(new[] { ("section", "string", "کلید بخش: " + string.Join(", ", CeoModules.Sections) + "؛ اگر نیامد همه‌ی بخش‌ها", false) }),
                Run = async (c, a, ct) =>
                {
                    string section = AiTools.Str(a, "section")?.ToLowerInvariant();
                    if (section != null && !CeoModules.Sections.Contains(section))
                        throw new AiToolFailureException("بخش ناشناخته است؛ یکی از این‌ها را بدهید: " + string.Join("، ", CeoModules.Sections));
                    return await c.Get<ICeoSectionNumbersProvider>().GetAsync(section, ct);
                },
            });

            tools.Add(new AiToolDefinition
            {
                Name = DrillsTool, Title = "فهرست جزئیات داشبورد", RequiredRoles = CeoDrillRoles.Readers, CitationType = "Dashboard", PageLink = null,
                Description = "فهرست جزئیات (drill) داشبورد مدیرعامل: کلید، عنوان فارسی و نام پارامترهای هر جدول جزئیات به تفکیک ماژول؛ کلید را به ceo_drill بدهید.",
                Run = (c, a, ct) => Task.FromResult<object>(CeoDrillCatalog.Compact()),
            });

            tools.Add(new AiToolDefinition
            {
                Name = DrillTool, Title = "جزئیات داشبورد", RequiredRoles = CeoDrillRoles.Readers, CitationType = "Dashboard", PageLink = null,
                Description = "جدول جزئیات پشت یک شاخص داشبورد مدیرعامل (مثلاً qc.ncr_open گزارش‌های عدم انطباق باز، fin.receivables_by_customer مطالبات به تفکیک مشتری، mf.orders_late دستورهای تولید دارای تأخیر): ردیف‌های برتر، تعداد کل و پیوند هر رکورد. کلیدها را از ceo_drills بگیرید؛ top تعداد ردیف است.",
                SchemaJson = Schema(new[] { ("key", "string", "کلید جزئیات، مثلاً qc.ncr_open", true) }
                    .Concat(ParameterSchema.Select(p => (p.Key, p.Value.Type, p.Value.Description, false)))),
                Run = RunDrillAsync,
            });
            return tools;
        }

        // ------------------------------------------------------------------------------------------ ceo_drill

        private static async Task<object> RunDrillAsync(AiToolContext c, JsonElement a, CancellationToken ct)
        {
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (a.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty p in a.EnumerateObject())
                {
                    string text = p.Value.ValueKind switch
                    {
                        JsonValueKind.String => p.Value.GetString(),
                        JsonValueKind.Number => p.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => null,
                    };
                    if (!string.IsNullOrWhiteSpace(text)) parameters[p.Name] = text.Trim();
                }
            parameters.TryGetValue("key", out string key);
            parameters.TryGetValue("top", out string top);
            parameters.TryGetValue("page", out string page);

            ResponseDto<DrillTableDto> response = await c.Get<ICeoDrillService>().DrillAsync(key, top, page, parameters, ct, DefaultTop);
            if (!response.Success)
            {
                // an unknown key is refused (the call is not "allowed"); a refusal of a module is forbidden; anything else is an error result the model can read
                if (response.Message == CeoDrillText.UnknownKey) throw new AiToolRejectedException(CeoDrillText.UnknownKey);
                AiToolShaper.Unwrap(response);
                throw new AiToolFailureException(response.Message);
            }

            DrillTableDto table = response.Data;
            DrillDefinition definition = CeoDrillCatalog.Find(table.Key);
            var data = new Dictionary<string, object>
            {
                ["key"] = table.Key,
                ["title"] = table.Title,
                ["asOfText"] = table.AsOfText,
                ["totalCount"] = table.TotalCount,
                ["page"] = table.Page,
                ["pageSize"] = table.PageSize,
                ["columns"] = string.Join("؛ ", table.Columns.Where(x => x.Kind != "link").Select(x => x.Key + "=" + x.Title)),
            };
            if (table.Hidden) data["hidden"] = true;
            if (table.Note != null) data["note"] = table.Note;
            if (table.Link != null) data["link"] = table.Link;
            if (table.Totals != null && table.Totals.Count > 0) data["totals"] = table.Totals;
            data["items"] = table.Rows;

            var output = new AiToolOutput { Data = data };
            if (table.Link != null) output.Citations.Add(new AiCitationDto { Type = "Drill", Id = table.Key, Title = table.Title, Link = table.Link });
            // the first ten rows are the ones the registry keeps; a row with an id and a link is a record the user can open
            foreach (Dictionary<string, object> row in table.Rows.Take(AiToolShaper.ListLimit))
            {
                string citation = RowCitation(definition, row, out string id, out string link);
                if (citation != null) output.Citations.Add(new AiCitationDto { Type = definition?.RecordType ?? "Record", Id = id, Title = citation, Link = link });
            }
            return output;
        }

        /// <summary>The title of the citation of a row (the Persian label of the record and its number or name), or null for a row without id or link.</summary>
        public static string RowCitation(DrillDefinition definition, Dictionary<string, object> row, out string id, out string link)
        {
            id = null;
            link = null;
            if (row == null || !row.TryGetValue("id", out object idValue) || idValue == null) return null;
            if (!row.TryGetValue("link", out object linkValue) || linkValue is not string l || string.IsNullOrWhiteSpace(l)) return null;
            id = Convert.ToString(idValue, CultureInfo.InvariantCulture);
            link = l;
            string number = FirstText(row, "number", "code");
            string name = FirstText(row, "title", "name", "customerName", "subject");
            string label = (definition?.RecordLabel ?? string.Empty) + " " + (number ?? string.Empty);
            if (name != null && name != number) label = label.Trim() + " — " + (name.Length > 80 ? name.Substring(0, 80) : name);
            label = label.Trim(' ', '—');
            return string.IsNullOrEmpty(label) ? id : label;
        }

        private static string FirstText(Dictionary<string, object> row, params string[] keys)
        {
            foreach (string k in keys)
                if (row.TryGetValue(k, out object v) && v != null)
                {
                    string s = Convert.ToString(v, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                }
            return null;
        }
    }
}
