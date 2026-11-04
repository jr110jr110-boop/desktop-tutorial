using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.ContractsSystem;
using Services.NasbNiro.Procurement;
using Ct = Entities.Model.NasbNiro.ContractsSystem;

namespace Services.NasbNiro.Attachments
{
    /// <summary>
    /// PB6: the attachment rules of the records of this package (progress statements, supplier guarantees, petty-cash flow, addenda, contracts system).
    /// The holder of a petty cash and the maker of a draft read and add the files of their own record; the checkers see them by role.
    /// </summary>
    public static class Pb6AttachmentOwners
    {
        public const string PettyWrite = "FINANCIAL,FINANCEMANAGER,PMO";

        public static string ReadRolesOf(AttachmentOwnerType type)
        {
            switch (type)
            {
                case AttachmentOwnerType.ProgressStatement: return PsRoles.Readers;
                case AttachmentOwnerType.SupplierGuarantee:
                case AttachmentOwnerType.ContractAddendum: return AttachmentRoles.Procurement;
                case AttachmentOwnerType.PettyCashSummaryLine:
                case AttachmentOwnerType.PettyCashRequest:
                case AttachmentOwnerType.PettyCashSummary: return PettyFlowRoles.Readers;
                case AttachmentOwnerType.CtDealRequest:
                case AttachmentOwnerType.CtTenderParticipant:
                case AttachmentOwnerType.CtContractDraft: return CtRoles.Readers;
                default: return null;
            }
        }

