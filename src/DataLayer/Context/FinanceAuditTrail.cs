using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Common.IdentityToolkit;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Sales;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using LegacyContracts = Entities.Model.NasbNiro.Contracts;
using Pc = Entities.Model.NasbNiro.ProjectControl;
using Proc = Entities.Model.NasbNiro.Procurement;

namespace DataLayer.Context
{
    /// <summary>
    /// Finance audit trail (release F3): every change of the audited finance records is written to the append-only
    /// <see cref="FnAuditLog"/> in the same SaveChanges as the change itself (before / after values of the changed fields,
    /// user, time, IP). Personal data is masked; nothing in the trail can be edited or deleted.
    /// </summary>
    internal static class FinanceAuditTrail
    {
        /// <summary>Audited record types. A null list means all scalar properties; otherwise only the listed ones are recorded.</summary>
        private static readonly Dictionary<Type, string[]> Audited = new Dictionary<Type, string[]>
        {
            { typeof(FnAccount), null },
            { typeof(FnDetailAccount), null },
            { typeof(FnVoucher), null },
            { typeof(FnFiscalYear), null },
            { typeof(FnPeriod), null },
            { typeof(AccountMapping), null },
            { typeof(FnTaxRate), null },
            { typeof(FnBankAccount), null },
            // only the bank account number (IBAN) of an employee is audited; no salary or other personal data
            { typeof(FnEmployee), new[] { "BankIban" } },
            { typeof(FnAsset), null },
            { typeof(FnLegalEntity), null },
            { typeof(FnIntercompanyPair), null },
            { typeof(FnApprovalThreshold), null },
            { typeof(FnAttachmentRule), null },
            { typeof(FnSetting), null },
            { typeof(FnCloseChecklist), null },
            { typeof(FnMigrationAccountMap), null },
            { typeof(FnParallelRun), null },
            { typeof(FnExportLayoutColumn), null },
            { typeof(FnDetailSepidarMap), null },
            { typeof(FnCurrency), null },
            { typeof(FnExchangeRate), null },
            { typeof(FnLetterOfCredit), null },
            { typeof(FnBudget), null },
            { typeof(FnBudgetLine), null },
            { typeof(FnOverheadRate), null },
            { typeof(FnAllocationKey), null },
            { typeof(FnStandardCost), null },
            { typeof(FnContractRevenue), null },
            { typeof(FnDeferral), null },
            { typeof(FnProvision), null },
            // review 2, finding 21: voucher lines (manual vouchers), pay structure, payments, loans and supplier IBANs
            { typeof(FnVoucherLine), null },
            { typeof(FnPayrollItem), null },
            { typeof(FnPayrollRateTable), null },
            { typeof(FnTaxBracket), null },
            { typeof(FnPayment), null },
            { typeof(FnReceipt), null },
            { typeof(FnLoan), null },
            // review 3 (finding 21): payment lines (payee IBAN masked), loan instalments and the F3b records; settlements and bonus runs record status and totals only, never a person's pay
            { typeof(FnPaymentLine), new[] { "PayeeIban", "Amount", "Instrument", "BankAccountId" } },
            { typeof(FnLoanInstallment), null },
            { typeof(FnInsuranceClearance), null },
            { typeof(FnBankGuarantee), null },
            { typeof(FnSupplierNote), null },
            { typeof(FnRetentionRelease), null },
            { typeof(FnTermination), new[] { "Status", "VoucherId", "TerminationDate" } },
            { typeof(FnBonusRun), new[] { "Status", "Year", "TotalGross", "TotalNet", "VoucherId" } },
            { typeof(Entities.Model.NasbNiro.Procurement.SupplierProfile), new[] { "Iban" } },
            // quality and inventory (R1q): the decisions that release, scrap, waive or relax something; the quality settings; the item quality gates; the calibration waiver
            { typeof(Entities.Model.NasbNiro.Inventory.NonConformance), new[] { "Status", "Severity", "Disposition", "CancelReason", "CancelledByUserId", "QualityApprovedByUserId", "EngineeringApprovedByUserId", "ManufacturingApprovedByUserId", "ConcessionApprovedByUserId", "ExecutedByUserId", "ClosedByUserId", "ScrapDocumentId", "ScrappedSerialUnitId", "ReworkOperationId", "ReinspectionKind", "ReinspectionRecordId", "CapaId", "ResultDocumentId" } },
            { typeof(Entities.Model.NasbNiro.Inventory.InventoryItem), new[] { "Code", "RequiresIncomingInspection", "RequiredCertificateType", "ShelfLifeDays" } },
            { typeof(Entities.Model.NasbNiro.Inventory.ItemQualityChange), null },
            { typeof(Entities.Model.NasbNiro.Quality.QualitySetting), null },
            { typeof(Entities.Model.NasbNiro.Quality.MeasuringEquipment), new[] { "Code", "RequiresCalibration", "CalibrationWaiverReason", "CalibrationWaivedByUserId", "CalibrationWaiverRequestReason", "CalibrationWaiverRequestedByUserId", "Status", "OutOfServiceReason" } },
            { typeof(Entities.Model.NasbNiro.Quality.Capa), new[] { "Number", "Status", "OwnerUserId", "CloseNote", "ClosedByUserId", "Iteration", "EscalationLevel", "CancelReason", "CancelledByUserId" } },
            { typeof(Entities.Model.NasbNiro.Quality.Itp), new[] { "Number", "Status", "Revision", "NoticeDays", "ClientApprovalReference" } },
            { typeof(Entities.Model.NasbNiro.Quality.ItpActivity), new[] { "Status", "NotApplicableReference", "NotificationSentAt", "NotificationLetterId", "StatusNote", "EvidenceNote", "EvidenceDocumentId" } },
            { typeof(Entities.Model.NasbNiro.Quality.ReleaseNote), new[] { "Number", "Status", "ReleasedByUserId", "CancelReason", "CertificateOfConformityNumber" } },
            { typeof(Entities.Model.NasbNiro.Quality.DocumentRevisionRecord), new[] { "Revision", "Status", "ApprovedByUserId", "EffectiveDate" } },
            { typeof(Entities.Model.NasbNiro.Quality.ManagementReviewDecision), new[] { "Status", "ResponsibleUserId", "DueDate", "DoneByUserId", "DoneNote" } },
            // office automation, review 1 (M7): who may act for a position, who sits in it, the numbering rules, the attendance policy, manual and imported clock events and the payroll attendance input
            { typeof(OfDelegation), null },
            { typeof(OfPosition), new[] { "Title", "UnitId", "UserId", "EmployeeId", "IsManager", "CanSign", "IsActive" } },
            { typeof(OfNumberingRule), null },
            { typeof(OfAttendancePolicy), null },
            { typeof(OfClockImportProfile), null },
            { typeof(OfClockEvent), new[] { "EmployeeId", "Timestamp", "Direction", "Source", "Reason", "DeviceCode" } },
            // production / shop floor (release P1): master data, routing approvals, the release gate, status changes and edits of time bookings
            { typeof(MfWorkCenter), null },
            { typeof(MfMachineInfo), null },
            { typeof(MfOperationStandard), null },
            { typeof(MfRoutingRevision), null },
            { typeof(MfReasonCode), null },
            { typeof(MfProductionOrder), new[] { "Status", "Quantity", "RoutingRevisionId", "ReleasedByUserId", "ReleaseOverride", "ReleaseOverrideReason", "KitOverrideReason", "CancelReason" } },
            { typeof(MfOperation), new[] { "Status", "SkipReason", "WorkCenterId", "QcAccepted" } },
            { typeof(MfTimeBooking), new[] { "OperationId", "EmployeeId", "MachineId", "Kind", "StartAt", "EndAt", "Minutes", "Hours", "GoodQty", "ScrapQty", "ReworkQty", "SupervisorReason", "EditReason", "Source" } },
            { typeof(MfMaintenanceRequest), new[] { "MachineId", "Status", "Description" } },
            // release P1 part B: maintenance gates and master data, and the cost runs (one row a day; the per-booking marks are not listed)
            { typeof(MfMaintenancePlan), null },
            { typeof(MfMaintenanceOrder), new[] { "MachineId", "Kind", "Status", "PlanId", "RequestId", "Task", "DueDate", "DowntimeStart", "DowntimeEnd", "Cause", "ActionTaken", "CostCenter", "MaterialRequestId", "IssueDocumentId", "StartedByUserId", "DoneByUserId", "CancelReason" } },
            { typeof(MfMaintenanceLabour), new[] { "OrderId", "EmployeeId", "WorkDate", "Hours" } },
            { typeof(MfMaintenancePart), new[] { "OrderId", "ItemId", "Quantity" } },
            { typeof(MfCostRun), new[] { "Number", "PostingDate", "RunByUserId", "BookingCount", "MachineHours", "OverheadAmount", "AccountingEventId" } }, // not the labour amount or hours: with one employee they give his hourly payroll cost
            // sales (release S1): the thresholds, the credit limit, the reason codes and the gates (bid decision, proposal approval, price list approval, credit approval); contacts and sites hold personal data and are not listed
            { typeof(SlSetting), null },
            { typeof(SlReasonCode), null },
            { typeof(SlCustomerProfile), null },
            { typeof(SlCustomerLink), null },
            { typeof(SlBidDecision), new[] { "Status", "SanctionCheck", "CapacityOverloadWeeks", "DecidedByUserId", "DecidedAt", "DecisionReason" } },
            { typeof(SlEstimate), new[] { "Status", "TotalCost", "Price", "FinalizedByUserId" } },
            { typeof(SlProposal), new[] { "Revision", "Status", "Price", "EstimateTotalCost", "BelowCost", "ApprovedByUserId", "ApprovedAt", "ApprovalNote", "SubmittedByUserId", "ResultReasonId", "ContractId" } },
            { typeof(SlPriceList), new[] { "Status", "ValidFrom", "ValidTo", "ApprovedByUserId" } },
            { typeof(SlSalesOrder), new[] { "Status", "NetAmount", "CreditApprovalRequired", "CreditApprovedByUserId", "CreditApprovedNetAmount", "ConfirmedByUserId", "CancelReason" } },
            // after-sales service (release S1 part B): SLA table, settings, the installed base, the warranty decision and the gates; time entries and the labour cost of an order are not listed (they give a person's hourly pay)
            { typeof(Entities.Model.NasbNiro.AfterSales.SvSetting), null },
            { typeof(Entities.Model.NasbNiro.AfterSales.SvSlaRule), null },
            { typeof(Entities.Model.NasbNiro.AfterSales.SvInstalledUnit), new[] { "Tag", "CustomerId", "SiteId", "ContractId", "WarrantyMonths", "WarrantyStart", "WarrantyEnd", "WarrantySource", "Decommissioned", "IsConfirmed", "ConfirmedByUserId" } },
            { typeof(Entities.Model.NasbNiro.AfterSales.SvServiceRequest), new[] { "CustomerId", "UnitId", "Type", "Priority", "Status", "ResponseDue", "ResolutionDue", "AssignedToUserId", "ClosedAt" } },
            { typeof(Entities.Model.NasbNiro.AfterSales.SvServiceOrder), new[] { "Status", "UnitId", "RequestId", "Decision", "CauseIsMisuse", "DecidedByUserId", "GoodwillRequestedByUserId", "ReportDocumentId", "ClosedByUserId", "InvoiceId", "WarrantyEventId" } },
            { typeof(Entities.Model.NasbNiro.AfterSales.SvContract), new[] { "Status", "StartDate", "EndDate", "VisitIntervalMonths", "PriceRial", "ActivatedByUserId" } },
            { typeof(Entities.Model.NasbNiro.AfterSales.SlSurvey), new[] { "Status", "Kind", "AverageScore" } },
            { typeof(FnAttendanceSheet), new[] { "EmployeeId", "Year", "Month", "DaysWorked", "OvertimeHours", "NightHours", "HolidayHours", "ShiftType", "PaidLeaveDays", "SickLeaveDays", "UnpaidLeaveDays", "AbsenceDays", "MissionDays" } },
            // R1a: user administration. Created, changed and deleted users, their roles and the claims that carry the temporary-password flag and the requests for sensitive roles.
            // Never listed: the password hash, the security stamp, the token serial, the confirmation codes and the birth date. The national code and the phone number are masked.
            { typeof(Entities.Identity.User), new[] { "UserName", "Email", "EmailConfirmed", "PhoneNumber", "PhoneNumberConfirmed", "Name", "LastName", "NationalCode", "IsActive", "IsDeleted", "LockoutEnabled", "LockoutEnd" } },
            { typeof(Entities.Identity.UserRole), new[] { "UserId", "RoleId" } },
            { typeof(Entities.Identity.UserClaim), new[] { "UserId", "ClaimType", "ClaimValue" } },

            // R1a (finding H7): the legacy contract, the project control records that move money, dates or authority, and the procurement headers.
            // Fields that the system recomputes all the time (float, forecast, percent cache, snapshots) and long free text are not listed; "Deleted" is listed so a soft delete leaves a row.
            { typeof(LegacyContracts.ContractEntity), null },
            { typeof(Pc.PcSetting), null },
            { typeof(Pc.PcApprovalAuthority), null },
            { typeof(Pc.PcCalendar), null },
            { typeof(Pc.PcCalendarException), null },
            { typeof(Pc.PcContractTerms), new[] { "ContractId", "ProjectCode", "Category", "CurrencyCode", "ValueExVat", "VatPercent", "AdvancePercent", "RetentionPercent", "LatePenaltyPercentPerWeek", "PenaltyCapPercent", "GraceDays", "ChangeProcedureAgreed", "ProjectManagerUserId", "CalendarId", "CurrentBaselineId", "ScheduleStartDate", "ContractDeliveryDate", "AccrualMode", "ClientReviewDays", "ManagementReserve", "Deleted" } },
            { typeof(Pc.PcProjectInitiation), new[] { "ContractId", "CurrentStep", "Completed", "Deleted" } },
            { typeof(Pc.PcInitiationStepRecord), new[] { "InitiationId", "Step", "SlaDays", "DueDate", "DoneAt", "DoneByUserId" } },
            { typeof(Pc.PcProjectCharter), new[] { "ContractId", "ManagerAcceptedAt", "ApprovedAt", "ApprovedByUserId", "Deleted" } },
            { typeof(Pc.PcProjectPlan), new[] { "ContractId", "ApprovedAt", "ApprovedByUserId", "Deleted" } },
            { typeof(Pc.PcContractReview), new[] { "ContractId", "TenderCode", "Kind", "Decision", "DecisionNote", "DecidedAt", "DecidedByUserId", "CapabilityCheckedByUserId", "Deleted" } },
            { typeof(Pc.PcContractAmendment), new[] { "Number", "ContractId", "Type", "Origin", "PriceDelta", "DaysDelta", "CeoApprovedAt", "CeoApprovedByUserId", "CustomerApprovalReference", "CustomerApprovalDate", "Deleted" } },
            { typeof(Pc.PcPaymentMilestone), new[] { "ContractId", "Seq", "Title", "Kind", "Trigger", "TriggerPercent", "WorkOrderId", "ActivityId", "PercentOfValue", "Amount", "PlannedDate", "Status", "DueAt", "PaidAt", "ProvisionalStatementId", "FinanceRef", "ClientLetterRef", "Deleted" } },
            { typeof(Pc.PcGuaranteeInfo), new[] { "SourceKind", "SourceId", "ContractId", "GuaranteeType", "AmountDecimal", "CurrencyCode", "IssuerKind", "ValidFrom", "ExpiryDate", "ReleaseTrigger", "Status", "Deleted" } },
            { typeof(Pc.PcPenaltyAssessment), new[] { "ContractId", "MilestoneId", "WorkOrderId", "Subject", "DueDate", "ActualDate", "LateDays", "GraceDays", "RatePercentPerWeek", "CapPercent", "BaseAmount", "Amount", "Status", "DecisionReason", "DecidedByUserId", "Deleted" } },
            { typeof(Pc.PcVariationOrder), new[] { "Number", "ContractId", "ChangeRequestId", "AmendmentId", "PriceDelta", "DaysDelta", "ClientApprovalReference", "ClientApprovalDate", "Status", "Deleted" } },
            { typeof(Pc.PcChangeRequest), new[] { "Number", "ContractId", "Origin", "Type", "RequesterUserId", "Status", "RegisteredAt", "DueDate", "Escalated", "RequestedPriceDelta", "RequestedDaysDelta", "ImpactDeltaDays", "ImpactDeltaCost", "ImpactOnCriticalPath", "Decision", "DecisionNote", "DecidedByUserId", "DecidedByRole", "DecidedAt", "BaselineId", "Deleted" } },
            { typeof(Pc.PcChangeBoardMember), new[] { "ChangeRequestId", "Role", "UserId", "Vote", "VotedAt" } },
            { typeof(Pc.PcClaim), new[] { "Number", "ContractId", "Direction", "DaysQuantified", "AmountQuantified", "Status", "IsCrossClaim", "PmoApproved", "DesignManufacturingApproved", "ReviewedByUserId", "ReviewedAt", "Deleted" } },
            { typeof(Pc.PcManualProgress), new[] { "ActivityId", "Percent", "State", "ProposedByUserId", "DecidedByUserId", "DecidedAt", "DecisionNote" } },
            { typeof(Pc.PcDailyReport), new[] { "ContractId", "Unit", "ReportDate", "State", "ConfirmedByUserId", "ConfirmedAt", "Deleted" } },
            { typeof(Pc.PcShipment), new[] { "Number", "ContractId", "WorkOrderId", "ReleaseNoteId", "Status", "ShippedAt", "DeliveryReceiptNumber", "DeliveredAt", "HandoverMinutesNumber", "HandoverAt", "Deleted" } },
            { typeof(Pc.PcBaseline), new[] { "ContractId", "Number", "Kind", "Status", "Reason", "ChangeRequestId", "ProjectStart", "ProjectFinish", "Bac", "ManagementReserve", "ApprovedAt", "ApprovedByUserId", "Deleted" } },
            { typeof(Pc.PcBaselineSignature), new[] { "BaselineId", "UnitRole", "UserId", "SignedAt" } },
            { typeof(Pc.PcCostBaselineLine), new[] { "ContractId", "BaselineId", "WbsNodeId", "ActivityId", "CostClass", "ResourceClass", "Amount", "Description" } },
            // generated in bulk from the legacy BOM tree: the insert is not listed, a later change is
            { typeof(Pc.PcWbsNode), new[] { "ContractId", "Code", "Title", "NodeType", "Weight", "BudgetAmount", "ResponsibleUnit", "ApprovedAt", "ApprovedByUserId", "Deleted" } },
            { typeof(Pc.PcActivity), new[] { "ContractId", "Code", "Title", "DurationDays", "Weight", "BudgetAtCompletion", "ResponsibleUnit", "ConstraintType", "ConstraintDate", "ActualStart", "ActualFinish", "Status", "OnHoldReason", "Deleted" } },
            { typeof(Proc.ProcurementSetting), null },
            { typeof(Proc.PurchaseRequest), new[] { "Number", "Source", "Kind", "Status", "RequestDate", "WorkOrderId", "ContractId", "CostCenter", "EstimatedAmount", "SubmittedAt", "PlanningApprovedAt", "PlanningApprovedByUserId", "ManagementApprovedAt", "ManagementApprovedByUserId", "BoardResolution", "RejectedAt", "RejectedByUserId", "RejectReason", "Deleted" } },
            { typeof(Proc.PurchaseInquiry), new[] { "Number", "Status", "PurchaseRequestId", "RequiredQuotes", "FewerQuotesReason", "SelectedQuoteId", "SelectionReason", "SelectedAt", "SelectedByUserId", "FinancialApprovedAt", "FinancialApprovedByUserId", "CommercialApprovedAt", "CommercialApprovedByUserId", "TechnicalApprovedAt", "TechnicalApprovedByUserId", "ManagementApprovedAt", "ManagementApprovedByUserId", "Deleted" } },
            { typeof(Proc.PurchaseOrder), new[] { "Number", "Status", "Kind", "OrderDate", "ProviderId", "InquiryId", "WorkOrderId", "ContractId", "Currency", "ExchangeRate", "Incoterms", "PaymentTerms", "VatPercent", "AdvancePercent", "AdvanceGuarantee", "AdvanceGuaranteeExpiry", "PerformanceGuarantee", "RetentionPercent", "ContractNumber", "DeliveryDate", "LatePenaltyPercentPerWeek", "TotalAmount", "ApprovedAt", "ApprovedByUserId", "ReleasedAt", "ReleasedByUserId", "ClosedAt", "CancelledAt", "CancelReason", "Deleted" } },
            { typeof(Proc.SupplierInvoice), new[] { "Number", "Status", "ProviderId", "PurchaseOrderId", "InvoiceNumber", "InvoiceDate", "IsOfficial", "NetAmount", "VatAmount", "TotalAmount", "NonRecoverableVatAmount", "PriceVariance", "MatchNote", "ApprovedAt", "ApprovedByUserId", "CancelledAt", "CancelReason", "Deleted" } },
            { typeof(Proc.PaymentRequest), new[] { "Number", "Type", "Status", "RequestDate", "ProviderId", "PurchaseOrderId", "InvoiceId", "Amount", "Deductions", "DeductionNote", "QcApproved", "ApprovedAt", "ApprovedByUserId", "PaidAt", "PaidByUserId", "PaymentReference", "RejectedAt", "RejectReason", "Deleted" } },
        };

