using System.Collections.Generic;

namespace ViewModels.NasbNiro.PredecessorCheckDtos
{
    /// <summary>One finding of the read-only check of the product bank (assembly or BOM predecessors, BOM without processes).</summary>
    public class PredecessorCheckRowDto
    {
        public int EquipmentId { get; set; }
        public string EquipmentCode { get; set; }
        public string EquipmentTitle { get; set; }

        /// <summary>Assembly or Bom.</summary>
        public string ItemKind { get; set; }
        public int ItemId { get; set; }
        public string ItemTitle { get; set; }
        public int? PredecessorId { get; set; }

        /// <summary>Deleted, OtherProduct, Missing, Cycle or NoProcesses.</summary>
        public string Problem { get; set; }

        /// <summary>The Persian reason shown to the PMO.</summary>
        public string Reason { get; set; }

        /// <summary>The Persian proposal. It is only a proposal: a second person approves any correction (maker-checker).</summary>
        public string ProposedFix { get; set; }
    }

    public class PredecessorCheckReportDto
    {
        public int ProductsChecked { get; set; }
        public int ProductsWithFindings { get; set; }
        public int FindingCount { get; set; }
        public string Note { get; set; }
        public List<PredecessorCheckRowDto> Rows { get; set; } = new List<PredecessorCheckRowDto>();
    }
}
