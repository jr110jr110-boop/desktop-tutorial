using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Common.Results;
using Entities.Model.NasbNiro.Agents;

namespace Services.NasbNiro.Agents
{
    // Agent layer: the business processes are done by AI agents (one per specialty, each an AI seat) as the maker. People only decide at the gates, give what only a person has and do the physical work.
    // Agents work with fixed rules (no language model). An agent never approves: the data layer refuses it (SeatApprovalGuard) whatever a service forgot to check.

    /// <summary>The codes of the agent seats. They are normal seats of the AI module (AiSeatCatalog), so the user, the roles, the run rows, the audit and the failure alert come with them.</summary>
    public static class AgentCodes
    {
        /// <summary>The coordinator: reads everything, writes only the tables of the agent layer.</summary>
        public const string Master = "AG-MASTER";
        public const string Sales = "AG-SALES";
        public const string Contract = "AG-CONTRACT";
        public const string Init = "AG-INIT";
        public const string Plan = "AG-PLAN";
        public const string Eng = "AG-ENG";
        public const string Proc = "AG-PROC";
        public const string Wh = "AG-WH";
        public const string Mf = "AG-MF";
        public const string Qc = "AG-QC";
        public const string Ship = "AG-SHIP";
        public const string Service = "AG-SERVICE";
        public const string Fin = "AG-FIN";

        /// <summary>The twelve process agents, in the order of the stations they mainly serve.</summary>
        public static readonly IReadOnlyList<string> Process = new[] { Sales, Contract, Init, Plan, Eng, Proc, Wh, Mf, Qc, Ship, Service, Fin };

        public static bool IsAgent(string code) => !string.IsNullOrWhiteSpace(code) && code.Trim().StartsWith("AG-", StringComparison.OrdinalIgnoreCase);

        /// <summary>A process agent: an AG-* seat other than the coordinator.</summary>
        public static bool IsProcessAgent(string code) => IsAgent(code) && !string.Equals(code.Trim(), Master, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The thirteen stations of the process.</summary>
    public static class AgentStations
    {
        public const int Min = 1;
        public const int Max = 13;

        public static readonly IReadOnlyList<string> Titles = new[]
        {
            "فروش و پیشنهاد",             // 1
            "قرارداد و بازنگری قرارداد",   // 2
            "آغاز پروژه",                 // 3
            "برنامه‌ریزی و کنترل پروژه",   // 4
            "مهندسی",                    // 5
            "تدارکات و خرید",             // 6
            "انبار",                      // 7
            "تولید",                      // 8
            "کیفیت",                     // 9
            "بسته‌بندی",                   // 10
            "ارسال و تحویل",              // 11
            "خدمات پس از فروش",           // 12
            "مالی و سپیدار",              // 13
        };

        public static bool IsValid(int station) => station >= Min && station <= Max;

        public static string Title(int station) => IsValid(station) ? Titles[station - 1] : station.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static class AgentRoles
    {
        /// <summary>Reads the tasks, the board and the step catalogue.</summary>
        public const string BoardReaders = "PMO,CEO,AUDITOR";

        /// <summary>Retries a failed task and switches a step on or off.</summary>
        public const string Admins = "PMO";

        /// <summary>Reads the step catalogue.</summary>
        public const string StepReaders = "PMO,CEO";
    }

    /// <summary>The configuration keys of the module (appsettings.json, section Agents).</summary>
    public static class AgentConfigKeys
    {
        /// <summary>False keeps the scheduler from starting any AG-* seat (the CI runs the agents by hand). A run by hand through the admin page still works. Default true.</summary>
        public const string Jobs = "Agents:Jobs";
    }

    public static class AgentText
    {
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string InvalidData = "اطلاعات ارسال‌شده کامل نیست.";
        public const string SeatsHaveNoInbox = "کارتابل گیت برای کاربران انسانی است؛ ایجنت هوش مصنوعی تصمیم نمی‌گیرد.";
        public const string ReasonRequired = "علت برگشت به ایجنت را بنویسید.";
        public const string TaskNotFound = "کار ایجنت پیدا نشد.";
        public const string StepNotFound = "گام ایجنت پیدا نشد.";
        public const string NotYourGate = "این کار در کارتابل نقش شما نیست.";
        public const string NotAtGate = "فقط کاری که منتظر تصمیم، کار فیزیکی یا ورود اطلاعات است برگشت می‌خورد.";
        public const string NoProducer = "ایجنتی که این سند را آماده کرده پیدا نشد؛ برگشت ممکن نیست.";
        public const string WrongAgent = "این گام به ایجنت دیگری تعلق دارد و این ایجنت آن را اجرا نمی‌کند.";
        public const string StepUnknown = "گام این کار در فهرست گام‌ها نیست.";
        public const string StepSwitchedOff = "گام یا ایجنت غیرفعال است؛ کار به یک انسان سپرده شد.";
        public const string DocumentChanged = "وضعیت سند تغییر کرد";
        public const string StepSwitchedOffCancel = "گام غیرفعال شد";
        public const string DoneByAgent = "انجام شد توسط ایجنت";
        public const string AgentRestored = "ایجنت دوباره فعال شد و کار به او برگشت";
        public const string StaleRunning = "اجرای ایجنت پایان نیافت و کار دوباره در صف قرار گرفت.";
        public const string OnlyFailedCanRetry = "فقط کار ناموفق دوباره اجرا می‌شود.";

        public static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);
        public static ResponseDto<T> NotFound<T>(string message) => ResponseDtoHelper.NotFound<T>(message);
        public static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        public static string Cut(string s, int max) => s == null ? null : (s.Length <= max ? s : s.Substring(0, max));

        /// <summary>Upper case, trimmed, distinct role names of a comma separated list, as a comma separated list (null when empty).</summary>
        public static string NormalizeRoles(string roles)
        {
            if (string.IsNullOrWhiteSpace(roles)) return null;
            string[] list = roles.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim().ToUpperInvariant()).Where(r => r.Length > 0).Distinct().ToArray();
            return list.Length == 0 ? null : string.Join(",", list);
        }

        public static string[] SplitRoles(string roles) =>
            string.IsNullOrWhiteSpace(roles) ? Array.Empty<string>() : roles.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim().ToUpperInvariant()).Where(r => r.Length > 0).ToArray();
    }

    /// <summary>
    /// The step that the agent is executing in this flow of execution (set by the executor around <c>IAgentStep.RunAsync</c>, null otherwise). A service that refuses AI in general because its method makes
    /// a document (a BOM, a routing) can allow the agent that owns the step with <see cref="AgentMaker.Allows"/>. An approval line is never changed this way.
    /// </summary>
    public static class AgentStepContext
    {
        private static readonly AsyncLocal<string> CurrentCode = new AsyncLocal<string>();

        public static string Current => CurrentCode.Value;

        /// <summary>Sets the step for the flow until the returned object is disposed.</summary>
        public static IDisposable Enter(string stepCode)
        {
            string previous = CurrentCode.Value;
            CurrentCode.Value = stepCode;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly string _previous;

            public Scope(string previous)
            {
                _previous = previous;
            }

            public void Dispose() => CurrentCode.Value = _previous;
        }
    }

    public static class AgentMaker
    {
        /// <summary>True when an agent is executing one of the given steps (the maker side of a document; never an approval).</summary>
        public static bool Allows(params string[] stepCodes)
        {
            string current = AgentStepContext.Current;
            return current != null && stepCodes != null && stepCodes.Any(c => string.Equals(c, current, StringComparison.OrdinalIgnoreCase));
        }
    }
}
