using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Ai;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services.Contracts.Identity;

namespace Services.NasbNiro.Ai
{
    // Release A1 part B: the seats. A seat is a scheduled assistant that runs as its own service user with only its own roles,
    // so every role check and row filter of the existing services applies to it. What it produces is a draft or a note in the gate of a module.

    public static class AiSeatNames
    {
        /// <summary>The user names of the seats and of the evaluation user start with this. Nobody can log in with such a name.</summary>
        public const string UserPrefix = "seat.";

        public static string UserName(string code) => UserPrefix + code.ToLowerInvariant();

        public static bool IsSeatUser(string userName) => !string.IsNullOrWhiteSpace(userName) && userName.Trim().StartsWith(UserPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static class AiSeatCodes
    {
        public const string CeoBrief = "CEO-BRIEF";
        public const string PmoWeekly = "PMO-WEEKLY";
        public const string WhReorder = "WH-REORDER";
        public const string ProcCompare = "PROC-COMPARE";
        public const string QcNcrAssist = "QC-NCR-ASSIST";
        public const string FinAnomaly = "FIN-ANOMALY";
        public const string OfficeDraft = "OFFICE-DRAFT";

        /// <summary>The user the evaluation runs as (it is not a seat).</summary>
        public const string Eval = "EVAL";
    }

    public sealed class AiSeatDefinition
    {
        public string Code { get; set; }
        public string Title { get; set; }

        /// <summary>The only roles the seat acts with.</summary>
        public string[] Roles { get; set; }
        public string Schedule { get; set; }
        public string OutputKind { get; set; }
        public bool UsesModel { get; set; }

        public string UserName => AiSeatNames.UserName(Code);
        public string RolesText => string.Join(",", Roles);
    }

    public static class AiSeatCatalog
    {
        // ★ roles: exactly what the existing services demand for the data the seat reads and the draft it makes
        public static readonly IReadOnlyList<AiSeatDefinition> All = new List<AiSeatDefinition>
        {
            new AiSeatDefinition { Code = AiSeatCodes.CeoBrief, Title = "گزارش روزانه‌ی مدیرعامل", Roles = new[] { "CEO" }, Schedule = "daily 07:00", OutputKind = "CeoBrief", UsesModel = true },
            new AiSeatDefinition { Code = AiSeatCodes.PmoWeekly, Title = "روایت گزارش هفتگی پروژه‌ها", Roles = new[] { "PMO" }, Schedule = "weekly 5 07:00", OutputKind = "PcWeeklyReport", UsesModel = true },
            new AiSeatDefinition { Code = AiSeatCodes.WhReorder, Title = "پیش‌نویس درخواست خرید اقلام زیر حداقل", Roles = new[] { "WAREHOUSE" }, Schedule = "daily 06:30", OutputKind = "PurchaseRequestDraft", UsesModel = false },
            new AiSeatDefinition { Code = AiSeatCodes.ProcCompare, Title = "یادداشت مقایسه‌ی استعلام‌ها", Roles = new[] { "COMMERCIAL" }, Schedule = "poll 15", OutputKind = "PurchaseRfq", UsesModel = true },
            new AiSeatDefinition { Code = AiSeatCodes.QcNcrAssist, Title = "دستیار گزارش عدم انطباق", Roles = new[] { "QC" }, Schedule = "poll 10", OutputKind = "QualityNcr", UsesModel = true },
            new AiSeatDefinition { Code = AiSeatCodes.FinAnomaly, Title = "بازبینی ناهنجاری‌های مالی", Roles = new[] { "FINANCIAL" }, Schedule = "daily 06:00", OutputKind = "FinanceNotes", UsesModel = false },
            new AiSeatDefinition { Code = AiSeatCodes.OfficeDraft, Title = "پیش‌نویس پاسخ نامه و خلاصه‌ی صورت‌جلسه", Roles = new[] { "SECRETARIAT" }, Schedule = "poll 15", OutputKind = "OfficeNotes", UsesModel = true },
        };

        /// <summary>The evaluation user: it reads like the CEO (★), it is not a seat and has no row in AiSeats.</summary>
        public static readonly AiSeatDefinition EvalUser = new AiSeatDefinition { Code = AiSeatCodes.Eval, Title = "کاربر ارزیابی هوش مصنوعی", Roles = new[] { "CEO" } };

        public static AiSeatDefinition Find(string code) =>
            All.FirstOrDefault(s => string.Equals(s.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public enum AiScheduleKind
    {
        Daily = 1,
        Weekly = 2,
        Poll = 3,
    }

    /// <summary>
    /// "daily HH:mm", "weekly D HH:mm" (D is 0..6, Saturday = 0 ... Friday = 6 as in the Persian week) or "poll N" (every N minutes). Times are the server's local time.
    /// Pure, so it is unit tested.
    /// </summary>
    public sealed class AiSchedule
    {
        public AiScheduleKind Kind { get; private set; }
        public TimeSpan Time { get; private set; }

        /// <summary>0 = Saturday ... 6 = Friday.</summary>
        public int PersianWeekday { get; private set; }
        public int PollMinutes { get; private set; }

        private static readonly string[] DayNames = { "شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه" };

        public static int PersianIndex(DayOfWeek day) => ((int)day + 1) % 7;

        public static AiSchedule Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string[] p = text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (p[0].ToLowerInvariant())
            {
                case "daily":
                    return p.Length == 2 && TryTime(p[1], out TimeSpan t) ? new AiSchedule { Kind = AiScheduleKind.Daily, Time = t } : null;
                case "weekly":
                    return p.Length == 3 && int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out int d) && d >= 0 && d <= 6 && TryTime(p[2], out TimeSpan w)
                        ? new AiSchedule { Kind = AiScheduleKind.Weekly, PersianWeekday = d, Time = w } : null;
                case "poll":
                    return p.Length == 2 && int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= 1440
                        ? new AiSchedule { Kind = AiScheduleKind.Poll, PollMinutes = n } : null;
                default:
                    return null;
            }
        }

        private static bool TryTime(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            string[] p = text.Split(':');
            if (p.Length != 2 || !int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out int h) || !int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out int m)) return false;
            if (h > 23 || m > 59) return false;
            time = new TimeSpan(h, m, 0);
            return true;
        }

        /// <summary>A sentence for the admin page.</summary>
        public string Describe()
        {
            string hm = Time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            switch (Kind)
            {
                case AiScheduleKind.Daily: return "هر روز ساعت " + hm;
                case AiScheduleKind.Weekly: return "هر هفته " + DayNames[PersianWeekday] + " ساعت " + hm;
                default: return "هر " + PollMinutes.ToString(CultureInfo.InvariantCulture) + " دقیقه";
            }
        }

        /// <summary>The latest planned moment that is not after <paramref name="now"/> (daily and weekly seats); null for a poll seat.</summary>
        public DateTime? LatestSlot(DateTime now)
        {
            if (Kind == AiScheduleKind.Poll) return null;
            for (int back = 0; back <= 8; back++)
            {
                DateTime day = now.Date.AddDays(-back);
                if (Kind == AiScheduleKind.Weekly && PersianIndex(day.DayOfWeek) != PersianWeekday) continue;
                DateTime slot = day + Time;
                if (slot <= now) return slot;
            }
            return null;
        }

        /// <summary>
        /// Is a run due now? <paramref name="lastAttempt"/> is the start of the latest run of any status, <paramref name="lastSuccess"/> the start of the latest successful one.
        /// Daily and weekly seats run once after every planned moment (a missed moment is made up at the next tick); a run that did not succeed is tried again after
        /// <paramref name="retryMinutes"/>. A poll seat runs again when N minutes have passed since its last attempt.
        /// </summary>
        public bool IsDue(DateTime now, DateTime? lastAttempt, DateTime? lastSuccess, int retryMinutes)
        {
            if (Kind == AiScheduleKind.Poll) return !lastAttempt.HasValue || now - lastAttempt.Value >= TimeSpan.FromMinutes(PollMinutes);
            DateTime? slot = LatestSlot(now);
            if (!slot.HasValue) return false;
            if (lastSuccess.HasValue && lastSuccess.Value >= slot.Value) return false;
            if (lastAttempt.HasValue && lastAttempt.Value >= slot.Value && now - lastAttempt.Value < TimeSpan.FromMinutes(Math.Max(1, retryMinutes))) return false;
            return true;
        }
    }

