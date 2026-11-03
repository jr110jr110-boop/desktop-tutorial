using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LegacyContracts = Entities.Model.NasbNiro.Contracts;
using Pc = Entities.Model.NasbNiro.ProjectControl;
using Proc = Entities.Model.NasbNiro.Procurement;

namespace MsTests
{
    /// <summary>
    /// R1a (finding H7): the legacy contract, the project control records that move money, dates or authority, and the procurement headers
    /// write to the append-only audit trail with the user, the time and the changed fields. The list of audited records is checked against the
    /// EF model, so a renamed property can no longer drop out of the trail without a red test.
    /// </summary>
    [TestClass]
    public class AuditTrailTests
    {
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(7, "pmo.user", "PMO");
        private static readonly Lazy<AppHost> Shared = new Lazy<AppHost>(() => NewHost());

        private static AppHost NewHost() => new AppHost("AuditTrail_" + Guid.NewGuid().ToString("N"));

        private static Dictionary<Type, string[]> AuditedTypes()
        {
            Type trail = typeof(ApplicationDbContext).Assembly.GetType("DataLayer.Context.FinanceAuditTrail", throwOnError: true);
            FieldInfo field = trail.GetField("Audited", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "FinanceAuditTrail.Audited was renamed: update this test");
            return (Dictionary<Type, string[]>)field.GetValue(null);
        }

        private static Dictionary<Type, string> LabelTypes()
        {
            Type trail = typeof(ApplicationDbContext).Assembly.GetType("DataLayer.Context.FinanceAuditTrail", throwOnError: true);
            FieldInfo field = trail.GetField("Labels", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "FinanceAuditTrail.Labels was renamed: update this test");
            return (Dictionary<Type, string>)field.GetValue(null);
        }

        private static List<FnAuditLog> Trail(AppHost host, string entityType) =>
            host.Read(uow => uow.Set<FnAuditLog>().Where(l => l.EntityType == entityType).OrderBy(l => l.Id).ToList());

        private static Dictionary<string, JsonElement> Fields(string json)
        {
            Assert.IsNotNull(json, "the row has no values");
            using (JsonDocument doc = JsonDocument.Parse(json))
                return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        }

        // ------------------------------------------------------------------------------------------------ the lists

        [TestMethod]
        public void EveryFilterNamesAScalarPropertyOfItsEntity()
        {
            IModel model = Shared.Value.Model;
            var problems = new List<string>();
            foreach (KeyValuePair<Type, string[]> item in AuditedTypes())
            {
                IEntityType type = model.FindEntityType(item.Key);
                if (type == null) { problems.Add(item.Key.Name + ": not an entity type of the model, the entry never matches"); continue; }
                if (item.Value == null) continue;
                var scalar = new HashSet<string>(type.GetProperties().Select(p => p.Name), StringComparer.Ordinal);
                problems.AddRange(item.Value.Where(n => !scalar.Contains(n)).Select(n => item.Key.Name + "." + n + ": no such property in the model (a renamed property drops out of the trail)"));
                problems.AddRange(item.Value.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => item.Key.Name + "." + g.Key + ": listed twice"));
                if (item.Value.Length == 0) problems.Add(item.Key.Name + ": an empty filter records nothing (use null for all properties)");
            }
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
        }

