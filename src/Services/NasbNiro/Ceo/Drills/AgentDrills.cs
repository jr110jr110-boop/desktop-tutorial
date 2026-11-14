using System;
using System.Collections.Generic;
using System.Linq;
using Services.NasbNiro.Agents;
using ViewModels.NasbNiro.AgentDtos;
using static Services.NasbNiro.Ceo.Drills.DrillText;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>The drills of the agent layer: the master board (per contract and station) and what is stuck. Both go through the board service of the layer (PMO, CEO and the auditor read it).</summary>
    internal static class AgentDrills
    {
        private const string M = CeoModules.Ai;

        public static IEnumerable<DrillDefinition> Build()
        {
            yield return Board();
            yield return Stuck();
        }

        private static string CellText(AgentCellStatus status) => status switch
        {
            AgentCellStatus.Idle => "بدون کار باز",
            AgentCellStatus.Agent => "در دست ایجنت",
            AgentCellStatus.WaitingInput => "منتظر ورود اطلاعات",
            AgentCellStatus.WaitingPhysical => "منتظر کار فیزیکی",
            AgentCellStatus.WaitingGate => "منتظر تصمیم انسان",
            AgentCellStatus.Failed => "ناموفق",
            _ => "شروع نشده",
        };

        private static string StuckText(AgentStuckReason reason) => reason switch
        {
            AgentStuckReason.Failed => "ناموفق",
            AgentStuckReason.GateOverdue => "از مهلت گذشته",
            _ => "ایجنت خاموش است؛ انسان انجام می‌دهد",
        };

        private static DrillDefinition Board() => new DrillDefinition
        {
            Key = "ag.board", Module = M, Title = "نمای ایجنت اصلی: وضعیت ایستگاه‌ها به تفکیک قرارداد",
            Description = "برای هر قرارداد و هر یک از ۱۳ ایستگاه فرایند، کارهای باز ایجنت‌های هوش مصنوعی: در دست ایجنت، منتظر تصمیم انسان (با نقش منتظر و مدت انتظار)، منتظر کار فیزیکی یا ورود اطلاعات، یا ناموفق. فقط ایستگاه‌هایی می‌آیند که کار باز دارند. «بدون کار باز» یعنی کار بازی نیست، نه اینکه ایستگاه کامل شده است.",
            Link = "/agents/board", RecordType = "AgentTask", RecordLabel = "کار ایجنت",
            Columns = new[]
            {
                Col.Text("contract", "قرارداد"), Col.Text("station", "ایستگاه"), Col.Status("status", "وضعیت"), Col.Text("waitingOn", "منتظر نقش"), Col.Num("openTasks", "کارهای باز"),
                Col.Num("ageHours", "ساعت انتظار"), Col.Text("title", "قدیمی‌ترین کار"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                AgentBoardDto board = Take(await c.Get<IAgentBoardService>().GetBoardAsync(null, ct));
                var rows = new List<Row>();
                foreach (AgentBoardRowDto r in board.Rows)
                    foreach (AgentBoardCellDto cell in r.Cells.Where(x => x.Status >= AgentCellStatus.Agent))
                        rows.Add(new Row("/agents/board")
                        {
                            ["id"] = cell.TaskId, ["contract"] = r.Label, ["station"] = AgentStations.Title(cell.Station), ["status"] = CellText(cell.Status), ["waitingOn"] = cell.WaitingOn,
                            ["openTasks"] = cell.OpenCount, ["ageHours"] = cell.OldestHours, ["title"] = Short(cell.Title, 120),
                        });
                List<Row> ordered = rows.OrderByDescending(x => (int)x["ageHours"]).ToList();
                return DrillResult.Slice(ordered, c, "وضعیت هر ایستگاه بدترین کار بازِ آن است؛ مدت انتظار از ساخت قدیمی‌ترین کار باز آن ایستگاه شمرده می‌شود.", new Dictionary<string, object>
                {
                    ["cells"] = rows.Count, ["stuck"] = board.Stuck.Count,
                });
            },
        };

        private static DrillDefinition Stuck() => new DrillDefinition
        {
            Key = "ag.stuck", Module = M, Title = "کارهای گیرکرده‌ی ایجنت‌ها",
            Description = "کارهایی که ایجنت‌ها انجامشان نداده‌اند و نیاز به توجه دارند: کار ناموفق (با پیام خطا)، گیت یا کار انسانی که از مهلتش گذشته و ایجنت‌های خاموشی که کارشان به انسان سپرده شده است؛ ابتدا ناموفق‌ها و بعد قدیمی‌ترین‌ها.",
            Link = "/agents/board", RecordType = "AgentTask", RecordLabel = "کار ایجنت",
            Columns = new[]
            {
                Col.Status("reason", "علت"), Col.Text("title", "کار"), Col.Text("station", "ایستگاه"), Col.Text("contract", "قرارداد"), Col.Text("waitingOn", "منتظر نقش"),
                Col.Num("ageHours", "ساعت انتظار"), Col.Text("error", "خطا"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                AgentBoardDto board = Take(await c.Get<IAgentBoardService>().GetBoardAsync(null, ct));
                List<Row> rows = board.Stuck.Select(s => new Row(string.IsNullOrWhiteSpace(s.Link) ? "/agents/board" : s.Link)
                {
                    ["id"] = s.TaskId, ["reason"] = StuckText(s.Reason), ["title"] = Short(s.Title, 120), ["station"] = s.StationTitle, ["contract"] = s.ContractLabel, ["waitingOn"] = s.WaitingOn,
                    ["ageHours"] = s.AgeHours, ["error"] = Short(s.Error, 160),
                }).ToList();
                return DrillResult.Slice(rows, c, "ایجنت هرگز تایید یا امضا نمی‌کند؛ کار گیرکرده یا ناموفق را PMO در نمای ایجنت اصلی دوباره اجرا می‌کند.", new Dictionary<string, object>
                {
                    ["stuck"] = board.Stuck.Count, ["failed"] = board.Stuck.Count(s => s.Reason == AgentStuckReason.Failed), ["overdue"] = board.Stuck.Count(s => s.Reason == AgentStuckReason.GateOverdue),
                    ["agentOff"] = board.Stuck.Count(s => s.Reason == AgentStuckReason.AgentOff),
                });
            },
        };
    }
}
