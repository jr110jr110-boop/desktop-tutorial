using System;
using System.Collections.Generic;
using System.Globalization;
using Entities.Model.NasbNiro.Quality;

namespace Services.NasbNiro.GoLive
{
    /// <summary>Package G: the settings of the go-live cut-over (store Quality, whole numbers). Every one is on the safety profile: a change that weakens a control goes through the two-person route.</summary>
    public static class GlSettingKeys
    {
        /// <summary>
        /// The day the quality and configuration control started (Shamsi number yyyyMMdd). 0 = the instant recorded once by the first start of the program after the update (GlActivation), which is the day the update ran.
        /// What was created / released before it is legacy: the gates of Package B that would stop work that already exists show a Persian note and an AUDIT observation instead (never silently); everything after it is strict.
        /// Moving the day later makes more of the existing work legacy, so it is a weakening and goes through the two-person route.
        /// </summary>
        public const string ControlStartDate = "Quality.ControlStartDate";

        /// <summary>The shortest reason (characters) of an adoption, a void of an adoption and the evidence note of a legacy QC acceptance.</summary>
        public const string LegacyReasonMinChars = "Gl.Legacy.ReasonMinChars";

        /// <summary>1: a purchase order line for an item with a released drawing carries the drawing number and revision (resolved by the EDMS, ISO 9001 8.4.3) and watches its supersede.</summary>
        public const string PoCiteDrawing = "Gl.Po.CiteDrawing";

        /// <summary>1: every new transmittal line of a document that has revision records takes the revision from the record; free text only for a document with no revision record (flagged).</summary>
        public const string TransmittalRevisionFromEdms = "Gl.Transmittal.RevisionFromEdms";

        public const string Source = "★ تصمیم مدیرعامل (عدم تحمل انحراف؛ گذار بدون توقف کار موجود)";

        public static IEnumerable<QualitySetting> Defaults()
        {
            yield return new QualitySetting { Key = ControlStartDate, Value = 0, Title = "روز آغاز کنترل پیکربندی و کیفیت (تاریخ شمسی به‌صورت yyyyMMdd؛ ۰ = روز نخستین اجرای برنامه پس از به‌روزرسانی): کارهای پیش از آن «قدیمی» شمرده می‌شوند", Source = Source };
            yield return new QualitySetting { Key = LegacyReasonMinChars, Value = 15, Title = "حداقل نویسه دلیل پذیرش مورد قدیمی و یادداشت مدرک پذیرش QC قدیمی", Source = Source };
            yield return new QualitySetting { Key = PoCiteDrawing, Value = 1, Title = "ردیف سفارش خرید قلم دارای نقشه منتشرشده، شماره و بازنگری نقشه را (از EDMS) در خود دارد", Source = "★ ISO 9001 بند 8.4.3" };
            yield return new QualitySetting { Key = TransmittalRevisionFromEdms, Value = 1, Title = "بازنگری هر ردیف ترنسمیتال از رکورد بازنگری EDMS می‌آید (متن آزاد فقط برای سند بدون رکورد بازنگری و با علامت قدیمی)", Source = "★ IEC 82045 / ISO 19650" };
        }

        public static bool TryGetBounds(string key, out int min, out int max)
        {
            min = 0; max = 0;
            switch (key)
            {
                case ControlStartDate: max = 99991231; return true;
                case LegacyReasonMinChars: min = 5; max = 200; return true;
                case PoCiteDrawing: case TransmittalRevisionFromEdms: max = 1; return true;
                default: return false;
            }
        }
    }

    /// <summary>Package G: the Persian texts shown on records and checks.</summary>
    public static class GlText
    {
        public const string PcaLegacy = "قدیمی، پیش از شروع کنترل پیکربندی";
        public const string AiNever = "این کار با انسان است؛ صندلی هوش مصنوعی آن را انجام نمی‌دهد.";
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string InvalidData = "اطلاعات ارسال‌شده کامل نیست.";
        public const string Concurrent = "اطلاعات هم‌زمان توسط کاربر دیگری تغییر کرد. صفحه را دوباره باز کنید و دوباره تلاش کنید.";
        public const string SameUser = "تفکیک وظایف: این کار را شخص دیگری انجام می‌دهد.";

        public static string Cut(string s, int max) => s == null || s.Length <= max ? s : s.Substring(0, max);

        public static string Over(string text, int max, string label) => text != null && text.Trim().Length > max ? $"{label} بیش از {max.ToString(CultureInfo.InvariantCulture)} نویسه است." : null;
    }
}

namespace Services.NasbNiro.GoLive
{
    /// <summary>Package G: the fixed texts of the controlled printed copy («نسخه کنترل‌شده») and of an uncontrolled print.</summary>
    public static class GlStampText
    {
        public const string Footer = "نسخه کنترل‌نشده پس از چاپ";
        public const string Uncontrolled = "نسخه کنترل‌نشده";

        public static string Header(string documentNumber, string revision, string releasedAtText, string printedAtText, string userName) =>
            $"نسخه کنترل‌شده | سند {documentNumber} | بازنگری {revision} | تاریخ انتشار {releasedAtText} | چاپ: {printedAtText} توسط {userName}";
    }
}
