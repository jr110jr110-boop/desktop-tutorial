using System.Collections.Generic;
using System.Globalization;
using Entities.Model.NasbNiro.Integrity;
using Services.NasbNiro.AuditSeat;

namespace Services.NasbNiro.Integrity
{
    // Package 9 (AI internal auditor): every switch that weakens the audit is a security-relevant setting changed only by the two-person route.
    public static partial class SafetyProfile
    {
        private static IEnumerable<SafetySettingSpec> AuditSeatSpecs()
        {
            const SafetyStore Q = SafetyStore.Quality;
            foreach (var s in AusSettingKeys.Strict())
            {
                SafetyKind kind = s.Kind == "on" ? SafetyKind.FlagMustBeOn : s.Kind == "atmost" ? SafetyKind.NumberAtMost : SafetyKind.NumberAtLeast;
                yield return new SafetySettingSpec { Store = Q, Key = s.Key, Kind = kind, Strict = s.Strict.ToString(CultureInfo.InvariantCulture), Min = s.Min, Max = s.Max, Title = s.Title };
            }
            yield return new SafetySettingSpec { Store = Q, Key = AusSettingKeys.Prefix + "Rule.*", Kind = SafetyKind.FlagMustBeOn, Strict = "1", Min = 0, Max = 1, Title = "قاعده ممیزی فعال بماند", Note = "غیرفعال کردن هر قاعده، تصمیمی دو نفره است" };
        }
    }
}
