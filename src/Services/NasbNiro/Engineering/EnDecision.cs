using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Office;
using Entities.Model.NasbNiro.Office;

namespace Services.NasbNiro.Engineering
{
    /// <summary>
    /// ENG-E1 fix round A (C2, H6): the one predicate that says whether the classification decision of a case is in force right now, and the one routine that sends a case back for a new decision. The handover submit, accept and release
    /// (and the tender release of an approved platform case) re-check it inside the same service call, so a re-decision round (a changed standard, a reclassification, a stale standard-gap report) can never be skipped.
    /// </summary>
    internal static class EnDecision
    {
        /// <summary>The single refusal message of the decision predicate.</summary>
        public const string Refused = "تصویب نوع پرونده معتبر نیست: تایید مدیر مهندسی و مدیرعامل (برای قطعات یدکی و خدمات پس از فروش فقط مدیر مهندسی) باید ثبت شده و با محتوای فعلی پرونده (نوع، استاندارد و گزارش تفاوت) یکی باشد؛ پرونده باید دوباره تصمیم‌گیری شود و ایجنت مهندسی پس از تایید تازه تحویل جدید می‌سازد.";

        /// <summary>The Persian reason written on a handover that a new decision round voided.</summary>
        public const string VoidReason = "تحویل باطل شد: پرونده برای تصمیم تازه (تغییر استاندارد یا نوع پرونده) به مدیر مهندسی برگشت؛ پس از تصویب تازه ایجنت مهندسی تحویل جدید می‌سازد.";

