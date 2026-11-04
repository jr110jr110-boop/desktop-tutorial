using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.TraceDtos;

namespace Services.NasbNiro.Trace
{
    public sealed partial class TraceCenterService
    {
        private static readonly Dictionary<string, string> KindFa = new Dictionary<string, string>
        {
            ["product"] = "محصول", ["assembly"] = "مجموعه", ["part"] = "قطعه", ["stage"] = "مرحله OPC", ["order"] = "دستور تولید", ["operation"] = "عملیات", ["unit"] = "قطعه ساخته‌شده",
            ["material"] = "ماده / ورودی", ["receipt"] = "رسید خرید", ["purchase"] = "سفارش خرید", ["inquiry"] = "استعلام", ["request"] = "درخواست خرید", ["inspection"] = "بازرسی ورودی",
            ["ncr"] = "عدم انطباق", ["delivery"] = "تحویل به مشتری",
        };

        private static string Shamsi(DateTime? date)
        {
            if (!date.HasValue) return "";
            var pc = new PersianCalendar();
            DateTime d = date.Value;
            return $"{pc.GetYear(d):0000}/{pc.GetMonth(d):00}/{pc.GetDayOfMonth(d):00}";
        }

        private static string BasisFa(string basis) => basis == "asBuilt" ? "ساخته‌شده" : "طبق طرح";

        public async Task<ResponseDto<byte[]>> ExportTreeAsync(TraceQuery query, CancellationToken ct)
        {
            ResponseDto<TraceTreeDto> tree = await TreeAsync(query, ct);
            if (!tree.Success) return new ResponseDto<byte[]>(false, tree.Message).HasHttpStatusCode(tree.HttpStatusCode);
            if (tree.Data.Root == null) return Bad<byte[]>("برای انتخاب‌های چندگانه خروجی ساخته نمی‌شود؛ ابتدا یک مورد را انتخاب کنید.");

            var nodes = new XlsxSheet { Name = "درخت محصول", Header = new List<string> { "سطح", "نوع", "مبنا", "عنوان", "کد", "وضعیت", "تاریخ", "مقدار", "واحد", "جزئیات", "فایل‌ها" } };
            var files = new XlsxSheet { Name = "مدارک", Header = new List<string> { "گره", "عنوان فایل", "نام فایل", "نوع", "ویرایش", "رکورد مبدا", "ازطریق زنجیره" } };
            void Walk(TraceNodeDto n, int level)
            {
                nodes.Rows.Add(new object[]
                {
                    level, KindFa.TryGetValue(n.Kind, out string kind) ? kind : n.Kind, BasisFa(n.Basis), n.Title, n.Code, n.Status, Shamsi(n.Date), n.Quantity.HasValue ? (object)n.Quantity.Value : "", n.Unit,
                    string.Join(" | ", n.Facts.Select(f => f.Label + ": " + f.Value)), n.Files.Count == 0 ? "" : string.Join(" | ", n.Files.Select(f => f.Title ?? f.FileName)),
                });
                foreach (TraceFileDto f in n.Files)
                    files.Rows.Add(new object[] { n.Title, f.Title, f.FileName, f.KindTitle, f.Revision, f.OwnerTitle + " " + f.OwnerNumber, f.Propagated ? "بله" : "" });
                foreach (TraceNodeDto child in n.Children) Walk(child, level + 1);
            }
            Walk(tree.Data.Root, 0);
            var info = new XlsxSheet { Name = "مشخصات", Header = new List<string> { "عنوان", "مقدار" } };
            info.Rows.Add(new object[] { "جستجو", tree.Data.Query });
            info.Rows.Add(new object[] { "حالت", tree.Data.Mode });
            info.Rows.Add(new object[] { "مبنا", tree.Data.Basis == "project" ? "کپی BOM پروژه" : tree.Data.Basis == "asDesigned" ? "طبق طرح" : "ساخته‌شده" });
            info.Rows.Add(new object[] { "تعداد گره", tree.Data.NodeCount });
            if (tree.Data.PricesMasked) info.Rows.Add(new object[] { "نکته", "قیمت‌ها برای نقش شما نمایش داده نمی‌شود." });
            foreach (string note in tree.Data.Notes) info.Rows.Add(new object[] { "یادداشت", note });
            return ResponseDtoHelper.Ok(SimpleXlsx.Build(new List<XlsxSheet> { nodes, files, info }));
        }

