using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;

namespace ViewModels.NasbNiro.FinanceDtos
{
    // Finance release F3, section 1b: full voucher export for Sepidar (fallback path; the F2 outbox stays the default).

    public class ExportLayoutColumnDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class SaveExportLayoutDto
    {
        public List<ExportLayoutColumnDto> Columns { get; set; } = new List<ExportLayoutColumnDto>();
    }

    public class ExportMappingRowDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string SepidarCode { get; set; }
        public bool Mapped { get; set; }
    }

    public class SaveExportMappingDto
    {
        public string Code { get; set; }
        /// <summary>Empty clears the mapping.</summary>
        public string SepidarCode { get; set; }
    }

    public class ExportPeriodDto
    {
        public int PeriodId { get; set; }
        /// <summary>Required when the period already has an active export: the old export is superseded, never deleted.</summary>
        public string SupersedeReason { get; set; }
    }

    public class ExportPreviewDto
    {
        public bool Enabled { get; set; }
        public int PeriodId { get; set; }
        public int Vouchers { get; set; }
        public int Rows { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public bool AlreadyExported { get; set; }
        public List<string> UnmappedAccounts { get; set; } = new List<string>();
        public List<string> UnmappedDetails { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class VoucherExportDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int LegalEntityId { get; set; }
        public int PeriodId { get; set; }
        public string PeriodTitle { get; set; }
        public FnExportStatus Status { get; set; }
        public int Vouchers { get; set; }
        public int Rows { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public string Sha256 { get; set; }
        public int ExportedByUserId { get; set; }
        public string ExportedByName { get; set; }
        public DateTimeOffset ExportedAt { get; set; }
        public int? SupersededByExportId { get; set; }
        public string SupersedeReason { get; set; }
    }
}
