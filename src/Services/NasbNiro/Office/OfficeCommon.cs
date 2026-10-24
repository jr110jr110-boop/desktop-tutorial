using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;

namespace Services.NasbNiro.Office
{
    /// <summary>Role names (as written in the JWT, upper case) for the office automation module. Every user may draft letters and requests and read their own inbox.</summary>
    public static class OfficeRoles
    {
        public const string Secretariat = "SECRETARIAT";
        public const string Hr = "HR";
        public const string Ceo = "CEO";

        /// <summary>Organisation chart, positions, shifts and devices.</summary>
        public const string OrgAdmins = "HR,PMO,CEO";

        /// <summary>Numbering rules and letter templates.</summary>
        public const string LetterAdmins = "SECRETARIAT,PMO,CEO";

        /// <summary>Attendance of everybody; ordinary users see their own days only.</summary>
        public const string AttendanceReaders = "HR,CEO";
    }

    /// <summary>Counter kinds in <c>DocumentNumberSequence</c> (the office module uses 531..560; letters use the numbering rules).</summary>
    internal static class OfficeNumbers
    {
        public const int Meeting = 531;
        public const int LeaveRequest = 532;
        public const int Request = 533;
    }

    internal static class OfficeText
    {
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string InvalidData = "اطلاعات ارسال‌شده کامل نیست.";
        public const string Concurrency = "اطلاعات هم‌زمان توسط کاربر دیگری تغییر کرد. صفحه را دوباره باز کنید و دوباره تلاش کنید.";

        public static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        public static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);
        public static ResponseDto<T> NotFound<T>(string message) => ResponseDtoHelper.NotFound<T>(message);

        /// <summary>Persian (Shamsi) date for everything a person reads, for example 1405/08/03.</summary>
        public static string Date(DateTime d) => InventoryText.PersianDate(d);

        public static string Date(DateTime? d) => d.HasValue ? InventoryText.PersianDate(d.Value) : null;

        public static string Date(DateTimeOffset? d) => d.HasValue ? InventoryText.PersianDate(d.Value.LocalDateTime) : null;

        /// <summary>Persian date and time, for example 1405/08/03 14:05.</summary>
        public static string Stamp(DateTime d) => InventoryText.PersianDate(d) + " " + d.ToString("HH:mm", CultureInfo.InvariantCulture);

        public static string Stamp(DateTimeOffset? d) => d.HasValue ? Stamp(d.Value.LocalDateTime) : null;

