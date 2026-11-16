using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.EngInbox;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.ProductBankDtos;

namespace Services.NasbNiro.EngInbox
{
    public enum EngApplyOutcome { Applied = 1, Blocked = 2, NotReady = 3 }

    public sealed class EngApplyResult
    {
        public EngApplyOutcome Outcome { get; set; }
        public string Message { get; set; }
    }

    public interface IEngInboxApplyService
    {
        /// <summary>The agent step: continue one ingested package into the product bank (platform) or link it to its contract and project (project). Only the engineering agent runs it.</summary>
        Task<ResponseDto<EngApplyResult>> ApplyAsync(int packageId, CancellationToken ct);
    }

    /// <summary>
    /// ENG.INBOX.APPLY. The agent is the MAKER: it creates the product (when new), one BOM draft per assembly of the package and one OPC routing draft for the root, THROUGH THE SERVICES OF THE PRODUCT BANK ONLY (nothing is written to a
    /// Pb table here), and sends them for review: the four signatures, the release, the check and the approval of the routing and its publication stay with people (the services refuse an AI seat there). The services let the agent make
    /// and send its own drafts only while this step runs (AgentMaker). Every gate of the bank stays: a BOM that is released, approved or under review with other content, and a routing that is approved, needs a person and an approved
    /// ECO; the agent never makes a second revision, never edits what a person has taken over and never bypasses a refusal. A refusal or a missing master (work centre) is a blocked package with the exact Persian reason.
    /// Key of the product: the part code of the root assembly of the BOM (PbProduct.Code); the platform code goes to PbProduct.Platform and the standard to its description.
    /// </summary>
    public class EngInboxApplyService : IEngInboxApplyService
    {
        private const string Tag = "صندوق ورودی مهندسی";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IPbProductService _products;
        private readonly IPbBomService _boms;
        private readonly IPbRoutingService _routings;

        public EngInboxApplyService(IUnitOfWork uow, IInventoryUserContext user, IPbProductService products, IPbBomService boms, IPbRoutingService routings)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _products = products ?? throw new ArgumentNullException(nameof(products));
            _boms = boms ?? throw new ArgumentNullException(nameof(boms));
            _routings = routings ?? throw new ArgumentNullException(nameof(routings));
        }

        private sealed class Blocked : Exception
        {
            public Blocked(string reason) : base(reason) { }
        }

