using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Production
{
    /// <summary>Role names (as written in the JWT, upper case) for the production module. ASSEMBLER and CONSTRUCTION work as operators of their areas.</summary>
    public static class ProductionRoles
    {
        public const string Manager = "PRODUCTIONMANAGER";
        public const string Supervisor = "SHOPSUPERVISOR";
        public const string Operator = "OPERATOR";
        public const string Maintenance = "MAINTENANCE";

        /// <summary>Master data: work centres, machines, standards, reason codes.</summary>
        public const string MasterEditors = "PRODUCTIONMANAGER,PMO";

        /// <summary>Operation standards are filled by production and engineering.</summary>
        public const string StandardEditors = "PRODUCTIONMANAGER,ENGINEERING,ENGINEER,PMO";

        /// <summary>The second approver of a routing revision.</summary>
        public const string Engineering = "ENGINEERING,ENGINEER";

        /// <summary>Anybody who may open a routing revision (it is approved by other people).</summary>
        public const string RoutingWriters = "PRODUCTIONMANAGER,ENGINEERING,ENGINEER,PMO,PLANNING";

        /// <summary>Hourly rates are set by finance only.</summary>
        public const string RateSetters = "FINANCEMANAGER,FINANCIAL";

        /// <summary>Releases an order and works with the shop floor.</summary>
        public const string Supervisors = "SHOPSUPERVISOR,PRODUCTIONMANAGER";

        /// <summary>Drafts production orders by hand (the system drafts them by itself) and edits the components.</summary>
        public const string Planners = "PRODUCTIONMANAGER,SHOPSUPERVISOR,PLANNING,PMO,ENGINEERING,ENGINEER";

        /// <summary>The shop-floor terminal.</summary>
        public const string Terminal = "OPERATOR,SHOPSUPERVISOR,PRODUCTIONMANAGER,ASSEMBLER,CONSTRUCTION";

        /// <summary>Accepts or rejects an operation: QC inspectors only.</summary>
        public const string Inspectors = "QC";

        public const string Readers = "PMO,CEO,PRODUCTIONMANAGER,SHOPSUPERVISOR,PLANNING,QC,QA,ENGINEERING,ENGINEER,PROJECTMANAGER,WHSUPERVISOR,WAREHOUSE,MAINTENANCE,FINANCEMANAGER,AUDITOR";

        /// <summary>Whoever reads or writes on the terminal also sees the machines and reason codes.</summary>
        public const string MasterReaders = Readers + ",OPERATOR,ASSEMBLER,CONSTRUCTION";

        public const string AttendanceCheck = "SHOPSUPERVISOR,PRODUCTIONMANAGER,HR,CEO,PMO";
    }

    /// <summary>Counter kinds in <c>DocumentNumberSequence</c> (the production module uses 481..500).</summary>
    internal static class MfNumbers
    {
        public const int ProductionOrder = 481;
        public const int MaintenanceRequest = 482;
    }

    internal static class MfText
    {
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string InvalidData = "اطلاعات ارسال‌شده کامل نیست.";
        public const string Concurrency = "اطلاعات هم‌زمان توسط کاربر دیگری تغییر کرد. صفحه را دوباره باز کنید و دوباره تلاش کنید.";
        public const string SameUser = "این کار باید توسط شخص دیگری انجام شود (تفکیک وظایف).";

        public static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        public static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);
        public static ResponseDto<T> NotFound<T>(string message) => ResponseDtoHelper.NotFound<T>(message);

        /// <summary>Persian (Shamsi) date for everything a person reads, for example 1405/08/03.</summary>
        public static string Date(DateTime? d) => d.HasValue ? InventoryText.PersianDate(d.Value) : null;

        public static string Date(DateTimeOffset? d) => d.HasValue ? InventoryText.PersianDate(d.Value.LocalDateTime) : null;

        /// <summary>Persian date and time, for example 1405/08/03 14:05.</summary>
        public static string Stamp(DateTime? d) => d.HasValue ? InventoryText.PersianDate(d.Value) + " " + d.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : null;

        public static string Stamp(DateTimeOffset? d) => d.HasValue ? Stamp(d.Value.LocalDateTime) : null;

        public static string Number(decimal value) => InventoryText.Number(value);

        public static decimal HoursOf(int minutes) => Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);

        /// <summary>Pages are never unlimited: a missing size is 25, the largest is 500.</summary>
        public static int Size(int pageSize) => pageSize <= 0 ? 25 : Math.Min(pageSize, 500);

        public static async Task<PagedList<T>> PageAsync<T>(IQueryable<T> rows, int pageNumber, int pageSize, CancellationToken ct) =>
            await PagedList<T>.CreateAsync(rows, pageNumber, Size(pageSize), ct);

        public static async Task<PagedList<T>> PageAsync<T>(List<T> rows, int pageNumber, int pageSize, CancellationToken ct) =>
            await PagedList<T>.CreateAsync(rows, pageNumber, Size(pageSize), ct);

        /// <summary>The employee record (payroll personnel file) linked to a user.</summary>
        public static Task<FnEmployee> EmployeeOfUserAsync(IUnitOfWork uow, int userId, CancellationToken ct) =>
            uow.Set<FnEmployee>().AsNoTracking().Where(e => e.UserId == userId && e.IsActive).OrderBy(e => e.Id).FirstOrDefaultAsync(ct);
    }

    internal static class MfNames
    {
        public static string Operation(string orderNumber, int sequence) => orderNumber + "/" + sequence.ToString(CultureInfo.InvariantCulture);

        public static string EmployeeName(FnEmployee e) => e == null ? null : (e.PersonnelCode + " " + e.FirstName + " " + e.LastName).Trim();

        public static Dictionary<int, string> ToDictionary(IEnumerable<FnEmployee> rows) => rows.ToDictionary(e => e.Id, EmployeeName);
    }
}

namespace Services.NasbNiro.Production
{
    internal static class MfConcurrency
    {
        /// <summary>Uses the row version the client saw; false when it is not a valid version text.</summary>
        public static bool Apply<T>(IUnitOfWork uow, T entity, string version) where T : class
        {
            if (string.IsNullOrWhiteSpace(version)) return true;
            try
            {
                uow.Entry(entity).Property("RowVersion").OriginalValue = Convert.FromBase64String(version);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static string Text(byte[] version) => version == null ? null : Convert.ToBase64String(version);
    }
}
