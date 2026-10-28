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
            // the wage and the exemption flags are written as one-way hashes only and the account number masked, so the trail shows that and when they changed, never the amount or the condition
            { typeof(FnEmployee), new[] { "PersonnelCode", "BankIban", "DailyWage", "ExemptionFlags", "ChildrenCount", "HardAndHarmful", "PendingApproval", "IsActive", "EndDate", "WorkshopId", "CostCenterId", "DefaultWorkOrderId", "UserId" } },
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
            // office automation, review 1 (M7): who may act for a position, who sits in it, the numbering rules, the attendance policy, manual and imported clock events and the payroll attendance input
            { typeof(OfDelegation), null },
            { typeof(OfPosition), new[] { "Title", "UnitId", "UserId", "EmployeeId", "IsManager", "CanSign", "IsActive" } },
            { typeof(OfNumberingRule), null },
            { typeof(OfAttendancePolicy), null },
            { typeof(OfClockImportProfile), null },
            // release R1f: a manual event is a request; who made it, who approved or rejected it and when are part of the trail (device and import events are logged per batch in OfDevicePushLog, not per punch)
            { typeof(OfClockEvent), new[] { "EmployeeId", "Timestamp", "Direction", "Source", "Reason", "DeviceCode", "CreatedByUserId", "Approval", "ApprovedByUserId", "ApprovedAt", "RejectReason" } },
            // release R1f: devices (never the key hash; LastPushAt moves with every push and the push log is its trail), shifts, assignments, leave requests (the reason may be medical and is not copied) and the two-step attendance month close (not the snapshot body: it lists every employee's attendance)
            { typeof(OfDevice), new[] { "Code", "Title", "Location", "IsActive", "KeyRotatedAt", "KeyRotatedByUserId" } },
            { typeof(OfShift), null },
            { typeof(OfShiftAssignment), null },
            { typeof(OfLeaveRequest), new[] { "Number", "EmployeeId", "Kind", "FromDate", "ToDate", "FromMinute", "ToMinute", "Hours", "Direction", "Days", "Status", "SubmittedAt", "ApproverPositionId", "ApprovedByUserId", "ApprovedOnBehalf", "ApprovedAt", "RecordedByUserId", "RecordedAt", "RejectReason" } },
            { typeof(OfAttendanceMonth), new[] { "Year", "Month", "Status", "ClosedAt", "ClosedByUserId", "SheetCount", "SkippedLocked", "ConfirmedAt", "ConfirmedByUserId", "RejectReason", "SnapshotHash" } },
            // release R1f: the maker-checker requests of payroll. The proposed values are confidential (a hash and the names of the fields are written), the decision and the people are plain.
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
            // release R1f: where the numbers came from (HR close / manual / import), whether the row is locked, and the Input items as a hash plus the names of the items that changed (never the amounts)
            { typeof(FnAttendanceSheet), new[] { "EmployeeId", "Year", "Month", "DaysWorked", "OvertimeHours", "NightHours", "HolidayHours", "ShiftType", "PaidLeaveDays", "SickLeaveDays", "UnpaidLeaveDays", "AbsenceDays", "MissionDays", "Source", "Locked", "InputsJson" } },
        };

        private static readonly HashSet<string> Masked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BankIban", "Iban", "PayeeIban" };
        private static readonly HashSet<string> Skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RowVersion" };

        /// <summary>How a property that must not appear in the trail with its value is written.</summary>
        private enum Special
        {
            /// <summary>Only a one-way hash of the value ("NameHash"): a change is visible, the value is not (wages, exemption flags, loan amounts).</summary>
            HashOnly = 1,
            /// <summary>A JSON object of amounts or numbers: a hash of the whole text ("NameHash") and the names of the members that changed ("NameKeys"); never the values.</summary>
            JsonDigest = 2,
        }

        private static readonly Dictionary<(Type, string), Special> SpecialProperties = new Dictionary<(Type, string), Special>
        {
            { (typeof(FnEmployee), "DailyWage"), Special.HashOnly },
            { (typeof(FnEmployee), "ExemptionFlags"), Special.HashOnly },
            { (typeof(FnEmployeeWage), "DailyWage"), Special.HashOnly },
            { (typeof(FnEmployeeLoan), "Principal"), Special.HashOnly },
            { (typeof(FnEmployeeLoan), "InstallmentAmount"), Special.HashOnly },
            { (typeof(FnEmployeeLoan), "Balance"), Special.HashOnly },
            { (typeof(FnAttendanceSheet), "InputsJson"), Special.JsonDigest },
            { (typeof(FnEmployeeChange), "ProposedJson"), Special.JsonDigest },
            { (typeof(FnAttendanceCorrection), "ProposedJson"), Special.JsonDigest },
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

                // system-made records are too many to list one by one (the vouchers keep their own history through the events)
                if (entry.State == EntityState.Added)
                {
                    if (entry.Entity is FnVoucher v && (v.Type == FnVoucherType.Auto || v.IsImported)) continue;
                    if (entry.Entity is FnVoucherLine vl && vl.Voucher != null && (vl.Voucher.Type != FnVoucherType.Normal || vl.Voucher.IsImported)) continue;   // only the lines of manual vouchers
                    if (entry.Entity is FnDetailAccount d && d.LinkedEntityId.HasValue) continue;
                    // the punches of the devices and of imported files are too many to list one by one (release R1f: their trail is the push log, one row per push or import with the count, the device and a hash of the payload);
                    // a manual event is a request that a second person decides and needs its own trail
                    if (entry.Entity is OfClockEvent ce && ce.Source != OfClockSource.Manual) continue;
                    // operations and terminal bookings are made by the system or by the floor in bulk; their changes and supervisor edits are the trail
                    if (entry.Entity is MfOperation) continue;
                    if (entry.Entity is MfTimeBooking tb && tb.Source == MfBookingSource.Terminal) continue;
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
                    if (SpecialProperties.TryGetValue((type, name), out Special special))
                    {
                        WriteSpecial(entry, p, special, before, after);
                        continue;
                    }
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

        /// <summary>
        /// Writes a property that must not appear with its value. HashOnly: "NameHash" only. JsonDigest: "NameHash" and "NameKeys" (the names of the members that differ from the old value,
        /// or from the baseline column of a new row). A modified property whose content did not really change (same members, same values) is left out.
        /// </summary>
        private static void WriteSpecial(EntityEntry entry, PropertyEntry p, Special special, Dictionary<string, object> before, Dictionary<string, object> after)
        {
            string name = p.Metadata.Name;
            if (special == Special.HashOnly)
            {
                if (entry.State == EntityState.Added) after[name + "Hash"] = HashValue(p.CurrentValue);
                else if (entry.State == EntityState.Deleted) before[name + "Hash"] = HashValue(p.OriginalValue);
                else if (p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
                {
                    before[name + "Hash"] = HashValue(p.OriginalValue);
                    after[name + "Hash"] = HashValue(p.CurrentValue);
                }
                return;
            }

            if (entry.State == EntityState.Deleted) { if (p.OriginalValue is string removed) before[name + "Hash"] = HashValue(removed); return; }
            string newJson = p.CurrentValue as string;
            if (entry.State == EntityState.Added)
            {
                if (newJson == null) return;
                after[name + "Hash"] = HashValue(newJson);
                after[name + "Keys"] = ChangedKeys(BaselineOf(entry, name), newJson);
                return;
            }
            if (!p.IsModified || Equals(p.OriginalValue, p.CurrentValue)) return;
            string oldJson = p.OriginalValue as string;
            string keys = ChangedKeys(oldJson, newJson);
            if (keys.Length == 0) return;   // the text changed but no member did (formatting only)
            before[name + "Hash"] = HashValue(oldJson);
            after[name + "Hash"] = HashValue(newJson);
            after[name + "Keys"] = keys;
        }

        /// <summary>A correction request keeps the numbers the sheet held when it was made next to the proposed ones: the keys of a new request are the members that differ between the two.</summary>
        private static string BaselineOf(EntityEntry entry, string name) =>
            entry.Entity is FnAttendanceCorrection && name == "ProposedJson" ? entry.Properties.FirstOrDefault(x => x.Metadata.Name == "CurrentJson")?.CurrentValue as string : null;

        private static string NumberText(decimal value) => value.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A one-way hash of a number, flag or text (numbers in a canonical form so that 100 and 100.00 are the same value).</summary>
        private static string HashValue(object value)
        {
            if (value == null) return null;
            string text = value switch
            {
                decimal d => NumberText(d),
                bool b => b ? "1" : "0",
                string s => s,
                _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
            };
            return Hash(text ?? "");
        }

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
            if (entry.Entity is FnEmployee) text = "کارمند (مشخصات حساس)";
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
            if (entry.Entity is OfClockEvent) text = "تردد " + (entry.Properties.FirstOrDefault(x => x.Metadata.Name == "Source")?.CurrentValue?.ToString() == "Manual" ? "دستی" : "واردشده");
            if (entry.Entity is SlCustomerProfile) text = "پروفایل فروش مشتری (سقف اعتبار)";
            if (entry.Entity is SlBidDecision) text = "تصمیم شرکت در استعلام";
            if (entry.Entity is SlCustomerLink) text = "اتصال قرارداد یا مناقصه به مشتری";
            if (entry.Entity is FnAttendanceSheet) text = "برگه حضور ماهانه";
            if (entry.Entity is OfAttendancePolicy) text = "سیاست حضور و غیاب";
            if (entry.Entity is OfDelegation) text = "نیابت سمت";
            if (entry.Entity is OfNumberingRule) text = "قاعده شماره‌گذاری نامه";
            if (entry.Entity is OfClockImportProfile) text = "الگوی ستون‌بندی فایل اکسل ترددها";
            if (text != null && text.Length > 280) text = text.Substring(0, 280);
            return text;
        }
    }
}
