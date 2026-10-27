using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.Office;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ceo.Drills;

namespace MsTests
{
    /// <summary>
    /// The attendance drill of the CEO dashboard (of.attendance_month) for a role that may not read people (the PMO): a day or a unit with fewer than three people shows counts only,
    /// the units that are too small are added together until the group holds three people (or are dropped into the totals), and the minutes of delay and the hours worked of such a group
    /// are never shown (they would be the figures of one or two persons). A role that reads people (HR, CEO) sees everything.
    /// </summary>
    [TestClass]
    public class CeoAttendancePrivacyTests
    {
        private const string Others = "سایر واحدها";

        private static OfficeDrills.AttendanceCounts Counts(int employees, int present = 0, int absent = 0, int late = 0, int lateMinutes = 0, int workedMinutes = 0, int leave = 0, int mission = 0, int rest = 0, int incomplete = 0, DateTime? date = null) =>
            new OfficeDrills.AttendanceCounts
            {
                Employees = employees, Present = present, Absent = absent, Leave = leave, Mission = mission, Rest = rest, Incomplete = incomplete,
                Late = late, LateMinutes = lateMinutes, WorkedMinutes = workedMinutes, Date = date ?? default,
            };

        private static int Int(Row row, string key) => (int)row[key];

        private static string Label(Row row) => (string)row["label"];

        private static bool CountsOnly(Row row) => row["lateMinutes"] == null && row["workedHours"] == null;

        // ------------------------------------------------------------------------------------------ a row of counts

        [TestMethod]
        public void A_Row_Without_Detail_Keeps_Every_Count_And_Has_No_Minutes_And_No_Hours()
        {
            OfficeDrills.AttendanceCounts counts = Counts(2, present: 3, absent: 1, leave: 1, late: 2, lateMinutes: 35, workedMinutes: 1500, rest: 4);
            Row detailed = OfficeDrills.Summary("x", counts, detail: true);
            Row plain = OfficeDrills.Summary("x", counts, detail: false);

            Assert.AreEqual(35, detailed["lateMinutes"]);
            Assert.AreEqual(25m, detailed["workedHours"]);
            Assert.IsNull(plain["lateMinutes"]);
            Assert.IsNull(plain["workedHours"]);
            Assert.IsTrue(plain.ContainsKey("lateMinutes") && plain.ContainsKey("workedHours"), "the columns are there, empty");
            foreach (string key in new[] { "label", "employees", "present", "absent", "leave", "mission", "rest", "incomplete", "late", "presentPercent", "link" })
                Assert.AreEqual(detailed[key], plain[key], key);
            // present / (present + absent + leave + mission + incomplete): a rest day is not a working day
            Assert.AreEqual(60m, plain["presentPercent"]);
            Assert.IsNull(OfficeDrills.Summary("x", Counts(5, rest: 5), detail: false)["presentPercent"], "no working day, no percent");
        }

