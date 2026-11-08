using System.Collections.Generic;
using Entities.Model.NasbNiro.Integrity;
using Services.NasbNiro.ProductBank;

namespace Services.NasbNiro.Integrity
{
    // Package A1 (design and development control): the weakening switches of the design gates. Every key changes only through the two-person route; the strict value is the default.
    public static partial class SafetyProfile
    {
        private static IEnumerable<SafetySettingSpec> DesignSpecs()
        {
            const SafetyStore Q = SafetyStore.Quality;
            yield return new SafetySettingSpec { Store = Q, Key = DesignSettingKeys.VerificationRequired, Kind = SafetyKind.FlagMustBeOn, Strict = "1", Min = 0, Max = 1, Title = "انتشار BOM و نقشه خروجی طراحی نیازمند بازبینی تایید طراحی (Verification) پذیرفته" };
            yield return new SafetySettingSpec { Store = Q, Key = DesignSettingKeys.ValidationRequired, Kind = SafetyKind.FlagMustBeOn, Strict = "1", Min = 0, Max = 1, Title = "ممیزی پیکربندی عملکردی: صحه‌گذاری طراحی (Validation) پذیرفته پیش از ترخیص" };
            yield return new SafetySettingSpec { Store = Q, Key = DesignSettingKeys.MinIndependent, Kind = SafetyKind.NumberAtLeast, Strict = "1", Min = 1, Max = 5, Title = "حداقل شرکت‌کننده مستقل در بازبینی طراحی" };
            yield return new SafetySettingSpec { Store = Q, Key = DesignSettingKeys.ReasonMinChars, Kind = SafetyKind.NumberAtLeast, Strict = "15", Min = 5, Max = 200, Title = "حداقل نویسه دلیل در کنترل طراحی (رفع تعارض، معافیت، پذیرش مشروط، ابطال)" };
        }
    }
}