        public static string Minute(int minuteOfDay) => $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}";

        /// <summary>"08:30" to 510; null when the text is not a time of day.</summary>
        public static int? ParseMinute(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string[] parts = text.Trim().Split(':');
            if (parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int h) || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int m)) return null;
            if (h < 0 || h > 23 || m < 0 || m > 59) return null;
            return h * 60 + m;
        }

        /// <summary>Persian digits and the Arabic comma and slash to Latin, for codes typed on a Persian keyboard.</summary>
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

        public static List<int> Ids(string csv) =>
            string.IsNullOrWhiteSpace(csv)
                ? new List<int>()
                : csv.Split(new[] { ',', '،', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : 0).Where(v => v > 0).ToList();
    }

    /// <summary>A position the signed-in user may act for: held by the user, or delegated to the user as a deputy today.</summary>
    internal sealed class ActingPosition
    {
        public int PositionId { get; set; }

        /// <summary>The user is a deputy, not the holder: every action is recorded "on behalf of".</summary>
        public bool OnBehalf { get; set; }
    }

    internal static class OfficeActors
    {
        public static async Task<List<ActingPosition>> ActingAsync(IUnitOfWork uow, int userId, CancellationToken ct)
        {
            DateTime today = DateTime.Today;
            var result = new List<ActingPosition>();
            if (userId <= 0) return result;
            List<int> own = await uow.Set<OfPosition>().AsNoTracking().Where(p => !p.Deleted && p.IsActive && p.UserId == userId).Select(p => p.Id).ToListAsync(ct);
            foreach (int id in own) result.Add(new ActingPosition { PositionId = id, OnBehalf = false });
            List<int> delegated = await uow.Set<OfDelegation>().AsNoTracking()
                .Where(d => !d.Deleted && d.IsActive && d.DeputyUserId == userId && d.FromDate <= today && d.ToDate >= today).Select(d => d.PositionId).Distinct().ToListAsync(ct);
            if (delegated.Count > 0)
            {
                List<int> active = await uow.Set<OfPosition>().AsNoTracking().Where(p => !p.Deleted && p.IsActive && delegated.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
                foreach (int id in active) if (result.All(r => r.PositionId != id)) result.Add(new ActingPosition { PositionId = id, OnBehalf = true });
            }
            return result;
        }

        public static async Task<Dictionary<int, string>> PositionTitlesAsync(IUnitOfWork uow, IEnumerable<int?> ids, CancellationToken ct)
        {
            List<int> list = ids.Where(i => i.HasValue).Select(i => i.Value).Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, string>();
            return await uow.Set<OfPosition>().AsNoTracking().Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Title, ct);
        }

        public static string Title(this Dictionary<int, string> titles, int? id) => id.HasValue && titles.TryGetValue(id.Value, out string t) ? t : null;

        /// <summary>The units root is seeded once, idempotently.</summary>
        public static async Task<OfUnit> EnsureRootUnitAsync(IUnitOfWork uow, CancellationToken ct)
        {
            OfUnit root = await uow.Set<OfUnit>().FirstOrDefaultAsync(u => !u.Deleted && u.ParentId == null && u.Code == "CO", ct);
            if (root != null) return root;
            DateTimeOffset now = DateTimeOffset.Now;
            root = new OfUnit { Code = "CO", Title = "شرکت", IsActive = true, CreateDate = now, ModifyDate = now };
            uow.Set<OfUnit>().Add(root);
            try { await uow.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                uow.Entry(root).State = EntityState.Detached;
                root = await uow.Set<OfUnit>().FirstOrDefaultAsync(u => !u.Deleted && u.Code == "CO", ct);
            }
            return root;
        }
    }

    /// <summary>Rich text is kept as HTML. Only a short list of formatting tags and attributes survives; scripts, styles, event handlers and unsafe links are removed.</summary>
    internal static class OfficeHtml
    {
        private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "p", "br", "b", "strong", "i", "em", "u", "s", "sub", "sup", "ul", "ol", "li", "h1", "h2", "h3", "h4", "h5", "h6", "table", "thead", "tbody", "tfoot",
            "tr", "th", "td", "blockquote", "span", "div", "hr", "a", "img", "pre", "code",
        };

        private static readonly HashSet<string> Void = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "br", "hr", "img" };

        private static readonly Regex DropBlocks = new Regex(
            @"<\s*(script|style|iframe|object|embed|svg|math|form|textarea|select|button|applet|frameset|frame|noscript|template)\b[^>]*>.*?<\s*/\s*\1\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex Comments = new Regex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex Tag = new Regex(@"<\s*(/?)\s*([a-zA-Z][a-zA-Z0-9]*)((?:""[^""]*""|'[^']*'|[^'"">])*)>", RegexOptions.Compiled);
        private static readonly Regex Attr = new Regex(@"([a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*(?:=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+)))?", RegexOptions.Compiled);

        private static readonly HashSet<string> GlobalAttrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dir", "class", "colspan", "rowspan", "align", "title" };

        public static string Sanitize(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;
            string text = Comments.Replace(html, "");
            string previous;
            do { previous = text; text = DropBlocks.Replace(text, ""); } while (text != previous);
            var sb = new StringBuilder(text.Length);
            int position = 0;
            foreach (Match m in Tag.Matches(text))
            {
                sb.Append(EscapeText(text.Substring(position, m.Index - position)));
                position = m.Index + m.Length;
                string name = m.Groups[2].Value.ToLowerInvariant();
                if (!Allowed.Contains(name)) continue;
                if (m.Groups[1].Value == "/")
                {
                    if (!Void.Contains(name)) sb.Append("</").Append(name).Append('>');
                    continue;
                }
                sb.Append('<').Append(name);
                foreach (Match a in Attr.Matches(m.Groups[3].Value))
                {
                    string attribute = a.Groups[1].Value.ToLowerInvariant();
                    string value = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Success ? a.Groups[4].Value : "";
                    value = WebUtility.HtmlDecode(value);
                    bool keep = GlobalAttrs.Contains(attribute)
                        || (name == "a" && attribute == "href" && SafeUrl(value, false))
                        || (name == "img" && attribute == "src" && SafeUrl(value, true))
                        || (name == "img" && (attribute == "alt" || attribute == "width" || attribute == "height"));
                    if (keep) sb.Append(' ').Append(attribute).Append("=\"").Append(WebUtility.HtmlEncode(value)).Append('"');
                }
                sb.Append('>');
            }
            sb.Append(EscapeText(text.Substring(position)));
            string result = sb.ToString().Trim();
            return result.Length == 0 ? null : result;
        }

        private static string EscapeText(string s) => s.Replace("<", "&lt;");

        private static bool SafeUrl(string value, bool image)
        {
            string v = new string(value.Where(c => !char.IsControl(c) && !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
            if (v.Length == 0) return false;
            if (v.StartsWith("data:image/png;base64,") || v.StartsWith("data:image/jpeg;base64,") || v.StartsWith("data:image/gif;base64,")) return image;
            if (v.StartsWith("http://") || v.StartsWith("https://")) return true;
            if (!image && (v.StartsWith("mailto:") || v.StartsWith("tel:") || v.StartsWith("#"))) return true;
            int colon = v.IndexOf(':');
            int slash = v.IndexOf('/');
            return colon < 0 || (slash >= 0 && slash < colon);
        }
    }
}
