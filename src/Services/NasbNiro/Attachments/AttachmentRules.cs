using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Entities.Model.NasbNiro.Attachments;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Procurement;

namespace Services.NasbNiro.Attachments
{
    /// <summary>PB3: the texts shown to the user (Persian).</summary>
    public static class AttachmentText
    {
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string NoFile = "فایلی برای بارگذاری ارسال نشده است.";
        public const string OwnerNotFound = "رکوردی که می‌خواهید فایل را به آن پیوست کنید پیدا نشد.";
        public const string NotFound = "پیوست پیدا نشد.";
        public const string AlreadyVoided = "این پیوست قبلاً ابطال شده است.";
        public const string AlreadySuperseded = "این پیوست قبلاً با نسخه جدیدتری جایگزین شده است.";
        public const string ReasonRequired = "دلیل ابطال را بنویسید.";
        public const string InvalidKind = "نوع مدرک نامعتبر است.";
        public const string OwnerCancelled = "این رکورد لغو یا رد شده است و پیوست تازه‌ای نمی‌پذیرد.";
        public const string SignatureMismatch = "محتوای فایل با پسوند آن هم‌خوان نیست؛ فایل پذیرفته نشد.";
        public const string RemoveAfterApproval = "رکورد تایید شده است؛ پیوست حذف نمی‌شود و فقط با ذکر دلیل ابطال یا با نسخه جدیدتر جایگزین می‌شود.";
        public const string RemoveNotUploader = "پیش از تایید، فقط کاربری که فایل را بارگذاری کرده می‌تواند آن را حذف کند.";
        public const string PhysicalDeleteRefused = "حذف فیزیکی مدارک مجاز نیست؛ پیوست را با ذکر دلیل ابطال کنید.";
        public const string NumberRequired = "شماره درخواست را وارد کنید.";
        public const string DossierNotFound = "با این شماره سندی پیدا نشد.";

        public static string ExtensionRefused(string extension) =>
            "فایل با پسوند «" + (string.IsNullOrWhiteSpace(extension) ? "بدون پسوند" : extension) + "» پذیرفته نمی‌شود. پسوندهای مجاز: " + string.Join("، ", AttachmentRules.AllowedExtensions) + ".";

        public static string TooLarge(long maxBytes) => "حجم فایل بیش از حد مجاز است (حداکثر " + (maxBytes / (1024 * 1024)) + " مگابایت).";

        public static string KindTitle(AttachmentKind kind)
        {
            switch (kind)
            {
                case AttachmentKind.Drawing: return "نقشه";
                case AttachmentKind.Bom: return "BOM";
                case AttachmentKind.Specification: return "مشخصات فنی";
                case AttachmentKind.Datasheet: return "دیتاشیت";
                case AttachmentKind.QcReport: return "گزارش کنترل کیفیت";
                case AttachmentKind.Certificate: return "گواهی";
                case AttachmentKind.MeasurementSheet: return "برگه اندازه‌گیری";
                case AttachmentKind.Minutes: return "صورتجلسه";
                case AttachmentKind.Invoice: return "فاکتور / رسید";
                case AttachmentKind.PaymentSlip: return "فیش واریزی";
                case AttachmentKind.DeliveryNote: return "بارنامه / برگه تحویل";
                case AttachmentKind.Photo: return "عکس";
                case AttachmentKind.Form: return "فرم";
                default: return "سایر";
            }
        }

