using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;

namespace MsTests
{
    /// <summary>Shared helpers of the ENG-E1 tests (engineering module, external workflow).</summary>
    internal static class EnKit
    {
        public static readonly CancellationToken Ct = CancellationToken.None;
        public static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        public static readonly ClaimsPrincipal Mgr1 = AppHost.Principal(2101, "eng.manager.one", "ENGINEERING");
        public static readonly ClaimsPrincipal Mgr2 = AppHost.Principal(2102, "eng.manager.two", "ENGINEERING");
        public static readonly ClaimsPrincipal Eng1 = AppHost.Principal(2111, "engineer.one", "ENGINEER");
        public static readonly ClaimsPrincipal Eng2 = AppHost.Principal(2112, "engineer.two", "ENGINEER");
        public static readonly ClaimsPrincipal Ceo = AppHost.Principal(2121, "ceo.one", "CEO");
        public static readonly ClaimsPrincipal Sales1 = AppHost.Principal(2131, "sales.one", "SALES");
        public static readonly ClaimsPrincipal Comm1 = AppHost.Principal(2132, "commercial.one", "COMMERCIAL");
        public static readonly ClaimsPrincipal Pmo1 = AppHost.Principal(2141, "pmo.one", "PMO");
        public static readonly ClaimsPrincipal Pmo2 = AppHost.Principal(2142, "pmo.two", "PMO");
        public static readonly ClaimsPrincipal Qa1 = AppHost.Principal(2151, "qa.one", "QA");
        public static readonly ClaimsPrincipal Prod1 = AppHost.Principal(2152, "production.one", "PRODUCTIONMANAGER");
        public static readonly ClaimsPrincipal Nobody = AppHost.Principal(2199, "nobody", "SECURITY");

        public static AppHost NewHost(string name) => new AppHost("En_" + name + "_" + Guid.NewGuid().ToString("N")).WithoutReadinessGate();

        /// <summary>The control has started a day ago: everything created from now on is controlled.</summary>
        public static void StartControl(AppHost host, int daysAgo = 1)
        {
            host.Write(u =>
            {
                AppHost.Add(u, new EnActivation { StartAt = DateTimeOffset.Now.AddDays(-daysAgo), Note = "test" });
                u.SaveChanges();
            }, Seeder);
        }

        public static async Task<ClaimsPrincipal> SeatAsync(AppHost host, string code = "AG-ENG", params string[] roles)
        {
            Dictionary<string, AiActingUser> seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct));
            return AppHost.Principal(seats[code].UserId, AiSeatNames.UserName(code), roles.Length == 0 ? new[] { "ENGINEER" } : roles);
        }

        public static EnSaveReferenceDto Ref(string code = "VDDR-TEST", params (string Type, bool Required, bool Tender)[] lines)
        {
            if (lines.Length == 0) lines = new[] { ("BOM", true, true), ("OPC", true, false), ("DWG", true, true), ("NOTE", false, false) };
            return new EnSaveReferenceDto
            {
                Code = code, Title = "فهرست مدارک مرجع آزمون", StandardSource = "API 612",
                Lines = lines.Select(l => new EnReferenceLineDto { DocumentType = l.Type, Discipline = "مکانیک", Title = "مدرک " + l.Type, Required = l.Required, TenderStage = l.Tender }).ToList(),
            };
        }

        public static T Ok<T>(ResponseDto<T> r)
        {
            Assert.IsTrue(r.Success, r.Message);
            return r.Data;
        }

        public static string Refused<T>(ResponseDto<T> r, string fragment = null)
        {
            Assert.IsFalse(r.Success, "expected a refusal");
            if (fragment != null) StringAssert.Contains(r.Message, fragment);
            return r.Message;
        }

        public static Task<ResponseDto<EnReferenceListDto>> RefAction(AppHost host, ClaimsPrincipal who, int id, string action, string note = null) =>
            host.As(who, p => p.GetRequiredService<IEnReferenceService>().ListActionAsync(new EnReferenceActionDto { Id = id, Action = action, Note = note }, Ct));

        /// <summary>An approved reference list (drafted by Eng1, approved by Mgr1).</summary>
        public static async Task<EnReferenceListDto> ApprovedList(AppHost host, string code = "VDDR-TEST", params (string Type, bool Required, bool Tender)[] lines)
        {
            EnReferenceListDto d = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref(code, lines), Ct)));
            Ok(await RefAction(host, Eng1, d.Id, "submit"));
            return Ok(await RefAction(host, Mgr1, d.Id, "approve"));
        }

        public static int Product(AppHost host, string code = "99.11.00.000.2", bool active = true)
        {
            int id = 0;
            host.Write(u => { PbProduct p = AppHost.Add(u, new PbProduct { Code = code, Title = "توربین نمونه", IsActive = active }); u.SaveChanges(); id = p.Id; }, Seeder);
            return id;
        }

        public static Guid File(AppHost host, int uploader, string extension = ".pdf")
        {
            Guid id = Guid.Empty;
            host.Write(u =>
            {
                Document d = AppHost.Add(u, new Document("UploadedDocuments/" + Guid.NewGuid().ToString("N") + extension, extension).SetUploader(uploader).SetName("doc").SetCode("T").SetDescription("d"));
                u.SaveChanges(); id = d.Id;
            }, Seeder);
            return id;
        }

        public static Task<ResponseDto<EnDossierDto>> Dossier(AppHost host, ClaimsPrincipal who, int productId, string action, string note = null, int? lineId = null) =>
            host.As(who, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = productId, Action = action, Note = note, LineId = lineId }, Ct));

        public static Task<ResponseDto<EnDossierDto>> Supply(AppHost host, ClaimsPrincipal who, int lineId, Guid? file, bool na = false, string reason = null) =>
            host.As(who, p => p.GetRequiredService<IEnPlatformService>().SupplyLineAsync(new EnSupplyLineDto { LineId = lineId, DocumentId = file, NotApplicable = na, NotApplicableReason = reason, Revision = "A" }, Ct));

        /// <summary>A platform that is complete and confirmed by a person who did not touch it.</summary>
        public static async Task<int> CompletePlatform(AppHost host, int productId, int fileUploader = 2111)
        {
            EnDossierDto d = Ok(await Dossier(host, Eng1, productId, "assess"));
            foreach (EnDossierLineDto l in d.Lines.Where(x => x.Required && x.State == 0).ToList())
                d = Ok(await Supply(host, Eng1, l.Id, File(host, fileUploader)));
            Ok(await Dossier(host, Eng1, productId, "complete"));
            return Ok(await Dossier(host, Mgr1, productId, "confirm")).Id;
        }
    }
}
