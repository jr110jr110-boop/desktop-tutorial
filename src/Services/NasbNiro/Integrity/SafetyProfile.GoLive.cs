using System.Collections.Generic;
using Entities.Model.NasbNiro.Integrity;
using Services.NasbNiro.GoLive;

namespace Services.NasbNiro.Integrity
{
    // Package G (go-live cut-over): the cut-over day and the switches of the new gates. Every key changes only through the two-person route; the strict value is the default.
    public static partial class SafetyProfile
    {
        private static IEnumerable<SafetySettingSpec> GoLiveSpecs()
        {
            const SafetyStore Q = SafetyStore.Quality;
            // 0 is strict (the instant of the update); any stored day is an override of it, so moving the cut-over to a later day makes more of the existing work legacy and is a weakening
            yield return new SafetySettingSpec { Store = Q, Key = GlSettingKeys.ControlStartDate, Kind = SafetyKind.NumberAtMost, Strict = "0", Min = 0, Max = 99991231, Title = "روز آغاز کنترل پیکربندی و کیفیت جابه‌جا نشود (جابه‌جایی به روز دیرتر، کارهای بیشتری را «قدیمی» و معاف می‌کند)" };
            yield return new SafetySettingSpec { Store = Q, Key = GlSettingKeys.LegacyReasonMinChars, Kind = SafetyKind.NumberAtLeast, Strict = "15", Min = 5, Max = 200, Title = "حداقل نویسه دلیل پذیرش مورد قدیمی" };
            yield return new SafetySettingSpec { Store = Q, Key = GlSettingKeys.PoCiteDrawing, Kind = SafetyKind.FlagMustBeOn, Strict = "1", Min = 0, Max = 1, Title = "ردیف سفارش خرید شماره و بازنگری نقشه منتشرشده را دارد (ISO 9001 بند 8.4.3)" };
            yield return new SafetySettingSpec { Store = Q, Key = GlSettingKeys.TransmittalRevisionFromEdms, Kind = SafetyKind.FlagMustBeOn, Strict = "1", Min = 0, Max = 1, Title = "بازنگری ترنسمیتال از رکورد بازنگری EDMS می‌آید" };
            yield return new SafetySettingSpec { Store = Q, Key = GlSettingKeys.StampMaxMb, Kind = SafetyKind.NumberAtMost, Strict = "50", Min = 1, Max = 200, Title = "بیشینهٔ حجم PDF برای مهر مستقیم (بالا بردن آن حافظهٔ هر درخواست را زیاد می‌کند)" };
        }
    }
}