        [TestMethod]
        public void AuditedMoneyDatesAndAuthorityRecordsOfTheReviewAreListed()
        {
            Type[] required =
            {
                typeof(LegacyContracts.ContractEntity), typeof(LegacyContracts.ProvisionalStatement),
                typeof(Pc.PcContractTerms), typeof(Pc.PcPaymentMilestone), typeof(Pc.PcGuaranteeInfo), typeof(Pc.PcPenaltyAssessment),
                typeof(Pc.PcVariationOrder), typeof(Pc.PcContractAmendment), typeof(Pc.PcChangeRequest), typeof(Pc.PcClaim), typeof(Pc.PcBaseline),
                typeof(Pc.PcBaselineSignature), typeof(Pc.PcCostBaselineLine), typeof(Pc.PcProjectCharter), typeof(Pc.PcProjectPlan), typeof(Pc.PcContractReview),
                typeof(Pc.PcSetting), typeof(Pc.PcApprovalAuthority),
                typeof(Proc.PurchaseRequest), typeof(Proc.PurchaseInquiry), typeof(Proc.PurchaseOrder), typeof(Proc.SupplierInvoice), typeof(Proc.PaymentRequest), typeof(Proc.ProcurementSetting),
            };
            Dictionary<Type, string[]> audited = AuditedTypes();
            string[] missing = required.Where(t => !audited.ContainsKey(t)).Select(t => t.Name).ToArray();
            Assert.AreEqual(0, missing.Length, "no longer audited: " + string.Join(", ", missing));

            // the record that carries the status of a guarantee, the amount of a payment and the approval of a purchase order must list those fields
            Assert.IsTrue(audited[typeof(Pc.PcGuaranteeInfo)].Contains("Status"));
            Assert.IsTrue(audited[typeof(Pc.PcGuaranteeInfo)].Contains("ExpiryDate"));
            Assert.IsTrue(audited[typeof(Pc.PcPaymentMilestone)].Contains("Amount"));
            Assert.IsTrue(audited[typeof(Pc.PcContractTerms)].Contains("ValueExVat"));
            Assert.IsTrue(audited[typeof(Pc.PcClaim)].Contains("ReviewedByUserId"));
            Assert.IsTrue(audited[typeof(Proc.PurchaseOrder)].Contains("ApprovedByUserId"));
            Assert.IsTrue(audited[typeof(Proc.PaymentRequest)].Contains("PaidByUserId"));
            Assert.IsNull(audited[typeof(LegacyContracts.ContractEntity)], "the legacy contract is audited field by field");
            // R1a2 (M3): the amounts of a provisional statement and its delete flag
            foreach (string field in new[] { "Price", "AcceptedPrice", "Status", "Deleted" }) Assert.IsTrue(audited[typeof(LegacyContracts.ProvisionalStatement)].Contains(field), field);
        }

        [TestMethod]
        public void EveryLabelBelongsToAnAuditedTypeAndIsPersian()
        {
            Dictionary<Type, string[]> audited = AuditedTypes();
            Dictionary<Type, string> labels = LabelTypes();
            string[] orphan = labels.Keys.Where(t => !audited.ContainsKey(t)).Select(t => t.Name).ToArray();
            Assert.AreEqual(0, orphan.Length, "labels of types that are not audited: " + string.Join(", ", orphan));
            string[] notPersian = labels.Where(kv => string.IsNullOrWhiteSpace(kv.Value) || !kv.Value.Any(ch => ch >= '؀' && ch <= 'ۿ')).Select(kv => kv.Key.Name).ToArray();
            Assert.AreEqual(0, notPersian.Length, "labels without Persian text: " + string.Join(", ", notPersian));
            // every record type added in R1a has a label, so the row says what it is in the user's language
            string[] unlabelled = audited.Keys
                .Where(t => t == typeof(LegacyContracts.ContractEntity) || t.Namespace == typeof(Pc.PcActivity).Namespace || t.Namespace == typeof(Proc.PurchaseOrder).Namespace)
                .Where(t => !labels.ContainsKey(t) && t != typeof(Proc.SupplierProfile))
                .Select(t => t.Name).ToArray();
            Assert.AreEqual(0, unlabelled.Length, "audited project control or procurement types without a Persian label: " + string.Join(", ", unlabelled));
        }

