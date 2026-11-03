using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.Inventory;

namespace Services.NasbNiro.Inventory
{
    /// <summary>
    /// The maker-checker and state rules of the NCR actions (sign, close, cancel), written once. The service runs the action with them and the NCR read computes the per-caller flags
    /// (<c>CanSign</c>, <c>CanClose</c>, <c>CanCancel</c>) with them, so the screen never offers an act the service would refuse. Each returns the Persian reason, or null when the caller may act.
    /// </summary>
    internal static class NcrRules
    {
        public const string MakerSignMessage = " (ثبت‌کننده یا ویرایشگر گزارش نمی‌تواند آن را امضا کند؛ کاربر دیگری از همین نقش امضا کند)";

        /// <summary>The user raised or edited the report: its maker (R1q D3, review H7).</summary>
        public static bool IsMaker(NonConformance ncr, int user) => global::Services.NasbNiro.Finance.LedgerMath.Touched(ncr.CreatorUserId, ncr.EditorUserIds, user);

        /// <summary>The users who edited the report, as a list (the column keeps them between commas).</summary>
        public static List<int> Editors(string ids) =>
            (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x.Trim(), out int v) ? v : 0).Where(v => v > 0).Distinct().ToList();

        /// <summary>The role that signs one kind of approval; null for an unknown kind.</summary>
        public static string RoleOf(ApprovalKind kind) =>
            kind == ApprovalKind.Quality ? InventoryRoles.RoleQualitySigner
            : kind == ApprovalKind.Engineering ? InventoryRoles.RoleEngineering
            : kind == ApprovalKind.Manufacturing ? InventoryRoles.RoleManufacturing
            : null;

        /// <summary>
        /// Signing: the report is open, classified and decided (a major one has its committee), and the caller holds the role, is not the maker and has not signed another part.
        /// <paramref name="committeeMin"/> is the minimum committee size of a major NCR (company setting).
        /// </summary>
        public static string SignProblem(NonConformance ncr, ApprovalKind kind, bool concession, int me, Func<string, bool> inRole, int committeeMin)
        {
            if (ncr.Status != NcrStatus.Open) return "فقط گزارش باز تایید می‌شود.";
            if (ncr.Severity == NcrSeverity.NotClassified) return "ابتدا نوع عدم انطباق (جزئی / عمده) را مشخص کنید.";
            if (ncr.Disposition == NcrDisposition.None) return "ابتدا تصمیم (مجوز ارفاقی، دوباره‌کاری، تعمیر، ضایعات، مرجوعی) را مشخص کنید.";
            if (ncr.Severity == NcrSeverity.Major && (ncr.CommitteeMinutesNumber == null || ncr.CommitteeMembers == null))
                return "عدم انطباق عمده با تصمیم کمیته فنی است؛ شماره صورت‌جلسه و اعضای کمیته را وارد کنید.";
            if (ncr.Severity == NcrSeverity.Major && Quality.QualityText.SplitList(ncr.CommitteeMembers).Count < committeeMin)
                return $"کمیته عدم انطباق عمده باید حداقل {committeeMin} عضو (QC، مهندسی، ساخت) داشته باشد؛ اعضا را با ویرگول یا در خط‌های جدا بنویسید.";
            if (ncr.Disposition == NcrDisposition.Scrap && ncr.OwnedByContractor)
                return "کالای متعلق به پیمانکار/مشتری ضایعات شرکت نمی‌شود؛ تصمیم «مرجوعی» را انتخاب کنید.";

            bool maker = IsMaker(ncr, me);
            if (concession)
            {
                if (!Quality.NcrGates.NeedsConcession(ncr.Disposition)) return "این تصمیم مجوز ارفاقی لازم ندارد.";
                if (!inRole(InventoryRoles.RoleEngineering)) return InventoryMessages.NotAllowed + " (مجوز ارفاقی را معاونت طراحی و ساخت صادر می‌کند)";
                if (maker) return InventoryMessages.SameUser + MakerSignMessage;
                // the permit is the last act on a decision the quality department has already signed (review H4): never before the QC signature
                if (!ncr.QualityApprovedAt.HasValue) return "مجوز ارفاقی پس از امضای کنترل کیفیت صادر می‌شود؛ ابتدا QC تصمیم را امضا کند.";
                if (ncr.ConcessionNumber == null) return "شماره مجوز ارفاقی را وارد کنید.";
                if (ncr.CustomerApprovalRequired && ncr.CustomerApprovalReference == null)
                    return "قرارداد تایید انحراف توسط مشتری را لازم دارد؛ شماره نامه/تایید مشتری را وارد کنید.";
                return null;
            }
            string role = RoleOf(kind);
            if (role == null) return InventoryMessages.InvalidData;
            if (!inRole(role)) return InventoryMessages.NotAllowed;
            if (new[] { ncr.QualityApprovedByUserId, ncr.EngineeringApprovedByUserId, ncr.ManufacturingApprovedByUserId }.Contains(me)) return InventoryMessages.SameUser;
            if (maker)
                return InventoryMessages.SameUser + (kind == ApprovalKind.Quality
                    ? " (ثبت‌کننده یا ویرایشگر گزارش نمی‌تواند امضای کنترل کیفیت را بدهد؛ کاربر دیگری از QC یا مدیر QA امضا کند)"
                    : MakerSignMessage);
            return null;
        }

        /// <summary>
        /// Closing (after the decision was carried out): the report is executed, the closer is not its maker and the root cause and corrective action are recorded. What the closer types
        /// (the effectiveness check and, for a rework or repair, the re-inspection) is checked by the service.
        /// </summary>
        public static string CloseProblem(NonConformance ncr, int me)
        {
            if (ncr.Status != NcrStatus.Executed) return "گزارش پس از اجرای تصمیم بسته می‌شود.";
            if (IsMaker(ncr, me)) return InventoryMessages.SameUser + " (ثبت‌کننده یا ویرایشگر گزارش نمی‌تواند آن را ببندد؛ کاربر دیگری از QC یا QA ببندد)";
            if (ncr.CauseCategory == NcrCauseCategory.None || ncr.RootCause == null || ncr.CorrectiveAction == null)
                return "پیش از بستن، دسته علت ریشه‌ای، شرح علت و اقدام اصلاحی را ثبت کنید.";
            return null;
        }

        /// <summary>
        /// Cancelling: QA manager or QC user, the report is still open, and a QC user (not the QA manager) never cancels a report he raised or edited. When <paramref name="validateReason"/> is set the
        /// reason that was typed is checked too (the flag has none yet).
        /// </summary>
        public static string CancelProblem(NonConformance ncr, int me, Func<string, bool> inRole, bool validateReason, string reason)
        {
            if (!inRole("QA,QC")) return InventoryMessages.NotAllowed + " (ابطال گزارش عدم انطباق را مدیر QA یا کارشناس QC انجام می‌دهد)";
            if (ncr.Status != NcrStatus.Open) return "فقط گزارش باز ابطال می‌شود.";
            if (validateReason)
            {
                if (reason == null) return "علت ابطال را بنویسید.";
                if (reason.Length > 1000) return "علت ابطال نباید بیش از ۱۰۰۰ نویسه باشد.";
            }
            // the QA manager may cancel any report (always with a reason, and the cancellation is recorded); a QC user only a report he did not raise or edit
            if (IsMaker(ncr, me) && !inRole("QA")) return InventoryMessages.SameUser + " (ثبت‌کننده یا ویرایشگر گزارش نمی‌تواند آن را ابطال کند؛ مدیر QA یا کاربر دیگری از QC ابطال کند)";
            return null;
        }
    }
}
