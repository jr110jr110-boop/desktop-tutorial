using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.ProductBankDtos;

namespace Services.NasbNiro.ProductBank
{
    public interface IPbProductService
    {
        Task<ResponseDto<List<PbProductDto>>> GetProductsAsync(CancellationToken ct);
        Task<ResponseDto<PbProductDto>> SaveProductAsync(SavePbProductDto dto, CancellationToken ct);
        Task<ResponseDto<PbProductBankDto>> GetBankAsync(int productId, CancellationToken ct);

        Task<ResponseDto<PbWbsTemplateDto>> ImportMspdiAsync(PbImportMspdiDto dto, CancellationToken ct);
        Task<ResponseDto<PbWbsTemplateDto>> GetTemplateAsync(int id, CancellationToken ct);
        Task<ResponseDto<PbWbsTemplateDto>> ApproveTemplateAsync(int id, CancellationToken ct);

        Task<ResponseDto<PbBaselineDto>> InstantiateAsync(PbInstantiateDto dto, CancellationToken ct);

        /// <summary>Package G: the baseline of an adopted legacy project (the adoption workflow has its own three-person review; the readiness gate and the plan lock do not apply to a project that is already running).</summary>
        Task<ResponseDto<PbBaselineDto>> InstantiateAdoptedAsync(PbInstantiateDto dto, int bomHeaderId, Func<PbBaselineDto, CancellationToken, Task> inTransaction, CancellationToken ct);
        Task<ResponseDto<PbBaselineDto>> GetBaselineAsync(int workOrderId, CancellationToken ct);
        Task<ResponseDto<PbBaselineDto>> GenerateWbsAsync(PbGenerateWbsDto dto, CancellationToken ct);

        Task<ResponseDto<PbChangeDto>> SaveChangeAsync(SavePbChangeDto dto, CancellationToken ct);
        Task<ResponseDto<PbChangeDto>> SubmitChangeAsync(PbChangeActionDto dto, CancellationToken ct);
        Task<ResponseDto<PbChangeDto>> ApproveChangeEngineeringAsync(PbChangeActionDto dto, CancellationToken ct);
        Task<ResponseDto<PbChangeDto>> ApproveChangePlanningAsync(PbChangeActionDto dto, CancellationToken ct);
        Task<ResponseDto<PbChangeDto>> RejectChangeAsync(PbChangeActionDto dto, CancellationToken ct);
        Task<ResponseDto<PbChangeDto>> ApplyChangeAsync(PbChangeActionDto dto, CancellationToken ct);
        Task<ResponseDto<PbChangeDto>> CancelChangeAsync(PbChangeActionDto dto, CancellationToken ct);
    }

    /// <summary>
    /// PB1 part 4 and 5: the product (BOM + OPC routings + WBS template), the MSPDI import, taking a product into a project (the baseline copy)
    /// and the per-project deviations. A deviation is approved by ENGINEERING and PLANNING (two different people, neither the requester);
    /// once the PC baseline of the contract is current it is applied only with an approved change request of project control.
    /// </summary>
    public class PbProductService : PbServiceBase, IPbProductService
    {
        private readonly IPcProjectService _project;
        private readonly global::Services.NasbNiro.ProjectBalance.IProjectBalanceService _balance;
        private readonly global::Services.NasbNiro.OpsData.IOpsReadinessService _readiness;

        public PbProductService(IUnitOfWork uow, IInventoryUserContext user, IPcProjectService project, global::Services.NasbNiro.ProjectBalance.IProjectBalanceService balance = null, global::Services.NasbNiro.OpsData.IOpsReadinessService readiness = null) : base(uow, user)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _balance = balance;
            _readiness = readiness;
        }

        // -------------------------------------------------------------------------------- products

