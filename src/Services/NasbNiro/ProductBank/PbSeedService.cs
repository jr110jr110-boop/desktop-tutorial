using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.ProductBankDtos;

namespace Services.NasbNiro.ProductBank
{
    public interface IPbSeedService
    {
        Task<ResponseDto<PbSeedResultDto>> InstallFyAsync(CancellationToken ct);
    }

    /// <summary>
    /// Installs the company's own FY product from its documents: the product, BOM revision 00 (released: it was signed on paper), the seven OPC routings (approved on paper),
    /// the WBS template (through the MSPDI importer) and a few legacy warehouse items with suggested mapping drafts. Run once by the PMO; running it again changes nothing.
    /// The signatures of the paper documents are roles, not people: no signature column is filled and the note names the paper document.
    /// </summary>
    public class PbSeedService : PbServiceBase, IPbSeedService
    {
        public const string FyCode = "16.11.00.000.2";
        public const string PaperNote = "نسخهٔ ۰۰ با امضای کاغذی (تهیه، کنترل، تایید، کنترل کیفیت و کنترل ساخت) در شرکت تایید شده و به‌عنوان نسخهٔ پایه وارد شد.";

        private readonly IPbBomService _bom;
        private readonly IPbRoutingService _routing;
        private readonly IPbProductService _products;
        private readonly IPbCodingService _coding;

        public PbSeedService(IUnitOfWork uow, IInventoryUserContext user, IPbBomService bom, IPbRoutingService routing, IPbProductService products, IPbCodingService coding) : base(uow, user)
        {
            _bom = bom; _routing = routing; _products = products; _coding = coding;
        }

        public async Task<ResponseDto<PbSeedResultDto>> InstallFyAsync(CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbSeedResultDto>(PbMessages.AiNeverApproves);
            if (!InRole(PbRoles.Admin)) return ResponseDtoHelper.BadRequest<PbSeedResultDto>(PbMessages.NotAllowed);
            // the demonstration data goes only into a demo or test installation: the setting is off in a production install
            if ((await PbSettingKeys.LoadAsync(Uow, ct))[PbSettingKeys.DemoData] != "true")
                return ResponseDtoHelper.BadRequest<PbSeedResultDto>("نصب داده‌های نمونه در این نصب مجاز نیست؛ فقط در نصب آزمایشی یا نمایشی تنظیم «DemoData:Enabled» را روشن کنید.");
            // everything or nothing: a failure in the middle leaves no half-installed product
            return await InTransactionAsync(() => InstallCoreAsync(ct), r => r.Success, ct);
        }