        /// <summary>Persian label of the record types added in R1a, put in front of the number or code in the summary of the trail row.</summary>
        private static readonly Dictionary<Type, string> Labels = new Dictionary<Type, string>
        {
            { typeof(LegacyContracts.ContractEntity), "قرارداد" },
            { typeof(Pc.PcSetting), "تنظیم کنترل پروژه" },
            { typeof(Pc.PcApprovalAuthority), "اختیار تصمیم‌گیری کنترل پروژه" },
            { typeof(Pc.PcCalendar), "تقویم کاری" },
            { typeof(Pc.PcCalendarException), "استثنای تقویم کاری" },
            { typeof(Pc.PcContractTerms), "شرایط قرارداد (کنترل پروژه)" },
            { typeof(Pc.PcProjectInitiation), "راه‌اندازی پروژه" },
            { typeof(Pc.PcInitiationStepRecord), "گام راه‌اندازی پروژه" },
            { typeof(Pc.PcProjectCharter), "منشور پروژه" },
            { typeof(Pc.PcProjectPlan), "برنامه پروژه" },
            { typeof(Pc.PcContractReview), "بازنگری قرارداد" },
            { typeof(Pc.PcContractAmendment), "الحاقیه قرارداد" },
            { typeof(Pc.PcPaymentMilestone), "نقطه پرداخت" },
            { typeof(Pc.PcGuaranteeInfo), "ضمانت‌نامه (کنترل پروژه)" },
            { typeof(Pc.PcPenaltyAssessment), "ارزیابی جریمه" },
            { typeof(Pc.PcVariationOrder), "دستور تغییر" },
            { typeof(Pc.PcChangeRequest), "درخواست تغییر" },
            { typeof(Pc.PcChangeBoardMember), "رأی هیئت تغییر" },
            { typeof(Pc.PcClaim), "ادعا" },
            { typeof(Pc.PcManualProgress), "پیشرفت دستی" },
            { typeof(Pc.PcDailyReport), "گزارش روزانه" },
            { typeof(Pc.PcShipment), "محموله" },
            { typeof(Pc.PcBaseline), "خط‌مبنای برنامه" },
            { typeof(Pc.PcBaselineSignature), "امضای خط‌مبنا" },
            { typeof(Pc.PcCostBaselineLine), "سطر خط‌مبنای هزینه" },
            { typeof(Pc.PcWbsNode), "گره WBS" },
            { typeof(Pc.PcActivity), "فعالیت برنامه" },
            { typeof(Proc.ProcurementSetting), "تنظیم تدارکات" },
            { typeof(Proc.PurchaseRequest), "درخواست خرید" },
            { typeof(Proc.PurchaseInquiry), "استعلام خرید" },
            { typeof(Proc.PurchaseOrder), "سفارش خرید" },
            { typeof(Proc.SupplierInvoice), "فاکتور تأمین‌کننده" },
            { typeof(Proc.PaymentRequest), "درخواست پرداخت" },
        };

