using System;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Finance;

namespace MsTests
{
    /// <summary>Fiscal years of the finance module follow the Persian calendar of .NET; these dates are the agreed reference points.</summary>
    [TestClass]
    public class PersianCalendarTests
    {
        [TestMethod]
        public void Year_1405_Starts_On_2026_03_21()
        {
            Assert.AreEqual(new DateTime(2026, 3, 21), new PersianCalendar().ToDateTime(1405, 1, 1, 0, 0, 0, 0).Date);
            Assert.AreEqual(new DateTime(2026, 3, 21), FinanceSeed.YearStart(1405));
        }

        [TestMethod]
        public void Month_7_Of_1405_Starts_On_2026_09_23()
        {
            Assert.AreEqual(new DateTime(2026, 9, 23), new PersianCalendar().ToDateTime(1405, 7, 1, 0, 0, 0, 0).Date);
            Assert.AreEqual(new DateTime(2026, 9, 23), FinanceSeed.PersianDate(1405, 7, 1));
        }

        [TestMethod]
        public void Year_1405_Ends_On_2027_03_20()
        {
            Assert.AreEqual(new DateTime(2027, 3, 20), FinanceSeed.YearEnd(1405));
            var calendar = new PersianCalendar();
            Assert.AreEqual(1405, calendar.GetYear(new DateTime(2027, 3, 20)));
            Assert.AreEqual(1406, calendar.GetYear(new DateTime(2027, 3, 21)));
        }

        [TestMethod]
        public void First_Six_Months_Have_31_Days_And_The_Next_Five_Have_30()
        {
            var calendar = new PersianCalendar();
            for (int month = 1; month <= 6; month++) Assert.AreEqual(31, calendar.GetDaysInMonth(1405, month));
            for (int month = 7; month <= 11; month++) Assert.AreEqual(30, calendar.GetDaysInMonth(1405, month));
        }
    }
}
