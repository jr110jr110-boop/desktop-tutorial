using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.AttachmentDtos;

namespace Services.NasbNiro.Attachments
{
    public interface IDossierService
    {
        /// <summary>
        /// پرونده درخواست: any number of the chain (purchase request, inquiry, purchase order, warehouse receipt, inspection, NCR, supplier invoice, payment request, treasury payment, petty-cash settlement)
        /// gives the whole chain, step by step, with status, dates, users and every attachment. A step the caller may not read is left out and only counted. The number is matched exactly against our own numbers;
        /// the supplier's own invoice number only when asked for (<c>kind</c> = SupplierInvoiceNumber); several matches come back as choices.
        /// </summary>
        Task<ResponseDto<DossierDto>> GetAsync(string number, CancellationToken ct, string kind = null);
    }

    public sealed class DossierService : IDossierService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IAttachmentService _attachments;

        public DossierService(IUnitOfWork uow, IInventoryUserContext user, IAttachmentService attachments)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _attachments = attachments ?? throw new ArgumentNullException(nameof(attachments));
        }

        private bool SeesPrices => _user.IsInRole(AttachmentRoles.Prices);

        /// <summary>The set of records found for one request.</summary>
        private sealed class Chain
        {
            public HashSet<int> Requests = new HashSet<int>();
            public HashSet<int> Inquiries = new HashSet<int>();
            public HashSet<int> Orders = new HashSet<int>();
            public HashSet<int> Documents = new HashSet<int>();
            public HashSet<int> Inspections = new HashSet<int>();
            public HashSet<int> Ncrs = new HashSet<int>();
            public HashSet<int> Invoices = new HashSet<int>();
            public HashSet<int> Payments = new HashSet<int>();
            public HashSet<int> Treasury = new HashSet<int>();
            public HashSet<int> Settlements = new HashSet<int>();
        }

        public const string SupplierInvoiceNumberKind = "SupplierInvoiceNumber";

        private bool CanRead(AttachmentOwnerType type) { string roles = AttachmentOwners.ReadRolesOf(type); return roles == null || _user.IsInRole(roles); }

        private sealed class Match
        {
            public AttachmentOwnerType Type;
            public string Kind;
            public string Label;
            public int Id;
            public int? RequestId;
            public string Number;
            public string Description;
        }

        public async Task<ResponseDto<DossierDto>> GetAsync(string number, CancellationToken ct, string kind = null)
        {
            string text = string.IsNullOrWhiteSpace(number) ? null : number.Trim();
            if (text == null) return ResponseDtoHelper.BadRequest<DossierDto>(AttachmentText.NumberRequired);
            if (!_user.IsInRole(AttachmentRoles.TechnicalChain)) return ResponseDtoHelper.Forbidden<DossierDto>(AttachmentText.NotAllowed);

            List<Match> matches = await FindAsync(text, string.IsNullOrWhiteSpace(kind) ? null : kind.Trim(), ct);
            if (matches.Count == 0) return ResponseDtoHelper.NotFound<DossierDto>(AttachmentText.DossierNotFound);
            // a record the caller may not read is not even confirmed to exist: the standard refusal
            List<Match> readable = matches.Where(m => CanRead(m.Type)).ToList();
            if (readable.Count == 0) return ResponseDtoHelper.Forbidden<DossierDto>(AttachmentText.NotAllowed);

            var dossier = new DossierDto { Query = text, GeneratedAt = DateTimeOffset.Now };
            if (readable.Count > 1)
            {
                dossier.FoundAs = "چند سند با این شماره پیدا شد؛ یکی را انتخاب کنید.";
                dossier.Choices = readable.Select(m => new DossierChoiceDto { Kind = m.Kind, Title = m.Label, Number = m.Number, Description = m.Description }).ToList();
                return ResponseDtoHelper.Ok(dossier);
            }

            Match found = readable[0];
            var chain = new Chain();
            AddToChain(chain, found);
            dossier.FoundAs = found.Label;
            await ExpandAsync(chain, ct);
            int hidden = await BuildStepsAsync(dossier, chain, ct);
            dossier.StepCount = dossier.Steps.Count;
            dossier.HiddenStepCount = hidden;
            if (hidden > 0) dossier.HiddenStepsText = hidden + " مرحله بدون دسترسی";
            dossier.RecordCount = dossier.Steps.Sum(s => s.Records.Count);
            dossier.AttachmentCount = dossier.Steps.Sum(s => s.Records.Sum(r => r.Attachments.Count));
            dossier.GeneratedBy = (await InventoryUsers.GetNamesAsync(_uow, new int?[] { _user.UserId }, ct)).Name(_user.UserId);
            return ResponseDtoHelper.Ok(dossier);
        }

        private static void AddToChain(Chain c, Match m)
        {
            switch (m.Type)
            {
                case AttachmentOwnerType.PurchaseRequest: c.Requests.Add(m.Id); break;
                case AttachmentOwnerType.Inquiry: c.Inquiries.Add(m.Id); if (m.RequestId.HasValue) c.Requests.Add(m.RequestId.Value); break;
                case AttachmentOwnerType.PurchaseOrder: c.Orders.Add(m.Id); break;
                case AttachmentOwnerType.WarehouseDocument: c.Documents.Add(m.Id); break;
                case AttachmentOwnerType.QcInspection: c.Inspections.Add(m.Id); break;
                case AttachmentOwnerType.Ncr: c.Ncrs.Add(m.Id); break;
                case AttachmentOwnerType.SupplierInvoice: c.Invoices.Add(m.Id); break;
                case AttachmentOwnerType.PaymentRequest: c.Payments.Add(m.Id); break;
                case AttachmentOwnerType.TreasuryPayment: c.Treasury.Add(m.Id); break;
                case AttachmentOwnerType.PettyCashSettlementLine: c.Settlements.Add(m.Id); break;
            }
        }

        // ------------------------------------------------------------------------------------------ find the starting record

        /// <summary>
        /// Exact match on our own numbers, every record type, deterministic order (type, then id). The supplier's own invoice number is searched only when the caller asks for it with kind = SupplierInvoiceNumber.
        /// A kind equal to an owner type name limits the search to that type.
        /// </summary>
        private async Task<List<Match>> FindAsync(string text, string kind, CancellationToken ct)
        {
            var list = new List<Match>();
            bool Wants(string k) => kind == null || string.Equals(kind, k, StringComparison.OrdinalIgnoreCase);
            bool invoiceNumberOnly = string.Equals(kind, SupplierInvoiceNumberKind, StringComparison.OrdinalIgnoreCase);
            if (!invoiceNumberOnly)
            {
                if (Wants("PurchaseRequest"))
                    foreach (var x in await _uow.Set<PurchaseRequest>().AsNoTracking().Where(x => x.Number == text && !x.Deleted).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number }).ToListAsync(ct))
                        list.Add(new Match { Type = AttachmentOwnerType.PurchaseRequest, Kind = "PurchaseRequest", Label = "درخواست خرید", Id = x.Id, Number = x.Number });
                if (Wants("Inquiry"))
                    foreach (var x in await _uow.Set<PurchaseInquiry>().AsNoTracking().Where(x => x.Number == text && !x.Deleted).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number, x.PurchaseRequestId }).ToListAsync(ct))
                        list.Add(new Match { Type = AttachmentOwnerType.Inquiry, Kind = "Inquiry", Label = "استعلام", Id = x.Id, RequestId = x.PurchaseRequestId, Number = x.Number });
                if (Wants("PurchaseOrder"))
                    foreach (var x in await _uow.Set<PurchaseOrder>().AsNoTracking().Where(x => x.Number == text && !x.Deleted).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number }).ToListAsync(ct))
                        list.Add(new Match { Type = AttachmentOwnerType.PurchaseOrder, Kind = "PurchaseOrder", Label = "سفارش خرید", Id = x.Id, Number = x.Number });
                if (Wants("WarehouseDocument"))
                    foreach (var x in await _uow.Set<InventoryDocument>().AsNoTracking().Where(x => x.Number == text && !x.Deleted).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number }).ToListAsync(ct))
                        list.Add(new Match { Type = AttachmentOwnerType.WarehouseDocument, Kind = "WarehouseDocument", Label = "سند انبار", Id = x.Id, Number = x.Number });
                if (Wants("QcInspection"))
                    foreach (var x in await _uow.Set<QcInspection>().AsNoTracking().Where(x => x.Number == text && !x.Deleted).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number }).ToListAsync(ct))
                        list.Add(new Match { Type = AttachmentOwnerType.QcInspection, Kind = "QcInspection", Label = "بازرسی کیفی ورودی", Id = x.Id, Number = x.Number });
                if (Wants("Ncr"))
                    foreach (var x in await _uow.Set<NonConformance>().AsNoTracking().Where(x => x.Number == text && !x.Deleted).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number }).ToListAsync(ct))
                        list.Add(new Match { Type = AttachmentOwnerType.Ncr, Kind = "Ncr", Label = "گزارش عدم انطباق", Id = x.Id, Number = x.Number });
                if (Wants("SupplierInvoice"))
                    foreach (var x in await _uow.Set<SupplierInvoice>().AsNoTracking().Where(x => x.Number == text && !x.Deleted).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number }).ToListAsync(ct))
                        list.Add(new Match { Type = AttachmentOwnerType.SupplierInvoice, Kind = "SupplierInvoice", Label = "فاکتور تامین‌کننده", Id = x.Id, Number = x.Number });
                if (Wants("PaymentRequest"))
                    foreach (var x in await _uow.Set<PaymentRequest>().AsNoTracking().Where(x => x.Number == text && !x.Deleted).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number }).ToListAsync(ct))
                        list.Add(new Match { Type = AttachmentOwnerType.PaymentRequest, Kind = "PaymentRequest", Label = "درخواست پرداخت", Id = x.Id, Number = x.Number });
                if (Wants("TreasuryPayment"))
                    foreach (var x in await _uow.Set<FnPayment>().AsNoTracking().Where(x => x.Number == text).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number }).ToListAsync(ct))
                        list.Add(new Match { Type = AttachmentOwnerType.TreasuryPayment, Kind = "TreasuryPayment", Label = "پرداخت خزانه", Id = x.Id, Number = x.Number });
                if (Wants("PettyCashSettlementLine"))
                    foreach (var x in await _uow.Set<FnPettyCashSettlement>().AsNoTracking().Where(x => x.Number == text).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number }).ToListAsync(ct))
                        list.Add(new Match { Type = AttachmentOwnerType.PettyCashSettlementLine, Kind = "PettyCashSettlementLine", Label = "تسویه تنخواه", Id = x.Id, Number = x.Number });
            }
            else
            {
                // the supplier's own number can repeat across suppliers: every match is offered with our number and the supplier
                foreach (var x in await _uow.Set<SupplierInvoice>().AsNoTracking().Where(x => x.InvoiceNumber == text && !x.Deleted).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number, Provider = x.Provider.Name }).ToListAsync(ct))
                    list.Add(new Match { Type = AttachmentOwnerType.SupplierInvoice, Kind = "SupplierInvoice", Label = "فاکتور تامین‌کننده", Id = x.Id, Number = x.Number, Description = "شماره فاکتور تامین‌کننده " + text + " - " + x.Provider });
            }
            return list;
        }

        // ------------------------------------------------------------------------------------------ walk to the rest of the chain

        private async Task ExpandAsync(Chain c, CancellationToken ct)
        {
            // upstream: from any record to the purchase orders and the requests
            if (c.Treasury.Count > 0)
                foreach (int p in await _uow.Set<FnPaymentRequestLink>().AsNoTracking().Where(l => c.Treasury.Contains(l.PaymentId)).Select(l => l.PaymentRequestId).ToListAsync(ct)) c.Payments.Add(p);
            if (c.Settlements.Count > 0)
                foreach (int d in await _uow.Set<FnPettyCashSettlementLine>().AsNoTracking().Where(l => c.Settlements.Contains(l.SettlementId) && l.InventoryDocumentId != null).Select(l => l.InventoryDocumentId.Value).ToListAsync(ct)) c.Documents.Add(d);
            if (c.Payments.Count > 0)
            {
                var pays = await _uow.Set<PaymentRequest>().AsNoTracking().Where(p => c.Payments.Contains(p.Id)).Select(p => new { p.PurchaseOrderId, p.InvoiceId }).ToListAsync(ct);
                foreach (var p in pays) { if (p.PurchaseOrderId.HasValue) c.Orders.Add(p.PurchaseOrderId.Value); if (p.InvoiceId.HasValue) c.Invoices.Add(p.InvoiceId.Value); }
            }
            if (c.Invoices.Count > 0)
                foreach (int o in await _uow.Set<SupplierInvoice>().AsNoTracking().Where(v => c.Invoices.Contains(v.Id)).Select(v => v.PurchaseOrderId).ToListAsync(ct)) c.Orders.Add(o);
            if (c.Ncrs.Count > 0)
            {
                var ncrs = await _uow.Set<NonConformance>().AsNoTracking().Where(n => c.Ncrs.Contains(n.Id)).Select(n => new { n.InspectionId, n.ReceiptDocumentId }).ToListAsync(ct);
                foreach (var n in ncrs) { if (n.InspectionId.HasValue) c.Inspections.Add(n.InspectionId.Value); if (n.ReceiptDocumentId.HasValue) c.Documents.Add(n.ReceiptDocumentId.Value); }
            }
            if (c.Inspections.Count > 0)
                foreach (int d in await _uow.Set<QcInspection>().AsNoTracking().Where(i => c.Inspections.Contains(i.Id)).Select(i => i.ReceiptDocumentId).ToListAsync(ct)) c.Documents.Add(d);
            if (c.Documents.Count > 0)
                foreach (int o in await AttachmentOwners.OrderIdsOfDocumentsAsync(_uow, c.Documents, ct)) c.Orders.Add(o);
            if (c.Orders.Count > 0)
            {
                var facts = new OwnerFacts();
                await AttachmentOwners.AddOrderChainAsync(_uow, facts, c.Orders, ct);
                foreach (int pr in facts.PurchaseRequestIds) c.Requests.Add(pr);
            }
            if (c.Inquiries.Count > 0)
                foreach (int pr in await _uow.Set<PurchaseInquiry>().AsNoTracking().Where(q => c.Inquiries.Contains(q.Id)).Select(q => q.PurchaseRequestId).ToListAsync(ct)) c.Requests.Add(pr);

            // downstream: from the requests to everything that was made for them
            if (c.Requests.Count > 0)
            {
                foreach (int q in await _uow.Set<PurchaseInquiry>().AsNoTracking().Where(q => c.Requests.Contains(q.PurchaseRequestId) && !q.Deleted).Select(q => q.Id).ToListAsync(ct)) c.Inquiries.Add(q);
                foreach (int o in await _uow.Set<PurchaseOrder>().AsNoTracking().Where(o => !o.Deleted && ((o.InquiryId != null && c.Inquiries.Contains(o.InquiryId.Value)) || o.Lines.Any(l => l.RequestLine != null && c.Requests.Contains(l.RequestLine.PurchaseRequestId)))).Select(o => o.Id).ToListAsync(ct)) c.Orders.Add(o);
            }
            if (c.Orders.Count > 0)
            {
                foreach (int d in await _uow.Set<InventoryDocument>().AsNoTracking().Where(d => !d.Deleted && ((d.PurchaseOrderId != null && c.Orders.Contains(d.PurchaseOrderId.Value)) || d.Lines.Any(l => l.PurchaseOrderLine != null && c.Orders.Contains(l.PurchaseOrderLine.PurchaseOrderId)))).Select(d => d.Id).ToListAsync(ct)) c.Documents.Add(d);
                foreach (int v in await _uow.Set<SupplierInvoice>().AsNoTracking().Where(v => !v.Deleted && c.Orders.Contains(v.PurchaseOrderId)).Select(v => v.Id).ToListAsync(ct)) c.Invoices.Add(v);
            }
            if (c.Documents.Count > 0)
            {
                var insp = await _uow.Set<QcInspection>().AsNoTracking().Where(i => !i.Deleted && c.Documents.Contains(i.ReceiptDocumentId)).Select(i => new { i.Id, i.ReleaseDocumentId }).ToListAsync(ct);
                foreach (var i in insp) { c.Inspections.Add(i.Id); if (i.ReleaseDocumentId.HasValue) c.Documents.Add(i.ReleaseDocumentId.Value); }
                foreach (int n in await _uow.Set<NonConformance>().AsNoTracking().Where(n => !n.Deleted && ((n.ReceiptDocumentId != null && c.Documents.Contains(n.ReceiptDocumentId.Value)) || (n.InspectionId != null && c.Inspections.Contains(n.InspectionId.Value)))).Select(n => n.Id).ToListAsync(ct)) c.Ncrs.Add(n);
                foreach (int s in await _uow.Set<FnPettyCashSettlementLine>().AsNoTracking().Where(l => l.InventoryDocumentId != null && c.Documents.Contains(l.InventoryDocumentId.Value)).Select(l => l.SettlementId).ToListAsync(ct)) c.Settlements.Add(s);
            }
            if (c.Orders.Count > 0 || c.Invoices.Count > 0)
                foreach (int p in await _uow.Set<PaymentRequest>().AsNoTracking().Where(p => !p.Deleted && ((p.PurchaseOrderId != null && c.Orders.Contains(p.PurchaseOrderId.Value)) || (p.InvoiceId != null && c.Invoices.Contains(p.InvoiceId.Value)))).Select(p => p.Id).ToListAsync(ct)) c.Payments.Add(p);
            if (c.Payments.Count > 0)
                foreach (int t in await _uow.Set<FnPaymentRequestLink>().AsNoTracking().Where(l => c.Payments.Contains(l.PaymentRequestId)).Select(l => l.PaymentId).ToListAsync(ct)) c.Treasury.Add(t);
        }

        // ------------------------------------------------------------------------------------------ the steps

        /// <summary>Builds the steps the caller may read; returns the number of steps that hold records the caller may not read.</summary>
        private async Task<int> BuildStepsAsync(DossierDto d, Chain c, CancellationToken ct)
        {
            var owners = new List<KeyValuePair<AttachmentOwnerType, int>>();
            var records = new Dictionary<string, List<DossierRecordDto>>();
            var userIds = new List<int?>();
            var hiddenSteps = new HashSet<string>();
            void Add(string step, DossierRecordDto r, int? creator, int? approver)
            {
                // every step is authorised with the read rule of its own record type: a step the caller cannot read is not described, only counted
                if (!CanRead(r.Type)) { hiddenSteps.Add(step); return; }
                if (!records.TryGetValue(step, out List<DossierRecordDto> list)) records[step] = list = new List<DossierRecordDto>();
                list.Add(r);
                if (step != "PettyCash") owners.Add(new KeyValuePair<AttachmentOwnerType, int>(r.Type, r.Id));   // the petty-cash record carries the settlement id; its files hang on the lines
                userIds.Add(creator); userIds.Add(approver);
                r.CreatedByName = creator?.ToString(); r.ApprovedByName = approver?.ToString();   // replaced by the names below
            }
            decimal? Money(decimal v) => SeesPrices ? v : (decimal?)null;

            // children of the chain, loaded once for all records (no query per record)
            var requestLines = await _uow.Set<PurchaseRequestLine>().AsNoTracking().Where(l => c.Requests.Contains(l.PurchaseRequestId)).Select(l => new { l.Id, l.PurchaseRequestId }).ToListAsync(ct);
            var quotes = await _uow.Set<SupplierQuote>().AsNoTracking().Where(x => c.Inquiries.Contains(x.InquiryId)).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.InquiryId, x.QuoteNumber, x.QuoteDate, x.TotalAmount, x.TechnicallyAccepted, x.TechnicalReviewedByUserId, x.TechnicalReviewedAt, Provider = x.Provider.Name }).ToListAsync(ct);
            var settlementLines = await _uow.Set<FnPettyCashSettlementLine>().AsNoTracking().Where(l => c.Settlements.Contains(l.SettlementId)).Select(l => new { l.Id, l.SettlementId }).ToListAsync(ct);

            foreach (var r in await _uow.Set<PurchaseRequest>().AsNoTracking().Where(x => c.Requests.Contains(x.Id) && !x.Deleted).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number, x.Status, x.RequestDate, x.Description, x.CreatorUserId, x.ManagementApprovedByUserId, x.PlanningApprovedByUserId, x.PlanningApprovedAt, x.EstimatedAmount }).ToListAsync(ct))
            {
                Add("PurchaseRequest", new DossierRecordDto { Type = AttachmentOwnerType.PurchaseRequest, Id = r.Id, Number = r.Number, Status = Persian(r.Status), Date = r.RequestDate, Description = r.Description, ApprovedAt = r.PlanningApprovedAt, Amount = Money(r.EstimatedAmount), Route = "/procurement/requests/" + r.Id },
                    r.CreatorUserId, r.PlanningApprovedByUserId);
                if (CanRead(AttachmentOwnerType.PurchaseRequestLine))
                    foreach (var line in requestLines.Where(l => l.PurchaseRequestId == r.Id)) owners.Add(new KeyValuePair<AttachmentOwnerType, int>(AttachmentOwnerType.PurchaseRequestLine, line.Id));
            }
            foreach (var q in await _uow.Set<PurchaseInquiry>().AsNoTracking().Where(x => c.Inquiries.Contains(x.Id) && !x.Deleted).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number, x.Status, x.CreateDate, x.Description, x.CreatorUserId, x.ManagementApprovedByUserId, x.ManagementApprovedAt }).ToListAsync(ct))
            {
                Add("Inquiry", new DossierRecordDto { Type = AttachmentOwnerType.Inquiry, Id = q.Id, Number = q.Number, Status = Persian(q.Status), Date = q.CreateDate.DateTime, Description = q.Description, ApprovedAt = q.ManagementApprovedAt, Route = "/procurement/inquiries/" + q.Id }, q.CreatorUserId, q.ManagementApprovedByUserId);
                foreach (var quote in quotes.Where(x => x.InquiryId == q.Id))
                    Add("Quote", new DossierRecordDto { Type = AttachmentOwnerType.Quote, Id = quote.Id, Number = (q.Number) + " / " + (quote.QuoteNumber ?? quote.Provider), Status = quote.TechnicallyAccepted ? "تایید فنی" : "در انتظار بررسی فنی", Date = quote.QuoteDate, Description = quote.Provider, ApprovedAt = quote.TechnicalReviewedAt, Amount = Money(quote.TotalAmount), Route = "/procurement/inquiries/" + q.Id }, null, quote.TechnicalReviewedByUserId);
            }
            foreach (var o in await _uow.Set<PurchaseOrder>().AsNoTracking().Where(x => c.Orders.Contains(x.Id) && !x.Deleted).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number, x.Status, x.OrderDate, x.CreatorUserId, x.ApprovedByUserId, x.ApprovedAt, Provider = x.Provider.Name, Total = x.Lines.Sum(l => l.Quantity * l.UnitPrice) }).ToListAsync(ct))
                Add("PurchaseOrder", new DossierRecordDto { Type = AttachmentOwnerType.PurchaseOrder, Id = o.Id, Number = o.Number, Status = Persian(o.Status), Date = o.OrderDate, Description = o.Provider, ApprovedAt = o.ApprovedAt, Amount = Money(o.Total), Route = "/procurement/orders/" + o.Id }, o.CreatorUserId, o.ApprovedByUserId);
            foreach (var x in await _uow.Set<InventoryDocument>().AsNoTracking().Where(v => c.Documents.Contains(v.Id) && !v.Deleted).OrderBy(v => v.Id).Select(v => new { v.Id, v.Number, v.Status, v.Type, v.DocumentDate, v.Description, v.CreatorUserId, v.ConfirmedByUserId, v.ConfirmedAt }).ToListAsync(ct))
                Add(x.Type == InventoryDocumentType.QcRelease ? "FinalReceipt" : "TemporaryReceipt", new DossierRecordDto { Type = AttachmentOwnerType.WarehouseDocument, Id = x.Id, Number = string.IsNullOrWhiteSpace(x.Number) ? "پیش‌نویس " + x.Id : x.Number, Status = Persian(x.Status), Date = x.DocumentDate, Description = x.Description, ApprovedAt = x.ConfirmedAt, Route = "/inventory/documents/" + x.Id }, x.CreatorUserId, x.ConfirmedByUserId);
            foreach (var i in await _uow.Set<QcInspection>().AsNoTracking().Where(v => c.Inspections.Contains(v.Id) && !v.Deleted).OrderBy(v => v.Id).Select(v => new { v.Id, v.Number, v.Status, v.Note, v.CreatorUserId, v.InspectedByUserId, v.InspectedAt, v.CreateDate }).ToListAsync(ct))
                Add("QcInspection", new DossierRecordDto { Type = AttachmentOwnerType.QcInspection, Id = i.Id, Number = i.Number, Status = i.Status == InspectionStatus.Done ? "انجام شده" : "در انتظار بازرسی", Date = i.CreateDate.DateTime, Description = i.Note, ApprovedAt = i.InspectedAt, Route = "/inventory/inspections?id=" + i.Id }, i.CreatorUserId, i.InspectedByUserId);
            foreach (var n in await _uow.Set<NonConformance>().AsNoTracking().Where(v => c.Ncrs.Contains(v.Id) && !v.Deleted).OrderBy(v => v.Id).Select(v => new { v.Id, v.Number, v.Status, v.ReportDate, v.Description, v.CreatorUserId, v.QualityApprovedByUserId, v.QualityApprovedAt }).ToListAsync(ct))
                Add("Ncr", new DossierRecordDto { Type = AttachmentOwnerType.Ncr, Id = n.Id, Number = n.Number, Status = Persian(n.Status), Date = n.ReportDate, Description = n.Description, ApprovedAt = n.QualityApprovedAt, Route = "/inventory/ncr/" + n.Id }, n.CreatorUserId, n.QualityApprovedByUserId);
            foreach (var v in await _uow.Set<SupplierInvoice>().AsNoTracking().Where(x => c.Invoices.Contains(x.Id) && !x.Deleted).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number, x.Status, x.InvoiceDate, x.InvoiceNumber, x.TotalAmount, x.CreatorUserId, x.ApprovedByUserId, x.ApprovedAt }).ToListAsync(ct))
                Add("SupplierInvoice", new DossierRecordDto { Type = AttachmentOwnerType.SupplierInvoice, Id = v.Id, Number = v.Number, Status = Persian(v.Status), Date = v.InvoiceDate, Description = "شماره فاکتور تامین‌کننده: " + v.InvoiceNumber, ApprovedAt = v.ApprovedAt, Amount = Money(v.TotalAmount), Route = "/procurement/invoices?id=" + v.Id }, v.CreatorUserId, v.ApprovedByUserId);
            foreach (var p in await _uow.Set<PaymentRequest>().AsNoTracking().Where(x => c.Payments.Contains(x.Id) && !x.Deleted).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number, x.Status, x.RequestDate, x.Amount, x.Deductions, x.Description, x.CreatorUserId, x.ApprovedByUserId, x.ApprovedAt, x.DeductionsProposedAt, x.DeductionsConfirmedAt, x.DeductionsConfirmedByUserId }).ToListAsync(ct))
            {
                var rec = new DossierRecordDto { Type = AttachmentOwnerType.PaymentRequest, Id = p.Id, Number = p.Number, Status = Persian(p.Status), Date = p.RequestDate, Description = p.Description, ApprovedAt = p.ApprovedAt, Amount = Money(p.Amount - p.Deductions), Route = "/procurement/payments?id=" + p.Id };
                // finance approves from the dossier without paper: the same conditions the approve action checks (state, role, not the maker, deductions confirmed)
                rec.CanApprovePayment = p.Status == PaymentRequestStatus.Draft && _user.IsInRole(ProcurementRoles.OrderApproval) && p.CreatorUserId != _user.UserId && p.DeductionsConfirmedByUserId != _user.UserId
                    && (!p.DeductionsProposedAt.HasValue || p.DeductionsConfirmedAt.HasValue);
                Add("PaymentRequest", rec, p.CreatorUserId, p.ApprovedByUserId);
            }
            foreach (var t in await _uow.Set<FnPayment>().AsNoTracking().Where(x => c.Treasury.Contains(x.Id)).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number, x.Status, x.Date, x.NetAmount, x.Description, x.CreatedByUserId, x.ApprovedByUserId, x.PostedAt }).ToListAsync(ct))
                Add("TreasuryPayment", new DossierRecordDto { Type = AttachmentOwnerType.TreasuryPayment, Id = t.Id, Number = string.IsNullOrWhiteSpace(t.Number) ? "پیش‌نویس " + t.Id : t.Number, Status = Persian(t.Status), Date = t.Date, Description = t.Description, ApprovedAt = t.PostedAt, Amount = Money(t.NetAmount), Route = "/fn/treasury/payments?id=" + t.Id }, t.CreatedByUserId, t.ApprovedByUserId);
            foreach (var s in await _uow.Set<FnPettyCashSettlement>().AsNoTracking().Where(x => c.Settlements.Contains(x.Id)).OrderBy(x => x.Id).Select(x => new { x.Id, x.Number, x.Status, x.Date, x.TotalAmount, x.CreatedByUserId, x.ApprovedByUserId, x.ApprovedAt }).ToListAsync(ct))
            {
                Add("PettyCash", new DossierRecordDto { Type = AttachmentOwnerType.PettyCashSettlementLine, Id = s.Id, Number = s.Number, Status = Persian(s.Status), Date = s.Date, Description = "تسویه تنخواه", ApprovedAt = s.ApprovedAt, Amount = Money(s.TotalAmount), Route = "/fn/treasury/petty-cash" }, s.CreatedByUserId, s.ApprovedByUserId);
                foreach (var line in settlementLines.Where(l => l.SettlementId == s.Id)) owners.Add(new KeyValuePair<AttachmentOwnerType, int>(AttachmentOwnerType.PettyCashSettlementLine, line.Id));
            }

            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, userIds, ct);
            Dictionary<string, List<AttachmentDto>> files = await _attachments.ForOwnersAsync(owners.Where(o => o.Value > 0), ct);
            // the petty-cash step shows the files of all its lines; the PR step those of the request and its lines

            foreach (var step in StepOrder)
            {
                if (!records.TryGetValue(step.Code, out List<DossierRecordDto> list)) continue;
                foreach (DossierRecordDto r in list)
                {
                    r.CreatedByName = r.CreatedByName != null && int.TryParse(r.CreatedByName, out int cu) ? names.Name(cu) : null;
                    r.ApprovedByName = r.ApprovedByName != null && int.TryParse(r.ApprovedByName, out int au) ? names.Name(au) : null;
                    string key = (int)r.Type + ":" + r.Id;
                    if (step.Code != "PettyCash" && r.Id > 0 && files.TryGetValue(key, out List<AttachmentDto> mine)) r.Attachments.AddRange(mine);
                    if (r.Type == AttachmentOwnerType.PurchaseRequest)
                        foreach (var l in requestLines.Where(l => l.PurchaseRequestId == r.Id))
                            if (files.TryGetValue((int)AttachmentOwnerType.PurchaseRequestLine + ":" + l.Id, out List<AttachmentDto> lf)) r.Attachments.AddRange(lf);
                    if (step.Code == "PettyCash")
                        foreach (var l in settlementLines.Where(l => l.SettlementId == r.Id))
                            if (files.TryGetValue((int)AttachmentOwnerType.PettyCashSettlementLine + ":" + l.Id, out List<AttachmentDto> lf)) r.Attachments.AddRange(lf);
                }
                d.Steps.Add(new DossierStepDto { Order = d.Steps.Count + 1, Code = step.Code, Title = step.Title, Records = list });
            }
            return hiddenSteps.Count;
        }

        private static readonly (string Code, string Title)[] StepOrder =
        {
            ("PurchaseRequest", "۱. درخواست خرید"),
            ("Inquiry", "۲. استعلام"),
            ("Quote", "۳. پیشنهادهای قیمت"),
            ("PurchaseOrder", "۴. سفارش خرید"),
            ("TemporaryReceipt", "۵. رسید انبار موقت"),
            ("QcInspection", "۶. بازرسی کیفی ورودی"),
            ("Ncr", "۷. عدم انطباق"),
            ("FinalReceipt", "۸. رسید انبار نهایی (آزادسازی)"),
            ("SupplierInvoice", "۹. فاکتور تامین‌کننده"),
            ("PaymentRequest", "۱۰. درخواست پرداخت"),
            ("TreasuryPayment", "۱۱. پرداخت خزانه"),
            ("PettyCash", "۱۲. تسویه تنخواه"),
        };

        private static string Persian(PurchaseRequestStatus s) => s switch
        {
            PurchaseRequestStatus.Draft => "پیش‌نویس", PurchaseRequestStatus.Submitted => "ارسال‌شده", PurchaseRequestStatus.Approved => "تایید شده", PurchaseRequestStatus.Ordered => "سفارش داده شده",
            PurchaseRequestStatus.Closed => "بسته", PurchaseRequestStatus.Rejected => "رد شده", _ => s.ToString(),
        };

        private static string Persian(InquiryStatus s) => s switch
        {
            InquiryStatus.Open => "باز", InquiryStatus.Selected => "برگزیده (در انتظار تایید)", InquiryStatus.Approved => "تایید شده", InquiryStatus.Cancelled => "لغو شده", _ => s.ToString(),
        };

        private static string Persian(PurchaseOrderStatus s) => s switch
        {
            PurchaseOrderStatus.Draft => "پیش‌نویس", PurchaseOrderStatus.Approved => "تایید شده", PurchaseOrderStatus.Released => "ابلاغ شده", PurchaseOrderStatus.PartlyReceived => "تحویل جزئی",
            PurchaseOrderStatus.Received => "تحویل کامل", PurchaseOrderStatus.Closed => "بسته", PurchaseOrderStatus.Cancelled => "لغو شده", _ => s.ToString(),
        };

        private static string Persian(InventoryDocumentStatus s) => s switch
        {
            InventoryDocumentStatus.Draft => "پیش‌نویس", InventoryDocumentStatus.Confirmed => "قطعی شده", InventoryDocumentStatus.Cancelled => "لغو شده", _ => s.ToString(),
        };

        private static string Persian(NcrStatus s) => s switch
        {
            NcrStatus.Open => "باز", NcrStatus.Approved => "تایید شده", NcrStatus.Executed => "اجرا شده", NcrStatus.Closed => "بسته", NcrStatus.Cancelled => "لغو شده", _ => s.ToString(),
        };

        private static string Persian(SupplierInvoiceStatus s) => s switch
        {
            SupplierInvoiceStatus.Draft => "پیش‌نویس", SupplierInvoiceStatus.Matched => "مطابق سفارش", SupplierInvoiceStatus.NeedsApproval => "نیازمند تایید", SupplierInvoiceStatus.Approved => "تایید شده", SupplierInvoiceStatus.Cancelled => "لغو شده", _ => s.ToString(),
        };

        private static string Persian(PaymentRequestStatus s) => s switch
        {
            PaymentRequestStatus.Draft => "در انتظار تایید", PaymentRequestStatus.Approved => "تایید شده", PaymentRequestStatus.Paid => "پرداخت شده", PaymentRequestStatus.Rejected => "رد شده", _ => s.ToString(),
        };

        private static string Persian(FnDocStatus s) => s switch
        {
            FnDocStatus.Draft => "پیش‌نویس", FnDocStatus.Approved => "تایید شده", FnDocStatus.Posted => "ثبت شده", FnDocStatus.Cancelled => "لغو شده", _ => s.ToString(),
        };
    }
}