        [TestMethod]
        public void The_Counts_Of_Day_Facts_Count_Different_People_And_Add_Up()
        {
            DateTime d1 = new DateTime(2026, 10, 1), d2 = d1.AddDays(1), d3 = d1.AddDays(2);
            OfficeDrills.DayFact Fact(int employee, DateTime date, OfDayStatus status, int delay, int worked) =>
                new OfficeDrills.DayFact { EmployeeId = employee, Date = date, Status = status, DelayMinutes = delay, WorkedMinutes = worked };
            var facts = new[]
            {
                Fact(1, d1, OfDayStatus.Present, 10, 480), Fact(1, d2, OfDayStatus.Absent, 0, 0), Fact(2, d1, OfDayStatus.Present, 0, 500), Fact(2, d2, OfDayStatus.Rest, 0, 0),
                Fact(3, d1, OfDayStatus.Leave, 0, 0), Fact(3, d2, OfDayStatus.Mission, 0, 0), Fact(3, d3, OfDayStatus.Incomplete, 5, 100),
            };
            OfficeDrills.AttendanceCounts all = OfficeDrills.AttendanceCounts.Of(facts);
            Assert.AreEqual(3, all.Employees);
            Assert.AreEqual(2, all.Present);
            Assert.AreEqual(1, all.Absent);
            Assert.AreEqual(1, all.Leave);
            Assert.AreEqual(1, all.Mission);
            Assert.AreEqual(1, all.Rest);
            Assert.AreEqual(1, all.Incomplete);
            Assert.AreEqual(2, all.Late);
            Assert.AreEqual(15, all.LateMinutes);
            Assert.AreEqual(1080, all.WorkedMinutes);

            OfficeDrills.AttendanceCounts sum = OfficeDrills.AttendanceCounts.Of(facts.Where(f => f.EmployeeId == 1)).Plus(OfficeDrills.AttendanceCounts.Of(facts.Where(f => f.EmployeeId != 1)));
            Assert.AreEqual(all.Employees, sum.Employees);
            Assert.AreEqual(all.Present, sum.Present);
            Assert.AreEqual(all.LateMinutes, sum.LateMinutes);
            Assert.AreEqual(all.WorkedMinutes, sum.WorkedMinutes);
        }

        [TestMethod]
        public void Minutes_And_Hours_Are_Shown_From_Three_People_Or_To_A_Role_That_Reads_People()
        {
            Assert.IsFalse(OfficeDrills.ShowDetail(Counts(0), false));
            Assert.IsFalse(OfficeDrills.ShowDetail(Counts(1), false));
            Assert.IsFalse(OfficeDrills.ShowDetail(Counts(2), false));
            Assert.IsTrue(OfficeDrills.ShowDetail(Counts(3), false));
            Assert.IsTrue(OfficeDrills.ShowDetail(Counts(40), false));
            foreach (int employees in new[] { 0, 1, 2, 3, 40 }) Assert.IsTrue(OfficeDrills.ShowDetail(Counts(employees), true), "HR and the CEO read people");
        }

        // ------------------------------------------------------------------------------------------ the day view

        [TestMethod]
        public void A_Day_With_Fewer_Than_Three_People_Shows_Counts_Only_To_A_Role_That_Cannot_Read_People()
        {
            DateTime d1 = new DateTime(2026, 10, 1), d2 = d1.AddDays(1), d3 = d1.AddDays(2), d4 = d1.AddDays(3);
            var days = new[]
            {
                Counts(2, present: 2, late: 1, lateMinutes: 25, workedMinutes: 960, date: d3),
                Counts(3, present: 3, late: 2, lateMinutes: 40, workedMinutes: 1440, date: d1),
                Counts(1, present: 1, late: 1, lateMinutes: 12, workedMinutes: 480, date: d2),
                Counts(0, rest: 0, date: d4),
            };

            List<Row> rows = OfficeDrills.DayRows(days, people: false);
            CollectionAssert.AreEqual(new[] { d1, d2, d3, d4 }.Select(d => DrillDates.Text((DateTime?)d)).ToList(), rows.Select(Label).ToList(), "oldest day first");
            Assert.AreEqual(40, rows[0]["lateMinutes"], "three people: the detail is shown");
            Assert.AreEqual(24m, rows[0]["workedHours"]);
            Assert.IsTrue(CountsOnly(rows[1]), "one person");
            Assert.IsTrue(CountsOnly(rows[2]), "two people");
            Assert.IsTrue(CountsOnly(rows[3]), "nobody");
            // the counts of the small days are there
            Assert.AreEqual(1, Int(rows[1], "employees"));
            Assert.AreEqual(1, Int(rows[1], "present"));
            Assert.AreEqual(1, Int(rows[1], "late"));
            Assert.AreEqual(2, Int(rows[2], "employees"));
            Assert.AreEqual(2, Int(rows[2], "present"));

            // a role that reads people sees the minutes and the hours of every day
            List<Row> everything = OfficeDrills.DayRows(days, people: true);
            Assert.IsFalse(everything.Any(CountsOnly));
            Assert.AreEqual(12, everything[1]["lateMinutes"]);
            Assert.AreEqual(8m, everything[1]["workedHours"]);
        }

