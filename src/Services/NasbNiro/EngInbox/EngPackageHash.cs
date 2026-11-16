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
        public const string Version = "ENGPKG1";

        public static string Of(EngParsedPackage p, IReadOnlyDictionary<string, EngFileInfo> files)
        {
            var sb = new StringBuilder();
            string I(int n) => n.ToString(CultureInfo.InvariantCulture);
            EngInfo i = p.Info;
            sb.Append(Version).Append('\n');
            sb.Append("INFO|").Append(i.Kind.HasValue ? I((int)i.Kind.Value) : "").Append('|').Append(i.PlatformCode).Append('|').Append(i.Standard).Append('|').Append(I(i.PackageVersion)).Append('|')
              .Append(i.ContractCode).Append('|').Append(i.ProjectCode).Append('|').Append(i.Title).Append('|').Append(I(i.SchemaVersion)).Append('\n');
            foreach (EngBomRow b in p.Bom)
                sb.Append("BOM|").Append(I(b.Level)).Append('|').Append(b.ParentCode).Append('|').Append(b.PartCode).Append('|').Append(b.Description).Append('|').Append(EngText.Num(b.Qty)).Append('|').Append(b.Unit).Append('|')
                  .Append(I((int)b.MakeBuy)).Append('|').Append(b.Material).Append('|').Append(b.DrawingNumber).Append('|').Append(b.DrawingRevision).Append('|').Append(b.Note).Append('\n');
            foreach (EngOpcRow o in p.Opc)
                sb.Append("OPC|").Append(o.PartCode).Append('|').Append(I(o.OperationNumber)).Append('|').Append(o.Description).Append('|').Append(o.WorkCenterCode).Append('|').Append(EngText.Num(o.SetupMinutes)).Append('|').Append(EngText.Num(o.RunMinutes)).Append('|')
                  .Append(o.IsInspection ? "1" : "0").Append('|').Append(o.IsSpecialProcess ? "1" : "0").Append('|').Append(string.Join(",", o.Predecessors.OrderBy(x => x).Select(I))).Append('|').Append(o.StageCode).Append('\n');
            foreach (EngDocRow d in p.Docs)
                sb.Append("DOC|").Append(d.Number).Append('|').Append(d.Title).Append('|').Append(d.Revision).Append('|').Append(d.DocType).Append('|').Append(d.FileName).Append('|').Append(d.Required ? "1" : "0").Append('\n');
            foreach (EngMdlRow m in p.Mdl)
                sb.Append("MDL|").Append(m.Number).Append('|').Append(m.Title).Append('|').Append(m.DocType).Append('|').Append(m.CommitDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
            if (files != null)
                foreach (EngFileInfo f in p.Docs.Where(d => d.FileName != null && d.FileSha256 != null).Select(d => new EngFileInfo { Name = d.FileName, Size = d.FileSize ?? 0, Sha256 = d.FileSha256 })
                             .GroupBy(f => f.Name, StringComparer.Ordinal).Select(g => g.First()).OrderBy(f => f.Name, StringComparer.Ordinal))
                    sb.Append("FILE|").Append(f.Name).Append('|').Append(f.Size.ToString(CultureInfo.InvariantCulture)).Append('|').Append(f.Sha256).Append('\n');
            return EngText.Sha256Hex(sb.ToString());
        }
    }
}
