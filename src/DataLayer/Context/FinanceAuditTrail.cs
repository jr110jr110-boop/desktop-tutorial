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
using Tm = Entities.Model.NasbNiro.Tasks;

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
            // release R1f: the guarded fields of an employee (daily wage, exemption flags, children, hard-and-harmful work, bank account) and the facts a payroll run depends on are audited;
            // the wage, the exemption flags, the children count and the hard-and-harmful flag are written as a "changed" marker only (never a value and never a hash) and the account number masked,
            // so the trail shows that and when they changed, never the amount or the condition. The audit service returns these record types with their changes only to the readers of payroll.
            { typeof(FnEmployee), new[] { "PersonnelCode", "BankIban", "IdentityCardNumber", "Nationality", "DailyWage", "ExemptionFlags", "ChildrenCount", "HardAndHarmful", "PendingApproval", "IsActive", "EndDate", "WorkshopId", "CostCenterId", "DefaultWorkOrderId", "UserId" } },
            { typeof(FnAsset), null },
            { typeof(FnLegalEntity), null },
            { typeof(FnIntercompanyPair), null },
            { typeof(FnApprovalThreshold), null },
            { typeof(FnAttachmentRule), null },
            { typeof(FnSetting), null },
            { typeof(FnCloseChecklist), null },
            { typeof(FnCloseOverride), null },
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
            // R1t D7: the bank reconciliation (who prepared and approved it, the difference accepted) and the explained items that let one with a difference be approved
            { typeof(FnBankReconciliation), new[] { "BankAccountId", "StatementId", "AsOfDate", "BookBalance", "BankBalance", "Difference", "ExplainedAmount", "Status", "PreparedByUserId", "ApprovedByUserId", "ItemsByUserId" } },
            { typeof(FnBankReconciliationItem), null },
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
            // release R1f: a manual event is a request; who made it, who approved or rejected it and when are part of the trail. The punches of the devices are too many to list (their trail is the push log, one row
            // per push); an imported row is audited one by one (review H1: an import is a person's decision), together with the batch that a second HR user decides, and a punch a device held for the key holder is listed
            { typeof(OfClockEvent), new[] { "EmployeeId", "Timestamp", "Direction", "Source", "Reason", "DeviceCode", "CreatedByUserId", "Approval", "ApprovedByUserId", "ApprovedAt", "RejectReason", "ImportBatchId" } },
            { typeof(OfClockImportBatch), null },
            // release R1f: devices (never the key hash; LastPushAt moves with every push and the push log is its trail), shifts, assignments, leave requests (the reason may be medical and is not copied) and the two-step attendance month close (not the snapshot body: it lists every employee's attendance)
            { typeof(OfDevice), new[] { "Code", "Title", "Location", "IsActive", "KeyRotatedAt", "KeyRotatedByUserId", "AllowedAddresses" } },
            { typeof(OfShift), null },
            { typeof(OfShiftAssignment), null },
            { typeof(OfLeaveRequest), new[] { "Number", "EmployeeId", "Kind", "FromDate", "ToDate", "FromMinute", "ToMinute", "Hours", "Direction", "Days", "Status", "SubmittedAt", "ApproverPositionId", "ApprovedByUserId", "ApprovedOnBehalf", "ApprovedAt", "RecordedByUserId", "RecordedAt", "RejectReason" } },
            { typeof(OfAttendanceMonth), new[] { "Year", "Month", "Status", "ClosedAt", "ClosedByUserId", "SheetCount", "SkippedLocked", "ConfirmedAt", "ConfirmedByUserId", "RejectReason", "SnapshotHash", "PolicyJson" } },
            // release R1f: the maker-checker requests of payroll. The proposed values are confidential (a "changed" marker and the names of the fields are written), the reasons and notes stay on the records, the decision and the people are plain.
            { typeof(FnEmployeeChange), new[] { "EmployeeId", "Kind", "Status", "WageEffectiveFrom", "Reason", "ProposedByUserId", "ProposedAt", "DecidedByUserId", "DecidedAt", "DecisionNote", "ProposedJson" } },
            { typeof(FnEmployeeWage), new[] { "EmployeeId", "EffectiveFrom", "DailyWage", "CreatedByUserId", "ApprovedByUserId", "ApprovedAt", "ChangeId", "IsBaseline" } },
            { typeof(FnAttendanceCorrection), new[] { "SheetId", "EmployeeId", "Year", "Month", "Status", "Reason", "ProposedByUserId", "ProposedAt", "DecidedByUserId", "DecidedAt", "DecisionNote", "ProposedJson" } },
            { typeof(FnLeaveOpening), null },
            { typeof(FnEmployeeLoan), new[] { "EmployeeId", "Kind", "Title", "Principal", "InstallmentAmount", "Balance", "DisbursementPaymentId", "FirstYear", "FirstMonth", "IsActive", "CreatedByUserId", "ApprovalStatus", "ApprovedByUserId", "ApprovedAt", "RejectReason" } },
            // production / shop floor (release P1): master data, routing approvals, the release gate, status changes and edits of time bookings
            { typeof(MfWorkCenter), null },
            { typeof(MfMachineInfo), null },
            { typeof(MfOperationStandard), null },
            { typeof(MfRoutingRevision), null },
            { typeof(MfReasonCode), null },
            { typeof(MfProductionOrder), new[] { "Status", "Quantity", "RoutingRevisionId", "ReleasedByUserId", "ReleaseOverride", "ReleaseOverrideReason", "KitOverrideReason", "CancelReason" } },
            { typeof(MfOperation), new[] { "Status", "SkipReason", "WorkCenterId", "QcAccepted" } },
            { typeof(MfTimeBooking), new[] { "OperationId", "EmployeeId", "MachineId", "Kind", "StartAt", "EndAt", "Minutes", "Hours", "GoodQty", "ScrapQty", "ReworkQty", "SupervisorReason", "EditReason", "Source", "IsCorrection", "CorrectsBookingId", "CorrectionApprovedByUserId", "CorrectionApprovedAt" } },   // R1p review 9: the maker and the approver of a correction booking
            { typeof(MfMaintenanceRequest), new[] { "MachineId", "Status", "Description" } },
            // release P1 part B: maintenance gates and master data, and the cost runs (one row a day; the per-booking marks are not listed)
            { typeof(MfMaintenancePlan), null },
            { typeof(MfMaintenanceOrder), new[] { "MachineId", "Kind", "Status", "PlanId", "RequestId", "Task", "DueDate", "DowntimeStart", "DowntimeEnd", "Cause", "ActionTaken", "CostCenter", "MaterialRequestId", "IssueDocumentId", "StartedByUserId", "DoneByUserId", "CancelReason" } },
            { typeof(MfMaintenanceLabour), new[] { "OrderId", "EmployeeId", "WorkDate", "Hours" } },
            { typeof(MfMaintenancePart), new[] { "OrderId", "ItemId", "Quantity" } },
            { typeof(MfCostRun), new[] { "Number", "PostingDate", "RunByUserId", "BookingCount", "MachineHours", "OverheadAmount", "AccountingEventId" } }, // the labour amount is hours x the standard rate (R1p), no one person's pay; it stays off this list only because the accounting event of the run carries it
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
            // R1p D25: who typed the scores in (the role and the user), the reason of a skip and the QA verification (who checked the answers with the customer) leave a trail too
            { typeof(Entities.Model.NasbNiro.AfterSales.SlSurvey), new[] { "Status", "Kind", "AverageScore", "RecordedByRole", "AnsweredByUserId", "SkipReason", "Verified", "VerifiedByUserId" } },
            // release R1f: where the numbers came from (HR close / manual / import), whether the row is locked, and the Input items as a "changed" marker plus the names of the items that changed (never the amounts)
            { typeof(FnAttendanceSheet), new[] { "EmployeeId", "Year", "Month", "DaysWorked", "OvertimeHours", "NightHours", "HolidayHours", "ShiftType", "PaidLeaveDays", "SickLeaveDays", "UnpaidLeaveDays", "AbsenceDays", "MissionDays", "Source", "Locked", "InputsJson" } },
            // R1a: user administration. Created, changed and deleted users, their roles and the claims that carry the temporary-password flag and the requests for sensitive roles.
            // Never listed: the password hash, the security stamp, the token serial, the confirmation codes and the birth date. The national code and the phone number are masked.
            { typeof(Entities.Identity.User), new[] { "UserName", "Email", "EmailConfirmed", "PhoneNumber", "PhoneNumberConfirmed", "Name", "LastName", "NationalCode", "IsActive", "IsDeleted", "LockoutEnabled", "LockoutEnd" } },
            { typeof(Entities.Identity.UserRole), new[] { "UserId", "RoleId" } },
            { typeof(Entities.Identity.UserClaim), new[] { "UserId", "ClaimType", "ClaimValue" } },

            // R1a (finding H7): the legacy contract, the project control records that move money, dates or authority, and the procurement headers.
            // Fields that the system recomputes all the time (float, forecast, percent cache, snapshots) and long free text are not listed; "Deleted" is listed so a soft delete leaves a row.
            { typeof(LegacyContracts.ContractEntity), null },
            // R1a2 (M3): the provisional statements (صورت‌وضعیت) carry the amounts the client is asked to accept; an edit or a delete leaves a row
            { typeof(LegacyContracts.ProvisionalStatement), new[] { "ContractId", "Number", "Date", "Price", "AcceptedPrice", "Type", "Status", "Deleted" } },
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
            { typeof(Pc.PcGuaranteeInfo), new[] { "SourceKind", "SourceId", "ContractId", "GuaranteeType", "AmountDecimal", "CurrencyCode", "IssuerKind", "ValidFrom", "ExpiryDate", "ReleaseTrigger", "Status", "ReleaseRequestedByUserId", "ReleaseRequestedAt", "ReleaseRequestedKind", "ReleaseRequestedNote", "ReleaseRequestedDate", "Deleted" } },
            { typeof(Pc.PcPenaltyAssessment), new[] { "ContractId", "MilestoneId", "WorkOrderId", "Subject", "DueDate", "ActualDate", "LateDays", "GraceDays", "RatePercentPerWeek", "CapPercent", "BaseAmount", "Amount", "Status", "DecisionReason", "DecidedByUserId", "Deleted" } },
            { typeof(Pc.PcVariationOrder), new[] { "Number", "ContractId", "ChangeRequestId", "AmendmentId", "PriceDelta", "DaysDelta", "ClientApprovalReference", "ClientApprovalDate", "Status", "Deleted" } },
            { typeof(Pc.PcChangeRequest), new[] { "Number", "ContractId", "Origin", "Type", "RequesterUserId", "Status", "RegisteredAt", "DueDate", "Escalated", "RequestedPriceDelta", "RequestedDaysDelta", "ImpactDeltaDays", "ImpactDeltaCost", "ImpactOnCriticalPath", "Decision", "DecisionNote", "DecidedByUserId", "DecidedByRole", "DecidedAt", "BaselineId", "Deleted" } },
            { typeof(Pc.PcChangeBoardMember), new[] { "ChangeRequestId", "Role", "UserId", "Vote", "VotedAt" } },
            // R1a2 (H3): the stored files (the bytes themselves are not in the trail; the replacement of the bytes under the same name is a row written by the document service with the SHA-256 before and after)
            { typeof(Entities.Model.Document), new[] { "Path", "MimType", "Name", "Description", "Code", "UploadedByUserId" } },
            { typeof(Pc.PcClaim), new[] { "Number", "ContractId", "Direction", "Issue", "Basis", "DaysQuantified", "AmountQuantified", "Status", "IsCrossClaim", "PmoApproved", "DesignManufacturingApproved", "ReviewedByUserId", "ReviewedAt", "QuantifiedByUserId", "LastEditedByUserId", "PmoApprovedByUserId", "PmoApprovedAt", "DesignApprovedByUserId", "DesignApprovedAt", "Deleted" } },
            { typeof(Pc.PcManualProgress), new[] { "ActivityId", "Percent", "State", "ProposedByUserId", "DecidedByUserId", "DecidedAt", "DecisionNote" } },
            { typeof(Pc.PcDailyReport), new[] { "ContractId", "Unit", "ReportDate", "State", "ConfirmedByUserId", "ConfirmedAt", "Deleted" } },
            { typeof(Pc.PcShipment), new[] { "Number", "ContractId", "WorkOrderId", "ReleaseNoteId", "Status", "ShippedAt", "DeliveryReceiptNumber", "DeliveredAt", "HandoverMinutesNumber", "HandoverAt", "Deleted" } },
            { typeof(Pc.PcBaseline), new[] { "ContractId", "Number", "Kind", "Status", "Reason", "ChangeRequestId", "ProjectStart", "ProjectFinish", "Bac", "ManagementReserve", "ApprovedAt", "ApprovedByUserId", "Deleted" } },
            { typeof(Pc.PcBaselineSignature), new[] { "BaselineId", "UnitRole", "UserId", "SignedAt" } },
            // the lines of the cost baseline are written as ONE summary row per baseline (R1a2, L9), see Capture; they are in this table so that the changed lines are found
            { typeof(Pc.PcCostBaselineLine), new[] { "ContractId", "BaselineId", "WbsNodeId", "ActivityId", "CostClass", "ResourceClass", "Amount", "Description" } },
            { typeof(Pc.PcWbsNode), new[] { "ContractId", "Code", "Title", "NodeType", "Weight", "BudgetAmount", "ResponsibleUnit", "ApprovedAt", "ApprovedByUserId", "Deleted" } },
            { typeof(Pc.PcActivity), new[] { "ContractId", "Code", "Title", "DurationDays", "Weight", "BudgetAtCompletion", "ResponsibleUnit", "ConstraintType", "ConstraintDate", "ActualStart", "ActualFinish", "Status", "OnHoldReason", "Deleted" } },
            // R1c: the project code parts and the planned delivery date of a work order (frozen with the terms), the requests to snapshot a past data date and the reply codes (a rejection code resets progress)
            { typeof(Pc.PcWorkOrderInfo), new[] { "WorkOrderId", "ContractId", "EquipmentProjectCode", "PlannedDeliveryDate", "Deleted" } },
            { typeof(Pc.PcSnapshotRequest), new[] { "ContractId", "DataDate", "Reason", "State", "DecidedByUserId", "DecidedAt", "DecisionNote", "SnapshotId", "Deleted" } },
            { typeof(Pc.PcClientResponseCode), null },
            { typeof(Pc.PcCostCheck), new[] { "CostEntryId", "CheckedByUserId", "CheckedAt", "Note" } },
            { typeof(Proc.ProcurementSetting), null },
            { typeof(Proc.PurchaseRequest), new[] { "Number", "Source", "Kind", "Status", "RequestDate", "WorkOrderId", "ContractId", "CostCenter", "EstimatedAmount", "SubmittedAt", "PlanningApprovedAt", "PlanningApprovedByUserId", "ManagementApprovedAt", "ManagementApprovedByUserId", "BoardResolution", "RejectedAt", "RejectedByUserId", "RejectReason", "Deleted" } },
            { typeof(Proc.PurchaseInquiry), new[] { "Number", "Status", "PurchaseRequestId", "RequiredQuotes", "FewerQuotesReason", "SelectedQuoteId", "SelectionReason", "SelectedAt", "SelectedByUserId", "FinancialApprovedAt", "FinancialApprovedByUserId", "CommercialApprovedAt", "CommercialApprovedByUserId", "TechnicalApprovedAt", "TechnicalApprovedByUserId", "ManagementApprovedAt", "ManagementApprovedByUserId", "Deleted" } },
            { typeof(Proc.PurchaseOrder), new[] { "Number", "Status", "Kind", "OrderDate", "ProviderId", "InquiryId", "WorkOrderId", "ContractId", "Currency", "ExchangeRate", "Incoterms", "PaymentTerms", "VatPercent", "AdvancePercent", "AdvanceGuarantee", "AdvanceGuaranteeExpiry", "PerformanceGuarantee", "RetentionPercent", "ContractNumber", "DeliveryDate", "LatePenaltyPercentPerWeek", "TotalAmount", "ApprovedAt", "ApprovedByUserId", "ReleasedAt", "ReleasedByUserId", "ClosedAt", "CancelledAt", "CancelReason", "Deleted" } },
            { typeof(Proc.SupplierInvoice), new[] { "Number", "Status", "ProviderId", "PurchaseOrderId", "InvoiceNumber", "InvoiceDate", "IsOfficial", "NetAmount", "VatAmount", "TotalAmount", "NonRecoverableVatAmount", "PriceVariance", "MatchNote", "ApprovedAt", "ApprovedByUserId", "CancelledAt", "CancelReason", "Deleted" } },
            { typeof(Proc.PaymentRequest), new[] { "Number", "Type", "Status", "RequestDate", "ProviderId", "PurchaseOrderId", "InvoiceId", "Amount", "Deductions", "DeductionNote", "QcApproved", "ApprovedAt", "ApprovedByUserId", "PaidAt", "PaidByUserId", "PaymentReference", "RejectedAt", "RejectReason", "Deleted" } },
            // R1c (M12): the technical judgement of a quote (who accepted it and when); the quotes of an open inquiry are rewritten when the buyer edits it
            { typeof(Proc.SupplierQuote), new[] { "InquiryId", "ProviderId", "QuoteNumber", "TotalAmount", "TechnicallyAccepted", "TechnicalScore", "TechnicalNote", "TechnicalReviewedByUserId", "TechnicalReviewedAt" } },
            // P-B7a: tasks (status, due date and links; the text of the description and comments is in the task history itself), the referral rows, the progress-approval cartable and the registers
            { typeof(Tm.TmTask), new[] { "Number", "Title", "Kind", "Priority", "Confidentiality", "DueDate", "EstimatedHours", "PercentComplete", "Status", "StatusReason", "ParentTaskId", "ContractId", "WbsNodeId", "ActivityId", "RiskId", "IssueId", "DecisionId", "CompletedByUserId", "ClosedByUserId", "Deleted" } },
            { typeof(Tm.TmTaskAssignee), new[] { "TaskId", "UserId", "PositionId", "Role", "State", "Reason", "AssignedByUserId", "ParentId", "EndedByUserId", "EndReason" } },
            { typeof(Tm.TmTaskAttachment), new[] { "TaskId", "DocumentId", "UploadedByUserId", "IsVoid", "VoidReason", "VoidedByUserId" } },
            { typeof(Pc.PcProgressChainLevel), null },
            { typeof(Pc.PcProgressSubmission), new[] { "Number", "ContractId", "Target", "ActivityId", "TaskId", "Percent", "PreviousPercent", "ActualStart", "ActualFinish", "ActualCost", "State", "Round", "CurrentLevel", "SubmittedByUserId", "ResultNote", "ManualProgressId", "CostEntryId" } },
            { typeof(Pc.PcProgressApproval), new[] { "SubmissionId", "Round", "Level", "State", "DecidedByUserId", "DecidedAt", "Note" } },
            { typeof(Pc.PcRisk), new[] { "Number", "ContractId", "Title", "Probability", "Impact", "Score", "OwnerPositionId", "Response", "Status", "ReviewDate", "ResidualScore", "ClosedByUserId", "ClosureNote", "Deleted" } },
            { typeof(Pc.PcIssue), new[] { "Number", "ContractId", "Title", "Priority", "OwnerPositionId", "DueDate", "Status", "ActivityId", "Resolution", "ResolvedByUserId", "ClosedByUserId", "Deleted" } },
            { typeof(Pc.PcStakeholder), new[] { "ContractId", "Name", "Organization", "RoleTitle", "Influence", "Interest", "EngagementCurrent", "EngagementDesired", "IsActive", "Deleted" } },
            { typeof(Pc.PcLesson), new[] { "Number", "ContractId", "Phase", "State", "DecidedByUserId", "DecisionNote", "Deleted" } },
            { typeof(Pc.PcProjectDeliverable), new[] { "ContractId", "Code", "Title", "DueDate", "Status", "DeliveredByUserId", "AcceptedByUserId", "AcceptanceNote", "Deleted" } },
            { typeof(Pc.PcProjectPhoto), new[] { "ContractId", "DocumentId", "TakenDate", "IsVoid", "VoidReason", "VoidedByUserId" } },
            { typeof(Pc.PcPartyEvaluation), new[] { "Number", "ContractId", "PartyKind", "ProviderId", "PartyName", "State", "TotalScore", "Grade", "EvaluatorUserId", "Deleted" } },
            { typeof(Pc.PcWeeklyReport), new[] { "Number", "ContractId", "WeekStart", "State", "LastEditorUserId", "ReleasedByUserId", "ReleasedAt", "Deleted" } },
            { typeof(Pc.PcPhaseEvaluation), new[] { "ContractId", "Phase", "OverallScore", "Decision", "State", "DecidedByUserId", "DecisionNote", "Deleted" } },
        };

        /// <summary>Persian label of the record types added in R1a, put in front of the number or code in the summary of the trail row.</summary>
        private static readonly Dictionary<Type, string> Labels = new Dictionary<Type, string>
        {
            { typeof(LegacyContracts.ContractEntity), "قرارداد" },
            { typeof(Tm.TmTask), "وظیفه" },
            { typeof(Tm.TmTaskAssignee), "ارجاع وظیفه" },
            { typeof(Tm.TmTaskAttachment), "پیوست وظیفه" },
            { typeof(Pc.PcProgressChainLevel), "زنجیره تایید پیشرفت" },
            { typeof(Pc.PcProgressSubmission), "گزارش پیشرفت" },
            { typeof(Pc.PcProgressApproval), "سطح تایید پیشرفت" },
            { typeof(Pc.PcRisk), "ریسک" },
            { typeof(Pc.PcIssue), "مشکل پروژه" },
            { typeof(Pc.PcStakeholder), "ذی‌نفع" },
            { typeof(Pc.PcLesson), "درس آموخته" },
            { typeof(Pc.PcProjectDeliverable), "تحویلی پروژه" },
            { typeof(Pc.PcProjectPhoto), "عکس پروژه" },
            { typeof(Pc.PcPartyEvaluation), "ارزیابی پیمانکار/مشاور" },
            { typeof(Pc.PcWeeklyReport), "گزارش هفتگی" },
            { typeof(Pc.PcPhaseEvaluation), "ارزیابی فاز" },
            { typeof(LegacyContracts.ProvisionalStatement), "صورت‌وضعیت" },
            { typeof(Entities.Model.Document), "فایل" },
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
            { typeof(Pc.PcWorkOrderInfo), "اطلاعات پروژه دستور ساخت" },
            { typeof(Pc.PcSnapshotRequest), "درخواست برش داده گذشته" },
            { typeof(Pc.PcClientResponseCode), "کد پاسخ کارفرما" },
            { typeof(Pc.PcCostCheck), "بررسی دوم هزینه دستی پروژه" },
            { typeof(Proc.ProcurementSetting), "تنظیم تدارکات" },
            { typeof(Proc.PurchaseRequest), "درخواست خرید" },
            { typeof(Proc.PurchaseInquiry), "استعلام خرید" },
            { typeof(Proc.PurchaseOrder), "سفارش خرید" },
            { typeof(Proc.SupplierInvoice), "فاکتور تأمین‌کننده" },
            { typeof(Proc.PaymentRequest), "درخواست پرداخت" },
            { typeof(FnBankReconciliation), "مغایرت‌گیری بانکی" },
            { typeof(FnBankReconciliationItem), "قلم مغایرت بانکی" },
            { typeof(FnCloseOverride), "تایید ویژه بستن دوره یا سال" },
            { typeof(Proc.SupplierQuote), "پیشنهاد قیمت تامین‌کننده" },
            { typeof(Proc.SupplierProfile), "پرونده تامین‌کننده" },
        };

        // R1c: columns added to entries above, kept apart so the entries stay as they were (merge friendly)
        static FinanceAuditTrail()
        {
            var more = new Dictionary<Type, string[]>
            {
                { typeof(Entities.Model.NasbNiro.Procurement.SupplierProfile), new[] { "Grade", "Score", "Blocked", "BlockReason", "Warnings" } },
                { typeof(Pc.PcContractTerms), new[] { "PenaltyBasis", "WarrantyMonthsAfterCommissioning", "WarrantyMonthsAfterDelivery", "TermsFrozenAt" } },
                { typeof(Pc.PcProjectPlan), new[] { "EditorUserIds" } },
                { typeof(Pc.PcPaymentMilestone), new[] { "IsFinal" } },
                { typeof(Pc.PcWbsNode), new[] { "EditorUserIds" } },
                { typeof(Pc.PcContractReview), new[] { "EditorUserIds", "Stage", "AmendmentId", "VariationOrderId", "PmoAcceptedByUserId", "PmoAcceptedAt", "CommercialAcceptedByUserId", "CommercialAcceptedAt" } },
                { typeof(Pc.PcContractAmendment), new[] { "TermsAppliedAt" } },
                { typeof(Pc.PcVariationOrder), new[] { "SubmittedByUserId", "SubmittedAt", "ApprovedByUserId", "ApprovedAt", "ApprovedByRole", "ClientLetterId", "ClientDocumentId", "TermsAppliedAt", "DecisionNote" } },
                { typeof(Pc.PcShipment), new[] { "CommissionedAt", "InventoryDocumentId", "PackedAt", "PackedByUserId", "PackingCheckedByUserId", "PackingCheckedAt" } },
                { typeof(Proc.PurchaseOrder), new[] { "QuoteId", "Revision", "RevisionReason", "RevisedAt", "RevisedByUserId" } },
                { typeof(Proc.PaymentRequest), new[] { "DeductionsProposedAt", "DeductionsConfirmedByUserId", "DeductionsConfirmedAt" } },
            };
            foreach (var pair in more)
                if (Audited.TryGetValue(pair.Key, out string[] have) && have != null)
                    Audited[pair.Key] = have.Concat(pair.Value.Where(c => !have.Contains(c))).ToArray();
        }

        private static readonly HashSet<string> Masked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BankIban", "Iban", "PayeeIban", "NationalCode", "PhoneNumber", "IdentityCardNumber" };

        /// <summary>
        /// R1p C1: a voucher line flagged as confidential labour (LabourProtected) is read by PAYROLL, FINANCEMANAGER and CEO only, but the audit trail is read by the auditor too, so the amounts of such a line
        /// are never written to it: the trail says that the line was added, changed or deleted and shows this text instead of the amounts. The total of a voucher that holds such a line is the sum of its debits and
        /// would give the amounts away (total less the visible lines), so it is replaced by the same text.
        /// </summary>
        internal const string ConfidentialText = "محرمانه (حقوق)";
        private static readonly HashSet<string> ConfidentialAmounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Debit", "Credit", "CurrencyAmount", "Quantity", "TotalAmount" };
        private static readonly HashSet<string> Skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RowVersion" };

        /// <summary>
        /// What is written instead of a value that must not appear in the trail (a wage, a loan amount, a children count, a free text): this fixed text. The trail shows THAT the field was set or changed and when,
        /// never what it held, and never a hash of it (a hash of a small number can be guessed).
        /// </summary>
        public const string ChangedMarker = "«تغییر کرد»";

        /// <summary>How a property that must not appear in the trail with its value is written.</summary>
        private enum Special
        {
            /// <summary>Only the <see cref="ChangedMarker"/> (wages, exemption flags, children, hard-and-harmful work, loan amounts and title, reasons and decision notes).</summary>
            Marker = 1,
            /// <summary>A JSON object of amounts or numbers: the marker, and "NameKeys" with the names of the members that changed; never the values (the names alone show which field a request touches).</summary>
            MarkerWithKeys = 2,
        }

        private static readonly Dictionary<(Type, string), Special> SpecialProperties = new Dictionary<(Type, string), Special>
        {
            { (typeof(FnEmployee), "DailyWage"), Special.Marker },
            { (typeof(FnEmployee), "ExemptionFlags"), Special.Marker },
            { (typeof(FnEmployee), "ChildrenCount"), Special.Marker },
            { (typeof(FnEmployee), "Nationality"), Special.Marker },
            { (typeof(FnEmployee), "HardAndHarmful"), Special.Marker },
            { (typeof(FnEmployeeWage), "DailyWage"), Special.Marker },
            { (typeof(FnEmployeeLoan), "Principal"), Special.Marker },
            { (typeof(FnEmployeeLoan), "InstallmentAmount"), Special.Marker },
            { (typeof(FnEmployeeLoan), "Balance"), Special.Marker },
            { (typeof(FnEmployeeLoan), "Title"), Special.Marker },
            { (typeof(FnEmployeeLoan), "RejectReason"), Special.Marker },
            { (typeof(FnEmployeeChange), "Reason"), Special.Marker },
            { (typeof(FnEmployeeChange), "DecisionNote"), Special.Marker },
            { (typeof(FnEmployeeChange), "ProposedJson"), Special.MarkerWithKeys },
            { (typeof(FnAttendanceCorrection), "Reason"), Special.Marker },
            { (typeof(FnAttendanceCorrection), "DecisionNote"), Special.Marker },
            { (typeof(FnAttendanceCorrection), "ProposedJson"), Special.MarkerWithKeys },
            { (typeof(FnLeaveOpening), "Note"), Special.Marker },
            { (typeof(FnLeaveOpening), "DecisionNote"), Special.Marker },
            { (typeof(FnAttendanceSheet), "InputsJson"), Special.MarkerWithKeys },
            { (typeof(OfLeaveRequest), "RejectReason"), Special.Marker },
        };

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

                // the lines of a cost baseline are listed as one row per baseline after this loop (R1a2, L9)
                if (entry.Entity is Pc.PcCostBaselineLine) continue;

                // system-made records are too many to list one by one (the vouchers keep their own history through the events)
                if (entry.State == EntityState.Added)
                {
                    if (entry.Entity is FnVoucher v && (v.Type == FnVoucherType.Auto || v.IsImported)) continue;
                    if (entry.Entity is FnVoucherLine vl && vl.Voucher != null && (vl.Voucher.Type != FnVoucherType.Normal || vl.Voucher.IsImported)) continue;   // only the lines of manual vouchers
                    if (entry.Entity is FnDetailAccount d && d.LinkedEntityId.HasValue) continue;
                    // the punches of the devices are too many to list one by one (release R1f: their trail is the push log, one row per push with the count, the device and a hash of the payload);
                    // a manual event is a request that a second person decides, an imported row is a person's decision (review H1) and a punch a device held for the key holder waits for a second person:
                    // all three need their own trail
                    if (entry.Entity is OfClockEvent ce && ce.Source == OfClockSource.Device && ce.Approval == OfClockApproval.Approved) continue;
                    // operations and terminal bookings are made by the system or by the floor in bulk; their changes and supervisor edits are the trail
                    if (entry.Entity is MfOperation) continue;
                    // items (imports) and ITP activities (copied from a template) are made in bulk; only the changes of their gates are listed
                    if (entry.Entity is Entities.Model.NasbNiro.Inventory.InventoryItem || entry.Entity is Entities.Model.NasbNiro.Quality.ItpActivity) continue;
                    if (entry.Entity is MfTimeBooking tb && tb.Source == MfBookingSource.Terminal) continue;
                    // (R1a2, L9: the inserts of the schedule and the WBS nodes are listed too; a node made by the generator is as much a decision as an edit)
                    // the one-off linker of legacy contracts and tenders links them in bulk; a person's manual link is listed
                    if (entry.Entity is SlCustomerLink link && link.Method == SlLinkMethod.LegacyMatch) continue;
                }

                // a terminal start or stop changes a booking all the time; only a correction (with its reason) is written
                if (entry.State == EntityState.Modified && entry.Entity is MfTimeBooking && !(entry.Properties.FirstOrDefault(x => x.Metadata.Name == "EditReason")?.IsModified ?? false)) continue;

                var before = new Dictionary<string, object>();
                var after = new Dictionary<string, object>();
                bool confidentialLine = IsConfidential(entry);
                foreach (PropertyEntry p in entry.Properties)
                {
                    string name = p.Metadata.Name;
                    if (Skipped.Contains(name) || p.Metadata.ClrType == typeof(byte[])) continue;
                    if (only != null && !only.Contains(name)) continue;
                    // a key (or a foreign key that points at one) the database has not generated yet is a temporary number, not a fact: it stays out of the row
                    // (an imported punch is saved together with its batch, so its ImportBatchId is such a number on a real database)
                    if (entry.State == EntityState.Added && p.IsTemporary) continue;
                    if (SpecialProperties.TryGetValue((type, name), out Special special))
                    {
                        WriteSpecial(entry, p, special, before, after);
                        continue;
                    }
                    if (entry.State == EntityState.Added)
                    {
                        after[name] = Shape(name, p.CurrentValue, confidentialLine);
                        if (Masked.Contains(name)) AddHash(after, name, p.CurrentValue);
                    }
                    else if (entry.State == EntityState.Deleted)
                    {
                        before[name] = Shape(name, p.OriginalValue, confidentialLine);
                        if (Masked.Contains(name)) AddHash(before, name, p.OriginalValue);
                    }
                    else
                    {
                        if (!p.IsModified || Equals(p.OriginalValue, p.CurrentValue)) continue;
                        before[name] = Shape(name, p.OriginalValue, confidentialLine);
                        after[name] = Shape(name, p.CurrentValue, confidentialLine);
                        // a keyed hash of the full value (R1a2, M4): a change that keeps the visible last digits is still detectable; no key, no hash
                        if (Masked.Contains(name)) { AddHash(before, name, p.OriginalValue); AddHash(after, name, p.CurrentValue); }
                    }
                }
                if (entry.State == EntityState.Modified && after.Count == 0) continue;
                if (only != null && before.Count == 0 && after.Count == 0) continue;

                int? entityId = null;
                if (entry.State != EntityState.Added)
                {
                    PropertyEntry idProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
                    if (idProperty != null && idProperty.CurrentValue is int idValue) entityId = idValue;
                    else if (idProperty != null && idProperty.CurrentValue is long longId && longId > 0 && longId <= int.MaxValue) entityId = (int)longId;   // the clock events have a long key
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

            // R1a2 (L9): the lines of a cost baseline are written in bulk (hundreds at once). One summary row per baseline says how many lines were added, changed and removed
            // and what their amounts were; the full lines stay in the baseline itself.
            foreach (var group in audited.Where(e => e.Entity is Pc.PcCostBaselineLine)
                         .GroupBy(e => new { ((Pc.PcCostBaselineLine)e.Entity).ContractId, ((Pc.PcCostBaselineLine)e.Entity).BaselineId }))
            {
                int added = group.Count(e => e.State == EntityState.Added);
                int changed = group.Count(e => e.State == EntityState.Modified);
                int removed = group.Count(e => e.State == EntityState.Deleted);
                decimal amountBefore = group.Where(e => e.State != EntityState.Added).Sum(e => (decimal)e.Property("Amount").OriginalValue);
                decimal amountAfter = group.Where(e => e.State != EntityState.Deleted).Sum(e => (decimal)e.Property("Amount").CurrentValue);
                string text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "سطرهای خط‌مبنای هزینه: {0} سطر افزوده، {1} سطر تغییر‌یافته، {2} سطر حذف‌شده؛ جمع مبلغ سطرها از {3:N0} به {4:N0}", added, changed, removed, amountBefore, amountAfter);
                context.Set<FnAuditLog>().Add(new FnAuditLog
                {
                    EntityType = typeof(Pc.PcCostBaselineLine).Name,
                    EntityId = group.Key.BaselineId,
                    Action = changed == 0 && removed == 0 ? "Added" : changed == 0 && added == 0 ? "Deleted" : "Modified",
                    Summary = text,
                    BeforeJson = changed + removed == 0 ? null : JsonSerializer.Serialize(new { contractId = group.Key.ContractId, lines = changed + removed, amount = amountBefore }),
                    AfterJson = added + changed == 0 ? null : JsonSerializer.Serialize(new { contractId = group.Key.ContractId, added, changed, removed, lines = added + changed, amount = amountAfter }),
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

        /// <summary>R1a2 (M4): writes "<c>name</c>Hash" (HMAC-SHA256 with the server key) when a key is set; otherwise nothing is written.</summary>
        private static void AddHash(Dictionary<string, object> target, string name, object value)
        {
            if (!(value is string text)) return;
            string hash = AuditHmac.Compute(text);
            if (hash != null) target[name + "Hash"] = hash;
        }

        /// <summary>
        /// True for a voucher line that is flagged as confidential labour now or was before the change, and for a voucher that holds such a line (its lines are in memory when it is made or reversed, or are tracked
        /// by the context when it is changed).
        /// </summary>
        private static bool IsConfidential(EntityEntry entry)
        {
            if (entry.Entity is FnVoucher voucher)
            {
                if (voucher.Lines != null && voucher.Lines.Any(l => l.LabourProtected)) return true;
                return voucher.Id != 0 && entry.Context.ChangeTracker.Entries<FnVoucherLine>().Any(e => e.Entity.VoucherId == voucher.Id && e.Entity.LabourProtected);
            }
            if (!(entry.Entity is FnVoucherLine line)) return false;
            if (line.LabourProtected) return true;
            return entry.State != EntityState.Added && entry.Property(nameof(FnVoucherLine.LabourProtected)).OriginalValue is true;
        }


        private static bool HasContent(object value) => value != null && !(value is string text && string.IsNullOrWhiteSpace(text));

        /// <summary>
        /// Writes a property that must not appear with its value. Marker: the <see cref="ChangedMarker"/> only. MarkerWithKeys: the marker and "NameKeys" (the names of the members that differ from the old value,
        /// or from the baseline column of a new row). A modified property whose content did not really change (same members, same values) is left out.
        /// </summary>
        private static void WriteSpecial(EntityEntry entry, PropertyEntry p, Special special, Dictionary<string, object> before, Dictionary<string, object> after)
        {
            string name = p.Metadata.Name;
            if (special == Special.Marker)
            {
                if (entry.State == EntityState.Added) { if (HasContent(p.CurrentValue)) after[name] = ChangedMarker; }
                else if (entry.State == EntityState.Deleted) { if (HasContent(p.OriginalValue)) before[name] = ChangedMarker; }
                else if (p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
                {
                    before[name] = ChangedMarker;
                    after[name] = ChangedMarker;
                }
                return;
            }

            if (entry.State == EntityState.Deleted) { if (HasContent(p.OriginalValue)) before[name] = ChangedMarker; return; }
            string newJson = p.CurrentValue as string;
            if (entry.State == EntityState.Added)
            {
                if (newJson == null) return;
                after[name] = ChangedMarker;
                after[name + "Keys"] = ChangedKeys(BaselineOf(entry, name), newJson);
                return;
            }
            if (!p.IsModified || Equals(p.OriginalValue, p.CurrentValue)) return;
            string oldJson = p.OriginalValue as string;
            string keys = ChangedKeys(oldJson, newJson);
            if (keys.Length == 0) return;   // the text changed but no member did (formatting only)
            before[name] = ChangedMarker;
            after[name] = ChangedMarker;
            after[name + "Keys"] = keys;
        }

        /// <summary>A correction request keeps the numbers the sheet held when it was made next to the proposed ones: the keys of a new request are the members that differ between the two.</summary>
        private static string BaselineOf(EntityEntry entry, string name) =>
            entry.Entity is FnAttendanceCorrection && name == "ProposedJson" ? entry.Properties.FirstOrDefault(x => x.Metadata.Name == "CurrentJson")?.CurrentValue as string : null;

        private static string NumberText(decimal value) => value.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);

        private static Dictionary<string, string> JsonMembers(string json, out bool valid)
        {
            var members = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            valid = true;
            if (string.IsNullOrWhiteSpace(json)) return members;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) { valid = false; return members; }
                foreach (JsonProperty m in doc.RootElement.EnumerateObject())
                    members[m.Name] = m.Value.ValueKind == JsonValueKind.Number && m.Value.TryGetDecimal(out decimal n) ? NumberText(n) : m.Value.GetRawText();
            }
            catch (JsonException) { valid = false; }
            return members;
        }

        /// <summary>The names (sorted, comma separated, never the values) of the members that exist on one side only or differ between two JSON objects. Text that is not a JSON object counts as changed ("?").</summary>
        private static string ChangedKeys(string oldJson, string newJson)
        {
            Dictionary<string, string> a = JsonMembers(oldJson, out bool okOld), b = JsonMembers(newJson, out bool okNew);
            if (!okOld || !okNew) return "?";
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> m in b) if (!a.TryGetValue(m.Key, out string old) || old != m.Value) keys.Add(m.Key);
            foreach (KeyValuePair<string, string> m in a) if (!b.ContainsKey(m.Key)) keys.Add(m.Key);
            string text = string.Join(",", keys);
            return text.Length <= 400 ? text : text.Substring(0, 400);
        }

        private static object Shape(string name, object value, bool confidentialLine = false)
        {
            if (value == null) return null;
            if (confidentialLine && ConfidentialAmounts.Contains(name)) return ConfidentialText;
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
            if (entry.Entity is FnEmployee) text = "کارمند (مشخصات حساس)";
            if (entry.Entity is Entities.Identity.User)
            {
                PropertyEntry un = entry.Properties.FirstOrDefault(x => x.Metadata.Name == "UserName");
                object name = entry.State == EntityState.Deleted ? un?.OriginalValue : un?.CurrentValue;
                text = "کاربر " + name;
            }
            if (entry.Entity is Entities.Identity.UserRole) text = "نقش کاربر";
            if (entry.Entity is Entities.Identity.UserClaim) text = "ادعای کاربر (رمز موقت یا درخواست نقش حساس)";
            if (entry.Entity is OfDevice) text = "دستگاه حضور و غیاب " + text;
            if (entry.Entity is OfShift) text = "شیفت کاری " + text;
            if (entry.Entity is OfShiftAssignment) text = "تخصیص شیفت به کارمند";
            if (entry.Entity is OfLeaveRequest) text = "درخواست مرخصی " + text;
            if (entry.Entity is OfAttendanceMonth) text = "بستن ماه حضور و غیاب " + (entry.Properties.FirstOrDefault(x => x.Metadata.Name == "Year")?.CurrentValue) + "/" + ((entry.Properties.FirstOrDefault(x => x.Metadata.Name == "Month")?.CurrentValue as int?)?.ToString("00") ?? "");
            if (entry.Entity is FnEmployeeChange) text = "درخواست ایجاد یا تغییر مشخصات حساس کارمند";
            if (entry.Entity is FnEmployeeWage) text = "سابقه مزد روزانه کارمند";
            if (entry.Entity is FnAttendanceCorrection) text = "درخواست اصلاح برگه حضور قفل‌شده";
            if (entry.Entity is FnLeaveOpening) text = "مانده اول دوره مرخصی";
            if (entry.Entity is FnEmployeeLoan) text = "وام یا مساعده کارمند";
            if (entry.Entity is MfTimeBooking) text = "ثبت زمان کار";
            if (entry.Entity is MfOperation) text = "عملیات دستور تولید";
            if (entry.Entity is OfClockEvent)
            {
                string source = entry.Properties.FirstOrDefault(x => x.Metadata.Name == "Source")?.CurrentValue?.ToString();
                text = source == "Manual" ? "تردد دستی" : source == "Import" ? "تردد واردشده از فایل" : "تردد دستگاه (نگه‌داشته برای تایید نفر دوم)";
            }
            if (entry.Entity is OfClockImportBatch) text = "بسته ورود ترددها";
            if (entry.Entity is SlCustomerProfile) text = "پروفایل فروش مشتری (سقف اعتبار)";
            if (entry.Entity is SlBidDecision) text = "تصمیم شرکت در استعلام";
            if (entry.Entity is SlCustomerLink) text = "اتصال قرارداد یا مناقصه به مشتری";
            if (entry.Entity is FnAttendanceSheet) text = "برگه حضور ماهانه";
            if (entry.Entity is OfAttendancePolicy) text = "سیاست حضور و غیاب";
            if (entry.Entity is OfDelegation) text = "نیابت سمت";
            if (entry.Entity is Entities.Model.Document)
            {
                // the key of a file is a Guid, which the integer column of the trail cannot hold; it is part of the text instead
                object documentId = entry.Properties.FirstOrDefault(x => x.Metadata.Name == "Id")?.CurrentValue;
                text = (string.IsNullOrWhiteSpace(text) ? null : text + " ") + (documentId == null ? null : "(" + documentId + ")");
            }
            if (entry.Entity is OfNumberingRule) text = "قاعده شماره‌گذاری نامه";
            if (entry.Entity is OfClockImportProfile) text = "الگوی ستون‌بندی فایل اکسل ترددها";
            if (Labels.TryGetValue(entry.Entity.GetType(), out string label)) text = string.IsNullOrWhiteSpace(text) ? label : label + " " + text;
            if (text != null && text.Length > 280) text = text.Substring(0, 280);
            return text;
        }
    }
}