    /// <summary>The seat that is running in this flow of execution (for the audit rows). Null outside a seat run.</summary>
    public static class AiSeatContext
    {
        private static readonly AsyncLocal<string> Current = new AsyncLocal<string>();

        public static string Code
        {
            get => Current.Value;
            set => Current.Value = value;
        }
    }

    public sealed class AiActingUser
    {
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string DisplayName { get; set; }
        public string[] Roles { get; set; }
    }

    /// <summary>
    /// Runs code as a service user. The services read the current user from the HTTP context (IInventoryUserContext, the audit columns, the finance trail), so the run gets a
    /// context of its own with the service user and its roles. It starts in a flow of execution that does not inherit the caller's context, so a normal request is never touched.
    /// </summary>
    public static class AiActing
    {
        private static readonly AsyncLocal<AiActingUser> CurrentUser = new AsyncLocal<AiActingUser>();

        public static AiActingUser Current => CurrentUser.Value;

        /// <summary>True inside a run of a seat (or of the evaluation): such a user never decides a note.</summary>
        public static bool IsActing => CurrentUser.Value != null;

        public static ClaimsPrincipal Principal(AiActingUser user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
                // the other claims a login token carries, so a service that reads them sees the same shape
                new Claim("DisplayName", user.DisplayName ?? user.UserName ?? string.Empty),
                new Claim("personageId", "0"),
                new Claim("inviteCode", string.Empty),
                new Claim(ClaimTypes.SerialNumber, "1"),
                new Claim(ClaimTypes.UserData, user.UserId.ToString(CultureInfo.InvariantCulture)),
            };
            foreach (string role in user.Roles ?? Array.Empty<string>()) claims.Add(new Claim(ClaimTypes.Role, role));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "AiSeat"));
        }

        public static Task<T> RunAsync<T>(IServiceScopeFactory scopes, AiActingUser user, string seatCode, Func<IServiceProvider, Task<T>> body)
        {
            if (scopes == null) throw new ArgumentNullException(nameof(scopes));
            if (user == null) throw new ArgumentNullException(nameof(user));
            using (ExecutionContext.SuppressFlow())
            {
                return Task.Run(async () =>
                {
                    using (IServiceScope scope = scopes.CreateScope())
                    {
                        IHttpContextAccessor accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
                        accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = Principal(user) };
                        CurrentUser.Value = user;
                        AiSeatContext.Code = seatCode;
                        try
                        {
                            return await body(scope.ServiceProvider);
                        }
                        finally
                        {
                            accessor.HttpContext = null;
                            CurrentUser.Value = null;
                            AiSeatContext.Code = null;
                        }
                    }
                });
            }
        }
    }

    public sealed class AiSeatIdentity
    {
        public AiActingUser User { get; set; }
        public AiSeatDefinition Definition { get; set; }
    }

    public interface IAiSeatProvisioner
    {
        /// <summary>
        /// Creates what is missing (idempotent): the rows of the seats and one service user for every seat and for the evaluation. The users have no password and are locked out for good.
        /// </summary>
        Task<Dictionary<string, AiActingUser>> EnsureAsync(CancellationToken ct);
    }

    public class AiSeatProvisioner : IAiSeatProvisioner
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private readonly IUnitOfWork _uow;
        private readonly IApplicationUserManager _users;
        private readonly ILogger<AiSeatProvisioner> _logger;

        public AiSeatProvisioner(IUnitOfWork uow, IApplicationUserManager users, ILogger<AiSeatProvisioner> logger = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _users = users ?? throw new ArgumentNullException(nameof(users));
            _logger = logger;
        }

        public async Task<Dictionary<string, AiActingUser>> EnsureAsync(CancellationToken ct)
        {
            await Gate.WaitAsync(ct);
            try
            {
                // the rows of the seats: roles, title and user name follow the catalogue; the schedule and the switch belong to the admin once the row exists
                List<AiSeat> rows = await _uow.Set<AiSeat>().ToListAsync(ct);
                bool changed = false;
                foreach (AiSeatDefinition d in AiSeatCatalog.All)
                {
                    AiSeat row = rows.FirstOrDefault(r => r.Code == d.Code);
                    if (row == null)
                    {
                        _uow.Set<AiSeat>().Add(new AiSeat
                        {
                            Code = d.Code, Title = d.Title, ServiceUserName = d.UserName, Roles = d.RolesText, Schedule = d.Schedule, PromptVersionCode = "CHAT",
                            Enabled = true, OutputKind = d.OutputKind,
                        });
                        changed = true;
                        continue;
                    }
                    if (row.Title != d.Title || row.ServiceUserName != d.UserName || row.Roles != d.RolesText || row.OutputKind != d.OutputKind)
                    {
                        row.Title = d.Title; row.ServiceUserName = d.UserName; row.Roles = d.RolesText; row.OutputKind = d.OutputKind;
                        changed = true;
                    }
                    if (AiSchedule.Parse(row.Schedule) == null) { row.Schedule = d.Schedule; changed = true; }
                }
                if (changed)
                {
                    try { await _uow.SaveChangesAsync(ct); }
                    catch (DbUpdateException)
                    {
                        // another process created the rows at the same moment: the unique index on the code protects us
                        foreach (var e in _uow.Set<AiSeat>().Local.Where(x => x.Id == 0).ToList()) _uow.Entry(e).State = EntityState.Detached;
                    }
                }

                var result = new Dictionary<string, AiActingUser>(StringComparer.OrdinalIgnoreCase);
                foreach (AiSeatDefinition d in AiSeatCatalog.All.Concat(new[] { AiSeatCatalog.EvalUser }))
                    result[d.Code] = await EnsureUserAsync(d, ct);
                return result;
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task<AiActingUser> EnsureUserAsync(AiSeatDefinition d, CancellationToken ct)
        {
            string name = d.UserName;
            User user = await _users.FindByNameAsync(name);
            if (user == null)
            {
                user = new User
                {
                    UserName = name, Name = d.Title, LastName = "(صندلی هوش مصنوعی)", Email = name + "@pmis.local", EmailConfirmed = true,
                    LockoutEnabled = true, LockoutEnd = DateTimeOffset.MaxValue, IsActive = true, IsDeleted = false, BirthDate = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
                };
                // no password: PasswordHash stays empty, so no password can ever match
                var created = await _users.CreateAsync(user);
                if (!created.Succeeded)
                {
                    user = await _users.FindByNameAsync(name);
                    if (user == null) throw new InvalidOperationException("کاربر سرویس صندلی ساخته نشد: " + string.Join("؛ ", created.Errors.Select(e => e.Description)));
                }
            }
            if (!user.IsActive || user.IsDeleted || !user.LockoutEnabled || !user.LockoutEnd.HasValue || user.LockoutEnd.Value < DateTimeOffset.Now.AddYears(50) || !string.IsNullOrEmpty(user.PasswordHash))
            {
                user.IsActive = true; user.IsDeleted = false; user.LockoutEnabled = true; user.LockoutEnd = DateTimeOffset.MaxValue; user.PasswordHash = null;
                await _users.UpdateAsync(user);
            }

            IList<string> current = await _users.GetRolesAsync(user);
            string[] want = d.Roles.Select(r => r.ToUpperInvariant()).ToArray();
            string[] have = current.Select(r => r.ToUpperInvariant()).ToArray();
            string[] add = want.Except(have).ToArray();
            string[] remove = have.Except(want).ToArray();
            if (add.Length > 0)
            {
                var r = await _users.AddToRolesAsync(user, add);
                if (!r.Succeeded) _logger?.LogWarning("Roles {Roles} of {User} could not be added: {Errors}", string.Join(",", add), name, string.Join("; ", r.Errors.Select(e => e.Description)));
            }
            if (remove.Length > 0) await _users.RemoveFromRolesAsync(user, remove);
            return new AiActingUser { UserId = user.Id, UserName = name, DisplayName = d.Title, Roles = want };
        }
    }
}