        // ------------------------------------------------------------------------------------------ the unit view

        [TestMethod]
        public void Units_Of_Fewer_Than_Three_People_Are_Added_Together_And_Shown_With_The_Group_Of_Others()
        {
            var units = new List<(string, OfficeDrills.AttendanceCounts)>
            {
                ("A", Counts(5, present: 20, absent: 3, late: 3, lateMinutes: 90, workedMinutes: 9000)),
                ("B", Counts(1, present: 4, absent: 1, late: 1, lateMinutes: 7, workedMinutes: 1900)),
                ("C", Counts(2, present: 8, absent: 1, late: 1, lateMinutes: 15, workedMinutes: 3800)),
                ("D", Counts(4, present: 16, late: 1, lateMinutes: 10, workedMinutes: 7600)),
            };

            List<Row> rows = OfficeDrills.UnitRows(units, people: false);
            // B and C (1 + 2 people) form the group of others; the order is by absent days plus days with delay, the most first
            CollectionAssert.AreEqual(new[] { "A", Others, "D" }, rows.Select(Label).ToList());
            Row others = rows.Single(r => Label(r) == Others);
            Assert.AreEqual(3, Int(others, "employees"));
            Assert.AreEqual(12, Int(others, "present"));
            Assert.AreEqual(2, Int(others, "absent"));
            Assert.AreEqual(2, Int(others, "late"));
            Assert.AreEqual(22, others["lateMinutes"], "three people together: the detail is shown");
            Assert.AreEqual(95m, others["workedHours"]);
            Assert.IsFalse(rows.Any(CountsOnly));
            Assert.IsFalse(rows.Any(r => Int(r, "employees") < 3), "no row stands for fewer than three people");
            Assert.AreEqual(units.Sum(u => u.Item2.Present), rows.Sum(r => Int(r, "present")), "nobody is lost");

            // a role that reads people sees every unit on its own
            List<Row> own = OfficeDrills.UnitRows(units, people: true);
            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D" }, own.Select(Label).ToList(), "A has 6 days of absence or delay, B and C 2 each (label order), D 1");
            Assert.IsFalse(own.Any(r => Label(r) == Others));
            Assert.IsFalse(own.Any(CountsOnly));
            Assert.AreEqual(1, Int(own.Single(r => Label(r) == "B"), "employees"));
            Assert.AreEqual(7, own.Single(r => Label(r) == "B")["lateMinutes"]);
        }