        public static string OwnerTitle(AttachmentOwnerType type)
        {
            switch (type)
            {
                case AttachmentOwnerType.PurchaseRequest: return "درخواست خرید";
                case AttachmentOwnerType.PurchaseRequestLine: return "ردیف درخواست خرید";
                case AttachmentOwnerType.Inquiry: return "استعلام";
                case AttachmentOwnerType.Quote: return "پیشنهاد قیمت";
                case AttachmentOwnerType.PurchaseOrder: return "سفارش خرید";
                case AttachmentOwnerType.WarehouseDocument: return "سند انبار";
                case AttachmentOwnerType.QcInspection: return "بازرسی کیفی ورودی";
                case AttachmentOwnerType.Ncr: return "گزارش عدم انطباق";
                case AttachmentOwnerType.ProductionOperation: return "عملیات تولید";
                case AttachmentOwnerType.PartOperation: return "عملیات قطعه";
                case AttachmentOwnerType.SupplierInvoice: return "فاکتور تامین‌کننده";
                case AttachmentOwnerType.PaymentRequest: return "درخواست پرداخت";
                case AttachmentOwnerType.TreasuryPayment: return "پرداخت خزانه";
                case AttachmentOwnerType.ProjectCostEntry: return "هزینه دستی پروژه";
                case AttachmentOwnerType.Contract: return "قرارداد";
                case AttachmentOwnerType.Tender: return "مناقصه";
                case AttachmentOwnerType.PettyCashSettlementLine: return "ردیف تسویه تنخواه";
                case AttachmentOwnerType.InventoryItem: return "کالا";
                case AttachmentOwnerType.InventoryLot: return "محموله";
                case AttachmentOwnerType.SerialUnit: return "سریال";
                case AttachmentOwnerType.BuyOrder: return "دستور خرید";
                case AttachmentOwnerType.Process: return "فرایند";
                case AttachmentOwnerType.EquipmentProcess: return "فرایند تجهیز";
                case AttachmentOwnerType.TmTask: return "وظیفه";
                case AttachmentOwnerType.PcProgressSubmission: return "گزارش پیشرفت";
                case AttachmentOwnerType.PcProjectPhoto: return "عکس پروژه";
                case AttachmentOwnerType.PbBomHeader: return "BOM محصول";
                case AttachmentOwnerType.PbOpcRouting: return "مسیر ساخت (OPC)";
                default: return "رکورد";
            }
        }
    }

    /// <summary>PB3: roles of the attachment layer. Read: who sees the attachments of a record. Write: who adds, voids and supersedes (the owning module's write roles).</summary>
    public static class AttachmentRoles
    {
        /// <summary>The procurement readers plus the people of the shop floor who read purchase documents (the same set the procurement controller opens to).</summary>
        public const string Procurement = ProcurementRoles.Readers + ",ASSEMBLER,ENGINEER";

        /// <summary>Warehouse, quality and production records: everyone who works with stock and the shop floor, the buyer and finance (who look at the receipt).</summary>
        public const string Operational = "PMO,CEO,PLANNING,COMMERCIAL,FINANCIAL,FINANCEMANAGER,TREASURY,WAREHOUSE,WHSUPERVISOR,ENGINEERING,ENGINEER,QC,CONSTRUCTION,ASSEMBLER,PRODUCTIONMANAGER,SHOPSUPERVISOR,SECURITY";

        /// <summary>Who may read the technical documents of a purchase request (PB3 P3): everyone who may read one of the downstream records (inquiry ... payment request, receipt, inspection, NCR).</summary>
        public const string TechnicalChain = Procurement + "," + Operational;

        /// <summary>The files that carry money (invoice, payment slip): never QC, the warehouse or the shop floor, on any record.</summary>
        public const string Prices = "PMO,CEO,PLANNING,COMMERCIAL,FINANCIAL,FINANCEMANAGER,TREASURY,AUDITOR";

        public const string Finance = "PMO,FINANCIAL,FINANCEMANAGER,TREASURY,CEO,AUDITOR";

        /// <summary>Who may add and void files on a purchase request before it is sent: the creator (checked per record) and the PMO. After it is sent: only these roles, with a reason.</summary>
        public const string PurchaseRequestLockedWrite = "PLANNING,PMO";
        public const string PurchaseRequestWrite = ProcurementRoles.Requesters + ",PLANNING,ENGINEERING";
        public const string NcrLockedWrite = "PMO,QC";
        /// <summary>PB2: the documents of a product BOM or an OPC routing (drawings, specifications): engineering writes while the revision is a draft; after it is signed only engineering and the PMO void or replace, with a reason.</summary>
        public const string ProductBankWrite = "PMO,ENGINEERING,ENGINEER";
        public const string ProductBankLockedWrite = "PMO,ENGINEERING";
        public const string InquiryWrite = "COMMERCIAL,PLANNING,ENGINEERING,QC,CONSTRUCTION,PMO";
        public const string OrderWrite = "COMMERCIAL,PMO,CEO";
        public const string ReceiptWrite = "PMO,WAREHOUSE,WHSUPERVISOR,COMMERCIAL";
        public const string InspectionWrite = "PMO,QC";
        public const string NcrWrite = "PMO,QC,ENGINEERING,CONSTRUCTION,PRODUCTIONMANAGER,WAREHOUSE,WHSUPERVISOR";
        public const string ProductionWrite = "PMO,QC,PRODUCTIONMANAGER,SHOPSUPERVISOR,CONSTRUCTION,ENGINEERING,ENGINEER,ASSEMBLER,PLANNING";
        public const string PartOperationWrite = InventoryRoles.Production + ",WHSUPERVISOR";
        public const string InvoiceWrite = "COMMERCIAL,FINANCIAL,FINANCEMANAGER,TREASURY,PMO";
        public const string PaymentWrite = "FINANCIAL,FINANCEMANAGER,TREASURY,PMO";
        public const string CostWrite = "FINANCIAL,FINANCEMANAGER,PMO";
        public const string PettyCashWrite = "FINANCIAL,FINANCEMANAGER,TREASURY,PMO";
        public const string StockMasterWrite = "PMO,PLANNING,WAREHOUSE,WHSUPERVISOR,ENGINEERING,QC";

