using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.ContractsSystem;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.ProcurementDtos;

namespace Services.NasbNiro.ContractsSystem
{
    public interface IProgressStatementService
    {
        Task<ResponseDto<List<StatementDto>>> GetStatementsAsync(FlowQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<StatementDto>> GetStatementAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<StatementDto>> SaveStatementAsync(SaveStatementDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<StatementDto>> StatementActionAsync(FlowActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<StatementDto>> RecordPaymentAsync(StatementPaymentDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<StatementDto>> SaveCounterpartyAsync(StatementCounterpartyDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<StatementPrintDto>> GetPrintAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<List<LegalRateDto>>> GetRatesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<LegalRateDto>> SaveRateAsync(SaveLegalRateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<LegalRateDto>> RateActionAsync(FlowActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<CbsItemDto>>> GetCbsAsync(int? purchaseOrderId, int? contractId, CancellationToken cancellationToken);
        Task<ResponseDto<List<CbsItemDto>>> SaveCbsAsync(SaveCbsDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<CashFlowDto>> GetCashFlowAsync(int? purchaseOrderId, int? contractId, CancellationToken cancellationToken);
        Task<ResponseDto<CashFlowDto>> SaveCashPlanAsync(SaveCashPlanDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<StatementAnalyticsDto>> GetAnalyticsAsync(FlowQuery query, CancellationToken cancellationToken);
    }

    /// <summary>
    /// PB6: progress statements (پیوست ۷ and the company form) of supplier and subcontractor contracts, and of the client side. Prepared by commercial, then the technical office, the supervision and the
    /// project manager of the contract authorise the payment; the payment request is made through the usual payables service (its caps apply). Legal deduction rates come from a table that the
    /// system ships empty: one person types a rate, another verifies it.
    /// </summary>
    public class ProgressStatementService : IProgressStatementService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IPayablesService _payables;
        private readonly global::Services.NasbNiro.Office.INotificationService _notify;

        public ProgressStatementService(IUnitOfWork uow, IInventoryUserContext user, IPayablesService payables, global::Services.NasbNiro.Office.INotificationService notify)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _payables = payables ?? throw new ArgumentNullException(nameof(payables));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private const string NotAllowed = "شما مجوز این کار را ندارید";
        private const string SameUser = "این مرحله باید توسط شخص دیگری غیر از تهیه‌کننده و تاییدکنندگان قبلی انجام شود.";
        private static decimal R(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);

        // =========================================================================================== loading

        private async Task<List<StatementDto>> LoadAsync(IQueryable<PsStatement> source, bool withDetails, CancellationToken cancellationToken)
        {
            IQueryable<PsStatement> q = source.AsNoTracking();
            if (withDetails) q = q.Include(s => s.Lines).Include(s => s.PreviousPaymentRows).Include(s => s.Deductions).AsSplitQuery();
            List<PsStatement> rows = await q.Include(s => s.PurchaseOrder).Include(s => s.Provider).OrderByDescending(s => s.Id).Take(500).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(s => new int?[] { s.CreatorUserId, s.PreparedByUserId, s.TechnicalApprovedByUserId, s.SupervisionApprovedByUserId, s.PaymentAuthorisedByUserId }), cancellationToken);
            List<int> contractIds = rows.Where(s => s.ContractId.HasValue).Select(s => s.ContractId.Value).Distinct().ToList();
            Dictionary<int, string> codes = await _uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().AsNoTracking().Where(c => contractIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, cancellationToken);
            var terms = await _uow.Set<PcContractTerms>().AsNoTracking().Where(t => contractIds.Contains(t.ContractId)).Select(t => new { t.ContractId, t.ProjectManagerUserId }).ToListAsync(cancellationToken);
            Dictionary<int, string> managers = await InventoryUsers.GetNamesAsync(_uow, terms.Select(t => t.ProjectManagerUserId), cancellationToken);
            List<int> requestIds = rows.Where(s => s.PaymentRequestId.HasValue).Select(s => s.PaymentRequestId.Value).ToList();
            Dictionary<int, string> requestNumbers = await _uow.Set<PaymentRequest>().AsNoTracking().Where(p => requestIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number, cancellationToken);
            var ncrIds = rows.SelectMany(s => s.Deductions ?? new List<PsDeduction>()).Where(d => d.NcrId.HasValue).Select(d => d.NcrId.Value).Distinct().ToList();
            Dictionary<int, string> ncrNumbers = await _uow.Set<NonConformance>().AsNoTracking().Where(n => ncrIds.Contains(n.Id)).ToDictionaryAsync(n => n.Id, n => n.Number, cancellationToken);
            var rateIds = rows.SelectMany(s => s.Deductions ?? new List<PsDeduction>()).Where(d => d.LegalRateId.HasValue).Select(d => d.LegalRateId.Value).Distinct().ToList();
            Dictionary<int, string> rateTitles = await _uow.Set<PsLegalRate>().AsNoTracking().Where(r => rateIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Title, cancellationToken);
            List<int> cbsIds = rows.SelectMany(s => s.Lines ?? new List<PsStatementLine>()).Where(l => l.CbsItemId.HasValue).Select(l => l.CbsItemId.Value).Distinct().ToList();
            Dictionary<int, string> cbsCodes = await _uow.Set<CtCbsItem>().AsNoTracking().Where(c => cbsIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, cancellationToken);

            var list = new List<StatementDto>();
            foreach (PsStatement s in rows)
            {
                var d = new StatementDto
                {
                    Id = s.Id, Number = s.Number, Side = s.Side, Status = s.Status, PurchaseOrderId = s.PurchaseOrderId, PurchaseOrderNumber = s.PurchaseOrder?.Number, ProviderId = s.ProviderId,
                    ProviderName = s.Provider?.Name, ContractId = s.ContractId, ContractCode = s.ContractId.HasValue && codes.TryGetValue(s.ContractId.Value, out string c) ? c : null, Serial = s.Serial,
                    PeriodFrom = s.PeriodFrom, PeriodTo = s.PeriodTo, SendingDate = s.SendingDate, Subject = s.Subject, SupportingDocuments = s.SupportingDocuments, CounterpartyStatus = s.CounterpartyStatus,
                    CounterpartyComments = s.CounterpartyComments, ContractAmount = s.ContractAmount, AddendaAmount = s.AddendaAmount, PreviousPayments = s.PreviousPayments,
                    NonconformityDeductions = s.NonconformityDeductions, LegalDeductions = s.LegalDeductions, GrossThisPeriod = s.GrossThisPeriod, PayableThisPeriod = s.PayableThisPeriod,
                    PreparedAt = s.PreparedAt, PreparedByName = names.Name(s.PreparedByUserId), TechnicalApprovedAmount = s.TechnicalApprovedAmount, TechnicalApprovedAt = s.TechnicalApprovedAt,
                    TechnicalApprovedByName = names.Name(s.TechnicalApprovedByUserId), SupervisionApprovedAmount = s.SupervisionApprovedAmount, SupervisionApprovedAt = s.SupervisionApprovedAt,
                    SupervisionApprovedByName = names.Name(s.SupervisionApprovedByUserId), PaymentAuthorisedAt = s.PaymentAuthorisedAt, PaymentAuthorisedByName = names.Name(s.PaymentAuthorisedByUserId),
                    ProjectManagerName = s.ContractId.HasValue ? managers.Name(terms.FirstOrDefault(t => t.ContractId == s.ContractId.Value)?.ProjectManagerUserId) : null,
                    PaymentRequestId = s.PaymentRequestId, PaymentRequestNumber = s.PaymentRequestId.HasValue && requestNumbers.TryGetValue(s.PaymentRequestId.Value, out string pn) ? pn : null,
                    PaidDate = s.PaidDate, PayMethod = s.PayMethod, ChequeNumber = s.ChequeNumber, ExpectedAmount = s.ExpectedAmount, ReceivedAmount = s.ReceivedAmount, PaymentVariance = s.PaymentVariance,
                    ReturnReason = s.ReturnReason, CreatorName = names.Name(s.CreatorUserId), CreateDate = s.CreateDate,
                };
                if (withDetails)
                {
                    foreach (PsStatementLine l in s.Lines.OrderBy(x => x.RowNumber))
                        d.Lines.Add(new StatementLineDto
                        {
                            Id = l.Id, RowNumber = l.RowNumber, Title = l.Title, SerialOrDrawing = l.SerialOrDrawing, ProjectCode = l.ProjectCode, EquipmentCode = l.EquipmentCode, Quantity = l.Quantity,
                            UnitPrice = l.UnitPrice, AdvanceAmount = l.AdvanceAmount, AfterDeliveryQcAmount = l.AfterDeliveryQcAmount, GoodPerformanceAmount = l.GoodPerformanceAmount,
                            AfterFinalTestsAmount = l.AfterFinalTestsAmount, IsAddendum = l.IsAddendum, OrderLineId = l.OrderLineId, CbsItemId = l.CbsItemId, LineTotal = l.LineTotal,
                            ThisPeriodAmount = l.ThisPeriodAmount, ProgressPercent = l.ProgressPercent, CbsCode = l.CbsItemId.HasValue && cbsCodes.TryGetValue(l.CbsItemId.Value, out string cc) ? cc : null,
                        });
                    foreach (PsPreviousPayment p in s.PreviousPaymentRows.OrderBy(x => x.Id))
                        d.Previous.Add(new StatementPreviousDto { Id = p.Id, Amount = p.Amount, PaymentDocNumber = p.PaymentDocNumber, MethodTitle = p.MethodTitle, PaymentTitle = p.PaymentTitle, PaymentDate = p.PaymentDate, StatementSerial = p.StatementSerial, IsAuto = p.IsAuto });
                    foreach (PsDeduction x in s.Deductions.OrderBy(y => y.Kind).ThenBy(y => y.RowNumber))
                        d.Deductions.Add(new StatementDeductionDto
                        {
                            Id = x.Id, RowNumber = x.RowNumber, Kind = x.Kind, Title = x.Title, ItemTitle = x.ItemTitle, SerialNumber = x.SerialNumber, Quantity = x.Quantity, UnitPrice = x.UnitPrice, Basis = x.Basis,
                            DeviationType = x.DeviationType, LetterNumber = x.LetterNumber, NcrId = x.NcrId, NcrNumber = x.NcrId.HasValue && ncrNumbers.TryGetValue(x.NcrId.Value, out string nn) ? nn : null,
                            LegalRateId = x.LegalRateId, RateTitle = x.LegalRateId.HasValue && rateTitles.TryGetValue(x.LegalRateId.Value, out string rt) ? rt : null, Percent = x.Percent, BaseAmount = x.BaseAmount, Amount = x.Amount,
                        });
                }
                list.Add(d);
            }
            return list;
        }

        public async Task<ResponseDto<List<StatementDto>>> GetStatementsAsync(FlowQuery query, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PsRoles.Readers)) return Bad<List<StatementDto>>(NotAllowed);
            query ??= new FlowQuery();
            IQueryable<PsStatement> q = _uow.Set<PsStatement>().Where(s => !s.Deleted);
            if (query.PurchaseOrderId.HasValue) q = q.Where(s => s.PurchaseOrderId == query.PurchaseOrderId.Value);
            if (query.ContractId.HasValue) q = q.Where(s => s.ContractId == query.ContractId.Value);
            if (query.ProviderId.HasValue) q = q.Where(s => s.ProviderId == query.ProviderId.Value);
            if (query.Status.HasValue) q = q.Where(s => (int)s.Status == query.Status.Value);
            if (query.DateFrom.HasValue) { DateTime f = query.DateFrom.Value.Date; q = q.Where(s => s.PeriodTo >= f); }
            if (query.DateTo.HasValue) { DateTime t = query.DateTo.Value.Date; q = q.Where(s => s.PeriodFrom <= t); }
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(s => s.Number.Contains(text) || s.Subject.Contains(text) || s.Provider.Name.Contains(text));
            return ResponseDtoHelper.Ok(await LoadAsync(q, false, cancellationToken));
        }

        public async Task<ResponseDto<StatementDto>> GetStatementAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PsRoles.Readers)) return Bad<StatementDto>(NotAllowed);
            List<StatementDto> list = await LoadAsync(_uow.Set<PsStatement>().Where(s => s.Id == id && !s.Deleted), true, cancellationToken);
            if (list.Count == 0) return ResponseDtoHelper.NotFound<StatementDto>("صورت‌وضعیت پیدا نشد.");
            list[0].Warnings = await WarningsAsync(id, cancellationToken);
            return ResponseDtoHelper.Ok(list[0]);
        }

        // =========================================================================================== the progress of the WBS node behind a CBS item

        private async Task<decimal?> NodeProgressAsync(int wbsNodeId, CancellationToken cancellationToken)
        {
            PcWbsNode node = await _uow.Set<PcWbsNode>().AsNoTracking().FirstOrDefaultAsync(n => n.Id == wbsNodeId && !n.Deleted, cancellationToken);
            if (node == null) return null;
            var all = await _uow.Set<PcWbsNode>().AsNoTracking().Where(n => n.ContractId == node.ContractId && !n.Deleted).Select(n => new { n.Id, n.ParentId }).ToListAsync(cancellationToken);
            var subtree = new HashSet<int> { wbsNodeId };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var n in all)
                    if (n.ParentId.HasValue && subtree.Contains(n.ParentId.Value) && subtree.Add(n.Id)) grew = true;
            }
            var acts = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.WbsNodeId != null && subtree.Contains(a.WbsNodeId.Value) && !a.Deleted).Select(a => new { a.Weight, a.PercentCache }).ToListAsync(cancellationToken);
            if (acts.Count == 0) return null;
            decimal weight = acts.Sum(a => a.Weight <= 0 ? 1 : a.Weight);
            return Math.Round(acts.Sum(a => (a.Weight <= 0 ? 1 : a.Weight) * a.PercentCache) / weight, 2);
        }

