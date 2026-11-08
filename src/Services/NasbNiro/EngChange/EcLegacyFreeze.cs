using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Services.NasbNiro.ProductBank;

namespace Services.NasbNiro.EngChange
{
    /// <summary>Package A2 (single BOM master): the legacy Bom / EquipmentBom are read only. The switch is a safety-profile setting (two-person change); it is on unless a transition is running.</summary>
    public static class EcLegacyFreeze
    {
        public const string Message = "BOM قدیمی (Bom / BOM تجهیزات) بسته شده و فقط خواندنی است؛ تنها مرجع BOM، «فهرست مواد (BOM)» بانک محصول است. محصول را در بانک محصول بسازید و برای پروژه از «مبنای BOM پروژه» بگیرید.";

        /// <summary>The Persian refusal while the legacy BOM is frozen, else null.</summary>
        public static async Task<string> ProblemAsync(IUnitOfWork uow, CancellationToken ct)
        {
            var settings = await PbSettingKeys.LoadAsync(uow, ct);
            return settings[PbSettingKeys.LegacyBomFrozen] == "false" ? null : Message;
        }
    }
}