        // legacy modules: the same lists as WebApi's LegacyRoles (the services cannot see that class)
        public const string ContractWrite = "PMO,PLANNING,COMMERCIAL,PROJECTMANAGER";
        public const string TenderWrite = "PMO,COMMERCIAL,SALES";
        public const string BuyOrderWrite = "PMO,COMMERCIAL,FINANCIAL,ENGINEERING,QC,WAREHOUSE,ASSEMBLER,CONSTRUCTION";
        public const string ProcessWrite = "PMO,ENGINEERING,ENGINEER";
    }

    /// <summary>PB3: the pure rules (no database): the file type allow-list, the size cap and the remove/void/supersede decisions.</summary>
    public static class AttachmentRules
    {
        /// <summary>
        /// ★ The upload allow-list: spreadsheets, documents, PDF, pictures (also phone HEIC), drawings, STEP models, plain text and CSV, and mail (msg, eml).
        /// There is no setting for it on purpose: widening it is a reviewed code change.
        /// </summary>
        public static readonly string[] AllowedExtensions =
        {
            ".xlsx", ".xls", ".docx", ".doc", ".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".dwg", ".dxf", ".step", ".stp", ".txt", ".csv", ".msg", ".eml", ".heic", ".heif",
        };

        /// <summary>★ Archives are accepted only for the attachments of letters (Office module), where mail with its files travels as a zip.</summary>
        public const string ArchiveExtension = ".zip";

        /// <summary>The document code the front sends for the attachments of letters (see uploadFile in the front): the only context that may carry a zip.</summary>
        public const string LetterContextCode = "OFC";

        /// <summary>★ The size cap in megabytes; the procurement setting <see cref="AttachmentSettingKeys.MaxMegabytes"/> changes it.</summary>
        public const int DefaultMaxMegabytes = 50;

        public static bool IsAllowed(string fileName, out string extension) => IsAllowed(fileName, false, out extension);

        public static bool IsAllowed(string fileName, bool allowArchive, out string extension)
        {
            extension = Path.GetExtension(fileName ?? string.Empty)?.ToLowerInvariant() ?? string.Empty;
            return AllowedExtensions.Contains(extension) || (allowArchive && extension == ArchiveExtension);
        }

        /// <summary>The kinds that carry money: hidden from everybody outside <see cref="AttachmentRoles.Prices"/>, and never passed down from a purchase request.</summary>
        public static bool CarriesPrices(AttachmentKind kind) => kind == AttachmentKind.Invoice || kind == AttachmentKind.PaymentSlip;

