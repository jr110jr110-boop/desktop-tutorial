using System;
using System.Linq;
using Entities.Model.NasbNiro.Quality;

namespace Services.NasbNiro.Quality
{
    /// <summary>
    /// The role, state and maker-checker rules of the ITP plan actions, written once: <see cref="ItpService.ItpActionAsync"/> runs the action with them and the ITP read computes the per-caller flags
    /// (<c>CanSubmit</c>, <c>CanApprove</c>, <c>CanUse</c>, <c>CanClose</c>, <c>CanRevise</c>) with them. Each returns the Persian reason, or null when the caller may act.
    /// </summary>
    internal static class ItpRules
    {
        /// <summary>The ids of the makers of the plan: who submitted it and who edited it since the last client approval (an auto-drafted plan has no human creator).</summary>
        public static bool IsMaker(Itp itp, int me) =>
            itp.SubmittedByUserId == me || global::Services.NasbNiro.Finance.LedgerMath.Touched(itp.AutoDrafted ? 0 : itp.CreatorUserId, itp.EditorUserIds, me);

        public static string SubmitProblem(Itp itp, Func<string, bool> inRole)
        {
            if (!inRole(QualityRoles.Planners)) return QualityText.NotAllowed;
            if (itp.Status != ItpStatus.Draft) return "فقط برنامه پیش‌نویس برای تایید کارفرما ارسال می‌شود.";
            if (itp.Activities.Count == 0) return "برنامه بدون فعالیت ارسال نمی‌شود.";
            return null;
        }

        /// <summary>R1q H6: the plan is approved by somebody who did not prepare, change, revise or submit it (a vendor cannot approve for the client alone). The reference and date typed by the approver are checked by the service.</summary>
        public static string ApproveProblem(Itp itp, int me, Func<string, bool> inRole)
        {
            if (!inRole(QualityRoles.QualityManagers)) return QualityText.NotAllowed;
            if (itp.Status != ItpStatus.Submitted) return "ابتدا برنامه را برای کارفرما ارسال کنید.";
            if (IsMaker(itp, me)) return "تایید کارفرما را باید شخصی ثبت کند که برنامه را تهیه، ویرایش، بازنگری یا ارسال نکرده است (تفکیک وظایف).";
            return null;
        }

        public static string UseProblem(Itp itp, Func<string, bool> inRole)
        {
            if (!inRole(QualityRoles.QualityManagers)) return QualityText.NotAllowed;
            if (itp.Status != ItpStatus.ClientApproved) return "فقط برنامه تاییدشده در حال اجرا قرار می‌گیرد.";
            return null;
        }

        public static string CloseProblem(Itp itp, Func<string, bool> inRole)
        {
            if (!inRole(QualityRoles.QualityManagers)) return QualityText.NotAllowed;
            if (itp.Status != ItpStatus.ClientApproved && itp.Status != ItpStatus.InUse) return "فقط برنامه تاییدشده بسته می‌شود.";
            if (itp.Activities.Any(a => !ItpService.IsTerminal(a.Status))) return "همه فعالیت‌ها باید انجام‌شده، معاف یا غیرقابل‌اجرا باشند.";
            return null;
        }

        /// <summary>The state and role of a revision; the reason typed by the planner is checked when the revision is opened.</summary>
        public static string ReviseProblem(Itp itp, Func<string, bool> inRole)
        {
            if (!inRole(QualityRoles.Planners)) return QualityText.NotAllowed;
            if (itp.Status != ItpStatus.ClientApproved && itp.Status != ItpStatus.InUse) return "بازنگری فقط برای برنامه تاییدشده ثبت می‌شود.";
            return null;
        }
    }

    /// <summary>The rules of the release note actions (sign = release, cancel) written once, for the action and for the per-caller flags of the note.</summary>
    internal static class ReleaseNoteRules
    {
        /// <summary>R1q D22: the person who drafted or edited the note cannot release it (maker-checker).</summary>
        public static bool IsMaker(ReleaseNote note, int me) => global::Services.NasbNiro.Finance.LedgerMath.Touched(note.CreatorUserId, note.EditorUserIds, me);

        public static string SignProblem(ReleaseNote note, int me, Func<string, bool> inRole)
        {
            if (!inRole(QualityRoles.Inspectors)) return QualityText.NotAllowed + " (برگه ترخیص و گواهی انطباق را مدیر QC امضا می‌کند)";
            if (note.Status != ReleaseNoteStatus.Draft) return "فقط برگه پیش‌نویس صادر می‌شود.";
            if (IsMaker(note, me)) return QualityText.SameUser + " (برگه را کسی که آن را تنظیم یا ویرایش کرده نمی‌تواند صادر کند؛ مدیر QC دیگری امضا کند)";
            return null;
        }

        /// <summary>Role and state of a cancellation; the reason typed by the caller is checked by the service.</summary>
        public static string CancelProblem(ReleaseNote note, Func<string, bool> inRole)
        {
            if (!inRole(QualityRoles.QualityManagers)) return QualityText.NotAllowed;
            if (note.Status == ReleaseNoteStatus.Cancelled) return "برگه قبلاً لغو شده است.";
            return null;
        }
    }
}
