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

        [TestMethod]
        public void Exact_Boundaries_Of_The_Day_And_The_Week()
        {
            Assert.AreEqual(At(22, 9), OfficeClock.Add(At(21, 19), 1), "starting exactly at closing time begins at the next opening");
            Assert.AreEqual(At(26, 19), OfficeClock.Add(At(26, 17), 2), "a result exactly at closing time stays on that day");
            Assert.AreEqual(At(28, 9), OfficeClock.Add(At(27, 7, 59), 1), "Friday 07:59");
            Assert.AreEqual(At(28, 9), OfficeClock.Add(At(27, 8), 1), "Friday 08:00");
            Assert.AreEqual(At(21, 9).AddSeconds(30), OfficeClock.Add(At(21, 8).AddSeconds(30), 1), "seconds are kept");
            Assert.AreEqual(At(26, 19), OfficeClock.Add(At(21, 8), 66), "a whole working week ends Thursday at closing time");
            Assert.AreEqual(At(28, 9), OfficeClock.Add(At(21, 8), 67));
        }

        [TestMethod]
        public void Between_Partial_Values()
        {
            Assert.AreEqual(1.5, OfficeClock.Between(At(26, 18), At(28, 8, 30)), 1e-9, "Thursday 18:00 to Saturday 08:30");
            Assert.AreEqual(1.0, OfficeClock.Between(At(26, 18), At(27, 10)), 1e-9, "ending inside a Friday");
            Assert.AreEqual(2.0, OfficeClock.Between(At(21, 18), At(22, 9)), 1e-9, "straddling midnight: 1 h before closing and 1 h after opening");
            Assert.AreEqual(0.0, OfficeClock.Between(At(21, 23), At(22, 1)), 1e-9, "only the night between");
            Assert.AreEqual(11.0, OfficeClock.Between(At(21, 0), At(21, 23)), 1e-9, "a whole day");
        }

        [TestMethod]
        public void Large_Inputs_Jump_Whole_Weeks_And_Stay_Consistent_With_Between()
        {
            foreach (int h in new[] { 66, 67, 132, 133, 5000, 66 * 100 })
                Assert.AreEqual(h, OfficeClock.Between(At(21, 8), OfficeClock.Add(At(21, 8), h)), 1e-6, "h=" + h);
            Assert.AreEqual(At(26, 19).AddDays(7), OfficeClock.Add(At(21, 8), 132));
            Assert.AreEqual(Iran, OfficeClock.Add(At(21, 8), OfficeClock.MaxHours).Offset);   // finishes and keeps the offset
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OfficeClock.Add(At(21, 8), OfficeClock.MaxHours + 1));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OfficeClock.Add(At(21, 8), int.MaxValue));
        }
    }
}