        private async Task<ResponseDto<PbSeedResultDto>> InstallCoreAsync(CancellationToken ct)
        {
            var result = new PbSeedResultDto();
            PbProduct existing = await Uow.Set<PbProduct>().AsNoTracking().FirstOrDefaultAsync(p => !p.Deleted && p.Code == FyCode, ct);
            if (existing != null)
            {
                result.AlreadyInstalled = true; result.ProductId = existing.Id;
                result.BomId = await Uow.Set<PbBomHeader>().Where(h => h.ProductId == existing.Id).Select(h => h.Id).FirstOrDefaultAsync(ct);
                return ResponseDtoHelper.Ok(result);
            }

            result.Stages = (await LoadStagesAsync(false, ct)).Count;

            var product = new PbProduct { Code = FyCode, Title = "توربین بخار FY", Platform = "FY", Description = "محصول پایه شرکت؛ BOM، چارت‌های OPC و WBS از اسناد خود شرکت." };
            Stamp(product, true);
            Uow.Set<PbProduct>().Add(product);
            await Uow.SaveChangesAsync(ct);
            result.ProductId = product.Id;

            // ---- BOM revision 00
            var lines = ParseBom(PbSeedData.Bom);
            var saved = await _bom.SaveAsync(new SavePbBomDto
            {
                ProductId = product.Id, AssemblyPartNumber = FyCode, Title = "BOM توربین FY", Platform = "FY", IssueFor = "ساخت", DocumentDate = new DateTime(2026, 10, 4), Lines = lines,
            }, ct);
            if (!saved.Success) return ResponseDtoHelper.BadRequest<PbSeedResultDto>("BOM: " + saved.Message);
            var submitted = await _bom.SubmitAsync(new PbBomActionDto { Id = saved.Data.Id }, ct);
            if (!submitted.Success) return ResponseDtoHelper.BadRequest<PbSeedResultDto>("BOM: " + submitted.Message);
            PbBomHeader h = await Uow.Set<PbBomHeader>().FirstAsync(x => x.Id == saved.Data.Id, ct);
            h.Status = PbBomStatus.Released; h.ImportedSigned = true; h.PaperSignatureNote = PaperNote;
            h.ReleasedByUserId = User.UserId; h.ReleasedAt = DateTimeOffset.Now; h.EffectiveFrom = h.DocumentDate;
            Stamp(h, false);
            await Uow.SaveChangesAsync(ct);
            result.BomId = h.Id; result.BomLines = lines.Count(l => !l.IsHeading);

            // ---- OPC routings
            foreach (SavePbOpcDto dto in ParseOpc(product.Id))
            {
                var r = await _routing.SaveAsync(dto, ct);
                if (!r.Success) return ResponseDtoHelper.BadRequest<PbSeedResultDto>("OPC " + dto.PartNumber + ": " + r.Message);
                var s = await _routing.SubmitAsync(new PbOpcActionDto { Id = r.Data.Id }, ct);
                if (!s.Success) return ResponseDtoHelper.BadRequest<PbSeedResultDto>("OPC " + dto.PartNumber + ": " + s.Message);
                PbOpcRouting row = await Uow.Set<PbOpcRouting>().FirstAsync(x => x.Id == r.Data.Id, ct);
                row.Status = PbRoutingStatus.Approved; row.ImportedSigned = true; Stamp(row, false);
                await Uow.SaveChangesAsync(ct);
                result.Routings++;
            }

            // ---- WBS template through the MSPDI importer; it was never signed on paper, so it stays a Draft for the ordinary ENGINEERING then PLANNING approval
            var tpl = await _products.ImportMspdiAsync(new PbImportMspdiDto { ProductId = product.Id, Title = "قالب WBS توربین FY", Xml = BuildMspdi() }, ct);
            if (!tpl.Success) return ResponseDtoHelper.BadRequest<PbSeedResultDto>("WBS: " + tpl.Message);
            result.TemplateNodes = tpl.Data.NodeCount;

            // ---- legacy warehouse items (demo data, marked as such) and the suggested mappings
            foreach (string line in PbSeedData.Legacy.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = line.TrimEnd('\r').Split('|');
                if (f.Length < 3) continue;
                string code = f[0].Trim();
                if (await Uow.Set<InventoryItem>().AnyAsync(i => i.Code == code, ct)) continue;
                var item = new InventoryItem
                {
                    Code = code, LegacyCode = code, Title = f[1].Trim(), Material = f[2].Trim() == "-" ? null : f[2].Trim(), Unit = "عدد", GroupCode = code.Substring(0, 2), IsActive = true,
                    TrackingMode = ItemTrackingMode.None, Specification = "★ داده نمونه بانک محصول (نصب آزمایشی)",
                };
                Stamp(item, true);
                Uow.Set<InventoryItem>().Add(item);
                result.Items++;
            }
            await Uow.SaveChangesAsync(ct);
            var sug = await _coding.SuggestMappingsAsync(ct);
            if (sug.Success) result.Mappings = sug.Data.Created;
            result.Items += await Uow.Set<InventoryItem>().CountAsync(i => i.PartNumber != null && !i.Deleted, ct);
            return ResponseDtoHelper.Ok(result);
        }

        // -------------------------------------------------------------------------------- parsers

