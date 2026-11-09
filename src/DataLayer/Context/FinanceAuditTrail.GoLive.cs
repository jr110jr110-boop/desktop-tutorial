using System;
using System.Collections.Generic;
using System.Linq;
using G = Entities.Model.NasbNiro.GoLive;
using I = Entities.Model.NasbNiro.Inventory;
using P = Entities.Model.NasbNiro.Production;
using Pb = Entities.Model.NasbNiro.ProductBank;
using Pc = Entities.Model.NasbNiro.ProjectControl;
using Proc = Entities.Model.NasbNiro.Procurement;
using Q = Entities.Model.NasbNiro.Quality;

namespace DataLayer.Context
{
    /// <summary>
    /// Package G (go-live): (1) the four new tables and the new columns join the audit chain; (2) the record-drift check (the nightly comparison of a row with its trail) now covers the engineering masters that Package D did not
    /// register: BOM and OPC, the project baseline and its lines, the EDMS revisions and their files, the inspection plans, the non-conformance and corrective-action records, the release notes, the production orders with
    /// their operations and components, and the routing revisions and operation standards. A direct SQL edit of any of them is then found, named by table, row and column. In its own file so that it merges cleanly.
    /// </summary>
    internal static partial class FinanceAuditTrail
    {
        /// <summary>
        /// The engineering masters added to the record-drift scope, each with the written reason for the columns that are NOT compared. A column is compared when the trail holds it (the audited column list of the type);
        /// the columns outside the list are the volatile operational ones: they change with every booking, count, scan or message of the floor and carry no engineering decision.
        /// </summary>
        internal static readonly IReadOnlyList<(Type Type, string NotCompared)> DriftScopeGoLive = new List<(Type, string)>
        {
            (typeof(Pb.PbBomHeader), "همه ستون‌های محتوا و امضا مقایسه می‌شوند (فهرست ستون‌های ثبت‌شده در سوابق)."),
            (typeof(Pb.PbBomLine), "همه ستون‌ها مقایسه می‌شوند."),
            (typeof(Pb.PbOpcRouting), "همه ستون‌های محتوا و امضا مقایسه می‌شوند."),
            (typeof(Pb.PbOpcStep), "همه ستون‌ها مقایسه می‌شوند."),
            (typeof(Pb.PbProjectBaseline), "همه ستون‌ها مقایسه می‌شوند."),
            (typeof(Pb.PbProjectBomLine), "همه ستون‌ها مقایسه می‌شوند."),
            (typeof(Pb.PbProjectRouting), "همه ستون‌ها مقایسه می‌شوند."),
            (typeof(Pc.PcDocumentRevision), "ستون‌های ثبت‌شده در سوابق مقایسه می‌شوند؛ شمارنده‌های نمایشی و زمان آخرین مشاهده بیرون‌اند."),
            (typeof(Pc.PcDocumentRevisionFile), "ستون‌های ثبت‌شده در سوابق مقایسه می‌شوند."),
            (typeof(Q.Itp), "ستون‌های ثبت‌شده مقایسه می‌شوند؛ شمارندهٔ اعلام و تاریخ‌های اطلاع‌رسانی خودکار بیرون‌اند."),
            (typeof(Q.ItpActivity), "ستون‌های ثبت‌شده در سوابق مقایسه می‌شوند؛ ردیف‌ها با کپی از قالب ساخته می‌شوند و ثبت افزودن ندارند، فقط ستون‌های تغییر‌یافته مقایسه می‌شوند."),
            (typeof(I.NonConformance), "ستون‌های ثبت‌شده در سوابق (وضعیت، تصمیم، امضاها، لینک‌ها) مقایسه می‌شوند؛ متن‌های شرح و یادداشت‌های آزاد بیرون‌اند."),
            (typeof(Q.Capa), "ستون‌های ثبت‌شده در سوابق مقایسه می‌شوند؛ سطح ارجاع شبانه نیز ثبت می‌شود."),
            (typeof(Q.ReleaseNote), "شماره، وضعیت، امضاکننده و علت لغو مقایسه می‌شوند؛ عکس بررسی‌ها و متن‌ها بیرون‌اند."),
            (typeof(P.MfRoutingRevision), "همه ستون‌ها مقایسه می‌شوند."),
            (typeof(P.MfOperationStandard), "همه ستون‌ها مقایسه می‌شوند."),
            (typeof(P.MfProductionOrder), "وضعیت، مقدار، بازنگری مسیر، اجازه‌های استثنا و منبع قطعات مقایسه می‌شوند؛ زمان‌ها، پیشرفت و یادداشت‌های کف کارگاه بیرون‌اند (با هر ثبت زمان تغییر می‌کنند)."),
            (typeof(P.MfOperation), "وضعیت، علت رد، مرکز کار و تایید QC مقایسه می‌شوند؛ مقدارهای خوب/ضایعات، زمان شروع و پایان و یادداشت‌ها بیرون‌اند (با هر ثبت زمان تغییر می‌کنند)."),
            (typeof(P.MfOrderComponent), "همه ستون‌ها (قطعه، مقدار، واحد) مقایسه می‌شوند."),
            (typeof(Proc.PurchaseOrderLine), "فقط ارجاع نقشه و تایید تامین‌کننده مقایسه می‌شود؛ مقدار دریافت‌شده و صورتحساب‌شده با هر رسید تغییر می‌کند و بیرون است."),
        };

