using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;

namespace Services.NasbNiro.Quality
{
    /// <summary>
    /// The system drafts the NCR when an inspection or a test fails; people classify it, decide and approve it
    /// through the existing NCR flow of the inventory module (PR-QC-002).
    /// </summary>
    internal static class QualityNcrFactory
    {
        public static async Task<NonConformance> DraftAsync(
            IUnitOfWork uow, int userId, NcrSource source, int itemId, int? lotId, decimal quantity, string serial, int? workOrderId,
            string requirement, string description, string sourceRecordNumber, int? equipmentId, CancellationToken cancellationToken)
        {
            int? contractId = workOrderId.HasValue
                ? await uow.Set<WorkOrder>().Where(w => w.Id == workOrderId.Value).Select(w => (int?)w.ContractId).FirstOrDefaultAsync(cancellationToken)
                : null;
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(uow, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            var ncr = new NonConformance
            {
                Number = await InventoryNumbers.NextNumberAsync(uow, InventoryNumbers.Ncr, "NCR", DateTime.Today, cancellationToken),
                ReportDate = DateTime.Today,
                Source = source,
                Status = NcrStatus.Open,
                Severity = NcrSeverity.NotClassified,
                ItemId = itemId,
                LotId = lotId,
                Quantity = quantity > 0 ? quantity : 1,
                SerialNumbers = QualityText.Clean(serial),
                WorkOrderId = workOrderId,
                ContractId = contractId,
                Requirement = QualityText.Clean(requirement),
                Description = QualityText.Clean(description),
                SourceRecordNumber = sourceRecordNumber,
                AffectedEquipmentId = equipmentId,
                DueDate = DateTime.Today.AddDays((int)settings[QualitySettingKeys.NcrMinorDays]),
                CreateDate = now, ModifyDate = now, CreatorUserId = userId, ModifierUserId = userId,
            };
            uow.Set<NonConformance>().Add(ncr);
            return ncr;
        }
    }
}
