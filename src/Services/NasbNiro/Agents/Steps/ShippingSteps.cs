using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // Stations 10 (packing and shipping) and 11 (delivery, handover, commissioning). The shipping agent drafts the shipment as soon as the work order has a released quality release note: the lines come from the
    // work order (the serial units of the release note, else its items and quantities) and the packing list is written from those lines. People pack (physical), check the packing (QC, not the packer), give the
    // shipping permit (planning, not the packer), and do the physical work of the warehouse, the carrier and the customer. What exists only outside the system (the waybill, the signed receipt, the signed minutes
    // of the handover) is typed by the person who holds it, as part of the physical task: no separate Input task is needed.

    public static class ShippingSteps
    {
        public static IServiceCollection AddShippingSteps(this IServiceCollection services)
        {
            services.AddScoped<IAgentStep, ShipDraftStep>();
            services.AddScoped<IAgentStep, ShipPackStep>();
            services.AddScoped<IAgentStep, ShipCheckStep>();
            services.AddScoped<IAgentStep, ShipReleaseStep>();
            services.AddScoped<IAgentStep, ShipDeliveryDocStep>();
            services.AddScoped<IAgentStep, ShipShipStep>();
            services.AddScoped<IAgentStep, ShipDeliverStep>();
            services.AddScoped<IAgentStep, ShipHandoverStep>();
            services.AddScoped<IAgentStep, ShipCommissionStep>();
            return services;
        }
    }

    /// <summary>The roles of the shipment actions, copied from <c>PcReportService.ShipmentActionAsync</c> (a change of the service must change them here too; the tests compare them with the actions).</summary>
    internal static class ShipRoles
    {
        /// <summary>SaveShipmentAsync, "pack" and "ship".</summary>
        public const string Pack = "PLANNING,PMO,WAREHOUSE,WHSUPERVISOR";

        /// <summary>"check-packing".</summary>
        public const string Check = "QC,QA";

        /// <summary>"release" (the shipping permit).</summary>
        public const string Release = "PLANNING,PMO";

        /// <summary>"deliver".</summary>
        public const string Deliver = "PLANNING,PMO,COMMERCIAL,WAREHOUSE,WHSUPERVISOR";

        /// <summary>"handover".</summary>
        public const string Handover = "PLANNING,PMO,COMMERCIAL";

        /// <summary>"commission".</summary>
        public const string Commission = "PLANNING,PMO,COMMERCIAL,SERVICE";
    }

    internal abstract class ShipStep : ProcStep
    {
        public override string AgentCode => AgentCodes.Ship;

        protected const string ShipmentsLink = "/project-control/shipments";

        protected static IQueryable<int> LiveIds(IUnitOfWork uow, DateTime today) => ConFacts.Live(uow, today).Select(c => c.Id);

        protected static AgentWorkItem ShipItem(int id, string number, int contractId, int workOrderId, string summary, string roles) =>
            Item("PcShipment", id, number, contractId, workOrderId, summary, roles);

        /// <summary>The shipments of live contracts in one status that also match <paramref name="extra"/>, oldest first.</summary>
        protected static async Task<List<AgentWorkItem>> ByStatusAsync(IUnitOfWork uow, DateTime today, PcShipmentStatus status, Expression<Func<PcShipment, bool>> extra, string summary, string roles, CancellationToken ct)
        {
            IQueryable<int> live = LiveIds(uow, today);
            IQueryable<PcShipment> q = uow.Set<PcShipment>().AsNoTracking().Where(s => !s.Deleted && s.Status == status && live.Contains(s.ContractId));
            if (extra != null) q = q.Where(extra);
            var rows = await q.OrderBy(s => s.Id).Take(ProcStepCommon.Cap).Select(s => new { s.Id, s.Number, s.ContractId, s.WorkOrderId }).ToListAsync(ct);
            return rows.Select(s => ShipItem(s.Id, s.Number, s.ContractId, s.WorkOrderId, summary, roles)).ToList();
        }

        /// <summary>
        /// The warehouse documents "delivery to the customer" of the contracts of the given shipments: for each shipment the confirmed documents that the service would accept at "ship" (same contract, the same
        /// work order or none, not tied to another shipment) and whether a draft one exists.
        /// </summary>
        protected static async Task<Dictionary<int, (bool Confirmed, bool Draft)>> DeliveryDocsAsync(IUnitOfWork uow, ICollection<(int Id, int ContractId, int WorkOrderId)> shipments, CancellationToken ct)
        {
            var result = shipments.ToDictionary(s => s.Id, s => (false, false));
            if (shipments.Count == 0) return result;
            int[] contracts = shipments.Select(s => s.ContractId).Distinct().ToArray();
            var docs = await uow.Set<InventoryDocument>().AsNoTracking().Where(d => !d.Deleted && d.Type == InventoryDocumentType.CustomerDelivery && d.ContractId != null && contracts.Contains(d.ContractId.Value)
                    && (d.Status == InventoryDocumentStatus.Confirmed || d.Status == InventoryDocumentStatus.Draft))
                .OrderBy(d => d.Id).Take(ProcStepCommon.Cap * 2).Select(d => new { d.Id, d.ContractId, d.WorkOrderId, d.Status }).ToListAsync(ct);
            HashSet<int> taken = (await uow.Set<PcShipment>().AsNoTracking().Where(s => !s.Deleted && s.InventoryDocumentId != null).Select(s => s.InventoryDocumentId.Value).ToListAsync(ct)).ToHashSet();
            foreach (var s in shipments)
            {
                var mine = docs.Where(d => d.ContractId == s.ContractId && (!d.WorkOrderId.HasValue || d.WorkOrderId.Value == s.WorkOrderId) && !taken.Contains(d.Id)).ToList();
                result[s.Id] = (mine.Any(d => d.Status == InventoryDocumentStatus.Confirmed), mine.Any(d => d.Status == InventoryDocumentStatus.Draft));
            }
            return result;
        }

        protected static string Qty(decimal q) => q.ToString("0.####", CultureInfo.InvariantCulture);
    }

    /// <summary>SHIP.DRAFT (station 10): a work order whose quality release note is released and that has no shipment gets one, with the lines derived from the release note and the work order and a packing list written from the lines.</summary>
    internal sealed class ShipDraftStep : ShipStep
    {
        public override string Code => "SHIP.DRAFT";
        public override int Station => 10;
        public override string Title => "ایجنت ارسال: ساخت محموله با اقلام و فهرست بسته‌بندی پس از ترخیص کیفی";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => ShipRoles.Pack;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> live = LiveIds(uow, today);
            IQueryable<int> released = uow.Set<ReleaseNote>().AsNoTracking().Where(n => !n.Deleted && n.Status == ReleaseNoteStatus.Released).Select(n => n.WorkOrderId);
            IQueryable<int> shipped = uow.Set<PcShipment>().AsNoTracking().Where(s => !s.Deleted).Select(s => s.WorkOrderId);
            var rows = await uow.Set<WorkOrder>().AsNoTracking().Where(w => !w.Deleted && live.Contains(w.ContractId) && released.Contains(w.Id) && !shipped.Contains(w.Id))
                .OrderBy(w => w.Id).Take(ProcStepCommon.Cap).Select(w => new { w.Id, w.Code, w.ContractId }).ToListAsync(ct);
            return rows.Select(w => Item("WorkOrder", w.Id, w.Code, w.ContractId, w.Id, "برگه ترخیص کیفی صادر شده ولی محموله‌ای برای دستور کار ساخته نشده است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            int woId = item.TargetId;
            var wo = await uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == woId && !w.Deleted).Select(w => new { w.Id, w.Code, w.ContractId, w.Count }).FirstOrDefaultAsync(ct);
            if (wo == null) return AgentStepResult.Waiting("دستور کار پیدا نشد.");
            int noteId = await uow.Set<ReleaseNote>().AsNoTracking().Where(n => n.WorkOrderId == woId && !n.Deleted && n.Status == ReleaseNoteStatus.Released).OrderByDescending(n => n.Id).Select(n => n.Id).FirstOrDefaultAsync(ct);
            if (noteId == 0) return AgentStepResult.Waiting("برگه ترخیص کیفی صادرشده‌ای برای دستور کار نیست.");
            string noteNumber = await uow.Set<ReleaseNote>().AsNoTracking().Where(n => n.Id == noteId).Select(n => n.Number).FirstAsync(ct);
            var noteLines = await uow.Set<ReleaseNoteLine>().AsNoTracking().Where(l => l.ReleaseNoteId == noteId).OrderBy(l => l.RowNumber)
                .Select(l => new { l.ItemId, l.Quantity, l.SerialNumbers, ItemCode = l.Item.Code, ItemTitle = l.Item.Title }).ToListAsync(ct);
            if (noteLines.Count == 0) return AgentStepResult.Waiting("برگه ترخیص کیفی ردیف ندارد.");

            // the serial units of the work order, by serial number (a serial item of the release note is shipped piece by piece)
            Dictionary<string, int> units = (await uow.Set<SerialUnit>().AsNoTracking().Where(u => !u.Deleted && u.WorkOrderId == woId).Select(u => new { u.Id, u.SerialNumber }).ToListAsync(ct))
                .GroupBy(u => u.SerialNumber).ToDictionary(g => g.Key, g => g.First().Id);
            var lines = new List<PcShipmentLineDto>();
            var text = new List<string>();
            foreach (var l in noteLines)
            {
                string label = string.IsNullOrWhiteSpace(l.ItemCode) ? l.ItemTitle : l.ItemCode + " - " + l.ItemTitle;
                List<string> serials = (l.SerialNumbers ?? string.Empty).Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (serials.Count > 0 && serials.All(units.ContainsKey))
                {
                    foreach (string sn in serials)
                    {
                        lines.Add(new PcShipmentLineDto { ItemId = l.ItemId, SerialUnitId = units[sn], Quantity = 1m });
                        text.Add(label + " | سریال " + sn + " | تعداد ۱");
                    }
                }
                else
                {
                    lines.Add(new PcShipmentLineDto { ItemId = l.ItemId, Quantity = l.Quantity });
                    text.Add(label + " | تعداد " + Qty(l.Quantity));
                }
            }
            // the service never lets the shipments of a work order carry more than its ordered quantity: when the release note lists more pieces than that, one descriptive line of the ordered quantity is shipped
            decimal ordered = wo.Count > 0 ? wo.Count : 1m;
            if (lines.Sum(x => x.Quantity) > ordered)
            {
                lines = new List<PcShipmentLineDto> { new PcShipmentLineDto { Quantity = ordered, Description = "اقلام دستور کار " + wo.Code + " طبق برگه ترخیص " + noteNumber } };
                text = new List<string> { "اقلام دستور کار " + wo.Code + " طبق برگه ترخیص " + noteNumber + " | تعداد " + Qty(ordered) };
            }
            var packing = new StringBuilder();
            packing.AppendLine("فهرست بسته‌بندی دستور کار " + wo.Code + " (برگه ترخیص " + noteNumber + ")");
            for (int i = 0; i < text.Count; i++) packing.AppendLine((i + 1).ToString(CultureInfo.InvariantCulture) + ") " + text[i]);

            ResponseDto<PcShipmentDto> saved = await services.GetRequiredService<IPcReportService>().SaveShipmentAsync(
                new PcShipmentDto { ContractId = wo.ContractId, WorkOrderId = wo.Id, Lines = lines, PackingList = packing.ToString().TrimEnd() }, ct);
            if (!saved.Success) return ProcStepCommon.Refusal(saved);
            return AgentStepResult.Done("PcShipment", saved.Data.Id, saved.Data.Number, $"ایجنت ارسال محموله {saved.Data.Number} را با {lines.Count} ردیف و فهرست بسته‌بندی برای دستور کار {wo.Code} ساخت.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ShipRoles.Pack, Link = ShipmentsLink, Title = "ساخت محموله دستور کار " + item.TargetNumber };
    }

    /// <summary>SHIP.PACK (physical, station 10): the planned shipment is packed; the packer records the result of the packing verification (and a photo). The service refuses without a released release note, lines and a packing list.</summary>
    internal sealed class ShipPackStep : ShipStep
    {
        public override string Code => "SHIP.PACK";
        public override int Station => 10;
        public override string Title => "بسته‌بندی محموله و ثبت نتیجه بازبینی بسته‌بندی";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> released = uow.Set<ReleaseNote>().AsNoTracking().Where(n => !n.Deleted && n.Status == ReleaseNoteStatus.Released).Select(n => n.WorkOrderId);
            return ByStatusAsync(uow, today, PcShipmentStatus.Planned, s => s.Lines.Any() && released.Contains(s.WorkOrderId), "اقلام و فهرست بسته‌بندی آماده است؛ محموله بسته‌بندی و نتیجه بازبینی ثبت شود.", ShipRoles.Pack, ct);
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ShipRoles.Pack, Link = ShipmentsLink, Title = "بسته‌بندی محموله " + item.TargetNumber };
    }

    /// <summary>SHIP.CHECK (gate, station 10): QC or QA checks the packing; the person who packed cannot (the service refuses).</summary>
    internal sealed class ShipCheckStep : ShipStep
    {
        public override string Code => "SHIP.CHECK";
        public override int Station => 10;
        public override string Title => "بازبینی بسته‌بندی توسط کنترل کیفیت";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByStatusAsync(uow, today, PcShipmentStatus.Packed, s => s.PackingCheckedByUserId == null, "محموله بسته‌بندی شده؛ بازبینی با QC یا QA است و بسته‌بند نمی‌تواند آن را بازبینی کند.", ShipRoles.Check, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ShipRoles.Check, Link = ShipmentsLink, Title = "بازبینی بسته‌بندی محموله " + item.TargetNumber };
    }

    /// <summary>SHIP.RELEASE (gate, station 10): planning gives the shipping permit after the packing was checked; the packer cannot.</summary>
    internal sealed class ShipReleaseStep : ShipStep
    {
        public override string Code => "SHIP.RELEASE";
        public override int Station => 10;
        public override string Title => "مجوز ارسال محموله توسط برنامه‌ریزی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByStatusAsync(uow, today, PcShipmentStatus.Packed, s => s.PackingCheckedByUserId != null, "بسته‌بندی بازبینی شده؛ مجوز ارسال با برنامه‌ریزی است و بسته‌بند نمی‌تواند آن را بدهد.", ShipRoles.Release, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ShipRoles.Release, Link = ShipmentsLink, Title = "مجوز ارسال محموله " + item.TargetNumber };
    }

    /// <summary>SHIP.DELIVERYDOC (physical, station 10): a released shipment needs the warehouse document "delivery to the customer", confirmed after the goods are counted and loaded; the service refuses to ship without it.</summary>
    internal sealed class ShipDeliveryDocStep : ShipStep
    {
        public override string Code => "SHIP.DELIVERYDOC";
        public override int Station => 10;
        public override string Title => "سند انبار «تحویل به مشتری» برای محموله مجاز به ارسال";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<AgentWorkItem> released = await ByStatusAsync(uow, today, PcShipmentStatus.Released, null, null, InventoryRoles.RoleWarehouse, ct);
            Dictionary<int, (bool Confirmed, bool Draft)> docs = await DeliveryDocsAsync(uow, released.Select(s => (s.TargetId, s.ContractId.Value, s.WorkOrderId.Value)).ToList(), ct);
            var result = new List<AgentWorkItem>();
            foreach (AgentWorkItem s in released.Where(s => !docs[s.TargetId].Confirmed))
            {
                s.Summary = docs[s.TargetId].Draft ? "سند «تحویل به مشتری» پیش‌نویس است؛ پس از شمارش و بارگیری تایید شود." : "برای محموله مجاز به ارسال سند «تحویل به مشتری» انبار ساخته و تایید شود.";
                result.Add(s);
            }
            return result;
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = InventoryRoles.RoleWarehouse, Link = "/inventory/documents", Title = "سند تحویل به مشتری محموله " + item.TargetNumber };
    }

    /// <summary>SHIP.SHIP (physical, station 10): the goods leave; the person records the waybill and the shipping documents (they exist only on the carrier's paper) and ties the confirmed delivery document.</summary>
    internal sealed class ShipShipStep : ShipStep
    {
        public override string Code => "SHIP.SHIP";
        public override int Station => 10;
        public override string Title => "ارسال محموله و ثبت بارنامه و اسناد حمل";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<AgentWorkItem> released = await ByStatusAsync(uow, today, PcShipmentStatus.Released, null, "سند تحویل به مشتری تاییدشده است؛ محموله ارسال و بارنامه و اسناد حمل ثبت شود.", ShipRoles.Pack, ct);
            Dictionary<int, (bool Confirmed, bool Draft)> docs = await DeliveryDocsAsync(uow, released.Select(s => (s.TargetId, s.ContractId.Value, s.WorkOrderId.Value)).ToList(), ct);
            return released.Where(s => docs[s.TargetId].Confirmed).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ShipRoles.Pack, Link = ShipmentsLink, Title = "ارسال محموله " + item.TargetNumber };
    }

    /// <summary>SHIP.DELIVER (physical, station 11): the shipment reaches the customer; the signed receipt (a paper outside the system) is attached.</summary>
    internal sealed class ShipDeliverStep : ShipStep
    {
        public override string Code => "SHIP.DELIVER";
        public override int Station => 11;
        public override string Title => "تحویل محموله به مشتری و ثبت رسید امضاشده";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByStatusAsync(uow, today, PcShipmentStatus.Shipped, null, "محموله ارسال شده؛ پس از تحویل به مشتری شماره و تصویر رسید امضاشده ثبت شود.", ShipRoles.Deliver, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ShipRoles.Deliver, Link = ShipmentsLink, Title = "تحویل محموله " + item.TargetNumber };
    }

    /// <summary>SHIP.HANDOVER (physical, station 11): the handover minutes are signed with the customer (the number comes from the signed paper); planning or commercial records it. Nobody decides here: the acceptance is the customer's.</summary>
    internal sealed class ShipHandoverStep : ShipStep
    {
        public override string Code => "SHIP.HANDOVER";
        public override int Station => 11;
        public override string Title => "ثبت صورتجلسه تحویل با مشتری";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByStatusAsync(uow, today, PcShipmentStatus.Delivered, null, "محموله تحویل شده؛ شماره و تاریخ صورتجلسه تحویل امضاشده با مشتری ثبت شود.", ShipRoles.Handover, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ShipRoles.Handover, Link = ShipmentsLink, Title = "صورتجلسه تحویل محموله " + item.TargetNumber };
    }

    /// <summary>SHIP.COMMISSION (physical, station 11): the date the equipment was commissioned at the customer's site (it starts the warranty after commissioning).</summary>
    internal sealed class ShipCommissionStep : ShipStep
    {
        public override string Code => "SHIP.COMMISSION";
        public override int Station => 11;
        public override string Title => "ثبت تاریخ راه‌اندازی در محل مشتری";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByStatusAsync(uow, today, PcShipmentStatus.HandoverReceived, s => s.CommissionedAt == null, "صورتجلسه تحویل ثبت شده؛ پس از راه‌اندازی در محل، تاریخ راه‌اندازی ثبت شود.", ShipRoles.Commission, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ShipRoles.Commission, Link = ShipmentsLink, Title = "راه‌اندازی محموله " + item.TargetNumber };
    }
}
