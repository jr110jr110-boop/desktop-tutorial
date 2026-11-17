using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.Sales;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
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
                AppHost.Add(u, new Entities.Model.NasbNiro.GoLive.GlActivation { StartAt = DateTimeOffset.Now.AddDays(-daysAgo), Note = "test" });
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

        /// <summary>The standard code the tests use for a project unless a test says otherwise (it is the Code of <see cref="ApprovedList"/>).</summary>
        public const string Std = "VDDR-TEST";

        /// <summary>A dossier action. Before an "assess" of a product that has no dossier of the standard, the engineers record the standard of the original data sheet (a human fact), as the real flow needs.</summary>
        public static async Task<ResponseDto<EnDossierDto>> Dossier(AppHost host, ClaimsPrincipal who, int productId, string action, string note = null, int? lineId = null, string code = Std)
        {
            if (action == "assess" && !host.Read(u => u.Set<EnPlatformDossier>().Any(d => d.ProductId == productId && d.ReferenceCode == code && !d.Deleted)))
                Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = productId, Action = "native-standard", ReferenceCode = code, StandardBasis = "دیتاشیت پروژهٔ نمونه، بند ۴" }, Ct)));
            return await host.As(who, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = productId, Action = action, Note = note, LineId = lineId, ReferenceCode = code }, Ct));
        }

        /// <summary>A product becomes a platform only with a dossier: the engineers record the standard of its original data sheet.</summary>
        public static async Task NativeDossier(AppHost host, int productId, string code = Std) =>
            Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = productId, Action = "native-standard", ReferenceCode = code, StandardBasis = "دیتاشیت پروژهٔ نمونه، بند ۴" }, Ct)));

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

        // ---------------------------------------------------------------- stage 3 helpers

        public static int Inquiry(AppHost host, SlInquiryStatus status = SlInquiryStatus.Bidding, SlProductType type = SlProductType.SteamTurbine, DateTimeOffset? created = null, string number = null)
        {
            int id = 0;
            host.Write(u =>
            {
                SlInquiry i = AppHost.Add(u, new SlInquiry { Number = number ?? "INQ-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "استعلام توربین", CustomerId = 1, ProductType = type, Status = status, ReceivedDate = DateTime.Today, CreateDate = created ?? DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
                u.SaveChanges(); id = i.Id;
            }, Seeder);
            return id;
        }

        public static int Contract(AppHost host, DateTimeOffset? created = null, bool workOrder = true, int equipmentId = 7)
        {
            int id = 0;
            host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "ساخت توربین", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-5), Price = 1_000_000_000m, CreateDate = created ?? DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
                u.SaveChanges();
                if (workOrder) AppHost.Add(u, new WorkOrder { Code = "WO-" + c.Code, Title = "توربین", Count = 1, ContractId = c.Id, EquipmentId = equipmentId, CreateDate = DateTimeOffset.Now });
                u.SaveChanges(); id = c.Id;
            }, Seeder);
            return id;
        }

        public static Task<ResponseDto<EnCaseDto>> CaseAct(AppHost host, ClaimsPrincipal who, int? id, string action, Action<EnCaseActionDto> set = null)
        {
            EnCaseActionDto dto = new EnCaseActionDto { Id = id, Action = action };
            set?.Invoke(dto);
            return host.As(who, p => p.GetRequiredService<IEnCaseService>().CaseActionAsync(dto, Ct));
        }

        public static Task<ResponseDto<EnCaseDto>> GetCase(AppHost host, ClaimsPrincipal who, int id) => host.As(who, p => p.GetRequiredService<IEnCaseService>().GetCaseAsync(id, Ct));

        /// <summary>Records the client standard of a case; the data sheet file is required (uploaded by the caller), so one is created for the caller unless <paramref name="file"/> is given or <paramref name="withoutFile"/> is true.</summary>
        public static Task<ResponseDto<EnCaseDto>> SetStandard(AppHost host, ClaimsPrincipal who, int caseId, string code, string evidence = "دیتاشیت کارفرما، بند ۲٫۱", string note = null, Guid? file = null, bool withoutFile = false)
        {
            Guid? attachment = withoutFile ? (Guid?)null : file ?? File(host, int.Parse(who.FindFirst(ClaimTypes.NameIdentifier).Value));
            return CaseAct(host, who, caseId, "set-standard", d => { d.ReferenceCode = code; d.StandardEvidence = evidence; d.Note = note; d.StandardAttachmentId = attachment; });
        }

        /// <summary>An inquiry case that the engineer proposed, the manager confirmed and the CEO approved (status TenderPreparation for a bidding inquiry).</summary>
        public static async Task<EnCaseDto> ApprovedCase(AppHost host, int inquiry, bool platform, int? productId = null, string code = Std)
        {
            if (platform && productId.HasValue && !host.Read(u => u.Set<EnPlatformDossier>().Any(d => d.ProductId == productId.Value && !d.Deleted)))   // a product is a platform only with a dossier: the engineers record the standard of its original data sheet
                Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = productId.Value, Action = "native-standard", ReferenceCode = code, StandardBasis = "دیتاشیت پروژهٔ نمونه، بند ۴" }, Ct)));
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            c = Ok(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = platform ? 1 : 2; d.ProductId = productId; d.Basis = "مبنای آزمون برای تعیین نوع پرونده"; }));
            Ok(await SetStandard(host, Eng1, c.Id, code));
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm"));
            await ConfirmGapIfAny(host, c.Id);
            return Ok(await CaseAct(host, Ceo, c.Id, "approve"));
        }

        /// <summary>The standard-gap report of a case under another standard: the agent builds it, the manager confirms it.</summary>
        public static async Task ConfirmGapIfAny(AppHost host, int caseId)
        {
            ClaimsPrincipal seat = await SeatAsync(host);
            foreach (AgentWorkItem item in (await EngineeringStandardsTests.Discover(host, seat, "ENG.STANDARD.GAP")).Where(i => i.TargetId == caseId))
                await EngineeringStandardsTests.Run(host, seat, "ENG.STANDARD.GAP", item);
            int? gap = host.Read(u => u.Set<EnStandardGap>().Where(g => g.CaseId == caseId && g.Status == EnGapStatus.Draft).Select(g => (int?)g.Id).FirstOrDefault());
            if (!gap.HasValue) return;
            EnGapDto dto = Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnStandardGapService>().GetGapAsync(gap.Value, Ct)));
            Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnStandardGapService>().ConfirmAsync(new EnGapConfirmDto { Id = gap.Value, EngineeringImpactNote = "اثر بر مواد و طراحی بررسی شد؛ مدارک افزوده مطابق فهرست استاندارد سفارش تهیه می‌شود.", RowVersion = dto.RowVersion }, Ct)));
        }
    }
}
