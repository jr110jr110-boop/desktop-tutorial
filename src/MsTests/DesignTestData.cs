using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Entities.Identity;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.ProductBankDtos;

namespace MsTests
{
    /// <summary>
    /// Package A1 fix round 1 (M4): the design control stays ON in the test hosts of the other packages. A test that releases a BOM first gives it REAL design data through this helper: an approved plan, the seven kinds
    /// of inputs, the completeness declaration, and a Verification review (held, judged by an independent QA, closed by an independent manager) that covers the BOM. The people have their own high ids so they are never an author
    /// of the BOM in the tests that call this.
    /// </summary>
    internal static class DesignTestData
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        public static readonly ClaimsPrincipal Maker = AppHost.Principal(9101, "dtd.maker", "ENGINEER");
        public static readonly ClaimsPrincipal Mgr = AppHost.Principal(9102, "dtd.manager", "ENGINEERING");
        public static readonly ClaimsPrincipal Qa = AppHost.Principal(9103, "dtd.qa", "QA");

        private static int _wrapper;

        private static void EnsureUsers(AppHost h)
        {
            int[] ids = { 9101, 9102, 9103 };
            List<int> have = h.Read(u => u.Set<User>().AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToList());
            var missing = ids.Where(i => !have.Contains(i)).ToList();
            if (missing.Count == 0) return;
            h.Write(u =>
            {
                foreach (int id in missing) AppHost.Add(u, new User { Id = id, UserName = "dtd.user" + id, Email = "dtd.user" + id + "@example.com", IsActive = true });
                u.SaveChanges();
            }, Seeder);
        }

        public static SaveDesignInputDto Input(int productId, int revision, DesignInputKind kind, string text) => new SaveDesignInputDto
        {
            ProductId = productId, ProductRevision = revision, Kind = kind, Statement = text, Source = "قرارداد بند ۳", AcceptanceCriterion = "آزمون مطابق بند ۳ قبول",
            StandardEdition = kind == DesignInputKind.Standard ? "ASME 2019" : null, PreviousDesignRef = kind == DesignInputKind.PreviousDesign ? "DP-1403-001" : null,
            RefText = kind == DesignInputKind.CustomerSpec ? "مشخصات فنی مشتری" : null, RefRevisionText = kind == DesignInputKind.CustomerSpec ? "B" : null,
        };

