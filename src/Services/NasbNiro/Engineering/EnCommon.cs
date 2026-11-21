using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.GoLive;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Services.NasbNiro.Attachments;
using Services.NasbNiro.GoLive;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProductBank;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Engineering
{
    // ENG-E1 (engineering module, external workflow). Governing principle: the agents do the process; people only confirm at gates, do physical work or give data the system does not have; AI never approves; maker is never checker.

    public static class EnRoles
    {
        /// <summary>The engineering manager or his deputy: confirms, releases, requests a waiver.</summary>
        public const string Manager = "ENGINEERING";

        /// <summary>The engineers and the manager: draft, edit and supply (and the engineering agent seat, whose role is ENGINEER).</summary>
        public const string Writers = "ENGINEERING,ENGINEER";

        public const string Ceo = "CEO";

        /// <summary>Who records the standard of a project from the client data sheet: sales, commercial and engineering (nobody chooses it; it is the client's).</summary>
        public const string StandardSetters = "SALES,COMMERCIAL,ENGINEERING,ENGINEER";
        public const string Commercial = "SALES,COMMERCIAL";
        public const string Pmo = "PMO";
        public const string PmoSide = "PMO,PROJECTMANAGER";

        /// <summary>The reference list is approved by the engineering manager or the CEO, never by whoever drafted or edited it.</summary>
        public const string RefApprovers = "ENGINEERING,CEO";

        /// <summary>The units that receive the documents of a released handover, and the people of engineering, the PMO and the commercial side who work with the case.</summary>
        /// <summary>Who reads the project MDL (the API and its three files): need-to-know is narrower than <see cref="Readers"/>.</summary>
        public const string MdlReaders = "ENGINEERING,ENGINEER,CEO,PMO,PROJECTMANAGER,SALES,COMMERCIAL,DCC,AUDITOR";

        public const string Readers = "ENGINEERING,ENGINEER,PMO,CEO,PROJECTMANAGER,PLANNING,SALES,COMMERCIAL,DCC,QA,QC,PRODUCTIONMANAGER,SHOPSUPERVISOR,CONSTRUCTION,WAREHOUSE,WHSUPERVISOR,AUDITOR";
    }

    internal static class EnNumbers
    {
        public const int Case = 851;
        public const int Handover = 852;
        public const int Clarification = 853;
        public const int Waiver = 854;
        public const int Deviation = 855;
        public const int StandardGap = 856;
        public const int InfoRequest = 857;
        public const int PlatformRegistration = 858;
        public const int ProjectMdl = 859;
        public const int MdlPermit = 860;

        /// <summary>For example "ENG-1405-0012": the Shamsi year and a sequence of the year.</summary>
        public static async Task<string> NextAsync(IUnitOfWork uow, int kind, string prefix, DateTime date, CancellationToken ct)
        {
            int year = InventoryText.PersianYear(date);
            int number = await InventoryNumbers.NextAsync(uow, kind, year, ct);
            return $"{prefix}-{year}-{number:0000}";
        }
    }

    /// <summary>The document types the system can prove from its own state (a released BOM, an approved routing). The company's document type names are text, so they are compared without case; they are not settings (the settings store holds numbers only).</summary>
    public static class EnDocTypes
    {
        public const string Bom = "BOM";
        public const string Opc = "OPC";
    }

    public static class EnSettingKeys
    {
        /// <summary>The Shamsi number yyyyMMdd of the day the engineering control starts; 0 = the one cut-over instant of Package G (<see cref="CutOver"/>). Moving it later makes more work legacy: a weakening that needs a second person (like Design.ControlStartDate).</summary>
        public const string ControlStartDate = "Engineering.ControlStartDate";

        /// <summary>The farthest due date (days ahead) of the document debts of an emergency waiver.</summary>
        public const string WaiverMaxDebtHours = "Engineering.Waiver.MaxDebtHours";

        public const string Source = "★ تصمیم مدیرعامل (فرایند مهندسی با سایر واحدها؛ عدم تحمل انحراف)";

        public static IEnumerable<QualitySetting> Defaults()
        {
            yield return new QualitySetting { Key = ControlStartDate, Value = 0, Title = "روز آغاز کنترل مهندسی (تاریخ شمسی به‌صورت yyyyMMdd؛ ۰ = لحظهٔ آغاز کنترل ثبت‌شده در نخستین اجرای برنامه، همان گذار بسته G): استعلام و قراردادهای پیش از آن «قدیمی» شمرده می‌شوند", Source = Source };
            yield return new QualitySetting { Key = WaiverMaxDebtHours, Value = 72, Title = "حداکثر مهلت (ساعت اداری) بدهی مدارک در مجوز اضطرار قفل ابلاغ مدارک مهندسی به اجرا (سقف ۷۲ ساعت اداری)", Source = Source };
        }

        public static bool TryGetBounds(string key, out int min, out int max)
        {
            min = 0; max = 0;
            switch (key)
            {
                case ControlStartDate: max = 99991231; return true;
                case WaiverMaxDebtHours: min = 1; max = 72; return true;
                default: return false;
            }
        }
    }

    internal static class EnRules
    {
        /// <summary>The shortest reason (characters) of a return, a void and a non-applicable mark.</summary>
        public const int ReasonMin = 15;

        /// <summary>The shortest reason of an emergency waiver request.</summary>
        public const int WaiverReasonMin = 50;

    }

    internal static class EnText
    {
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string InvalidData = "اطلاعات ارسال‌شده کامل نیست.";
        public const string Concurrency = "اطلاعات هم‌زمان توسط کاربر دیگری تغییر کرد. صفحه را دوباره باز کنید و دوباره تلاش کنید.";
        public const string SameUser = "این کار باید توسط شخص دیگری انجام شود (تفکیک وظایف: سازنده یا ویرایشگر تأییدکننده نیست).";
        public const string AiNever = "این کار با انسان است؛ صندلی هوش مصنوعی آن را انجام نمی‌دهد.";
        public const string NotFoundCase = "پرونده مهندسی پیدا نشد.";
        public const string NotFoundHandover = "تحویل مدارک پیدا نشد.";
        public const string NotFoundList = "فهرست MDL پیدا نشد.";
        public const string NotFoundDossier = "پرونده مدارک پلتفرم پیدا نشد.";

        public static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        public static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);
        public static ResponseDto<T> NotFound<T>(string message) => ResponseDtoHelper.NotFound<T>(message);
        public static string Date(DateTime? d) => d.HasValue ? InventoryText.PersianDate(d.Value) : null;
        public static string Date(DateTimeOffset? d) => d.HasValue ? InventoryText.PersianDate(d.Value.LocalDateTime) : null;
        public static string Stamp(DateTimeOffset? d) => d.HasValue ? InventoryText.PersianDate(d.Value.LocalDateTime) + " " + d.Value.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) : null;
        public static int Size(int pageSize) => pageSize <= 0 ? 25 : Math.Min(pageSize, 500);
        public static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

        public static string Over(string text, int max, string label) => text != null && text.Length > max ? $"{label} بیش از {Num(max)} نویسه است." : null;

        public static string ReasonShort(string reason, int min, string label) => reason == null || reason.Length < min ? $"{label} دست‌کم {Num(min)} نویسه باشد." : null;

        public static string Cut(string s, int max) => s == null || s.Length <= max ? s : s.Substring(0, max);

        public static string RefStatus(EnRefStatus s) => s switch
        {
            EnRefStatus.Draft => "پیش‌نویس", EnRefStatus.UnderReview => "در انتظار تایید", EnRefStatus.Approved => "تاییدشده", EnRefStatus.Superseded => "جایگزین‌شده", _ => "ابطال‌شده",
        };

        public static string Dossier(EnDossierStatus s) => s switch
        {
            EnDossierStatus.NotAssessed => "ارزیابی‌نشده", EnDossierStatus.Incomplete => "ناقص", EnDossierStatus.Completing => "کامل‌شده، منتظر تایید مدیر مهندسی", _ => "کامل",
        };

        public static string LineState(EnLineState s) => s switch { EnLineState.Missing => "موجود نیست", EnLineState.Available => "موجود", _ => "غیرقابل‌اجرا" };

        public static string Classification(EnClassification c) => c switch { EnClassification.Platform => "پلتفرم (توربین ساخته‌شده)", EnClassification.New => "طراحی جدید", _ => "تعیین‌نشده" };

        public static string Kind(EnCaseKind k) => k switch { EnCaseKind.SpareParts => "قطعات یدکی", EnCaseKind.Service => "خدمات پس از فروش", _ => "توربین" };

        public static string Case(EnCaseStatus s) => s switch
        {
            EnCaseStatus.Classifying => "در حال تعیین نوع", EnCaseStatus.AwaitingEngConfirm => "منتظر تایید مدیر مهندسی", EnCaseStatus.AwaitingCeoApproval => "منتظر تصویب مدیرعامل",
            EnCaseStatus.TenderPreparation => "تهیه بسته فنی مناقصه", EnCaseStatus.TenderIssued => "بسته فنی به بازرگانی ابلاغ شد", EnCaseStatus.Awarded => "برنده‌شده",
            EnCaseStatus.InEngineering => "در مهندسی", EnCaseStatus.HandedOver => "تحویل به PMO", EnCaseStatus.Accepted => "پذیرفته‌شده توسط PMO", EnCaseStatus.Released => "ابلاغ‌شده به اجرا",
            EnCaseStatus.ClosedLost => "بسته‌شده (بازنده یا عدم شرکت)", _ => "لغوشده",
        };

        public static string Clar(EnClarStatus s) => s switch
        {
            EnClarStatus.Draft => "پیش‌نویس", EnClarStatus.WithCommercial => "نزد بازرگانی", EnClarStatus.SentToClient => "ارسال‌شده به کارفرما", EnClarStatus.Answered => "پاسخ‌داده‌شده", EnClarStatus.Closed => "بسته‌شده", _ => "ابطال‌شده",
        };

        public static string Handover(EnHandoverStatus s) => s switch
        {
            EnHandoverStatus.Draft => "پیش‌نویس", EnHandoverStatus.Submitted => "ارسال‌شده به PMO", EnHandoverStatus.Returned => "برگشت‌خورده", EnHandoverStatus.Accepted => "پذیرفته‌شده", EnHandoverStatus.Released => "ابلاغ‌شده به اجرا", _ => "ابطال‌شده",
        };

        public static string Waiver(EnWaiverStatus s) => s switch
        {
            EnWaiverStatus.Requested => "درخواست‌شده", EnWaiverStatus.Approved => "تاییدشده", EnWaiverStatus.Rejected => "ردشده", _ => "ابطال‌شده",
        };

        public static string Unit(EnUnit u) => u switch
        {
            EnUnit.Qa => "تضمین کیفیت", EnUnit.Qc => "کنترل کیفیت", EnUnit.Production => "تولید", EnUnit.Procurement => "تدارکات", EnUnit.Warehouse => "انبار", EnUnit.Engineering => "مهندسی", EnUnit.Planning => "برنامه‌ریزی", _ => "نامشخص",
        };

        public static string Charter(EnCharterOutcome o) => o switch { EnCharterOutcome.Confirmed => "تاییدشده توسط مهندسی", EnCharterOutcome.Returned => "برگشت‌خورده از مهندسی", _ => "بررسی‌نشده" };
    }

    /// <summary>The units of a document line: comma-separated names of the distribution unit enum, empty = every unit.</summary>
    internal static class EnUnits
    {
        /// <summary>The six distribution units (distribution rows, document unit lists, the parser).</summary>
        public static readonly EnUnit[] All = { EnUnit.Qa, EnUnit.Qc, EnUnit.Production, EnUnit.Procurement, EnUnit.Warehouse, EnUnit.Planning };

        /// <summary>The units that may ask the PMO: the six distribution units and engineering.</summary>
        public static readonly EnUnit[] Askers = { EnUnit.Qa, EnUnit.Qc, EnUnit.Production, EnUnit.Procurement, EnUnit.Warehouse, EnUnit.Planning, EnUnit.Engineering };

        /// <summary>The system-proven BOM line is for these units, the OPC line for those (the employer's rule).</summary>
        public static readonly string Bom = Join(new[] { EnUnit.Production, EnUnit.Procurement, EnUnit.Warehouse, EnUnit.Planning, EnUnit.Qc });
        public static readonly string Opc = Join(new[] { EnUnit.Production, EnUnit.Planning, EnUnit.Qc, EnUnit.Qa });

        /// <summary>The roles of each unit (who receives the notice, asks the PMO and confirms the receipt).</summary>
        public static readonly (EnUnit Unit, string Roles)[] UnitRoles =
        {
            (EnUnit.Qa, "QA"), (EnUnit.Qc, "QC"), (EnUnit.Production, "PRODUCTIONMANAGER,SHOPSUPERVISOR,CONSTRUCTION"), (EnUnit.Procurement, "COMMERCIAL"), (EnUnit.Warehouse, "WAREHOUSE,WHSUPERVISOR"), (EnUnit.Planning, "PLANNING"), (EnUnit.Engineering, "ENGINEERING,ENGINEER"),
        };

        /// <summary>The roles of the six distribution units only (what the distribution builds its rows from).</summary>
        public static (EnUnit Unit, string Roles)[] DistributionRoles => UnitRoles.Where(x => All.Contains(x.Unit)).ToArray();

        public static string RolesOf(EnUnit unit) => UnitRoles.First(x => x.Unit == unit).Roles;

        private static string Join(IEnumerable<EnUnit> units) => string.Join(",", units.Distinct().OrderBy(u => (int)u).Select(u => u.ToString()));

        /// <summary>The canonical text of the units of a line (null = all), or a Persian message when a name is not a unit.</summary>
        public static string Normalize(string text, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(text)) return null;
            List<EnUnit> units = new List<EnUnit>();
            foreach (string part in text.Split(new[] { ',', '،', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                if (Enum.TryParse(part, true, out EnUnit eng) && eng == EnUnit.Engineering) { error = "واحد «مهندسی» فقط از PMO می‌پرسد و گیرندهٔ مدرک یا ابلاغ نیست؛ آن را در فهرست واحدهای مدرک نیاورید."; return null; }
                if (!Enum.TryParse(part, true, out EnUnit u) || !All.Contains(u)) { error = "واحد «" + part + "» شناخته‌شده نیست (" + string.Join("، ", All.Select(x => EnText.Unit(x) + " = " + x)) + ")."; return null; }
                units.Add(u);
            }
            if (units.Count == 0 || units.Distinct().Count() == All.Length) return null;
            return Join(units);
        }

        public static bool Includes(string units, EnUnit unit) => string.IsNullOrWhiteSpace(units) || units.Split(',', StringSplitOptions.RemoveEmptyEntries).Any(x => string.Equals(x.Trim(), unit.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>SHA-256 of a canonical text: an approval binds to the exact content it saw.</summary>
    internal static class EnHash
    {
        public static string Of(string canonical) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical ?? string.Empty))).ToLowerInvariant();

        public static string Norm(string s) => (s ?? string.Empty).Trim().Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
    }

    /// <summary>
    /// Runs several saves as one unit in a database transaction inside the execution strategy (the provider retries transient failures). The work may run again from the start after the change tracker was cleared, so it must
    /// load what it changes itself; a result that is not a success rolls the transaction back. The in-memory database of the tests has no transactions, so the work simply runs.
    /// </summary>
    internal static class EnTransaction
    {
        public static async Task<T> InAsync<T>(IUnitOfWork uow, Func<Task<T>> work, Func<T, bool> succeeded, CancellationToken ct)
        {
            if (!(uow is DbContext db) || !db.Database.IsRelational()) return await work();
            int attempt = 0;
            return await db.Database.CreateExecutionStrategy().ExecuteAsync<T>(async token =>
            {
                if (attempt++ > 0) db.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(token);
                T result = await work();
                if (succeeded(result)) await transaction.CommitAsync(token); else await transaction.RollbackAsync(token);
                return result;
            }, ct);
        }
    }

    internal static class EnConcurrency
    {
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

    internal static class EnEditors
    {
        public const int Max = 400;

        /// <summary>The Persian refusal when adding this person would not fit the column (null = fits).</summary>
        public static string Full(string ids, int user) => With(ids, user).Length > Max ? "شمار ویرایشگران این رکورد به سقف رسیده است؛ برای ادامه با مدیر مهندسی هماهنگ کنید." : null;

        public static string With(string ids, int user) => ids != null && ids.Contains("," + user.ToString(CultureInfo.InvariantCulture) + ",") ? ids : (ids ?? ",") + user.ToString(CultureInfo.InvariantCulture) + ",";

        public static bool Has(string ids, int user) => user > 0 && ids != null && ids.Contains("," + user.ToString(CultureInfo.InvariantCulture) + ",");

        /// <summary>The maker or an editor.</summary>
        public static bool Touched(int creator, string ids, int user) => (creator > 0 && creator == user) || Has(ids, user);

        public static List<int> Parse(string ids) =>
            (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0).Where(v => v > 0).Distinct().ToList();
    }

    /// <summary>The one place that says what is legacy for the engineering gates: work created before the cut-over instant. Nothing recorded and no day set: the control has not started, so nothing is controlled yet (only true before the first start of the program after the update).</summary>
    public static class EnControl
    {
        /// <summary>The instant the control started: the one cut-over of Package G (<see cref="CutOver"/>), or null when it has not started.</summary>
        /// <summary>
        /// Only for the SQL-translation probe of the tests (the probe database swallows every command, so no setting or list can be seeded): while set on the calling thread, the control counts as started and the steps do not return
        /// before their main query on an empty list of approved lists, so that query is translated too. Never set by production code.
        /// </summary>
        private static readonly System.Threading.AsyncLocal<bool> probe = new System.Threading.AsyncLocal<bool>();
        internal static bool Probe { get => probe.Value; set => probe.Value = value; }

        public static async Task<DateTimeOffset?> StartAsync(IUnitOfWork uow, CancellationToken ct)
        {
            if (Probe) return DateTimeOffset.Now.AddDays(-1);
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(uow, ct);
            DateTime? day = DesignGate.ShamsiDay(settings[EnSettingKeys.ControlStartDate]);
            if (day.HasValue) return new DateTimeOffset(DateTime.SpecifyKind(day.Value.Date, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(day.Value.Date));   // a day set for the engineering control only
            DateTimeOffset start = await CutOver.StartAsync(uow, settings, ct);   // else the one cut-over of Package G
            return start <= DateTimeOffset.MinValue ? (DateTimeOffset?)null : start;
        }

        /// <summary>The control is in force now: a start exists and it is not in the future. Before that (a day set ahead for the old data) the agent steps that are not tied to a record find nothing to do.</summary>
        public static async Task<bool> ActiveAsync(IUnitOfWork uow, CancellationToken ct)
        {
            DateTimeOffset? start = await StartAsync(uow, ct);
            return start.HasValue && start.Value <= DateTimeOffset.Now;
        }

        /// <summary>Legacy: created before the cut-over, or the control has not started.</summary>
        public static bool IsLegacy(DateTimeOffset createdAt, DateTimeOffset? start) => !start.HasValue || createdAt < start.Value;

        public static string Text(DateTimeOffset? start) => start.HasValue ? DesignGate.ShamsiText(start.Value.LocalDateTime.Date) : "—";

        public static string LegacyNote(DateTimeOffset? start) =>
            "قدیمی، پیش از آغاز کنترل مهندسی (" + Text(start) + "): دروازه‌های پرونده مهندسی برای این مورد اعمال نمی‌شود.";

        public static async Task<bool> ContractIsLegacyAsync(IUnitOfWork uow, int contractId, CancellationToken ct)
        {
            DateTimeOffset? start = await StartAsync(uow, ct);
            if (!start.HasValue) return true;
            DateTimeOffset? created = await uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().AsNoTracking().Where(c => c.Id == contractId).Select(c => (DateTimeOffset?)c.CreateDate).FirstOrDefaultAsync(ct);
            return !created.HasValue || created.Value < start.Value;
        }
    }

    /// <summary>Who is an AI seat: the service refuses before the data layer does, with a Persian message.</summary>
    internal static class EnActors
    {
        public static Task<bool> IsSeatAsync(IUnitOfWork uow, int userId, CancellationToken ct) => GlText.IsAiSeatUserAsync(uow, userId, ct);

        /// <summary>The user is the seat of the main (master) agent.</summary>
        public static async Task<bool> IsMasterSeatAsync(IUnitOfWork uow, int userId, CancellationToken ct)
        {
            if (userId <= 0) return false;
            string name = await uow.Set<Entities.Identity.User>().AsNoTracking().Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct);
            return DataLayer.Context.SeatApprovalGuard.IsMasterName(name);
        }

        /// <summary>The user is the seat of the project-initiation agent (AG-INIT), identified by its seat user name, never by a display name.</summary>
        public static async Task<bool> IsInitSeatAsync(IUnitOfWork uow, int userId, CancellationToken ct)
        {
            if (userId <= 0) return false;
            string name = await uow.Set<Entities.Identity.User>().AsNoTracking().Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct);
            return DataLayer.Context.SeatApprovalGuard.IsInitName(name);
        }

        public static async Task<int[]> SeatIdsAsync(IUnitOfWork uow, CancellationToken ct)
        {
            string prefix = global::Services.NasbNiro.Ai.AiSeatNames.UserPrefix;
            return (await uow.Set<Entities.Identity.User>().AsNoTracking().Where(u => u.UserName.StartsWith(prefix)).OrderBy(u => u.Id).Select(u => u.Id).Take(200).ToListAsync(ct)).ToArray();
        }
    }

    /// <summary>A file of a record: it exists in the store, has an allowed type, was uploaded by the one who attaches it and is not on another line of a dossier.</summary>
    internal static class EnFiles
    {
        /// <summary>A file attached to a clarification: it exists, has an allowed type and was uploaded by the one who attaches it.</summary>
        public static async Task<string> CheckOwnAsync(IUnitOfWork uow, Guid documentId, int me, CancellationToken ct)
        {
            var file = await uow.Set<Entities.Model.Document>().AsNoTracking().Where(d => d.Id == documentId).Select(d => new { d.Path, d.UploadedByUserId }).FirstOrDefaultAsync(ct);
            if (file == null) return "فایل در مخزن اسناد پیدا نشد؛ ابتدا فایل را بارگذاری کنید.";
            string extension = (Path.GetExtension(file.Path ?? string.Empty) ?? string.Empty).ToLowerInvariant();
            if (!AttachmentRules.AllowedExtensions.Contains(extension)) return AttachmentText.ExtensionRefused(extension);
            if (!file.UploadedByUserId.HasValue || file.UploadedByUserId.Value != me) return "فایل را باید خود شما بارگذاری کرده باشید.";
            return null;
        }

        public static async Task<string> CheckAsync(IUnitOfWork uow, Guid documentId, int me, int? ownLineId, CancellationToken ct)
        {
            var file = await uow.Set<Entities.Model.Document>().AsNoTracking().Where(d => d.Id == documentId).Select(d => new { d.Path, d.UploadedByUserId }).FirstOrDefaultAsync(ct);
            if (file == null) return "فایل در مخزن اسناد پیدا نشد؛ ابتدا فایل را بارگذاری کنید.";
            string extension = (Path.GetExtension(file.Path ?? string.Empty) ?? string.Empty).ToLowerInvariant();
            if (!AttachmentRules.AllowedExtensions.Contains(extension)) return AttachmentText.ExtensionRefused(extension);
            if (!file.UploadedByUserId.HasValue || file.UploadedByUserId.Value != me) return "فایل را باید خود شما بارگذاری کرده باشید.";
            if (await uow.Set<EnPlatformDocument>().AnyAsync(d => d.DocumentId == documentId && (!ownLineId.HasValue || d.Id != ownLineId.Value), ct)) return "این فایل برای ردیف دیگری از پرونده پلتفرم ثبت شده است.";
            return null;
        }
    }

    internal static class EnEvents
    {
        public static void Add(IUnitOfWork uow, EnCase c, string kind, int? userId, string note)
        {
            uow.Set<EnCaseEvent>().Add(new EnCaseEvent { CaseId = c.Id, Kind = kind, At = DateTimeOffset.Now, UserId = userId > 0 ? userId : null, Note = EnText.Cut(note, 1000) });
        }

        /// <summary>For a case that has no key yet (it is added in the same save): the event waits for the key through the navigation-free pair.</summary>
        public static async Task AddAfterKeyAsync(IUnitOfWork uow, EnCase c, string kind, int? userId, string note, CancellationToken ct)
        {
            if (c.Id <= 0) await uow.SaveChangesAsync(ct);
            Add(uow, c, kind, userId, note);
        }
    }

    internal static class EnUsers
    {
        public static async Task<Dictionary<int, string>> NamesAsync(IUnitOfWork uow, IEnumerable<int?> ids, CancellationToken ct) => await InventoryUsers.GetNamesAsync(uow, ids, ct);
    }

    internal static class EnPaging
    {
        public static async Task<PagedList<TDto>> CutAsync<TRow, TDto>(IQueryable<TRow> ordered, int pageNumber, int pageSize, Func<List<TRow>, Task<List<TDto>>> map, CancellationToken ct)
        {
            int total = await ordered.CountAsync(ct);
            int size = EnText.Size(pageSize);
            int number = pageNumber <= 0 ? 1 : pageNumber;
            List<TRow> rows = await ordered.Skip((number - 1) * size).Take(size).ToListAsync(ct);
            List<TDto> dtos = await map(rows);
            PagedList<TDto> page = await PagedList<TDto>.CreateAsync(dtos, 1, 0, ct);
            page.TotalCount = total;
            page.CurrentPage = number;
            page.PageSize = size;
            page.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return page;
        }
    }
}