        private static decimal? Dec(string s) => decimal.TryParse((s ?? "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v) ? v : (decimal?)null;

        internal static List<SavePbBomLineDto> ParseBom(string text)
        {
            var list = new List<SavePbBomLineDto>();
            var headingAt = new Dictionary<int, int>(); // level -> index of the latest heading of that level
            int lastLine = -1;
            foreach (string raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = raw.TrimEnd('\r').Split('|');
                if (f[0] == "H")
                {
                    int level = int.Parse(f[1], CultureInfo.InvariantCulture);
                    int? parent = level > 0 && headingAt.TryGetValue(level - 1, out int p) ? p : (int?)null;
                    list.Add(new SavePbBomLineDto { IsHeading = true, PartName = f[2], ParentIndex = parent });
                    headingAt[level] = list.Count - 1;
                    foreach (int k in headingAt.Keys.Where(k => k > level).ToList()) headingAt.Remove(k);
                }
                else if (f[0] == "L")
                {
                    int deepest = headingAt.Count == 0 ? -1 : headingAt.Keys.Max();
                    int? itemNo = int.TryParse(f[1], out int n) ? n : (int?)null;
                    // the paper BOM lists the clockwise and the counter-clockwise part under one item number ("one of them is chosen by the direction of the turbine"): the second is an alternative of the first
                    int? alternativeOf = null;
                    string desc = f.Length > 14 ? f[14] : null;
                    if (itemNo.HasValue && desc != null && desc.Contains("یکی انتخاب شود") && lastLine >= 0 && list[lastLine].ItemNumber == itemNo && !list[lastLine].AlternativeOfIndex.HasValue) alternativeOf = lastLine;
                    list.Add(new SavePbBomLineDto
                    {
                        AlternativeOfIndex = alternativeOf,
                        ItemNumber = itemNo, PartName = f[2], PartNumber = f[3], Qty = Dec(f[4]) ?? 1, Unit = "عدد", Standard = f[5], Material = f[6],
                        RawDiameter = Dec(f[7]), RawLength = Dec(f[8]), RawWidth = Dec(f[9]), RawThickness = Dec(f[10]), NetWeight = Dec(f[11]), GrossWeight = Dec(f[12]),
                        MakeOrBuy = f[13].Trim() == "MPL" ? PbMakeOrBuy.Make : PbMakeOrBuy.Purchase, Description = f.Length > 14 ? f[14] : null,
                        ParentIndex = deepest >= 0 ? headingAt[deepest] : (int?)null,
                    });
                    lastLine = list.Count - 1;
                }
            }
            return list;
        }

        private static string ResourceOf(string stage)
        {
            switch (stage)
            {
                case "LA": case "RL": case "SL": case "FL": return "LATHE";
                case "ML": case "RG": case "DR": case "MC": case "FM": case "JM": case "SD": case "GR": case "FG": case "WC": return "CNC_MILLING";
                case "AS": case "SA": case "HY": case "BL": return "ASSEMBLY";
                case "QC": case "NT": case "TH": case "RT": return "QC";
                default: return null;
            }
        }

        /// <summary>Construction (C) days of every part in the MS Project plan, by the part's name: the only time the company's documents give for the shop work.</summary>
        internal static Dictionary<string, decimal> ConstructionDays()
        {
            var days = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            string part = null;
            foreach (string raw in PbSeedData.Wbs.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = raw.TrimEnd('\r').Split('|');
                int level = int.Parse(f[0], CultureInfo.InvariantCulture);
                if (level == 3) part = f[1].Trim();
                else if (level == 4 && f[1].Trim() == "C" && part != null && !days.ContainsKey(part)) days[part] = decimal.Parse(f[2], CultureInfo.InvariantCulture);
            }
            return days;
        }

        /// <summary>★ standard hours of the shop operations: the construction days of the part in the plan x 8 hours / the number of operations; a part the plan does not name gets a flagged placeholder.</summary>
        private static void FillStandardHours(SavePbOpcDto routing)
        {
            Dictionary<string, decimal> days = ConstructionDays();
            Dictionary<string, PbStageKind> kinds = PbStageDefaults.All().ToDictionary(s => s.Code, s => s.Kind);
            foreach (var column in routing.Steps.GroupBy(s => s.PartNumber))
            {
                var ops = column.Where(s => s.StageCode2 != "PO" && kinds.TryGetValue(s.StageCode2, out PbStageKind k) && k == PbStageKind.Operation).ToList();
                if (ops.Count == 0) continue;
                string name = column.First().PartName;
                bool known = name != null && days.TryGetValue(name.Trim(), out decimal c) && c > 0;
                decimal hours = known ? Math.Max(0.25m, Math.Round(days[name.Trim()] * 8m / ops.Count, 2)) : 1m;
                foreach (SavePbOpcStepDto s in ops)
                {
                    s.StandardHours = hours;
                    s.Note = known ? "★ تقریبی از برنامهٔ MSP: مدت ساخت ÷ تعداد عملیات؛ زمان واقعی را مهندسی تعیین کند" : "★ جایگزین موقت؛ این قطعه در برنامهٔ MSP نیست و زمان استاندارد را مهندسی تعیین کند";
                }
            }
        }

        internal static List<SavePbOpcDto> ParseOpc(int productId)
        {
            var result = new List<SavePbOpcDto>();
            SavePbOpcDto cur = null;
            string part = null, partName = null;
            var seq = new Dictionary<string, int>();
            foreach (string raw in PbSeedData.Opc.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = raw.TrimEnd('\r').Split('|');
                if (f[0] == "R")
                {
                    cur = new SavePbOpcDto { ProductId = productId, PartNumber = f[1], Title = f[2], ProjectNumber = f[3], Platform = "FY", DocumentDate = new DateTime(2026, 10, 5) };
                    result.Add(cur);
                    seq.Clear();
                }
                else if (f[0] == "P") { part = f[1]; partName = f[2]; }
                else if (f[0] == "S" && cur != null)
                {
                    foreach (string token in f[1].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] tp = token.Split(':');
                        string stage = tp[0];
                        seq[part] = seq.TryGetValue(part, out int s) ? s + 1 : 1;
                        cur.Steps.Add(new SavePbOpcStepDto
                        {
                            Seq = seq[part], PartNumber = part, PartName = partName, StageCode2 = stage, ResourceClass = ResourceOf(stage),
                            RequiredEvidence = tp.Length > 1 ? tp[1].Replace('+', ',') : null,
                            IsQcPoint = stage == "QC" || stage == "NT" || stage == "TH" || stage == "RT",
                        });
                    }
                }
            }
            foreach (SavePbOpcDto routing in result) FillStandardHours(routing);
            return result;
        }

        /// <summary>The work breakdown of the plan as an MSPDI file (480-minute days, E to P to C inside every part), so the seed goes through the same importer as a real file.</summary>
        internal static string BuildMspdi()
        {
            XNamespace ns = "http://schemas.microsoft.com/project";
            var rows = PbSeedData.Wbs.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => r.TrimEnd('\r').Split('|')).ToList();
            var tasks = new XElement(ns + "Tasks");
            tasks.Add(new XElement(ns + "Task", new XElement(ns + "UID", 0), new XElement(ns + "ID", 0), new XElement(ns + "Name", "FY"), new XElement(ns + "OutlineLevel", 0)));
            DateTime baseDay = new DateTime(2000, 1, 3, 8, 0, 0);
            var lastAtLevel = new Dictionary<int, int>();
            var lastPhase = new Dictionary<string, int>(); // parent uid + phase -> uid
            var parentStack = new Dictionary<int, int>();
            for (int i = 0; i < rows.Count; i++)
            {
                string[] f = rows[i];
                int level = int.Parse(f[0], CultureInfo.InvariantCulture) + 1;
                decimal dur = decimal.Parse(f[2], CultureInfo.InvariantCulture);
                decimal off = decimal.Parse(f[3], CultureInfo.InvariantCulture);
                int uid = i + 1;
                bool summary = i + 1 < rows.Count && int.Parse(rows[i + 1][0], CultureInfo.InvariantCulture) + 1 > level;
                parentStack[level] = uid;
                int parentUid = level > 1 ? parentStack[level - 1] : 0;
                var t = new XElement(ns + "Task",
                    new XElement(ns + "UID", uid), new XElement(ns + "ID", uid), new XElement(ns + "Name", f[1]), new XElement(ns + "OutlineLevel", level),
                    new XElement(ns + "Summary", summary ? 1 : 0), new XElement(ns + "Milestone", dur == 0 && !summary ? 1 : 0),
                    new XElement(ns + "Start", baseDay.AddDays((double)off).ToString("s", CultureInfo.InvariantCulture)),
                    new XElement(ns + "Duration", "PT" + ((int)Math.Round(dur * 480m) / 60).ToString(CultureInfo.InvariantCulture) + "H" + ((int)Math.Round(dur * 480m) % 60).ToString(CultureInfo.InvariantCulture) + "M0S"));
                string phase = PbMspdi.PhaseOf(f[1]);
                if (phase == "P" && lastPhase.TryGetValue(parentUid + ":E", out int e)) t.Add(Link(ns, e));
                if (phase == "C")
                {
                    if (lastPhase.TryGetValue(parentUid + ":P", out int p)) t.Add(Link(ns, p));
                    else if (lastPhase.TryGetValue(parentUid + ":E", out int e2)) t.Add(Link(ns, e2));
                }
                if (phase != null) lastPhase[parentUid + ":" + phase] = uid;
                tasks.Add(t);
            }
            var doc = new XDocument(new XElement(ns + "Project", new XElement(ns + "Name", "FY Platform"), new XElement(ns + "MinutesPerDay", 480), tasks));
            return doc.ToString(SaveOptions.DisableFormatting);
        }

        private static XElement Link(XNamespace ns, int pred) =>
            new XElement(ns + "PredecessorLink", new XElement(ns + "PredecessorUID", pred), new XElement(ns + "Type", 1), new XElement(ns + "LinkLag", 0));
    }
}