        private static readonly HashSet<string> Masked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BankIban", "Iban", "PayeeIban", "NationalCode", "PhoneNumber" };
        private static readonly HashSet<string> Skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RowVersion" };

        public static void Capture(DbContext context, IHttpContextAccessor accessor)
        {
            List<EntityEntry> entries = context.ChangeTracker.Entries().ToList();
            foreach (EntityEntry e in entries)
            {
                if ((e.Entity is FnAuditLog || e.Entity is Entities.Model.NasbNiro.Ai.AiAuditEvent || e.Entity is Entities.Model.NasbNiro.Ai.AiMessage || e.Entity is Entities.Model.NasbNiro.Ai.AiToolCallLog) && (e.State == EntityState.Modified || e.State == EntityState.Deleted))
                    throw new InvalidOperationException("سوابق ممیزی قابل ویرایش یا حذف نیست.");
            }

            List<EntityEntry> audited = entries
                .Where(e => (e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted) && Audited.ContainsKey(e.Entity.GetType()))
                .ToList();
            if (audited.Count == 0) return;

            int userId = 0;
            string ip = null;
            HttpContext http = accessor?.HttpContext;
            if (http != null)
            {
                if (int.TryParse(http.User?.Identity?.GetUserId(), out int id)) userId = id;
                ip = http.Connection?.RemoteIpAddress?.ToString();
            }
            DateTimeOffset now = DateTimeOffset.Now;

            foreach (EntityEntry entry in audited)
            {
                Type type = entry.Entity.GetType();
                string[] only = Audited[type];

                // system-made records are too many to list one by one (the vouchers keep their own history through the events)
                if (entry.State == EntityState.Added)
                {
                    if (entry.Entity is FnVoucher v && (v.Type == FnVoucherType.Auto || v.IsImported)) continue;
                    if (entry.Entity is FnVoucherLine vl && vl.Voucher != null && (vl.Voucher.Type != FnVoucherType.Normal || vl.Voucher.IsImported)) continue;   // only the lines of manual vouchers
                    if (entry.Entity is FnDetailAccount d && d.LinkedEntityId.HasValue) continue;
                    // the punches of the devices are too many to list; manual and imported events are the ones that need a trail
                    if (entry.Entity is OfClockEvent ce && ce.Source == OfClockSource.Device) continue;
                    // operations and terminal bookings are made by the system or by the floor in bulk; their changes and supervisor edits are the trail
                    if (entry.Entity is MfOperation) continue;
                    // items (imports) and ITP activities (copied from a template) are made in bulk; only the changes of their gates are listed
                    if (entry.Entity is Entities.Model.NasbNiro.Inventory.InventoryItem || entry.Entity is Entities.Model.NasbNiro.Quality.ItpActivity) continue;
                    if (entry.Entity is MfTimeBooking tb && tb.Source == MfBookingSource.Terminal) continue;
                    // the schedule and the WBS are generated in bulk from the legacy BOM tree; a later change of a node is listed
                    if (entry.Entity is Pc.PcActivity || entry.Entity is Pc.PcWbsNode) continue;
                    // the one-off linker of legacy contracts and tenders links them in bulk; a person's manual link is listed
                    if (entry.Entity is SlCustomerLink link && link.Method == SlLinkMethod.LegacyMatch) continue;
                }

                // a terminal start or stop changes a booking all the time; only a correction (with its reason) is written
                if (entry.State == EntityState.Modified && entry.Entity is MfTimeBooking && !(entry.Properties.FirstOrDefault(x => x.Metadata.Name == "EditReason")?.IsModified ?? false)) continue;

                var before = new Dictionary<string, object>();
                var after = new Dictionary<string, object>();
                foreach (PropertyEntry p in entry.Properties)
                {
                    string name = p.Metadata.Name;
                    if (Skipped.Contains(name) || p.Metadata.ClrType == typeof(byte[])) continue;
                    if (only != null && !only.Contains(name)) continue;
                    if (entry.State == EntityState.Added)
                    {
                        after[name] = Shape(name, p.CurrentValue);
                        if (Masked.Contains(name) && p.CurrentValue is string added) after[name + "Hash"] = Hash(added);
                    }
                    else if (entry.State == EntityState.Deleted)
                    {
                        before[name] = Shape(name, p.OriginalValue);
                        if (Masked.Contains(name) && p.OriginalValue is string removed) before[name + "Hash"] = Hash(removed);
                    }
                    else
                    {
                        if (!p.IsModified || Equals(p.OriginalValue, p.CurrentValue)) continue;
                        before[name] = Shape(name, p.OriginalValue);
                        after[name] = Shape(name, p.CurrentValue);
                        // a one-way hash of the full value: a change that keeps the visible last digits is still detectable
                        if (Masked.Contains(name)) { before[name + "Hash"] = p.OriginalValue is string o ? Hash(o) : null; after[name + "Hash"] = p.CurrentValue is string c ? Hash(c) : null; }
                    }
                }
                if (entry.State == EntityState.Modified && after.Count == 0) continue;
                if (only != null && before.Count == 0 && after.Count == 0) continue;

                int? entityId = null;
                if (entry.State != EntityState.Added)
                {
                    PropertyEntry idProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
                    if (idProperty != null && idProperty.CurrentValue is int idValue) entityId = idValue;
                }

                context.Set<FnAuditLog>().Add(new FnAuditLog
                {
                    EntityType = type.Name,
                    EntityId = entityId,
                    Action = entry.State == EntityState.Added ? "Added" : entry.State == EntityState.Deleted ? "Deleted" : "Modified",
                    Summary = Describe(entry),
                    BeforeJson = before.Count == 0 ? null : JsonSerializer.Serialize(before),
                    AfterJson = after.Count == 0 ? null : JsonSerializer.Serialize(after),
                    UserId = userId,
                    At = now,
                    Ip = ip,
                });
            }
        }

