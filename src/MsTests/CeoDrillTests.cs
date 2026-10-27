using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Ceo;
using Services.NasbNiro.Ceo.Drills;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.AiDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>A user with fixed roles (any-of, like the real context) for the tests of the CEO dashboard tools.</summary>
    internal sealed class CeoFakeUser : IInventoryUserContext
    {
        private readonly string[] _roles;

        public CeoFakeUser(params string[] roles)
        {
            _roles = roles;
        }

        public int UserId => 7;

        public bool IsInRole(string role) => !string.IsNullOrWhiteSpace(role) && role.Split(',').Any(r => _roles.Contains(r.Trim(), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>A service provider that knows one service (the drills and the UI tools need no database before they validate their arguments).</summary>
    internal sealed class CeoOneService : IServiceProvider
    {
        private readonly Type _type;
        private readonly object _service;

        public CeoOneService(Type type = null, object service = null)
        {
            _type = type;
            _service = service;
        }

        public object GetService(Type serviceType) => _type != null && serviceType == _type ? _service : null;
    }

    /// <summary>The drill catalogue of the CEO dashboard (release D1): the contract keys, the parameters, the columns and the rules of the binder. The drills themselves read the modules and are checked by the smoke test.</summary>
    [TestClass]
    public class CeoDrillTests
    {
        // the 68 keys of the contract (section 2 of the D1 contract)
        private static readonly string[] ContractKeys =
        {
            "fin.cash_accounts", "fin.receivables_by_customer", "fin.receivable_invoices_overdue", "fin.payables_by_supplier", "fin.payables_overdue", "fin.cheques_due",
            "fin.bank_unreconciled", "fin.vouchers_waiting", "fin.revenue_by_month", "fin.cost_by_month", "fin.project_profitability",
            "sl.pipeline_by_stage", "sl.inquiries_open", "sl.proposals_waiting", "sl.proposals_due", "sl.orders_open", "sl.orders_waiting_credit", "sl.hit_rate_by_month",
            "sl.lost_by_reason", "sl.customers_top", "sl.quotations_expiring",
            "pc.portfolio", "pc.late_activities", "pc.milestones_next", "pc.change_requests_open", "pc.guarantees_expiring", "pc.penalty_exposure", "pc.initiation_overdue", "pc.client_responses_overdue",
            "mf.orders_late", "mf.orders_by_status", "mf.machines_down", "mf.maintenance_overdue", "mf.work_center_load", "mf.oee_by_work_center",
            "qc.ncr_open", "qc.capa_overdue", "qc.calibration_due", "qc.complaints_open", "qc.h_points_pending", "qc.documents_waiting",
            "inv.items_below_minimum", "inv.stock_value_by_warehouse", "inv.quarantine", "inv.material_requests_open", "inv.slow_moving", "inv.top_movements",
            "pr.requests_open", "pr.orders_open", "pr.late_lines", "pr.invoices_waiting", "pr.payments_waiting", "pr.blocked_suppliers", "pr.spend_by_supplier", "pr.supplier_on_time",
            "sv.requests_open", "sv.sla_breaches", "sv.orders_open", "sv.warranty_cost_by_month", "sv.units_by_customer", "sv.survey_by_month", "sv.ltsa_expiring",
            "of.decisions_overdue", "of.letters_waiting", "of.attendance_month", "of.leave_requests_pending",
            "ai.notes_new", "ai.seat_runs",
        };

        private static readonly Dictionary<string, string> ModuleOfPrefix = new Dictionary<string, string>
        {
            ["fin"] = CeoModules.Finance, ["sl"] = CeoModules.Sales, ["pc"] = CeoModules.Projects, ["mf"] = CeoModules.Production, ["qc"] = CeoModules.Quality,
            ["inv"] = CeoModules.Inventory, ["pr"] = CeoModules.Procurement, ["sv"] = CeoModules.Service, ["of"] = CeoModules.Office, ["ai"] = CeoModules.Ai,
        };

        private static readonly DateTime Today = new DateTime(2026, 10, 25);

        private static bool HasPersian(string text) => !string.IsNullOrEmpty(text) && text.Any(c => c >= '؀' && c <= 'ۿ');

        // ------------------------------------------------------------------------------------------ the catalogue

        [TestMethod]
        public void Catalogue_Holds_Exactly_The_Contract_Keys()
        {
            Assert.AreEqual(68, ContractKeys.Length);
            Assert.AreEqual(ContractKeys.Length, ContractKeys.Distinct().Count());
            List<string> keys = CeoDrillCatalog.All.Select(d => d.Key).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "keys are unique");
            Assert.AreEqual(68, keys.Count, "the exact number of drills of the contract");
            CollectionAssert.AreEquivalent(ContractKeys, keys);
        }

        [TestMethod]
        public void Catalogue_Has_The_Drills_Of_Every_Module()
        {
            var expected = new Dictionary<string, int>
            {
                [CeoModules.Finance] = 11, [CeoModules.Sales] = 10, [CeoModules.Projects] = 8, [CeoModules.Production] = 6, [CeoModules.Quality] = 6,
                [CeoModules.Inventory] = 6, [CeoModules.Procurement] = 8, [CeoModules.Service] = 7, [CeoModules.Office] = 4, [CeoModules.Ai] = 2,
            };
            foreach (KeyValuePair<string, int> e in expected)
                Assert.AreEqual(e.Value, CeoDrillCatalog.All.Count(d => d.Module == e.Key), e.Key);
            CollectionAssert.AreEqual(CeoModules.All.ToList(), expected.Keys.ToList());
            Assert.AreEqual(9, CeoModules.Sections.Count);
            Assert.IsFalse(CeoModules.Sections.Contains(CeoModules.Ai));
        }

        [TestMethod]
        public void Every_Drill_Is_Described_In_Persian_And_Sits_In_Its_Module()
        {
            string[] kinds = { "text", "number", "rial", "percent", "date", "link", "status" };
            foreach (DrillDefinition d in CeoDrillCatalog.All)
            {
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(d.Key, "^(fin|sl|pc|mf|qc|inv|pr|sv|of|ai)\\.[a-z0-9]+(_[a-z0-9]+)*$"), d.Key);
                Assert.AreEqual(ModuleOfPrefix[d.Key.Substring(0, d.Key.IndexOf('.'))], d.Module, d.Key);
                Assert.IsTrue(HasPersian(d.Title), d.Key + " title");
                Assert.IsTrue(HasPersian(d.Description), d.Key + " description");
                Assert.IsTrue(d.Description.Length > d.Title.Length, d.Key);
                Assert.IsTrue(d.Link != null && d.Link.StartsWith("/"), d.Key + " link");
                // a drill whose rows are records the user can open (a link column) names the kind of record and its Persian label for the citations of the AI tool
                if (d.Columns.Any(c => c.Kind == "link"))
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(d.RecordType), d.Key + " record type");
                    Assert.IsTrue(HasPersian(d.RecordLabel), d.Key + " record label");
                }
                Assert.IsNotNull(d.Run, d.Key);

                Assert.IsTrue(d.Columns.Count >= 2, d.Key + " columns");
                Assert.AreEqual(d.Columns.Count, d.Columns.Select(c => c.Key).Distinct().Count(), d.Key + " column keys are unique");
                foreach (DrillColumn c in d.Columns)
                {
                    CollectionAssert.Contains(kinds, c.Kind, d.Key + "." + c.Key);
                    // a title is Persian; the names of the standard indicators (CPI, OEE, SLA, SPI) are written as everybody writes them
                    Assert.IsTrue(HasPersian(c.Title) || System.Text.RegularExpressions.Regex.IsMatch(c.Title, "^[A-Z]{2,4}$"), d.Key + "." + c.Key + " title");
                    if (c.Kind == "link") Assert.AreEqual("link", c.Key, d.Key);
                }
                Assert.IsTrue(d.Columns.Count(c => c.Kind == "link") <= 1, d.Key);
            }
        }

        [TestMethod]
        public void Every_Parameter_Is_Typed_Described_And_Known_To_The_Tool()
        {
            var jsonType = new Dictionary<string, string> { ["int"] = "integer", ["date"] = "string", ["string"] = "string", ["bool"] = "boolean" };
            foreach (DrillDefinition d in CeoDrillCatalog.All)
            {
                Assert.AreEqual(d.Parameters.Count, d.Parameters.Select(p => p.Name).Distinct().Count(), d.Key + " parameter names are unique");
                foreach (DrillParam p in d.Parameters)
                {
                    Assert.IsTrue(jsonType.ContainsKey(p.Type), d.Key + "." + p.Name);
                    Assert.IsTrue(AiCeoTools.ParameterSchema.ContainsKey(p.Name), d.Key + "." + p.Name + " is not in the schema of ceo_drill");
                    Assert.AreEqual(jsonType[p.Type], AiCeoTools.ParameterSchema[p.Name].Type, d.Key + "." + p.Name);
                    Assert.IsTrue(HasPersian(p.Description), d.Key + "." + p.Name + " description");
                    Assert.AreNotEqual("top", p.Name, d.Key);
                    Assert.AreNotEqual("page", p.Name, d.Key);
                    Assert.AreNotEqual("key", p.Name, d.Key);
                    if (p.Type == "int" && p.Min.HasValue && p.Max.HasValue) Assert.IsTrue(p.Min <= p.Max, d.Key + "." + p.Name);
                }
                // from and to come together
                Assert.AreEqual(d.Parameters.Any(p => p.Name == "from"), d.Parameters.Any(p => p.Name == "to"), d.Key);
            }
            // a parameter of the schema that no drill declares would only confuse the model
            foreach (string name in AiCeoTools.ParameterSchema.Keys.Where(k => k != "top" && k != "page"))
                Assert.IsTrue(CeoDrillCatalog.All.Any(d => d.Parameters.Any(p => p.Name == name)), name);
        }

        [TestMethod]
        public void No_Column_Key_Is_Removed_By_The_Privacy_Filter_Of_The_Tools()
        {
            foreach (DrillDefinition d in CeoDrillCatalog.All)
            {
                var row = new JsonObject();
                foreach (DrillColumn c in d.Columns) row[c.Key] = 1;
                JsonNode cleaned = AiToolShaper.Clean(row);
                foreach (DrillColumn c in d.Columns) Assert.IsTrue(((JsonObject)cleaned).ContainsKey(c.Key), d.Key + "." + c.Key + " would be removed from the result for the model");
                // and no column carries what a person is paid
                foreach (DrillColumn c in d.Columns)
                {
                    string lower = c.Key.ToLowerInvariant();
                    Assert.IsFalse(lower.Contains("salary") || lower.Contains("wage") || lower.Contains("payroll"), d.Key + "." + c.Key);
                }
            }
        }

        [TestMethod]
        public void Compact_Catalogue_Lists_Every_Key_In_One_Short_Result()
        {
            Dictionary<string, object> compact = CeoDrillCatalog.Compact();
            Assert.AreEqual(68, compact["totalCount"]);
            string all = string.Join("\n", compact.Where(p => p.Key != "totalCount").Select(p => (string)p.Value));
            foreach (string key in ContractKeys) StringAssert.Contains(all, key);
            foreach (string module in CeoModules.All) Assert.IsTrue(compact.ContainsKey(module), module);
            // the whole catalogue goes through the registry without a string being cut (the registry cuts strings longer than 200 characters only when the result is too long)
            string json = AiToolShaper.Fit(AiToolShaper.Clean(AiToolShaper.ToNode(compact)), new List<AiCitationDto>(), new AiToolDefinition { Name = "ceo_drills" });
            Assert.IsTrue(json.Length <= AiToolShaper.MaxChars, "length " + json.Length);
            foreach (string key in ContractKeys) StringAssert.Contains(json, key);
            Assert.IsFalse(json.Contains("\"truncated\""));
        }

        [TestMethod]
        public void Descriptors_Mirror_The_Definitions_And_Find_Ignores_Case()
        {
            foreach (DrillDefinition d in CeoDrillCatalog.All)
            {
                var dto = CeoDrillCatalog.Describe(d);
                Assert.AreEqual(d.Key, dto.Key);
                Assert.AreEqual(d.Parameters.Count, dto.Parameters.Count);
                Assert.AreEqual("CEO,PMO", dto.Roles);
            }
            Assert.IsNotNull(CeoDrillCatalog.Find(" QC.NCR_OPEN "));
            Assert.IsNull(CeoDrillCatalog.Find("qc.nothing"));
            Assert.IsNull(CeoDrillCatalog.Find(null));
            Assert.IsNull(CeoDrillCatalog.Find("  "));
        }

        // ------------------------------------------------------------------------------------------ binding of the parameters

        [TestMethod]
        public void Every_Drill_Binds_With_No_Parameters_And_Its_Defaults_Are_Sensible()
        {
            foreach (DrillDefinition d in CeoDrillCatalog.All)
            {
                string error = DrillBinder.Bind(d.Parameters, new Dictionary<string, string>(), Today, out DrillArgs args);
                Assert.IsNull(error, d.Key + ": " + error);
                Assert.IsFalse(d.Parameters.Any(p => p.Required), d.Key + " has a required parameter, so the AI could not call it with the key alone");
                foreach (DrillParam p in d.Parameters.Where(p => p.Type == "int" && p.Default != null))
                    Assert.AreEqual((int)p.Default, args.Int(p.Name), d.Key + "." + p.Name);
                if (d.Parameters.Any(p => p.Name == "from"))
                    Assert.IsTrue(args.Date("from") <= args.Date("to"), d.Key + ": the default range is empty");
            }
        }

        [TestMethod]
        public void Paging_Defaults_And_Limits()
        {
            Assert.IsNull(DrillBinder.BindPaging(null, null, 50, out int top, out int page));
            Assert.AreEqual(50, top);
            Assert.AreEqual(1, page);
            Assert.IsNull(DrillBinder.BindPaging("20", "3", 50, out top, out page));
            Assert.AreEqual(20, top);
            Assert.AreEqual(3, page);
            Assert.IsNull(DrillBinder.BindPaging("۳۰", null, 50, out top, out _));
            Assert.AreEqual(30, top);
            Assert.IsNull(DrillBinder.BindPaging("200", "1", 50, out top, out _));
            Assert.AreEqual(200, top);
            // the refusals are Persian sentences that name the field in Persian and the limit (no English word reaches the user)
            foreach (string bad in new[] { "0", "-1", "201", "abc", "1.5" })
                Assert.AreEqual("مقدار «تعداد» باید عددی بین 1 و 200 باشد.", DrillBinder.BindPaging(bad, null, 50, out _, out _), bad);
            foreach (string bad in new[] { "0", "-2", "x", "10001", "99999999999" })
                Assert.AreEqual("مقدار «صفحه» باید عدد صحیح بین 1 و 10000 باشد.", DrillBinder.BindPaging(null, bad, 50, out _, out _), bad);
            // the last page that may be asked for
            Assert.AreEqual(10000, DrillBinder.MaxPage);
            Assert.IsNull(DrillBinder.BindPaging("1", "10000", 50, out _, out page));
            Assert.AreEqual(10000, page);
            Assert.IsNull(DrillBinder.BindPaging("  ", " ", 20, out top, out page));
            Assert.AreEqual(20, top);
            Assert.AreEqual(1, page);
        }

        [TestMethod]
        public void Parameters_Are_Validated_Against_Their_Declaration()
        {
            DrillDefinition ncr = CeoDrillCatalog.Find("qc.ncr_open");
            Assert.IsNull(DrillBinder.Bind(ncr.Parameters, new Dictionary<string, string> { ["severity"] = "major" }, Today, out DrillArgs args));
            Assert.AreEqual("Major", args.Str("severity"), "an allowed value is written as the catalogue spells it");
            Assert.IsNull(DrillBinder.Bind(ncr.Parameters, new Dictionary<string, string> { ["SEVERITY"] = "Minor" }, Today, out args), "names are compared without case");
            Assert.AreEqual("Minor", args.Str("severity"));
            string error = DrillBinder.Bind(ncr.Parameters, new Dictionary<string, string> { ["severity"] = "Huge" }, Today, out _);
            StringAssert.Contains(error, "severity");
            StringAssert.Contains(error, "Major");
            Assert.IsNull(DrillBinder.Bind(ncr.Parameters, new Dictionary<string, string> { ["unknown"] = "x" }, Today, out _), "a parameter that is not declared is ignored");

            DrillDefinition slow = CeoDrillCatalog.Find("inv.slow_moving");
            DrillParam days = slow.Parameters.Single(p => p.Name == "days");
            Assert.IsNull(DrillBinder.Bind(slow.Parameters, new Dictionary<string, string>(), Today, out args));
            Assert.AreEqual((int)days.Default, args.Int("days"));
            Assert.IsNull(DrillBinder.Bind(slow.Parameters, new Dictionary<string, string> { ["days"] = "۹۰" }, Today, out args));
            Assert.AreEqual(90, args.Int("days"));
            foreach (string bad in new[] { "0", "-5", "abc", "1.5", (days.Max.Value + 1).ToString() })
                Assert.IsNotNull(DrillBinder.Bind(slow.Parameters, new Dictionary<string, string> { ["days"] = bad }, Today, out _), "days=" + bad);

            DrillDefinition late = CeoDrillCatalog.Find("pc.late_activities");
            Assert.IsNull(DrillBinder.Bind(late.Parameters, new Dictionary<string, string> { ["onlyCritical"] = "true" }, Today, out args));
            Assert.IsTrue(args.Bool("onlyCritical"));
            Assert.IsNull(DrillBinder.Bind(late.Parameters, new Dictionary<string, string>(), Today, out args));
            Assert.IsFalse(args.Bool("onlyCritical"));
            StringAssert.Contains(DrillBinder.Bind(late.Parameters, new Dictionary<string, string> { ["onlyCritical"] = "maybe" }, Today, out _), "onlyCritical");
        }

        [TestMethod]
        public void The_Portfolio_Drill_Takes_A_Status_Of_Danger_Warn_Or_Ok()
        {
            DrillDefinition portfolio = CeoDrillCatalog.Find("pc.portfolio");
            DrillParam status = portfolio.Parameters.Single(p => p.Name == "status");
            Assert.AreEqual("string", status.Type);
            Assert.IsFalse(status.Required);
            CollectionAssert.AreEqual(new[] { "danger", "warn", "ok" }, status.Allowed);
            Assert.IsTrue(HasPersian(status.Description));

            Assert.IsNull(DrillBinder.Bind(portfolio.Parameters, new Dictionary<string, string>(), Today, out DrillArgs args));
            Assert.IsNull(args.Str("status"), "without a status every active project is listed");
            foreach ((string given, string canonical) in new[] { ("danger", "danger"), ("WARN", "warn"), ("Ok", "ok") })
            {
                Assert.IsNull(DrillBinder.Bind(portfolio.Parameters, new Dictionary<string, string> { ["status"] = given }, Today, out args), given);
                Assert.AreEqual(canonical, args.Str("status"), "an allowed value is written as the catalogue spells it");
            }
            string error = DrillBinder.Bind(portfolio.Parameters, new Dictionary<string, string> { ["status"] = "red" }, Today, out _);
            StringAssert.Contains(error, "status");
            StringAssert.Contains(error, "danger");
            StringAssert.Contains(error, "warn");

            // the table says which index made the project red or amber
            CollectionAssert.IsSubsetOf(new[] { "status", "spiStatus", "cpiStatus", "spi", "cpi" }, portfolio.Columns.Select(c => c.Key).ToList());
            Assert.AreEqual("status", portfolio.Columns.Single(c => c.Key == "spiStatus").Kind);
            Assert.AreEqual("status", portfolio.Columns.Single(c => c.Key == "cpiStatus").Kind);
        }

        [TestMethod]
        public void The_Portfolio_Status_Filter_Counts_Like_The_Project_Dashboard()
        {
            PcPortfolioRowDto Row(string spi, string cpi) => new PcPortfolioRowDto { SpiStatus = spi, CpiStatus = cpi };
            var rows = new Dictionary<string, PcPortfolioRowDto>
            {
                ["red/green"] = Row("red", "green"), ["amber/amber"] = Row("amber", "amber"), ["green/amber"] = Row("green", "amber"), ["none/none"] = Row("none", "none"),
                ["green/none"] = Row("green", "none"), ["red/amber"] = Row("red", "amber"), ["green/green"] = Row("green", "green"), ["null/null"] = Row(null, null),
            };
            List<string> Names(string status) => rows.Where(r => ProjectDrills.InStatus(r.Value, status)).Select(r => r.Key).ToList();

            // one colour per project, the rule of the project dashboard and the CEO overview (PcReportService.IsRed / IsAmber): a project that is red on one index and amber on the other is red only;
            // green is every project that is neither red nor amber, the ones without an index value included (the green slice of the donut of the overview is the same count)
            CollectionAssert.AreEqual(new[] { "red/green", "red/amber" }, Names("danger"));
            CollectionAssert.AreEqual(new[] { "amber/amber", "green/amber" }, Names("warn"));
            CollectionAssert.AreEqual(new[] { "none/none", "green/none", "green/green", "null/null" }, Names("ok"));
            Assert.AreEqual(rows.Count, Names(null).Count, "no status: every project");
            Assert.AreEqual(rows.Count, Names("").Count);
            Assert.AreEqual(rows.Values.Count(PcReportService.IsRed), Names("danger").Count);
            Assert.AreEqual(rows.Values.Count(PcReportService.IsAmber), Names("warn").Count);
            Assert.AreEqual(0, Names("danger").Intersect(Names("warn")).Count(), "no project is in two lists");
            Assert.AreEqual(0, Names("ok").Intersect(Names("danger")).Concat(Names("ok").Intersect(Names("warn"))).Count());
            Assert.AreEqual(rows.Count, Names("danger").Count + Names("warn").Count + Names("ok").Count, "red + amber + green is every project");
            Assert.AreEqual(rows.Count - rows.Values.Count(PcReportService.IsRed) - rows.Values.Count(PcReportService.IsAmber), Names("ok").Count, "green is what is left, as the donut counts it");

            // the light of a row: the worse of the two indices; "none" (the module's word for no value) is not green
            Assert.AreEqual("قرمز", ProjectDrills.Traffic("red", "amber"));
            Assert.AreEqual("زرد", ProjectDrills.Traffic("green", "amber"));
            Assert.AreEqual("سبز", ProjectDrills.Traffic("green", "none"));
            Assert.AreEqual("سبز", ProjectDrills.Traffic("green", "green"));
            Assert.AreEqual("بدون داده", ProjectDrills.Traffic("none", "none"));
            Assert.AreEqual("بدون داده", ProjectDrills.Traffic(null, null));
        }

        // ------------------------------------------------------------------------------------------ the figures of the dashboard and the drills behind them

        private static DrillParam ParameterOf(string drillKey, string name) => CeoDrillCatalog.Find(drillKey).Parameters.Single(p => p.Name == name);

        /// <summary>The arguments a figure of the overview gives its drill (what the front sends when the CEO clicks the figure).</summary>
        private static DrillArgs ArgsOfFigure(string kpiKey)
        {
            CeoKpiSpec spec = CeoKpiSpecs.Get(kpiKey);
            DrillDefinition drill = CeoDrillCatalog.Find(spec.DrillKey);
            Assert.IsNotNull(spec.DrillParams, kpiKey + " carries parameters");
            Assert.IsNull(DrillBinder.Bind(drill.Parameters, spec.DrillParams(Today), Today, out DrillArgs args), kpiKey);
            return args;
        }

        /// <summary>The arguments of a drill called with no parameter at all (what the AI and the open drill give).</summary>
        private static DrillArgs DefaultsOf(string drillKey)
        {
            Assert.IsNull(DrillBinder.Bind(CeoDrillCatalog.Find(drillKey).Parameters, new Dictionary<string, string>(), Today, out DrillArgs args), drillKey);
            return args;
        }

        [TestMethod]
        public void Every_Drill_Key_Of_The_Overview_Is_In_The_Catalogue_And_Every_Filter_Of_A_Figure_Is_Declared_By_Its_Drill()
        {
            // the keys the overview points at are the 66 keys of the modules; the two drills of the AI module are behind no figure
            List<string> referred = typeof(CeoDrillRef).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()).ToList();
            CollectionAssert.AreEquivalent(ContractKeys.Where(k => !k.StartsWith("ai.", StringComparison.Ordinal)).ToList(), referred);

            var filtered = new List<string>();
            foreach (CeoKpiSpec spec in CeoKpiSpecs.All)
            {
                DrillDefinition drill = CeoDrillCatalog.Find(spec.DrillKey);
                Assert.IsNotNull(drill, spec.Key + " -> " + spec.DrillKey);
                Dictionary<string, string> given = spec.DrillParams?.Invoke(Today);
                if (given == null) continue;
                filtered.Add(spec.Key);
                foreach (string name in given.Keys)
                    Assert.IsTrue(drill.Parameters.Any(p => p.Name == name), spec.Key + ": " + spec.DrillKey + " declares no parameter " + name);
                Assert.IsNull(DrillBinder.Bind(drill.Parameters, given, Today, out _), spec.Key);
            }
            // the figures that count a narrower set than their drill lists by default, and the two of the year so far
            CollectionAssert.AreEquivalent(
                new[] { "netProfitYtd", "projectsRed", "projectsAmber", "overdueNcrs", "calibrationOverdue", "purchaseRequestsWaiting", "serviceWarrantyCostYtd", "serviceGoodwillWaiting", "lettersWaiting" }, filtered);
        }

        [TestMethod]
        public void A_Figure_Opens_Its_Drill_On_Exactly_The_Records_The_Figure_Counts()
        {
            // purchase requests: the figure counts the submitted ones; the drill without a parameter lists the approved ones too
            Assert.AreEqual("submitted", ArgsOfFigure("purchaseRequestsWaiting").Str("status"));
            Assert.AreEqual("all", DefaultsOf("pr.requests_open").Str("status"));
            CollectionAssert.AreEqual(new[] { "submitted", "approved", "all" }, ParameterOf("pr.requests_open", "status").Allowed);

            // letters: the figure counts the letters in approval; the drill lists the signed and the registered ones too
            Assert.AreEqual("approval", ArgsOfFigure("lettersWaiting").Str("stage"));
            Assert.AreEqual("all", DefaultsOf("of.letters_waiting").Str("stage"));
            CollectionAssert.AreEqual(new[] { "approval", "signature", "registered", "all" }, ParameterOf("of.letters_waiting", "stage").Allowed);
            Assert.IsFalse(DefaultsOf("of.letters_waiting").Bool("mineOnly"));

            // leave requests: the figure counts the ones waiting for the manager, which is the default of the drill, so the figure needs no parameter
            Assert.IsNull(CeoKpiSpecs.Get("leaveRequestsPending").DrillParams);
            Assert.AreEqual("submitted", DefaultsOf("of.leave_requests_pending").Str("status"));
            CollectionAssert.AreEqual(new[] { "submitted", "approved", "all" }, ParameterOf("of.leave_requests_pending", "status").Allowed);

            // service orders, NCRs, calibrations: a flag or a number of days that narrows the list to the figure
            Assert.IsTrue(ArgsOfFigure("serviceGoodwillWaiting").Bool("goodwillOnly"));
            Assert.IsFalse(DefaultsOf("sv.orders_open").Bool("goodwillOnly"));
            Assert.IsTrue(ArgsOfFigure("overdueNcrs").Bool("onlyOverdue"));
            Assert.IsFalse(DefaultsOf("qc.ncr_open").Bool("onlyOverdue"));
            Assert.AreEqual(0, ArgsOfFigure("calibrationOverdue").Int("days"), "zero days: only the equipment whose calibration is overdue");
            Assert.AreEqual(30, DefaultsOf("qc.calibration_due").Int("days"));

            // projects: the red and the amber figures are the two statuses of the portfolio drill
            Assert.AreEqual("danger", ArgsOfFigure("projectsRed").Str("status"));
            Assert.AreEqual("warn", ArgsOfFigure("projectsAmber").Str("status"));
            Assert.IsNull(DefaultsOf("pc.portfolio").Str("status"));

            // the figures of the year so far: from the first day of the Persian year to today
            DateTime yearStart = new PersianCalendar().ToDateTime(1405, 1, 1, 0, 0, 0, 0);
            foreach (string key in new[] { "netProfitYtd", "serviceWarrantyCostYtd" })
            {
                DrillArgs year = ArgsOfFigure(key);
                Assert.AreEqual(yearStart, year.Date("from"), key);
                Assert.AreEqual(Today, year.Date("to"), key);
            }

            // the backlog lists the open orders by itself: no parameter
            Assert.IsNull(CeoKpiSpecs.Get("salesBacklog").DrillParams);
        }

        [TestMethod]
        public async Task The_Calibration_Drill_Takes_Zero_Days_The_Movement_Drill_Takes_A_Kind_And_At_Most_A_Year()
        {
            DrillParam days = ParameterOf("qc.calibration_due", "days");
            Assert.AreEqual(0, days.Min);
            Assert.AreEqual(365, days.Max);
            foreach (string good in new[] { "0", "۰", "1", "30", "365" })
                Assert.IsNull(DrillBinder.Bind(new[] { days }, new Dictionary<string, string> { ["days"] = good }, Today, out _), good);
            foreach (string bad in new[] { "-1", "366", "x" })
                Assert.IsNotNull(DrillBinder.Bind(new[] { days }, new Dictionary<string, string> { ["days"] = bad }, Today, out _), bad);

            DrillParam kind = ParameterOf("inv.top_movements", "kind");
            CollectionAssert.AreEqual(new[] { "receipt", "issue", "all" }, kind.Allowed);
            Assert.AreEqual("all", DefaultsOf("inv.top_movements").Str("kind"));
            Assert.IsNull(DrillBinder.Bind(new[] { kind }, new Dictionary<string, string> { ["kind"] = "Receipt" }, Today, out DrillArgs args));
            Assert.AreEqual("receipt", args.Str("kind"));
            StringAssert.Contains(DrillBinder.Bind(new[] { kind }, new Dictionary<string, string> { ["kind"] = "transfer" }, Today, out _), "receipt");

            // a range of 367 days is refused with a Persian sentence before any record is read (the service has no database here)
            var tooLong = new Dictionary<string, string> { ["from"] = "2025-10-24", ["to"] = "2026-10-25" };
            var answer = await Service("CEO").DrillAsync("inv.top_movements", null, null, tooLong, CancellationToken.None);
            Assert.IsFalse(answer.Success);
            StringAssert.Contains(answer.Message, "366");
            Assert.IsTrue(HasPersian(answer.Message));
            // the attendance of more than a year is refused too
            answer = await Service("CEO").DrillAsync("of.attendance_month", null, null, new Dictionary<string, string> { ["from"] = "2025-10-23", ["to"] = "2026-10-25" }, CancellationToken.None);
            Assert.IsFalse(answer.Success);
            Assert.IsTrue(HasPersian(answer.Message));
        }

        [TestMethod]
        public void The_Backlog_Of_An_Order_Line_Is_The_Share_Of_Its_Amount_That_Is_Not_Invoiced_And_Never_Negative()
        {
            Assert.AreEqual(600m, CeoFigures.UninvoicedAmount(1000m, 10m, 4m));
            Assert.AreEqual(0m, CeoFigures.UninvoicedAmount(500m, 5m, 5m), "fully invoiced");
            Assert.AreEqual(300m, CeoFigures.UninvoicedAmount(300m, 3m, 0m));
            Assert.AreEqual(0m, CeoFigures.UninvoicedAmount(150m, 1m, 3m), "over-invoiced: never below zero");
            Assert.AreEqual(0m, CeoFigures.UninvoicedAmount(900m, 0m, 0m), "a line without a quantity has no share");
            Assert.AreEqual(0m, CeoFigures.UninvoicedAmount(900m, -2m, 0m));
            // the sum of the lines is rounded once, not each line
            decimal sum = CeoFigures.UninvoicedAmount(100m, 3m, 1m) + CeoFigures.UninvoicedAmount(100m, 3m, 1m) + CeoFigures.UninvoicedAmount(100m, 3m, 1m);
            Assert.AreEqual(200m, CeoFigures.RoundRial(sum));
            Assert.AreEqual(67m, CeoFigures.RoundRial(CeoFigures.UninvoicedAmount(100m, 3m, 1m)));
            Assert.AreEqual(2m, CeoFigures.RoundRial(1.5m));
        }

        // ------------------------------------------------------------------------------------------ reading a list of a module

        private sealed class FakePage<T> : PagedList<T>
        {
            public FakePage(IEnumerable<T> items, int total) : base(items, total, 1, DrillRead.ModulePageSize)
            {
            }
        }

        /// <summary>The pages of a list of <paramref name="total"/> numbers as the module serves them (500 a page); the page numbers asked for are recorded.</summary>
        private static Func<int, Task<ResponseDto<PagedList<int>>>> ModulePages(int total, List<int> asked) => number =>
        {
            asked.Add(number);
            IEnumerable<int> page = Enumerable.Range(1, total).Skip((number - 1) * DrillRead.ModulePageSize).Take(DrillRead.ModulePageSize);
            return Task.FromResult(ResponseDtoHelper.Ok<PagedList<int>>(new FakePage<int>(page, total)));
        };

        [TestMethod]
        public async Task A_Module_List_Is_Read_Page_By_Page_And_A_List_Too_Long_For_The_Limit_Is_Refused_Not_Cut()
        {
            Assert.AreEqual(40, DrillRead.MaxPages);
            Assert.AreEqual(500, DrillRead.ModulePageSize);

            var asked = new List<int>();
            List<int> read = await DrillRead.AllAsync(ModulePages(1100, asked));
            CollectionAssert.AreEqual(Enumerable.Range(1, 1100).ToList(), read);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, asked);

            asked.Clear();
            Assert.AreEqual(0, (await DrillRead.AllAsync(ModulePages(0, asked))).Count, "an empty list");
            CollectionAssert.AreEqual(new[] { 1 }, asked);
            asked.Clear();
            Assert.AreEqual(500, (await DrillRead.AllAsync(ModulePages(500, asked))).Count, "exactly one page");
            CollectionAssert.AreEqual(new[] { 1 }, asked);

            // the longest list that is read: forty pages
            asked.Clear();
            int longest = DrillRead.MaxPages * DrillRead.ModulePageSize;
            Assert.AreEqual(longest, (await DrillRead.AllAsync(ModulePages(longest, asked))).Count);
            Assert.AreEqual(DrillRead.MaxPages, asked.Count);

            // one row more: no silent cut, a Persian sentence that asks for a narrower range
            asked.Clear();
            DrillFailureException tooLong = await Assert.ThrowsAsync<DrillFailureException>(() => DrillRead.AllAsync(ModulePages(longest + 1, asked)));
            Assert.AreEqual(CeoDrillText.TooManyRows, tooLong.Message);
            Assert.IsTrue(HasPersian(tooLong.Message));
            Assert.AreEqual(DrillRead.MaxPages, asked.Count, "the limit stops the reading");

            // the refusal of the module becomes a failure with the module's own message
            DrillFailureException refused = await Assert.ThrowsAsync<DrillFailureException>(() => DrillRead.AllAsync<int>(n => Task.FromResult(ResponseDtoHelper.BadRequest<PagedList<int>>(CeoText.NotAllowed))));
            Assert.AreEqual(CeoText.NotAllowed, refused.Message);

            // a page that comes back empty ends the reading although the total says more (no endless loop)
            Assert.AreEqual(0, (await DrillRead.AllAsync<int>(n => Task.FromResult(ResponseDtoHelper.Ok<PagedList<int>>(new FakePage<int>(new int[0], 10))))).Count);
        }

        // ------------------------------------------------------------------------------------------ the words of the catalogue

        [TestMethod]
        public void Titles_And_Descriptions_Are_Persian_With_No_Latin_Word_Except_The_Standard_Acronyms()
        {
            string[] acronyms = { "CAPA", "CPI", "ITP", "KOM", "LTSA", "NCR", "OEE", "PEP", "SLA", "SPI" };
            var latin = new Regex("[A-Za-z][A-Za-z0-9_]+");
            foreach (DrillDefinition d in CeoDrillCatalog.All)
                foreach ((string what, string text) in new[] { ("title", d.Title), ("description", d.Description) })
                    foreach (Match m in latin.Matches(text))
                        CollectionAssert.Contains(acronyms, m.Value, d.Key + " " + what + " holds the Latin word «" + m.Value + "»");
            // the digits of a description are Persian or plain: no Arabic-Indic digit (they are not the ones the front end writes)
            foreach (DrillDefinition d in CeoDrillCatalog.All)
                Assert.IsFalse((d.Title + d.Description).Any(c => c >= '٠' && c <= '٩'), d.Key);
        }

        [TestMethod]
        public void Date_Ranges_Accept_Both_Calendars_And_Refuse_A_Backwards_Range()
        {
            DrillDefinition month = CeoDrillCatalog.Find("of.attendance_month");
            Assert.IsNull(DrillBinder.Bind(month.Parameters, new Dictionary<string, string> { ["from"] = "2026-10-01", ["to"] = "2026-10-20" }, Today, out DrillArgs args));
            Assert.AreEqual(new DateTime(2026, 10, 1), args.Date("from"));
            Assert.AreEqual(new DateTime(2026, 10, 20), args.Date("to"));
            // 1405/08/01 is 2026-10-23
            Assert.IsNull(DrillBinder.Bind(month.Parameters, new Dictionary<string, string> { ["from"] = "1405/08/01", ["to"] = "1405/08/05" }, Today, out args));
            Assert.AreEqual(new DateTime(2026, 10, 23), args.Date("from"));
            Assert.AreEqual(new DateTime(2026, 10, 27), args.Date("to"));
            StringAssert.Contains(DrillBinder.Bind(month.Parameters, new Dictionary<string, string> { ["from"] = "2026-10-20", ["to"] = "2026-10-01" }, Today, out _), "from");
            StringAssert.Contains(DrillBinder.Bind(month.Parameters, new Dictionary<string, string> { ["from"] = "not a date" }, Today, out _), "from");

            // the month so far: the first day of the Persian month up to today
            Assert.IsNull(DrillBinder.Bind(month.Parameters, new Dictionary<string, string>(), Today, out args));
            Assert.AreEqual(new DateTime(2026, 10, 23), args.Date("from"));
            Assert.AreEqual(Today, args.Date("to"));
            // the last twelve months start eleven Persian months back
            DrillDefinition revenue = CeoDrillCatalog.Find("fin.revenue_by_month");
            Assert.IsNull(DrillBinder.Bind(revenue.Parameters, new Dictionary<string, string>(), Today, out args));
            Assert.AreEqual(new DateTime(2025, 11, 22), args.Date("from"), "1404/09/01");
        }

        // ------------------------------------------------------------------------------------------ the service and the tools (no database is touched before an argument is refused)

        private static CeoDrillService Service(params string[] roles) => new CeoDrillService(new CeoOneService(), new CeoFakeUser(roles));

        [TestMethod]
        public async Task Service_Refuses_Other_Roles_Unknown_Keys_And_Bad_Parameters_With_A_Persian_Message()
        {
            var denied = await Service("WAREHOUSE").DrillAsync("qc.ncr_open", null, null, new Dictionary<string, string>(), CancellationToken.None);
            Assert.IsFalse(denied.Success);
            Assert.AreEqual(CeoText.NotAllowed, denied.Message);
            Assert.IsFalse((await Service("WAREHOUSE").GetCatalogueAsync(CancellationToken.None)).Success);

            var unknown = await Service("CEO").DrillAsync("qc.nothing", null, null, new Dictionary<string, string>(), CancellationToken.None);
            Assert.IsFalse(unknown.Success);
            Assert.AreEqual(CeoDrillText.UnknownKey, unknown.Message);
            Assert.AreEqual(CeoDrillText.KeyRequired, (await Service("CEO").DrillAsync("  ", null, null, null, CancellationToken.None)).Message);

            var badTop = await Service("PMO").DrillAsync("qc.ncr_open", "0", null, null, CancellationToken.None);
            Assert.IsFalse(badTop.Success);
            StringAssert.Contains(badTop.Message, "«تعداد»");
            var badPage = await Service("PMO").DrillAsync("qc.ncr_open", "5", "10001", null, CancellationToken.None);
            Assert.IsFalse(badPage.Success);
            StringAssert.Contains(badPage.Message, "«صفحه»");
            var badValue = await Service("PMO").DrillAsync("qc.ncr_open", "5", "1", new Dictionary<string, string> { ["severity"] = "Huge" }, CancellationToken.None);
            Assert.IsFalse(badValue.Success);
            StringAssert.Contains(badValue.Message, "severity");

            var catalogue = await Service("PMO").GetCatalogueAsync(CancellationToken.None);
            Assert.IsTrue(catalogue.Success);
            Assert.AreEqual(68, catalogue.Data.Count);
        }

        [TestMethod]
        public async Task Tools_Of_The_Dashboard_Are_For_CEO_And_PMO_And_An_Unknown_Key_Is_Refused()
        {
            foreach (AiToolDefinition t in AiCeoTools.Build())
            {
                Assert.AreEqual("CEO,PMO", t.RequiredRoles, t.Name);
                Assert.IsNotNull(JsonNode.Parse(t.SchemaJson), t.Name);
                Assert.IsTrue(HasPersian(t.Description), t.Name);
            }
            CollectionAssert.AreEquivalent(new[] { "ceo_overview", "ceo_drills", "ceo_drill" }, AiCeoTools.Build().Select(t => t.Name).ToList());
            JsonNode drillSchema = JsonNode.Parse(AiCeoTools.Build().Single(t => t.Name == "ceo_drill").SchemaJson);
            Assert.AreEqual("key", (string)drillSchema["required"][0]);
            foreach (string name in AiCeoTools.ParameterSchema.Keys) Assert.IsNotNull(drillSchema["properties"][name], name);

            var ceo = new CeoFakeUser("CEO");
            var registry = new ToolRegistry(new CeoOneService(typeof(ICeoDrillService), new CeoDrillService(new CeoOneService(), ceo)), ceo);
            AiToolResult unknown = await registry.ExecuteAsync("ceo_drill", "{\"key\":\"no.such_drill\"}", CancellationToken.None);
            Assert.IsFalse(unknown.Allowed);
            Assert.AreEqual("{\"error\":true,\"message\":\"" + CeoDrillText.UnknownKey + "\"}", unknown.Json);

            var storekeeper = new CeoFakeUser("WAREHOUSE");
            var denied = await new ToolRegistry(new CeoOneService(), storekeeper).ExecuteAsync("ceo_drill", "{\"key\":\"qc.ncr_open\"}", CancellationToken.None);
            Assert.IsFalse(denied.Allowed);
            StringAssert.Contains(denied.Json, "\"forbidden\":true");
            var dashboardTools = new[] { AiCeoTools.OverviewTool, AiCeoTools.DrillsTool, AiCeoTools.DrillTool };
            Assert.IsFalse(new ToolRegistry(new CeoOneService(), storekeeper).Offered().Any(t => dashboardTools.Contains(t.Name)));
            Assert.AreEqual(3, new ToolRegistry(new CeoOneService(), new CeoFakeUser("PMO")).Offered().Count(t => dashboardTools.Contains(t.Name)));
            Assert.AreEqual(3, new ToolRegistry(new CeoOneService(), ceo).Offered().Count(t => dashboardTools.Contains(t.Name)));
        }

        [TestMethod]
        public void A_Drill_Result_Is_Trimmed_Like_Any_Tool_Result_And_Keeps_Its_Total()
        {
            var rows = Enumerable.Range(1, 120).Select(i => new Dictionary<string, object>
            {
                ["id"] = i, ["number"] = "NCR-" + i, ["title"] = new string('ب', 120), ["note"] = new string('م', 200), ["link"] = "/inventory/ncr/" + i,
            }).ToList();
            var data = new Dictionary<string, object> { ["key"] = "qc.ncr_open", ["title"] = "x", ["totalCount"] = 120, ["page"] = 1, ["pageSize"] = 50, ["items"] = rows };
            string json = AiToolShaper.Fit(AiToolShaper.Clean(AiToolShaper.ToNode(data)), new List<AiCitationDto>(), new AiToolDefinition { Name = "ceo_drill" });
            Assert.IsTrue(json.Length <= AiToolShaper.MaxChars, "length " + json.Length);
            JsonNode node = JsonNode.Parse(json);
            Assert.AreEqual(120, (int)node["totalCount"]);
            Assert.IsTrue(node["items"].AsArray().Count <= AiToolShaper.ListLimit);
            Assert.AreEqual("NCR-1", (string)node["items"][0]["number"]);
        }

        [TestMethod]
        public void A_Row_With_An_Id_And_A_Link_Becomes_A_Citation()
        {
            DrillDefinition ncr = CeoDrillCatalog.Find("qc.ncr_open");
            var row = new Dictionary<string, object> { ["id"] = 7, ["number"] = "NCR-0007", ["link"] = "/inventory/ncr/7" };
            string title = AiCeoTools.RowCitation(ncr, row, out string id, out string link);
            Assert.AreEqual("7", id);
            Assert.AreEqual("/inventory/ncr/7", link);
            StringAssert.StartsWith(title, ncr.RecordLabel);
            StringAssert.Contains(title, "NCR-0007");

            Assert.IsNull(AiCeoTools.RowCitation(ncr, new Dictionary<string, object> { ["id"] = 7, ["number"] = "NCR-0007" }, out _, out _), "no link, no citation");
            Assert.IsNull(AiCeoTools.RowCitation(ncr, new Dictionary<string, object> { ["number"] = "NCR-0007", ["link"] = "/x" }, out _, out _), "no id, no citation");
            Assert.IsNull(AiCeoTools.RowCitation(ncr, null, out _, out _));

            string named = AiCeoTools.RowCitation(ncr, new Dictionary<string, object> { ["id"] = 8, ["number"] = "NCR-8", ["title"] = "عیب جوش", ["link"] = "/inventory/ncr/8" }, out _, out _);
            StringAssert.Contains(named, "عیب جوش");
        }

        [TestMethod]
        public void The_Analysis_Prompt_Is_Seeded_And_Mentions_The_Numbers_And_The_Tools()
        {
            Assert.AreEqual(AiChatService.AnalysisSeedPromptText, AiChatService.SeedTextFor(AiChatService.AnalysisPromptCode));
            Assert.AreEqual(AiChatService.SeedPromptText, AiChatService.SeedTextFor(AiChatService.ChatPromptCode));
            Assert.IsNull(AiChatService.SeedTextFor("OTHER"));
            Assert.AreEqual("ANALYSIS", AiChatService.AnalysisPromptCode);
            StringAssert.Contains(AiChatService.AnalysisSeedPromptText, "ceo_overview");
            StringAssert.Contains(AiChatService.AnalysisSeedPromptText, "ceo_drill");
            Assert.IsTrue(HasPersian(AiChatService.AnalysisSeedPromptText));
        }
    }
}