        /// <summary>
        /// Project control records that are deliberately not in the trail, with the reason. A new Pc* entity must be audited or be added here
        /// with a reason, so the decision is made when the table is created and not found out in a dispute.
        /// </summary>
        private static readonly Dictionary<string, string> NotAudited = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "PcAmendmentCopy", "who received a copy of an amendment: a distribution note, no money, date or authority" },
            { "PcBaselineActivity", "frozen copy of the activities of an approved baseline; the baseline header and its signatures are audited" },
            { "PcBaselineCostPeriod", "frozen copy of the planned cost curve of a baseline; the baseline and its cost lines are audited" },
            { "PcClientResponseCode", "lookup table of the reply codes of the client" },
            { "PcCodingTable", "lookup table of the project coding (type, option, size)" },
            { "PcDailyReportLine", "lines of a daily report; the confirmation of the report is audited" },
            { "PcDependency", "links between activities, generated with the schedule; the dates and the status of the activities are audited" },
            { "PcEngineeringDeliverable", "the document register changes with every revision; each revision is a controlled document with its own history" },
            { "PcGuaranteeEvent", "append-only history of a guarantee that carries its own user and time" },
            { "PcMdrTemplate", "template of the document list; copied into a contract, not an authority" },
            { "PcMdrTemplateLine", "line of a document-list template" },
            { "PcProgressSnapshot", "weekly snapshot; immutable and written by the server job" },
            { "PcProgressSnapshotLine", "line of a weekly snapshot; immutable" },
            { "PcReportIssue", "an issued report: its content and hash never change, the hash is the proof" },
            { "PcTransmittal", "transmittals to and from the client carry their own number, date and reply codes" },
            { "PcTransmittalLine", "line of a transmittal; the reply code and date are on the line itself" },
            { "PcWorkOrderInfo", "parts of the project code of a work order, allocated once; the project code of the contract terms is audited" },
        };

        [TestMethod]
        public void EveryProjectControlRecordIsAuditedOrOnTheNotAuditedList()
        {
            IModel model = Shared.Value.Model;
            Dictionary<Type, string[]> audited = AuditedTypes();
            string ns = typeof(Pc.PcActivity).Namespace;
            Type[] pcTypes = model.GetEntityTypes().Select(t => t.ClrType).Where(t => t.Namespace == ns && t.Name.StartsWith("Pc", StringComparison.Ordinal)).ToArray();
            Assert.IsTrue(pcTypes.Length > 30, "the project control entities were not found in the model");

            string[] undecided = pcTypes.Where(t => !audited.ContainsKey(t) && !NotAudited.ContainsKey(t.Name)).Select(t => t.Name).OrderBy(x => x).ToArray();
            Assert.AreEqual(0, undecided.Length, "project control records that are neither audited nor on the not-audited list (decide and add a reason): " + string.Join(", ", undecided));

            string[] stale = NotAudited.Keys.Where(k => !pcTypes.Any(t => t.Name == k)).ToArray();
            Assert.AreEqual(0, stale.Length, "not-audited entries that match no entity: " + string.Join(", ", stale));
            string[] contradictory = pcTypes.Where(t => audited.ContainsKey(t) && NotAudited.ContainsKey(t.Name)).Select(t => t.Name).ToArray();
            Assert.AreEqual(0, contradictory.Length, "audited records that are also on the not-audited list: " + string.Join(", ", contradictory));
            string[] noReason = NotAudited.Where(kv => string.IsNullOrWhiteSpace(kv.Value) || kv.Value.Length < 12).Select(kv => kv.Key).ToArray();
            Assert.AreEqual(0, noReason.Length, "not-audited entries without a reason: " + string.Join(", ", noReason));
        }

        // ------------------------------------------------------------------------------------------------ the rows

        [TestMethod]
        public void ALegacyContractChangeIsInTheTrailWithTheUserAndAPersianLabel()
        {
            AppHost host = NewHost();
            int id = 0;
            host.Write(uow => { var c = AppHost.Add(uow, new LegacyContracts.ContractEntity { Code = "C-100", Title = "old title", Price = 1000m }); uow.SaveChanges(); id = c.Id; }, Pmo);
            host.Write(uow => { var c = uow.Set<LegacyContracts.ContractEntity>().Single(x => x.Id == id); c.Title = "new title"; c.Price = 2500m; uow.SaveChanges(); }, Pmo);
            host.Write(uow => { var c = uow.Set<LegacyContracts.ContractEntity>().Single(x => x.Id == id); c.Delete(); uow.SaveChanges(); }, Pmo);

            List<FnAuditLog> rows = Trail(host, nameof(LegacyContracts.ContractEntity));
            Assert.AreEqual(3, rows.Count, "added, modified, soft-deleted");

            Assert.AreEqual("Added", rows[0].Action);
            Assert.AreEqual(7, rows[0].UserId);
            Assert.AreEqual("10.9.8.7", rows[0].Ip);
            Assert.AreEqual("قرارداد C-100", rows[0].Summary);
            Assert.AreEqual("C-100", Fields(rows[0].AfterJson)["Code"].GetString());

            Assert.AreEqual("Modified", rows[1].Action);
            Assert.AreEqual(id, rows[1].EntityId);
            Assert.AreEqual(7, rows[1].UserId);
            Dictionary<string, JsonElement> before = Fields(rows[1].BeforeJson), after = Fields(rows[1].AfterJson);
            Assert.AreEqual("old title", before["Title"].GetString());
            Assert.AreEqual("new title", after["Title"].GetString());
            Assert.AreEqual(1000m, before["Price"].GetDecimal());
            Assert.AreEqual(2500m, after["Price"].GetDecimal());
            Assert.IsFalse(after.ContainsKey("Code"), "an unchanged field is not written");

            Assert.AreEqual("Modified", rows[2].Action);
            Assert.IsFalse(Fields(rows[2].BeforeJson)["Deleted"].GetBoolean());
            Assert.IsTrue(Fields(rows[2].AfterJson)["Deleted"].GetBoolean(), "a soft delete leaves a row");
        }

        [TestMethod]
        public void ContractTermsAndGuaranteeChangesAreListedAndUnlistedFieldsAreNot()
        {
            AppHost host = NewHost();
            int termsId = 0, guaranteeId = 0;
            host.Write(uow =>
            {
                var terms = AppHost.Add(uow, new Pc.PcContractTerms { ContractId = 5, ProjectCode = "P-1", ValueExVat = 100m });
                var guarantee = AppHost.Add(uow, new Pc.PcGuaranteeInfo { SourceKind = Pc.PcGuaranteeSource.ContractGuaranty, SourceId = 1, ContractId = 5, GuaranteeType = Pc.PcGuaranteeType.Performance, Status = Pc.PcGuaranteeStatus.Active });
                uow.SaveChanges();
                termsId = terms.Id; guaranteeId = guarantee.Id;
            }, Pmo);
            Assert.AreEqual(1, Trail(host, nameof(Pc.PcContractTerms)).Count);
            Assert.AreEqual(1, Trail(host, nameof(Pc.PcGuaranteeInfo)).Count);

            // a listed field: one row with before and after
            host.Write(uow => { uow.Set<Pc.PcContractTerms>().Single(x => x.Id == termsId).ValueExVat = 250m; uow.SaveChanges(); }, Pmo);
            // fields that the system rewrites all the time are not listed: no row
            host.Write(uow => { var t = uow.Set<Pc.PcContractTerms>().Single(x => x.Id == termsId); t.AutoSnapshot = false; t.DataDateWeekday = 4; t.ClientReportShowsIndices = true; uow.SaveChanges(); }, Pmo);

            List<FnAuditLog> termsRows = Trail(host, nameof(Pc.PcContractTerms));
            Assert.AreEqual(2, termsRows.Count, "the unlisted fields wrote nothing");
            Assert.AreEqual("Modified", termsRows[1].Action);
            Assert.AreEqual(termsId, termsRows[1].EntityId);
            Assert.AreEqual(100m, Fields(termsRows[1].BeforeJson)["ValueExVat"].GetDecimal());
            Assert.AreEqual(250m, Fields(termsRows[1].AfterJson)["ValueExVat"].GetDecimal());
            Assert.IsTrue(termsRows[1].Summary.StartsWith("شرایط قرارداد"), termsRows[1].Summary);

            // the status of a guarantee is the thing the review wanted to see
            host.Write(uow => { var g = uow.Set<Pc.PcGuaranteeInfo>().Single(x => x.Id == guaranteeId); g.Status = Pc.PcGuaranteeStatus.Released; uow.SaveChanges(); }, AppHost.Principal(9, "commercial.user", "COMMERCIAL"));
            List<FnAuditLog> guaranteeRows = Trail(host, nameof(Pc.PcGuaranteeInfo));
            Assert.AreEqual(2, guaranteeRows.Count);
            Assert.AreEqual(9, guaranteeRows[1].UserId);
            Assert.AreEqual("Active", Fields(guaranteeRows[1].BeforeJson)["Status"].GetString());
            Assert.AreEqual("Released", Fields(guaranteeRows[1].AfterJson)["Status"].GetString());
            Assert.IsTrue(guaranteeRows[1].Summary.StartsWith("ضمانت‌نامه"), guaranteeRows[1].Summary);
        }

        [TestMethod]
        public void TheInsertOfScheduleRowsIsListedAndTheRecomputedFieldsAreNot()
        {
            AppHost host = NewHost();
            int activityId = 0, nodeId = 0;
            host.Write(uow =>
            {
                var node = AppHost.Add(uow, new Pc.PcWbsNode { ContractId = 5, Code = "1", Title = "Project", NodeType = Pc.PcWbsNodeType.Project, BudgetAmount = 10m });
                var activity = AppHost.Add(uow, new Pc.PcActivity { ContractId = 5, Code = "A-1", Title = "Cut", DurationDays = 3, Status = Pc.PcActivityStatus.NotStarted });
                uow.SaveChanges();
                nodeId = node.Id; activityId = activity.Id;
            }, Pmo);

            // R1a2 (L9): the insert of an activity and of a WBS node is a decision (a node made by the generator is as much a decision as an edit): one row each
            List<FnAuditLog> inserted = Trail(host, nameof(Pc.PcActivity));
            Assert.AreEqual(1, inserted.Count, "the insert of an activity is listed");
            Assert.AreEqual("Added", inserted[0].Action);
            Assert.AreEqual("NotStarted", Fields(inserted[0].AfterJson)["Status"].GetString());
            List<FnAuditLog> insertedNodes = Trail(host, nameof(Pc.PcWbsNode));
            Assert.AreEqual(1, insertedNodes.Count, "the insert of a WBS node is listed");
            Assert.AreEqual("Added", insertedNodes[0].Action);

            // the forecast run rewrites these all the time
            host.Write(uow =>
            {
                var a = uow.Set<Pc.PcActivity>().Single(x => x.Id == activityId);
                a.EarlyStart = new DateTime(2026, 1, 1); a.EarlyFinish = new DateTime(2026, 1, 4); a.TotalFloat = 2; a.IsCritical = true; a.ForecastFinish = new DateTime(2026, 1, 9);
                uow.SaveChanges();
            }, null);
            Assert.AreEqual(1, Trail(host, nameof(Pc.PcActivity)).Count, "recomputed schedule fields are not listed");

            // a person starts the activity and edits the budget of a node
            host.Write(uow => { var a = uow.Set<Pc.PcActivity>().Single(x => x.Id == activityId); a.Status = Pc.PcActivityStatus.InProgress; a.ActualStart = new DateTime(2026, 1, 2); uow.SaveChanges(); }, Pmo);
            host.Write(uow => { uow.Set<Pc.PcWbsNode>().Single(x => x.Id == nodeId).BudgetAmount = 99m; uow.SaveChanges(); }, Pmo);

            List<FnAuditLog> activityRows = Trail(host, nameof(Pc.PcActivity));
            Assert.AreEqual(2, activityRows.Count);
            Assert.AreEqual("NotStarted", Fields(activityRows[1].BeforeJson)["Status"].GetString());
            Assert.AreEqual("InProgress", Fields(activityRows[1].AfterJson)["Status"].GetString());
            Assert.IsTrue(activityRows[1].Summary.StartsWith("فعالیت برنامه"), activityRows[1].Summary);
            List<FnAuditLog> nodeRows = Trail(host, nameof(Pc.PcWbsNode));
            Assert.AreEqual(2, nodeRows.Count);
            Assert.AreEqual(99m, Fields(nodeRows[1].AfterJson)["BudgetAmount"].GetDecimal());
        }

        [TestMethod]
        public void TheLinesOfACostBaselineAreOneSummaryRowPerBaseline()
        {
            AppHost host = NewHost();
            host.Write(uow =>
            {
                for (int i = 1; i <= 3; i++) AppHost.Add(uow, new Pc.PcCostBaselineLine { ContractId = 5, BaselineId = 11, Amount = 100m * i, Description = "line " + i });
                uow.SaveChanges();
            }, Pmo);

            // R1a2 (L9): hundreds of lines are written at once; one summary row per baseline says how many and how much
            List<FnAuditLog> rows = Trail(host, nameof(Pc.PcCostBaselineLine));
            Assert.AreEqual(1, rows.Count, "three lines of one baseline, one row");
            Assert.AreEqual(11, rows[0].EntityId, "the row is about the baseline");
            Assert.AreEqual("Added", rows[0].Action);
            StringAssert.Contains(rows[0].Summary, "3 سطر افزوده");
            Assert.AreEqual(3, Fields(rows[0].AfterJson)["lines"].GetInt32());
            Assert.AreEqual(600m, Fields(rows[0].AfterJson)["amount"].GetDecimal());
            Assert.AreEqual(7, rows[0].UserId);

            // a change of one line and the removal of another make one more row, with the amounts before and after
            host.Write(uow =>
            {
                List<Pc.PcCostBaselineLine> lines = uow.Set<Pc.PcCostBaselineLine>().Where(l => l.BaselineId == 11).OrderBy(l => l.Id).ToList();
                lines[0].Amount = 150m;
                uow.Set<Pc.PcCostBaselineLine>().Remove(lines[2]);
                uow.SaveChanges();
            }, Pmo);
            rows = Trail(host, nameof(Pc.PcCostBaselineLine));
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("Modified", rows[1].Action);
            StringAssert.Contains(rows[1].Summary, "1 سطر تغییر");
            StringAssert.Contains(rows[1].Summary, "1 سطر حذف");
            Assert.AreEqual(400m, Fields(rows[1].BeforeJson)["amount"].GetDecimal(), "the lines that were changed or removed, as they were");
            Assert.AreEqual(150m, Fields(rows[1].AfterJson)["amount"].GetDecimal(), "the line that is left, as it is now");

            // another baseline is its own row
            host.Write(uow => { AppHost.Add(uow, new Pc.PcCostBaselineLine { ContractId = 5, BaselineId = 12, Amount = 5m, Description = "other" }); uow.SaveChanges(); }, Pmo);
            Assert.AreEqual(12, Trail(host, nameof(Pc.PcCostBaselineLine)).Last().EntityId);
        }

        [TestMethod]
        public async Task AServerJobWritesTheTrailWithUserZero()
        {
            AppHost host = NewHost();
            int guaranteeId = 0;
            host.Write(uow =>
            {
                var g = AppHost.Add(uow, new Pc.PcGuaranteeInfo { SourceKind = Pc.PcGuaranteeSource.ContractGuaranty, SourceId = 1, ContractId = 5, GuaranteeType = Pc.PcGuaranteeType.Advance, Status = Pc.PcGuaranteeStatus.Active });
                uow.SaveChanges();
                guaranteeId = g.Id;
            }, Pmo);

            await host.AsSystem(async provider =>
            {
                var uow = provider.GetRequiredService<IUnitOfWork>();
                uow.Set<Pc.PcGuaranteeInfo>().Single(x => x.Id == guaranteeId).Status = Pc.PcGuaranteeStatus.Expired;
                await uow.SaveChangesAsync();
                return 0;
            });

            List<FnAuditLog> rows = Trail(host, nameof(Pc.PcGuaranteeInfo));
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(0, rows[1].UserId, "no HTTP context: the job is user 0");
            Assert.IsNull(rows[1].Ip);
            Assert.AreEqual("Expired", Fields(rows[1].AfterJson)["Status"].GetString());
        }

        [TestMethod]
        public void ProcurementHeadersAreInTheTrailWithoutTheirLines()
        {
            AppHost host = NewHost();
            int orderId = 0;
            host.Write(uow => { var po = AppHost.Add(uow, new Proc.PurchaseOrder { Number = "PO-1", ProviderId = 3, Status = Proc.PurchaseOrderStatus.Draft, TotalAmount = 10m }); uow.SaveChanges(); orderId = po.Id; }, Pmo);
            host.Write(uow => { var po = uow.Set<Proc.PurchaseOrder>().Single(x => x.Id == orderId); po.Status = Proc.PurchaseOrderStatus.Approved; po.ApprovedByUserId = 7; po.TotalAmount = 12m; uow.SaveChanges(); }, Pmo);

            List<FnAuditLog> rows = Trail(host, nameof(Proc.PurchaseOrder));
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("سفارش خرید PO-1", rows[0].Summary);
            Assert.AreEqual("Draft", Fields(rows[1].BeforeJson)["Status"].GetString());
            Assert.AreEqual("Approved", Fields(rows[1].AfterJson)["Status"].GetString());
            Assert.AreEqual(7, Fields(rows[1].AfterJson)["ApprovedByUserId"].GetInt32());
        }
    }
}