        /// <summary>
        /// Magic-byte check of the first bytes against the extension. An executable (MZ, ELF) is refused whatever its name; a PDF must start with %PDF (after an optional BOM and white space);
        /// an Office Open XML file must be a ZIP that names [Content_Types].xml; a DXF must open with a SECTION; text formats must be text. An extension that is not on the list never matches.
        /// </summary>
        public static bool MatchesSignature(string extension, byte[] head, int length)
        {
            if (head == null || length <= 0) return false;
            bool Starts(params byte[] sig)
            {
                if (length < sig.Length) return false;
                for (int i = 0; i < sig.Length; i++) if (head[i] != sig[i]) return false;
                return true;
            }
            // executables are refused anywhere, with any name
            if (Starts(0x4D, 0x5A) || Starts(0x7F, 0x45, 0x4C, 0x46)) return false;
            int Skip()
            {
                int i = 0;
                if (length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) i = 3;
                while (i < length && (head[i] == 0x20 || head[i] == 0x09 || head[i] == 0x0D || head[i] == 0x0A)) i++;
                return i;
            }
            bool StartsAt(int at, string text)
            {
                if (at + text.Length > length) return false;
                for (int i = 0; i < text.Length; i++) if (head[at + i] != (byte)text[i]) return false;
                return true;
            }
            bool IsZip() => Starts(0x50, 0x4B, 0x03, 0x04);
            bool IsOle() => Starts(0xD0, 0xCF, 0x11, 0xE0);
            bool OoxmlZip() => IsZip() && IndexOf(head, length, System.Text.Encoding.ASCII.GetBytes("[Content_Types].xml"), length) >= 0;
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case ".pdf": return StartsAt(Skip(), "%PDF");
                case ".jpg":
                case ".jpeg": return Starts(0xFF, 0xD8, 0xFF);
                case ".png": return Starts(0x89, 0x50, 0x4E, 0x47);
                case ".tif":
                case ".tiff": return Starts(0x49, 0x49, 0x2A, 0x00) || Starts(0x4D, 0x4D, 0x00, 0x2A);
                case ".heic":
                case ".heif": return length >= 12 && head[4] == (byte)'f' && head[5] == (byte)'t' && head[6] == (byte)'y' && head[7] == (byte)'p';
                case ".docx":
                case ".xlsx": return OoxmlZip();
                case ".doc":
                case ".xls": return IsOle() || OoxmlZip();   // a renamed OOXML file is still an Office file
                case ".msg": return IsOle();
                case ".zip": return IsZip();
                case ".dwg": return Starts(0x41, 0x43, 0x31);   // "AC1..." version stamp
                case ".dxf":
                    if (StartsAt(0, "AutoCAD Binary DXF")) return true;
                    {
                        int o = length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0;
                        return DxfStart.IsMatch(System.Text.Encoding.ASCII.GetString(head, o, length - o));
                    }
                case ".step":
                case ".stp": return StartsAt(Skip(), "ISO-10303-21");
                case ".txt":
                case ".csv":
                case ".eml": return !ContainsNul(head, length);
                default: return false;
            }
        }

        // ASCII DXF: optional comment pairs (group 999), then "0" and "SECTION"
        private static readonly System.Text.RegularExpressions.Regex DxfStart =
            new System.Text.RegularExpressions.Regex(@"^\s*(999\s*\r?\n[^\r\n]*\r?\n\s*)*0\s*\r?\nSECTION", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static string Text(byte[] head, int length) => System.Text.Encoding.ASCII.GetString(head, 0, length);

        private static bool ContainsNul(byte[] head, int length)
        {
            for (int i = 0; i < length; i++) if (head[i] == 0) return true;
            return false;
        }

        private static int IndexOf(byte[] data, int length, byte[] pattern, int limit)
        {
            int max = Math.Min(length, limit) - pattern.Length;
            for (int i = 0; i <= max; i++)
            {
                bool all = true;
                for (int j = 0; j < pattern.Length && all; j++) all = data[i + j] == pattern[j];
                if (all) return i;
            }
            return -1;
        }

        /// <summary>
        /// What the caller may do with one attachment. Remove (hide, logged): only the uploader, only while the owner is not approved. Void (reason required) and supersede: the write roles of the module, at any time.
        /// Nothing is ever deleted physically.
        /// </summary>
        public static AttachmentActions Actions(bool callerCanWrite, bool callerIsUploader, bool ownerLocked, bool voided, bool superseded, bool removed)
        {
            bool live = !voided && !removed;
            return new AttachmentActions
            {
                CanRemove = live && !superseded && !ownerLocked && callerIsUploader && callerCanWrite,
                CanVoid = live && !superseded && callerCanWrite,
                CanSupersede = live && !superseded && callerCanWrite,
            };
        }
    }

    public sealed class AttachmentActions
    {
        public bool CanRemove { get; set; }
        public bool CanVoid { get; set; }
        public bool CanSupersede { get; set; }
    }

    public static class AttachmentSettingKeys
    {
        public const string MaxMegabytes = "AttachmentMaxMegabytes";
    }
}