        private static void DriftAlso(Type type)
        {
            if (!PdTypes.Contains(type)) PdTypes.Add(type);
        }

        private static void AddGoLive()
        {
            AuditPd(typeof(G.GlActivation), new[] { "StartAt", "RecordedByUserId", "Note", "Deleted" }, "آغاز کنترل پیکربندی و کیفیت");
            AuditPd(typeof(G.GlLegacyUse), new[] { "Kind", "ObjectKey", "WorkOrderId", "ItemId", "RevisionId", "Number", "Note", "UsedByUserId", "UsedAt", "Deleted" }, "استفاده از معافیت قدیمی");
            AuditPd(typeof(G.GlCharAdoption), new[] { "DeliverableId", "RevisionId", "ListId", "Status", "Reason", "EvidenceRef", "RequestedByUserId", "RequestedAt", "AcceptedByUserId", "AcceptedAt", "AcceptNote", "VoidReason", "Deleted" }, "پذیرش مشخصه‌های نقشه موجود");
            AuditPd(typeof(G.GlConfigAdoption), new[] { "WorkOrderId", "ProductId", "BomHeaderId", "BaselineId", "Status", "Reason", "EvidenceRef", "ComparisonNote", "LegacyLineCount", "BankLineCount", "RequestedByUserId", "RequestedAt", "EngReviewerUserId", "EngReviewedAt", "EngNote", "PmoReviewerUserId", "PmoReviewedAt", "PmoNote", "VoidReason", "Deleted" }, "پذیرش پیکربندی پروژه موجود");

            // the existing rows say where a legacy acceptance / free-text revision came from
            ExtendInspection(typeof(Q.QcRecord), "LegacyNote");
            ExtendInspection(typeof(Pc.PcTransmittalLine), "LegacyFreeText");

            // the purchase order line carries the drawing and revision of the order (ISO 9001 8.4.3) and the supplier's confirmation of a newer revision
            AuditPd(typeof(Proc.PurchaseOrderLine), new[] { "PurchaseOrderId", "RowNumber", "ItemId", "DrawingDeliverableId", "DrawingRevisionId", "DrawingNumber", "DrawingRevision", "DrawingCitedAt", "SupplierConfirmedRevisionId", "SupplierConfirmedRevision", "SupplierConfirmedAt", "SupplierConfirmedByUserId", "SupplierConfirmedRef" }, "ردیف سفارش خرید (نقشه و بازنگری)");

            // components of a production order were not in the map before
            if (!Audited.ContainsKey(typeof(P.MfOrderComponent)))
            {
                Audited[typeof(P.MfOrderComponent)] = new[] { "OrderId", "ItemId", "Quantity", "Unit" };
                Labels[typeof(P.MfOrderComponent)] = "قطعهٔ دستور تولید";
            }
            foreach ((Type type, string _) in DriftScopeGoLive) DriftAlso(type);
        }
    }
}
