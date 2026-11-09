using System.Collections.Generic;
using Entities.Model.NasbNiro.Integrity;

namespace Services.NasbNiro.Integrity
{
    // Package A2 (engineering change, effectivity and a single BOM master): the switches this package adds to the strict production profile.
    public static partial class SafetyProfile
    {
        private static IEnumerable<SafetySettingSpec> EngChangeSpecs()
        {
            yield return new SafetySettingSpec
            {
                Store = SafetyStore.ProductBank, Key = "EngChange.LegacyBomFrozen", Kind = SafetyKind.BoolText, Strict = "true",
                Title = "BOM قدیمی (Bom / BOM تجهیزات) فقط‌خواندنی باشد و BOM محصول مرجع یگانه باشد (بسته A2)",
                Note = "تنها برای دوره گذار و ساخت BOM قدیمی لازم برای پروژه‌های بدون مبنای منتشرشده خاموش شود",
            };
        }
    }
}