        public async Task<ResponseDto<byte[]>> ExportReverseAsync(ReverseTraceQuery query, CancellationToken ct)
        {
            ResponseDto<ReverseTraceDto> reverse = await ReverseAsync(query, ct);
            if (!reverse.Success) return new ResponseDto<byte[]>(false, reverse.Message).HasHttpStatusCode(reverse.HttpStatusCode);
            ReverseTraceDto d = reverse.Data;
            var roots = new XlsxSheet { Name = "مبدا", Header = new List<string> { "نوع", "کد کالا", "عنوان کالا", "شماره بچ", "شماره ذوب", "شماره گواهی", "سریال", "تامین‌کننده", "رسید", "سفارش خرید", "درخواست خرید", "مقدار دریافتی", "تاریخ دریافت" } };
            foreach (ReverseRootDto r in d.Roots)
                roots.Rows.Add(new object[] { r.Kind == "lot" ? "بچ" : "سریال", r.ItemCode, r.ItemTitle, r.LotNumber, r.HeatNumber, r.CertificateNumber, r.SerialNumber, r.ProviderName, r.ReceiptNumber, r.PurchaseOrderNumber, r.PurchaseRequestNumber, r.ReceivedQuantity.HasValue ? (object)r.ReceivedQuantity.Value : "", Shamsi(r.ReceivedDate) });
            var items = new XlsxSheet { Name = "قطعات و مجموعه‌ها", Header = new List<string> { "کد کالا", "عنوان", "شماره قطعه", "مرحله", "تعداد گره", "کمترین سطح", "بیشترین سطح" } };
            foreach (ReverseItemDto i in d.Items) items.Rows.Add(new object[] { i.Code, i.Title, i.PartNumber, i.StageCode, i.NodeCount, i.MinLevel, i.MaxLevel });
            var nodes = new XlsxSheet { Name = "مسیر مصرف", Header = new List<string> { "سطح", "نوع", "کد کالا", "عنوان", "بچ", "ذوب", "سریال", "از", "ارتباط", "دستور تولید", "پروژه", "مقدار", "پایان زنجیره" } };
            foreach (ReverseNodeDto n in d.Nodes)
                nodes.Rows.Add(new object[] { n.Level, n.Kind == "lot" ? "بچ" : n.Kind == "serial" ? "سریال" : "کالا", n.ItemCode, n.ItemTitle, n.LotNumber, n.HeatNumber, n.SerialNumber, n.FromKey, ViaFa(n.Via), n.OrderNumber, n.WorkOrderCode, n.Quantity.HasValue ? (object)n.Quantity.Value : "", n.Terminal ? "بله" : "" });
            var projects = new XlsxSheet { Name = "پروژه‌ها", Header = new List<string> { "دستور ساخت", "عنوان", "قرارداد", "کارفرما", "تعداد گره" } };
            foreach (ReverseProjectDto p in d.Projects) projects.Rows.Add(new object[] { p.WorkOrderCode, p.WorkOrderTitle, p.ContractTitle ?? p.ContractCode, p.Customer, p.NodeCount });
            var deliveries = new XlsxSheet { Name = "تحویل به مشتری", Header = new List<string> { "سند", "تاریخ", "کد کالا", "سریال", "بچ", "مقدار", "پروژه", "قرارداد", "کارفرما", "مقصد" } };
            foreach (ReverseDeliveryDto x in d.Deliveries) deliveries.Rows.Add(new object[] { x.DocumentNumber, Shamsi(x.DocumentDate), x.ItemCode, x.SerialNumber, x.LotNumber, x.Quantity, x.WorkOrderCode, x.ContractCode, x.Customer, x.Destination });
            var consumptions = new XlsxSheet { Name = "مصرف خارج از دستور تولید", Header = new List<string> { "سند", "تاریخ", "نوع", "کد کالا", "بچ", "سریال", "مقدار", "پروژه", "تامین‌کننده" } };
            foreach (ReverseConsumptionDto x in d.Consumptions)
                consumptions.Rows.Add(new object[] { x.DocumentNumber, Shamsi(x.DocumentDate), x.Kind == "issue" ? "حواله" : x.Kind == "subcontract" ? "ارسال به پیمانکار" : "برگشت به تامین‌کننده", x.ItemCode, x.LotNumber, x.SerialNumber, x.Quantity, x.WorkOrderCode, x.ProviderName });
            var installed = new XlsxSheet { Name = "پایگاه نصب‌شده", Header = new List<string> { "سریال", "تگ", "عنوان", "پروژه", "قرارداد", "کارفرما", "تاریخ تحویل", "از رده خارج" } };
            foreach (ReverseInstalledDto x in d.Installed) installed.Rows.Add(new object[] { x.SerialNumber, x.Tag, x.Title, x.WorkOrderCode, x.ContractCode, x.Customer, Shamsi(x.HandoverDate), x.Decommissioned ? "بله" : "" });
            var info = new XlsxSheet { Name = "مشخصات", Header = new List<string> { "عنوان", "مقدار" } };
            info.Rows.Add(new object[] { "جستجو", d.Query });
            info.Rows.Add(new object[] { "حالت", d.Mode });
            info.Rows.Add(new object[] { "تعداد قطعه و مجموعه", d.Counts.Items });
            info.Rows.Add(new object[] { "تعداد گره", d.Counts.Nodes });
            info.Rows.Add(new object[] { "عمق جستجو", d.MaxDepth });
            if (d.Truncated) info.Rows.Add(new object[] { "هشدار", "نتیجه به سقف عمق یا تعداد رسید و ممکن است کامل نباشد." });
            foreach (string note in d.Notes) info.Rows.Add(new object[] { "یادداشت", note });
            return ResponseDtoHelper.Ok(SimpleXlsx.Build(new List<XlsxSheet> { info, roots, items, nodes, projects, deliveries, consumptions, installed }));
        }

        private static string ViaFa(string via) => via switch
        {
            "production" => "مصرف در دستور تولید", "conversion" => "تبدیل مرحله", "assembly" => "نصب در مجموعه", "lotMember" => "قطعه‌ای از همین بچ", _ => via,
        };
    }
}