        public async Task<ResponseDto<List<PbProductDto>>> GetProductsAsync(CancellationToken ct)
        {
            List<PbProduct> products = await Uow.Set<PbProduct>().AsNoTracking().Where(p => !p.Deleted).OrderBy(p => p.Code).ToListAsync(ct);
            // four grouped queries for all products, not four per product
            var boms = await Uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.ProductId != null).Select(h => new { h.ProductId, h.Id, h.Revision, h.Status }).ToListAsync(ct);
            var routings = await Uow.Set<PbOpcRouting>().AsNoTracking().Where(r => !r.Deleted && r.ProductId != null && r.Status == PbRoutingStatus.Approved).GroupBy(r => r.ProductId).Select(g => new { ProductId = g.Key, Count = g.Count() }).ToListAsync(ct);
            var templates = await Uow.Set<PbWbsTemplate>().AsNoTracking().Where(t => !t.Deleted).Select(t => new { t.ProductId, t.Id, t.Version, t.IsApproved }).ToListAsync(ct);
            var result = new List<PbProductDto>();
            foreach (PbProduct p in products)
            {
                var released = boms.Where(b => b.ProductId == p.Id && b.Status == PbBomStatus.Released).OrderByDescending(b => b.Revision).FirstOrDefault();
                var tpl = templates.Where(t => t.ProductId == p.Id).OrderByDescending(t => t.IsApproved).ThenByDescending(t => t.Version).FirstOrDefault();
                result.Add(new PbProductDto
                {
                    Id = p.Id, Code = p.Code, Title = p.Title, Platform = p.Platform, Description = p.Description, EquipmentId = p.EquipmentId, IsActive = p.IsActive,
                    ReleasedBomId = released?.Id, ReleasedRevision = released?.Revision, BomRevisions = boms.Count(b => b.ProductId == p.Id),
                    RoutingCount = routings.Where(r => r.ProductId == p.Id).Select(r => r.Count).FirstOrDefault(), WbsTemplateId = tpl?.Id, WbsTemplateVersion = tpl?.Version,
                });
            }
            return ResponseDtoHelper.Ok(result);
        }

        private async Task<PbProductDto> ProductDtoAsync(PbProduct p, CancellationToken ct)
        {
            PbBomHeader released = await Uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.ProductId == p.Id && h.Status == PbBomStatus.Released).OrderByDescending(h => h.Revision).FirstOrDefaultAsync(ct);
            PbWbsTemplate tpl = await Uow.Set<PbWbsTemplate>().AsNoTracking().Where(t => !t.Deleted && t.ProductId == p.Id).OrderByDescending(t => t.IsApproved).ThenByDescending(t => t.Version).FirstOrDefaultAsync(ct);
            return new PbProductDto
            {
                Id = p.Id, Code = p.Code, Title = p.Title, Platform = p.Platform, Description = p.Description, EquipmentId = p.EquipmentId, IsActive = p.IsActive,
                ReleasedBomId = released?.Id, ReleasedRevision = released?.Revision,
                BomRevisions = await Uow.Set<PbBomHeader>().CountAsync(h => !h.Deleted && h.ProductId == p.Id, ct),
                RoutingCount = await Uow.Set<PbOpcRouting>().CountAsync(r => !r.Deleted && r.ProductId == p.Id && r.Status == PbRoutingStatus.Approved, ct),
                WbsTemplateId = tpl?.Id, WbsTemplateVersion = tpl?.Version,
            };
        }

        public async Task<ResponseDto<PbProductDto>> SaveProductAsync(SavePbProductDto dto, CancellationToken ct)
        {
            if (!InRole(PbRoles.Writers)) return ResponseDtoHelper.BadRequest<PbProductDto>(PbMessages.NotAllowed);
            if (dto == null) return ResponseDtoHelper.BadRequest<PbProductDto>(PbMessages.InvalidData);
            string code = PbText.PartNumber(dto.Code);
            string title = PbText.Clean(dto.Title);
            if (code == null) return ResponseDtoHelper.BadRequest<PbProductDto>(PbMessages.PartNumberRequired);
            if (title == null) return ResponseDtoHelper.BadRequest<PbProductDto>("عنوان محصول را وارد کنید.");
            PbProduct p;
            if (dto.Id.HasValue)
            {
                p = await Uow.Set<PbProduct>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (p == null) return ResponseDtoHelper.NotFound<PbProductDto>(PbMessages.NotFound);
            }
            else
            {
                p = new PbProduct();
                Uow.Set<PbProduct>().Add(p);
            }
            if (await Uow.Set<PbProduct>().AnyAsync(x => !x.Deleted && x.Code == code && x.Id != p.Id, ct)) return ResponseDtoHelper.BadRequest<PbProductDto>("محصولی با این شماره قبلا ثبت شده است.");
            p.Code = code; p.Title = title; p.Platform = PbText.Clean(dto.Platform); p.Description = PbText.Clean(dto.Description); p.EquipmentId = dto.EquipmentId; p.IsActive = dto.IsActive;
            Stamp(p, Uow.Entry(p).State == EntityState.Added);
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await ProductDtoAsync(p, ct));
        }

        public async Task<ResponseDto<PbProductBankDto>> GetBankAsync(int productId, CancellationToken ct)
        {
            PbProduct p = await Uow.Set<PbProduct>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == productId && !x.Deleted, ct);
            if (p == null) return ResponseDtoHelper.NotFound<PbProductBankDto>(PbMessages.NotFound);
            var dto = new PbProductBankDto { Product = await ProductDtoAsync(p, ct) };
            dto.Boms = await Uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.ProductId == productId).OrderByDescending(h => h.Revision)
                .Select(h => new PbBomListDto
                {
                    Id = h.Id, ProductId = h.ProductId, Code = h.Code, Revision = h.Revision, RevisionText = PbText.Revision(h.Revision), Title = h.Title, AssemblyPartNumber = h.AssemblyPartNumber, Status = h.Status,
                    DocumentDate = h.DocumentDate, EffectiveFrom = h.EffectiveFrom, LineCount = h.Lines.Count(l => !l.Deleted), PreparedByUserId = h.PreparedByUserId, CreateDate = h.CreateDate,
                }).ToListAsync(ct);
            dto.Routings = await Uow.Set<PbOpcRouting>().AsNoTracking().Where(r => !r.Deleted && r.ProductId == productId).OrderBy(r => r.PartNumber).ThenByDescending(r => r.Revision)
                .Select(r => new PbOpcListDto
                {
                    Id = r.Id, ProductId = r.ProductId, PartNumber = r.PartNumber, Title = r.Title, Revision = r.Revision, Status = r.Status,
                    StepCount = r.Steps.Count(s => !s.Deleted), PublishedToMesAt = r.PublishedToMesAt,
                }).ToListAsync(ct);
            dto.Templates = await Uow.Set<PbWbsTemplate>().AsNoTracking().Where(t => !t.Deleted && t.ProductId == productId).OrderByDescending(t => t.Version)
                .Select(t => new PbWbsTemplateListDto
                {
                    Id = t.Id, ProductId = t.ProductId, Title = t.Title, Version = t.Version, IsApproved = t.IsApproved, Source = t.Source, TotalDurationDays = t.TotalDurationDays,
                    NodeCount = Uow.Set<PbWbsTemplateNode>().Count(n => n.TemplateId == t.Id && !n.Deleted),
                }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(dto);
        }

        // -------------------------------------------------------------------------------- WBS template (MSPDI)

        public async Task<ResponseDto<PbWbsTemplateDto>> ImportMspdiAsync(PbImportMspdiDto dto, CancellationToken ct)
        {
            if (!InRole(PbRoles.Writers)) return ResponseDtoHelper.BadRequest<PbWbsTemplateDto>(PbMessages.NotAllowed);
            if (dto == null || dto.ProductId <= 0) return ResponseDtoHelper.BadRequest<PbWbsTemplateDto>(PbMessages.InvalidData);
            if (!await Uow.Set<PbProduct>().AnyAsync(p => p.Id == dto.ProductId && !p.Deleted, ct)) return ResponseDtoHelper.NotFound<PbWbsTemplateDto>(PbMessages.NotFound);
            var (tasks, projectName, error, warnings) = PbMspdi.ParseWithWarnings(dto.Xml);
            if (error != null) return ResponseDtoHelper.BadRequest<PbWbsTemplateDto>(error);

            int version = (await Uow.Set<PbWbsTemplate>().Where(t => t.ProductId == dto.ProductId).Select(t => (int?)t.Version).MaxAsync(ct) ?? 0) + 1;
            var tpl = new PbWbsTemplate
            {
                ProductId = dto.ProductId, Title = PbText.Clean(dto.Title) ?? projectName ?? "قالب WBS", Version = version, IsApproved = false,
                Source = "MSPDI", ImportedByUserId = User.UserId,
            };
            Stamp(tpl, true);
            Uow.Set<PbWbsTemplate>().Add(tpl);
            await Uow.SaveChangesAsync(ct);
            await AddNodesAsync(tpl, tasks, ct);
            PbWbsTemplateDto imported = await TemplateDtoAsync(tpl.Id, ct);
            imported.Warnings = warnings;
            return ResponseDtoHelper.Ok(imported);
        }

        /// <summary>Writes the nodes and links of a parsed task list into a template (also used by the seed).</summary>
        public async Task AddNodesAsync(PbWbsTemplate tpl, List<MspdiTask> tasks, CancellationToken ct)
        {
            var nodes = new List<(MspdiTask Task, PbWbsTemplateNode Node)>();
            var stack = new Stack<(int Level, PbWbsTemplateNode Node)>();
            DateTime? first = tasks.Where(t => t.Start.HasValue).Select(t => t.Start).Min();
            int seq = 0;
            for (int i = 0; i < tasks.Count; i++)
            {
                MspdiTask t = tasks[i];
                bool hasChild = i + 1 < tasks.Count && tasks[i + 1].OutlineLevel > t.OutlineLevel;
                string phase = PbMspdi.PhaseOf(t.Name);
                string pn = PbMspdi.PartNumberOf(t.Name);
                string resource = t.Resources.Count > 0 ? PbMspdi.ResourceClassOf(t.Resources[0]) : null;
                PbMakeOrBuy? mob = null;
                string upper = " " + (t.Name ?? "").ToUpperInvariant() + " ";
                if (upper.Contains(" MPL ")) mob = PbMakeOrBuy.Make; else if (upper.Contains(" PL ")) mob = PbMakeOrBuy.Purchase;
                var node = new PbWbsTemplateNode
                {
                    TemplateId = tpl.Id, Seq = ++seq, Uid = t.Uid, OutlineLevel = t.OutlineLevel, Title = t.Name,
                    Kind = t.IsMilestone || (t.DurationDays == 0 && !hasChild) ? PbWbsNodeKind.Milestone : (t.IsSummary || hasChild ? PbWbsNodeKind.Summary : PbWbsNodeKind.Task),
                    PhaseCode = phase, DurationDays = t.DurationDays,
                    StartOffsetDays = first.HasValue && t.Start.HasValue ? (decimal?)Math.Round((decimal)(t.Start.Value - first.Value).TotalDays, 3) : null,
                    PartNumber = pn, MakeOrBuy = mob, ResourceClass = resource,
                };
                Stamp(node, true);
                while (stack.Count > 0 && stack.Peek().Level >= t.OutlineLevel) stack.Pop();
                node.ParentId = null;
                if (stack.Count > 0) node.Parent = stack.Peek().Node;
                stack.Push((t.OutlineLevel, node));
                nodes.Add((t, node));
                Uow.Set<PbWbsTemplateNode>().Add(node);
            }
            tpl.TotalDurationDays = tasks.Where(t => t.OutlineLevel == 1).Select(t => t.DurationDays).DefaultIfEmpty(0).Max();
            await Uow.SaveChangesAsync(ct);

            var byUid = nodes.GroupBy(n => n.Task.Uid).ToDictionary(g => g.Key, g => g.First().Node);
            foreach (var (task, node) in nodes)
                foreach (var pr in task.Predecessors)
                    if (byUid.TryGetValue(pr.PredecessorUid, out PbWbsTemplateNode from))
                    {
                        var link = new PbWbsTemplateLink { TemplateId = tpl.Id, FromNodeId = from.Id, ToNodeId = node.Id, LinkType = pr.Type, LagDays = pr.LagDays };
                        Stamp(link, true);
                        Uow.Set<PbWbsTemplateLink>().Add(link);
                    }
            await Uow.SaveChangesAsync(ct);
        }

        public async Task<ResponseDto<PbWbsTemplateDto>> GetTemplateAsync(int id, CancellationToken ct)
        {
            PbWbsTemplateDto dto = await TemplateDtoAsync(id, ct);
            return dto == null ? ResponseDtoHelper.NotFound<PbWbsTemplateDto>(PbMessages.NotFound) : ResponseDtoHelper.Ok(dto);
        }

        private async Task<PbWbsTemplateDto> TemplateDtoAsync(int id, CancellationToken ct)
        {
            PbWbsTemplate t = await Uow.Set<PbWbsTemplate>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (t == null) return null;
            List<PbWbsTemplateNode> nodes = await Uow.Set<PbWbsTemplateNode>().AsNoTracking().Where(n => n.TemplateId == id && !n.Deleted).OrderBy(n => n.Seq).ToListAsync(ct);
            List<PbWbsTemplateLink> links = await Uow.Set<PbWbsTemplateLink>().AsNoTracking().Where(l => l.TemplateId == id && !l.Deleted).ToListAsync(ct);
            return new PbWbsTemplateDto
            {
                Id = t.Id, ProductId = t.ProductId, Title = t.Title, Version = t.Version, IsApproved = t.IsApproved, Source = t.Source, TotalDurationDays = t.TotalDurationDays, NodeCount = nodes.Count,
                ImportedByUserId = t.ImportedByUserId, ApprovedByUserId = t.ApprovedByUserId, SecondApprovedByUserId = t.SecondApprovedByUserId,
                Nodes = nodes.Select(NodeDto).ToList(),
                Links = links.Select(l => new PbWbsLinkDto { FromNodeId = l.FromNodeId, ToNodeId = l.ToNodeId, LinkType = l.LinkType, LagDays = l.LagDays }).ToList(),
                CanApprove = !t.IsApproved && !IsAi && TemplateStep(t) != null,
            };
        }

        private static PbWbsNodeDto NodeDto(PbWbsTemplateNode n) => new PbWbsNodeDto
        {
            Id = n.Id, ParentId = n.ParentId, Seq = n.Seq, Uid = n.Uid, OutlineLevel = n.OutlineLevel, Title = n.Title, Kind = n.Kind, PhaseCode = n.PhaseCode,
            DurationDays = n.DurationDays, StartOffsetDays = n.StartOffsetDays, PartNumber = n.PartNumber, MakeOrBuy = n.MakeOrBuy, ResourceClass = n.ResourceClass,
        };

        /// <summary>"eng" or "plan" when the caller can sign the next step of the template approval; null otherwise.</summary>
        private string TemplateStep(PbWbsTemplate t)
        {
            int me = User.UserId;
            if (t.ImportedByUserId == me || t.CreatorUserId == me) return null;
            if (!t.ApprovedByUserId.HasValue) return InRole(PbRoles.Engineering) ? "eng" : null;
            if (t.ApprovedByUserId == me) return null;
            return InRole(PbRoles.Planning) ? "plan" : null;
        }

        public async Task<ResponseDto<PbWbsTemplateDto>> ApproveTemplateAsync(int id, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbWbsTemplateDto>(PbMessages.AiNeverApproves);
            PbWbsTemplate t = await Uow.Set<PbWbsTemplate>().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (t == null) return ResponseDtoHelper.NotFound<PbWbsTemplateDto>(PbMessages.NotFound);
            if (t.IsApproved) return ResponseDtoHelper.BadRequest<PbWbsTemplateDto>("این قالب قبلا تایید شده است.");
            string step = TemplateStep(t);
            if (step == null) return ResponseDtoHelper.BadRequest<PbWbsTemplateDto>(User.UserId == t.ImportedByUserId || User.UserId == t.CreatorUserId || User.UserId == t.ApprovedByUserId ? PbMessages.SameUser : PbMessages.NotAllowed);
            DateTimeOffset now = DateTimeOffset.Now;
            if (step == "eng") { t.ApprovedByUserId = User.UserId; t.ApprovedAt = now; }
            else { t.SecondApprovedByUserId = User.UserId; t.SecondApprovedAt = now; t.IsApproved = true; }
            Stamp(t, false);
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await TemplateDtoAsync(id, ct));
        }

        // -------------------------------------------------------------------------------- project instantiation

        /// <summary>The approved project-control change request that lets a product into a contract whose plan is locked: it must be the board's approved request of the same contract, name the work order, and not be used before.</summary>
        private async Task<(PcChangeRequest Cr, string Error)> LockedPlanRequestAsync(int? changeRequestId, WorkOrder wo, int? exceptBaselineId, CancellationToken ct)
        {
            if (!changeRequestId.HasValue)
                return (null, "برنامهٔ این قرارداد با خط پایهٔ کنترل پروژه قفل است؛ افزودن محصول فقط با درخواست تغییر تاییدشده ممکن است. شمارهٔ درخواست تغییر را وارد کنید.");
            PcChangeRequest cr = await Uow.Set<PcChangeRequest>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == changeRequestId.Value && !x.Deleted, ct);
            if (cr == null || cr.ContractId != wo.ContractId) return (null, "این درخواست تغییر برای همین قرارداد پیدا نشد.");
            if (!IsDecidedApproval(cr)) return (null, "درخواست تغییر هنوز در هیئت تایید نشده است.");
            if (!Mentions(cr, wo.Code)) return (null, "درخواست تغییر باید کد دستور کار (" + wo.Code + ") را در شرح یا دلیل خود بیاورد؛ هر درخواست فقط همان‌ها را مجاز می‌کند که در آن آمده است.");
            if (await Uow.Set<PbProjectBaseline>().AnyAsync(b => !b.Deleted && b.ChangeRequestId == cr.Id && b.Id != (exceptBaselineId ?? 0), ct))
                return (null, "این درخواست تغییر قبلاً برای افزودن محصول دیگری به کار رفته است.");
            return (cr, null);
        }

        private static bool IsDecidedApproval(PcChangeRequest cr) =>
            cr.Decision == PcChangeDecision.Approve && (cr.Status == PcChangeStatus.Decided || cr.Status == PcChangeStatus.Implementing || cr.Status == PcChangeStatus.Closed);

        private static bool Mentions(PcChangeRequest cr, string text) =>
            !string.IsNullOrWhiteSpace(text) && ((cr.Description ?? "") + " " + (cr.Reason ?? "") + " " + (cr.ScenarioJson ?? "")).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;

        public async Task<ResponseDto<PbBaselineDto>> InstantiateAsync(PbInstantiateDto dto, CancellationToken ct)
        {
            var r = await InstantiateCoreAsync(dto, ct);
            if (r.Success && _balance != null)
            {
                int wo = r.Data?.WorkOrderId ?? 0;
                if (wo > 0) await _balance.AfterChangeAsync(wo, "Instantiate", ct);
            }
            // PB8: what engineering still owes stays open as obligations with an owner and a due date (the owners are told)
            if (r.Success && _readiness != null && r.Data != null)
            {
                try { await _readiness.AfterBaselineAsync(r.Data.WorkOrderId, dto.ProductId, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception) { /* the baseline exists; the daily OPS-DATA run opens the obligations */ }
            }
            return r;
        }

        /// <summary>Package G: the baseline of an adopted project. <paramref name="inTransaction"/> runs INSIDE the same database transaction after the baseline exists (it must load what it changes itself: with the retrying execution strategy it runs again), so the adoption record and the baseline are saved together or not at all.</summary>
        public Task<ResponseDto<PbBaselineDto>> InstantiateAdoptedAsync(PbInstantiateDto dto, int bomHeaderId, Func<PbBaselineDto, CancellationToken, Task> inTransaction, CancellationToken ct) => InstantiateCoreAsync(dto, ct, bomHeaderId, inTransaction);

        private async Task<ResponseDto<PbBaselineDto>> InstantiateCoreAsync(PbInstantiateDto dto, CancellationToken ct, int? adoptedBomId = null, Func<PbBaselineDto, CancellationToken, Task> inTransaction = null)
        {
            if (adoptedBomId == null && !InRole(PbRoles.Requesters)) return ResponseDtoHelper.BadRequest<PbBaselineDto>(PbMessages.NotAllowed);
            if (dto == null || dto.WorkOrderId <= 0 || dto.ProductId <= 0) return ResponseDtoHelper.BadRequest<PbBaselineDto>(PbMessages.InvalidData);
            WorkOrder wo = await Uow.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == dto.WorkOrderId && !w.Deleted, ct);
            if (wo == null) return ResponseDtoHelper.NotFound<PbBaselineDto>("دستور کار پیدا نشد.");
            PbProduct product = await Uow.Set<PbProduct>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.ProductId && !p.Deleted && p.IsActive, ct);
            if (product == null) return ResponseDtoHelper.NotFound<PbBaselineDto>("محصول در بانک محصول پیدا نشد.");
            if (await Uow.Set<PbProjectBaseline>().AnyAsync(b => b.WorkOrderId == wo.Id && !b.Deleted && b.IsCurrent, ct))
                return ResponseDtoHelper.BadRequest<PbBaselineDto>("برای این دستور کار قبلا پایه‌ریزی از بانک محصول انجام شده است؛ تغییرات را از راه انحراف ثبت کنید.");
            // R1c: once the contract has a current baseline the plan is locked and a product enters it only through an approved change request
            PcChangeRequest cr = null;
            if (adoptedBomId == null && await PcPlanLock.IsLockedAsync(Uow, wo.ContractId, ct))
            {
                var gate = await LockedPlanRequestAsync(dto.ChangeRequestId, wo, null, ct);
                if (gate.Error != null) return ResponseDtoHelper.BadRequest<PbBaselineDto>(gate.Error);
                cr = gate.Cr;
            }
            PbBomHeader bomHead = await Uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.ProductId == product.Id && h.Status == PbBomStatus.Released && (adoptedBomId == null || h.Id == adoptedBomId.Value)).OrderByDescending(h => h.Revision).FirstOrDefaultAsync(ct);
            if (bomHead == null) return ResponseDtoHelper.BadRequest<PbBaselineDto>("برای این محصول BOM منتشرشده (Released) وجود ندارد.");
            // PB8: the engineering readiness gate (hard checklist items); an exception approved by the PMO and the CEO lets it pass
            if (_readiness != null && adoptedBomId == null)
            {
                global::Services.NasbNiro.OpsData.ReadinessGateResult ready = await _readiness.GateForBaselineAsync(wo.Id, product.Id, ct);
                if (ready.Refusal != null) return ResponseDtoHelper.BadRequest<PbBaselineDto>(ready.Refusal);
            }
            PbWbsTemplate tpl = await Uow.Set<PbWbsTemplate>().AsNoTracking().Where(t => !t.Deleted && t.ProductId == product.Id && t.IsApproved).OrderByDescending(t => t.Version).FirstOrDefaultAsync(ct);
            List<PbOpcRouting> routings = await Uow.Set<PbOpcRouting>().AsNoTracking().Where(r => !r.Deleted && r.ProductId == product.Id && r.Status == PbRoutingStatus.Approved).ToListAsync(ct);
            routings = routings.GroupBy(r => r.PartNumber).Select(g => g.OrderByDescending(r => r.Revision).First()).ToList();

            int count = Math.Max(1, wo.Count);
            try
            {
                return await InTransactionAsync(async () =>
                {
                    var baseline = new PbProjectBaseline
                    {
                        WorkOrderId = wo.Id, ContractId = wo.ContractId, ProductId = product.Id, BomHeaderId = bomHead.Id, SourceBomCode = bomHead.Code, SourceBomRevision = bomHead.Revision,
                        WbsTemplateId = tpl?.Id, WbsTemplateVersion = tpl?.Version ?? 0, Count = count, CopiedAt = DateTimeOffset.Now, IsCurrent = true, ChangeRequestId = cr?.Id,
                    };
                    Stamp(baseline, true);
                    Uow.Set<PbProjectBaseline>().Add(baseline);
                    await Uow.SaveChangesAsync(ct);

                    foreach (PbProjectBomLine pl in await ExplodeAsync(bomHead.Id, baseline.Id, count, ct)) Uow.Set<PbProjectBomLine>().Add(pl);
                    foreach (PbOpcRouting r in routings)
                    {
                        var pr = new PbProjectRouting { BaselineId = baseline.Id, OpcRoutingId = r.Id, Revision = r.Revision, PartNumber = r.PartNumber };
                        Stamp(pr, true);
                        Uow.Set<PbProjectRouting>().Add(pr);
                    }
                    await Uow.SaveChangesAsync(ct);
                    string wbsNote;
                    if (adoptedBomId == null) wbsNote = await GeneratePcWbsAsync(baseline.Id, ct);
                    else wbsNote = await LinkAdoptedBaselineAsync(baseline, wo, product, ct);
                    PbBaselineDto result = await BaselineDtoAsync(baseline.Id, ct);
                    result.WbsNote = wbsNote;
                    if (inTransaction != null) await inTransaction(result, ct);
                    return ResponseDtoHelper.Ok(result);
                }, r => r.Success, ct);
            }
            catch (DbUpdateException)
            {
                return ResponseDtoHelper.BadRequest<PbBaselineDto>("برای این دستور کار همزمان پایه‌ریزی دیگری انجام شد.");
            }
        }

        /// <summary>
        /// Package G (fix round 1, H2): the baseline of an ADOPTED running project is linked to the plan that already exists; no WBS node or activity is generated, the locked plan is not edited and no earned-value figure moves.
        /// When the work order already has a product node in the project WBS the baseline points at it (a mapping). When the contract has a plan but the work order has no node to map to, a draft PC change request
        /// (the project's normal change path, decided by the PMO) asks for the link and names the baseline. A contract without any plan gets no generated plan either: the normal «ساخت WBS» action does it with its own gates.
        /// </summary>
        private async Task<string> LinkAdoptedBaselineAsync(PbProjectBaseline baseline, WorkOrder wo, PbProduct product, CancellationToken ct)
        {
            PcWbsNode node = await Uow.Set<PcWbsNode>().AsNoTracking().Where(n => n.ContractId == wo.ContractId && n.WorkOrderId == wo.Id && n.NodeType == PcWbsNodeType.Product && !n.Deleted).OrderBy(n => n.Id).FirstOrDefaultAsync(ct);
            if (node != null)
            {
                baseline.PcProductNodeId = node.Id;
                await Uow.SaveChangesAsync(ct);
                return $"پایه‌ریزی به گرهٔ موجود {node.Code} در WBS پروژه نگاشت شد؛ WBS و برنامهٔ زمانی تغییر نکرد و هیچ عدد ارزش کسب‌شده دست نخورد.";
            }
            bool contractHasPlan = await Uow.Set<PcWbsNode>().AnyAsync(n => n.ContractId == wo.ContractId && !n.Deleted, ct) || await Uow.Set<PcActivity>().AnyAsync(a => a.ContractId == wo.ContractId && !a.Deleted, ct);
            if (!contractHasPlan && !await PcPlanLock.IsLockedAsync(Uow, wo.ContractId, ct))
                return "این قرارداد هنوز برنامهٔ زمانی ندارد؛ برای ساخت WBS از روی قالب محصول، پس از آماده شدن برنامه «ساخت WBS» را بزنید (چیزی ساخته نشد).";
            DateTimeOffset now = DateTimeOffset.Now;
            var cr = new PcChangeRequest
            {
                ContractId = wo.ContractId, Origin = PcChangeOrigin.InternalRequest, Type = PcChangeType.Method, RequesterUserId = User.UserId, Status = PcChangeStatus.Draft, ScenarioJson = "[]",
                Description = $"پذیرش پیکربندی پروژهٔ در جریان: پایه‌ریزی (baseline) دستور کار {wo.Code} از BOM منتشرشدهٔ محصول {product.Code} ساخته شد. برای دستور کار گرهٔ محصول در WBS موجود نیست؛ لطفاً پیوند پایه‌ریزی با WBS و برنامهٔ زمانی موجود را از مسیر تغییر تعیین کنید.",
                Reason = "پذیرش پیکربندی موجود (گذار به کنترل پیکربندی)؛ برنامهٔ قفل‌شده بدون تصمیم PMO ویرایش نمی‌شود و ارزش کسب‌شده دست نمی‌خورد.",
                CreateDate = now, ModifyDate = now, CreatorUserId = User.UserId, ModifierUserId = User.UserId,
            };
            cr.Number = await InventoryNumbers.NextNumberAsync(Uow, PcNumbers.ChangeRequest, "CR", DateTime.Today, ct);
            Uow.Set<PcChangeRequest>().Add(cr);
            await Uow.SaveChangesAsync(ct);
            baseline.ChangeRequestId = cr.Id;
            await Uow.SaveChangesAsync(ct);
            return $"برای دستور کار گرهٔ محصول در WBS نبود؛ WBS ساخته و برنامه ویرایش نشد. درخواست تغییر {cr.Number} (پیش‌نویس) برای تعیین تکلیف پیوند با برنامه ثبت شد.";
        }

        /// <summary>
        /// The lines of the released BOM for ONE product at every level: a line with a child BOM is followed by the child's lines (quantities multiplied down), headings are kept for the top level,
        /// and alternatives (options of another line) are not copied, so the project never orders both the clockwise and the counter-clockwise part.
        /// </summary>
        private async Task<List<PbProjectBomLine>> ExplodeAsync(int topBomId, int baselineId, int count, CancellationToken ct)
        {
            var loaded = new Dictionary<int, PbBomHeader>();
            var queue = new Queue<int>();
            queue.Enqueue(topBomId);
            while (queue.Count > 0 && loaded.Count < 200)
            {
                int id = queue.Dequeue();
                if (loaded.ContainsKey(id)) continue;
                PbBomHeader h = await Uow.Set<PbBomHeader>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
                if (h == null) continue;
                loaded[id] = h;
                foreach (PbBomLine l in h.Lines.Where(l => !l.Deleted && l.ChildBomId.HasValue)) queue.Enqueue(l.ChildBomId.Value);
            }
            var output = new List<PbProjectBomLine>();
            void Walk(PbBomHeader h, decimal multiplier, int depth, HashSet<int> path, PbProjectBomLine parent)
            {
                foreach (PbBomLine l in h.Lines.Where(x => !x.Deleted && x.AlternativeOfLineId == null).OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
                {
                    if (l.IsHeading && depth > 0) continue;
                    decimal local = l.IsHeading ? 0 : l.Qty;
                    decimal qty = local * multiplier;   // the quantity for one product (tree product); the line itself keeps the quantity per parent
                    var pl = new PbProjectBomLine
                    {
                        BaselineId = baselineId, SourceLineId = l.Id, SortOrder = output.Count + 1, ItemNumber = l.ItemNumber, IsHeading = l.IsHeading, AssemblyPartNumber = h.AssemblyPartNumber,
                        PartNumber = l.PartNumber, ItemId = l.ItemId, PartName = l.PartName, Qty = local, TotalQty = qty * count, Unit = l.Unit, Material = l.Material, MakeOrBuy = l.MakeOrBuy,
                        ParentLine = parent,
                    };
                    Stamp(pl, true);
                    output.Add(pl);
                    if (l.ChildBomId.HasValue && depth < 8 && !path.Contains(l.ChildBomId.Value) && loaded.TryGetValue(l.ChildBomId.Value, out PbBomHeader child))
                    {
                        var next = new HashSet<int>(path) { child.Id };
                        Walk(child, qty, depth + 1, next, pl);
                    }
                }
            }
            Walk(loaded[topBomId], 1m, 0, new HashSet<int> { topBomId }, null);
            return output;
        }

        public async Task<ResponseDto<PbBaselineDto>> GenerateWbsAsync(PbGenerateWbsDto dto, CancellationToken ct)
        {
            var r = await GenerateWbsCoreAsync(dto, ct);
            if (r.Success && _balance != null)
            {
                int wo = r.Data?.WorkOrderId ?? 0;
                if (wo > 0) await _balance.AfterChangeAsync(wo, "GenerateWbs", ct);
            }
            return r;
        }

        private async Task<ResponseDto<PbBaselineDto>> GenerateWbsCoreAsync(PbGenerateWbsDto dto, CancellationToken ct)
        {
            if (!InRole(PbRoles.Planning + ",ENGINEERING")) return ResponseDtoHelper.BadRequest<PbBaselineDto>(PbMessages.NotAllowed);
            if (dto == null || dto.WorkOrderId <= 0) return ResponseDtoHelper.BadRequest<PbBaselineDto>(PbMessages.InvalidData);
            PbProjectBaseline b = await Uow.Set<PbProjectBaseline>().FirstOrDefaultAsync(x => x.WorkOrderId == dto.WorkOrderId && !x.Deleted && x.IsCurrent, ct);
            if (b == null) return ResponseDtoHelper.NotFound<PbBaselineDto>("برای این دستور کار هنوز از بانک محصول پایه‌ریزی نشده است.");
            if (b.PcProductNodeId.HasValue) return ResponseDtoHelper.BadRequest<PbBaselineDto>("WBS کنترل پروژه برای این دستور کار قبلاً ساخته شده است.");
            WorkOrder wo = await Uow.Set<WorkOrder>().AsNoTracking().FirstAsync(w => w.Id == b.WorkOrderId, ct);
            if (await PcPlanLock.IsLockedAsync(Uow, wo.ContractId, ct))
            {
                var gate = await LockedPlanRequestAsync(dto.ChangeRequestId ?? b.ChangeRequestId, wo, b.Id, ct);
                if (gate.Error != null) return ResponseDtoHelper.BadRequest<PbBaselineDto>(gate.Error);
                b.ChangeRequestId = gate.Cr.Id;
                Stamp(b, false);
            }
            int id = b.Id;
            return await InTransactionAsync(async () =>
            {
                string note = await GeneratePcWbsAsync(id, ct);
                PbBaselineDto result = await BaselineDtoAsync(id, ct);
                result.WbsNote = note;
                return ResponseDtoHelper.Ok(result);
            }, r => r.Success, ct);
        }

        private async Task<int> WorkOrderOfBaselineAsync(int baselineId, CancellationToken ct) => await Uow.Set<PbProjectBaseline>().AsNoTracking().Where(x => x.Id == baselineId).Select(x => x.WorkOrderId).FirstOrDefaultAsync(ct);

        private static PcPhase? PhaseOf(string code) => code == "E" ? PcPhase.Engineering : code == "P" ? PcPhase.Procurement : code == "C" ? PcPhase.Construction : (PcPhase?)null;

        private static string UnitOf(PbWbsTemplateNode n)
        {
            if (n.PhaseCode == "E") return "ENGINEERING";
            if (n.PhaseCode == "P") return "COMMERCIAL";
            switch (n.ResourceClass)
            {
                case "ASSEMBLY": return "ASSEMBLER";
                case "QC": return "QC";
                default: return "CONSTRUCTION";
            }
        }

        private static string PhaseWord(string code) => code == "E" ? "مهندسی" : code == "P" ? "تدارک" : "ساخت";

        /// <summary>
        /// The project-control WBS (PcWbsNode, PcActivity, PcDependency) is the only project WBS: the approved template of the bank becomes the product node of the work order with its parts, the E / P / C activities of
        /// every part (durations rounded up to whole days), the dependencies of the template and the resource classes as responsible units. The template tables stay in the bank. Returns why nothing was generated, or null.
        /// </summary>
        private async Task<string> GeneratePcWbsAsync(int baselineId, CancellationToken ct)
        {
            bool procuredPercent = (await global::Services.NasbNiro.ProjectBalance.BalanceSettingKeys.LoadAsync(Uow, ct)).ProcuredPercent;
            PbProjectBaseline b = await Uow.Set<PbProjectBaseline>().FirstAsync(x => x.Id == baselineId, ct);
            if (!b.WbsTemplateId.HasValue) return "برای این محصول قالب WBS تاییدشده‌ای نیست؛ پس از تایید قالب، «ساخت WBS» را بزنید.";
            WorkOrder wo = await Uow.Set<WorkOrder>().AsNoTracking().FirstAsync(w => w.Id == b.WorkOrderId, ct);
            int contractId = wo.ContractId;
            if (await PcGates.RequireReviewAsync(Uow, contractId, ct) is string reviewMissing) return reviewMissing + " پس از پذیرش، «ساخت WBS» را بزنید.";
            if (await Uow.Set<PcWbsNode>().AnyAsync(n => n.ContractId == contractId && n.WorkOrderId == wo.Id && n.NodeType == PcWbsNodeType.Product && !n.Deleted, ct))
                return "WBS این دستور کار پیش‌تر در کنترل پروژه ساخته شده است؛ چیزی اضافه نشد.";

            List<PbWbsTemplateNode> template = await Uow.Set<PbWbsTemplateNode>().AsNoTracking().Where(n => n.TemplateId == b.WbsTemplateId.Value && !n.Deleted).OrderBy(n => n.Seq).ToListAsync(ct);
            List<PbWbsTemplateLink> links = await Uow.Set<PbWbsTemplateLink>().AsNoTracking().Where(l => l.TemplateId == b.WbsTemplateId.Value && !l.Deleted).ToListAsync(ct);
            if (template.Count == 0) return "قالب WBS گره‌ای ندارد.";

            DateTimeOffset now = DateTimeOffset.Now;
            int me = User.UserId;
            PcContractTerms terms = await PcContext.TermsAsync(Uow, contractId, false, ct);
            ContractEntity contract = await Uow.Set<ContractEntity>().AsNoTracking().FirstAsync(c => c.Id == contractId, ct);
            string projectCode = terms?.ProjectCode ?? contract.Code ?? ("C" + contract.Id);
            int counter = await Uow.Set<PcActivity>().Where(a => a.ContractId == contractId).MaxAsync(a => (int?)a.Sort, ct) ?? 0;
            var nodes = new List<PcWbsNode>();
            var acts = new List<PcActivity>();
            var deps = new List<PcDependency>();

            PcWbsNode root = await Uow.Set<PcWbsNode>().FirstOrDefaultAsync(n => n.ContractId == contractId && n.ParentId == null && n.NodeType == PcWbsNodeType.Project && !n.Deleted, ct);
            if (root == null)
            {
                root = new PcWbsNode { ContractId = contractId, Code = projectCode, Title = string.IsNullOrWhiteSpace(contract.Title) ? projectCode : contract.Title, NodeType = PcWbsNodeType.Project, IsGenerated = true, Sort = ++counter, CreateDate = now, CreatorUserId = me, ModifyDate = now, ModifierUserId = me };
                nodes.Add(root);
            }
            int productIndex = await Uow.Set<PcWbsNode>().CountAsync(n => n.ContractId == contractId && n.NodeType == PcWbsNodeType.Product && !n.Deleted, ct) + 1;
            string pcode = projectCode + "." + productIndex.ToString("00");
            PcWbsNode NewNode(PcWbsNode parent, string code, string title, PcWbsNodeType type, PcPhase? phase) =>
                Track(new PcWbsNode { ContractId = contractId, Parent = parent, Code = code, Title = title, NodeType = type, Phase = phase, WorkOrderId = wo.Id, IsGenerated = true, Sort = ++counter, CreateDate = now, CreatorUserId = me, ModifyDate = now, ModifierUserId = me });
            PcWbsNode Track(PcWbsNode n) { nodes.Add(n); return n; }
            PcWbsNode product = NewNode(root, pcode, wo.Title ?? wo.Code, PcWbsNodeType.Product, null);

            var children = template.GroupBy(n => n.ParentId ?? 0).ToDictionary(g => g.Key, g => g.OrderBy(n => n.Seq).ToList());
            var childCount = new Dictionary<PcWbsNode, int>();
            var leaves = new Dictionary<int, List<PcActivity>>();
            int seqNo = 0;
            List<PcActivity> Build(PbWbsTemplateNode n, PcWbsNode parent)
            {
                children.TryGetValue(n.Id, out List<PbWbsTemplateNode> kids);
                if (n.Kind == PbWbsNodeKind.Summary || (kids != null && kids.Count > 0))
                {
                    childCount[parent] = childCount.TryGetValue(parent, out int c) ? c + 1 : 1;
                    bool isPart = kids != null && kids.Any(k => k.PhaseCode != null);
                    PcWbsNode node = NewNode(parent, parent.Code + "." + childCount[parent].ToString("00"), n.Title, isPart ? PcWbsNodeType.Part : PcWbsNodeType.WorkPackage, PhaseOf(n.PhaseCode));
                    var all = new List<PcActivity>();
                    if (kids != null) foreach (PbWbsTemplateNode k in kids) all.AddRange(Build(k, node));
                    leaves[n.Id] = all;
                    return all;
                }
                string title = n.PhaseCode != null && parent.NodeType != PcWbsNodeType.Product ? parent.Title + " — " + PhaseWord(n.PhaseCode) : n.Title;
                int days = n.Kind == PbWbsNodeKind.Milestone ? 0 : (int)Math.Max(1m, Math.Ceiling(n.DurationDays));
                var act = new PcActivity
                {
                    ContractId = contractId, WbsNode = parent, Code = pcode + "-" + (++seqNo).ToString("000"), Title = title.Length > 300 ? title.Substring(0, 300) : title, SourceType = PcSourceType.Manual,
                    WorkOrderId = wo.Id, DurationDays = days, Phase = PhaseOf(n.PhaseCode), ResponsibleUnit = UnitOf(n), ProgressMethod = n.PhaseCode == "P" && procuredPercent ? PcProgressMethod.MaterialBalance : PcProgressMethod.Manual, Status = PcActivityStatus.NotStarted,
                    Sort = ++counter, CreateDate = now, CreatorUserId = me, ModifyDate = now, ModifierUserId = me,
                };
                acts.Add(act);
                leaves[n.Id] = new List<PcActivity> { act };
                return leaves[n.Id];
            }
            if (children.TryGetValue(0, out List<PbWbsTemplateNode> top)) foreach (PbWbsTemplateNode n in top) Build(n, product);

            foreach (PbWbsTemplateLink l in links)
            {
                if (!leaves.TryGetValue(l.FromNodeId, out List<PcActivity> from) || !leaves.TryGetValue(l.ToNodeId, out List<PcActivity> to)) continue;
                if (from.Count == 0 || to.Count == 0 || from.Intersect(to).Any() || (long)from.Count * to.Count > 400) continue;
                PcDependencyType type = l.LinkType == PbLinkType.StartToStart ? PcDependencyType.SS : l.LinkType == PbLinkType.FinishToFinish ? PcDependencyType.FF : l.LinkType == PbLinkType.StartToFinish ? PcDependencyType.SF : PcDependencyType.FS;
                foreach (PcActivity pred in from)
                    foreach (PcActivity succ in to)
                        if (!deps.Any(d => ReferenceEquals(d.Predecessor, pred) && ReferenceEquals(d.Successor, succ)))
                            deps.Add(new PcDependency { ContractId = contractId, Predecessor = pred, Successor = succ, Type = type, LagDays = (int)Math.Round(l.LagDays), Origin = PcDependencyOrigin.Manual });
            }

            var allNodes = (await Uow.Set<PcWbsNode>().Where(n => n.ContractId == contractId && !n.Deleted).ToListAsync(ct)).Concat(nodes).Distinct().ToList();
            var allActs = (await Uow.Set<PcActivity>().Where(a => a.ContractId == contractId && !a.Deleted).ToListAsync(ct)).Concat(acts).Distinct().ToList();
            PcScheduleService.ApplyWeights(allNodes.Where(n => n.IsGenerated).ToList(), allActs.Where(a => a.SourceType != PcSourceType.Manual || acts.Contains(a)).ToList());

            Uow.Set<PcWbsNode>().AddRange(nodes.Where(n => Uow.Entry(n).State == EntityState.Detached));
            Uow.Set<PcActivity>().AddRange(acts);
            Uow.Set<PcDependency>().AddRange(deps);
            await Uow.SaveChangesAsync(ct);
            b.PcProductNodeId = product.Id;
            Stamp(b, false);
            await Uow.SaveChangesAsync(ct);
            await _project.MarkStepAsync(contractId, PcInitiationStep.WbsDrafted, me, false, "WBS از بانک محصول", ct);
            await Uow.SaveChangesAsync(ct);
            return null;
        }

        public async Task<ResponseDto<PbBaselineDto>> GetBaselineAsync(int workOrderId, CancellationToken ct)
        {
            int? id = await Uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => b.WorkOrderId == workOrderId && !b.Deleted && b.IsCurrent).Select(b => (int?)b.Id).FirstOrDefaultAsync(ct);
            if (id == null) return ResponseDtoHelper.NotFound<PbBaselineDto>("برای این دستور کار هنوز از بانک محصول پایه‌ریزی نشده است.");
            return ResponseDtoHelper.Ok(await BaselineDtoAsync(id.Value, ct));
        }

        private async Task<bool> PcBaselineCurrentAsync(int? contractId, CancellationToken ct) =>
            contractId.HasValue && await PcPlanLock.IsLockedAsync(Uow, contractId.Value, ct);

        private async Task<PbBaselineDto> BaselineDtoAsync(int id, CancellationToken ct)
        {
            PbProjectBaseline b = await Uow.Set<PbProjectBaseline>().AsNoTracking().FirstAsync(x => x.Id == id, ct);
            PbProduct p = await Uow.Set<PbProduct>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == b.ProductId, ct);
            bool pcCurrent = await PcBaselineCurrentAsync(b.ContractId, ct);
            var dto = new PbBaselineDto
            {
                Id = b.Id, WorkOrderId = b.WorkOrderId, ContractId = b.ContractId, ProductId = b.ProductId, ProductCode = p?.Code, BomHeaderId = b.BomHeaderId,
                SourceBomCode = b.SourceBomCode, SourceBomRevision = b.SourceBomRevision, WbsTemplateId = b.WbsTemplateId, WbsTemplateVersion = b.WbsTemplateVersion,
                Count = b.Count, CopiedAt = b.CopiedAt, PcBaselineCurrent = pcCurrent, PcProductNodeId = b.PcProductNodeId, ChangeRequestId = b.ChangeRequestId,
            };
            dto.Lines = await Uow.Set<PbProjectBomLine>().AsNoTracking().Where(l => l.BaselineId == id && !l.Deleted).OrderBy(l => l.SortOrder)
                .Select(l => new PbProjectLineDto
                {
                    Id = l.Id, SortOrder = l.SortOrder, ItemNumber = l.ItemNumber, IsHeading = l.IsHeading, AssemblyPartNumber = l.AssemblyPartNumber, PartNumber = l.PartNumber, PartName = l.PartName, Qty = l.Qty, TotalQty = l.TotalQty,
                    Unit = l.Unit, Material = l.Material, MakeOrBuy = l.MakeOrBuy, IsRemoved = l.IsRemoved, ChangedByChangeId = l.ChangedByChangeId,
                }).ToListAsync(ct);
            dto.Routings = await Uow.Set<PbProjectRouting>().AsNoTracking().Where(r => r.BaselineId == id && !r.Deleted)
                .Join(Uow.Set<PbOpcRouting>().AsNoTracking(), r => r.OpcRoutingId, o => o.Id, (r, o) => new PbOpcListDto
                {
                    Id = o.Id, ProductId = o.ProductId, PartNumber = o.PartNumber, Title = o.Title, Revision = o.Revision, Status = o.Status, StepCount = o.Steps.Count(s => !s.Deleted), PublishedToMesAt = o.PublishedToMesAt,
                }).ToListAsync(ct);
            if (b.PcProductNodeId.HasValue)
            {
                var nodes = await Uow.Set<PcWbsNode>().AsNoTracking().Where(n => n.ContractId == b.ContractId && n.WorkOrderId == b.WorkOrderId && !n.Deleted).OrderBy(n => n.Sort).ToListAsync(ct);
                var activities = await Uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == b.ContractId && a.WorkOrderId == b.WorkOrderId && !a.Deleted).OrderBy(a => a.Sort).ToListAsync(ct);
                foreach (PcWbsNode n in nodes)
                    dto.PcWbs.Add(new PbPcWbsRowDto { Key = "n" + n.Id, ParentKey = n.ParentId.HasValue && n.Id != b.PcProductNodeId ? "n" + n.ParentId : null, Code = n.Code, Title = n.Title, IsActivity = false, Phase = n.Phase?.ToString() });
                foreach (PcActivity a in activities)
                    dto.PcWbs.Add(new PbPcWbsRowDto { Key = "a" + a.Id, ParentKey = a.WbsNodeId.HasValue ? "n" + a.WbsNodeId : null, Code = a.Code, Title = a.Title, IsActivity = true, Phase = a.Phase?.ToString(), DurationDays = a.DurationDays, ResponsibleUnit = a.ResponsibleUnit });
                dto.PcDependencies = await Uow.Set<PcDependency>().AsNoTracking().CountAsync(d => d.ContractId == b.ContractId && d.Predecessor.WorkOrderId == b.WorkOrderId, ct);
            }
            List<PbProjectBomChange> changes = await Uow.Set<PbProjectBomChange>().AsNoTracking().Where(c => c.BaselineId == id && !c.Deleted).OrderByDescending(c => c.Id).ToListAsync(ct);
            dto.Changes = changes.Select(c => ChangeDto(c, dto.Lines, pcCurrent)).ToList();
            return dto;
        }

        // -------------------------------------------------------------------------------- deviations

        private PbChangeDto ChangeDto(PbProjectBomChange c, List<PbProjectLineDto> lines, bool pcCurrent)
        {
            PbProjectLineDto target = c.TargetLineId.HasValue ? lines?.FirstOrDefault(l => l.Id == c.TargetLineId.Value) : null;
            int me = User.UserId;
            bool canEng = c.Status == PbChangeStatus.Submitted && !c.EngineeringApprovedByUserId.HasValue && c.CreatorUserId != me && !IsAi && InRole(PbRoles.Engineering);
            bool canPlan = c.Status == PbChangeStatus.Submitted && !c.PlanningApprovedByUserId.HasValue && c.CreatorUserId != me && c.EngineeringApprovedByUserId != me && !IsAi && InRole(PbRoles.Planning);
            return new PbChangeDto
            {
                Id = c.Id, BaselineId = c.BaselineId, Number = c.Number, Kind = c.Kind, TargetLineId = c.TargetLineId, TargetPartNumber = target?.PartNumber, PartNumber = c.PartNumber, PartName = c.PartName,
                NewQty = c.NewQty, Unit = c.Unit, Material = c.Material, MakeOrBuy = c.MakeOrBuy, Reason = c.Reason, Status = c.Status, RequesterUserId = c.CreatorUserId,
                EngineeringApprovedByUserId = c.EngineeringApprovedByUserId, PlanningApprovedByUserId = c.PlanningApprovedByUserId, RejectedByUserId = c.RejectedByUserId, RejectReason = c.RejectReason,
                ChangeRequestId = c.ChangeRequestId, AppliedAt = c.AppliedAt, NeedsChangeRequest = pcCurrent,
                CanApproveEngineering = canEng, CanApprovePlanning = canPlan,
            };
        }

        private async Task<ResponseDto<PbChangeDto>> ChangeResultAsync(PbProjectBomChange c, CancellationToken ct)
        {
            PbProjectBaseline b = await Uow.Set<PbProjectBaseline>().AsNoTracking().FirstAsync(x => x.Id == c.BaselineId, ct);
            return ResponseDtoHelper.Ok(ChangeDto(c, null, await PcBaselineCurrentAsync(b.ContractId, ct)));
        }

        public async Task<ResponseDto<PbChangeDto>> SaveChangeAsync(SavePbChangeDto dto, CancellationToken ct)
        {
            if (!InRole(PbRoles.Requesters)) return ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.NotAllowed);
            if (dto == null || dto.BaselineId <= 0) return ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.InvalidData);
            PbProjectBaseline b = await Uow.Set<PbProjectBaseline>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.BaselineId && !x.Deleted, ct);
            if (b == null) return ResponseDtoHelper.NotFound<PbChangeDto>(PbMessages.NotFound);
            string reason = PbText.Clean(dto.Reason);
            if (reason == null) return ResponseDtoHelper.BadRequest<PbChangeDto>("دلیل انحراف را بنویسید.");
            string pn = PbText.PartNumber(dto.PartNumber);
            PbProjectBomLine target = null;
            if (dto.Kind != PbChangeKind.Add)
            {
                if (!dto.TargetLineId.HasValue) return ResponseDtoHelper.BadRequest<PbChangeDto>("ردیف هدف را انتخاب کنید.");
                target = await Uow.Set<PbProjectBomLine>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == dto.TargetLineId.Value && l.BaselineId == b.Id && !l.Deleted && !l.IsHeading, ct);
                if (target == null) return ResponseDtoHelper.BadRequest<PbChangeDto>("ردیف هدف در این پایه پیدا نشد.");
                if (target.IsRemoved) return ResponseDtoHelper.BadRequest<PbChangeDto>("این ردیف قبلا حذف شده است.");
            }
            if ((dto.Kind == PbChangeKind.Add || dto.Kind == PbChangeKind.Replace) && pn == null) return ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.PartNumberRequired);
            if ((dto.Kind == PbChangeKind.Add || dto.Kind == PbChangeKind.Quantity) && (!dto.NewQty.HasValue || dto.NewQty.Value <= 0)) return ResponseDtoHelper.BadRequest<PbChangeDto>("تعداد باید بیشتر از صفر باشد.");
            if (dto.NewQty.HasValue && dto.NewQty.Value <= 0) return ResponseDtoHelper.BadRequest<PbChangeDto>("تعداد باید بیشتر از صفر باشد (برای حذف، نوع «حذف» را بزنید).");
            if (dto.Kind == PbChangeKind.Remove && dto.NewQty.HasValue) return ResponseDtoHelper.BadRequest<PbChangeDto>("برای حذف تعداد جدید نمی‌آید.");

            PbProjectBomChange c;
            if (dto.Id.HasValue)
            {
                c = await Uow.Set<PbProjectBomChange>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (c == null) return ResponseDtoHelper.NotFound<PbChangeDto>(PbMessages.NotFound);
                if (c.Status != PbChangeStatus.Draft) return ResponseDtoHelper.BadRequest<PbChangeDto>("فقط انحراف پیش‌نویس قابل ویرایش است.");
                if (c.CreatorUserId != User.UserId) return ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.NotAllowed);
                if (c.BaselineId != b.Id) return ResponseDtoHelper.BadRequest<PbChangeDto>("پایهٔ یک انحراف تغییر نمی‌کند.");
            }
            else
            {
                c = new PbProjectBomChange { BaselineId = b.Id, Number = await NextNumberAsync(0, ct), Status = PbChangeStatus.Draft };
                Uow.Set<PbProjectBomChange>().Add(c);
            }
            c.Kind = dto.Kind; c.TargetLineId = dto.TargetLineId; c.PartNumber = pn; c.PartName = PbText.Clean(dto.PartName) ?? target?.PartName; c.NewQty = dto.NewQty;
            c.Unit = PbText.Clean(dto.Unit) ?? target?.Unit; c.Material = PbText.Clean(dto.Material); c.MakeOrBuy = dto.MakeOrBuy; c.Reason = reason;
            bool isNew = Uow.Entry(c).State == EntityState.Added;
            Stamp(c, isNew);
            for (int attempt = 0; ; attempt++)
            {
                try { await Uow.SaveChangesAsync(ct); break; }
                catch (DbUpdateException) when (isNew && attempt < 4)
                {
                    // another request took the same number at the same time: the next free one is used
                    c.Number = await NextNumberAsync(attempt + 1, ct);
                }
            }
            return await ChangeResultAsync(c, ct);
        }

        /// <summary>PBC-year-nnnn: one more than the highest number of the year (a count would repeat a number after a deletion).</summary>
        private async Task<string> NextNumberAsync(int skip, CancellationToken ct)
        {
            string prefix = "PBC-" + DateTime.Now.Year + "-";
            List<string> numbers = await Uow.Set<PbProjectBomChange>().AsNoTracking().Where(x => x.Number.StartsWith(prefix)).Select(x => x.Number).ToListAsync(ct);
            int max = numbers.Select(x => int.TryParse(x.Substring(prefix.Length), out int v) ? v : 0).DefaultIfEmpty(0).Max();
            return prefix + (max + 1 + skip).ToString("0000");
        }

        private async Task<(PbProjectBomChange C, ResponseDto<PbChangeDto> Error)> LoadChangeAsync(PbChangeActionDto dto, CancellationToken ct)
        {
            if (dto == null || dto.Id <= 0) return (null, ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.InvalidData));
            PbProjectBomChange c = await Uow.Set<PbProjectBomChange>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            return c == null ? (null, ResponseDtoHelper.NotFound<PbChangeDto>(PbMessages.NotFound)) : (c, null);
        }

        public async Task<ResponseDto<PbChangeDto>> SubmitChangeAsync(PbChangeActionDto dto, CancellationToken ct)
        {
            var (c, err) = await LoadChangeAsync(dto, ct);
            if (err != null) return err;
            if (c.Status != PbChangeStatus.Draft) return ResponseDtoHelper.BadRequest<PbChangeDto>("فقط انحراف پیش‌نویس ارسال می‌شود.");
            if (c.CreatorUserId != User.UserId) return ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.NotAllowed);
            c.Status = PbChangeStatus.Submitted;
            Stamp(c, false);
            await Uow.SaveChangesAsync(ct);
            return await ChangeResultAsync(c, ct);
        }

        public Task<ResponseDto<PbChangeDto>> ApproveChangeEngineeringAsync(PbChangeActionDto dto, CancellationToken ct) => ApproveChangeAsync(dto, true, ct);
        public Task<ResponseDto<PbChangeDto>> ApproveChangePlanningAsync(PbChangeActionDto dto, CancellationToken ct) => ApproveChangeAsync(dto, false, ct);

        private async Task<ResponseDto<PbChangeDto>> ApproveChangeAsync(PbChangeActionDto dto, bool engineering, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.AiNeverApproves);
            var (c, err) = await LoadChangeAsync(dto, ct);
            if (err != null) return err;
            if (!InRole(engineering ? PbRoles.Engineering : PbRoles.Planning)) return ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.NotAllowed);
            if (c.Status != PbChangeStatus.Submitted) return ResponseDtoHelper.BadRequest<PbChangeDto>("انحراف باید در وضعیت «ارسال‌شده» باشد.");
            int me = User.UserId;
            if (c.CreatorUserId == me) return ResponseDtoHelper.BadRequest<PbChangeDto>("درخواست‌کننده انحراف را تایید نمی‌کند.");
            if (engineering)
            {
                if (c.EngineeringApprovedByUserId.HasValue) return ResponseDtoHelper.BadRequest<PbChangeDto>("تایید مهندسی قبلا ثبت شده است.");
                if (c.PlanningApprovedByUserId == me) return ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.SameUser);
                c.EngineeringApprovedByUserId = me; c.EngineeringApprovedAt = DateTimeOffset.Now;
            }
            else
            {
                if (c.PlanningApprovedByUserId.HasValue) return ResponseDtoHelper.BadRequest<PbChangeDto>("تایید برنامه‌ریزی قبلا ثبت شده است.");
                if (c.EngineeringApprovedByUserId == me) return ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.SameUser);
                c.PlanningApprovedByUserId = me; c.PlanningApprovedAt = DateTimeOffset.Now;
            }
            if (c.EngineeringApprovedByUserId.HasValue && c.PlanningApprovedByUserId.HasValue) c.Status = PbChangeStatus.Approved;
            Stamp(c, false);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return ResponseDtoHelper.BadRequest<PbChangeDto>(conflict);
            return await ChangeResultAsync(c, ct);
        }

        public async Task<ResponseDto<PbChangeDto>> RejectChangeAsync(PbChangeActionDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.AiNeverApproves);
            var (c, err) = await LoadChangeAsync(dto, ct);
            if (err != null) return err;
            if (!InRole(PbRoles.Engineering + "," + PbRoles.Planning)) return ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.NotAllowed);
            if (c.Status != PbChangeStatus.Submitted) return ResponseDtoHelper.BadRequest<PbChangeDto>("انحراف باید در وضعیت «ارسال‌شده» باشد.");
            if (c.CreatorUserId == User.UserId) return ResponseDtoHelper.BadRequest<PbChangeDto>("درخواست‌کننده انحراف را رد نمی‌کند؛ آن را لغو کند.");
            string note = PbText.Clean(dto.Note);
            if (note == null) return ResponseDtoHelper.BadRequest<PbChangeDto>("دلیل رد را بنویسید.");
            c.Status = PbChangeStatus.Rejected; c.RejectedByUserId = User.UserId; c.RejectReason = note;
            Stamp(c, false);
            await Uow.SaveChangesAsync(ct);
            return await ChangeResultAsync(c, ct);
        }

        public async Task<ResponseDto<PbChangeDto>> CancelChangeAsync(PbChangeActionDto dto, CancellationToken ct)
        {
            var (c, err) = await LoadChangeAsync(dto, ct);
            if (err != null) return err;
            if (c.Status != PbChangeStatus.Draft && c.Status != PbChangeStatus.Submitted) return ResponseDtoHelper.BadRequest<PbChangeDto>("این انحراف دیگر قابل لغو نیست.");
            if (c.CreatorUserId != User.UserId) return ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.NotAllowed);
            c.Status = PbChangeStatus.Cancelled;
            Stamp(c, false);
            await Uow.SaveChangesAsync(ct);
            return await ChangeResultAsync(c, ct);
        }

        public async Task<ResponseDto<PbChangeDto>> ApplyChangeAsync(PbChangeActionDto dto, CancellationToken ct)
        {
            var r = await ApplyChangeCoreAsync(dto, ct);
            if (r.Success && _balance != null)
            {
                int wo = r.Data == null ? 0 : await WorkOrderOfBaselineAsync(r.Data.BaselineId, ct);
                if (wo > 0) await _balance.AfterChangeAsync(wo, "Deviation", ct);
            }
            return r;
        }

        private async Task<ResponseDto<PbChangeDto>> ApplyChangeCoreAsync(PbChangeActionDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.AiNeverApproves);
            var (c, err) = await LoadChangeAsync(dto, ct);
            if (err != null) return err;
            if (!InRole(PbRoles.Engineering + "," + PbRoles.Planning)) return ResponseDtoHelper.BadRequest<PbChangeDto>(PbMessages.NotAllowed);
            if (c.Status != PbChangeStatus.Approved) return ResponseDtoHelper.BadRequest<PbChangeDto>("فقط انحراف تاییدشده اعمال می‌شود.");
            PbProjectBaseline b = await Uow.Set<PbProjectBaseline>().FirstAsync(x => x.Id == c.BaselineId, ct);

            if (await PcBaselineCurrentAsync(b.ContractId, ct))
            {
                // After the PC baseline is current a deviation is a scope change: it needs the board's approved change request of the same contract.
                int? crId = dto.ChangeRequestId ?? c.ChangeRequestId;
                if (!crId.HasValue) return ResponseDtoHelper.BadRequest<PbChangeDto>("خط پایهٔ کنترل پروژه فعال است؛ این انحراف فقط با درخواست تغییر تاییدشده اعمال می‌شود. شمارهٔ درخواست تغییر را وارد کنید.");
                PcChangeRequest cr = await Uow.Set<PcChangeRequest>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == crId.Value && !x.Deleted, ct);
                if (cr == null || cr.ContractId != b.ContractId) return ResponseDtoHelper.BadRequest<PbChangeDto>("این درخواست تغییر برای همین قرارداد پیدا نشد.");
                if (!IsDecidedApproval(cr)) return ResponseDtoHelper.BadRequest<PbChangeDto>("درخواست تغییر هنوز در هیئت تایید نشده است.");
                // one request authorises only the deviations it lists (by their numbers); a deviation is applied once, so each listed number is used once
                if (!Mentions(cr, c.Number)) return ResponseDtoHelper.BadRequest<PbChangeDto>("درخواست تغییر باید شمارهٔ این انحراف (" + c.Number + ") را در شرح یا دلیل خود بیاورد؛ هر درخواست فقط انحراف‌های نام‌برده را مجاز می‌کند.");
                c.ChangeRequestId = crId;
            }

            List<PbProjectBomLine> lines = await Uow.Set<PbProjectBomLine>().Where(l => l.BaselineId == b.Id && !l.Deleted).ToListAsync(ct);
            PbProjectBomLine target = c.TargetLineId.HasValue ? lines.FirstOrDefault(l => l.Id == c.TargetLineId.Value) : null;
            if (c.Kind != PbChangeKind.Add && (target == null || target.IsRemoved)) return ResponseDtoHelper.BadRequest<PbChangeDto>("ردیف هدف دیگر در پایه وجود ندارد.");
            int count = Math.Max(1, b.Count);
            switch (c.Kind)
            {
                case PbChangeKind.Remove:
                    target.IsRemoved = true; target.ChangedByChangeId = c.Id; Stamp(target, false);
                    DropDescendants(lines, target, c.Id);
                    break;
                case PbChangeKind.Quantity:
                    // the new quantity is per parent assembly: the parts of a changed assembly follow it, and a part keeps the multiplier of its parent
                    target.Qty = c.NewQty.Value; target.ChangedByChangeId = c.Id; Stamp(target, false);
                    break;
                case PbChangeKind.Replace:
                    target.IsRemoved = true; target.ChangedByChangeId = c.Id; Stamp(target, false);
                    DropDescendants(lines, target, c.Id);
                    {
                        PbProjectBomLine added = AddProjectLine(b, c, target.SortOrder + 1, c.NewQty ?? target.Qty, target.ItemNumber, count, target.ParentLineId, target.AssemblyPartNumber);
                        PbProjectBomLine up = target.ParentLineId.HasValue ? lines.FirstOrDefault(x => x.Id == target.ParentLineId.Value) : null;
                        if (up != null && up.Qty > 0) added.TotalQty = added.Qty * (up.TotalQty / up.Qty);
                    }
                    break;
                case PbChangeKind.Add:
                    {
                        // the line belongs to the top assembly of the baseline (the assembly part number of the lines that have no parent line); drafting finds it there
                        string top = lines.Where(l => !l.ParentLineId.HasValue && l.AssemblyPartNumber != null).OrderBy(l => l.SortOrder).Select(l => l.AssemblyPartNumber).FirstOrDefault()
                            ?? await Uow.Set<PbBomHeader>().AsNoTracking().Where(h => h.Id == b.BomHeaderId).Select(h => h.AssemblyPartNumber).FirstOrDefaultAsync(ct);
                        AddProjectLine(b, c, (lines.Count == 0 ? 0 : lines.Max(l => l.SortOrder)) + 1, c.NewQty.Value, null, count, null, top);
                    }
                    break;
            }
            RefreshTotals(lines, count);
            c.Status = PbChangeStatus.Applied; c.AppliedAt = DateTimeOffset.Now; c.AppliedByUserId = User.UserId;
            Stamp(c, false);
            string saved = await TrySaveAsync(ct);
            if (saved != null) return ResponseDtoHelper.BadRequest<PbChangeDto>(saved);
            return await ChangeResultAsync(c, ct);
        }

        /// <summary>PB2 (H2): a removed or replaced assembly takes all its parts with it.</summary>
        private void DropDescendants(List<PbProjectBomLine> lines, PbProjectBomLine root, int changeId)
        {
            var queue = new Queue<int>();
            queue.Enqueue(root.Id);
            var seen = new HashSet<int>();
            while (queue.Count > 0)
            {
                int parent = queue.Dequeue();
                if (!seen.Add(parent)) continue;
                foreach (PbProjectBomLine ch in lines.Where(l => l.ParentLineId == parent && !l.IsRemoved))
                {
                    ch.IsRemoved = true; ch.ChangedByChangeId = changeId; Stamp(ch, false);
                    queue.Enqueue(ch.Id);
                }
            }
        }

        /// <summary>PB2 (H2): the quantity of every line for the whole work order, from the tree (quantity per parent x the quantities of its ancestors x the count).</summary>
        private void RefreshTotals(List<PbProjectBomLine> lines, int count)
        {
            Dictionary<int, decimal> totals = PbBomTree.Totals(lines, count, null);
            foreach (PbProjectBomLine l in lines.Where(l => !l.IsRemoved && !l.IsHeading))
                if (totals.TryGetValue(l.Id, out decimal t) && l.TotalQty != t) { l.TotalQty = t; Stamp(l, false); }
        }

        private PbProjectBomLine AddProjectLine(PbProjectBaseline b, PbProjectBomChange c, int sort, decimal qty, int? itemNumber, int count, int? parentLineId, string assemblyPartNumber)
        {
            var l = new PbProjectBomLine
            {
                BaselineId = b.Id, SortOrder = sort, ItemNumber = itemNumber, AssemblyPartNumber = assemblyPartNumber, PartNumber = c.PartNumber, PartName = c.PartName ?? c.PartNumber, Qty = qty, TotalQty = qty * count,
                Unit = c.Unit ?? "عدد", Material = c.Material, MakeOrBuy = c.MakeOrBuy ?? PbMakeOrBuy.Make, ChangedByChangeId = c.Id, ParentLineId = parentLineId,
            };
            Stamp(l, true);
            Uow.Set<PbProjectBomLine>().Add(l);
            return l;
        }
    }
}