        /// <summary>The claims on the lines of a CBS item may not run ahead of the progress of its WBS node; a claim beyond it is a warning the approvers see (not a block: advances are legitimate).</summary>
        private async Task<List<string>> WarningsAsync(int statementId, CancellationToken cancellationToken)
        {
            var warnings = new List<string>();
            PsStatement s = await _uow.Set<PsStatement>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == statementId, cancellationToken);
            if (s == null) return warnings;
            foreach (IGrouping<int?, PsStatementLine> g in s.Lines.Where(l => l.CbsItemId.HasValue).GroupBy(l => l.CbsItemId))
            {
                CtCbsItem item = await _uow.Set<CtCbsItem>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == g.Key.Value, cancellationToken);
                if (item?.WbsNodeId == null) continue;
                decimal? progress = await NodeProgressAsync(item.WbsNodeId.Value, cancellationToken);
                if (progress == null) continue;
                decimal earned = R(item.Amount * progress.Value / 100m);
                decimal claimedBefore = await _uow.Set<PsStatementLine>().AsNoTracking().Where(l => l.CbsItemId == item.Id && l.Statement.Id != s.Id && l.Statement.Status != PsStatus.Rejected && l.Statement.Status != PsStatus.Returned && !l.Statement.Deleted && l.Statement.Serial < s.Serial)
                    .SumAsync(l => (decimal?)(l.AfterDeliveryQcAmount + l.GoodPerformanceAmount + l.AfterFinalTestsAmount), cancellationToken) ?? 0;
                decimal claims = g.Sum(l => l.AfterDeliveryQcAmount + l.GoodPerformanceAmount + l.AfterFinalTestsAmount) + claimedBefore;
                if (claims > earned) warnings.Add($"بند {item.Code}: مجموع مطالبات ({claims:N0}) از ارزش کار انجام‌شده ({earned:N0} = {progress:0.##}٪ از {item.Amount:N0}) بیشتر است.");
            }
            return warnings;
        }

        // =========================================================================================== save

        public async Task<ResponseDto<StatementDto>> SaveStatementAsync(SaveStatementDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<StatementDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(PsRoles.Preparers)) return Bad<StatementDto>(NotAllowed + " (بازرگانی)");
            if (!Enum.IsDefined(typeof(PsSide), dto.Side)) return Bad<StatementDto>("سمت صورت‌وضعیت نامعتبر است.");
            DateTime from = dto.PeriodFrom.Date, to = dto.PeriodTo.Date;
            if (from == default || to < from) return Bad<StatementDto>("دوره صورت‌وضعیت نامعتبر است.");

            PurchaseOrder order = null;
            int? contractId = dto.ContractId;
            if (dto.Side == PsSide.Supplier)
            {
                if (!dto.PurchaseOrderId.HasValue) return Bad<StatementDto>("سفارش (قرارداد) تامین‌کننده یا پیمانکار را انتخاب کنید.");
                order = await _uow.Set<PurchaseOrder>().Include(o => o.Lines).AsNoTracking().FirstOrDefaultAsync(o => o.Id == dto.PurchaseOrderId.Value && !o.Deleted, cancellationToken);
                if (order == null) return ResponseDtoHelper.NotFound<StatementDto>("سفارش پیدا نشد.");
                if (order.Status < PurchaseOrderStatus.Released || order.Status == PurchaseOrderStatus.Cancelled) return Bad<StatementDto>("سفارش باید ابلاغ شده باشد.");
                contractId ??= order.ContractId;
            }
            else
            {
                if (!contractId.HasValue) return Bad<StatementDto>("قرارداد کارفرما را انتخاب کنید.");
                if (!await _uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().AnyAsync(c => c.Id == contractId.Value && !c.Deleted, cancellationToken)) return Bad<StatementDto>("قرارداد پیدا نشد.");
            }

            List<SaveStatementLineDto> lines = (dto.Lines ?? new List<SaveStatementLineDto>()).Where(l => l != null).ToList();
            if (lines.Count == 0) return Bad<StatementDto>("صورت‌وضعیت حداقل یک ردیف لازم دارد.");
            for (int i = 0; i < lines.Count; i++)
            {
                SaveStatementLineDto l = lines[i];
                string row = $"ردیف {i + 1}: ";
                if (Clean(l.Title) == null) return Bad<StatementDto>(row + "شرح کالا یا خدمت را بنویسید.");
                if (l.Quantity < 0 || l.UnitPrice < 0 || l.AdvanceAmount < 0 || l.AfterDeliveryQcAmount < 0 || l.GoodPerformanceAmount < 0 || l.AfterFinalTestsAmount < 0) return Bad<StatementDto>(row + "مقدارها نمی‌تواند منفی باشد.");
                if (l.OrderLineId.HasValue && (order == null || order.Lines.All(x => x.Id != l.OrderLineId.Value))) return Bad<StatementDto>(row + "ردیف سفارش متعلق به این قرارداد نیست.");
            }
            List<int> cbsIds = lines.Where(l => l.CbsItemId.HasValue).Select(l => l.CbsItemId.Value).Distinct().ToList();
            Dictionary<int, CtCbsItem> cbs = await _uow.Set<CtCbsItem>().AsNoTracking().Where(c => cbsIds.Contains(c.Id) && !c.Deleted).ToDictionaryAsync(c => c.Id, cancellationToken);
            if (cbsIds.Any(id => !cbs.ContainsKey(id))) return Bad<StatementDto>("بند CBS پیدا نشد.");
            if (cbs.Values.Any(c => (dto.Side == PsSide.Supplier ? c.PurchaseOrderId != dto.PurchaseOrderId : c.ContractId != contractId))) return Bad<StatementDto>("بند CBS متعلق به این قرارداد نیست.");

            PsStatement st;
            if (dto.Id.HasValue)
            {
                st = await _uow.Set<PsStatement>().Include(x => x.Lines).Include(x => x.PreviousPaymentRows).Include(x => x.Deductions).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (st == null) return ResponseDtoHelper.NotFound<StatementDto>("صورت‌وضعیت پیدا نشد.");
                if (st.Status != PsStatus.Draft && st.Status != PsStatus.Returned) return Bad<StatementDto>("فقط پیش‌نویس یا برگشتی ویرایش می‌شود.");
                if (st.CreatorUserId != Me && !_user.IsInRole("PMO")) return Bad<StatementDto>(NotAllowed);
                _uow.Set<PsStatementLine>().RemoveRange(st.Lines); _uow.Set<PsPreviousPayment>().RemoveRange(st.PreviousPaymentRows); _uow.Set<PsDeduction>().RemoveRange(st.Deductions);
                st.Lines = new List<PsStatementLine>(); st.PreviousPaymentRows = new List<PsPreviousPayment>(); st.Deductions = new List<PsDeduction>();
            }
            else
            {
                int serial = (await _uow.Set<PsStatement>().Where(s => s.Side == dto.Side && s.PurchaseOrderId == dto.PurchaseOrderId && s.ContractId == contractId && !s.Deleted).MaxAsync(s => (int?)s.Serial, cancellationToken) ?? 0) + 1;
                st = new PsStatement
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, Pb6Numbers.Statement, dto.Side == PsSide.Supplier ? "PST" : "CST", DateTime.Today, cancellationToken), Side = dto.Side, Status = PsStatus.Draft,
                    Serial = serial, CreateDate = DateTimeOffset.Now, CreatorUserId = Me, Lines = new List<PsStatementLine>(), PreviousPaymentRows = new List<PsPreviousPayment>(), Deductions = new List<PsDeduction>(),
                };
                _uow.Set<PsStatement>().Add(st);
            }
            st.PurchaseOrderId = order?.Id; st.ProviderId = order?.ProviderId; st.ContractId = contractId; st.ProvisionalStatementId = dto.ProvisionalStatementId;
            st.PeriodFrom = from; st.PeriodTo = to; st.Subject = Clean(dto.Subject); st.SupportingDocuments = Clean(dto.SupportingDocuments);

            int rowNumber = 1;
            foreach (SaveStatementLineDto l in lines)
            {
                var line = new PsStatementLine
                {
                    RowNumber = rowNumber++, Title = Clean(l.Title), SerialOrDrawing = Clean(l.SerialOrDrawing), ProjectCode = Clean(l.ProjectCode), EquipmentCode = Clean(l.EquipmentCode), Quantity = l.Quantity, UnitPrice = l.UnitPrice,
                    LineTotal = R(l.Quantity * l.UnitPrice), AdvanceAmount = R(l.AdvanceAmount), AfterDeliveryQcAmount = R(l.AfterDeliveryQcAmount), GoodPerformanceAmount = R(l.GoodPerformanceAmount),
                    AfterFinalTestsAmount = R(l.AfterFinalTestsAmount), IsAddendum = l.IsAddendum, OrderLineId = l.OrderLineId, CbsItemId = l.CbsItemId,
                };
                line.ThisPeriodAmount = line.AdvanceAmount + line.AfterDeliveryQcAmount + line.GoodPerformanceAmount + line.AfterFinalTestsAmount;
                if (l.CbsItemId.HasValue && cbs[l.CbsItemId.Value].WbsNodeId.HasValue) line.ProgressPercent = await NodeProgressAsync(cbs[l.CbsItemId.Value].WbsNodeId.Value, cancellationToken);
                st.Lines.Add(line);
            }
            decimal gross = st.Lines.Sum(l => l.ThisPeriodAmount);
            st.GrossThisPeriod = gross;

            // the contract value and the addenda (the order total already holds the applied addenda)
            if (order != null)
            {
                st.ContractAmount = order.TotalAmount;
                st.AddendaAmount = await _uow.Set<PurchaseOrderAddendum>().Where(a => a.PurchaseOrderId == order.Id && a.Status == AddendumStatus.Applied).SumAsync(a => (decimal?)(a.AmountAfter - a.AmountBefore), cancellationToken) ?? 0;
            }
            else
            {
                st.ContractAmount = await _uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().Where(c => c.Id == contractId).Select(c => c.Price ?? 0).FirstOrDefaultAsync(cancellationToken);
            }

            // the previous payments: the system adds the earlier paid statements and the paid requests of the order that no statement carries; the clerk may add what the system cannot know
            decimal previous = 0;
            foreach (SavePreviousPaymentDto p in dto.ManualPrevious ?? new List<SavePreviousPaymentDto>())
            {
                if (p == null || p.Amount <= 0) continue;
                st.PreviousPaymentRows.Add(new PsPreviousPayment { Amount = R(p.Amount), PaymentDocNumber = Clean(p.PaymentDocNumber), MethodTitle = Clean(p.MethodTitle), PaymentTitle = Clean(p.PaymentTitle), PaymentDate = p.PaymentDate?.Date, StatementSerial = Clean(p.StatementSerial), IsAuto = false });
                previous += R(p.Amount);
            }
            int selfId = st.Id;
            List<PsStatement> paidBefore = await _uow.Set<PsStatement>().AsNoTracking().Where(s => s.Id != selfId && !s.Deleted && s.Side == dto.Side && s.PurchaseOrderId == dto.PurchaseOrderId && s.ContractId == contractId && s.Status == PsStatus.Paid && s.Serial < st.Serial).OrderBy(s => s.Serial).ToListAsync(cancellationToken);
            foreach (PsStatement p in paidBefore)
            {
                decimal amount = p.ReceivedAmount ?? p.ExpectedAmount ?? p.PayableThisPeriod;
                st.PreviousPaymentRows.Add(new PsPreviousPayment { Amount = amount, PaymentDocNumber = p.ChequeNumber, MethodTitle = p.PayMethod?.ToString(), PaymentTitle = "صورت‌وضعیت " + p.Serial, PaymentDate = p.PaidDate, StatementSerial = p.Number, IsAuto = true });
                previous += amount;
            }
            if (order != null)
            {
                var statementRequestIds = await _uow.Set<PsStatement>().AsNoTracking().Where(s => s.PaymentRequestId != null).Select(s => s.PaymentRequestId.Value).ToListAsync(cancellationToken);
                var paidRequests = await _uow.Set<PaymentRequest>().AsNoTracking().Where(p => p.PurchaseOrderId == order.Id && p.Status == PaymentRequestStatus.Paid && !p.Deleted && !statementRequestIds.Contains(p.Id)).ToListAsync(cancellationToken);
                foreach (PaymentRequest p in paidRequests)
                {
                    decimal amount = p.Amount - p.Deductions;
                    st.PreviousPaymentRows.Add(new PsPreviousPayment { Amount = amount, PaymentDocNumber = p.PaymentReference, MethodTitle = null, PaymentTitle = p.Type == PaymentRequestType.Advance ? "پیش‌پرداخت" : "پرداخت " + p.Number, PaymentDate = p.PaidAt?.DateTime, StatementSerial = null, IsAuto = true });
                    previous += amount;
                }
            }
            st.PreviousPayments = previous;

            // the deductions
            decimal nonconformity = 0, legal = 0;
            int ncRow = 1, lgRow = 1;
            foreach (SaveStatementDeductionDto d in dto.Deductions ?? new List<SaveStatementDeductionDto>())
            {
                if (d == null) continue;
                if (d.Kind == PsDeductionKind.Nonconformity)
                {
                    if (d.NcrId.HasValue && !await _uow.Set<NonConformance>().AnyAsync(n => n.Id == d.NcrId.Value && !n.Deleted, cancellationToken)) return Bad<StatementDto>("گزارش عدم انطباق پیدا نشد.");
                    decimal amount = R((d.Quantity ?? 0) * (d.UnitPrice ?? 0));
                    if (amount <= 0) return Bad<StatementDto>("کسر عدم انطباق: تعداد و قیمت واحد را بنویسید.");
                    st.Deductions.Add(new PsDeduction
                    {
                        Kind = PsDeductionKind.Nonconformity, RowNumber = ncRow++, Title = Clean(d.Title), ItemTitle = Clean(d.ItemTitle), SerialNumber = Clean(d.SerialNumber), Quantity = d.Quantity, UnitPrice = d.UnitPrice,
                        Basis = Clean(d.Basis), DeviationType = Clean(d.DeviationType), LetterNumber = Clean(d.LetterNumber), NcrId = d.NcrId, Amount = amount,
                    });
                    nonconformity += amount;
                }
                else
                {
                    if (!d.LegalRateId.HasValue) return Bad<StatementDto>("برای کسر قانونی نرخ را از جدول نرخ‌ها انتخاب کنید.");
                    PsLegalRate rate = await _uow.Set<PsLegalRate>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == d.LegalRateId.Value && !r.Deleted, cancellationToken);
                    if (rate == null) return Bad<StatementDto>("نرخ پیدا نشد.");
                    if (rate.Status != PsRateStatus.Verified) return Bad<StatementDto>($"نرخ «{rate.Title}» هنوز توسط نفر دوم تایید نشده یا کنار گذاشته شده است.");
                    decimal baseAmount = d.BaseAmount ?? gross;
                    decimal amount = R(baseAmount * rate.Percent / 100m);
                    st.Deductions.Add(new PsDeduction { Kind = PsDeductionKind.Legal, RowNumber = lgRow++, Title = Clean(d.Title) ?? rate.Title, LegalRateId = rate.Id, Percent = rate.Percent, BaseAmount = baseAmount, Amount = amount });
                    legal += amount;
                }
            }
            st.NonconformityDeductions = nonconformity; st.LegalDeductions = legal;
            st.PayableThisPeriod = gross - nonconformity - legal;
            if (st.PayableThisPeriod < 0) return Bad<StatementDto>("کسورات از مبلغ این دوره بیشتر است.");
            // the cumulative claim (what was paid before and this period) may not pass the contract with its addenda
            if (st.ContractAmount > 0 && previous + gross > st.ContractAmount) return Bad<StatementDto>($"مجموع پرداخت‌های قبلی ({previous:N0}) و این دوره ({gross:N0}) از مبلغ قرارداد ({st.ContractAmount:N0}) بیشتر می‌شود.");
            st.EditorUserIds = LedgerMath.WithEditor(st.EditorUserIds, Me);
            st.Status = PsStatus.Draft; st.ReturnReason = null;
            st.ModifyDate = DateTimeOffset.Now; st.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetStatementAsync(st.Id, cancellationToken);
        }

        // =========================================================================================== workflow

        private async Task<int?> ProjectManagerAsync(int? contractId, CancellationToken cancellationToken) =>
            contractId.HasValue ? await _uow.Set<PcContractTerms>().AsNoTracking().Where(t => t.ContractId == contractId.Value).Select(t => t.ProjectManagerUserId).FirstOrDefaultAsync(cancellationToken) : null;

        private Task SyncGateAsync(PsStatement s, CancellationToken cancellationToken)
        {
            string roles = s.Status == PsStatus.Prepared ? PsRoles.TechnicalOffice : s.Status == PsStatus.TechnicalApproved ? PsRoles.Supervision
                : s.Status == PsStatus.SupervisionApproved ? "PROJECTMANAGER,PMO" : s.Status == PsStatus.PaymentAuthorised && s.Side == PsSide.Supplier ? PsRoles.Preparers : null;
            return global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "ps:" + s.Id, roles != null, "procurement", "progress-statement", s.Id,
                (s.Status == PsStatus.PaymentAuthorised ? "درخواست پرداخت صورت‌وضعیت " : "تایید صورت‌وضعیت ") + s.Number, "/procurement/statements?id=" + s.Id, roles, cancellationToken);
        }

        public async Task<ResponseDto<StatementDto>> StatementActionAsync(FlowActionDto dto, CancellationToken cancellationToken)
        {
            PsStatement s = await _uow.Set<PsStatement>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (s == null) return ResponseDtoHelper.NotFound<StatementDto>("صورت‌وضعیت پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            string note = Clean(dto.Note);
            bool Touched(int user) => user == s.CreatorUserId || user == s.PreparedByUserId || user == s.TechnicalApprovedByUserId || user == s.SupervisionApprovedByUserId || (s.EditorUserIds != null && s.EditorUserIds.Contains("," + user + ","));
            switch (dto.Action?.Trim().ToLowerInvariant())
            {
                case "prepare":
                    if (s.Status != PsStatus.Draft && s.Status != PsStatus.Returned) return Bad<StatementDto>("صورت‌وضعیت قبلاً آماده شده است.");
                    if (!_user.IsInRole(PsRoles.Preparers)) return Bad<StatementDto>(NotAllowed + " (بازرگانی)");
                    if (s.CreatorUserId != Me) return Bad<StatementDto>(NotAllowed);
                    if (s.PayableThisPeriod <= 0) return Bad<StatementDto>("مبلغ قابل پرداخت این دوره صفر است.");
                    s.Status = PsStatus.Prepared; s.PreparedAt = now; s.PreparedByUserId = Me; s.ReturnReason = null;
                    break;
                case "technical-approve":
                {
                    if (s.Status != PsStatus.Prepared) return Bad<StatementDto>("صورت‌وضعیت در مرحله دفتر فنی نیست.");
                    if (!_user.IsInRole(PsRoles.TechnicalOffice)) return Bad<StatementDto>(NotAllowed + " (دفتر فنی)");
                    if (Touched(Me)) return Bad<StatementDto>(SameUser);
                    decimal amount = dto.Amount ?? s.PayableThisPeriod;
                    if (amount <= 0 || amount > s.PayableThisPeriod) return Bad<StatementDto>("مبلغ تاییدشده باید بین صفر و مبلغ قابل پرداخت باشد.");
                    s.TechnicalApprovedAmount = R(amount); s.TechnicalApprovedAt = now; s.TechnicalApprovedByUserId = Me; s.Status = PsStatus.TechnicalApproved;
                    break;
                }
                case "supervision-approve":
                {
                    if (s.Status != PsStatus.TechnicalApproved) return Bad<StatementDto>("صورت‌وضعیت باید اول توسط دفتر فنی تایید شود.");
                    if (!_user.IsInRole(PsRoles.Supervision)) return Bad<StatementDto>(NotAllowed + " (نظارت)");
                    if (Touched(Me)) return Bad<StatementDto>(SameUser);
                    decimal amount = dto.Amount ?? s.TechnicalApprovedAmount ?? 0;
                    if (amount <= 0 || amount > (s.TechnicalApprovedAmount ?? 0)) return Bad<StatementDto>("مبلغ تایید نظارت از مبلغ تایید دفتر فنی بیشتر نمی‌شود.");
                    s.SupervisionApprovedAmount = R(amount); s.SupervisionApprovedAt = now; s.SupervisionApprovedByUserId = Me; s.Status = PsStatus.SupervisionApproved;
                    break;
                }
                case "authorise-payment":
                {
                    if (s.Status != PsStatus.SupervisionApproved) return Bad<StatementDto>("صورت‌وضعیت باید اول توسط نظارت تایید شود.");
                    // the project manager of the contract authorises; a contract without one is authorised by the PMO deputy
                    int? manager = await ProjectManagerAsync(s.ContractId, cancellationToken);
                    bool allowed = manager.HasValue ? manager.Value == Me : _user.IsInRole("PMO");
                    if (!allowed) return Bad<StatementDto>(NotAllowed + " (مدیر پروژه همین قرارداد)");
                    if (Touched(Me)) return Bad<StatementDto>(SameUser);
                    s.PaymentAuthorisedAt = now; s.PaymentAuthorisedByUserId = Me; s.Status = PsStatus.PaymentAuthorised;
                    break;
                }
                case "request-payment":
                {
                    if (s.Status != PsStatus.PaymentAuthorised) return Bad<StatementDto>("پرداخت هنوز توسط مدیر پروژه مجاز نشده است.");
                    if (s.Side != PsSide.Supplier || !s.PurchaseOrderId.HasValue) return Bad<StatementDto>("درخواست پرداخت فقط برای صورت‌وضعیت تامین‌کننده یا پیمانکار است.");
                    if (!_user.IsInRole(PsRoles.Preparers)) return Bad<StatementDto>(NotAllowed + " (بازرگانی)");
                    decimal deductions = s.NonconformityDeductions + s.LegalDeductions;
                    decimal approved = s.SupervisionApprovedAmount ?? s.PayableThisPeriod;
                    ResponseDto<PaymentRequestDto> made = await _payables.SavePaymentAsync(new SavePaymentRequestDto
                    {
                        Type = PaymentRequestType.Progress, RequestDate = DateTime.Today, PurchaseOrderId = s.PurchaseOrderId, Amount = approved + deductions, Deductions = deductions,
                        Description = $"صورت‌وضعیت {s.Number} (سریال {s.Serial})", DeductionNote = deductions > 0 ? "کسورات صورت‌وضعیت " + s.Number : null,
                    }, cancellationToken);
                    if (!made.Success || made.Data == null) return Bad<StatementDto>(made.Message ?? "ثبت درخواست پرداخت ممکن نشد.");
                    s.PaymentRequestId = made.Data.Id; s.Status = PsStatus.PaymentRequested;
                    break;
                }
                case "send":
                    if (!_user.IsInRole(PsRoles.Preparers)) return Bad<StatementDto>(NotAllowed);
                    if (s.Status == PsStatus.Draft || s.Status == PsStatus.Rejected) return Bad<StatementDto>("صورت‌وضعیت آماده نیست.");
                    s.SendingDate = (dto.Date ?? DateTime.Today).Date; s.CounterpartyStatus = PsCounterpartyStatus.Sent;
                    break;
                case "return":
                    if (s.Status != PsStatus.Prepared && s.Status != PsStatus.TechnicalApproved && s.Status != PsStatus.SupervisionApproved && s.Status != PsStatus.PaymentAuthorised) return Bad<StatementDto>("صورت‌وضعیت در مرحله بررسی نیست.");
                    if (!_user.IsInRole(PsRoles.TechnicalOffice + "," + PsRoles.Supervision + ",PROJECTMANAGER,PMO")) return Bad<StatementDto>(NotAllowed);
                    if (Touched(Me) && s.CreatorUserId == Me) return Bad<StatementDto>(SameUser);
                    if (note == null) return Bad<StatementDto>("علت برگشت را بنویسید.");
                    s.Status = PsStatus.Returned; s.ReturnedAt = now; s.ReturnReason = note;
                    s.TechnicalApprovedAmount = null; s.TechnicalApprovedAt = null; s.TechnicalApprovedByUserId = null; s.SupervisionApprovedAmount = null; s.SupervisionApprovedAt = null; s.SupervisionApprovedByUserId = null;
                    s.PaymentAuthorisedAt = null; s.PaymentAuthorisedByUserId = null;
                    break;
                case "reject":
                    if (s.Status == PsStatus.Paid || s.Status == PsStatus.PaymentRequested || s.Status == PsStatus.Rejected) return Bad<StatementDto>("این صورت‌وضعیت رد نمی‌شود.");
                    if (!_user.IsInRole("PMO,PROJECTMANAGER")) return Bad<StatementDto>(NotAllowed);
                    if (note == null) return Bad<StatementDto>("علت رد را بنویسید.");
                    s.Status = PsStatus.Rejected; s.ReturnReason = note;
                    break;
                default:
                    return Bad<StatementDto>("عمل نامعتبر است.");
            }
            s.ModifyDate = now; s.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<StatementDto>("اطلاعات هم‌زمان تغییر کرد؛ دوباره تلاش کنید."); }
            await SyncGateAsync(s, cancellationToken);
            return await GetStatementAsync(s.Id, cancellationToken);
        }

        public async Task<ResponseDto<StatementDto>> RecordPaymentAsync(StatementPaymentDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<StatementDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(PsRoles.PaymentRecorders)) return Bad<StatementDto>(NotAllowed + " (مالی)");
            PsStatement s = await _uow.Set<PsStatement>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (s == null) return ResponseDtoHelper.NotFound<StatementDto>("صورت‌وضعیت پیدا نشد.");
            if (!Enum.IsDefined(typeof(PsPayMethod), dto.PayMethod)) return Bad<StatementDto>("روش پرداخت نامعتبر است.");
            if (dto.ReceivedAmount < 0) return Bad<StatementDto>("مبلغ نمی‌تواند منفی باشد.");
            if (dto.PayMethod == PsPayMethod.Cheque && Clean(dto.ChequeNumber) == null) return Bad<StatementDto>("شماره چک را بنویسید (تصویر چک را به عنوان پیوست ثبت کنید).");
            if (s.Side == PsSide.Supplier)
            {
                if (s.Status != PsStatus.PaymentRequested || !s.PaymentRequestId.HasValue) return Bad<StatementDto>("درخواست پرداخت این صورت‌وضعیت هنوز ثبت نشده است.");
                PaymentRequest p = await _uow.Set<PaymentRequest>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == s.PaymentRequestId.Value, cancellationToken);
                if (p == null || p.Status != PaymentRequestStatus.Paid) return Bad<StatementDto>("پرداخت هنوز در خزانه ثبت نهایی نشده است.");
                s.ExpectedAmount = p.Amount - p.Deductions;
            }
            else
            {
                if (s.Status != PsStatus.PaymentAuthorised) return Bad<StatementDto>("دریافت وجه پس از مجوز نهایی ثبت می‌شود.");
                s.ExpectedAmount = s.SupervisionApprovedAmount ?? s.PayableThisPeriod;
            }
            s.PaidDate = dto.PaidDate.Date; s.PayMethod = dto.PayMethod; s.ChequeNumber = Clean(dto.ChequeNumber); s.ReceivedAmount = R(dto.ReceivedAmount);
            s.PaymentVariance = s.ReceivedAmount - s.ExpectedAmount; s.PaymentRecordedAt = DateTimeOffset.Now; s.PaymentRecordedByUserId = Me; s.Status = PsStatus.Paid;
            s.ModifyDate = DateTimeOffset.Now; s.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            await SyncGateAsync(s, cancellationToken);
            return await GetStatementAsync(s.Id, cancellationToken);
        }

        public async Task<ResponseDto<StatementDto>> SaveCounterpartyAsync(StatementCounterpartyDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<StatementDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(PsRoles.Preparers + ",PMO")) return Bad<StatementDto>(NotAllowed);
            PsStatement s = await _uow.Set<PsStatement>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (s == null) return ResponseDtoHelper.NotFound<StatementDto>("صورت‌وضعیت پیدا نشد.");
            if (!Enum.IsDefined(typeof(PsCounterpartyStatus), dto.Status)) return Bad<StatementDto>("وضعیت نامعتبر است.");
            if (dto.Status == PsCounterpartyStatus.Disputed && Clean(dto.Comments) == null) return Bad<StatementDto>("نظر طرف مقابل را بنویسید.");
            s.CounterpartyStatus = dto.Status; s.CounterpartyComments = Clean(dto.Comments); if (dto.SendingDate.HasValue) s.SendingDate = dto.SendingDate.Value.Date;
            s.ModifyDate = DateTimeOffset.Now; s.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetStatementAsync(s.Id, cancellationToken);
        }

        public async Task<ResponseDto<StatementPrintDto>> GetPrintAsync(int id, CancellationToken cancellationToken)
        {
            ResponseDto<StatementDto> st = await GetStatementAsync(id, cancellationToken);
            if (!st.Success || st.Data == null) return Bad<StatementPrintDto>(st.Message);
            return ResponseDtoHelper.Ok(new StatementPrintDto { Statement = st.Data, BuyerName = "خریدار", PayableInWords = PersianNumber.ToWords(st.Data.PayableThisPeriod) + " ریال", TemplateCode = "FO-IP-صورت‌وضعیت" });
        }

        // =========================================================================================== legal rates

        public async Task<ResponseDto<List<LegalRateDto>>> GetRatesAsync(CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PsRoles.Readers)) return Bad<List<LegalRateDto>>(NotAllowed);
            List<PsLegalRate> rows = await _uow.Set<PsLegalRate>().AsNoTracking().Where(r => !r.Deleted).OrderBy(r => r.Code).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.CreatorUserId, r.VerifiedByUserId }), cancellationToken);
            return ResponseDtoHelper.Ok(rows.Select(r => new LegalRateDto
            {
                Id = r.Id, Code = r.Code, Title = r.Title, Percent = r.Percent, BasisNote = r.BasisNote, SourceReference = r.SourceReference, EffectiveFrom = r.EffectiveFrom, Status = r.Status,
                VerifiedAt = r.VerifiedAt, VerifiedByName = names.Name(r.VerifiedByUserId), CreatorName = names.Name(r.CreatorUserId),
            }).ToList());
        }

        public async Task<ResponseDto<LegalRateDto>> SaveRateAsync(SaveLegalRateDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<LegalRateDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(PsRoles.RateEditors)) return Bad<LegalRateDto>(NotAllowed + " (مالی)");
            if (Clean(dto.Code) == null || Clean(dto.Title) == null) return Bad<LegalRateDto>("کد و عنوان نرخ را بنویسید.");
            if (dto.Percent < 0 || dto.Percent > 100) return Bad<LegalRateDto>("درصد باید بین ۰ و ۱۰۰ باشد.");
            if (Clean(dto.SourceReference) == null) return Bad<LegalRateDto>("مرجع نرخ (قانون، آیین‌نامه یا نامه) را بنویسید؛ نرخ بدون مرجع ثبت نمی‌شود.");
            PsLegalRate r;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<PsLegalRate>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (r == null) return ResponseDtoHelper.NotFound<LegalRateDto>("نرخ پیدا نشد.");
                if (r.Status == PsRateStatus.Verified && (r.Percent != dto.Percent)) return Bad<LegalRateDto>("نرخ تاییدشده تغییر نمی‌کند؛ آن را کنار بگذارید و نرخ تازه ثبت کنید.");
            }
            else
            {
                if (await _uow.Set<PsLegalRate>().AnyAsync(x => x.Code == dto.Code.Trim() && !x.Deleted && x.Status != PsRateStatus.Retired, cancellationToken)) return Bad<LegalRateDto>("نرخ فعالی با این کد هست.");
                r = new PsLegalRate { Status = PsRateStatus.Draft, CreateDate = DateTimeOffset.Now, CreatorUserId = Me };
                _uow.Set<PsLegalRate>().Add(r);
            }
            r.Code = dto.Code.Trim(); r.Title = dto.Title.Trim(); r.Percent = dto.Percent; r.BasisNote = Clean(dto.BasisNote); r.SourceReference = Clean(dto.SourceReference); r.EffectiveFrom = dto.EffectiveFrom?.Date;
            if (r.Status == PsRateStatus.Verified) { /* only the notes changed */ }
            r.EditorUserIds = LedgerMath.WithEditor(r.EditorUserIds, Me);
            r.ModifyDate = DateTimeOffset.Now; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok((await GetRatesAsync(cancellationToken)).Data.First(x => x.Id == r.Id));
        }

        public async Task<ResponseDto<LegalRateDto>> RateActionAsync(FlowActionDto dto, CancellationToken cancellationToken)
        {
            PsLegalRate r = await _uow.Set<PsLegalRate>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (r == null) return ResponseDtoHelper.NotFound<LegalRateDto>("نرخ پیدا نشد.");
            switch (dto.Action?.Trim().ToLowerInvariant())
            {
                case "verify":
                    if (r.Status != PsRateStatus.Draft) return Bad<LegalRateDto>("فقط نرخ پیش‌نویس تایید می‌شود.");
                    if (!_user.IsInRole(PsRoles.RateVerifiers)) return Bad<LegalRateDto>(NotAllowed + " (مدیر مالی)");
                    if (LedgerMath.Touched(r.CreatorUserId, r.EditorUserIds, Me)) return Bad<LegalRateDto>("نرخ را کسی غیر از ثبت‌کننده یا ویراستار آن تایید می‌کند.");
                    r.Status = PsRateStatus.Verified; r.VerifiedAt = DateTimeOffset.Now; r.VerifiedByUserId = Me;
                    break;
                case "retire":
                    if (r.Status == PsRateStatus.Retired) return Bad<LegalRateDto>("نرخ قبلاً کنار گذاشته شده است.");
                    if (!_user.IsInRole(PsRoles.RateVerifiers + "," + PsRoles.RateEditors)) return Bad<LegalRateDto>(NotAllowed);
                    if (Clean(dto.Note) == null) return Bad<LegalRateDto>("علت کنار گذاشتن را بنویسید.");
                    r.Status = PsRateStatus.Retired;
                    break;
                default:
                    return Bad<LegalRateDto>("عمل نامعتبر است.");
            }
            r.ModifyDate = DateTimeOffset.Now; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok((await GetRatesAsync(cancellationToken)).Data.First(x => x.Id == r.Id));
        }

        // =========================================================================================== CBS linked to WBS

        public async Task<ResponseDto<List<CbsItemDto>>> GetCbsAsync(int? purchaseOrderId, int? contractId, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PsRoles.Readers)) return Bad<List<CbsItemDto>>(NotAllowed);
            IQueryable<CtCbsItem> q = _uow.Set<CtCbsItem>().AsNoTracking().Where(c => !c.Deleted);
            if (purchaseOrderId.HasValue) q = q.Where(c => c.PurchaseOrderId == purchaseOrderId.Value);
            else if (contractId.HasValue) q = q.Where(c => c.ContractId == contractId.Value);
            else return Bad<List<CbsItemDto>>("قرارداد یا سفارش را مشخص کنید.");
            List<CtCbsItem> rows = await q.OrderBy(c => c.Code).ToListAsync(cancellationToken);
            List<int> nodeIds = rows.Where(c => c.WbsNodeId.HasValue).Select(c => c.WbsNodeId.Value).ToList();
            var nodes = await _uow.Set<PcWbsNode>().AsNoTracking().Where(n => nodeIds.Contains(n.Id)).Select(n => new { n.Id, n.Code, n.Title }).ToListAsync(cancellationToken);
            var result = new List<CbsItemDto>();
            foreach (CtCbsItem c in rows)
            {
                decimal? progress = c.WbsNodeId.HasValue ? await NodeProgressAsync(c.WbsNodeId.Value, cancellationToken) : null;
                decimal claimed = await _uow.Set<PsStatementLine>().AsNoTracking().Where(l => l.CbsItemId == c.Id && !l.Statement.Deleted && l.Statement.Status != PsStatus.Rejected && l.Statement.Status != PsStatus.Returned && l.Statement.Status != PsStatus.Draft)
                    .SumAsync(l => (decimal?)l.ThisPeriodAmount, cancellationToken) ?? 0;
                var node = nodes.FirstOrDefault(n => n.Id == c.WbsNodeId);
                result.Add(new CbsItemDto { Id = c.Id, Code = c.Code, Title = c.Title, Amount = c.Amount, WbsNodeId = c.WbsNodeId, WbsCode = node?.Code, WbsTitle = node?.Title, ProgressPercent = progress, EarnedAmount = progress.HasValue ? R(c.Amount * progress.Value / 100m) : 0, ClaimedAmount = claimed });
            }
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<List<CbsItemDto>>> SaveCbsAsync(SaveCbsDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<List<CbsItemDto>>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(PsRoles.Preparers + ",PLANNING,PMO")) return Bad<List<CbsItemDto>>(NotAllowed);
            if (!dto.PurchaseOrderId.HasValue && !dto.ContractId.HasValue) return Bad<List<CbsItemDto>>("قرارداد یا سفارش را مشخص کنید.");
            int? wbsContract = dto.ContractId;
            if (dto.PurchaseOrderId.HasValue)
            {
                PurchaseOrder o = await _uow.Set<PurchaseOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.PurchaseOrderId.Value && !x.Deleted, cancellationToken);
                if (o == null) return ResponseDtoHelper.NotFound<List<CbsItemDto>>("سفارش پیدا نشد.");
                wbsContract = o.ContractId;
            }
            List<SaveCbsItemDto> items = (dto.Items ?? new List<SaveCbsItemDto>()).Where(i => i != null).ToList();
            if (items.Select(i => Clean(i.Code)).Distinct().Count() != items.Count || items.Any(i => Clean(i.Code) == null || Clean(i.Title) == null || i.Amount < 0)) return Bad<List<CbsItemDto>>("کد، عنوان و مبلغ هر بند را درست بنویسید؛ کدها تکراری نباشد.");
            foreach (SaveCbsItemDto i in items.Where(i => i.WbsNodeId.HasValue))
                if (!await _uow.Set<PcWbsNode>().AnyAsync(n => n.Id == i.WbsNodeId.Value && n.ContractId == wbsContract && !n.Deleted, cancellationToken)) return Bad<List<CbsItemDto>>($"بند {i.Code}: گره WBS متعلق به پروژه این قرارداد نیست.");
            IQueryable<CtCbsItem> existingQ = dto.PurchaseOrderId.HasValue ? _uow.Set<CtCbsItem>().Where(c => c.PurchaseOrderId == dto.PurchaseOrderId) : _uow.Set<CtCbsItem>().Where(c => c.ContractId == dto.ContractId && c.PurchaseOrderId == null);
            List<CtCbsItem> existing = await existingQ.ToListAsync(cancellationToken);
            List<int> usedIds = existing.Select(c => c.Id).ToList();
            if (await _uow.Set<PsStatementLine>().AnyAsync(l => l.CbsItemId != null && usedIds.Contains(l.CbsItemId.Value) && !items.Select(i => i.Code).Contains(existing.First(e => e.Id == l.CbsItemId.Value).Code), cancellationToken))
                return Bad<List<CbsItemDto>>("بندی که در صورت‌وضعیت استفاده شده حذف نمی‌شود.");
            foreach (CtCbsItem c in existing.Where(e => !items.Any(i => i.Code.Trim() == e.Code))) _uow.Set<CtCbsItem>().Remove(c);
            foreach (SaveCbsItemDto i in items)
            {
                CtCbsItem c = existing.FirstOrDefault(e => e.Code == i.Code.Trim());
                if (c == null) { c = new CtCbsItem { PurchaseOrderId = dto.PurchaseOrderId, ContractId = dto.PurchaseOrderId.HasValue ? wbsContract : dto.ContractId, CreateDate = DateTimeOffset.Now, CreatorUserId = Me }; _uow.Set<CtCbsItem>().Add(c); }
                c.Code = i.Code.Trim(); c.Title = i.Title.Trim(); c.Amount = i.Amount; c.WbsNodeId = i.WbsNodeId; c.ModifyDate = DateTimeOffset.Now; c.ModifierUserId = Me;
            }
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetCbsAsync(dto.PurchaseOrderId, dto.ContractId, cancellationToken);
        }

        // =========================================================================================== cash flow and analytics

        public async Task<ResponseDto<CashFlowDto>> GetCashFlowAsync(int? purchaseOrderId, int? contractId, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PsRoles.Readers)) return Bad<CashFlowDto>(NotAllowed);
            if (!purchaseOrderId.HasValue && !contractId.HasValue) return Bad<CashFlowDto>("قرارداد یا سفارش را مشخص کنید.");
            var months = new SortedDictionary<string, CashFlowMonthDto>();
            CashFlowMonthDto Month(DateTime d)
            {
                string key = d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                if (!months.TryGetValue(key, out CashFlowMonthDto m)) months[key] = m = new CashFlowMonthDto { Month = key };
                return m;
            }
            IQueryable<PsCashPlanItem> plan = _uow.Set<PsCashPlanItem>().AsNoTracking().Where(p => !p.Deleted);
            plan = purchaseOrderId.HasValue ? plan.Where(p => p.PurchaseOrderId == purchaseOrderId) : plan.Where(p => p.ContractId == contractId);
            foreach (PsCashPlanItem p in await plan.ToListAsync(cancellationToken))
            {
                if (p.Direction == PsCashDirection.Payment) Month(p.DueDate).PlannedPayment += p.Amount; else Month(p.DueDate).PlannedReceipt += p.Amount;
            }
            // the payment milestones of a supplier contract plan their own payments (a milestone that was paid is actual, not planned)
            string title = null;
            if (purchaseOrderId.HasValue)
            {
                PurchaseOrder order = await _uow.Set<PurchaseOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == purchaseOrderId.Value && !o.Deleted, cancellationToken);
                title = order?.Number;
                var terms = await _uow.Set<PurchaseOrderPaymentTerm>().AsNoTracking().Where(t => t.PurchaseOrderId == purchaseOrderId.Value && t.Status == PaymentTermStatus.Pending).ToListAsync(cancellationToken);
                if (order != null)
                    foreach (PurchaseOrderPaymentTerm t in terms)
                    {
                        DateTime due = (t.Kind == PaymentTermKind.Advance ? order.OrderDate : order.DeliveryDate ?? order.OrderDate).AddDays(t.Kind == PaymentTermKind.ChequeAfterInvoice ? t.DueDays ?? 60 : 0);
                        Month(due).PlannedPayment += t.Amount;
                    }
                var paid = await _uow.Set<PaymentRequest>().AsNoTracking().Where(p => p.PurchaseOrderId == purchaseOrderId.Value && p.Status == PaymentRequestStatus.Paid && p.PaidAt != null && !p.Deleted).Select(p => new { p.PaidAt, Net = p.Amount - p.Deductions }).ToListAsync(cancellationToken);
                foreach (var p in paid) Month(p.PaidAt.Value.DateTime).ActualPayment += p.Net;
            }
            IQueryable<PsStatement> clientPaid = _uow.Set<PsStatement>().AsNoTracking().Where(s => s.Status == PsStatus.Paid && s.PaidDate != null && !s.Deleted && s.Side == PsSide.Client);
            clientPaid = purchaseOrderId.HasValue ? clientPaid.Where(s => s.PurchaseOrderId == purchaseOrderId) : clientPaid.Where(s => s.ContractId == contractId);
            foreach (PsStatement s in await clientPaid.ToListAsync(cancellationToken)) Month(s.PaidDate.Value).ActualReceipt += s.ReceivedAmount ?? 0;
            if (contractId.HasValue && !purchaseOrderId.HasValue)
            {
                var supplierPaid = await _uow.Set<PaymentRequest>().AsNoTracking().Where(p => p.PurchaseOrder.ContractId == contractId.Value && p.Status == PaymentRequestStatus.Paid && p.PaidAt != null && !p.Deleted).Select(p => new { p.PaidAt, Net = p.Amount - p.Deductions }).ToListAsync(cancellationToken);
                foreach (var p in supplierPaid) Month(p.PaidAt.Value.DateTime).ActualPayment += p.Net;
                title = await _uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().Where(c => c.Id == contractId.Value).Select(c => c.Code).FirstOrDefaultAsync(cancellationToken);
            }
            decimal cumP = 0, cumA = 0;
            foreach (CashFlowMonthDto m in months.Values)
            {
                cumP += m.PlannedPayment - m.PlannedReceipt; cumA += m.ActualPayment - m.ActualReceipt;
                m.CumulativePlanned = cumP; m.CumulativeActual = cumA;
            }
            return ResponseDtoHelper.Ok(new CashFlowDto
            {
                PurchaseOrderId = purchaseOrderId, ContractId = contractId, Title = title, Months = months.Values.ToList(),
                PlannedTotal = months.Values.Sum(m => m.PlannedPayment), ActualTotal = months.Values.Sum(m => m.ActualPayment),
            });
        }

        public async Task<ResponseDto<CashFlowDto>> SaveCashPlanAsync(SaveCashPlanDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<CashFlowDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(PsRoles.Preparers + ",PLANNING,FINANCIAL,FINANCEMANAGER,PMO")) return Bad<CashFlowDto>(NotAllowed);
            if (!dto.PurchaseOrderId.HasValue && !dto.ContractId.HasValue) return Bad<CashFlowDto>("قرارداد یا سفارش را مشخص کنید.");
            List<SaveCashPlanItemDto> items = (dto.Items ?? new List<SaveCashPlanItemDto>()).Where(i => i != null).ToList();
            if (items.Any(i => i.Amount <= 0 || i.DueDate == default || !Enum.IsDefined(typeof(PsCashDirection), i.Direction))) return Bad<CashFlowDto>("مبلغ، تاریخ یا نوع یکی از ردیف‌های برنامه نامعتبر است.");
            IQueryable<PsCashPlanItem> q = _uow.Set<PsCashPlanItem>().Where(p => !p.Deleted);
            q = dto.PurchaseOrderId.HasValue ? q.Where(p => p.PurchaseOrderId == dto.PurchaseOrderId) : q.Where(p => p.ContractId == dto.ContractId && p.PurchaseOrderId == null);
            _uow.Set<PsCashPlanItem>().RemoveRange(await q.ToListAsync(cancellationToken));
            foreach (SaveCashPlanItemDto i in items)
                _uow.Set<PsCashPlanItem>().Add(new PsCashPlanItem { PurchaseOrderId = dto.PurchaseOrderId, ContractId = dto.ContractId, Direction = i.Direction, DueDate = i.DueDate.Date, Amount = i.Amount, Note = Clean(i.Note), CreateDate = DateTimeOffset.Now, CreatorUserId = Me });
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetCashFlowAsync(dto.PurchaseOrderId, dto.ContractId, cancellationToken);
        }

        public async Task<ResponseDto<StatementAnalyticsDto>> GetAnalyticsAsync(FlowQuery query, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PsRoles.Readers)) return Bad<StatementAnalyticsDto>(NotAllowed);
            query ??= new FlowQuery();
            IQueryable<PsStatement> q = _uow.Set<PsStatement>().AsNoTracking().Where(s => !s.Deleted && s.Status != PsStatus.Draft && s.Status != PsStatus.Rejected);
            if (query.ContractId.HasValue) q = q.Where(s => s.ContractId == query.ContractId.Value);
            if (query.PurchaseOrderId.HasValue) q = q.Where(s => s.PurchaseOrderId == query.PurchaseOrderId.Value);
            if (query.DateFrom.HasValue) { DateTime f = query.DateFrom.Value.Date; q = q.Where(s => s.PeriodTo >= f); }
            if (query.DateTo.HasValue) { DateTime t = query.DateTo.Value.Date; q = q.Where(s => s.PeriodFrom <= t); }
            List<PsStatement> rows = await q.Include(s => s.Lines).ToListAsync(cancellationToken);
            Dictionary<int, string> codes = await _uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().AsNoTracking().Where(c => rows.Select(r => r.ContractId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, cancellationToken);
            var result = new StatementAnalyticsDto
            {
                Count = rows.Count, TotalPayable = rows.Sum(r => r.PayableThisPeriod), TotalGross = rows.Sum(r => r.GrossThisPeriod), TotalDeductions = rows.Sum(r => r.NonconformityDeductions + r.LegalDeductions),
            };
            var durations = rows.Where(r => r.PreparedAt.HasValue && r.PaymentAuthorisedAt.HasValue).Select(r => (r.PaymentAuthorisedAt.Value - r.PreparedAt.Value).TotalDays).ToList();
            result.AverageApprovalDays = durations.Count == 0 ? 0 : Math.Round((decimal)durations.Average(), 1);
            result.ByContract = rows.GroupBy(r => r.ContractId).Select(g => new AnalyticsRowDto { Key = g.Key?.ToString() ?? "-", Title = g.Key.HasValue && codes.TryGetValue(g.Key.Value, out string c) ? c : "بدون پروژه", Count = g.Count(), Amount = g.Sum(x => x.PayableThisPeriod) }).OrderByDescending(x => x.Amount).ToList();
            result.ByPeriod = rows.GroupBy(r => r.PeriodTo.ToString("yyyy-MM", CultureInfo.InvariantCulture)).OrderBy(g => g.Key).Select(g => new AnalyticsRowDto { Key = g.Key, Title = g.Key, Count = g.Count(), Amount = g.Sum(x => x.PayableThisPeriod) }).ToList();
            result.ByStatus = rows.GroupBy(r => r.Status).Select(g => new AnalyticsRowDto { Key = ((int)g.Key).ToString(CultureInfo.InvariantCulture), Title = g.Key.ToString(), Count = g.Count(), Amount = g.Sum(x => x.PayableThisPeriod) }).ToList();
            List<int> cbsIds = rows.SelectMany(r => r.Lines).Where(l => l.CbsItemId.HasValue).Select(l => l.CbsItemId.Value).Distinct().ToList();
            Dictionary<int, string> cbsCodes = await _uow.Set<CtCbsItem>().AsNoTracking().Where(c => cbsIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code + " " + c.Title, cancellationToken);
            result.ByCbs = rows.SelectMany(r => r.Lines).Where(l => l.CbsItemId.HasValue).GroupBy(l => l.CbsItemId.Value)
                .Select(g => new AnalyticsRowDto { Key = g.Key.ToString(CultureInfo.InvariantCulture), Title = cbsCodes.TryGetValue(g.Key, out string t) ? t : "#" + g.Key, Count = g.Count(), Amount = g.Sum(x => x.ThisPeriodAmount) }).OrderByDescending(x => x.Amount).ToList();
            return ResponseDtoHelper.Ok(result);
        }
    }
}
