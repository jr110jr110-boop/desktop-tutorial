using System;
using System.Collections.Generic;
using System.Linq;
using ViewModels.NasbNiro.CeoDtos;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>
    /// The drill catalogue: every detail table behind a KPI or a chart of the CEO dashboard. The keys are fixed by the D1 contract
    /// (module prefix, a dot and the name: fin., sl., pc., mf., qc., inv., pr., sv., of., ai.). Built once; the definitions hold no state.
    /// </summary>
    public static class CeoDrillCatalog
    {
        private static readonly Lazy<IReadOnlyList<DrillDefinition>> Lazy = new Lazy<IReadOnlyList<DrillDefinition>>(Build);

        public static IReadOnlyList<DrillDefinition> All => Lazy.Value;

        public static DrillDefinition Find(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            string k = key.Trim();
            return All.FirstOrDefault(d => string.Equals(d.Key, k, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<DrillDefinition> Build()
        {
            var list = new List<DrillDefinition>();
            list.AddRange(FinanceDrills.Build());
            list.AddRange(SalesDrills.Build());
            list.AddRange(ProjectDrills.Build());
            list.AddRange(ProductionDrills.Build());
            list.AddRange(QualityDrills.Build());
            list.AddRange(InventoryDrills.Build());
            list.AddRange(ProcurementDrills.Build());
            list.AddRange(ServiceDrills.Build());
            list.AddRange(OfficeDrills.Build());
            list.AddRange(AiDrills.Build());
            return list;
        }

        public static DrillDescriptorDto Describe(DrillDefinition d) => new DrillDescriptorDto
        {
            Key = d.Key,
            Title = d.Title,
            Description = d.Description,
            Module = d.Module,
            Roles = CeoDrillRoles.Readers,
            Parameters = d.Parameters.Select(p => new DrillParameterDto { Name = p.Name, Type = p.Type, Required = p.Required, Description = p.Description }).ToList(),
        };

        /// <summary>The catalogue as the AI tool shows it: one short text per module (key, title and the names of the parameters), so that all drills fit into one tool result.</summary>
        public static Dictionary<string, object> Compact()
        {
            var result = new Dictionary<string, object> { ["totalCount"] = All.Count };
            foreach (string module in CeoModules.All)
            {
                List<DrillDefinition> drills = All.Where(d => d.Module == module).ToList();
                if (drills.Count == 0) continue;
                result[module] = string.Join("؛ ", drills.Select(d =>
                    d.Key + (d.Parameters.Count == 0 ? string.Empty : "(" + string.Join(",", d.Parameters.Select(p => p.Name)) + ")") + ": " + d.Title));
            }
            return result;
        }
    }
}
