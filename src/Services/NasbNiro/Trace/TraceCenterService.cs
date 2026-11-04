using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Services.NasbNiro.Attachments;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.TraceDtos;

namespace Services.NasbNiro.Trace
{
    public interface ITraceCenterService
    {
        /// <summary>The product tree: BOM structure of a product (the project copy, else the released bank BOM) with, for each part, the OPC stages, the production orders and their QC, the material lots and their purchase chain.</summary>
        Task<ResponseDto<TraceTreeDto>> TreeAsync(TraceQuery query, CancellationToken ct);

        /// <summary>Where used: from a material lot, heat number, supplier, purchase order, purchase request or material request to every part, assembly, product serial, project and customer delivery that consumed it.</summary>
        Task<ResponseDto<ReverseTraceDto>> ReverseAsync(ReverseTraceQuery query, CancellationToken ct);

        Task<ResponseDto<byte[]>> ExportTreeAsync(TraceQuery query, CancellationToken ct);
        Task<ResponseDto<byte[]>> ExportReverseAsync(ReverseTraceQuery query, CancellationToken ct);
    }

    public static class TraceRoles
    {
        /// <summary>Everyone who reads the technical chain (purchase to stock to shop floor) and the quality and audit readers.</summary>
        public const string Readers = AttachmentRoles.TechnicalChain + ",QA,AUDITOR,PROJECTMANAGER";

        /// <summary>
        /// Who sees the names of suppliers and customers (QC, warehouse, procurement, project, engineering and management). Security, assembly and construction read the trace
        /// but see the codes only: the names show as the word «محرمانه».
        /// </summary>
        public const string Names = "PMO,CEO,PLANNING,COMMERCIAL,FINANCIAL,FINANCEMANAGER,TREASURY,WAREHOUSE,WHSUPERVISOR,ENGINEERING,ENGINEER,QC,QA,AUDITOR,PROJECTMANAGER,PRODUCTIONMANAGER,SHOPSUPERVISOR";

        public const string Hidden = "محرمانه";

        /// <summary>Who sees unit prices and amounts of the purchase chain. QC, the warehouse and the shop floor never do.</summary>
        public const string Prices = AttachmentRoles.Prices;

        /// <summary>Rebuild or backfill of the genealogy links.</summary>
        public const string Admin = "PMO,PLANNING,WHSUPERVISOR,QA";
    }

    /// <summary>
    /// PB4 read model of the trace page. One service, four reads. Every read is a fixed number of batched queries: the work goes level by level (one query per kind of link per level), never one query per
    /// node, and depth and size are capped (the reply says when a cap cut the result).
    /// </summary>
    public sealed partial class TraceCenterService : ITraceCenterService
    {
        public const int DefaultTreeDepth = 6;
        public const int MaxTreeDepth = 8;
        public const int MaxParts = 400;
        public const int DefaultReverseDepth = 10;
        public const int MaxReverseDepth = 15;
        public const int MaxReverseNodes = 5000;
        public const int MaxRoots = 300;
        public const int MaxLotsPerPart = 40;

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IAttachmentService _attachments;

        public TraceCenterService(IUnitOfWork uow, IInventoryUserContext user, IAttachmentService attachments)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _attachments = attachments ?? throw new ArgumentNullException(nameof(attachments));
        }

        private bool SeesNames => _user.IsInRole(TraceRoles.Names);

        private bool SeesPrices => _user.IsInRole(TraceRoles.Prices);

        private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>The part number of an item code or a typed text: the code without a trailing "-XX" stage suffix, when the suffix looks like one (a symbol and two letters at the end).</summary>
        internal static string StripStage(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 4) return text;
            int n = text.Length;
            bool suffix = n > 3 && (text[n - 3] == '-' || text[n - 3] == '.' || text[n - 3] == '/' || text[n - 3] == '_') && char.IsLetter(text[n - 2]) && char.IsLetter(text[n - 1]);
            return suffix ? text.Substring(0, n - 3) : text;
        }

        private static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);

        /// <summary>The label codes printed by the warehouse: S/item/serial and L/item/lot.</summary>
        internal static (string Kind, string Item, string Value) ParseLabel(string text)
        {
            string[] parts = text.Split('/');
            if (parts.Length >= 3)
            {
                string kind = parts[0].ToUpperInvariant();
                if (kind == "S" || kind == "L") return (kind, parts[1], string.Join("/", parts.Skip(2)));
            }
            return (null, null, null);
        }
    }
}
