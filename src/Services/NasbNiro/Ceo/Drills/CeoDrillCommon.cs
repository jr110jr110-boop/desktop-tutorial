using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Ceo.Drills
{
    // Release D1 part A2: the drill catalogue of the CEO dashboard. A drill is a read-only table behind a KPI or a chart. Every drill calls the existing module services in the
    // request scope, so the role checks and row filters of the modules apply to the caller (the AI tool is the same caller).

    /// <summary>The nine sections of the dashboard (in the order of the overview) and the modules of the drills.</summary>
    public static class CeoModules
    {
        public const string Finance = "finance";
        public const string Sales = "sales";
        public const string Projects = "projects";
        public const string Production = "production";
        public const string Quality = "quality";
        public const string Inventory = "inventory";
        public const string Procurement = "procurement";
        public const string Service = "service";
        public const string Office = "office";
        public const string Ai = "ai";

        /// <summary>The sections of the overview, in order.</summary>
        public static readonly IReadOnlyList<string> Sections = new[] { Finance, Sales, Projects, Production, Quality, Inventory, Procurement, Service, Office };

        /// <summary>The modules of the drills, in the order of the catalogue.</summary>
        public static readonly IReadOnlyList<string> All = new[] { Finance, Sales, Projects, Production, Quality, Inventory, Procurement, Service, Office, Ai };

        public static string Title(string module) => module switch
        {
            Finance => "مالی",
            Sales => "فروش",
            Projects => "پروژه‌ها",
            Production => "تولید",
            Quality => "کیفیت",
            Inventory => "انبار",
            Procurement => "تدارکات",
            Service => "خدمات پس از فروش",
            Office => "دفتر",
            Ai => "هوش مصنوعی",
            _ => module,
        };
    }

    public static class CeoDrillText
    {
        public const string UnknownKey = "کلید جزئیات ناشناخته است";
        public const string KeyRequired = "کلید جزئیات را بدهید.";
        public const string Failed = "درخواست انجام نشد.";
        public const string PayrollHidden = "این گزارش شامل هزینه‌ی کارکرد پرسنل است و برای نقش شما محرمانه است (حقوق).";
        public const string DateExample = "نمونه: 2026-10-25 یا 1405/08/03";

        /// <summary>A module list that does not fit in the pages one drill may read: asked to be narrower, never cut silently.</summary>
        public const string TooManyRows = "تعداد رکوردها از حدی که یک جزئیات می‌تواند بخواند بیشتر است؛ بازه‌ی تاریخ یا فیلترها را محدودتر کنید.";
    }

    /// <summary>A module answered with a rule violation or a not-found message; the drill service turns it into a Persian answer.</summary>
    public sealed class DrillFailureException : Exception
    {
        public DrillFailureException(string message) : base(message)
        {
        }
    }

    // ------------------------------------------------------------------------------------------ the definition of a drill

    /// <summary>One typed parameter of a drill. Type is int, date, string or bool.</summary>
    public sealed class DrillParam
    {
        public string Name { get; init; }
        public string Type { get; init; }
        public bool Required { get; init; }
        public string Description { get; init; }
        public int? Min { get; init; }
        public int? Max { get; init; }

        /// <summary>The values a string parameter may have (compared without case).</summary>
        public string[] Allowed { get; init; }

        /// <summary>A constant default (int, bool or string).</summary>
        public object Default { get; init; }

        /// <summary>A default that depends on today (a date).</summary>
        public Func<DateTime, object> DefaultFrom { get; init; }
    }

    /// <summary>Parameter factories (the description says what the default is).</summary>
    public static class Prm
    {
        public static DrillParam Int(string name, string description, int? def = null, int? min = null, int? max = null, bool required = false) =>
            new DrillParam { Name = name, Type = "int", Description = description, Default = def, Min = min, Max = max, Required = required };

        public static DrillParam Date(string name, string description, Func<DateTime, object> defaultFrom = null, bool required = false) =>
            new DrillParam { Name = name, Type = "date", Description = description, DefaultFrom = defaultFrom, Required = required };

        public static DrillParam Str(string name, string description, string def = null, string[] allowed = null, bool required = false) =>
            new DrillParam { Name = name, Type = "string", Description = description, Default = def, Allowed = allowed, Required = required };

        public static DrillParam Bool(string name, string description, bool def = false) =>
            new DrillParam { Name = name, Type = "bool", Description = description, Default = def };

        /// <summary>from and to: the last 30 days up to today.</summary>
        public static DrillParam[] Last30Days(string what = "") => new[]
        {
            Date("from", "از تاریخ" + what + " (میلادی یا شمسی)؛ پیش‌فرض ۳۰ روز پیش", t => t.AddDays(-30)),
            Date("to", "تا تاریخ" + what + " (میلادی یا شمسی)؛ پیش‌فرض امروز", t => t),
        };

        /// <summary>from and to: the current Persian month (first day to the last day).</summary>
        public static DrillParam[] CurrentMonth(string what = "") => new[]
        {
            Date("from", "از تاریخ" + what + " (میلادی یا شمسی)؛ پیش‌فرض اول ماه شمسی جاری", t => DrillDates.MonthStart(t)),
            Date("to", "تا تاریخ" + what + " (میلادی یا شمسی)؛ پیش‌فرض آخر ماه شمسی جاری", t => DrillDates.MonthEnd(t)),
        };

        /// <summary>from and to: the first day of the current Persian month up to today (the month so far, as the module dashboards count it).</summary>
        public static DrillParam[] MonthToDate(string what = "") => new[]
        {
            Date("from", "از تاریخ" + what + " (میلادی یا شمسی)؛ پیش‌فرض اول ماه شمسی جاری", t => DrillDates.MonthStart(t)),
            Date("to", "تا تاریخ" + what + " (میلادی یا شمسی)؛ پیش‌فرض امروز", t => t),
        };

        /// <summary>from and to: the last twelve Persian months, the current one included (for the monthly series).</summary>
        public static DrillParam[] Last12Months(string what = "") => new[]
        {
            Date("from", "از تاریخ" + what + " (میلادی یا شمسی)؛ پیش‌فرض اول ماه شمسی ۱۱ ماه پیش (۱۲ ماه شمسی اخیر)", t => DrillDates.MonthStart(t, -11)),
            Date("to", "تا تاریخ" + what + " (میلادی یا شمسی)؛ پیش‌فرض امروز", t => t),
        };

        public static DrillParam Days(int def, int max, string what) =>
            Int("days", what + "؛ پیش‌فرض " + def.ToString(CultureInfo.InvariantCulture) + " روز", def, 1, max);
    }

    /// <summary>One column of a drill. Kind: text, number, rial, percent, date, link, status.</summary>
    public sealed class DrillColumn
    {
        public string Key { get; init; }
        public string Title { get; init; }
        public string Kind { get; init; }
    }

    public static class Col
    {
        public static DrillColumn Text(string key, string title) => new DrillColumn { Key = key, Title = title, Kind = "text" };
        public static DrillColumn Num(string key, string title) => new DrillColumn { Key = key, Title = title, Kind = "number" };
        public static DrillColumn Rial(string key, string title) => new DrillColumn { Key = key, Title = title, Kind = "rial" };
        public static DrillColumn Pct(string key, string title) => new DrillColumn { Key = key, Title = title, Kind = "percent" };
        public static DrillColumn Date(string key, string title) => new DrillColumn { Key = key, Title = title, Kind = "date" };
        public static DrillColumn Status(string key, string title) => new DrillColumn { Key = key, Title = title, Kind = "status" };

        /// <summary>The route of the record page in the front end.</summary>
        public static DrillColumn Link(string key = "link", string title = "پیوند") => new DrillColumn { Key = key, Title = title, Kind = "link" };
    }

    /// <summary>A row of a drill: the keys are the keys of the columns.</summary>
    public sealed class Row : Dictionary<string, object>
    {
        public Row()
        {
        }

        public Row(string link)
        {
            if (link != null) this["link"] = link;
        }
    }

    public sealed class DrillDefinition
    {
        public string Key { get; init; }
        public string Title { get; init; }
        public string Description { get; init; }

        /// <summary>One of <see cref="CeoModules.All"/>.</summary>
        public string Module { get; init; }

        /// <summary>The front-end page that lists the same records.</summary>
        public string Link { get; init; }

        /// <summary>The kind of record of the rows (the type of the citation the AI tool gives for a row), for example Ncr or SalesOrder.</summary>
        public string RecordType { get; init; }

        /// <summary>The Persian word in front of the number in the citation of a row, for example «گزارش عدم انطباق».</summary>
        public string RecordLabel { get; init; }
        public IReadOnlyList<DrillParam> Parameters { get; init; } = Array.Empty<DrillParam>();
        public IReadOnlyList<DrillColumn> Columns { get; init; } = Array.Empty<DrillColumn>();
        public Func<DrillContext, CancellationToken, Task<DrillResult>> Run { get; init; }
    }

    // ------------------------------------------------------------------------------------------ what a drill gets and gives

    /// <summary>The validated parameters of one request (defaults applied).</summary>
    public sealed class DrillArgs
    {
        private readonly Dictionary<string, object> _values;

        public DrillArgs(Dictionary<string, object> values)
        {
            _values = values ?? new Dictionary<string, object>();
        }

        public int Int(string name) => _values.TryGetValue(name, out object v) && v is int i ? i : 0;
        public DateTime Date(string name) => _values.TryGetValue(name, out object v) && v is DateTime d ? d : DateTime.Today;
        public DateTime? DateOrNull(string name) => _values.TryGetValue(name, out object v) && v is DateTime d ? d : (DateTime?)null;
        public string Str(string name) => _values.TryGetValue(name, out object v) ? v as string : null;
        public bool Bool(string name) => _values.TryGetValue(name, out object v) && v is bool b && b;
        public bool Has(string name) => _values.TryGetValue(name, out object v) && v != null;
    }

    public sealed class DrillContext
    {
        public DrillContext(IServiceProvider services, IInventoryUserContext user, DrillArgs args, int top, int page, DateTime today)
        {
            Services = services;
            User = user;
            Args = args;
            Top = top;
            Page = page;
            Today = today;
        }

        public IServiceProvider Services { get; }
        public IInventoryUserContext User { get; }
        public DrillArgs Args { get; }

        /// <summary>The page size.</summary>
        public int Top { get; }
        public int Page { get; }
        public DateTime Today { get; }

        public T Get<T>() => Services.GetRequiredService<T>();
    }

    public sealed class DrillResult
    {
        public List<Dictionary<string, object>> Rows { get; set; } = new List<Dictionary<string, object>>();
        public int TotalCount { get; set; }
        public string Note { get; set; }
        public bool Hidden { get; set; }
        public Dictionary<string, object> Totals { get; set; }

        /// <summary>Overrides the link of the definition (for example the page of one customer).</summary>
        public string Link { get; set; }

        /// <summary>A page cut in memory from the whole list.</summary>
        public static DrillResult Slice(IReadOnlyList<Row> all, DrillContext c, string note = null, Dictionary<string, object> totals = null)
        {
            int skip = Math.Max(0, (c.Page - 1)) * c.Top;
            return new DrillResult
            {
                Rows = all.Skip(skip).Take(c.Top).Cast<Dictionary<string, object>>().ToList(),
                TotalCount = all.Count,
                Note = note,
                Totals = totals,
            };
        }

        /// <summary>A page the module already cut (its own paging).</summary>
        public static DrillResult Paged<T>(PagedList<T> page, Func<T, Row> map, string note = null, Dictionary<string, object> totals = null) => new DrillResult
        {
            Rows = page.Select(map).Cast<Dictionary<string, object>>().ToList(),
            TotalCount = page.TotalCount,
            Note = note,
            Totals = totals,
        };

        /// <summary>The caller's role may not see the numbers: no rows, a note that says why.</summary>
        public static DrillResult Masked(string note = null) => new DrillResult { Hidden = true, Note = note ?? CeoDrillText.PayrollHidden };
    }

    // ------------------------------------------------------------------------------------------ dates

    public static class DrillDates
    {
        /// <summary>Shamsi text (1405/08/03) of a date, null for none.</summary>
        public static string Text(DateTime? d) => d.HasValue && d.Value.Year > 1000 ? InventoryText.PersianDate(d.Value) : null;

        public static string Text(DateTimeOffset? d) => d.HasValue ? Text(d.Value.LocalDateTime) : null;

        /// <summary>Shamsi date and the time (1405/08/03 14:30).</summary>
        public static string Stamp(DateTimeOffset? d) => d.HasValue ? InventoryText.PersianDate(d.Value.LocalDateTime) + " " + d.Value.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) : null;

        /// <summary>The first day of the Persian month that is <paramref name="monthOffset"/> months from the month of the date.</summary>
        public static DateTime MonthStart(DateTime date, int monthOffset = 0)
        {
            var calendar = new PersianCalendar();
            int year = calendar.GetYear(date);
            int month = calendar.GetMonth(date) + monthOffset;
            while (month < 1) { month += 12; year--; }
            while (month > 12) { month -= 12; year++; }
            return calendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
        }

        /// <summary>The last day of the Persian month of the date.</summary>
        public static DateTime MonthEnd(DateTime date)
        {
            var calendar = new PersianCalendar();
            int year = calendar.GetYear(date);
            int month = calendar.GetMonth(date);
            return calendar.ToDateTime(year, month, calendar.GetDaysInMonth(year, month), 0, 0, 0, 0);
        }

        /// <summary>The first days of the Persian months from the month of <paramref name="from"/> to the month of <paramref name="to"/>.</summary>
        public static List<DateTime> Months(DateTime from, DateTime to)
        {
            var list = new List<DateTime>();
            DateTime cursor = MonthStart(from);
            DateTime last = MonthStart(to);
            while (cursor <= last && list.Count < 60)
            {
                list.Add(cursor);
                cursor = MonthStart(cursor, 1);
            }
            return list;
        }

        public static bool TryParse(string text, out DateTime date)
        {
            date = default;
            string t = DrillText.Digits(text)?.Trim();
            if (string.IsNullOrEmpty(t)) return false;
            Match m = Regex.Match(t, @"^(\d{3,4})[/\-.](\d{1,2})[/\-.](\d{1,2})$", RegexOptions.CultureInvariant);
            try
            {
                if (m.Success)
                {
                    int y = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    int mo = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    int d = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                    date = y < 1700 ? new PersianCalendar().ToDateTime(y, mo, d, 0, 0, 0, 0) : new DateTime(y, mo, d);
                    return true;
                }
                if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed) && parsed.Year >= 1900 && parsed.Year <= 2200)
                {
                    date = parsed.Date;
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // a day or a month that does not exist
            }
            return false;
        }
    }

    public static class DrillText
    {
        /// <summary>Persian and Arabic-Indic digits to ASCII digits.</summary>
        public static string Digits(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c >= '۰' && c <= '۹') sb.Append((char)('0' + (c - '۰')));
                else if (c >= '٠' && c <= '٩') sb.Append((char)('0' + (c - '٠')));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        /// <summary>A long text cut to its first characters (descriptions in the rows of a table).</summary>
        public static string Short(string text, int length = 120) => text != null && text.Length > length ? text.Substring(0, length) : text;

        public static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

        /// <summary>Whole days from one date to another (negative when the second is earlier).</summary>
        public static int DaysBetween(DateTime from, DateTime to) => (int)(to.Date - from.Date).TotalDays;

        public static decimal Round(decimal value, int digits = 2) => Math.Round(value, digits, MidpointRounding.AwayFromZero);

        /// <summary>The percent of part in whole with one decimal (0 when whole is 0).</summary>
        public static decimal Percent(decimal part, decimal whole) => whole == 0m ? 0m : Math.Round(part * 100m / whole, 1, MidpointRounding.AwayFromZero);

        /// <summary>Takes the data of a module answer; a refusal or a rule violation becomes a <see cref="DrillFailureException"/> with the module's own Persian message.</summary>
        public static T Take<T>(ResponseDto<T> response)
        {
            if (response == null) throw new DrillFailureException(CeoDrillText.Failed);
            if (response.Success) return response.Data;
            throw new DrillFailureException(string.IsNullOrWhiteSpace(response.Message) ? CeoDrillText.Failed : response.Message);
        }
    }

    /// <summary>Reading a whole list of a module that pages its answers.</summary>
    public static class DrillRead
    {
        /// <summary>The page size that the sales, quality and project modules accept at most (they cut larger sizes to 500).</summary>
        public const int ModulePageSize = 500;

        /// <summary>The largest number of pages one drill reads from one list (20 000 rows at most).</summary>
        public const int MaxPages = 40;

        /// <summary>
        /// Calls the module page by page (page numbers from 1) until the list is complete; a refusal or a rule violation of the module becomes a <see cref="DrillFailureException"/>.
        /// A list that is still not complete after <see cref="MaxPages"/> pages is refused with a text that asks for a narrower range (<see cref="CeoDrillText.TooManyRows"/>), never cut silently.
        /// </summary>
        public static async Task<List<T>> AllAsync<T>(Func<int, Task<ResponseDto<PagedList<T>>>> fetch)
        {
            var all = new List<T>();
            for (int number = 1; number <= MaxPages; number++)
            {
                PagedList<T> page = DrillText.Take(await fetch(number));
                all.AddRange(page);
                if (page.Count == 0 || all.Count >= page.TotalCount) return all;
            }
            throw new DrillFailureException(CeoDrillText.TooManyRows);
        }
    }

    /// <summary>The role check of a drill that reads the database itself or calls a module service that has no role check of its own (the controller of that module has one).</summary>
    public static class Guard
    {
        public static void Require(DrillContext c, string roles)
        {
            if (!c.User.IsInRole(roles)) throw new DrillFailureException(CeoText.NotAllowed);
        }
    }

    // ------------------------------------------------------------------------------------------ binding of the parameters

    public static class DrillBinder
    {
        public const int DefaultTop = 50;
        public const int MaxTop = 200;

        /// <summary>The largest page number a request may ask for (a larger one is a mistake, and the rows skipped to reach it would be for nothing).</summary>
        public const int MaxPage = 10000;

        /// <summary>The first error as a Persian text, or null when the number of rows and the page are fine.</summary>
        public static string BindPaging(string topText, string pageText, int defaultTop, out int top, out int page)
        {
            top = defaultTop;
            page = 1;
            string t = DrillText.Clean(DrillText.Digits(topText));
            if (t != null && (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out top) || top < 1 || top > MaxTop))
                return "مقدار «تعداد» باید عددی بین 1 و " + MaxTop.ToString(CultureInfo.InvariantCulture) + " باشد.";
            string p = DrillText.Clean(DrillText.Digits(pageText));
            if (p != null && (!int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out page) || page < 1 || page > MaxPage))
                return "مقدار «صفحه» باید عدد صحیح بین 1 و " + MaxPage.ToString(CultureInfo.InvariantCulture) + " باشد.";
            return null;
        }

        /// <summary>Validates the raw texts against the declared parameters and applies the defaults. Parameters that are not declared are ignored.</summary>
        public static string Bind(IReadOnlyList<DrillParam> parameters, IReadOnlyDictionary<string, string> raw, DateTime today, out DrillArgs args)
        {
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            args = new DrillArgs(values);
            foreach (DrillParam p in parameters)
            {
                string text = null;
                if (raw != null)
                    foreach (KeyValuePair<string, string> kv in raw)
                        if (string.Equals(kv.Key, p.Name, StringComparison.OrdinalIgnoreCase)) { text = DrillText.Clean(kv.Value); break; }
                if (text == null)
                {
                    if (p.Required) return "پارامتر «" + p.Name + "» را بدهید.";
                    object fallback = p.DefaultFrom != null ? p.DefaultFrom(today) : p.Default;
                    if (fallback != null) values[p.Name] = fallback;
                    continue;
                }
                switch (p.Type)
                {
                    case "int":
                    {
                        if (!int.TryParse(DrillText.Digits(text), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                            return "مقدار «" + p.Name + "» باید عدد صحیح باشد.";
                        if ((p.Min.HasValue && n < p.Min.Value) || (p.Max.HasValue && n > p.Max.Value))
                            return "مقدار «" + p.Name + "» باید بین " + (p.Min ?? int.MinValue).ToString(CultureInfo.InvariantCulture) + " و " + (p.Max ?? int.MaxValue).ToString(CultureInfo.InvariantCulture) + " باشد.";
                        values[p.Name] = n;
                        break;
                    }
                    case "date":
                    {
                        if (!DrillDates.TryParse(text, out DateTime d)) return "مقدار «" + p.Name + "» تاریخ معتبر نیست (" + CeoDrillText.DateExample + ").";
                        values[p.Name] = d.Date;
                        break;
                    }
                    case "bool":
                    {
                        string b = DrillText.Digits(text).ToLowerInvariant();
                        if (b == "true" || b == "1" || b == "yes" || b == "بله") values[p.Name] = true;
                        else if (b == "false" || b == "0" || b == "no" || b == "خیر") values[p.Name] = false;
                        else return "مقدار «" + p.Name + "» باید true یا false باشد.";
                        break;
                    }
                    default:
                    {
                        if (text.Length > 100) return "مقدار «" + p.Name + "» بیش از حد طولانی است.";
                        if (p.Allowed != null)
                        {
                            string match = p.Allowed.FirstOrDefault(a => string.Equals(a, text, StringComparison.OrdinalIgnoreCase));
                            if (match == null) return "مقدار «" + p.Name + "» باید یکی از این‌ها باشد: " + string.Join("، ", p.Allowed) + ".";
                            text = match;
                        }
                        values[p.Name] = text;
                        break;
                    }
                }
            }
            if (values.TryGetValue("from", out object f) && values.TryGetValue("to", out object t) && f is DateTime from && t is DateTime to && from > to)
                return "تاریخ شروع (from) نباید بعد از تاریخ پایان (to) باشد.";
            return null;
        }
    }
}