        /// <summary>The approved plan of the product revision (made and approved now when there is none), its inputs of every kind, and the declaration that they are complete. Returns the plan id.</summary>
        public static async Task<int> PlanAsync(AppHost h, int productId, int revision, bool validation = true)
        {
            EnsureUsers(h);
            int plan = h.Read(u => u.Set<PbDesignPlan>().AsNoTracking().Where(p => !p.Deleted && p.ProductId == productId && p.ProductRevision == revision && p.Status == DesignPlanStatus.Approved).Select(p => p.Id).FirstOrDefault());
            if (plan == 0)
            {
                var dto = new SaveDesignPlanDto { ProductId = productId, ProductRevision = revision, Title = "برنامه طراحی آزمون", ValidationRequired = validation, ValidationWaiverReason = validation ? null : "محصول بدون نیاز به صحه‌گذاری جداگانه (آزمون خودکار)" };
                foreach (var d in DesignRules.DefaultStages())
                    dto.Stages.Add(new SaveDesignStageDto { Kind = d.Kind, ResponsibleRole = d.Role, ResponsibleUserId = 9101, PlannedStart = DateTime.Today, PlannedEnd = DateTime.Today.AddDays(30) });
                var saved = await h.As(Maker, p => p.GetRequiredService<IDesignPlanService>().SaveAsync(dto, Ct));
                Assert.IsTrue(saved.Success, "design plan: " + saved.Message);
                Assert.IsTrue((await h.As(Maker, p => p.GetRequiredService<IDesignPlanService>().ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "submit" }, Ct))).Success);
                var ap = await h.As(validation ? Mgr : Qa, p => p.GetRequiredService<IDesignPlanService>().ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "approve" }, Ct));
                Assert.IsTrue(ap.Success, "design plan approval: " + ap.Message);
                plan = saved.Data.Id;
            }
            bool any = h.Read(u => u.Set<PbDesignInput>().AsNoTracking().Any(i => !i.Deleted && i.ProductId == productId && i.ProductRevision == revision && i.Status == DesignInputStatus.Open));
            if (!any)
                foreach (DesignInputKind k in DesignRules.AllKinds)
                {
                    var r = await h.As(Maker, p => p.GetRequiredService<IDesignInputService>().SaveAsync(Input(productId, revision, k, "الزام " + k), Ct));
                    Assert.IsTrue(r.Success, "design input " + k + ": " + r.Message);
                }
            var declared = await h.As(Qa, p => p.GetRequiredService<IDesignPlanService>().ActionAsync(new DesignPlanActionDto { Id = plan, Action = "inputs-complete", Note = "منابع قرارداد و استانداردها بررسی شد" }, Ct));
            Assert.IsTrue(declared.Success, "inputs declared complete: " + declared.Message);
            return plan;
        }

        /// <summary>A closed, accepted Verification review of the outputs under the plan; every input of the plan revision is judged passed against every output.</summary>
        public static async Task<int> VerifyAsync(AppHost h, int plan, params SaveDesignOutputDto[] outputs)
        {
            var dto = new SaveDesignReviewDto
            {
                PlanId = plan, Type = DesignReviewType.Verification, Outputs = outputs.ToList(),
                Participants = new List<SaveDesignParticipantDto> { new SaveDesignParticipantDto { UserId = 9103, RoleText = "QA" }, new SaveDesignParticipantDto { UserId = 9102, RoleText = "مدیر مهندسی" } },
            };
            var saved = await h.As(Maker, p => p.GetRequiredService<IDesignReviewService>().SaveAsync(dto, Ct));
            Assert.IsTrue(saved.Success, "design verification: " + saved.Message);
            var held = await h.As(Maker, p => p.GetRequiredService<IDesignReviewService>().ActionAsync(new DesignReviewActionDto { Id = saved.Data.Id, Action = "hold", Minutes = "جلسه برگزار شد و موارد بررسی شد" }, Ct));
            Assert.IsTrue(held.Success, "design verification held: " + held.Message);
            var rows = new List<SaveDesignTraceRowDto>();
            foreach (DesignReviewInputRowDto i in held.Data.Inputs)
                foreach (DesignOutputDto o in held.Data.Outputs)
                    rows.Add(new SaveDesignTraceRowDto { InputId = i.InputId, OutputId = o.Id, Result = DesignTraceResult.Passed, Evidence = "اندازه‌گیری و آزمون مطابق معیار", EvidenceRef = "QR-1" });
            var judged = await h.As(Qa, p => p.GetRequiredService<IDesignReviewService>().SaveTraceAsync(new SaveDesignTraceDto { ReviewId = held.Data.Id, Rows = rows }, Ct));
            Assert.IsTrue(judged.Success, "design verification judged: " + judged.Message);
            var closed = await h.As(Mgr, p => p.GetRequiredService<IDesignReviewService>().ActionAsync(new DesignReviewActionDto { Id = held.Data.Id, Action = "close", Outcome = DesignReviewOutcome.Accepted }, Ct));
            Assert.IsTrue(closed.Success, "design verification closed: " + closed.Message);
            return closed.Data.Id;
        }

        /// <summary>The BOM revision gets its design verification. A BOM of a product is verified under the plan of that product; a BOM without a product (a sub-assembly, or a test BOM) under a wrapper product whose draft BOM names its assembly.</summary>
        public static async Task VerifyBomAsync(AppHost h, int bomId)
        {
            EnsureUsers(h);
            PbBomHeader head = h.Read(u => u.Set<PbBomHeader>().AsNoTracking().First(x => x.Id == bomId));
            int productId, revision;
            if (head.ProductId.HasValue) { productId = head.ProductId.Value; revision = head.Revision; }
            else
            {
                int n = Interlocked.Increment(ref _wrapper);
                var product = await h.As(Maker, p => p.GetRequiredService<IPbProductService>().SaveProductAsync(new SavePbProductDto { Code = "98.00.00." + n.ToString("000") + ".2", Title = "Design wrapper " + n }, Ct));
                Assert.IsTrue(product.Success, product.Message);
                productId = product.Data.Id; revision = 0;
                var wrap = await h.As(Maker, p => p.GetRequiredService<IPbBomService>().SaveAsync(new SavePbBomDto
                {
                    ProductId = productId, AssemblyPartNumber = "98.00.00." + n.ToString("000") + ".2", Title = "wrapper", Lines = new List<SavePbBomLineDto>
                    { new SavePbBomLineDto { ItemNumber = 1, PartNumber = head.AssemblyPartNumber, PartName = "sub", Qty = 1, Unit = "عدد", Material = "WCB", MakeOrBuy = PbMakeOrBuy.Make, NetWeight = 1m, GrossWeight = 1m } },
                }, Ct));
                Assert.IsTrue(wrap.Success, wrap.Message);
            }
            int plan = await PlanAsync(h, productId, revision);
            await VerifyAsync(h, plan, new SaveDesignOutputDto { Kind = DesignOutputKind.Bom, BomHeaderId = bomId });
        }
    }
}