        public async Task<ResponseDto<EngApplyResult>> ApplyAsync(int packageId, CancellationToken ct)
        {
            if (!AgentMaker.Allows(EngInboxCodes.Apply)) return ResponseDtoHelper.BadRequest<EngApplyResult>(EngInboxText.NotAllowed);
            EngInboxPackage head = await _uow.Set<EngInboxPackage>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == packageId && !x.Deleted, ct);
            if (head == null) return ResponseDtoHelper.NotFound<EngApplyResult>(EngInboxText.NotFound);
            if (head.Status != EngInboxStatus.Ingested && head.Status != EngInboxStatus.ApplyBlocked)
                return ResponseDtoHelper.Ok(new EngApplyResult { Outcome = EngApplyOutcome.NotReady, Message = "وضعیت بسته دیگر منتظر ایجنت نیست." });
            try
            {
                // the whole apply of one package is ONE transaction (A3): a block or a refusal anywhere rolls back every product, BOM, routing and item row of this run
                string done = await AtomicAsync(async () =>
                {
                    EngInboxPackage pkg = await _uow.Set<EngInboxPackage>().FirstAsync(x => x.Id == packageId, ct);
                    pkg.ApplyAttempts++;
                    pkg.LastApplyAt = DateTimeOffset.Now;
                    string text = pkg.Kind == EngInboxKind.Project ? await LinkProjectAsync(pkg, ct) : await ApplyPlatformAsync(pkg, ct);
                    pkg.Status = EngInboxStatus.Applied; pkg.AppliedAt = DateTimeOffset.Now; pkg.StatusAt = DateTimeOffset.Now; pkg.ApplyBlockReason = null; pkg.StatusNote = AgentText.Cut(text, 500);
                    Stamp(pkg);
                    await _uow.SaveChangesAsync(ct);
                    return text;
                }, ct);
                return ResponseDtoHelper.Ok(new EngApplyResult { Outcome = EngApplyOutcome.Applied, Message = done });
            }
            catch (Blocked b)
            {
                AgentDb.Clear(_uow);
                return await BlockAsync(packageId, b.Message, ct);
            }
        }

        /// <summary>Runs the work in one database transaction (SQL Server, with the retrying execution strategy). The database of the tests has no transactions, so the work simply runs; a block is then caught BEFORE anything is written (every decision is made first).</summary>
        private async Task<T> AtomicAsync<T>(Func<Task<T>> work, CancellationToken ct)
        {
            if (!(_uow is DbContext db) || !db.Database.IsRelational()) return await work();
            IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
            int attempt = 0;
            return await strategy.ExecuteAsync<T>(async token =>
            {
                if (attempt++ > 0) db.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(token);
                T result = await work();
                await transaction.CommitAsync(token);
                return result;
            }, ct);
        }

        private void Stamp(EngInboxPackage p)
        {
            p.ModifyDate = DateTimeOffset.Now;
            p.ModifierUserId = _user.UserId;
        }

        /// <summary>Re-reads the package after a refusal (the failed run was rolled back and the tracker cleared) and keeps the reason in a separate save.</summary>
        private async Task<ResponseDto<EngApplyResult>> BlockAsync(int packageId, string reason, CancellationToken ct)
        {
            EngInboxPackage pkg = await _uow.Set<EngInboxPackage>().FirstAsync(x => x.Id == packageId, ct);
            pkg.ApplyAttempts++;
            pkg.LastApplyAt = DateTimeOffset.Now;
            bool changed = pkg.Status != EngInboxStatus.ApplyBlocked || pkg.ApplyBlockReason != AgentText.Cut(reason, 1500);
            pkg.Status = EngInboxStatus.ApplyBlocked;
            pkg.ApplyBlockReason = AgentText.Cut(reason, 1500);
            pkg.StatusNote = "متوقف شد: " + AgentText.Cut(reason, 300);
            if (changed) pkg.StatusAt = DateTimeOffset.Now;
            Stamp(pkg);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new EngApplyResult { Outcome = EngApplyOutcome.Blocked, Message = reason });
        }

        // ------------------------------------------------------------------------------------------------ project packages

        private async Task<string> LinkProjectAsync(EngInboxPackage pkg, CancellationToken ct)
        {
            string contractCode = pkg.ContractCode, projectCode = pkg.ProjectCode;
            ContractEntity contract = await _uow.Set<ContractEntity>().AsNoTracking().Where(c => !c.Deleted && (c.Code == contractCode || c.InternalContractNo == contractCode)).OrderBy(c => c.Id).FirstOrDefaultAsync(ct);
            if (contract == null) throw new Blocked("قراردادی با کد «" + contractCode + "» در سیستم نیست؛ کد قرارداد را در بسته اصلاح کنید یا قرارداد را ثبت کنید.");
            int? workOrderId = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => !w.Deleted && w.ContractId == contract.Id && w.Code == projectCode).OrderBy(w => w.Id).Select(w => (int?)w.Id).FirstOrDefaultAsync(ct);
            bool projectByNumber = contract.InternalProjectNo == projectCode || contract.EmployerProjectNo == projectCode;
            if (workOrderId == null && !projectByNumber) throw new Blocked("پروژه‌ای با کد «" + projectCode + "» در قرارداد «" + contractCode + "» نیست (نه دستور کار و نه شمارهٔ پروژهٔ قرارداد)؛ کد پروژه را اصلاح کنید.");
            pkg.LinkedContractId = contract.Id;
            pkg.LinkedWorkOrderId = workOrderId;
            return "بستهٔ پروژه به قرارداد " + contractCode + " و پروژه " + projectCode + " وصل شد؛ فهرست مدارک (MDL) و مدارک مرجع در صندوق ورودی نگه داشته می‌شود.";
        }

        // ------------------------------------------------------------------------------------------------ platform packages

        private sealed class BomPlan
        {
            public string Assembly, Code, Title;
            public List<SavePbBomLineDto> Lines;
            public int? EditId, ReuseId;
            public string ReuseNote;
        }

        private sealed class RoutingPlan
        {
            public string Part;
            public List<SavePbOpcStepDto> Steps;
            public int? EditId, ReuseId;
            public string ReuseNote;
        }

        private async Task<string> ApplyPlatformAsync(EngInboxPackage pkg, CancellationToken ct)
        {
            List<EngInboxBomLine> bom = await _uow.Set<EngInboxBomLine>().AsNoTracking().Where(l => l.PackageId == pkg.Id && !l.Deleted).OrderBy(l => l.SortOrder).ToListAsync(ct);
            List<EngInboxOpcLine> opc = await _uow.Set<EngInboxOpcLine>().AsNoTracking().Where(l => l.PackageId == pkg.Id && !l.Deleted).OrderBy(l => l.SortOrder).ToListAsync(ct);
            EngInboxBomLine root = bom.FirstOrDefault(l => string.IsNullOrEmpty(l.ParentCode));
            if (root == null) throw new Blocked("BOM بسته ریشه ندارد.");

            // ---- PHASE 1: decide everything first (nothing is written): the work centres, the state of every BOM and routing the package meets
            string[] wcCodes = opc.Select(o => o.WorkCenterCode).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Dictionary<string, int> wcIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (wcCodes.Length > 0)
                foreach (var w in await _uow.Set<MfWorkCenter>().AsNoTracking().Where(w => !w.Deleted && wcCodes.Contains(w.Code)).Select(w => new { w.Id, w.Code }).ToListAsync(ct)) wcIds[w.Code] = w.Id;
            string[] missing = wcCodes.Where(c => !wcIds.ContainsKey(c)).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missing.Length > 0) throw new Blocked("مرکز کار با این کدها در سیستم تعریف نشده است: " + string.Join("، ", missing) + ". آن‌ها را در بخش تولید (مراکز کار) تعریف کنید یا بستهٔ اصلاح‌شده بدهید؛ ایجنت خودش دوباره تلاش می‌کند.");

            var children = bom.Where(l => !string.IsNullOrEmpty(l.ParentCode)).GroupBy(l => l.ParentCode, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var assemblies = new List<string>();
            var queue = new Queue<string>();
            queue.Enqueue(root.PartCode);
            while (queue.Count > 0)
            {
                string a = queue.Dequeue();
                if (assemblies.Contains(a) || !children.ContainsKey(a)) continue;
                assemblies.Add(a);
                foreach (EngInboxBomLine c in children[a]) queue.Enqueue(c.PartCode);
            }
            var bomPlans = new List<BomPlan>();
            foreach (string a in assemblies)
            {
                EngInboxBomLine self = bom.First(l => l.PartCode == a);
                var lines = children[a].Select((l, i) => new SavePbBomLineDto
                {
                    ItemNumber = i + 1, PartNumber = l.PartCode, PartName = l.Description, Qty = l.Qty, Unit = l.Unit, Material = l.Material, MakeOrBuy = (PbMakeOrBuy)l.MakeBuy,
                    Description = LineNote(l),
                }).ToList();
                bomPlans.Add(await PlanBomAsync(a, string.Equals(a, root.PartCode, StringComparison.Ordinal) ? (pkg.Title ?? self.Description) : self.Description, lines, ct));
            }

            RoutingPlan routingPlan = null;
            if (opc.Count > 0)
            {
                var hasChildren = new HashSet<string>(children.Keys, StringComparer.Ordinal);
                var partInfo = bom.GroupBy(l => l.PartCode, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                var steps = opc.Select(o =>
                {
                    partInfo.TryGetValue(o.PartCode, out EngInboxBomLine part);
                    string stage = !string.IsNullOrEmpty(o.StageCode) ? o.StageCode : o.IsInspection ? "QC" : hasChildren.Contains(o.PartCode) ? "AS" : "MC";
                    // the note is the same for the same content in every version of the package (it carries no package number)
                    string note = "آماده‌سازی " + EngText.Num(o.SetupMinutes) + " دقیقه؛ اجرا " + EngText.Num(o.RunMinutes) + " دقیقه" + (o.IsSpecialProcess ? "؛ فرایند ویژه" : "")
                        + (string.IsNullOrEmpty(o.Predecessors) ? "" : "؛ پیش‌نیاز: " + o.Predecessors) + "؛ منبع: " + Tag;
                    return new SavePbOpcStepDto
                    {
                        Seq = o.OperationNumber, PartNumber = o.PartCode, PartName = part?.Description, StageCode2 = stage, Title = o.Description, WorkCenterId = wcIds[o.WorkCenterCode],
                        StandardHours = Math.Round((o.SetupMinutes + o.RunMinutes) / 60m, 4), IsQcPoint = o.IsInspection, MakeOrBuy = part == null ? (PbMakeOrBuy?)null : (PbMakeOrBuy)part.MakeBuy, Note = note,
                    };
                }).ToList();
                routingPlan = await PlanRoutingAsync(root, pkg, steps, ct);
            }

            // ---- PHASE 2: write (inside the transaction of the caller)
            int productId = await EnsureProductAsync(pkg, root, ct);
            int rootBomId = 0;
            var notes = new List<string>();
            foreach (BomPlan plan in bomPlans)
            {
                (int id, string note) = await ExecuteBomAsync(pkg, productId, plan, ct);
                if (plan.Assembly == root.PartCode) rootBomId = id;
                notes.Add(note);
            }
            int? routingId = null;
            if (routingPlan != null)
            {
                (int rid, string rnote) = await ExecuteRoutingAsync(pkg, productId, root, routingPlan, ct);
                routingId = rid;
                notes.Add(rnote);
            }
            pkg.ProductId = productId;
            pkg.BomHeaderId = rootBomId;
            pkg.RoutingId = routingId;
            await _uow.SaveChangesAsync(ct);
            return "به بانک محصول رفت: " + string.Join("؛ ", notes) + ".";
        }

        private static string LineNote(EngInboxBomLine l)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(l.DrawingNumber)) parts.Add("نقشه " + l.DrawingNumber + (string.IsNullOrEmpty(l.DrawingRevision) ? "" : " بازنگری " + l.DrawingRevision));
            if (!string.IsNullOrEmpty(l.Note)) parts.Add(l.Note);
            return parts.Count == 0 ? null : string.Join("؛ ", parts);
        }

        private async Task RecordAppliedAsync(int packageId, byte kind, int entityId, CancellationToken ct)
        {
            if (await _uow.Set<EngInboxAppliedItem>().AnyAsync(a => a.PackageId == packageId && a.Kind == kind && a.EntityId == entityId && !a.Deleted, ct)) return;
            var row = new EngInboxAppliedItem { PackageId = packageId, Kind = kind, EntityId = entityId };
            DateTimeOffset now = DateTimeOffset.Now;
            row.CreateDate = now; row.CreatorUserId = _user.UserId; row.ModifyDate = now; row.ModifierUserId = _user.UserId;
            _uow.Set<EngInboxAppliedItem>().Add(row);
            await _uow.SaveChangesAsync(ct);
        }

        private async Task<int> EnsureProductAsync(EngInboxPackage pkg, EngInboxBomLine root, CancellationToken ct)
        {
            string code = root.PartCode;
            int existing = await _uow.Set<PbProduct>().AsNoTracking().Where(p => !p.Deleted && p.Code == code).Select(p => p.Id).FirstOrDefaultAsync(ct);
            if (existing > 0) return existing;
            ResponseDto<PbProductDto> r = await _products.SaveProductAsync(new SavePbProductDto
            {
                Code = code, Title = AgentText.Cut(pkg.Title ?? root.Description, 300), Platform = pkg.PlatformCode,
                Description = "استاندارد: " + pkg.Standard + "؛ منبع: " + Tag + " (بستهٔ " + pkg.Id.ToString(CultureInfo.InvariantCulture) + ")", IsActive = true,
            }, ct);
            if (!r.Success) throw new Blocked("ثبت محصول در بانک محصول پذیرفته نشد: " + r.Message);
            await RecordAppliedAsync(pkg.Id, EngInboxAppliedKind.Product, r.Data.Id, ct);
            return r.Data.Id;
        }

        // ---- the comparison with what is stored: EVERY field the apply maps across, as the service would store it (A2)

        private static string Cell(object v) => v switch
        {
            null => "",
            decimal d => EngText.Num(Math.Round(d, 4)),
            bool b => b ? "1" : "0",
            _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
        };

        private static string Row(params object[] cells) => string.Join("\u001f", cells.Select(Cell));

        private static List<string> WouldBeBomLines(List<SavePbBomLineDto> lines) =>
            lines.Select((l, i) => Row(i + 1, l.ItemNumber, PbText.PartNumber(l.PartNumber), PbText.Clean(l.PartName), l.Qty, PbText.Clean(l.Unit) ?? "عدد", PbText.Clean(l.Material), (int)l.MakeOrBuy, PbText.Clean(l.Description))).ToList();

        private async Task<BomPlan> PlanBomAsync(string assembly, string title, List<SavePbBomLineDto> lines, CancellationToken ct)
        {
            string code = assembly + "-BOM";
            var plan = new BomPlan { Assembly = assembly, Code = code, Title = AgentText.Cut(title, 300), Lines = lines };
            PbBomHeader latest = await _uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.Code == code).OrderByDescending(h => h.Revision).ThenByDescending(h => h.Id).FirstOrDefaultAsync(ct);
            if (latest == null) return plan;
            if (latest.Status == PbBomStatus.Draft)
            {
                if (latest.PreparedByUserId != _user.UserId) throw new Blocked("برای " + assembly + " یک پیش‌نویس BOM (بازنگری " + PbText.Revision(latest.Revision) + ") هست که شخص دیگری تهیه کرده؛ ایجنت آن را تغییر نمی‌دهد. آن پیش‌نویس را بفرستید یا حذف کنید، یا این بسته را کنار بگذارید.");
                plan.EditId = latest.Id;
                return plan;
            }
            var stored = await _uow.Set<PbBomLine>().AsNoTracking().Where(l => l.BomHeaderId == latest.Id && !l.Deleted).OrderBy(l => l.SortOrder)
                .Select(l => new { l.SortOrder, l.ItemNumber, l.PartNumber, l.PartName, l.Qty, l.Unit, l.Material, l.MakeOrBuy, l.Description }).ToListAsync(ct);
            List<string> have = stored.Select(l => Row(l.SortOrder, l.ItemNumber, l.PartNumber, l.PartName, l.Qty, l.Unit, l.Material, (int)l.MakeOrBuy, l.Description)).ToList();
            bool same = PbText.Clean(latest.Title) == PbText.Clean(plan.Title) && have.SequenceEqual(WouldBeBomLines(lines), StringComparer.Ordinal);
            if (same)
            {
                plan.ReuseId = latest.Id;
                plan.ReuseNote = "BOM " + code + " بازنگری " + PbText.Revision(latest.Revision) + " همین محتوا را دارد (بدون تغییر)";
                return plan;
            }
            throw new Blocked(latest.Status == PbBomStatus.UnderReview
                ? "BOM " + code + " بازنگری " + PbText.Revision(latest.Revision) + " در حال بررسی (امضا) است و محتوایش با این بسته فرق دارد؛ بازبین آن را «برگشت» دهد تا ایجنت آن را با این بسته به‌روز کند، یا این بسته را کنار بگذارید."
                : "BOM " + code + " بازنگری " + PbText.Revision(latest.Revision) + " قبلاً " + (latest.Status == PbBomStatus.Released ? "منتشر" : latest.Status == PbBomStatus.Approved ? "تایید" : "بسته") + " شده و محتوایش با این بسته فرق دارد. نسخهٔ جدید یک طرح منتشرشده فقط با دستور تغییر مهندسی تاییدشده (ECO) ساخته می‌شود؛ ایجنت آن را دور نمی‌زند. ECO را ثبت و تایید کنید و تغییر را در همان بازنگری وارد کنید، یا این بسته را کنار بگذارید.");
        }

        private async Task<(int Id, string Note)> ExecuteBomAsync(EngInboxPackage pkg, int productId, BomPlan plan, CancellationToken ct)
        {
            if (plan.ReuseId.HasValue) return (plan.ReuseId.Value, plan.ReuseNote);
            ResponseDto<PbBomDto> saved = await _boms.SaveAsync(new SavePbBomDto
            {
                Id = plan.EditId, ProductId = productId, AssemblyPartNumber = plan.Assembly, Title = plan.Title, Platform = pkg.PlatformCode, IssueFor = Tag, Lines = plan.Lines,
            }, ct);
            if (!saved.Success) throw new Blocked("ساخت پیش‌نویس BOM " + plan.Code + " پذیرفته نشد: " + saved.Message);
            await RecordAppliedAsync(pkg.Id, EngInboxAppliedKind.BomHeader, saved.Data.Id, ct);
            ResponseDto<PbBomDto> sent = await _boms.SubmitAsync(new PbBomActionDto { Id = saved.Data.Id }, ct);
            if (!sent.Success) throw new Blocked("پیش‌نویس BOM " + plan.Code + " ساخته شد ولی برای بررسی فرستاده نشد: " + sent.Message);
            return (saved.Data.Id, "BOM " + plan.Code + (plan.EditId.HasValue ? " به‌روز شد" : " ساخته شد") + " و برای امضا فرستاده شد");
        }

        private static PbStageKind KindOfStage(Dictionary<string, PbStage> stages, string code) =>
            stages.TryGetValue(code, out PbStage st) ? st.Kind : PbStageDefaults.All().Where(x => x.Code == code).Select(x => x.Kind).FirstOrDefault();

        private async Task<RoutingPlan> PlanRoutingAsync(EngInboxBomLine root, EngInboxPackage pkg, List<SavePbOpcStepDto> steps, CancellationToken ct)
        {
            string part = root.PartCode;
            var plan = new RoutingPlan { Part = part, Steps = steps };
            PbOpcRouting latest = await _uow.Set<PbOpcRouting>().AsNoTracking().Where(r => !r.Deleted && r.PartNumber == part).OrderByDescending(r => r.Revision).ThenByDescending(r => r.Id).FirstOrDefaultAsync(ct);
            if (latest == null) return plan;
            if (latest.Status == PbRoutingStatus.Draft)
            {
                if (latest.PreparedByUserId != _user.UserId) throw new Blocked("برای " + part + " یک پیش‌نویس چارت OPC (بازنگری " + PbText.Revision(latest.Revision) + ") هست که شخص دیگری تهیه کرده؛ ایجنت آن را تغییر نمی‌دهد.");
                plan.EditId = latest.Id;
                return plan;
            }
            Dictionary<string, PbStage> stages = (await _uow.Set<PbStage>().AsNoTracking().Where(s => !s.Deleted).ToListAsync(ct)).GroupBy(s => s.Code).ToDictionary(g => g.Key, g => g.First());
            // the steps as the routing service would store them (the QC flag is forced for QC and inspection-hold stages)
            List<string> would = steps.Select(s =>
            {
                string stageCode = PbText.Stage(s.StageCode2);
                PbStageKind kind = s.Kind ?? KindOfStage(stages, stageCode);
                bool qc = s.IsQcPoint || kind == PbStageKind.Qc || kind == PbStageKind.InspectionHold;
                string stageTitle = stages.TryGetValue(stageCode ?? "", out PbStage st) ? st.TitleFa : PbStageDefaults.All().Where(x => x.Code == stageCode).Select(x => x.TitleFa).FirstOrDefault();
                return Row(s.Seq, PbText.PartNumber(s.PartNumber) ?? part, PbText.Clean(s.PartName), stageCode, (int)kind, PbText.Clean(s.Title) ?? stageTitle, s.WorkCenterId, s.StandardHours, qc, s.MakeOrBuy.HasValue ? (int)s.MakeOrBuy.Value : (int?)null, PbText.Clean(s.Note));
            }).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var stored = await _uow.Set<PbOpcStep>().AsNoTracking().Where(x => x.RoutingId == latest.Id && !x.Deleted).Select(x => new { x.Seq, x.PartNumber, x.PartName, x.StageCode2, x.Kind, x.Title, x.WorkCenterId, x.StandardHours, x.IsQcPoint, x.MakeOrBuy, x.Note }).ToListAsync(ct);
            List<string> have = stored.Select(x => Row(x.Seq, x.PartNumber, x.PartName, x.StageCode2, (int)x.Kind, x.Title, x.WorkCenterId, x.StandardHours, x.IsQcPoint, x.MakeOrBuy.HasValue ? (int)x.MakeOrBuy.Value : (int?)null, x.Note)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            string wantTitle = PbText.Clean(AgentText.Cut(pkg.Title ?? root.Description, 300));
            bool same = have.SequenceEqual(would, StringComparer.Ordinal) && (PbText.Clean(latest.Title) == null || PbText.Clean(latest.Title) == wantTitle);
            if (same)
            {
                plan.ReuseId = latest.Id;
                plan.ReuseNote = "چارت OPC " + part + " بازنگری " + PbText.Revision(latest.Revision) + " همین مراحل را دارد (بدون تغییر)";
                return plan;
            }
            throw new Blocked(latest.Status == PbRoutingStatus.UnderReview
                ? "چارت OPC " + part + " بازنگری " + PbText.Revision(latest.Revision) + " در حال بررسی است و مراحلش با این بسته فرق دارد؛ بازبین آن را «برگشت» دهد تا ایجنت آن را به‌روز کند، یا این بسته را کنار بگذارید."
                : "چارت OPC " + part + " بازنگری " + PbText.Revision(latest.Revision) + " قبلاً تایید شده و مراحلش با این بسته فرق دارد. نسخهٔ جدید چارت تاییدشده فقط با دستور تغییر مهندسی تاییدشده (ECO) ساخته می‌شود؛ ایجنت آن را دور نمی‌زند.");
        }

        private async Task<(int Id, string Note)> ExecuteRoutingAsync(EngInboxPackage pkg, int productId, EngInboxBomLine root, RoutingPlan plan, CancellationToken ct)
        {
            if (plan.ReuseId.HasValue) return (plan.ReuseId.Value, plan.ReuseNote);
            string part = plan.Part;
            ResponseDto<PbOpcDto> saved = await _routings.SaveAsync(new SavePbOpcDto
            {
                Id = plan.EditId, ProductId = productId, PartNumber = part, Title = AgentText.Cut(pkg.Title ?? root.Description, 300), Platform = pkg.PlatformCode, Steps = plan.Steps,
            }, ct);
            if (!saved.Success) throw new Blocked("ساخت پیش‌نویس چارت OPC پذیرفته نشد: " + saved.Message);
            await RecordAppliedAsync(pkg.Id, EngInboxAppliedKind.Routing, saved.Data.Id, ct);
            ResponseDto<PbOpcDto> sent = await _routings.SubmitAsync(new PbOpcActionDto { Id = saved.Data.Id }, ct);
            if (!sent.Success) throw new Blocked("پیش‌نویس چارت OPC ساخته شد ولی برای بررسی فرستاده نشد: " + sent.Message);
            return (saved.Data.Id, "چارت OPC " + part + (plan.EditId.HasValue ? " به‌روز شد" : " ساخته شد") + " و برای کنترل فرستاده شد");
        }
    }
}
