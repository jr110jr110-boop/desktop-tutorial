using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Entities.Model.NasbNiro.Engineering;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EngineeringHandoverTests;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1c: C4 approved codes.</summary>
    [TestClass]
    public class EngineeringE1cC4Tests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        [TestMethod]
        public async Task C4_Approved_Codes_List_Has_Code_And_Title_Of_Approved_Lists_Only()
        {
            AppHost host = Started("e1ccodes");
            await ApprovedList(host, "VDDR-C4", ("DWG", true, false));
            Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref("VDDR-DRAFT-ONLY"), Ct)));   // a draft is not offered
            var codes = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().GetApprovedCodesAsync(Ct)));
            Assert.AreEqual(1, codes.Count);
            Assert.AreEqual("VDDR-C4", codes[0].Code);
            Assert.IsFalse(string.IsNullOrWhiteSpace(codes[0].Title));
            Refused(await host.As(AppHost.Principal(2999, "stranger", "WELDER"), p => p.GetRequiredService<IEnReferenceService>().GetApprovedCodesAsync(Ct)));
        }

    }
}
