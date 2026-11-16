using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Services.NasbNiro.EngInbox
{
    /// <summary>
    /// The canonical content hash of a package: SHA-256 over the normalised parsed content (never the raw bytes of the workbook: saving the same content again from Excel does not change it) plus the name, size and SHA-256
    /// of every referenced file. The date and the preparer text are not part of it (a re-dated copy of the same content is the same package). The rows keep the order of the workbook.
    /// </summary>
    public static class EngPackageHash
    {
        public const string Version = "ENGPKG2";

        public static string Of(EngParsedPackage p, IReadOnlyDictionary<string, EngFileInfo> files)
        {
            var sb = new StringBuilder();
            string I(int n) => n.ToString(CultureInfo.InvariantCulture);
            // every field is written as length:text, so a "|" or a line break inside a text can never move a boundary between two fields (B4)
            void Row(string tag, params string[] fields)
            {
                sb.Append(tag);
                foreach (string f in fields)
                {
                    string v = f ?? "";
                    sb.Append(' ').Append(I(v.Length)).Append(':').Append(v);
                }
                sb.Append('\n');
            }
            EngInfo i = p.Info;
            sb.Append(Version).Append('\n');
            Row("INFO", i.Kind.HasValue ? I((int)i.Kind.Value) : "", i.PlatformCode, i.Standard, I(i.PackageVersion), i.ContractCode, i.ProjectCode, i.Title, I(i.SchemaVersion));
            foreach (EngBomRow b in p.Bom)
                Row("BOM", I(b.Level), b.ParentCode, b.PartCode, b.Description, EngText.Num(b.Qty), b.Unit, I((int)b.MakeBuy), b.Material, b.DrawingNumber, b.DrawingRevision, b.Note);
            foreach (EngOpcRow o in p.Opc)
                Row("OPC", o.PartCode, I(o.OperationNumber), o.Description, o.WorkCenterCode, EngText.Num(o.SetupMinutes), EngText.Num(o.RunMinutes), o.IsInspection ? "1" : "0", o.IsSpecialProcess ? "1" : "0",
                    string.Join(",", o.Predecessors.OrderBy(x => x).Select(I)), o.StageCode);
            foreach (EngDocRow d in p.Docs)
                Row("DOC", d.Number, d.Title, d.Revision, d.DocType, d.FileName, d.Required ? "1" : "0");
            foreach (EngMdlRow m in p.Mdl)
                Row("MDL", m.Number, m.Title, m.DocType, m.CommitDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (files != null)
                foreach (EngFileInfo f in p.Docs.Where(d => d.FileName != null && d.FileSha256 != null).Select(d => new EngFileInfo { Name = d.FileName, Size = d.FileSize ?? 0, Sha256 = d.FileSha256 })
                             .GroupBy(f => f.Name, StringComparer.Ordinal).Select(g => g.First()).OrderBy(f => f.Name, StringComparer.Ordinal))
                    Row("FILE", f.Name, f.Size.ToString(CultureInfo.InvariantCulture), f.Sha256);
            return EngText.Sha256Hex(sb.ToString());
        }
    }
}
