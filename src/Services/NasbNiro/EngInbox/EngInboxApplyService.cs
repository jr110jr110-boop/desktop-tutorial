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
            EngInboxPackage pkg = await _uow.Set<EngInboxPackage>().FirstOrDefaultAsync(x => x.Id == packageId && !x.Deleted, ct);
            if (pkg == null) return ResponseDtoHelper.NotFound<EngApplyResult>(EngInboxText.NotFound);
            if (pkg.Status != EngInboxStatus.Ingested && pkg.Status != EngInboxStatus.ApplyBlocked)
                return ResponseDtoHelper.Ok(new EngApplyResult { Outcome = EngApplyOutcome.NotReady, Message = "وضعیت بسته دیگر منتظر ایجنت نیست." });
            pkg.ApplyAttempts++;
            pkg.LastApplyAt = DateTimeOffset.Now;
            try
            {
                string done = pkg.Kind == EngInboxKind.Project ? await LinkProjectAsync(pkg, ct) : await ApplyPlatformAsync(pkg, ct);
                pkg.Status = EngInboxStatus.Applied; pkg.AppliedAt = DateTimeOffset.Now; pkg.StatusAt = DateTimeOffset.Now; pkg.ApplyBlockReason = null; pkg.StatusNote = done;
                Stamp(pkg);
                await _uow.SaveChangesAsync(ct);
                return ResponseDtoHelper.Ok(new EngApplyResult { Outcome = EngApplyOutcome.Applied, Message = done });
            }
            catch (Blocked b)
            {
                AgentDb.Clear(_uow);
                return await BlockAsync(packageId, b.Message, ct);
            }
        }

        private void Stamp(EngInboxPackage p)
        {
            p.ModifyDate = DateTimeOffset.Now;
            p.ModifierUserId = _user.UserId;
        }

        /// <summary>Re-reads the package after a refusal (the failed save is dropped from the tracker) and keeps the reason.</summary>
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

        private async Task<string> ApplyPlatformAsync(EngInboxPackage pkg, CancellationToken ct)
        {
            List<EngInboxBomLine> bom = await _uow.Set<EngInboxBomLine>().AsNoTracking().Where(l => l.PackageId == pkg.Id && !l.Deleted).OrderBy(l => l.SortOrder).ToListAsync(ct);
            List<EngInboxOpcLine> opc = await _uow.Set<EngInboxOpcLine>().AsNoTracking().Where(l => l.PackageId == pkg.Id && !l.Deleted).OrderBy(l => l.SortOrder).ToListAsync(ct);
            EngInboxBomLine root = bom.FirstOrDefault(l => string.IsNullOrEmpty(l.ParentCode));
            if (root == null) throw new Blocked("BOM بسته ریشه ندارد.");

            // the work centres the operations name
            string[] wcCodes = opc.Select(o => o.WorkCenterCode).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Dictionary<string, int> wcIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (wcCodes.Length > 0)
                foreach (var w in await _uow.Set<MfWorkCenter>().AsNoTracking().Where(w => !w.Deleted && wcCodes.Contains(w.Code)).Select(w => new { w.Id, w.Code }).ToListAsync(ct)) wcIds[w.Code] = w.Id;
            string[] missing = wcCodes.Where(c => !wcIds.ContainsKey(c)).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missing.Length > 0) throw new Blocked("مرکز کار با این کدها در سیستم تعریف نشده است: " + string.Join("، ", missing) + ". آن‌ها را در بخش تولید (مراکز کار) تعریف کنید یا بستهٔ اصلاح‌شده بدهید؛ ایجنت خودش دوباره تلاش می‌کند.");

            // the product (the key is the part code of the root assembly)
            int productId = await EnsureProductAsync(pkg, root, ct);

            // one BOM per assembly (a part that has children), the root first
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
            int rootBomId = 0;
            var notes = new List<string>();
            foreach (string a in assemblies)
            {
                EngInboxBomLine self = bom.First(l => l.PartCode == a);
                var lines = children[a].Select((l, i) => new SavePbBomLineDto
                {
                    ItemNumber = i + 1, PartNumber = l.PartCode, PartName = l.Description, Qty = l.Qty, Unit = l.Unit, Material = l.Material, MakeOrBuy = (PbMakeOrBuy)l.MakeBuy,
                    Description = LineNote(l),
                }).ToList();
                var (id, note) = await ApplyBomAsync(pkg, productId, a, string.Equals(a, root.PartCode, StringComparison.Ordinal) ? (pkg.Title ?? self.Description) : self.Description, lines, ct);
                if (a == root.PartCode) rootBomId = id;
                notes.Add(note);
            }

            int? routingId = null;
            if (opc.Count > 0)
            {
                var hasChildren = new HashSet<string>(children.Keys, StringComparer.Ordinal);
                var partInfo = bom.GroupBy(l => l.PartCode, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                var steps = opc.Select(o =>
                {
                    partInfo.TryGetValue(o.PartCode, out EngInboxBomLine part);
                    string stage = !string.IsNullOrEmpty(o.StageCode) ? o.StageCode : o.IsInspection ? "QC" : hasChildren.Contains(o.PartCode) ? "AS" : "MC";
                    string note = "آماده‌سازی " + EngText.Num(o.SetupMinutes) + " دقیقه؛ اجرا " + EngText.Num(o.RunMinutes) + " دقیقه" + (o.IsSpecialProcess ? "؛ فرایند ویژه" : "")
                        + (string.IsNullOrEmpty(o.Predecessors) ? "" : "؛ پیش‌نیاز: " + o.Predecessors) + "؛ منبع: " + Tag + " (بستهٔ " + pkg.Id.ToString(CultureInfo.InvariantCulture) + ")";
                    return new SavePbOpcStepDto
                    {
                        Seq = o.OperationNumber, PartNumber = o.PartCode, PartName = part?.Description, StageCode2 = stage, Title = o.Description, WorkCenterId = wcIds[o.WorkCenterCode],
                        StandardHours = Math.Round((o.SetupMinutes + o.RunMinutes) / 60m, 4), IsQcPoint = o.IsInspection, MakeOrBuy = part == null ? (PbMakeOrBuy?)null : (PbMakeOrBuy)part.MakeBuy, Note = note,
                    };
                }).ToList();
                var (rid, rnote) = await ApplyRoutingAsync(pkg, productId, root, steps, ct);
                routingId = rid;
                notes.Add(rnote);
            }
            pkg.ProductId = productId;
            pkg.BomHeaderId = rootBomId;
            pkg.RoutingId = routingId;
            return "به بانک محصول رفت: " + string.Join("؛ ", notes) + ".";
        }

        private static string LineNote(EngInboxBomLine l)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(l.DrawingNumber)) parts.Add("نقشه " + l.DrawingNumber + (string.IsNullOrEmpty(l.DrawingRevision) ? "" : " بازنگری " + l.DrawingRevision));
            if (!string.IsNullOrEmpty(l.Note)) parts.Add(l.Note);
            return parts.Count == 0 ? null : string.Join("؛ ", parts);
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
            return r.Data.Id;
        }

        private static string BomSignature(IEnumerable<(string Part, decimal Qty, string Unit, int Mb)> lines) =>
            string.Join("\n", lines.Select(l => l.Part + "|" + EngText.Num(l.Qty) + "|" + l.Unit + "|" + l.Mb.ToString(CultureInfo.InvariantCulture)).OrderBy(x => x, StringComparer.Ordinal));

        private async Task<(int Id, string Note)> ApplyBomAsync(EngInboxPackage pkg, int productId, string assembly, string title, List<SavePbBomLineDto> lines, CancellationToken ct)
        {
            string code = assembly + "-BOM";
            PbBomHeader latest = await _uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.Code == code).OrderByDescending(h => h.Revision).ThenByDescending(h => h.Id).FirstOrDefaultAsync(ct);
            int? editId = null;
            if (latest != null)
            {
                if (latest.Status == PbBomStatus.Draft)
                {
                    if (latest.PreparedByUserId != _user.UserId) throw new Blocked("برای " + assembly + " یک پیش‌نویس BOM (بازنگری " + PbText.Revision(latest.Revision) + ") هست که شخص دیگری تهیه کرده؛ ایجنت آن را تغییر نمی‌دهد. آن پیش‌نویس را بفرستید یا حذف کنید، یا این بسته را کنار بگذارید.");
                    editId = latest.Id;
                }
                else
                {
                    var current = await _uow.Set<PbBomLine>().AsNoTracking().Where(l => l.BomHeaderId == latest.Id && !l.Deleted && !l.IsHeading).Select(l => new { l.PartNumber, l.Qty, l.Unit, l.MakeOrBuy }).ToListAsync(ct);
                    if (BomSignature(current.Select(l => (l.PartNumber, l.Qty, l.Unit, (int)l.MakeOrBuy))) == BomSignature(lines.Select(l => (l.PartNumber, l.Qty, l.Unit, (int)l.MakeOrBuy))))
                        return (latest.Id, "BOM " + code + " بازنگری " + PbText.Revision(latest.Revision) + " همین محتوا را دارد (بدون تغییر)");
                    throw new Blocked(latest.Status == PbBomStatus.UnderReview
                        ? "BOM " + code + " بازنگری " + PbText.Revision(latest.Revision) + " در حال بررسی (امضا) است و محتوایش با این بسته فرق دارد؛ بازبین آن را «برگشت» دهد تا ایجنت آن را با این بسته به‌روز کند، یا این بسته را کنار بگذارید."
                        : "BOM " + code + " بازنگری " + PbText.Revision(latest.Revision) + " قبلاً " + (latest.Status == PbBomStatus.Released ? "منتشر" : latest.Status == PbBomStatus.Approved ? "تایید" : "بسته") + " شده و محتوایش با این بسته فرق دارد. نسخهٔ جدید یک طرح منتشرشده فقط با دستور تغییر مهندسی تاییدشده (ECO) ساخته می‌شود؛ ایجنت آن را دور نمی‌زند. ECO را ثبت و تایید کنید و تغییر را در همان بازنگری وارد کنید، یا این بسته را کنار بگذارید.");
                }
            }
            ResponseDto<PbBomDto> saved = await _boms.SaveAsync(new SavePbBomDto
            {
                Id = editId, ProductId = productId, AssemblyPartNumber = assembly, Title = AgentText.Cut(title, 300), Platform = pkg.PlatformCode, IssueFor = Tag + " (بستهٔ " + pkg.Id.ToString(CultureInfo.InvariantCulture) + ")", Lines = lines,
            }, ct);
            if (!saved.Success) throw new Blocked("ساخت پیش‌نویس BOM " + code + " پذیرفته نشد: " + saved.Message);
            ResponseDto<PbBomDto> sent = await _boms.SubmitAsync(new PbBomActionDto { Id = saved.Data.Id }, ct);
            if (!sent.Success) throw new Blocked("پیش‌نویس BOM " + code + " ساخته شد ولی برای بررسی فرستاده نشد: " + sent.Message);
            return (saved.Data.Id, "BOM " + code + (editId.HasValue ? " به‌روز شد" : " ساخته شد") + " و برای امضا فرستاده شد");
        }

        private async Task<(int Id, string Note)> ApplyRoutingAsync(EngInboxPackage pkg, int productId, EngInboxBomLine root, List<SavePbOpcStepDto> steps, CancellationToken ct)
        {
            string part = root.PartCode;
            PbOpcRouting latest = await _uow.Set<PbOpcRouting>().AsNoTracking().Where(r => !r.Deleted && r.PartNumber == part).OrderByDescending(r => r.Revision).ThenByDescending(r => r.Id).FirstOrDefaultAsync(ct);
            int? editId = null;
            if (latest != null)
            {
                if (latest.Status == PbRoutingStatus.Draft)
                {
                    if (latest.PreparedByUserId != _user.UserId) throw new Blocked("برای " + part + " یک پیش‌نویس چارت OPC (بازنگری " + PbText.Revision(latest.Revision) + ") هست که شخص دیگری تهیه کرده؛ ایجنت آن را تغییر نمی‌دهد.");
                    editId = latest.Id;
                }
                else
                {
                    var current = await _uow.Set<PbOpcStep>().AsNoTracking().Where(s => s.RoutingId == latest.Id && !s.Deleted).Select(s => new { s.PartNumber, s.Seq, s.WorkCenterId, s.IsQcPoint }).ToListAsync(ct);
                    string Sig(IEnumerable<(string P, int S, int? W, bool Q)> x) => string.Join("\n", x.Select(i => i.P + "|" + i.S.ToString(CultureInfo.InvariantCulture) + "|" + i.W?.ToString(CultureInfo.InvariantCulture) + "|" + (i.Q ? "Q" : "-")).OrderBy(i => i, StringComparer.Ordinal));
                    if (Sig(current.Select(s => (s.PartNumber, s.Seq, s.WorkCenterId, s.IsQcPoint))) == Sig(steps.Select(s => (s.PartNumber, s.Seq, s.WorkCenterId, s.IsQcPoint))))
                        return (latest.Id, "چارت OPC " + part + " بازنگری " + PbText.Revision(latest.Revision) + " همین مراحل را دارد (بدون تغییر)");
                    throw new Blocked(latest.Status == PbRoutingStatus.UnderReview
                        ? "چارت OPC " + part + " بازنگری " + PbText.Revision(latest.Revision) + " در حال بررسی است و مراحلش با این بسته فرق دارد؛ بازبین آن را «برگشت» دهد تا ایجنت آن را به‌روز کند، یا این بسته را کنار بگذارید."
                        : "چارت OPC " + part + " بازنگری " + PbText.Revision(latest.Revision) + " قبلاً تایید شده و مراحلش با این بسته فرق دارد. نسخهٔ جدید چارت تاییدشده فقط با دستور تغییر مهندسی تاییدشده (ECO) ساخته می‌شود؛ ایجنت آن را دور نمی‌زند.");
                }
            }
            ResponseDto<PbOpcDto> saved = await _routings.SaveAsync(new SavePbOpcDto
            {
                Id = editId, ProductId = productId, PartNumber = part, Title = AgentText.Cut((pkg.Title ?? root.Description) + " [" + Tag + " " + pkg.Id.ToString(CultureInfo.InvariantCulture) + "]", 300), Platform = pkg.PlatformCode, Steps = steps,
            }, ct);
            if (!saved.Success) throw new Blocked("ساخت پیش‌نویس چارت OPC پذیرفته نشد: " + saved.Message);
            ResponseDto<PbOpcDto> sent = await _routings.SubmitAsync(new PbOpcActionDto { Id = saved.Data.Id }, ct);
            if (!sent.Success) throw new Blocked("پیش‌نویس چارت OPC ساخته شد ولی برای بررسی فرستاده نشد: " + sent.Message);
            return (saved.Data.Id, "چارت OPC " + part + (editId.HasValue ? " به‌روز شد" : " ساخته شد") + " و برای کنترل فرستاده شد");
        }
    }
}
