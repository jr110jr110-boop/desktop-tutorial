using System.Collections.Generic;

namespace ViewModels.NasbNiro.CeoDtos
{
    // Release D1 part A2: the drill catalogue (the details behind every KPI and chart of the CEO dashboard) and the analysis request.
    // One service feeds the front panel and the AI tool (the D1 contract, drill catalogue and analysis).

    /// <summary>One parameter of a drill. Type is int, date, string or bool; dates are accepted as ISO (2026-10-25) or Shamsi (1405/08/03).</summary>
    public class DrillParameterDto
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool Required { get; set; }

        /// <summary>Persian text; it also says what the default is when the parameter is left out.</summary>
        public string Description { get; set; }
    }

    /// <summary>A drill of the catalogue.</summary>
    public class DrillDescriptorDto
    {
        /// <summary>For example qc.ncr_open.</summary>
        public string Key { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }

        /// <summary>finance, sales, projects, production, quality, inventory, procurement, service, office or ai.</summary>
        public string Module { get; set; }
        public List<DrillParameterDto> Parameters { get; set; } = new List<DrillParameterDto>();
        public string Roles { get; set; }
    }

    public class DrillColumnDto
    {
        public string Key { get; set; }
        public string Title { get; set; }

        /// <summary>text, number, rial, percent, date (Shamsi text), link (a front-end route) or status.</summary>
        public string Kind { get; set; }
    }

    /// <summary>The rows of one drill. The keys of a row are the keys of the columns; a column of kind date holds Shamsi text, a column of kind link a front-end route.</summary>
    public class DrillTableDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string AsOfText { get; set; }
        public List<DrillColumnDto> Columns { get; set; } = new List<DrillColumnDto>();
        public List<Dictionary<string, object>> Rows { get; set; } = new List<Dictionary<string, object>>();

        /// <summary>All rows behind the drill, not only this page.</summary>
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }

        /// <summary>The front-end page that lists the same records.</summary>
        public string Link { get; set; }
        public string Note { get; set; }

        /// <summary>True when the caller's role may not see the amounts (payroll privacy): no rows are returned.</summary>
        public bool Hidden { get; set; }

        /// <summary>Additive: sums of the whole drill (not only the page) for the columns where a sum means something.</summary>
        public Dictionary<string, object> Totals { get; set; }
    }

    /// <summary>POST api/v1/ceo/analyze. SectionKey is one of the nine sections or empty (the whole company); an empty question means the standard analysis of the section.</summary>
    public class CeoAnalyzeDto
    {
        public string SectionKey { get; set; }
        public string Question { get; set; }
        public int? ConversationId { get; set; }
    }
}
