using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.TraceDtos
{
    // ------------------------------------------------------------------------------------------ genealogy links

    public class GenealogyLinkDto
    {
        public int Id { get; set; }
        public int ProductionOrderId { get; set; }
        public string OrderNumber { get; set; }
        public int? OperationId { get; set; }
        public string OperationTitle { get; set; }

        public int OutputItemId { get; set; }
        public string OutputItemCode { get; set; }
        public string OutputItemTitle { get; set; }
        public int? OutputLotId { get; set; }
        public string OutputLotNumber { get; set; }
        public int? OutputSerialUnitId { get; set; }
        public string OutputSerialNumber { get; set; }

        public int InputItemId { get; set; }
        public string InputItemCode { get; set; }
        public string InputItemTitle { get; set; }
        public int? InputLotId { get; set; }
        public string InputLotNumber { get; set; }
        public string InputHeatNumber { get; set; }
        public string InputCertificateNumber { get; set; }
        public int? InputSerialUnitId { get; set; }
        public string InputSerialNumber { get; set; }

        public decimal Quantity { get; set; }
        public bool IsShared { get; set; }
        public int? IssueDocumentId { get; set; }
        public string IssueDocumentNumber { get; set; }
        public int ReceiptDocumentId { get; set; }
        public string ReceiptDocumentNumber { get; set; }
        public DateTime LinkDate { get; set; }
        public int Source { get; set; }
        public string Note { get; set; }
    }

    /// <summary>What a rebuild or the backfill did, and what it could not do (each reason is a Persian line).</summary>
    public class GenealogySyncDto
    {
        public int OrdersSeen { get; set; }
        public int OrdersLinked { get; set; }
        public int LinksCreated { get; set; }
        public int LinksUpdated { get; set; }
        public int LinksRemoved { get; set; }
        public int OrdersWithoutKit { get; set; }
        public int OrdersReceiptNotConfirmed { get; set; }
        public int OrdersWithoutIssuedMaterial { get; set; }
        public int InputsWithoutTracking { get; set; }
        public int OutputsWithoutTracking { get; set; }
        public List<string> Notes { get; set; } = new List<string>();
    }

    public class GenealogyOrderDto
    {
        public int OrderId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ query

    public class TraceQuery
    {
        /// <summary>A product code, a part number (with or without the stage suffix), a serial, a lot or heat number, a label code (S/..., L/...), a purchase request or order number.</summary>
        public string Q { get; set; }

        /// <summary>Optional: product, part, serial, lot (which kind the text is). Empty = the service decides.</summary>
        public string Kind { get; set; }

        /// <summary>A project (work order) whose baseline is traced; 0 = the released bank BOM (as designed); empty = the only current baseline, else the choices are returned.</summary>
        public int? WorkOrderId { get; set; }

        public int? SerialUnitId { get; set; }
        public int? LotId { get; set; }

        /// <summary>Levels of the structure (default 6, at most 8).</summary>
        public int? Depth { get; set; }
    }

    public class ReverseTraceQuery
    {
        public string Q { get; set; }

        /// <summary>heat, lot, supplier, po, pr, mr, serial. Empty = the service tries them in this order.</summary>
        public string Kind { get; set; }

        public int? LotId { get; set; }
        public int? ProviderId { get; set; }

        /// <summary>Levels of where-used (default 10, at most 15).</summary>
        public int? Depth { get; set; }
    }

    // ------------------------------------------------------------------------------------------ product tree (forward)

    public class TraceFactDto
    {
        public string Label { get; set; }
        public string Value { get; set; }
        public TraceFactDto() { }
        public TraceFactDto(string label, string value) { Label = label; Value = value; }
    }

    public class TraceFileDto
    {
        public int Id { get; set; }
        public Guid DocumentId { get; set; }
        public string Title { get; set; }
        public string FileName { get; set; }
        public string KindTitle { get; set; }
        public string Revision { get; set; }
        public string OwnerTitle { get; set; }
        public string OwnerNumber { get; set; }

        /// <summary>Shown here because the file belongs to an upstream record (the purchase request) and follows the chain (P3).</summary>
        public bool Propagated { get; set; }

        public bool Legacy { get; set; }
    }

    /// <summary>A pointer the front turns into a route: type = serial, lot, ncr, inspection, document, order, mesOrder, request, dossier, item.</summary>
    public class TraceLinkDto
    {
        public string Type { get; set; }
        public int? Id { get; set; }
        public string Number { get; set; }
    }

    /// <summary>
    /// One node of the product tree. Kind: product, assembly, part, stage, order, operation, unit (a serial piece or a lot), material (an input lot), request, inquiry, purchase, receipt, inspection, ncr, delivery.
    /// Basis tells whether the node is as designed (BOM, OPC) or as built (serials, lots, documents that exist).
    /// </summary>
    public class TraceNodeDto
    {
        public string Key { get; set; }
        public string Kind { get; set; }
        public string Basis { get; set; }
        public string Title { get; set; }
        public string Code { get; set; }
        public string Status { get; set; }

        /// <summary>ok, warn, bad, info or empty: how the front colours the status.</summary>
        public string Tone { get; set; }

        public DateTime? Date { get; set; }
        public decimal? Quantity { get; set; }
        public string Unit { get; set; }
        public TraceLinkDto Link { get; set; }

        /// <summary>The number to open in the request dossier (purchase request, order, receipt, inspection, NCR); empty for the other kinds.</summary>
        public string DossierNumber { get; set; }

        public List<TraceFactDto> Facts { get; set; } = new List<TraceFactDto>();
        public List<TraceFileDto> Files { get; set; } = new List<TraceFileDto>();
        public List<TraceNodeDto> Children { get; set; } = new List<TraceNodeDto>();
        public bool Truncated { get; set; }
    }

    public class TraceChoiceDto
    {
        public string Kind { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Q { get; set; }
        public int? WorkOrderId { get; set; }
        public int? SerialUnitId { get; set; }
        public int? LotId { get; set; }
    }

    public class TraceTreeDto
    {
        public string Query { get; set; }

        /// <summary>product, part, serial, lot, or choices when the text names several things.</summary>
        public string Mode { get; set; }

        /// <summary>asDesigned (released bank BOM), project (the BOM copy of a project, as built where records exist), asBuilt (a serial or lot).</summary>
        public string Basis { get; set; }

        public TraceNodeDto Root { get; set; }
        public List<TraceChoiceDto> Choices { get; set; } = new List<TraceChoiceDto>();
        public List<string> Notes { get; set; } = new List<string>();

        /// <summary>The caller is not a price reader: unit prices and amounts are left out.</summary>
        public bool PricesMasked { get; set; }

        /// <summary>True when the names of suppliers and customers are replaced by «محرمانه» for this reader.</summary>
        public bool NamesMasked { get; set; }

        public bool Truncated { get; set; }
        public int NodeCount { get; set; }
        public int MaxDepth { get; set; }
        public int MaxParts { get; set; }
    }

    // ------------------------------------------------------------------------------------------ reverse trace (where used)

    public class ReverseRootDto
    {
        public string Key { get; set; }
        public string Kind { get; set; }
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public string CertificateNumber { get; set; }
        public string SerialNumber { get; set; }
        public string ProviderName { get; set; }
        public string ReceiptNumber { get; set; }
        public string PurchaseOrderNumber { get; set; }
        public string PurchaseRequestNumber { get; set; }
        public decimal? ReceivedQuantity { get; set; }
        public DateTime? ReceivedDate { get; set; }
    }

    public class ReverseNodeDto
    {
        public string Key { get; set; }
        public int Level { get; set; }
        public string Kind { get; set; }
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string PartNumber { get; set; }
        public string StageCode { get; set; }
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public string SerialNumber { get; set; }
        public string FromKey { get; set; }

        /// <summary>production (consumed by a production order), conversion (stage conversion), assembly (installed in an assembly), lotMember (a serial piece of the lot).</summary>
        public string Via { get; set; }

        public string OrderNumber { get; set; }
        public string DocumentNumber { get; set; }
        public decimal? Quantity { get; set; }
        public string WorkOrderCode { get; set; }
        public bool Terminal { get; set; }
    }

    public class ReverseItemDto
    {
        public int ItemId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string PartNumber { get; set; }
        public string StageCode { get; set; }
        public int NodeCount { get; set; }
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
    }

    public class ReverseProjectDto
    {
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public string WorkOrderTitle { get; set; }
        public string ContractCode { get; set; }
        public string ContractTitle { get; set; }
        public string Customer { get; set; }
        public int NodeCount { get; set; }
    }

    public class ReverseDeliveryDto
    {
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; }
        public DateTime DocumentDate { get; set; }
        public string NodeKey { get; set; }
        public string ItemCode { get; set; }
        public string SerialNumber { get; set; }
        public string LotNumber { get; set; }
        public decimal Quantity { get; set; }
        public string WorkOrderCode { get; set; }
        public string ContractCode { get; set; }
        public string Customer { get; set; }
        public string Destination { get; set; }
    }

    /// <summary>Material of the traced lots and serials that left the stock outside a production order kit: an issue to a project or activity, a subcontract issue or a return to the supplier.</summary>
    public class ReverseConsumptionDto
    {
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; }
        public DateTime DocumentDate { get; set; }
        /// <summary>issue, subcontract or supplierReturn</summary>
        public string Kind { get; set; }
        public string NodeKey { get; set; }
        public string ItemCode { get; set; }
        public string LotNumber { get; set; }
        public string SerialNumber { get; set; }
        public decimal Quantity { get; set; }
        public string WorkOrderCode { get; set; }
        public string ProviderName { get; set; }
    }

    /// <summary>A traced serial piece that is a delivered product in the after-sales installed base.</summary>
    public class ReverseInstalledDto
    {
        public string NodeKey { get; set; }
        public string SerialNumber { get; set; }
        public string Tag { get; set; }
        public string Title { get; set; }
        public string WorkOrderCode { get; set; }
        public string ContractCode { get; set; }
        public string Customer { get; set; }
        public DateTime? HandoverDate { get; set; }
        public bool Decommissioned { get; set; }
    }

    public class ReverseCountsDto
    {
        public int Consumptions { get; set; }
        public int Installed { get; set; }
        public int Roots { get; set; }
        public int Nodes { get; set; }
        public int Items { get; set; }
        public int Lots { get; set; }
        public int Serials { get; set; }
        public int Terminals { get; set; }
        public int Projects { get; set; }
        public int Deliveries { get; set; }
    }

    public class ReverseTraceDto
    {
        public string Query { get; set; }
        public string Mode { get; set; }
        public bool Truncated { get; set; }
        public int MaxDepth { get; set; }
        public bool NamesMasked { get; set; }
        public List<ReverseRootDto> Roots { get; set; } = new List<ReverseRootDto>();
        public List<ReverseNodeDto> Nodes { get; set; } = new List<ReverseNodeDto>();

        /// <summary>The distinct parts, stage items and assemblies that consumed or contain the root (the roots' own items are not listed).</summary>
        public List<ReverseItemDto> Items { get; set; } = new List<ReverseItemDto>();

        /// <summary>The end of every chain: the serial pieces or lots that nothing else consumed (finished products and spares).</summary>
        public List<ReverseNodeDto> Terminals { get; set; } = new List<ReverseNodeDto>();

        public List<ReverseProjectDto> Projects { get; set; } = new List<ReverseProjectDto>();
        public List<ReverseDeliveryDto> Deliveries { get; set; } = new List<ReverseDeliveryDto>();
        public List<ReverseConsumptionDto> Consumptions { get; set; } = new List<ReverseConsumptionDto>();
        public List<ReverseInstalledDto> Installed { get; set; } = new List<ReverseInstalledDto>();
        public ReverseCountsDto Counts { get; set; } = new ReverseCountsDto();
        public List<string> Notes { get; set; } = new List<string>();
    }
}