        public static async Task<OwnerFacts> FactsAsync(IUnitOfWork uow, AttachmentOwnerType type, int id, OwnerFacts f, CancellationToken ct)
        {
            switch (type)
            {
                case AttachmentOwnerType.ProgressStatement:
                {
                    var s = await uow.Set<Ct.PsStatement>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status, x.CreatorUserId }).FirstOrDefaultAsync(ct);
                    if (s == null) return f;
                    f.Exists = true; f.Number = s.Number;
                    f.Locked = s.Status != Ct.PsStatus.Draft && s.Status != Ct.PsStatus.Returned; f.Closed = s.Status == Ct.PsStatus.Rejected;
                    f.ReadRoles = PsRoles.Readers; f.WriteRoles = PsRoles.Preparers + ",PMO"; f.LockedWriteRoles = PsRoles.Preparers + "," + PsRoles.PaymentRecorders + ",PMO";
                    f.CreatorUserId = s.CreatorUserId;
                    return f;
                }
                case AttachmentOwnerType.SupplierGuarantee:
                {
                    var g = await uow.Set<SupplierGuarantee>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status }).FirstOrDefaultAsync(ct);
                    if (g == null) return f;
                    f.Exists = true; f.Number = g.Number;
                    f.Locked = g.Status != SupplierGuaranteeStatus.Active; f.Closed = g.Status == SupplierGuaranteeStatus.Released || g.Status == SupplierGuaranteeStatus.Claimed;
                    f.ReadRoles = AttachmentRoles.Procurement; f.WriteRoles = SupplierContractRoles.Registrars + ",PMO"; f.LockedWriteRoles = "PMO,FINANCEMANAGER";
                    return f;
                }
                case AttachmentOwnerType.ContractAddendum:
                {
                    var a = await uow.Set<PurchaseOrderAddendum>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status, x.PurchaseOrderId }).FirstOrDefaultAsync(ct);
                    if (a == null) return f;
                    f.Exists = true; f.Number = a.Number;
                    f.Locked = a.Status != AddendumStatus.Draft; f.Closed = a.Status == AddendumStatus.Rejected;
                    f.ReadRoles = AttachmentRoles.Procurement; f.WriteRoles = "COMMERCIAL,PMO"; f.LockedWriteRoles = "PMO";
                    return f;
                }
                case AttachmentOwnerType.PettyCashSummaryLine:
                {
                    var l = await uow.Set<FnPettyCashSummaryLine>().AsNoTracking().Where(x => x.Id == id)
                        .Select(x => new { x.RowNumber, x.Summary.Number, x.Summary.Status, x.Summary.CreatorUserId, x.Summary.Deleted }).FirstOrDefaultAsync(ct);
                    if (l == null || l.Deleted) return f;
                    f.Exists = true; f.Number = (l.Number ?? "صورت‌خلاصه") + " / " + l.RowNumber;
                    f.Locked = l.Status != FnSummaryStatus.Draft && l.Status != FnSummaryStatus.Returned; f.Closed = l.Status == FnSummaryStatus.Rejected;
                    f.ReadRoles = PettyFlowRoles.Readers; f.OwnReadRoles = "*"; f.WriteRoles = null; f.LockedWriteRoles = PettyWrite;
                    f.CreatorUserId = l.CreatorUserId; f.CreatorEditsWhileOpen = true;
                    return f;
                }
                case AttachmentOwnerType.PettyCashSummary:
                {
                    var s = await uow.Set<FnPettyCashSummary>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status, x.CreatorUserId }).FirstOrDefaultAsync(ct);
                    if (s == null) return f;
                    f.Exists = true; f.Number = s.Number;
                    f.Locked = s.Status != FnSummaryStatus.Draft && s.Status != FnSummaryStatus.Returned; f.Closed = s.Status == FnSummaryStatus.Rejected;
                    f.ReadRoles = PettyFlowRoles.Readers; f.OwnReadRoles = "*"; f.WriteRoles = null; f.LockedWriteRoles = PettyWrite;
                    f.CreatorUserId = s.CreatorUserId; f.CreatorEditsWhileOpen = true;
                    return f;
                }
                case AttachmentOwnerType.PettyCashRequest:
                {
                    var r = await uow.Set<FnPettyCashRequest>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status, x.CreatorUserId }).FirstOrDefaultAsync(ct);
                    if (r == null) return f;
                    f.Exists = true; f.Number = r.Number;
                    f.Locked = r.Status != FnPettyRequestStatus.Draft; f.Closed = r.Status == FnPettyRequestStatus.Rejected;
                    f.ReadRoles = PettyFlowRoles.Readers; f.OwnReadRoles = "*"; f.WriteRoles = null; f.LockedWriteRoles = PettyWrite;
                    f.CreatorUserId = r.CreatorUserId; f.CreatorEditsWhileOpen = true;
                    return f;
                }
                case AttachmentOwnerType.CtDealRequest:
                {
                    var d = await uow.Set<Ct.CtDealRequest>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status, x.CreatorUserId }).FirstOrDefaultAsync(ct);
                    if (d == null) return f;
                    f.Exists = true; f.Number = d.Number;
                    f.Locked = d.Status != Ct.CtDealStatus.Draft && d.Status != Ct.CtDealStatus.Returned; f.Closed = d.Status == Ct.CtDealStatus.Rejected;
                    f.ReadRoles = CtRoles.Readers; f.OwnReadRoles = "*"; f.WriteRoles = null; f.LockedWriteRoles = "PMO,COMMERCIAL";
                    f.CreatorUserId = d.CreatorUserId; f.CreatorEditsWhileOpen = true;
                    return f;
                }
                case AttachmentOwnerType.CtTenderParticipant:
                {
                    var p = await uow.Set<Ct.CtTenderParticipant>().AsNoTracking().Where(x => x.Id == id)
                        .Select(x => new { x.CompanyName, x.TenderRun.Number, x.TenderRun.Status, x.TenderRun.Deleted }).FirstOrDefaultAsync(ct);
                    if (p == null || p.Deleted) return f;
                    f.Exists = true; f.Number = p.Number + " / " + p.CompanyName;
                    f.Locked = p.Status == Ct.CtTenderStatus.WinnerConfirmed; f.Closed = p.Status == Ct.CtTenderStatus.Cancelled;
                    f.ReadRoles = CtRoles.Readers; f.WriteRoles = CtRoles.Commercial; f.LockedWriteRoles = "PMO";
                    return f;
                }
                case AttachmentOwnerType.CtContractDraft:
                {
                    var d = await uow.Set<Ct.CtContractDraft>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status }).FirstOrDefaultAsync(ct);
                    if (d == null) return f;
                    f.Exists = true; f.Number = d.Number;
                    f.Locked = d.Status == Ct.CtDraftStatus.Signed || d.Status == Ct.CtDraftStatus.Notified; f.Closed = d.Status == Ct.CtDraftStatus.Cancelled;
                    f.ReadRoles = CtRoles.Readers; f.WriteRoles = CtRoles.Commercial; f.LockedWriteRoles = "PMO,COMMERCIAL";
                    return f;
                }
                default:
                    return f;
            }
        }
    }
}
