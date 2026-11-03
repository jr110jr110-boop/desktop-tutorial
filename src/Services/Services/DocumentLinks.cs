using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Entities.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Services.Services
{
    /// <summary>One column of the model that holds the id of a stored file (a <see cref="Document"/>).</summary>
    public sealed class DocumentLink
    {
        public DocumentLink(Type entityType, string property, bool isNullable, bool isText = false)
        {
            EntityType = entityType;
            Property = property;
            IsNullable = isNullable;
            IsText = isText;
        }

        /// <summary>The CLR type of the record that holds the file id (a link table such as ContractFile, or the record itself such as OfLetter).</summary>
        public Type EntityType { get; }

        public string Property { get; }

        public bool IsNullable { get; }

        /// <summary>The column is text that holds several ids separated by commas (the attachments of a sales RFQ), not a single Guid.</summary>
        public bool IsText { get; }

        public override string ToString() => EntityType.Name + "." + Property;
    }

    /// <summary>
    /// R1a2 (H3): the list of columns that point at a stored file and the words used to name the owner of a file in a refusal.
    /// The list is read from the EF model, so a column added later that follows the naming rules is covered without touching this class;
    /// a unit test pins the list and fails when a Guid column of the model is neither found here nor known to hold something else.
    /// </summary>
    public static class DocumentLinks
    {
        /// <summary>
        /// Property names that hold the id of a file: anything with "Document" in it (DocumentId, SignatureDocumentId ...) and the older names
        /// that were written before that convention (profile photo, project charter file, the two inquiry documents, the buy-order time sheet and log sheet).
        /// </summary>
        private static readonly Regex NamePattern = new Regex(
            @"Document|PhotoId$|CharterFileId$|^(TechnicalDoc|FinancialDoc|TimeSheet|LogShitId)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Text columns that keep a list of file ids (separated by commas).</summary>
        private static readonly Regex TextPattern = new Regex(@"(Attachment|Document|File)Ids$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly ConcurrentDictionary<IModel, IReadOnlyList<DocumentLink>> Cache = new ConcurrentDictionary<IModel, IReadOnlyList<DocumentLink>>();

        /// <summary>Every column of the model that points at a stored file, found once per model.</summary>
        public static IReadOnlyList<DocumentLink> Of(IModel model) => Cache.GetOrAdd(model, Discover);

        /// <summary>The Guid properties of the model that look like a reference to a file (the file table itself and plain primary keys are left out).</summary>
        public static IReadOnlyList<DocumentLink> Discover(IModel model)
        {
            var list = new List<DocumentLink>();
            foreach (IEntityType entityType in model.GetEntityTypes())
            {
                Type clr = entityType.ClrType;
                if (clr == null || clr == typeof(Document) || entityType.IsOwned() || entityType.HasSharedClrType) continue;
                foreach (IProperty property in entityType.GetProperties())
                {
                    if (property.IsShadowProperty()) continue;
                    if (property.ClrType == typeof(string) && TextPattern.IsMatch(property.Name))
                    {
                        list.Add(new DocumentLink(clr, property.Name, true, isText: true));
                        continue;
                    }
                    if (property.ClrType != typeof(Guid) && property.ClrType != typeof(Guid?)) continue;
                    if (property.Name == "Id") continue;
                    if (!NamePattern.IsMatch(property.Name)) continue;
                    list.Add(new DocumentLink(clr, property.Name, property.ClrType == typeof(Guid?)));
                }
            }
            return list.OrderBy(l => l.EntityType.Name, StringComparer.Ordinal).ThenBy(l => l.Property, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// How to name the owner of a file kept in a link table: the Persian kind of the owner, the CLR type of the owner record and the property of the link record that holds its id.
        /// </summary>
        public sealed class ParentOwner
        {
            public ParentOwner(string kind, Type parentType, string foreignKey)
            {
                Kind = kind;
                ParentType = parentType;
                ForeignKey = foreignKey;
            }

            public string Kind { get; }
            public Type ParentType { get; }
            public string ForeignKey { get; }
        }

        /// <summary>Link tables: the file is named after the record the link belongs to (the contract, the tender ...), not after the link row.</summary>
        public static readonly IReadOnlyDictionary<string, ParentOwner> LinkTables = new Dictionary<string, ParentOwner>(StringComparer.Ordinal)
        {
            ["ContractFile"] = new ParentOwner("قرارداد", typeof(Entities.Model.NasbNiro.Contracts.ContractEntity), "ContractId"),
            ["TenderFile"] = new ParentOwner("مناقصه", typeof(Entities.Model.NasbNiro.Tenders.Tender), "TenderId"),
            ["BuyOrderFile"] = new ParentOwner("سفارش خرید", typeof(Entities.Model.NasbNiro.Contracts.BuyOrder), "BuyOrderId"),
            ["ProcessFile"] = new ParentOwner("فرایند", typeof(Entities.Model.NasbNiro.Contracts.ProcessEntity), "ProcessId"),
            ["EquipmentProcessFile"] = new ParentOwner("فرایند تجهیز", typeof(Entities.Model.NasbNiro.Equipments.EquipmentProcess), "EquipmentProcessId"),
            ["OfLetterAttachment"] = new ParentOwner("نامه", typeof(Entities.Model.NasbNiro.Office.OfLetter), "LetterId"),
        };

        /// <summary>The Persian word for the kind of record that holds a file (used when the record itself is the owner). A type that is not listed is named by its class name.</summary>
        public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Audit"] = "ممیزی کیفی",
            ["BuyOrder"] = "سفارش خرید",
            ["CalibrationEvent"] = "رویداد کالیبراسیون",
            ["CapaAction"] = "اقدام اصلاحی و پیشگیرانه",
            ["ContractEntity"] = "قرارداد",
            ["DocumentRevisionRecord"] = "بازنگری مدرک کنترل‌شده",
            ["FnInsuranceClearance"] = "تسویه بیمه",
            ["FnOutboxPackage"] = "بسته ارسال به نرم‌افزار مالی",
            ["FnPettyCashSettlementLine"] = "تسویه تنخواه",
            ["FnVoucherAttachment"] = "پیوست سند حسابداری",
            ["Inquiry"] = "استعلام",
            ["InventoryDocument"] = "سند انبار",
            ["InventoryLot"] = "محموله انبار",
            // R1a2 (merge preparation): the inspection and test plan activity of the quality branch (R1q: ItpActivity.EvidenceDocumentId becomes a file link at the merge). The entity is not in this branch yet;
            // until it is, the name is only a word in a list and the unit test accepts it because it is on the list of kinds that come with the merge (DocumentRulesTests).
            ["ItpActivity"] = "فعالیت برنامه بازرسی و آزمون",
            ["ManagementReview"] = "بازنگری مدیریت",
            ["MaterialCertificate"] = "گواهی مواد",
            ["MeasuringEquipment"] = "تجهیز اندازه‌گیری",
            ["OfDecision"] = "مصوبه",
            ["OfLeaveRequest"] = "درخواست مرخصی",
            ["OfLetter"] = "نامه",
            ["OfUserSignature"] = "امضای ثبت‌شده کاربر",
            ["PartOperationFile"] = "فایل عملیات قطعه",
            ["PcContractTerms"] = "شرایط قرارداد (کنترل پروژه)",
            ["PcEngineeringDeliverable"] = "مدرک مهندسی پروژه",
            ["PcGuaranteeEvent"] = "رویداد ضمانت‌نامه",
            ["PcShipment"] = "محموله",
            ["Pqr"] = "گزارش PQR",
            ["QcInspection"] = "بازرسی کیفی",
            ["QcRecord"] = "سابقه کنترل کیفی",
            ["Qualification"] = "تأییدیه صلاحیت",
            ["ServiceAcceptance"] = "پذیرش خدمات",
            ["SlInquiry"] = "استعلام مشتری",
            ["SlProposal"] = "پیشنهاد فروش",
            ["SupplierInvoice"] = "فاکتور تأمین‌کننده",
            ["SupplierQuote"] = "پیشنهاد قیمت تأمین‌کننده",
            ["SvServiceOrder"] = "دستور خدمات پس از فروش",
            ["TestReport"] = "گزارش آزمون",
            ["User"] = "تصویر نمایه کاربر",
        };

        /// <summary>Property names tried, in this order, to find a number or a title that a person recognises the owner by.</summary>
        private static readonly string[] NameProperties = { "Number", "Code", "Title", "Name", "Subject", "UserName", "FileName" };

        /// <summary>The Persian description of the owner of a file: the kind and, when the record has one, its number or title («قرارداد «C-12»» or "قرارداد شماره 7").</summary>
        public static string Describe(DocumentLink link, object linkRecord, object parentRecord)
        {
            string name = link.EntityType.Name;
            object owner = linkRecord;
            string kind;
            if (LinkTables.TryGetValue(name, out ParentOwner parent))
            {
                kind = parent.Kind;
                owner = parentRecord ?? linkRecord;
            }
            else
            {
                kind = Kinds.TryGetValue(name, out string known) ? known : "رکورد «" + name + "»";
            }

            string label = NameOf(owner);
            if (!string.IsNullOrWhiteSpace(label)) return kind + " «" + label + "»";
            object id = ValueOf(owner, "Id");
            return id != null ? kind + " شماره " + Convert.ToString(id, CultureInfo.InvariantCulture) : kind;
        }

        private static object ValueOf(object record, string property)
        {
            if (record == null) return null;
            PropertyInfo info = record.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            return info == null || info.GetIndexParameters().Length != 0 ? null : info.GetValue(record);
        }

        private static string NameOf(object record)
        {
            foreach (string property in NameProperties)
            {
                string text = ValueOf(record, property) as string;
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim().Length > 60 ? text.Trim().Substring(0, 60) : text.Trim();
            }
            return null;
        }
    }
}