        /// <summary>Null when the decision is in force: both signatures are present and the stored hashes (classification with the standard, and the standard-gap report) still match the current content.</summary>
        public static async Task<string> RefusalAsync(IUnitOfWork uow, EnCase c, CancellationToken ct)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.ReferenceCode) || c.ClassificationHash == null || c.ClassificationHash != EnCaseRules.ClassHash(c)) return Refused;
            if (!c.EngConfirmedAt.HasValue || !c.EngConfirmedByUserId.HasValue) return Refused;
            if (c.Kind == EnCaseKind.Turbine)
            {
                if (!c.CeoApprovedAt.HasValue || !c.CeoApprovedByUserId.HasValue || c.Classification == EnClassification.None || c.Classification != c.ProposedClassification) return Refused;
                if (c.ApprovedGapHash != null)
                {
                    EnStandardGap live = await EnGaps.LiveAsync(uow, c.Id, ct);
                    if (live == null || live.Status != EnGapStatus.Confirmed || live.ContentHash != c.ApprovedGapHash) return Refused;
                    // a new approved revision of the base or the target list after the approval makes the report out of date
                    if (await RevisionAsync(uow, live.BaseCode, ct) != live.BaseRevision || await RevisionAsync(uow, live.TargetCode, ct) != live.TargetRevision) return Refused;
                }
            }
            return null;
        }

        private static async Task<int> RevisionAsync(IUnitOfWork uow, string code, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(code) ? 0 : await uow.Set<EnReferenceList>().AsNoTracking().Where(l => !l.Deleted && l.Status == EnRefStatus.Approved && l.Code == code).Select(l => l.Revision).FirstOrDefaultAsync(ct);

        /// <summary>Voids every open (Draft or Submitted) handover of the case in the caller's save, with its unused waiver and open debts; the caller saves.</summary>
        public static async Task VoidOpenHandoversAsync(IUnitOfWork uow, INotificationService notify, EnCase c, int me, DateTimeOffset now, CancellationToken ct)
        {
            List<EnHandover> open = await uow.Set<EnHandover>().Where(h => !h.Deleted && h.CaseId == c.Id && (h.Status == EnHandoverStatus.Draft || h.Status == EnHandoverStatus.Submitted)).ToListAsync(ct);
            foreach (EnHandover h in open)
            {
                if (h.WaiverId.HasValue)
                    foreach (EnDocumentDebt d in await uow.Set<EnDocumentDebt>().Where(d => d.WaiverId == h.WaiverId.Value && !d.Deleted && d.Status == EnDebtStatus.Open).ToListAsync(ct)) { d.Status = EnDebtStatus.Void; d.ModifyDate = now; d.ModifierUserId = me; }
                foreach (EnWaiver w in await uow.Set<EnWaiver>().Where(w => w.HandoverId == h.Id && !w.Deleted && (w.Status == EnWaiverStatus.Requested || (w.Status == EnWaiverStatus.Approved && w.UsedAt == null))).ToListAsync(ct))
                {
                    w.Status = EnWaiverStatus.Void; w.ModifyDate = now; w.ModifierUserId = me;
                    await OfficeGates.SyncAsync(notify, "EN-WAIVER-" + w.Id, false, "Engineering", nameof(EnWaiver), w.Id, "تصمیم دربارهٔ معافیت اضطراری " + w.Number, "/engineering/waivers/" + w.Id, EnRoles.Ceo, ct, null, OfInboxKind.Approval);
                }
                bool submitted = h.Status == EnHandoverStatus.Submitted;
                h.Status = EnHandoverStatus.Void; h.VoidReason = EnText.Cut(VoidReason, 1000); h.ModifyDate = now; h.ModifierUserId = me;
                EnEvents.Add(uow, c, "handover-void", me, h.Number + ": " + VoidReason);
                if (submitted) await OfficeGates.SyncAsync(notify, "EN-HO-ACCEPT-" + h.Id, false, "Engineering", nameof(EnHandover), h.Id, "پذیرش تحویل مدارک مهندسی " + h.Number, "/engineering/handovers/" + h.Id, EnRoles.PmoSide, ct, null, OfInboxKind.Approval);
            }
        }
    }

    /// <summary>The origin of a product: who may make it a platform (H1).</summary>
    internal static class EnOrigin
    {
        public const string NoDossier = "برای این محصول پرونده مدارک پلتفرم ثبت نشده است؛ محصول پلتفرم نیست. استاندارد دیتاشیت اصلی آن را ثبت کنید، یا اگر طرح جدید است پس از ابلاغ تحویل مدارک پیشنهاد ثبت پلتفرم را مدیر مهندسی بدهد و مدیرعامل تصویب کند.";
        public const string UnregisteredNew = "این محصول تنها از یک پروندهٔ طراحی جدید آمده و هنوز با تصویب مدیرعامل (ثبت پلتفرم) پلتفرم نشده است؛ ثبت استاندارد اصلی یا نوع پلتفرم برای آن ممکن نیست.";

        /// <summary>The products whose only origin is a controlled New-design case (by the case product, or by the equipment of the work orders of its contract) with no Approved registration and no platform case.</summary>
        public static IQueryable<PbProduct> UnregisteredNewProducts(IUnitOfWork uow) =>
            uow.Set<PbProduct>().AsNoTracking().Where(p => !p.Deleted
                && uow.Set<EnCase>().Any(c => !c.Deleted && c.Kind == EnCaseKind.Turbine && c.Classification == EnClassification.New
                    && (c.ProductId == p.Id || (c.ContractId != null && p.EquipmentId != null && uow.Set<WorkOrder>().Any(w => w.ContractId == c.ContractId.Value && !w.Deleted && w.EquipmentId == p.EquipmentId.Value))))
                && !uow.Set<EnCase>().Any(c => !c.Deleted && c.Classification == EnClassification.Platform && c.ProductId == p.Id)
                && !uow.Set<EnPlatformRegistration>().Any(r => !r.Deleted && r.ProductId == p.Id && r.Status == EnRegStatus.Approved));

        public static Task<bool> IsUnregisteredNewDesignAsync(IUnitOfWork uow, int productId, CancellationToken ct) =>
            UnregisteredNewProducts(uow).AnyAsync(p => p.Id == productId, ct);

        public static Task<bool> HasDossierAsync(IUnitOfWork uow, int productId, CancellationToken ct) =>
            uow.Set<EnPlatformDossier>().AsNoTracking().AnyAsync(d => d.ProductId == productId && !d.Deleted, ct);

        /// <summary>The product belongs to the case: it is the case product, or the product of the equipment of a work order of the case contract.</summary>
        public static async Task<bool> BelongsToCaseAsync(IUnitOfWork uow, EnCase c, int productId, CancellationToken ct)
        {
            if (c.ProductId == productId) return true;
            if (!c.ContractId.HasValue) return false;
            int contractId = c.ContractId.Value;
            return await uow.Set<PbProduct>().AsNoTracking().AnyAsync(p => p.Id == productId && !p.Deleted && p.EquipmentId != null && uow.Set<WorkOrder>().Any(w => w.ContractId == contractId && !w.Deleted && w.EquipmentId == p.EquipmentId.Value), ct);
        }
    }
}
