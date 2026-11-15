using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Services.NasbNiro.ProductBank
{
    /// <summary>A resource of an MSPDI file. Only identity and kind are read: the rates and costs of the file (StandardRate, OvertimeRate, Cost, ...) are never read, so they can never be imported.</summary>
    public class MspdiResource
    {
        public int Uid { get; set; }
        public string Name { get; set; }

        /// <summary>The «Code» field of the resource in the file (or its initials); empty when none.</summary>
        public string Code { get; set; }

        /// <summary>The MSPDI type: 0 material, 1 work, 2 cost.</summary>
        public int Type { get; set; }
    }

    /// <summary>One assignment of a resource to a task of the file. Work is in minutes (null: the file states no work), Units is the number the file gives (1 = 100 % for a work resource, the quantity for a material resource).</summary>
    public class MspdiAssignment
    {
        public int TaskUid { get; set; }
        public int ResourceUid { get; set; }
        public decimal? WorkMinutes { get; set; }
        public decimal Units { get; set; }
    }

    public class MspdiResourceFile
    {
        public string ProjectName { get; set; }
        public int MinutesPerDay { get; set; } = PbMspdi.DefaultMinutesPerDay;
        public List<MspdiTask> Tasks { get; } = new List<MspdiTask>();
        public List<MspdiResource> Resources { get; } = new List<MspdiResource>();
        public List<MspdiAssignment> Assignments { get; } = new List<MspdiAssignment>();
        public string Error { get; set; }
    }

    /// <summary>Reads the resources and the assignments of a Microsoft Project XML file (MSPDI), through the same safe XML loader as the task reader (no DTD, no external entity). No rate or cost is read.</summary>
    public static class PbMspdiResources
    {
        /// <summary>Work (P..DT..H..M..S) as minutes (a day is the file's MinutesPerDay); null when the text is empty or not a duration.</summary>
        public static decimal? WorkMinutes(string iso, int minutesPerDay = 1440)
        {
            if (string.IsNullOrWhiteSpace(iso)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(iso.Trim(), @"^P(?:(\d+(?:\.\d+)?)D)?(?:T(?:(\d+(?:\.\d+)?)H)?(?:(\d+(?:\.\d+)?)M)?(?:(\d+(?:\.\d+)?)S)?)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (!m.Success || iso.Trim().Length < 3) return null;
            decimal Part(int i) => m.Groups[i].Success ? decimal.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0m;
            return Math.Round(Part(1) * (minutesPerDay > 0 ? minutesPerDay : 1440) + Part(2) * 60m + Part(3) + Part(4) / 60m, 4);
        }

        public static MspdiResourceFile Read(string xml)
        {
            var file = new MspdiResourceFile();
            if (string.IsNullOrWhiteSpace(xml)) { file.Error = "فایل XML خالی است."; return file; }
            if (xml.Length > PbMspdi.MaxXmlLength) { file.Error = "فایل XML از حد مجاز (۸ مگابایت) بزرگ‌تر است."; return file; }
            XDocument doc = PbMspdi.LoadDocument(xml);
            if (doc == null) { file.Error = "فایل XML معتبر نیست؛ از Project گزینهٔ Save As ← XML Format را بزنید."; return file; }
            XElement root = doc.Root;
            if (root == null || root.Name.LocalName != "Project") { file.Error = "این فایل خروجی XML نرم‌افزار Microsoft Project نیست."; return file; }
            XNamespace ns = root.Name.Namespace;
            file.ProjectName = (string)root.Element(ns + "Name");
            if (int.TryParse((string)root.Element(ns + "MinutesPerDay"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mp) && mp > 0) file.MinutesPerDay = mp;

            var parsed = PbMspdi.ParseWithWarnings(doc);
            if (parsed.Error != null) { file.Error = parsed.Error; return file; }
            file.Tasks.AddRange(parsed.Tasks);

            XElement resNode = root.Element(ns + "Resources");
            if (resNode != null)
                foreach (XElement r in resNode.Elements(ns + "Resource"))
                {
                    if (!int.TryParse((string)r.Element(ns + "UID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int uid) || uid <= 0) continue; // UID 0 is the empty resource of the file
                    string name = ((string)r.Element(ns + "Name") ?? "").Trim();
                    if (name.Length == 0) continue;
                    int type = int.TryParse((string)r.Element(ns + "Type"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedType) ? parsedType : 1; // a resource without a type is a work resource
                    string code = ((string)r.Element(ns + "Code") ?? (string)r.Element(ns + "Initials") ?? "").Trim();
                    file.Resources.Add(new MspdiResource { Uid = uid, Name = name, Code = code, Type = type });
                }

            XElement asg = root.Element(ns + "Assignments");
            if (asg != null)
                foreach (XElement a in asg.Elements(ns + "Assignment"))
                {
                    if (!int.TryParse((string)a.Element(ns + "TaskUID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int tuid)
                        || !int.TryParse((string)a.Element(ns + "ResourceUID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ruid) || ruid <= 0 || tuid <= 0) continue;
                    decimal units = 0m;
                    decimal.TryParse((string)a.Element(ns + "Units"), NumberStyles.Any, CultureInfo.InvariantCulture, out units);
                    file.Assignments.Add(new MspdiAssignment { TaskUid = tuid, ResourceUid = ruid, WorkMinutes = WorkMinutes((string)a.Element(ns + "Work"), file.MinutesPerDay), Units = units });
                }
            return file;
        }
    }
}
