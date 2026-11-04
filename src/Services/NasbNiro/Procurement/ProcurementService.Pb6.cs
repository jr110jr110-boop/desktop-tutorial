using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcurementDtos;

namespace Services.NasbNiro.Procurement
{
    /// <summary>PB6: the forms of a purchase request. The form decides the old kind (goods / service / subcontract) and the fields that are required.</summary>
    internal static class ProcFormTypes
    {
        public static PurchaseKind KindOf(PurchaseFormType form)
        {
            switch (form)
            {
                case PurchaseFormType.GoodsProject:
                case PurchaseFormType.GoodsMiscellaneous:
                    return PurchaseKind.Goods;
                case PurchaseFormType.ContractProject:
                    return PurchaseKind.Subcontract;
                default:
                    return PurchaseKind.Service;
            }
        }

        public static PurchaseFormType Derive(PurchaseKind kind, int? contractId)
        {
            switch (kind)
            {
                case PurchaseKind.Goods: return contractId.HasValue ? PurchaseFormType.GoodsProject : PurchaseFormType.GoodsMiscellaneous;
                case PurchaseKind.Subcontract: return PurchaseFormType.ContractProject;
                default: return contractId.HasValue ? PurchaseFormType.ServiceProject : PurchaseFormType.ServiceGeneral;
            }
        }

        public static bool IsProject(PurchaseFormType form) =>
            form == PurchaseFormType.GoodsProject || form == PurchaseFormType.ServiceProject || form == PurchaseFormType.VehicleProject || form == PurchaseFormType.ContractProject;

        public static bool IsVehicle(PurchaseFormType form) => form == PurchaseFormType.VehicleProject || form == PurchaseFormType.VehicleGeneral;

        public static string Validate(PurchaseFormType form, SavePurchaseRequestDto dto)
        {
            if (IsProject(form) && !dto.ContractId.HasValue && !dto.WorkOrderId.HasValue) return "این فرم برای پروژه است؛ پروژه (قرارداد) یا دستور ساخت را انتخاب کنید.";
            if (!IsProject(form) && form != PurchaseFormType.ProgressStatement && dto.ContractId.HasValue && !dto.WorkOrderId.HasValue)
                return "این فرم عمومی است و پروژه نمی‌گیرد؛ فرم پروژه‌ای را انتخاب کنید.";
            if (IsVehicle(form))
            {
                if (dto.VehicleDate == null || string.IsNullOrWhiteSpace(dto.VehicleUnit) || string.IsNullOrWhiteSpace(dto.VehicleType) ||
                    string.IsNullOrWhiteSpace(dto.VehicleOrigin) || string.IsNullOrWhiteSpace(dto.VehicleDestination))
                    return "فرم درخواست خودرو (FO-HR-025-02): تاریخ، واحد، نوع خودرو، مبدأ و مقصد را بنویسید.";
            }
            if (form == PurchaseFormType.ProgressStatement && !dto.RelatedOrderId.HasValue) return "درخواست صورت‌وضعیت باید سفارش (قرارداد) مربوط را داشته باشد.";
            return null;
        }

        /// <summary>A vehicle or progress-statement request has no list of goods: one line carries the description.</summary>
        public static SavePurchaseRequestLineDto SyntheticLine(PurchaseFormType form, SavePurchaseRequestDto dto)
        {
            if (IsVehicle(form))
                return new SavePurchaseRequestLineDto
                {
                    Quantity = 1, Unit = "سفر",
                    Description = $"درخواست خودرو {dto.VehicleType}: از {dto.VehicleOrigin} به {dto.VehicleDestination} ({dto.VehicleUnit})",
                };
            if (form == PurchaseFormType.ProgressStatement)
                return new SavePurchaseRequestLineDto { Quantity = 1, Unit = "مورد", Description = string.IsNullOrWhiteSpace(dto.Description) ? "پرداخت صورت‌وضعیت" : dto.Description };
            return null;
        }
    }

