using System;
using System.Collections.Generic;
using System.Linq;

namespace Services.NasbNiro.EngInbox
{
    // Engineering document package (ENG-F1). One workbook per package (sheets: مشخصات, BOM, OPC, مدارک, optional MDL) plus the referenced document files in the same folder.
    // The parser and the validator are pure: no database, no clock, no network. The workbook is read with the in-house XlsxReader (formulas are never evaluated).

    public enum EngFindingSeverity { Error = 1, Warning = 2 }

    /// <summary>One finding of the validation: the sheet, the row of the workbook (1-based, 0 when it is about the sheet or the package) and a Persian message.</summary>
    public sealed class EngFinding
    {
        public EngFindingSeverity Severity { get; set; }
        public string Sheet { get; set; }
        public int Row { get; set; }
        public string Message { get; set; }

        public bool IsError => Severity == EngFindingSeverity.Error;

        public string Describe() => (Severity == EngFindingSeverity.Error ? "خطا" : "هشدار") + " | " + (string.IsNullOrEmpty(Sheet) ? "بسته" : Sheet) + (Row > 0 ? " | سطر " + Row.ToString(System.Globalization.CultureInfo.InvariantCulture) : "") + " | " + Message;
    }

    public enum EngPackageKind { Platform = 1, Project = 2 }

    public sealed class EngFileInfo
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
    }

    public sealed class EngInfo
    {
        public EngPackageKind? Kind { get; set; }
        public string PlatformCode { get; set; }
        public string Standard { get; set; }
        public int PackageVersion { get; set; }
        public string ContractCode { get; set; }
        public string ProjectCode { get; set; }
        public string Title { get; set; }
        public string PreparedBy { get; set; }
        public DateTime? Date { get; set; }
        public int SchemaVersion { get; set; }
    }

    public enum EngMakeBuy { Make = 1, Purchase = 2, CustomerSupplied = 3, Subcontract = 4 }

    public sealed class EngBomRow
    {
        public int SourceRow { get; set; }
        public int Level { get; set; }
        public string ParentCode { get; set; }
        public string PartCode { get; set; }
        public string Description { get; set; }
        public decimal Qty { get; set; }
        public string Unit { get; set; }
        public EngMakeBuy MakeBuy { get; set; }
        public string Material { get; set; }
        public string DrawingNumber { get; set; }
        public string DrawingRevision { get; set; }
        public string Note { get; set; }
    }

    public sealed class EngOpcRow
    {
        public int SourceRow { get; set; }
        public string PartCode { get; set; }
        public int OperationNumber { get; set; }
        public string Description { get; set; }
        public string WorkCenterCode { get; set; }
        public decimal SetupMinutes { get; set; }
        public decimal RunMinutes { get; set; }
        public bool IsInspection { get; set; }
        public bool IsSpecialProcess { get; set; }

        /// <summary>Operation numbers of the same part that must be finished first.</summary>
        public List<int> Predecessors { get; set; } = new List<int>();

        /// <summary>Optional two-letter stage code of the product bank (RG, MC, QC ...); empty = derived when applied.</summary>
        public string StageCode { get; set; }
    }

    public sealed class EngDocRow
    {
        public int SourceRow { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Revision { get; set; }
        public string DocType { get; set; }
        public string FileName { get; set; }
        public bool Required { get; set; }
        public long? FileSize { get; set; }
        public string FileSha256 { get; set; }
    }

    public sealed class EngMdlRow
    {
        public int SourceRow { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string DocType { get; set; }
        public DateTime? CommitDate { get; set; }
    }

    /// <summary>What the parser and the validator made of one package. <see cref="ContentHash"/> is empty while there is an error that stopped the parse of the structure.</summary>
    public sealed class EngParsedPackage
    {
        public EngInfo Info { get; set; } = new EngInfo();
        public List<EngBomRow> Bom { get; set; } = new List<EngBomRow>();
        public List<EngOpcRow> Opc { get; set; } = new List<EngOpcRow>();
        public List<EngDocRow> Docs { get; set; } = new List<EngDocRow>();
        public List<EngMdlRow> Mdl { get; set; } = new List<EngMdlRow>();
        public List<EngFinding> Findings { get; set; } = new List<EngFinding>();
        public string ContentHash { get; set; }

        public int ErrorCount => Findings.Count(f => f.IsError);
        public int WarningCount => Findings.Count(f => !f.IsError);
        public bool HasErrors => ErrorCount > 0;

        internal void Error(string sheet, int row, string message) => Findings.Add(new EngFinding { Severity = EngFindingSeverity.Error, Sheet = sheet, Row = row, Message = message });
        internal void Warn(string sheet, int row, string message) => Findings.Add(new EngFinding { Severity = EngFindingSeverity.Warning, Sheet = sheet, Row = row, Message = message });
    }

    /// <summary>The limits of a package (a zip-bomb and runaway guard).</summary>
    public static class EngPackageLimits
    {
        public const long MaxWorkbookBytes = 20L * 1024 * 1024;
        public const int MaxBomRows = 20000;
        public const int MaxOpcRows = 50000;
        public const int MaxDocRows = 5000;
        public const int MaxZipEntries = 2000;
        public const long MaxUncompressedBytes = 200L * 1024 * 1024;
        public const int SupportedSchemaVersion = 1;
    }
}
