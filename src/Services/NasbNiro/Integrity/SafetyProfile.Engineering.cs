using System.Collections.Generic;
using Entities.Model.NasbNiro.Integrity;
using Services.NasbNiro.Engineering;

namespace Services.NasbNiro.Integrity
{
    // ENG-E1 (engineering module): the day the engineering control starts and the longest document debt of an emergency waiver. Both change only through the two-person route; the strict value is the default.
    public static partial class SafetyProfile
    {
        private static IEnumerable<SafetySettingSpec> EngineeringSpecs()
        {
            const SafetyStore Q = SafetyStore.Quality;
            yield return new SafetySettingSpec { Store = Q, Key = EnSettingKeys.WaiverMaxDebtDays, Kind = SafetyKind.NumberAtMost, Strict = "90", Min = 1, Max = 365, Title = "حداکثر مهلت بدهی مدارک در معافیت اضطراری قفل تحویل مدارک مهندسی (روز)" };
        }
    }
}