    /// <summary>The requesters behind an order are told about a step of it (received, QC, invoiced, paid). Static so that the warehouse, quality and treasury services can call it.</summary>
    public static class ProcOrderNotices
    {
        public static async Task SendAsync(IUnitOfWork uow, global::Services.NasbNiro.Office.INotificationService notify, int actingUserId, int purchaseOrderId, string step, string title, CancellationToken cancellationToken)
        {
            if (notify == null || uow == null) return;
            try
            {
                var rows = await uow.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == purchaseOrderId && l.RequestLineId != null)
                    .Select(l => new { l.RequestLine.PurchaseRequestId, Number = l.RequestLine.PurchaseRequest.Number, Creator = l.RequestLine.PurchaseRequest.CreatorUserId })
                    .Distinct().ToListAsync(cancellationToken);
                foreach (var row in rows)
                {
                    if (row.Creator == actingUserId) continue;
                    await notify.NotifyAsync(new global::Services.NasbNiro.Office.NotifyRequest
                    {
                        SourceKey = "prn:" + row.PurchaseRequestId + ":" + step + ":" + purchaseOrderId, Module = "procurement", SourceType = "purchase-request", SourceId = row.PurchaseRequestId,
                        Title = title + " (درخواست " + row.Number + ")", Link = "/procurement/requests/" + row.PurchaseRequestId,
                        Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Alert, UserIds = new List<int> { row.Creator },
                    }, cancellationToken);
                }
            }
            catch (Exception)
            {
                // a notice must never block the warehouse, the quality or the payment step
            }
        }
    }

    internal static class ProcArchiveFilter
    {
        /// <summary>The live documents by default; "only" = the archive; "all" = both.</summary>
        public static async Task<IQueryable<T>> ApplyAsync<T>(IUnitOfWork uow, IQueryable<T> source, ProcArchiveOwner owner, string mode, CancellationToken cancellationToken) where T : global::Entities.Model.BaseEntity
        {
            mode = mode?.Trim().ToLowerInvariant();
            if (mode == "all") return source;
            List<int> archived = await uow.Set<ProcArchiveRecord>().Where(a => a.OwnerType == owner && a.IsArchived).Select(a => a.OwnerId).ToListAsync(cancellationToken);
            return mode == "only" ? source.Where(x => archived.Contains(x.Id)) : source.Where(x => !archived.Contains(x.Id));
        }
    }

    public partial class ProcurementService
    {
        // ------------------------------------------------------------------------------------------ notifications

        /// <summary>A notice (not a task) to one person. It never throws and never blocks a step.</summary>
        private async Task NotifyUserAsync(int userId, string key, string module, string type, int id, string title, string link, CancellationToken cancellationToken)
        {
            if (_notify == null || userId <= 0) return;
            try
            {
                await _notify.NotifyAsync(new global::Services.NasbNiro.Office.NotifyRequest
                {
                    SourceKey = key, Module = module, SourceType = type, SourceId = id, Title = title, Link = link,
                    Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Alert, UserIds = new List<int> { userId },
                }, cancellationToken);
            }
            catch (Exception)
            {
                // the notice must never block a gate
            }
        }

        /// <summary>The requester of a purchase request is told about a step; a seat or the person himself is not told about his own action.</summary>
        private Task NotifyRequesterAsync(PurchaseRequest request, string step, string title, CancellationToken cancellationToken)
        {
            if (request == null || request.CreatorUserId == Me) return Task.CompletedTask;
            return NotifyUserAsync(request.CreatorUserId, "prn:" + request.Id + ":" + step, "procurement", "purchase-request", request.Id, title, "/procurement/requests/" + request.Id, cancellationToken);
        }

        public Task NotifyOrderEventAsync(int purchaseOrderId, string step, string title, CancellationToken cancellationToken) =>
            ProcOrderNotices.SendAsync(_uow, _notify, Me, purchaseOrderId, step, title, cancellationToken);

        /// <summary>The inbox of the approvers of an inquiry: planning first (D5), then the committee, then the management.</summary>
        private async Task SyncInquiryGateAsync(PurchaseInquiry i, PurchaseRequest request, CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            var roles = new List<string>();
            string title = "استعلام " + i.Number;
            if (i.Status == InquiryStatus.Open)
            {
                if (s[ProcurementSettingKeys.InquiryPlanningRequired] > 0 && !i.PlanningApprovedAt.HasValue) { roles.Add(ProcurementRoles.Planning); title = "تایید بودجه و زمان تحویل استعلام " + i.Number; }
            }
            else if (i.Status == InquiryStatus.Selected)
            {
                SupplierQuote chosen = i.Quotes?.FirstOrDefault(q => q.Id == i.SelectedQuoteId);
                decimal amount = chosen?.TotalAmount ?? 0;
                if (amount >= s[ProcurementSettingKeys.CommitteeFrom])
                {
                    if (!i.FinancialApprovedAt.HasValue) roles.Add(ProcurementRoles.Finance);
                    if (!i.CommercialApprovedAt.HasValue) roles.Add(ProcurementRoles.Commercial);
                    if (!i.TechnicalApprovedAt.HasValue) roles.Add(ProcurementRoles.Technical);
                }
                if (ProcurementAuthority.InquiryNeedsManagement(s, i, request, amount) && !i.ManagementApprovedAt.HasValue) roles.Add(ProcurementRoles.Management);
                title = "تایید انتخاب تامین‌کننده استعلام " + i.Number;
            }
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "inq:" + i.Id, roles.Count > 0, "procurement", "inquiry", i.Id, title,
                "/procurement/inquiries/" + i.Id, string.Join(",", roles), cancellationToken);
        }

        /// <summary>The inbox of a purchase order: approval by the PMO deputy / CEO while it is a draft, then the release by the buyer.</summary>
        private async Task SyncOrderGateAsync(PurchaseOrder o, CancellationToken cancellationToken)
        {
            bool waitingApproval = o.Status == PurchaseOrderStatus.Draft;
            bool waitingRelease = o.Status == PurchaseOrderStatus.Approved;
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "po:" + o.Id, waitingApproval || waitingRelease, "procurement", "purchase-order", o.Id,
                (waitingApproval ? "تایید سفارش خرید " : "ابلاغ سفارش خرید ") + o.Number, "/procurement/orders/" + o.Id,
                waitingApproval ? ProcurementRoles.OrderApproval : ProcurementRoles.Commercial, cancellationToken);
        }

        // ------------------------------------------------------------------------------------------ request listing

        private const string PriceRoles = "PMO,CEO,PLANNING,COMMERCIAL,FINANCIAL,FINANCEMANAGER,TREASURY,AUDITOR";

        private bool SeesAllRequests => _user.IsInRole("PMO,CEO,PLANNING,COMMERCIAL,FINANCIAL,FINANCEMANAGER,TREASURY,WAREHOUSE,WHSUPERVISOR,ENGINEERING,QC,CONSTRUCTION,ASSEMBLER,ENGINEER,AUDITOR");

        /// <summary>A unit manager who only asks reads his own requests; the procurement chain reads all of them.</summary>
        private bool CanSeeRequest(int creatorUserId) => SeesAllRequests || creatorUserId == Me;

        private async Task<IQueryable<PurchaseRequest>> ApplyRequestFiltersAsync(IQueryable<PurchaseRequest> requests, PurchaseRequestQuery query, CancellationToken cancellationToken)
        {
            if (!SeesAllRequests) { int me = Me; requests = requests.Where(r => r.CreatorUserId == me); }
            if (query.FormType.HasValue) requests = requests.Where(r => r.FormType == query.FormType.Value);
            if (query.Kind.HasValue) requests = requests.Where(r => r.Kind == query.Kind.Value);
            if (query.Source.HasValue) requests = requests.Where(r => r.Source == query.Source.Value);
            if (query.ContractId.HasValue) requests = requests.Where(r => r.ContractId == query.ContractId.Value);
            if (query.Priority.HasValue) requests = requests.Where(r => r.Priority == query.Priority.Value);
            if (query.DateFrom.HasValue) { DateTime from = query.DateFrom.Value.Date; requests = requests.Where(r => r.RequestDate >= from); }
            if (query.DateTo.HasValue) { DateTime to = query.DateTo.Value.Date; requests = requests.Where(r => r.RequestDate <= to); }
            if (query.ProviderId.HasValue)
            {
                int provider = query.ProviderId.Value;
                requests = requests.Where(r => r.Lines.Any(l => _uow.Set<PurchaseOrderLine>().Any(o => o.RequestLineId == l.Id && o.PurchaseOrder.ProviderId == provider && !o.PurchaseOrder.Deleted)) ||
                                               _uow.Set<PurchaseInquiry>().Any(i => i.PurchaseRequestId == r.Id && i.Quotes.Any(q => q.ProviderId == provider)));
            }
            return await ArchiveFilterAsync(requests, ProcArchiveOwner.PurchaseRequest, query.Archive, cancellationToken);
        }

        private Task<IQueryable<T>> ArchiveFilterAsync<T>(IQueryable<T> source, ProcArchiveOwner owner, string mode, CancellationToken cancellationToken) where T : global::Entities.Model.BaseEntity =>
            ProcArchiveFilter.ApplyAsync(_uow, source, owner, mode, cancellationToken);

        /// <summary>The columns of the follow-up: asked, ordered, received, accepted (QC), invoiced, paid; and the suppliers and the orders.</summary>
        private async Task FillRequestProgressAsync(List<PurchaseRequestDto> rows, CancellationToken cancellationToken)
        {
            if (rows.Count == 0) return;
            List<int> lineIds = rows.SelectMany(r => r.Lines.Select(l => l.Id)).ToList();
            List<int> requestIds = rows.Select(r => r.Id).ToList();
            var orderLines = await _uow.Set<PurchaseOrderLine>()
                .Where(l => l.RequestLineId.HasValue && lineIds.Contains(l.RequestLineId.Value) && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled && !l.PurchaseOrder.Deleted)
                .Select(l => new
                {
                    l.Id, RequestLineId = l.RequestLineId.Value, l.PurchaseOrderId, l.PurchaseOrder.Number, ProviderName = l.PurchaseOrder.Provider.Name,
                    l.Quantity, l.ReceivedQuantity, l.InvoicedQuantity, l.UnitPrice,
                }).ToListAsync(cancellationToken);
            Dictionary<int, decimal> accepted = await AcceptedQuantitiesAsync(orderLines.Select(l => l.Id), cancellationToken);
            List<int> orderIds = orderLines.Select(l => l.PurchaseOrderId).Distinct().ToList();
            var paid = await _uow.Set<PaymentRequest>().Where(p => p.PurchaseOrderId.HasValue && orderIds.Contains(p.PurchaseOrderId.Value) && p.Status == PaymentRequestStatus.Paid && !p.Deleted)
                .GroupBy(p => p.PurchaseOrderId.Value).Select(g => new { g.Key, Amount = g.Sum(p => p.Amount - p.Deductions) }).ToListAsync(cancellationToken);
            var orderTotals = await _uow.Set<PurchaseOrder>().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.TotalAmount }).ToListAsync(cancellationToken);
            List<ProcArchiveRecord> archive = await _uow.Set<ProcArchiveRecord>().Where(a => a.OwnerType == ProcArchiveOwner.PurchaseRequest && requestIds.Contains(a.OwnerId) && a.IsArchived).ToListAsync(cancellationToken);
            Dictionary<int, string> reqNumbers = await _uow.Set<PurchaseRequest>().Where(r => requestIds.Contains(r.Id) || rows.Select(x => x.PreviousRequestId).Contains(r.Id) || rows.Select(x => x.SupersededByRequestId).Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Number, cancellationToken);

            foreach (PurchaseRequestDto r in rows)
            {
                var mine = orderLines.Where(o => r.Lines.Any(l => l.Id == o.RequestLineId)).ToList();
                r.RequestedQuantity = r.Lines.Sum(l => l.Quantity);
                r.OrderedQuantity = mine.Sum(o => o.Quantity);
                r.ReceivedQuantity = mine.Sum(o => o.ReceivedQuantity);
                r.AcceptedQuantity = mine.Sum(o => accepted.TryGetValue(o.Id, out decimal a) ? a : 0);
                r.InvoicedQuantity = mine.Sum(o => o.InvoicedQuantity);
                r.InvoicedAmount = Math.Round(mine.Sum(o => o.InvoicedQuantity * o.UnitPrice), 0);
                List<int> myOrders = mine.Select(o => o.PurchaseOrderId).Distinct().ToList();
                // the money paid is that of the orders; an order that serves several requests shows its payments on each (the report says so)
                r.PaidAmount = paid.Where(p => myOrders.Contains(p.Key)).Sum(p => p.Amount);
                r.SupplierNames = mine.Select(o => o.ProviderName).Distinct().ToList();
                r.OrderNumbers = mine.Select(o => o.Number).Distinct().ToList();
                r.IsArchived = archive.Any(a => a.OwnerId == r.Id);
                // M2: money and supplier names are for the people who plan, buy and pay; the others (unit managers, warehouse, QC...) see quantities only
                if (!_user.IsInRole(PriceRoles))
                {
                    r.InvoicedAmount = 0; r.PaidAmount = 0; r.SupplierNames = new List<string>(); r.EstimatedAmount = 0;
                    foreach (var l in r.Lines) l.EstimatedUnitPrice = null;
                }
                if (r.PreviousRequestId.HasValue && reqNumbers.TryGetValue(r.PreviousRequestId.Value, out string pn)) r.PreviousRequestNumber = pn;
                if (r.SupersededByRequestId.HasValue && reqNumbers.TryGetValue(r.SupersededByRequestId.Value, out string sn)) r.SupersededByRequestNumber = sn;
            }
        }

        // ------------------------------------------------------------------------------------------ return and revise

        private static string BaseNumber(string number)
        {
            int cut = number?.LastIndexOf("/R", StringComparison.Ordinal) ?? -1;
            return cut > 0 ? number.Substring(0, cut) : number;
        }

        /// <summary>Planning or management sends a submitted request back to the requester with a reason. The request stays as it was (history); the requester makes revision n+1 with "revise".</summary>
        public async Task<ResponseDto<PurchaseRequestDto>> ReturnRequestAsync(ProcurementActionDto dto, CancellationToken cancellationToken)
        {
            PurchaseRequest r = await _uow.Set<PurchaseRequest>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (r == null) return ResponseDtoHelper.NotFound<PurchaseRequestDto>("درخواست خرید پیدا نشد.");
            if (r.Status != PurchaseRequestStatus.Submitted) return Bad<PurchaseRequestDto>("فقط درخواست ارسال‌شده به درخواست‌دهنده برگردانده می‌شود.");
            if (!_user.IsInRole(ProcurementRoles.Planning) && !_user.IsInRole(ProcurementRoles.Management)) return Bad<PurchaseRequestDto>(NotAllowed);
            if (r.CreatorUserId == Me) return Bad<PurchaseRequestDto>(SameUser);
            string reason = Clean(dto.Note);
            if (reason == null) return Bad<PurchaseRequestDto>("علت برگشت را بنویسید.");
            r.Status = PurchaseRequestStatus.Returned;
            r.ReturnedAt = DateTimeOffset.Now;
            r.ReturnedByUserId = Me;
            r.ReturnReason = reason;
            r.ModifyDate = DateTimeOffset.Now; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "pr:" + r.Id, false, "procurement", "purchase-request", r.Id, "", "", "", cancellationToken);
            await NotifyRequesterAsync(r, "returned", "درخواست خرید " + r.Number + " برای اصلاح برگشت خورد: " + reason, cancellationToken);
            return await GetRequestAsync(r.Id, cancellationToken);
        }

        /// <summary>
        /// A returned or rejected request: the requester makes revision n+1 (a new draft linked to the old one, which stays as it was). An approved request that nothing was ordered from:
        /// the approved content is frozen as a snapshot and the same request goes back to draft as the next revision (like a purchase order revision).
        /// </summary>
        public async Task<ResponseDto<PurchaseRequestDto>> ReviseRequestAsync(ProcurementActionDto dto, CancellationToken cancellationToken)
        {
            PurchaseRequest r = await _uow.Set<PurchaseRequest>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (r == null) return ResponseDtoHelper.NotFound<PurchaseRequestDto>("درخواست خرید پیدا نشد.");
            if (r.CreatorUserId != Me && !_user.IsInRole("PMO")) return Bad<PurchaseRequestDto>("نسخه جدید را ثبت‌کننده درخواست می‌سازد.");
            DateTimeOffset now = DateTimeOffset.Now;
            if (r.Status == PurchaseRequestStatus.Returned || r.Status == PurchaseRequestStatus.Rejected)
            {
                if (r.SupersededByRequestId.HasValue) return Bad<PurchaseRequestDto>("برای این درخواست قبلاً نسخه جدید ساخته شده است.");
                int next = (r.Revision == 0 ? 1 : r.Revision) + 1;
                var copy = new PurchaseRequest
                {
                    Number = BaseNumber(r.Number) + "/R" + next, Source = r.Source, Kind = r.Kind, Status = PurchaseRequestStatus.Draft, RequestDate = DateTime.Today,
                    WorkOrderId = r.WorkOrderId, ContractId = r.ContractId, CostCenter = r.CostCenter, MaterialRequestId = r.MaterialRequestId, Description = r.Description,
                    EstimatedAmount = r.EstimatedAmount, CreateDate = now, CreatorUserId = Me, Priority = r.Priority, FormType = r.FormType, Revision = next, PreviousRequestId = r.Id,
                    VehicleDate = r.VehicleDate, VehicleUnit = r.VehicleUnit, VehicleType = r.VehicleType, VehicleOrigin = r.VehicleOrigin, VehicleDestination = r.VehicleDestination,
                    RelatedOrderId = r.RelatedOrderId,
                    Lines = r.Lines.OrderBy(l => l.RowNumber).Select(l => new PurchaseRequestLine
                    {
                        RowNumber = l.RowNumber, ItemId = l.ItemId, Description = l.Description, Unit = l.Unit, Quantity = l.Quantity, NeededBy = l.NeededBy,
                        EstimatedUnitPrice = l.EstimatedUnitPrice, TechnicalRequirements = l.TechnicalRequirements, BuyOrderId = l.BuyOrderId, MaterialRequestLineId = l.MaterialRequestLineId,
                    }).ToList(),
                };
                // TODO(PB2 merge): the revision copy must also carry ProjectBaselineId, BalanceGroupKey, the line WbsNodeId, BalanceLineId and ManuallyEdited (the coordinator wires these when PB2 is merged)
                _uow.Set<PurchaseRequest>().Add(copy);
                string linkError = await SaveLinkedAsync(async () =>
                {
                    r.SupersededByRequestId = copy.Id;
                    r.ModifyDate = now; r.ModifierUserId = Me;
                    await CopyAttachmentsAsync(AttachmentOwnerType.PurchaseRequest, r.Id, copy.Id, cancellationToken);
                }, cancellationToken);
                if (linkError != null) return Bad<PurchaseRequestDto>(linkError);
                return await GetRequestAsync(copy.Id, cancellationToken);
            }
            if (r.Status != PurchaseRequestStatus.Approved) return Bad<PurchaseRequestDto>("فقط درخواست برگشتی، ردشده یا تاییدشده نسخه جدید می‌گیرد.");
            string reason = Clean(dto.Note);
            if (reason == null) return Bad<PurchaseRequestDto>("علت ویرایش درخواست تاییدشده را بنویسید.");
            List<int> lineIds = r.Lines.Select(l => l.Id).ToList();
            if (await _uow.Set<PurchaseInquiry>().AnyAsync(i => i.PurchaseRequestId == r.Id && i.Status != InquiryStatus.Cancelled && i.Status != InquiryStatus.Returned && !i.Deleted, cancellationToken))
                return Bad<PurchaseRequestDto>("از این درخواست استعلام گرفته شده است؛ ابتدا استعلام را لغو یا برگشت دهید.");
            if (await _uow.Set<PurchaseOrderLine>().AnyAsync(l => l.RequestLineId.HasValue && lineIds.Contains(l.RequestLineId.Value) && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled, cancellationToken))
                return Bad<PurchaseRequestDto>("از این درخواست سفارش صادر شده است و ویرایش نمی‌شود.");
            int current = r.Revision == 0 ? 1 : r.Revision;
            string json = JsonSerializer.Serialize(new
            {
                r.Number, Revision = current, r.Source, r.Kind, r.FormType, r.Priority, RequestDate = r.RequestDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.WorkOrderId, r.ContractId,
                r.CostCenter, r.Description, r.EstimatedAmount, r.BoardResolution, r.PlanningApprovedAt, r.PlanningApprovedByUserId, r.ManagementApprovedAt, r.ManagementApprovedByUserId,
                Lines = r.Lines.OrderBy(l => l.RowNumber).Select(l => new
                {
                    l.RowNumber, l.ItemId, l.Description, l.Unit, l.Quantity, NeededBy = l.NeededBy?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), l.EstimatedUnitPrice, l.TechnicalRequirements,
                }).ToList(),
            });
            _uow.Set<PurchaseRequestRevision>().Add(new PurchaseRequestRevision
            {
                PurchaseRequestId = r.Id, Revision = current, Reason = reason, StatusWhenFrozen = r.Status, EstimatedAmount = r.EstimatedAmount, SnapshotJson = json,
                Hash = global::Services.NasbNiro.ProjectControl.PcText.Sha256(json), FrozenAt = now, FrozenByUserId = Me,
                PlanningApprovedAt = r.PlanningApprovedAt, PlanningApprovedByUserId = r.PlanningApprovedByUserId,
                ManagementApprovedAt = r.ManagementApprovedAt, ManagementApprovedByUserId = r.ManagementApprovedByUserId,
            });
            r.Revision = current + 1;
            r.Status = PurchaseRequestStatus.Draft;
            r.PlanningApprovedAt = null; r.PlanningApprovedByUserId = null; r.ManagementApprovedAt = null; r.ManagementApprovedByUserId = null;
            r.SubmittedAt = null; r.BoardResolution = null;
            r.ModifyDate = now; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetRequestAsync(r.Id, cancellationToken);
        }

        public async Task<ResponseDto<RequestChainDto>> GetRequestChainAsync(int id, CancellationToken cancellationToken)
        {
            PurchaseRequest start = await _uow.Set<PurchaseRequest>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && !r.Deleted, cancellationToken);
            if (start == null || !CanSeeRequest(start.CreatorUserId)) return ResponseDtoHelper.NotFound<RequestChainDto>("درخواست خرید پیدا نشد.");
            string baseNumber = BaseNumber(start.Number);
            List<PurchaseRequest> chain = await _uow.Set<PurchaseRequest>().AsNoTracking()
                .Where(r => !r.Deleted && (r.Number == baseNumber || r.Number.StartsWith(baseNumber + "/R"))).OrderBy(r => r.Revision).ThenBy(r => r.Id).ToListAsync(cancellationToken);
            var result = new RequestChainDto();
            result.Items = chain.Select(r => new RequestChainItemDto
            {
                Id = r.Id, Number = r.Number, Revision = r.Revision == 0 ? 1 : r.Revision, Status = r.Status, ReturnReason = r.ReturnReason, RejectReason = r.RejectReason, CreateDate = r.CreateDate,
            }).ToList();
            List<int> ids = chain.Select(r => r.Id).ToList();
            List<PurchaseRequestRevision> snapshots = await _uow.Set<PurchaseRequestRevision>().AsNoTracking().Where(s => ids.Contains(s.PurchaseRequestId)).OrderBy(s => s.Revision).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, snapshots.SelectMany(s => new int?[] { s.FrozenByUserId, s.PlanningApprovedByUserId, s.ManagementApprovedByUserId }), cancellationToken);
            result.Snapshots = snapshots.Select(s => new PurchaseRequestRevisionDto
            {
                Id = s.Id, PurchaseRequestId = s.PurchaseRequestId, Revision = s.Revision, Reason = s.Reason, StatusWhenFrozen = s.StatusWhenFrozen, EstimatedAmount = s.EstimatedAmount,
                SnapshotJson = s.SnapshotJson, Hash = s.Hash, FrozenAt = s.FrozenAt, FrozenByName = names.Name(s.FrozenByUserId),
                PlanningApprovedByName = names.Name(s.PlanningApprovedByUserId), ManagementApprovedByName = names.Name(s.ManagementApprovedByUserId),
            }).ToList();
            return ResponseDtoHelper.Ok(result);
        }

        // ------------------------------------------------------------------------------------------ inquiry: planning step (D5), return, revise

        /// <summary>The leaf WBS budgets of a project against what is already ordered for it.</summary>
        private async Task<string> WbsBudgetErrorAsync(int? contractId, decimal amount, int ignoreInquiryId, bool overrideByManagement, string note, CancellationToken cancellationToken)
        {
            if (!contractId.HasValue) return null;
            var nodes = await _uow.Set<PcWbsNode>().AsNoTracking().Where(n => n.ContractId == contractId.Value && !n.Deleted).Select(n => new { n.Id, n.ParentId, n.BudgetAmount }).ToListAsync(cancellationToken);
            HashSet<int> parents = nodes.Where(n => n.ParentId.HasValue).Select(n => n.ParentId.Value).ToHashSet();
            decimal budget = nodes.Where(n => !parents.Contains(n.Id)).Sum(n => n.BudgetAmount);
            if (budget <= 0) return null;
            decimal committed = await _uow.Set<PurchaseOrder>().Where(o => o.ContractId == contractId.Value && o.Status != PurchaseOrderStatus.Cancelled && !o.Deleted && (o.InquiryId == null || o.InquiryId != ignoreInquiryId))
                .SumAsync(o => (decimal?)o.TotalAmount, cancellationToken) ?? 0;
            if (committed + amount <= budget) return null;
            if (overrideByManagement && Clean(note) != null) return null;
            return $"بودجه WBS پروژه {budget:N0} ریال است و {committed:N0} ریال سفارش شده؛ با این مبلغ ({amount:N0}) از بودجه می‌گذرد. فقط مدیرعامل با نوشتن دلیل (عمل «عبور از بودجه») می‌تواند ادامه را مجاز کند.";
        }

        /// <summary>M3: only the CEO passes the budget gate, with a written reason; the planner (someone else) then approves with it.</summary>
        private async Task<ResponseDto<InquiryDto>> OverrideBudgetAsync(PurchaseInquiry i, ProcurementActionDto dto, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole("CEO")) return Bad<InquiryDto>(NotAllowed + " (مدیرعامل)");
            if (i.Status != InquiryStatus.Open || i.PlanningApprovedAt.HasValue) return Bad<InquiryDto>("عبور از بودجه پیش از تایید برنامه‌ریزی و انتخاب برنده است.");
            if (Clean(dto.Note) == null) return Bad<InquiryDto>("دلیل عبور از بودجه را بنویسید.");
            List<SupplierQuote> accepted = i.Quotes.Where(q => q.TechnicallyAccepted).ToList();
            decimal budget = dto.Amount ?? (accepted.Count == 0 ? 0 : accepted.Max(q => q.TotalAmount));
            if (budget <= 0) return Bad<InquiryDto>("مبلغ باید بیشتر از صفر باشد (پیشنهاد دارای تایید فنی لازم است).");
            var gate = await _budget.GateAsync(i.PurchaseRequest.CostCenter, i.PurchaseRequest.ContractId, budget, i.InquiryDate, i.PurchaseRequestId, null, dto.Note, cancellationToken);
            if (!gate.Allowed) return Bad<InquiryDto>(gate.Message);
            string wbs = await WbsBudgetErrorAsync(i.PurchaseRequest.ContractId, budget, i.Id, true, dto.Note, cancellationToken);
            if (wbs != null) return Bad<InquiryDto>(wbs);
            i.BudgetOverrideAt = DateTimeOffset.Now; i.BudgetOverrideByUserId = Me; i.BudgetOverrideNote = Clean(dto.Note); i.BudgetOverrideAmount = budget;
            i.ModifyDate = DateTimeOffset.Now; i.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetInquiryAsync(i.Id, cancellationToken);
        }

        private async Task<ResponseDto<InquiryDto>> ApprovePlanningAsync(PurchaseInquiry i, ProcurementActionDto dto, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProcurementRoles.Planning)) return Bad<InquiryDto>(NotAllowed + " (برنامه‌ریزی و کنترل پروژه)");
            if (i.Status != InquiryStatus.Open) return Bad<InquiryDto>("بودجه و زمان تحویل پیش از انتخاب برنده تایید می‌شود.");
            if (i.PlanningApprovedAt.HasValue) return Bad<InquiryDto>("بودجه و زمان این استعلام قبلاً تایید شده است.");
            if (i.CreatorUserId == Me || i.PurchaseRequest.CreatorUserId == Me) return Bad<InquiryDto>(SameUser);
            List<SupplierQuote> accepted = i.Quotes.Where(q => q.TechnicallyAccepted).ToList();
            if (accepted.Count == 0) return Bad<InquiryDto>("پیش از تایید بودجه، دست‌کم یک پیشنهاد باید تایید فنی داشته باشد.");
            decimal budget = dto.Amount ?? accepted.Max(q => q.TotalAmount);
            if (budget <= 0) return Bad<InquiryDto>("بودجه تاییدشده باید بیشتر از صفر باشد.");
            int? days = dto.Days ?? accepted.Where(q => q.DeliveryDays.HasValue).Select(q => q.DeliveryDays).DefaultIfEmpty(null).Max();
            if (days.HasValue && days.Value < 0) return Bad<InquiryDto>("زمان تحویل نمی‌تواند منفی باشد.");
            // finance budget lines (the same gate the request passed) and the WBS budget of the project
            var gate = await _budget.GateAsync(i.PurchaseRequest.CostCenter, i.PurchaseRequest.ContractId, budget, i.InquiryDate, i.PurchaseRequestId, null, null, cancellationToken);
            bool overrideHeld = i.BudgetOverrideAt.HasValue && i.BudgetOverrideAmount.HasValue && budget <= i.BudgetOverrideAmount.Value;
            if (!gate.Allowed && !overrideHeld) return Bad<InquiryDto>(gate.Message);
            bool ceoPassed = i.BudgetOverrideAt.HasValue && i.BudgetOverrideAmount.HasValue && budget <= i.BudgetOverrideAmount.Value;
            string wbs = await WbsBudgetErrorAsync(i.PurchaseRequest.ContractId, budget, i.Id, ceoPassed, i.BudgetOverrideNote, cancellationToken);
            if (wbs != null) return Bad<InquiryDto>(wbs);
            i.PlanningApprovedAt = DateTimeOffset.Now;
            i.PlanningApprovedByUserId = Me;
            i.PlanningBudgetAmount = budget;
            i.PlanningDeliveryDays = days;
            i.PlanningNote = Clean(dto.Note);
            i.ModifyDate = DateTimeOffset.Now; i.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            await SyncInquiryGateAsync(i, i.PurchaseRequest, cancellationToken);
            return await GetInquiryAsync(i.Id, cancellationToken);
        }

        /// <summary>The selection is judged against what planning approved: the amount may not pass the budget; a longer delivery time needs a written reason.</summary>
        private string PlanningSelectionError(PurchaseInquiry inquiry, SupplierQuote quote, Dictionary<string, decimal> settings, string reason)
        {
            if (settings[ProcurementSettingKeys.InquiryPlanningRequired] <= 0) return null;
            if (!inquiry.PlanningApprovedAt.HasValue) return "پیش از انتخاب برنده، برنامه‌ریزی باید بودجه و زمان تحویل استعلام را تایید کند (مرحله «تایید برنامه‌ریزی»).";
            if (inquiry.PlanningBudgetAmount.HasValue && quote.TotalAmount > inquiry.PlanningBudgetAmount.Value)
                return $"مبلغ پیشنهاد ({quote.TotalAmount:N0}) از بودجه تاییدشده برنامه‌ریزی ({inquiry.PlanningBudgetAmount.Value:N0}) بیشتر است؛ استعلام را برای تایید دوباره برگردانید.";
            if (inquiry.PlanningDeliveryDays.HasValue && quote.DeliveryDays.HasValue && quote.DeliveryDays.Value > inquiry.PlanningDeliveryDays.Value && reason == null)
                return $"زمان تحویل پیشنهاد ({quote.DeliveryDays} روز) از زمان تاییدشده ({inquiry.PlanningDeliveryDays} روز) بیشتر است؛ دلیل انتخاب را بنویسید.";
            return null;
        }

        /// <summary>The committee (or planning) sends an inquiry back to the buyer with a reason; the buyer makes revision n+1.</summary>
        private async Task<ResponseDto<InquiryDto>> ReturnInquiryAsync(PurchaseInquiry i, ProcurementActionDto dto, CancellationToken cancellationToken)
        {
            if (i.Status != InquiryStatus.Open && i.Status != InquiryStatus.Selected) return Bad<InquiryDto>("فقط استعلام باز یا منتظر تایید برگردانده می‌شود.");
            if (!_user.IsInRole(ProcurementRoles.Planning) && !_user.IsInRole(ProcurementRoles.Finance) && !_user.IsInRole(ProcurementRoles.Technical) && !_user.IsInRole(ProcurementRoles.Management))
                return Bad<InquiryDto>(NotAllowed);
            if (i.CreatorUserId == Me || i.SelectedByUserId == Me) return Bad<InquiryDto>(SameUser);
            string reason = Clean(dto.Note);
            if (reason == null) return Bad<InquiryDto>("علت برگشت را بنویسید.");
            i.Status = InquiryStatus.Returned;
            i.ReturnedAt = DateTimeOffset.Now; i.ReturnedByUserId = Me; i.ReturnReason = reason;
            i.ModifyDate = DateTimeOffset.Now; i.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "inq:" + i.Id, false, "procurement", "inquiry", i.Id, "", "", "", cancellationToken);
            await NotifyUserAsync(i.CreatorUserId, "inqn:" + i.Id + ":returned", "procurement", "inquiry", i.Id, "استعلام " + i.Number + " برای اصلاح برگشت خورد: " + reason, "/procurement/inquiries/" + i.Id, cancellationToken);
            await NotifyRequesterAsync(i.PurchaseRequest, "inquiry-returned:" + i.Id, "استعلام درخواست " + i.PurchaseRequest.Number + " به بازرگانی برگشت خورد.", cancellationToken);
            return await GetInquiryAsync(i.Id, cancellationToken);
        }

        private async Task<ResponseDto<InquiryDto>> ReviseInquiryAsync(PurchaseInquiry i, ProcurementActionDto dto, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProcurementRoles.Commercial)) return Bad<InquiryDto>(NotAllowed + " (بازرگانی)");
            if (i.Status != InquiryStatus.Returned) return Bad<InquiryDto>("فقط استعلام برگشتی نسخه جدید می‌گیرد.");
            if (i.SupersededByInquiryId.HasValue) return Bad<InquiryDto>("برای این استعلام قبلاً نسخه جدید ساخته شده است.");
            PurchaseInquiry loaded = await _uow.Set<PurchaseInquiry>().Include(x => x.Quotes).ThenInclude(q => q.Lines).AsSplitQuery().FirstAsync(x => x.Id == i.Id, cancellationToken);
            int next = (loaded.Revision == 0 ? 1 : loaded.Revision) + 1;
            var copy = new PurchaseInquiry
            {
                Number = BaseNumber(loaded.Number) + "/R" + next, Status = InquiryStatus.Open, InquiryDate = DateTime.Today, PurchaseRequestId = loaded.PurchaseRequestId,
                Description = loaded.Description, RequiredQuotes = loaded.RequiredQuotes, FewerQuotesReason = loaded.FewerQuotesReason, CreateDate = DateTimeOffset.Now, CreatorUserId = Me,
                Revision = next, PreviousInquiryId = loaded.Id,
                Quotes = loaded.Quotes.Select(q => new SupplierQuote
                {
                    ProviderId = q.ProviderId, QuoteNumber = q.QuoteNumber, QuoteDate = q.QuoteDate, ValidUntil = q.ValidUntil, DeliveryDays = q.DeliveryDays, PaymentTerms = q.PaymentTerms,
                    VatPercent = q.VatPercent, DocumentId = q.DocumentId, Notes = q.Notes, TotalAmount = q.TotalAmount,
                    // the technical judgement is made again on the new revision
                    Lines = q.Lines.Select(l => new SupplierQuoteLine { RequestLineId = l.RequestLineId, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList(),
                }).ToList(),
            };
            _uow.Set<PurchaseInquiry>().Add(copy);
            string inquiryLinkError = await SaveLinkedAsync(async () =>
            {
                loaded.SupersededByInquiryId = copy.Id;
                await CopyAttachmentsAsync(AttachmentOwnerType.Inquiry, loaded.Id, copy.Id, cancellationToken);
            }, cancellationToken);
            if (inquiryLinkError != null) return Bad<InquiryDto>(inquiryLinkError);
            return await GetInquiryAsync(copy.Id, cancellationToken);
        }

        // ------------------------------------------------------------------------------------------ archive

        private async Task<(string Number, string Description, string StatusTitle, string Route, bool Terminal)?> ArchiveTargetAsync(ProcArchiveOwner owner, int id, CancellationToken cancellationToken)
        {
            switch (owner)
            {
                case ProcArchiveOwner.PurchaseRequest:
                    var r = await _uow.Set<PurchaseRequest>().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Description, x.Status }).FirstOrDefaultAsync(cancellationToken);
                    return r == null ? null : (r.Number, r.Description, r.Status.ToString(), "/procurement/requests/" + id,
                        r.Status == PurchaseRequestStatus.Closed || r.Status == PurchaseRequestStatus.Rejected || r.Status == PurchaseRequestStatus.Returned);
                case ProcArchiveOwner.Inquiry:
                    var q = await _uow.Set<PurchaseInquiry>().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Description, x.Status }).FirstOrDefaultAsync(cancellationToken);
                    return q == null ? null : (q.Number, q.Description, q.Status.ToString(), "/procurement/inquiries/" + id,
                        q.Status == InquiryStatus.Cancelled || q.Status == InquiryStatus.Returned);
                case ProcArchiveOwner.PurchaseOrder:
                    var o = await _uow.Set<PurchaseOrder>().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Description, x.Status }).FirstOrDefaultAsync(cancellationToken);
                    return o == null ? null : (o.Number, o.Description, o.Status.ToString(), "/procurement/orders/" + id,
                        o.Status == PurchaseOrderStatus.Closed || o.Status == PurchaseOrderStatus.Cancelled);
                case ProcArchiveOwner.SupplierInvoice:
                    var v = await _uow.Set<SupplierInvoice>().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.InvoiceNumber, x.Status }).FirstOrDefaultAsync(cancellationToken);
                    return v == null ? null : (v.Number, v.InvoiceNumber, v.Status.ToString(), "/procurement/invoices/" + id,
                        v.Status == SupplierInvoiceStatus.Cancelled || (v.Status == SupplierInvoiceStatus.Approved && !_uow.Set<PaymentRequest>().Any(p => p.InvoiceId == id && p.Status != PaymentRequestStatus.Paid && p.Status != PaymentRequestStatus.Rejected)));
                case ProcArchiveOwner.PaymentRequest:
                    var p = await _uow.Set<PaymentRequest>().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Description, x.Status }).FirstOrDefaultAsync(cancellationToken);
                    return p == null ? null : (p.Number, p.Description, p.Status.ToString(), "/procurement/payments/" + id,
                        p.Status == PaymentRequestStatus.Paid || p.Status == PaymentRequestStatus.Rejected);
                default:
                    return null;
            }
        }

        public async Task<ResponseDto<ArchiveRecordDto>> ArchiveAsync(ArchiveDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || !Enum.IsDefined(typeof(ProcArchiveOwner), dto.OwnerType)) return Bad<ArchiveRecordDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(ProcurementRoles.Archivers)) return Bad<ArchiveRecordDto>(NotAllowed);
            string reason = Clean(dto.Reason);
            if (reason == null) return Bad<ArchiveRecordDto>("علت بایگانی را بنویسید.");
            var target = await ArchiveTargetAsync(dto.OwnerType, dto.OwnerId, cancellationToken);
            if (target == null) return ResponseDtoHelper.NotFound<ArchiveRecordDto>("سند پیدا نشد.");
            if (!target.Value.Terminal) return Bad<ArchiveRecordDto>("فقط سند پایان‌یافته (بسته، لغوشده، رد یا برگشت‌شده، پرداخت‌شده) بایگانی می‌شود.");
            ProcArchiveRecord record = await _uow.Set<ProcArchiveRecord>().FirstOrDefaultAsync(a => a.OwnerType == dto.OwnerType && a.OwnerId == dto.OwnerId, cancellationToken);
            if (record != null && record.IsArchived) return Bad<ArchiveRecordDto>("این سند قبلاً بایگانی شده است.");
            if (record == null)
            {
                record = new ProcArchiveRecord { OwnerType = dto.OwnerType, OwnerId = dto.OwnerId };
                _uow.Set<ProcArchiveRecord>().Add(record);
            }
            record.IsArchived = true; record.ArchivedAt = DateTimeOffset.Now; record.ArchivedByUserId = Me; record.ArchiveReason = reason;
            record.RestoredAt = null; record.RestoredByUserId = null; record.RestoreReason = null; record.Times++;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await ToArchiveDtoAsync(record, cancellationToken));
        }

        public async Task<ResponseDto<ArchiveRecordDto>> RestoreAsync(ArchiveDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || !Enum.IsDefined(typeof(ProcArchiveOwner), dto.OwnerType)) return Bad<ArchiveRecordDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(ProcurementRoles.Archivers)) return Bad<ArchiveRecordDto>(NotAllowed);
            string reason = Clean(dto.Reason);
            if (reason == null) return Bad<ArchiveRecordDto>("علت بازگردانی را بنویسید.");
            ProcArchiveRecord record = await _uow.Set<ProcArchiveRecord>().FirstOrDefaultAsync(a => a.OwnerType == dto.OwnerType && a.OwnerId == dto.OwnerId, cancellationToken);
            if (record == null || !record.IsArchived) return Bad<ArchiveRecordDto>("این سند در بایگانی نیست.");
            record.IsArchived = false; record.RestoredAt = DateTimeOffset.Now; record.RestoredByUserId = Me; record.RestoreReason = reason;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await ToArchiveDtoAsync(record, cancellationToken));
        }

        private async Task<ArchiveRecordDto> ToArchiveDtoAsync(ProcArchiveRecord a, CancellationToken cancellationToken)
        {
            var target = await ArchiveTargetAsync(a.OwnerType, a.OwnerId, cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { a.ArchivedByUserId, a.RestoredByUserId }, cancellationToken);
            return new ArchiveRecordDto
            {
                OwnerType = a.OwnerType, OwnerId = a.OwnerId, Number = target?.Number, Description = target?.Description, StatusTitle = target?.StatusTitle, Route = target?.Route,
                IsArchived = a.IsArchived, ArchivedAt = a.ArchivedAt, ArchivedByName = names.Name(a.ArchivedByUserId), ArchiveReason = a.ArchiveReason,
                RestoredAt = a.RestoredAt, RestoredByName = names.Name(a.RestoredByUserId), RestoreReason = a.RestoreReason, Times = a.Times,
            };
        }

        public async Task<ResponseDto<PagedList<ArchiveRecordDto>>> GetArchiveAsync(ArchiveQuery query, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProcurementRoles.Archivers + ",CEO,AUDITOR")) return Bad<PagedList<ArchiveRecordDto>>(NotAllowed);
            query ??= new ArchiveQuery();
            IQueryable<ProcArchiveRecord> records = _uow.Set<ProcArchiveRecord>().AsNoTracking();
            if (query.OwnerType.HasValue) records = records.Where(a => a.OwnerType == query.OwnerType.Value);
            if (query.OnlyArchived != false) records = records.Where(a => a.IsArchived);
            List<ProcArchiveRecord> all = await records.OrderByDescending(a => a.ArchivedAt).Take(2000).ToListAsync(cancellationToken);
            var list = new List<ArchiveRecordDto>();
            foreach (ProcArchiveRecord a in all) list.Add(await ToArchiveDtoAsync(a, cancellationToken));
            string text = Clean(query.Contains);
            if (text != null) list = list.Where(x => (x.Number ?? "").Contains(text) || (x.Description ?? "").Contains(text) || (x.ArchiveReason ?? "").Contains(text)).ToList();
            return ResponseDtoHelper.Ok(await PagedList<ArchiveRecordDto>.CreateAsync(list, query.PageNumber, query.PageSize, cancellationToken));
        }

        // ------------------------------------------------------------------------------------------ legacy buy orders (frozen) and the bridge

        public async Task<ResponseDto<List<LegacyBuyOrderDto>>> GetLegacyBuyOrdersAsync(bool openOnly, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProcurementRoles.Planning + ",COMMERCIAL,CEO")) return Bad<List<LegacyBuyOrderDto>>(NotAllowed);
            var rows = await _uow.Set<BuyOrder>().AsNoTracking().Where(b => !b.Deleted)
                .Select(b => new { b.Id, b.Title, b.Status, b.WorkOrderId, WorkOrderCode = b.WorkOrder.Code, b.Count, b.ExitCommodityCode, b.EnterCommodityCode, b.FinancePrice, b.RegisterDate })
                .OrderByDescending(b => b.Id).Take(1000).ToListAsync(cancellationToken);
            List<int> ids = rows.Select(b => b.Id).ToList();
            var bridges = await _uow.Set<ProcBuyOrderBridge>().AsNoTracking().Where(b => ids.Contains(b.BuyOrderId))
                .Select(b => new { b.BuyOrderId, b.PurchaseRequestId, RequestNumber = b.PurchaseRequest.Number, b.PurchaseOrderId }).ToListAsync(cancellationToken);
            var viaLines = await _uow.Set<PurchaseOrderLine>().AsNoTracking()
                .Where(l => l.RequestLine.BuyOrderId.HasValue && ids.Contains(l.RequestLine.BuyOrderId.Value) && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled)
                .Select(l => new { BuyOrderId = l.RequestLine.BuyOrderId.Value, l.PurchaseOrderId, l.PurchaseOrder.Number }).Distinct().ToListAsync(cancellationToken);
            var list = new List<LegacyBuyOrderDto>();
            foreach (var b in rows)
            {
                bool open = (int)b.Status < 90;
                if (openOnly && !open) continue;
                var bridge = bridges.FirstOrDefault(x => x.BuyOrderId == b.Id);
                var order = viaLines.FirstOrDefault(x => x.BuyOrderId == b.Id);
                list.Add(new LegacyBuyOrderDto
                {
                    Id = b.Id, Title = b.Title, Status = (int)b.Status, StatusTitle = b.Status.ToString(), IsOpen = open, WorkOrderId = b.WorkOrderId, WorkOrderCode = b.WorkOrderCode, Count = b.Count,
                    ExitCommodityCode = b.ExitCommodityCode, EnterCommodityCode = b.EnterCommodityCode, FinancePrice = b.FinancePrice, RegisterDate = b.RegisterDate,
                    PurchaseRequestId = bridge?.PurchaseRequestId, PurchaseRequestNumber = bridge?.RequestNumber, PurchaseOrderId = bridge?.PurchaseOrderId ?? order?.PurchaseOrderId, PurchaseOrderNumber = order?.Number,
                });
            }
            return ResponseDtoHelper.Ok(list);
        }

        /// <summary>
        /// Converts one OPEN legacy buy order into a draft purchase request of the new chain (the lines keep the link to the legacy order). The legacy order is not changed.
        /// The request is a draft of the person who converts it: it goes through the same approvals as any other request.
        /// </summary>
        public async Task<ResponseDto<ConvertBuyOrderResultDto>> ConvertBuyOrderAsync(ConvertBuyOrderDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<ConvertBuyOrderResultDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(ProcurementRoles.Planning)) return Bad<ConvertBuyOrderResultDto>(NotAllowed + " (برنامه‌ریزی و کنترل پروژه)");
            BuyOrder buy = await _uow.Set<BuyOrder>().Include(b => b.WorkOrder).FirstOrDefaultAsync(b => b.Id == dto.BuyOrderId && !b.Deleted, cancellationToken);
            if (buy == null) return ResponseDtoHelper.NotFound<ConvertBuyOrderResultDto>("سفارش خرید قدیمی پیدا نشد.");
            if ((int)buy.Status >= 90) return Bad<ConvertBuyOrderResultDto>("فقط سفارش خرید قدیمی باز تبدیل می‌شود؛ این سفارش پایان یافته است.");
            if (await _uow.Set<ProcBuyOrderBridge>().AnyAsync(b => b.BuyOrderId == buy.Id, cancellationToken)) return Bad<ConvertBuyOrderResultDto>("این سفارش قبلاً تبدیل شده است.");
            InventoryItem item = null;
            if (dto.ItemId.HasValue)
            {
                item = await _uow.Set<InventoryItem>().FirstOrDefaultAsync(i => i.Id == dto.ItemId.Value && !i.Deleted && i.IsActive, cancellationToken);
                if (item == null) return Bad<ConvertBuyOrderResultDto>("کالا پیدا نشد یا غیرفعال است.");
            }
            decimal quantity = Math.Max(1, buy.Count);
            decimal? price = dto.UnitPrice ?? (buy.FinancePrice > 0 ? Math.Round(buy.FinancePrice / quantity, 0) : (decimal?)null);
            string title = Clean(buy.Title) ?? "سفارش خرید " + buy.Id;
            var request = new PurchaseRequest
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, ProcurementNumbers.PurchaseRequest, "PRQ", DateTime.Today, cancellationToken),
                Source = PurchaseRequestSource.Bom, Kind = item != null ? PurchaseKind.Goods : PurchaseKind.Service,
                FormType = item != null ? PurchaseFormType.GoodsProject : PurchaseFormType.ServiceProject, Status = PurchaseRequestStatus.Draft, RequestDate = DateTime.Today,
                WorkOrderId = buy.WorkOrderId, ContractId = buy.WorkOrder?.ContractId, Description = "تبدیل از سفارش خرید قدیمی " + buy.Id + ": " + title + (Clean(dto.Note) != null ? " - " + Clean(dto.Note) : ""),
                CreateDate = DateTimeOffset.Now, CreatorUserId = Me, Priority = PurchaseRequestPriority.Normal, Revision = 1,
                Lines = new List<PurchaseRequestLine>
                {
                    new PurchaseRequestLine
                    {
                        RowNumber = 1, ItemId = item?.Id, Description = item?.Title ?? title, Unit = item?.Unit ?? "عدد", Quantity = quantity, EstimatedUnitPrice = price,
                        TechnicalRequirements = Clean(buy.EnterCommodityCode) == null ? null : "کد کالای سفارش قدیمی: " + buy.EnterCommodityCode, BuyOrderId = buy.Id,
                    },
                },
            };
            request.EstimatedAmount = Math.Round(quantity * (price ?? 0), 0);
            _uow.Set<PurchaseRequest>().Add(request);
            string bridgeError = await SaveLinkedAsync(() =>
            {
                _uow.Set<ProcBuyOrderBridge>().Add(new ProcBuyOrderBridge { BuyOrderId = buy.Id, PurchaseRequestId = request.Id, Note = Clean(dto.Note), CreateDate = DateTimeOffset.Now, CreatorUserId = Me });
                return Task.CompletedTask;
            }, cancellationToken);
            if (bridgeError != null) return Bad<ConvertBuyOrderResultDto>("این سفارش قدیمی هم‌زمان تبدیل شد یا قبلاً تبدیل شده است.");
            return ResponseDtoHelper.Ok(new ConvertBuyOrderResultDto
            {
                BuyOrderId = buy.Id, PurchaseRequestId = request.Id, PurchaseRequestNumber = request.Number,
                Message = "پیش‌نویس درخواست خرید ساخته شد؛ مسیر تایید و استعلام از زنجیره جدید ادامه می‌یابد.",
            });
        }

        /// <summary>M13: two saves that belong together (the new row, then the link) in one transaction; a concurrent second call gets a message instead of an exception and no orphan row stays.</summary>
        private async Task<string> SaveLinkedAsync(Func<Task> second, CancellationToken cancellationToken)
        {
            const string conflict = "هم‌زمان کار دیگری روی همین سند انجام شد؛ صفحه را تازه کنید و دوباره تلاش کنید.";
            try
            {
                if (!(_uow is Microsoft.EntityFrameworkCore.DbContext db) || !db.Database.IsRelational())
                {
                    await _uow.SaveChangesAsync(cancellationToken);
                    await second();
                    await _uow.SaveChangesAsync(cancellationToken);
                    return null;
                }
                int attempt = 0;
                string result = null;
                await db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
                {
                    if (attempt++ > 0) { result = conflict; return; }
                    await using var transaction = await db.Database.BeginTransactionAsync(token);
                    await _uow.SaveChangesAsync(token);
                    await second();
                    await _uow.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                }, cancellationToken);
                return result;
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException) { return conflict; }
        }

        /// <summary>M-attachments: the files of the old revision are visible on the new one (the same stored document, a new row; the old revision keeps its own).</summary>
        private async Task CopyAttachmentsAsync(AttachmentOwnerType ownerType, int fromId, int toId, CancellationToken cancellationToken)
        {
            List<RecordAttachment> files = await _uow.Set<RecordAttachment>().AsNoTracking()
                .Where(a => a.OwnerType == ownerType && a.OwnerId == fromId && !a.Deleted && a.RemovedAt == null && !a.Voided && a.SupersededById == null).ToListAsync(cancellationToken);
            foreach (RecordAttachment a in files)
            {
                _uow.Set<RecordAttachment>().Add(new RecordAttachment
                {
                    OwnerType = ownerType, OwnerId = toId, Kind = a.Kind, DocumentId = a.DocumentId, Title = a.Title, Revision = a.Revision, FileName = a.FileName, SizeBytes = a.SizeBytes,
                    UploadedByUserId = a.UploadedByUserId, UploadedAt = a.UploadedAt, CreateDate = DateTimeOffset.Now, CreatorUserId = Me,
                });
            }
        }

        /// <summary>The lines of a revised order against the first frozen revision (what the supplier first received): quantity and unit price may move by the limit at most.</summary>
        private async Task<string> RevisionVolumeErrorAsync(PurchaseOrder o, decimal limitPercent, CancellationToken cancellationToken)
        {
            string json = await _uow.Set<PurchaseOrderRevision>().AsNoTracking().Where(r => r.PurchaseOrderId == o.Id).OrderBy(r => r.Revision).Select(r => r.SnapshotJson).FirstOrDefaultAsync(cancellationToken);
            if (string.IsNullOrEmpty(json)) return null;
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (System.Text.Json.JsonElement l in doc.RootElement.GetProperty("Lines").EnumerateArray())
            {
                int row = l.GetProperty("RowNumber").GetInt32();
                PurchaseOrderLine now = o.Lines.FirstOrDefault(x => x.RowNumber == row);
                if (now == null) return $"ردیف {row} نسبت به قرارداد امضاشده حذف شده؛ تغییر ساختار قرارداد الحاقیه می‌خواهد.";
                decimal q0 = l.GetProperty("Quantity").GetDecimal(), p0 = l.GetProperty("UnitPrice").GetDecimal();
                if (q0 > 0 && Math.Abs(now.Quantity - q0) / q0 * 100m > limitPercent) return $"ردیف {row}: تغییر حجم از {limitPercent:0.##} درصد قرارداد بیشتر است؛ الحاقیه لازم است.";
                if (p0 > 0 && Math.Abs(now.UnitPrice - p0) / p0 * 100m > limitPercent) return $"ردیف {row}: تغییر قیمت از {limitPercent:0.##} درصد قرارداد بیشتر است؛ الحاقیه لازم است.";
            }
            return null;
        }
    }
}