        private static readonly System.Globalization.PersianCalendar Persian = new System.Globalization.PersianCalendar();

        /// <summary>The company works in the Persian calendar: nothing the user reads shows a Gregorian date (storage stays DateTime).</summary>
        internal static string PersianText(DateTime dt)
        {
            if (dt.Year < 1000 || dt.Year > 9000) return "-";
            string date = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0000}/{1:00}/{2:00}", Persian.GetYear(dt), Persian.GetMonth(dt), Persian.GetDayOfMonth(dt));
            return dt.TimeOfDay == TimeSpan.Zero ? date : date + " " + dt.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string Hash(string value)
        {
            string normalised = new string(value.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalised))).Substring(0, 16).ToLowerInvariant();
        }

        private static object Shape(string name, object value)
        {
            if (value == null) return null;
            if (Masked.Contains(name) && value is string s) return s.Length <= 4 ? "****" : "****" + s.Substring(s.Length - 4);
            if (value is Enum) return value.ToString();
            if (value is DateTime dt) return PersianText(dt);
            if (value is DateTimeOffset dto) return PersianText(dto.DateTime);
            if (value is decimal || value is int || value is long || value is bool || value is string) return value;
            return value.ToString();
        }

        private static string Describe(EntityEntry entry)
        {
            string text = null;
            foreach (string name in new[] { "Code", "Number", "Title", "Name", "Role", "Key", "TemporaryNumber" })
            {
                PropertyEntry p = entry.Properties.FirstOrDefault(x => x.Metadata.Name == name);
                if (p == null) continue;
                object value = entry.State == EntityState.Deleted ? p.OriginalValue : p.CurrentValue;
                if (value == null) continue;
                string t = value.ToString();
                if (!string.IsNullOrWhiteSpace(t)) { text = name == "TemporaryNumber" ? "موقت " + t : t; break; }
            }
            if (entry.Entity is FnEmployee) text = "کارمند (شماره حساب)";
            if (entry.Entity is Entities.Identity.User)
            {
                PropertyEntry un = entry.Properties.FirstOrDefault(x => x.Metadata.Name == "UserName");
                object name = entry.State == EntityState.Deleted ? un?.OriginalValue : un?.CurrentValue;
                text = "کاربر " + name;
            }
            if (entry.Entity is Entities.Identity.UserRole) text = "نقش کاربر";
            if (entry.Entity is Entities.Identity.UserClaim) text = "ادعای کاربر (رمز موقت یا درخواست نقش حساس)";
            if (entry.Entity is MfTimeBooking) text = "ثبت زمان کار";
            if (entry.Entity is MfOperation) text = "عملیات دستور تولید";
            if (entry.Entity is OfClockEvent) text = "تردد " + (entry.Properties.FirstOrDefault(x => x.Metadata.Name == "Source")?.CurrentValue?.ToString() == "Manual" ? "دستی" : "واردشده");
            if (entry.Entity is SlCustomerProfile) text = "پروفایل فروش مشتری (سقف اعتبار)";
            if (entry.Entity is SlBidDecision) text = "تصمیم شرکت در استعلام";
            if (entry.Entity is SlCustomerLink) text = "اتصال قرارداد یا مناقصه به مشتری";
            if (entry.Entity is FnAttendanceSheet) text = "برگه حضور ماهانه";
            if (entry.Entity is OfAttendancePolicy) text = "سیاست حضور و غیاب";
            if (entry.Entity is OfDelegation) text = "نیابت سمت";
            if (entry.Entity is OfNumberingRule) text = "قاعده شماره‌گذاری نامه";
            if (entry.Entity is OfClockImportProfile) text = "الگوی ستون‌بندی فایل اکسل ترددها";
            if (Labels.TryGetValue(entry.Entity.GetType(), out string label)) text = string.IsNullOrWhiteSpace(text) ? label : label + " " + text;
            if (text != null && text.Length > 280) text = text.Substring(0, 280);
            return text;
        }
    }
}
