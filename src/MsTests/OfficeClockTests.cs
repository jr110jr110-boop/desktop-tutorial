using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Office;

namespace MsTests
{
    /// <summary>Employer rule 14: office hours 08:00-19:00 Iran time (UTC+03:30), Saturday to Thursday.</summary>
    [TestClass]
    public class OfficeClockTests
    {
        private static readonly TimeSpan Iran = new TimeSpan(3, 30, 0);
        // 2026-11-21 is a Saturday.
        private static DateTimeOffset At(int day, int hour, int minute = 0) => new DateTimeOffset(2026, 11, day, hour, minute, 0, Iran);

        [TestMethod]
        public void Dates_Used_Here_Are_The_Expected_Weekdays()
        {
            Assert.AreEqual(DayOfWeek.Saturday, At(21, 8).DayOfWeek);
            Assert.AreEqual(DayOfWeek.Thursday, At(26, 8).DayOfWeek);
            Assert.AreEqual(DayOfWeek.Friday, At(27, 8).DayOfWeek);
        }

        [TestMethod]
        public void Inside_Office_Time_Counts_Directly()
        {
            Assert.AreEqual(At(21, 19), OfficeClock.Add(At(21, 8), 11));
            Assert.AreEqual(At(22, 9), OfficeClock.Add(At(21, 8), 12));
            Assert.AreEqual(At(21, 10), OfficeClock.Add(At(21, 8), 2));
        }

        [TestMethod]
        public void Friday_Is_Skipped()
        {
            Assert.AreEqual(At(28, 9), OfficeClock.Add(At(26, 18), 2));   // Thursday 18:00 + 2 h = Saturday 09:00
            Assert.AreEqual(At(28, 9), OfficeClock.Add(At(27, 12), 1));   // Friday noon + 1 h
        }

        [TestMethod]
        public void Start_Outside_Office_Time_Begins_At_The_Next_Opening()
        {
            Assert.AreEqual(At(22, 9), OfficeClock.Add(At(21, 22), 1));
            Assert.AreEqual(At(21, 9), OfficeClock.Add(At(21, 5), 1));
            Assert.AreEqual(At(28, 9), OfficeClock.Add(At(26, 22), 1));
        }

        [TestMethod]
        public void Seventy_Two_Office_Hours_Are_Six_Days_And_Six_Hours()
        {
            Assert.AreEqual(72, OfficeClock.StopLimitHours);
            Assert.AreEqual(At(28, 14), OfficeClock.Add(At(21, 8), 72));
        }

        [TestMethod]
        public void Zero_Or_Negative_Hours_Return_The_Start()
        {
            DateTimeOffset s = At(27, 3);
            Assert.AreEqual(s, OfficeClock.Add(s, 0));
            Assert.AreEqual(s, OfficeClock.Add(s, -5));
        }

        [TestMethod]
        public void A_Start_In_Another_Offset_Is_Converted_And_The_Result_Keeps_The_Offset()
        {
            DateTimeOffset utcStart = At(26, 18).ToUniversalTime().ToOffset(TimeSpan.Zero);   // Thursday 18:00 Iran = 14:30 UTC
            DateTimeOffset r = OfficeClock.Add(utcStart, 2);
            Assert.AreEqual(TimeSpan.Zero, r.Offset);
            Assert.AreEqual(At(28, 9).ToUniversalTime(), r.ToUniversalTime());
        }

        [TestMethod]
        public void Between_Is_The_Inverse_Of_Add()
        {
            DateTimeOffset[] starts = { At(21, 8), At(26, 18), At(27, 12), At(21, 22), At(21, 5), At(23, 13, 30) };
            foreach (DateTimeOffset s in starts)
                foreach (int h in new[] { 1, 2, 11, 12, 30, 72 })
                    Assert.AreEqual(h, OfficeClock.Between(s, OfficeClock.Add(s, h)), 1e-6, $"{s:o} + {h}");
            Assert.AreEqual(0, OfficeClock.Between(At(21, 10), At(21, 9)));
            Assert.AreEqual(0, OfficeClock.Between(At(27, 9), At(27, 15)), "Friday only");
        }
    }
}
