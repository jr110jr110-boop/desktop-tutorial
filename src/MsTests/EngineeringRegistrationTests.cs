using System;
using System.Collections.Generic;
using System.Linq;
using DataLayer.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1 S7: every En* entity is in the audit trail map (the nightly hash chain reads it) with columns that really exist, every decision column is listed and guarded against an AI seat, and no DTO class name is used twice.</summary>
    [TestClass]
    public class EngineeringRegistrationTests
    {
        private static List<Type> EnTypes(AppHost host) =>
            host.Model.GetEntityTypes().Select(t => t.ClrType).Where(t => t != null && t.Namespace == "Entities.Model.NasbNiro.Engineering" && t.Name.StartsWith("En", StringComparison.Ordinal)).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

        [TestMethod]
        public void Every_En_Entity_Is_In_The_Audit_Trail_With_Real_Columns_And_Every_Decision_Column_Is_Listed_And_Guarded()
        {
            AppHost host = NewHost("reg");
            var audited = (Dictionary<Type, string[]>)typeof(ApplicationDbContext).Assembly.GetType("DataLayer.Context.FinanceAuditTrail")
                .GetField("Audited", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            List<Type> types = EnTypes(host);
            Assert.IsTrue(types.Count >= 13, "the En entities found: " + types.Count);
            var guarded = SeatApprovalGuard.Sweep(host.Model);
            foreach (Type type in types)
            {
                Assert.IsTrue(audited.ContainsKey(type), type.Name + " is in the audit trail map");
                var entity = host.Model.FindEntityType(type);
                foreach (string field in audited[type]) Assert.IsNotNull(entity.FindProperty(field), type.Name + "." + field + " is audited but is not a column");
                foreach (var p in entity.GetProperties().Where(p => (p.ClrType == typeof(int) || p.ClrType == typeof(int?)) && SeatApprovalGuard.MatchesName(p.Name)))
                {
                    Assert.IsTrue(audited[type].Contains(p.Name), type.Name + "." + p.Name + " is a decision column and is audited");
                    Assert.IsTrue(guarded.Contains((type.Name, p.Name)), type.Name + "." + p.Name + " is guarded against an AI seat");
                }
            }
        }

        [TestMethod]
        public void Every_Column_Of_An_En_Entity_That_Records_A_Person_Or_A_Content_Is_Audited()
        {
            AppHost host = NewHost("reg2");
            var audited = (Dictionary<Type, string[]>)typeof(ApplicationDbContext).Assembly.GetType("DataLayer.Context.FinanceAuditTrail")
                .GetField("Audited", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            var missing = new List<string>();
            foreach (Type type in EnTypes(host))
                foreach (var p in host.Model.FindEntityType(type).GetProperties())
                {
                    if (p.IsPrimaryKey() || p.Name == "RowVersion" || p.Name == "CreateDate" || p.Name == "CreatorUserId" || p.Name == "ModifyDate" || p.Name == "ModifierUserId") continue;
                    if (!audited[type].Contains(p.Name)) missing.Add(type.Name + "." + p.Name);
                }
            Assert.AreEqual(0, missing.Count, "audited columns are missing: " + string.Join(", ", missing));
        }

        [TestMethod]
        public void No_Dto_Class_Name_Of_The_Engineering_Module_Is_Used_Twice_In_The_Application()
        {
            var names = typeof(ViewModels.NasbNiro.EngineeringDtos.EnCaseDto).Assembly.GetTypes().Where(t => t.IsClass && t.Namespace != null && t.Namespace.StartsWith("ViewModels", StringComparison.Ordinal)).GroupBy(t => t.Name).Where(g => g.Count() > 1 && g.Key.StartsWith("En", StringComparison.Ordinal)).Select(g => g.Key).ToList();
            Assert.AreEqual(0, names.Count, string.Join(", ", names));
        }
    }
}
