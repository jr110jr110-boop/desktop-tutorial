using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Entities.Model.NasbNiro.ProductBank;

namespace Services.NasbNiro.ProductBank
{
    public class MspdiTask
    {
        public int Uid { get; set; }
        public string Name { get; set; }
        public int OutlineLevel { get; set; }
        public bool IsSummary { get; set; }
        public bool IsMilestone { get; set; }
        public decimal DurationDays { get; set; }
        public DateTime? Start { get; set; }
        public List<(int PredecessorUid, PbLinkType Type, decimal LagDays)> Predecessors { get; } = new List<(int, PbLinkType, decimal)>();
        public List<string> Resources { get; } = new List<string>();
    }

    /// <summary>
    /// Reads a Microsoft Project "Save As XML" file (MSPDI) with System.Xml only: tasks with outline levels, durations (PT..H..M..S, converted to days with the calendar's minutes per day),
    /// predecessor links and resource assignments. No new package; the file is text, so the import works offline.
    /// </summary>
    public static class PbMspdi
    {
        private static readonly Regex PartNumberRx = new Regex(@"\b\d{2}\.\d{2}\.\d{2}\.\d{3}\.\d\b", RegexOptions.Compiled);
        private static readonly Regex DurationRx = new Regex(@"^P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+(?:\.\d+)?)S)?)?$", RegexOptions.Compiled);

        /// <summary>Hours of a working day when the file says nothing (480 minutes).</summary>
        public const int DefaultMinutesPerDay = 480;

        /// <summary>Largest file and task count accepted (the body of a request is limited by the web server as well).</summary>
        public const int MaxXmlLength = 8_000_000;
        public const int MaxTasks = 5000;

        public static decimal ParseDurationDays(string iso, int minutesPerDay) => ParseDurationDays(iso, minutesPerDay, false, out _);

        /// <summary>PT..H..M..S to days. ok = false when the text is not a duration (it is not silently read as zero). An elapsed duration counts 24-hour days, not working days.</summary>
        public static decimal ParseDurationDays(string iso, int minutesPerDay, bool elapsed, out bool ok)
        {
            ok = true;
            if (string.IsNullOrWhiteSpace(iso)) return 0;
            Match m = DurationRx.Match(iso.Trim());
            if (!m.Success) { ok = false; return 0; }
            if (elapsed) minutesPerDay = 1440;
            decimal d = m.Groups[1].Success ? decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            decimal h = m.Groups[2].Success ? decimal.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            decimal mi = m.Groups[3].Success ? decimal.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            decimal s = m.Groups[4].Success ? decimal.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 0;
            decimal minutes = h * 60 + mi + s / 60;
            return Math.Round(d + minutes / Math.Max(1, minutesPerDay), 4);
        }

        public static string PartNumberOf(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            Match m = PartNumberRx.Match(text);
            return m.Success ? m.Value : null;
        }

