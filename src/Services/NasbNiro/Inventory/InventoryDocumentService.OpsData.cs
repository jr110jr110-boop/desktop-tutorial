using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Inventory;
using Services.NasbNiro.OpsData;
using ViewModels.NasbNiro.InventoryDtos;

namespace Services.NasbNiro.Inventory
{
    // PB8: the real-time gate of operational data on warehouse documents.
    public partial class InventoryDocumentService
    {
        /// <summary>Runs the rules on the document as it is now and stores the findings; the Persian refusal when a blocking finding has no approved exception, otherwise null.</summary>
        private async Task<string> OpsGateRefusalAsync(int documentId, CancellationToken ct)
        {
            Dictionary<int, OpsEvaluation> all = await _opsEvaluator.EvaluateInventoryAsync(new[] { documentId }, DateTime.Today, ct);
            if (!all.TryGetValue(documentId, out OpsEvaluation ev)) return null;
            OpsGateResult result = await _opsGate.CheckAsync(ev, ct);
            return result.Refusal;
        }

        /// <summary>The findings on the form: live ones while the document is a draft (the user sees them as he types and saves), the stored ones afterwards.</summary>
        private async Task EnrichOpsAsync(DocumentDto document, CancellationToken ct)
        {
            try
            {
                if (document.Status == InventoryDocumentStatus.Draft)
                {
                    Dictionary<int, OpsEvaluation> all = await _opsEvaluator.EvaluateInventoryAsync(new[] { document.Id }, DateTime.Today, ct);
                    if (all.TryGetValue(document.Id, out OpsEvaluation ev))
                    {
                        document.OpsFindings = await _opsGate.PreviewAsync(ev, ct);
                        document.OpsBlocked = document.OpsFindings.Any(f => f.Severity == Entities.Model.NasbNiro.OpsData.OpsSeverity.Block && f.ExceptionStatus != Entities.Model.NasbNiro.OpsData.OpsExceptionStatus.Approved);
                    }
                }
                else document.OpsFindings = await _opsGate.StoredAsync(OpsDocTypes.InventoryDocument, document.Id, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                // the findings are information: a failure of the rules must never stop the document from opening
                document.OpsFindings = new List<ViewModels.NasbNiro.OpsDataDtos.OpsFindingDto>();
            }
        }
    }
}
