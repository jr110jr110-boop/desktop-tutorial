using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.GoLive;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.GoLive;

namespace Services.NasbNiro.AuditSeat
{
    // Package G (go-live cut-over): the legacy exemptions that were used are observations of the AUDIT seat (never silent), and a purchase order line whose cited drawing revision was superseded is a finding until it is dealt with.
    public static partial class AusCatalog
    {
        private static IEnumerable<AusRuleDef> GoLiveRules()
        {
            yield return Def("AUD-CO-001", "DESIGN", AusSeverity.Minor, "دستور کار قدیمی بدون پایه‌ریزی از بانک محصول ترخیص شده است (ممیزی پیکربندی انجام نشده)", Iso9, "8.5.2 / ISO 10007 6.4", "برنامه بسته G (گذار به کنترل پیکربندی؛ پذیرش پیکربندی پروژه موجود)",
                "GlLegacyUse of kind PcaNotApplicable whose work order still has no current product-bank baseline", false, async c =>
                {
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<GlLegacyUse>().AsNoTracking()).Where(u => !u.Deleted && u.Kind == GlLegacyKind.PcaNotApplicable && u.WorkOrderId != null
                            && !c.Uow.Set<PbProjectBaseline>().Any(b => !b.Deleted && b.IsCurrent && b.WorkOrderId == u.WorkOrderId)).OrderBy(u => u.Id)
                        .Select(u => new { u.Id, u.Number, u.WorkOrderId, u.UsedAt, u.Note }));
                    return rows.Where(u => !c.FocusId.HasValue || u.Id == c.FocusId.Value).Select(u =>
                        Hit("GlLegacyUse", u.Id, u.Number ?? ("WO" + u.WorkOrderId), $"ترخیص دستور کار {u.Number ?? u.WorkOrderId.ToString()} در حالت قدیمی انجام شده و هنوز پایه‌ریزی از بانک محصول ندارد؛ «پذیرش پیکربندی پروژه موجود» را ثبت کنید.", link: "/go-live/config-adoptions")
                            .On(u.UsedAt).Fact("workOrder", u.WorkOrderId).Fact("note", u.Note)).ToList();
                });

            yield return Def("AUD-CO-002", "QUALITY", AusSeverity.Minor, "پذیرش QC در حالت قدیمی انجام شده است (بدون فهرست مشخصه، مقدار اسمی و تلرانس ثبت نشده)", Iso9, "8.6 / 8.5.1", "برنامه بسته G (گذار به کنترل کیفیت؛ پذیرش مشخصه‌های نقشه موجود)",
                "GlLegacyUse of kind QcAcceptWithoutList (a past acceptance event)", false, async c =>
                {
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<GlLegacyUse>().AsNoTracking()).Where(u => !u.Deleted && u.Kind == GlLegacyKind.QcAcceptWithoutList).OrderBy(u => u.Id)
                        .Select(u => new { u.Id, u.Number, u.ItemId, u.RevisionId, u.UsedAt, u.Note }));
                    return rows.Where(u => !c.FocusId.HasValue || u.Id == c.FocusId.Value).Select(u =>
                        Hit("GlLegacyUse", u.Id, u.Number ?? ("QC" + u.Id), $"پذیرش QC {u.Number} در حالت قدیمی ثبت شده است: {u.Note}", link: "/go-live/char-adoptions")
                            .On(u.UsedAt).Fact("item", u.ItemId).Fact("revision", u.RevisionId)).ToList();
                });

            yield return Def("AUD-CO-003", "PRODUCTION", AusSeverity.Minor, "نمونه اول (FAI) برای سفارش تولید قطعه قدیمی به‌صورت خودکار باز نشده است", Iso9, "8.5.1 / 8.6", "برنامه بسته G (گذار؛ FAI قطعات قدیمی)",
                "GlLegacyUse of kind FaiNotOpened (a past release event)", false, async c =>
                {
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<GlLegacyUse>().AsNoTracking()).Where(u => !u.Deleted && u.Kind == GlLegacyKind.FaiNotOpened).OrderBy(u => u.Id)
                        .Select(u => new { u.Id, u.Number, u.ItemId, u.UsedAt, u.Note }));
                    return rows.Where(u => !c.FocusId.HasValue || u.Id == c.FocusId.Value).Select(u =>
                        Hit("GlLegacyUse", u.Id, u.Number ?? ("MO" + u.Id), $"برای سفارش تولید {u.Number} نمونه اول باز نشد: {u.Note}", link: "/production/orders")
                            .On(u.UsedAt).Fact("item", u.ItemId)).ToList();
                });

            yield return Def("AUD-PO-004", "PROCUREMENT", AusSeverity.Major, "ردیف سفارش خرید باز به بازنگری منسوخ‌شده نقشه ارجاع دارد و تامین‌کننده بازنگری جدید را تایید نکرده است", Iso9, "8.4.3", "برنامه بسته G (ردیف سفارش خرید و بازنگری نقشه)",
                "PurchaseOrderLine of an open order whose cited drawing revision is no longer the revision in force and with no supplier confirmation of the new one", false, async c =>
                {
                    List<PoDrawingCitation.Stale> stale = await PoDrawingCitation.StaleAsync(c.Uow, null, c.Ct);
                    return stale.Where(s => !c.FocusId.HasValue || s.Line.Id == c.FocusId.Value).Take(c.Max).Select(s =>
                        Hit("PurchaseOrderLine", s.Line.Id, s.OrderNumber, $"سفارش {s.OrderNumber}: نقشه {s.Line.DrawingNumber} با بازنگری {s.Line.DrawingRevision} ارجاع شده ولی بازنگری {s.CurrentRevision} منتشر شده است؛ ردیف را اصلاح یا تایید تامین‌کننده را ثبت کنید.", link: "/procurement/orders/" + s.OrderId)
                            .Fact("order", s.OrderId).Fact("cited", s.Line.DrawingRevision).Fact("current", s.CurrentRevision)).ToList();
                });
        }
    }
}