        /// <summary>E / P / C when the task is one of the three activities of a part (named "E", "P", "C" or Engineering, Procurement, Construction).</summary>
        public static string PhaseOf(string name)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 1 && "EPC".Contains(char.ToUpperInvariant(n[0]))) return char.ToUpperInvariant(n[0]).ToString();
            switch (n.ToLowerInvariant())
            {
                case "engineering": return "E";
                case "procurement": return "P";
                case "construction": return "C";
                default: return null;
            }
        }

        public static string ResourceClassOf(string resourceName)
        {
            string r = (resourceName ?? "").ToLowerInvariant();
            if (r.Contains("mill")) return "CNC_MILLING";
            if (r.Contains("lathe") || r.Contains("turn")) return "LATHE";
            if (r.Contains("assembl")) return "ASSEMBLY";
            if (r.Contains("qc") || r.Contains("quality")) return "QC";
            return string.IsNullOrWhiteSpace(resourceName) ? null : resourceName.Trim();
        }

        public static (List<MspdiTask> Tasks, string ProjectName, string Error) Parse(string xml)
        {
            var r = ParseWithWarnings(xml);
            return (r.Tasks, r.ProjectName, r.Error);
        }

        /// <summary>DurationFormat values of MSPDI that count elapsed time: elapsed minutes, hours, days, weeks, months and elapsed percent.</summary>
        private static readonly HashSet<int> ElapsedFormats = new HashSet<int> { 4, 6, 8, 10, 12, 20 };

        public static (List<MspdiTask> Tasks, string ProjectName, string Error, List<string> Warnings) ParseWithWarnings(string xml)
        {
            var warnings = new List<string>();
            var tasks = new List<MspdiTask>();
            if (string.IsNullOrWhiteSpace(xml)) return (tasks, null, "فایل XML خالی است.", warnings);
            if (xml.Length > MaxXmlLength) return (tasks, null, "فایل XML از حد مجاز (۸ مگابایت) بزرگ‌تر است.", warnings);
            XDocument doc;
            try
            {
                // DTD processing is off: the file is user input.
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (var sr = new System.IO.StringReader(xml.TrimStart('﻿')))
                using (XmlReader reader = XmlReader.Create(sr, settings))
                    doc = XDocument.Load(reader);
            }
            catch (XmlException)
            {
                return (tasks, null, "فایل XML معتبر نیست؛ از Project گزینهٔ Save As ← XML Format را بزنید.", warnings);
            }

            XElement root = doc.Root;
            if (root == null || root.Name.LocalName != "Project") return (tasks, null, "این فایل خروجی XML نرم‌افزار Microsoft Project نیست.", warnings);
            XNamespace ns = root.Name.Namespace;

            int minutesPerDay = DefaultMinutesPerDay;
            string mpd = (string)root.Element(ns + "MinutesPerDay");
            if (int.TryParse(mpd, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mp) && mp > 0) minutesPerDay = mp;

            var resources = new Dictionary<int, string>();
            XElement resNode = root.Element(ns + "Resources");
            if (resNode != null)
                foreach (XElement r in resNode.Elements(ns + "Resource"))
                    if (int.TryParse((string)r.Element(ns + "UID"), out int ruid)) resources[ruid] = (string)r.Element(ns + "Name");

            var byUid = new Dictionary<int, MspdiTask>();
            XElement tasksNode = root.Element(ns + "Tasks");
            if (tasksNode == null) return (tasks, (string)root.Element(ns + "Name"), "فایل هیچ فعالیتی ندارد.", warnings);
            if (tasksNode.Elements(ns + "Task").Count() > MaxTasks) return (tasks, (string)root.Element(ns + "Name"), "تعداد فعالیت‌ها از حد مجاز (۵۰۰۰) بیشتر است.", warnings);
            if (root.Element(ns + "Calendars") != null) warnings.Add("تقویم‌های فایل خوانده نشد؛ روز کاری برابر MinutesPerDay فایل (پیش‌فرض ۴۸۰ دقیقه) حساب شد.");
            bool percentLag = false;
            foreach (XElement t in tasksNode.Elements(ns + "Task"))
            {
                if (!int.TryParse((string)t.Element(ns + "UID"), out int uid)) continue;
                if (uid == 0) continue; // the project summary row of MSPDI
                if ((string)t.Element(ns + "Null") == "1") continue;
                int.TryParse((string)t.Element(ns + "OutlineLevel"), out int level);
                var task = new MspdiTask
                {
                    Uid = uid,
                    Name = ((string)t.Element(ns + "Name") ?? "").Trim(),
                    OutlineLevel = Math.Max(1, level),
                    IsSummary = (string)t.Element(ns + "Summary") == "1",
                    IsMilestone = (string)t.Element(ns + "Milestone") == "1",
                };
                int.TryParse((string)t.Element(ns + "DurationFormat"), out int durationFormat);
                task.DurationDays = ParseDurationDays((string)t.Element(ns + "Duration"), minutesPerDay, ElapsedFormats.Contains(durationFormat), out bool durationOk);
                if (!durationOk) return (new List<MspdiTask>(), (string)root.Element(ns + "Name"), "مدت فعالیت «" + task.Name + "» خوانده نشد (قالب PT..H..M..S لازم است).", warnings);
                if (DateTime.TryParse((string)t.Element(ns + "Start"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime st)) task.Start = st;
                foreach (XElement p in t.Elements(ns + "PredecessorLink"))
                {
                    if (!int.TryParse((string)p.Element(ns + "PredecessorUID"), out int puid)) continue;
                    // a link without a Type element is finish-to-start (the MSPDI default)
                    int type = (int)PbLinkType.FinishToStart;
                    if (p.Element(ns + "Type") != null) int.TryParse((string)p.Element(ns + "Type"), out type);
                    // LinkLag is in tenths of a minute; a percent lag (LagFormat 19, 20) is not a duration: it is read as zero and reported
                    decimal lag = 0;
                    int.TryParse((string)p.Element(ns + "LagFormat"), out int lagFormat);
                    if (lagFormat == 19 || lagFormat == 20) percentLag = true;
                    else if (decimal.TryParse((string)p.Element(ns + "LinkLag"), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rawLag))
                        lag = Math.Round(rawLag / 10m / Math.Max(1, minutesPerDay), 4);
                    task.Predecessors.Add((puid, Enum.IsDefined(typeof(PbLinkType), type) ? (PbLinkType)type : PbLinkType.FinishToStart, lag));
                }
                tasks.Add(task);
                byUid[uid] = task;
            }

            XElement asg = root.Element(ns + "Assignments");
            if (asg != null)
                foreach (XElement a in asg.Elements(ns + "Assignment"))
                {
                    if (!int.TryParse((string)a.Element(ns + "TaskUID"), out int tuid) || !int.TryParse((string)a.Element(ns + "ResourceUID"), out int ruid)) continue;
                    if (byUid.TryGetValue(tuid, out MspdiTask task) && resources.TryGetValue(ruid, out string rn) && !string.IsNullOrWhiteSpace(rn)) task.Resources.Add(rn.Trim());
                }

            if (percentLag) warnings.Add("تأخیر درصدی برخی وابستگی‌ها پشتیبانی نمی‌شود و صفر خوانده شد.");
            if (tasks.Count == 0) return (tasks, (string)root.Element(ns + "Name"), "فایل هیچ فعالیتی ندارد.", warnings);
            return (tasks, (string)root.Element(ns + "Name"), null, warnings);
        }
    }
}
