using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Engineering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EnKit;

namespace MsTests
{
    [TestClass]
    public class EngineeringE1eReturnBoxTests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        [TestMethod]
        public async Task The_Previous_Return_Box_Follows_The_Round_Right_Before_Skipping_Void_Rounds()
        {
            AppHost host = Started("e1e6");
            Func<int, string, int, EnHandoverStatus, string, int> add = (caseId, number, round, status, reason) =>
            {
                int id = 0;
                host.Write(u =>
                {
                    int contract = u.Set<EnCase>().Single(x => x.Id == caseId).ContractId.Value;
                    EnHandover h = AppHost.Add(u, new EnHandover { Number = number, CaseId = caseId, ContractId = contract, Round = round, Status = status, PreparedByUserId = 1001, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
                    if (status == EnHandoverStatus.Returned) { h.ReturnedByUserId = 2141; h.ReturnedAt = DateTimeOffset.Now; h.ReturnReason = reason; }
                    u.SaveChanges(); id = h.Id;
                }, Seeder);
                return id;
            };
            Func<int, Task<EnHandoverDto>> get = id => host.As(Mgr1, p => p.GetRequiredService<IEnHandoverService>().GetHandoverAsync(id, Ct)).ContinueWith(t => Ok(t.Result));
            // returned, then a voided draft, then a new round: the voided round is skipped
            int a = EngineeringPart3Tests.Seed(host, EnClassification.New, EnCaseStatus.InEngineering);
            add(a, "EHO-A1", 1, EnHandoverStatus.Returned, "نقشه‌ها کامل نیست و باید اصلاح شود");
            add(a, "EHO-A2", 2, EnHandoverStatus.Void, null);
            int a3 = add(a, "EHO-A3", 3, EnHandoverStatus.Draft, null);
            EnHandoverDto shown = await get(a3);
            Assert.AreEqual("EHO-A1", shown.PreviousReturnNumber);
            Assert.AreEqual("نقشه‌ها کامل نیست و باید اصلاح شود", shown.PreviousReturnReason);
            // returned, then an accepted later round, then a new round: the round right before was not returned
            int b = EngineeringPart3Tests.Seed(host, EnClassification.New, EnCaseStatus.InEngineering);
            add(b, "EHO-B1", 1, EnHandoverStatus.Returned, "نقشه‌ها کامل نیست و باید اصلاح شود");
            int b2 = add(b, "EHO-B2", 2, EnHandoverStatus.Accepted, null);
            int b3 = add(b, "EHO-B3", 3, EnHandoverStatus.Draft, null);
            Assert.AreEqual("EHO-B1", (await get(b2)).PreviousReturnNumber);
            Assert.IsNull((await get(b3)).PreviousReturnNumber);
        }
    }
}
