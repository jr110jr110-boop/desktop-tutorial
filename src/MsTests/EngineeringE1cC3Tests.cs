using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Entities.Model.NasbNiro.Engineering;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EngineeringHandoverTests;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1c: C3 scope note.</summary>
    [TestClass]
    public class EngineeringE1cC3Tests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        [TestMethod]
        public void C3_Scope_Note_Marks_The_Documents_That_Are_Not_There_Yet()
        {
            DateTimeOffset due = new DateTimeOffset(2026, 11, 20, 14, 30, 0, DateTimeOffset.Now.Offset);
            List<EnHandoverLine> lines = new List<EnHandoverLine>
            {
                new EnHandoverLine { Id = 1, Seq = 1, Title = "نقشه مونتاژ", State = EnLineState.Available, Units = "Qc" },
                new EnHandoverLine { Id = 2, Seq = 2, Title = "برگه اطلاعات", State = EnLineState.Missing, Units = "Qc" },
                new EnHandoverLine { Id = 3, Seq = 3, Title = "نقشه لوله‌کشی", State = EnLineState.Missing, Units = "Qc" },
            };
            string note = EnHandoverRules.ScopeNote("ENGZ-K", "ENG-1", "API 612", EnUnit.Qc, lines, out int count, new Dictionary<int, DateTimeOffset> { [3] = due });
            Assert.AreEqual(3, count);
            StringAssert.Contains(note, "نقشه مونتاژ؛");
            Assert.IsFalse(note.Contains("نقشه مونتاژ ("), "a document that is there stays as it is");
            StringAssert.Contains(note, "برگه اطلاعات (هنوز نرسیده)؛");
            StringAssert.Contains(note, "نقشه لوله‌کشی (هنوز نرسیده، مهلت تا " + Services.NasbNiro.Engineering.EnText.Stamp(due) + ")");
        }

    }
}