        [TestMethod]
        public void The_Group_Of_Others_Takes_The_Smallest_Unit_Until_It_Holds_Three_People()
        {
            // two units of one person are two people: the smallest big unit joins them
            var units = new List<(string, OfficeDrills.AttendanceCounts)>
            {
                ("A", Counts(5, present: 20)), ("B", Counts(1, present: 4)), ("C", Counts(1, present: 5)), ("E", Counts(9, present: 30)),
            };
            List<Row> rows = OfficeDrills.UnitRows(units, people: false);
            CollectionAssert.AreEquivalent(new[] { Others, "E" }, rows.Select(Label).ToList());
            Row others = rows.Single(r => Label(r) == Others);
            Assert.AreEqual(7, Int(others, "employees"), "B and C and the smaller of the big units (A)");
            Assert.AreEqual(29, Int(others, "present"));
            Assert.AreEqual(30, Int(rows.Single(r => Label(r) == "E"), "present"));

            // only small units and the whole group is still under three people: nothing can be shown, it stays in the totals
            Assert.AreEqual(0, OfficeDrills.UnitRows(new List<(string, OfficeDrills.AttendanceCounts)> { ("A", Counts(1, present: 3)), ("B", Counts(1, present: 2)) }, people: false).Count);
            Assert.AreEqual(0, OfficeDrills.UnitRows(new List<(string, OfficeDrills.AttendanceCounts)> { ("A", Counts(2, present: 3)) }, people: false).Count);
            Assert.AreEqual(0, OfficeDrills.UnitRows(new List<(string, OfficeDrills.AttendanceCounts)>(), people: false).Count);

            // three single-person units reach three people together and are shown as one group
            List<Row> three = OfficeDrills.UnitRows(new List<(string, OfficeDrills.AttendanceCounts)> { ("A", Counts(1, present: 1)), ("B", Counts(1, present: 1)), ("C", Counts(1, present: 1)) }, people: false);
            Assert.AreEqual(1, three.Count);
            Assert.AreEqual(Others, Label(three[0]));
            Assert.AreEqual(3, Int(three[0], "employees"));

            // no small unit, no group of others
            List<Row> big = OfficeDrills.UnitRows(new List<(string, OfficeDrills.AttendanceCounts)> { ("A", Counts(3, present: 3)), ("B", Counts(4, present: 4)) }, people: false);
            CollectionAssert.AreEquivalent(new[] { "A", "B" }, big.Select(Label).ToList());

            // the units of people: all shown, even the dropped case of the role that may not see them
            Assert.AreEqual(2, OfficeDrills.UnitRows(new List<(string, OfficeDrills.AttendanceCounts)> { ("A", Counts(1, present: 3)), ("B", Counts(1, present: 2)) }, people: true).Count);
        }

        [TestMethod]
        public void Whatever_The_Units_No_Row_Of_A_Role_That_Cannot_Read_People_Stands_For_Fewer_Than_Three_People_And_Nobody_Is_Lost_Unless_The_Whole_Company_Is_That_Small()
        {
            var random = new Random(20261025);
            for (int round = 0; round < 300; round++)
            {
                int unitCount = random.Next(0, 9);
                var units = new List<(string, OfficeDrills.AttendanceCounts)>();
                for (int i = 0; i < unitCount; i++)
                {
                    int people = random.Next(1, 7);
                    units.Add(("U" + i, Counts(people, present: random.Next(0, 30), absent: random.Next(0, 5), late: random.Next(0, 5), lateMinutes: random.Next(0, 200), workedMinutes: random.Next(0, 9000))));
                }
                int everybody = units.Sum(u => u.Item2.Employees);

                List<Row> rows = OfficeDrills.UnitRows(units, people: false);
                foreach (Row row in rows)
                {
                    Assert.IsTrue(Int(row, "employees") >= 3, "round " + round + ": " + Label(row) + " has " + Int(row, "employees") + " people");
                    Assert.IsFalse(CountsOnly(row), "round " + round + ": a row of three people or more carries the detail");
                }
                Assert.IsTrue(rows.Count(r => Label(r) == Others) <= 1, "round " + round + ": the group of others appears at most once");
                if (everybody >= 3)
                {
                    Assert.AreEqual(everybody, rows.Sum(r => Int(r, "employees")), "round " + round + ": nobody is lost");
                    Assert.AreEqual(units.Sum(u => u.Item2.Present), rows.Sum(r => Int(r, "present")), "round " + round);
                    Assert.AreEqual(units.Sum(u => u.Item2.Late), rows.Sum(r => Int(r, "late")), "round " + round);
                }
                else Assert.AreEqual(0, rows.Count, "round " + round + ": the whole company is under three people");

                // the role that reads people sees each unit once with its own detail
                List<Row> own = OfficeDrills.UnitRows(units, people: true);
                Assert.AreEqual(units.Count, own.Count, "round " + round);
                Assert.IsFalse(own.Any(CountsOnly), "round " + round + ": a role that reads people sees the detail of every unit");
            }
        }
    }
}
