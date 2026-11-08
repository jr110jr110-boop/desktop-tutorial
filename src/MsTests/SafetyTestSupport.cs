using System.Linq;
using System.Threading;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;

namespace MsTests
{
    /// <summary>
    /// Package D: a security-relevant setting (the strict production profile) is no longer saved by the settings page of its module; a test that needs a non-default value for its own scenario
    /// writes the row directly, the way an installation would have it after a two-person change. The two-person flow itself is tested in <see cref="RecordsIntegrityPdTests"/>.
    /// </summary>
    internal static class SafetyTestSupport
    {
        public static void SetFinance(AppHost host, string key, string value) =>
            host.Write(uow =>
            {
                FinanceSeed.EnsureAsync(uow, CancellationToken.None).GetAwaiter().GetResult();
                FinanceSeed.EnsureSettingsAsync(uow, CancellationToken.None).GetAwaiter().GetResult();
                uow.Set<FnSetting>().First(s => s.Key == key).Value = value;
                uow.SaveChanges();
            });

        public static void SetProcurement(AppHost host, string key, decimal value) =>
            host.Write(uow =>
            {
                Services.NasbNiro.Procurement.ProcurementSettingKeys.LoadAsync(uow, CancellationToken.None).GetAwaiter().GetResult();
                uow.Set<ProcurementSetting>().First(x => x.Key == key).Value = value;
                uow.SaveChanges();
            });

        public static void SetProductBank(AppHost host, string key, string value) =>
            host.Write(uow =>
            {
                PbSetting row = uow.Set<PbSetting>().FirstOrDefault(s => s.Key == key);
                if (row == null) uow.Set<PbSetting>().Add(row = AppHost.Add(uow, new PbSetting { Key = key, Title = key, Source = "test" }));
                row.Value = value;
                uow.SaveChanges();
            });
    }
}
