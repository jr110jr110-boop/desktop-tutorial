using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Inventory
{
    public interface IInventoryQualityService
    {
        Task<ResponseDto<PagedList<InspectionDto>>> GetInspectionsAsync(InspectionQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<InspectionDto>> GetInspectionAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<InspectionDto>> RecordInspectionAsync(RecordInspectionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<NcrListItemDto>>> GetNcrsAsync(NcrQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<NcrDto>> GetNcrAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<NcrDto>> SaveNcrAsync(SaveNcrDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<NcrDto>> ApproveNcrAsync(ApproveNcrDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<NcrDto>> ExecuteNcrAsync(ExecuteNcrDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<NcrDto>> CloseNcrAsync(CloseNcrDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<SupplierReturnReportDto>> GetSupplierReturnReportAsync(int id, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Incoming inspection of goods in quarantine and non-conformance reports (PR-QC-002, FO-QC-013, ISO 9001 8.6 / 8.7).
    /// Every stock movement the quality decisions cause is a normal warehouse document linked to the inspection or NCR.
    /// </summary>
    public class InventoryQualityService : IInventoryQualityService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IInventoryDocumentService _documents;

        public InventoryQualityService(IUnitOfWork uow, IInventoryUserContext user, IInventoryDocumentService documents)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        }

        private DbSet<QcInspection> Inspections => _uow.Set<QcInspection>();
        private DbSet<NonConformance> Ncrs => _uow.Set<NonConformance>();

        private static List<string> Lines(string text) =>
            string.IsNullOrWhiteSpace(text)
                ? new List<string>()
                : text.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        private static string Join(IEnumerable<string> values)
        {
            List<string> list = (values ?? Enumerable.Empty<string>()).Select(InventoryText.Clean).Where(v => v != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return list.Count == 0 ? null : string.Join("\n", list);
        }

        // ---------------------------------------------------------------------------- inspections

        private IQueryable<InspectionDto> InspectionProjection(IQueryable<QcInspection> source) => source.Select(q => new InspectionDto
        {
            Id = q.Id,
            Number = q.Number,
            ReceiptDocumentId = q.ReceiptDocumentId,
            ReceiptNumber = q.ReceiptDocument.Number,
            ReceiptType = q.ReceiptDocument.Type,
            ReceiptDate = q.ReceiptDocument.DocumentDate,
            ProviderName = q.ReceiptDocument.Provider.Name,
            ItemId = q.ItemId,
            ItemCode = q.Item.Code,
            ItemTitle = q.Item.Title,
            Unit = q.Item.Unit,
            TrackingMode = q.Item.TrackingMode,
            LotId = q.LotId,
            LotNumber = q.Lot.LotNumber,
            HeatNumber = q.Lot.HeatNumber,
            WarehouseId = q.WarehouseId,
            WarehouseTitle = q.Warehouse.Title,
            Quantity = q.Quantity,
            TargetWarehouseId = q.TargetWarehouseId,
            TargetWarehouseTitle = q.TargetWarehouse.Title,
            Status = q.Status,
            AcceptedQuantity = q.AcceptedQuantity,
            RejectedQuantity = q.RejectedQuantity,
            ReportNumber = q.ReportNumber,
            ReportDocumentId = q.ReportDocumentId,
            Note = q.Note,
            InspectedAt = q.InspectedAt,
            InspectedByUserId = q.InspectedByUserId,
            ReleaseDocumentId = q.ReleaseDocumentId,
            NcrId = q.NcrId,
            // serial numbers are split after loading
            ReleaseDocumentNumber = q.SerialNumbers,
            NcrNumber = q.RejectedSerialNumbers,
        });

        private async Task<List<InspectionDto>> FinishInspectionsAsync(List<InspectionDto> rows, CancellationToken cancellationToken)
        {
            List<int> releaseIds = rows.Where(r => r.ReleaseDocumentId.HasValue).Select(r => r.ReleaseDocumentId.Value).ToList();
            List<int> ncrIds = rows.Where(r => r.NcrId.HasValue).Select(r => r.NcrId.Value).ToList();
            Dictionary<int, string> releases = await _uow.Set<InventoryDocument>().Where(d => releaseIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number, cancellationToken);
            Dictionary<int, string> ncrs = await Ncrs.Where(n => ncrIds.Contains(n.Id)).ToDictionaryAsync(n => n.Id, n => n.Number, cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => r.InspectedByUserId), cancellationToken);
            foreach (InspectionDto row in rows)
            {
                row.SerialNumbers = Lines(row.ReleaseDocumentNumber);
                row.RejectedSerialNumbers = Lines(row.NcrNumber);
                row.ReleaseDocumentNumber = row.ReleaseDocumentId.HasValue && releases.TryGetValue(row.ReleaseDocumentId.Value, out string r) ? r : null;
                row.NcrNumber = row.NcrId.HasValue && ncrs.TryGetValue(row.NcrId.Value, out string n) ? n : null;
                row.InspectedByName = names.Name(row.InspectedByUserId);
            }
            return rows;
        }

        public async Task<ResponseDto<PagedList<InspectionDto>>> GetInspectionsAsync(InspectionQuery query, CancellationToken cancellationToken)
        {
            query ??= new InspectionQuery();
            IQueryable<QcInspection> inspections = Inspections.Where(q => !q.Deleted);
            if (query.Status.HasValue) inspections = inspections.Where(q => q.Status == query.Status.Value);
            string text = InventoryText.Clean(query.Contains);
            if (text != null)
                inspections = inspections.Where(q => q.Number.Contains(text) || q.Item.Code.Contains(text) || q.Item.Title.Contains(text) ||
                                                     q.ReceiptDocument.Number.Contains(text) || q.SerialNumbers.Contains(text) || q.Lot.LotNumber.Contains(text));
            PagedList<InspectionDto> page = await PagedList<InspectionDto>.CreateAsync(
                InspectionProjection(inspections.OrderBy(q => q.Status).ThenByDescending(q => q.Id)), query.PageNumber, query.PageSize, cancellationToken);
            await FinishInspectionsAsync(page.ToList(), cancellationToken);
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<InspectionDto>> GetInspectionAsync(int id, CancellationToken cancellationToken)
        {
            List<InspectionDto> rows = await InspectionProjection(Inspections.Where(q => q.Id == id && !q.Deleted)).ToListAsync(cancellationToken);
            if (rows.Count == 0) return ResponseDtoHelper.NotFound<InspectionDto>(InventoryMessages.NotFound);
            return ResponseDtoHelper.Ok((await FinishInspectionsAsync(rows, cancellationToken))[0]);
        }

        public async Task<ResponseDto<InspectionDto>> RecordInspectionAsync(RecordInspectionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<InspectionDto>(InventoryMessages.InvalidData);
            QcInspection inspection = await Inspections.Include(q => q.ReceiptDocument)
                .FirstOrDefaultAsync(q => q.Id == dto.Id && !q.Deleted, cancellationToken);
            if (inspection == null) return ResponseDtoHelper.NotFound<InspectionDto>(InventoryMessages.NotFound);
            if (inspection.Status != InspectionStatus.Pending) return ResponseDtoHelper.BadRequest<InspectionDto>("نتیجه این بازرسی قبلاً ثبت شده است.");
            InventoryItem item = await _uow.Set<InventoryItem>().FirstAsync(i => i.Id == inspection.ItemId, cancellationToken);

            if (dto.AcceptedQuantity < 0 || dto.RejectedQuantity < 0) return ResponseDtoHelper.BadRequest<InspectionDto>("مقدار قبول یا رد نمی‌تواند منفی باشد.");
            if (dto.AcceptedQuantity + dto.RejectedQuantity != inspection.Quantity)
                return ResponseDtoHelper.BadRequest<InspectionDto>($"جمع مقدار قبول و رد باید برابر مقدار رسید ({InventoryText.Number(inspection.Quantity)}) باشد.");

            List<string> serials = Lines(inspection.SerialNumbers);
            List<string> rejectedSerials = (dto.RejectedSerialNumbers ?? new List<string>()).Select(InventoryText.Clean).Where(s => s != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<string> acceptedSerials = new List<string>();
            if (item.TrackingMode == ItemTrackingMode.Serial)
            {
                if (!InventoryText.IsWhole(dto.RejectedQuantity)) return ResponseDtoHelper.BadRequest<InspectionDto>("کالای سریالی با عدد صحیح قبول یا رد می‌شود.");
                if (rejectedSerials.Count != (int)dto.RejectedQuantity) return ResponseDtoHelper.BadRequest<InspectionDto>("سریال قطعات مردود را مشخص کنید (تعداد سریال‌ها برابر مقدار رد).");
                string unknown = rejectedSerials.FirstOrDefault(s => !serials.Contains(s, StringComparer.OrdinalIgnoreCase));
                if (unknown != null) return ResponseDtoHelper.BadRequest<InspectionDto>($"سریال {unknown} در این رسید نیست.");
                acceptedSerials = serials.Where(s => !rejectedSerials.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
            }
            else
            {
                rejectedSerials.Clear();
            }

            string rejectReason = InventoryText.Clean(dto.RejectReason);
            if (dto.RejectedQuantity > 0 && rejectReason == null) return ResponseDtoHelper.BadRequest<InspectionDto>("شرح عدم انطباق کالای مردود را بنویسید.");

            int? targetId = dto.TargetWarehouseId ?? inspection.TargetWarehouseId;
            if (dto.AcceptedQuantity > 0)
            {
                Warehouse target = targetId.HasValue
                    ? await _uow.Set<Warehouse>().FirstOrDefaultAsync(w => w.Id == targetId.Value && !w.Deleted && w.IsActive, cancellationToken)
                    : null;
                if (target == null) return ResponseDtoHelper.BadRequest<InspectionDto>("انبار مقصد کالای قبول‌شده را انتخاب کنید.");
                if (target.Kind == WarehouseKind.Quarantine || target.Kind == WarehouseKind.Scrap || target.Kind == WarehouseKind.Subcontractor)
                    return ResponseDtoHelper.BadRequest<InspectionDto>("کالای قبول‌شده به انبار قرنطینه، ضایعات یا پیمانکار ترخیص نمی‌شود.");
            }

            InventoryDocument receipt = inspection.ReceiptDocument;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.AcceptedQuantity > 0)
            {
                ResponseDto<DocumentDto> release = await _documents.CreateSystemDocumentAsync(new SaveDocumentDto
                {
                    Type = InventoryDocumentType.QcRelease,
                    DocumentDate = DateTime.Today,
                    WarehouseId = inspection.WarehouseId,
                    DestinationWarehouseId = targetId,
                    ProviderId = receipt.ProviderId,
                    BuyOrderId = receipt.BuyOrderId,
                    WorkOrderId = receipt.WorkOrderId,
                    ContractId = receipt.ContractId,
                    ReferenceNumber = inspection.Number,
                    Description = $"ترخیص از قرنطینه پس از بازرسی {inspection.Number} — رسید {receipt.Number}",
                    Lines = new List<SaveDocumentLineDto>
                    {
                        new SaveDocumentLineDto
                        {
                            ItemId = inspection.ItemId,
                            Quantity = dto.AcceptedQuantity,
                            LotId = inspection.LotId,
                            LocationId = inspection.LocationId,
                            DestinationLocationId = dto.TargetLocationId,
                            SerialNumbers = acceptedSerials,
                        },
                    },
                }, new SystemDocumentLinks { InspectionId = inspection.Id }, true, cancellationToken);
                if (!release.Success)
                {
                    await DeleteLeftDraftAsync(inspection.Id, cancellationToken);
                    return ResponseDtoHelper.BadRequest<InspectionDto>("ترخیص انجام نشد: " + release.Message);
                }
                inspection.ReleaseDocumentId = release.Data.Id;
            }

            if (dto.RejectedQuantity > 0)
            {
                var ncr = new NonConformance
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, InventoryNumbers.Ncr, "NCR", DateTime.Today, cancellationToken),
                    ReportDate = DateTime.Today,
                    Source = receipt.Type == InventoryDocumentType.SubcontractReturn ? NcrSource.Subcontractor : NcrSource.Receiving,
                    Status = NcrStatus.Open,
                    ItemId = inspection.ItemId,
                    LotId = inspection.LotId,
                    Quantity = dto.RejectedQuantity,
                    SerialNumbers = Join(rejectedSerials),
                    WarehouseId = inspection.WarehouseId,
                    LocationId = inspection.LocationId,
                    ProviderId = receipt.ProviderId,
                    WorkOrderId = receipt.WorkOrderId,
                    ContractId = receipt.ContractId,
                    InspectionId = inspection.Id,
                    ReceiptDocumentId = receipt.Id,
                    OwnedByContractor = receipt.Type == InventoryDocumentType.CustomerPropertyReceipt,
                    Description = rejectReason,
                    CreateDate = now,
                    CreatorUserId = _user.UserId,
                };
                Ncrs.Add(ncr);
                await _uow.SaveChangesAsync(cancellationToken);
                inspection.NcrId = ncr.Id;
            }

            inspection.Status = InspectionStatus.Done;
            inspection.AcceptedQuantity = dto.AcceptedQuantity;
            inspection.RejectedQuantity = dto.RejectedQuantity;
            inspection.RejectedSerialNumbers = Join(rejectedSerials);
            inspection.TargetWarehouseId = targetId;
            inspection.TargetLocationId = dto.TargetLocationId;
            inspection.ReportNumber = InventoryText.Clean(dto.ReportNumber);
            inspection.ReportDocumentId = dto.ReportDocumentId;
            inspection.Note = InventoryText.Clean(dto.Note);
            inspection.InspectedAt = now;
            inspection.InspectedByUserId = _user.UserId;
            inspection.ModifyDate = now;
            inspection.ModifierUserId = _user.UserId;
            try
            {
                await _uow.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ResponseDtoHelper.BadRequest<InspectionDto>(InventoryMessages.ConcurrencyConflict);
            }
            return await GetInspectionAsync(inspection.Id, cancellationToken);
        }

        /// <summary>A system document that could not be confirmed is not left behind as a draft.</summary>
        private async Task DeleteLeftDraftAsync(int inspectionId, CancellationToken cancellationToken)
        {
            List<int> drafts = await _uow.Set<InventoryDocument>()
                .Where(d => d.InspectionId == inspectionId && d.Status == InventoryDocumentStatus.Draft && !d.Deleted)
                .Select(d => d.Id).ToListAsync(cancellationToken);
            foreach (int id in drafts) await _documents.DeleteDraftAsync(id, cancellationToken);
        }

        // ------------------------------------------------------------------------------------ NCR

        private IQueryable<NcrDto> NcrProjection(IQueryable<NonConformance> source) => source.Select(n => new NcrDto
        {
            Id = n.Id,
            Number = n.Number,
            ReportDate = n.ReportDate,
            Source = n.Source,
            Status = n.Status,
            Severity = n.Severity,
            Disposition = n.Disposition,
            ItemId = n.ItemId,
            ItemCode = n.Item.Code,
            ItemTitle = n.Item.Title,
            Unit = n.Item.Unit,
            Quantity = n.Quantity,
            LotId = n.LotId,
            LotNumber = n.Lot.LotNumber,
            HeatNumber = n.Lot.HeatNumber,
            WarehouseId = n.WarehouseId,
            WarehouseTitle = n.Warehouse.Title,
            ProviderId = n.ProviderId,
            ProviderName = n.Provider.Name,
            WorkOrderId = n.WorkOrderId,
            WorkOrderCode = n.WorkOrder.Code,
            ContractId = n.ContractId,
            ContractCode = n.Contract.Code,
            InspectionId = n.InspectionId,
            ReceiptDocumentId = n.ReceiptDocumentId,
            OwnedByContractor = n.OwnedByContractor,
            Requirement = n.Requirement,
            Description = n.Description,
            CauseCategory = n.CauseCategory,
            RootCause = n.RootCause,
            DispositionNote = n.DispositionNote,
            CommitteeMinutesNumber = n.CommitteeMinutesNumber,
            CommitteeMembers = n.CommitteeMembers,
            QualityApprovedAt = n.QualityApprovedAt,
            EngineeringApprovedAt = n.EngineeringApprovedAt,
            ManufacturingApprovedAt = n.ManufacturingApprovedAt,
            ConcessionNumber = n.ConcessionNumber,
            ConcessionApprovedAt = n.ConcessionApprovedAt,
            CustomerApprovalRequired = n.CustomerApprovalRequired,
            CustomerApprovalReference = n.CustomerApprovalReference,
            TargetWarehouseId = n.TargetWarehouseId,
            ResultDocumentId = n.ResultDocumentId,
            ExecutedAt = n.ExecutedAt,
            CorrectiveAction = n.CorrectiveAction,
            CorrectiveActionOwner = n.CorrectiveActionOwner,
            CorrectiveActionDueDate = n.CorrectiveActionDueDate,
            EffectivenessCheck = n.EffectivenessCheck,
            ClosedAt = n.ClosedAt,
            // filled after loading
            ReceiptNumber = n.SerialNumbers,
        });

        public async Task<ResponseDto<PagedList<NcrListItemDto>>> GetNcrsAsync(NcrQuery query, CancellationToken cancellationToken)
        {
            query ??= new NcrQuery();
            IQueryable<NonConformance> ncrs = Ncrs.Where(n => !n.Deleted);
            if (query.Status.HasValue) ncrs = ncrs.Where(n => n.Status == query.Status.Value);
            if (query.ItemId.HasValue) ncrs = ncrs.Where(n => n.ItemId == query.ItemId.Value);
            string text = InventoryText.Clean(query.Contains);
            if (text != null)
                ncrs = ncrs.Where(n => n.Number.Contains(text) || n.Item.Code.Contains(text) || n.Item.Title.Contains(text) ||
                                       n.SerialNumbers.Contains(text) || n.Description.Contains(text) || n.Provider.Name.Contains(text));
            IQueryable<NcrListItemDto> rows = ncrs.OrderByDescending(n => n.ReportDate).ThenByDescending(n => n.Id).Select(n => new NcrListItemDto
            {
                Id = n.Id, Number = n.Number, ReportDate = n.ReportDate, Source = n.Source, Status = n.Status, Severity = n.Severity,
                Disposition = n.Disposition, ItemCode = n.Item.Code, ItemTitle = n.Item.Title, Quantity = n.Quantity,
                ProviderName = n.Provider.Name, WorkOrderCode = n.WorkOrder.Code,
            });
            return ResponseDtoHelper.Ok(await PagedList<NcrListItemDto>.CreateAsync(rows, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<NcrDto>> GetNcrAsync(int id, CancellationToken cancellationToken)
        {
            NcrDto ncr = await NcrProjection(Ncrs.Where(n => n.Id == id && !n.Deleted)).FirstOrDefaultAsync(cancellationToken);
            if (ncr == null) return ResponseDtoHelper.NotFound<NcrDto>("گزارش عدم انطباق پیدا نشد.");
            ncr.SerialNumbers = Lines(ncr.ReceiptNumber);
            ncr.ReceiptNumber = ncr.ReceiptDocumentId.HasValue
                ? await _uow.Set<InventoryDocument>().Where(d => d.Id == ncr.ReceiptDocumentId.Value).Select(d => d.Number).FirstOrDefaultAsync(cancellationToken)
                : null;
            ncr.ResultDocumentNumber = ncr.ResultDocumentId.HasValue
                ? await _uow.Set<InventoryDocument>().Where(d => d.Id == ncr.ResultDocumentId.Value).Select(d => d.Number).FirstOrDefaultAsync(cancellationToken)
                : null;
            var raw = await Ncrs.Where(n => n.Id == id).Select(n => new
            {
                n.CreatorUserId, n.QualityApprovedByUserId, n.EngineeringApprovedByUserId, n.ManufacturingApprovedByUserId,
                n.ConcessionApprovedByUserId, n.ClosedByUserId,
            }).FirstAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[]
            {
                raw.CreatorUserId, raw.QualityApprovedByUserId, raw.EngineeringApprovedByUserId, raw.ManufacturingApprovedByUserId,
                raw.ConcessionApprovedByUserId, raw.ClosedByUserId,
            }, cancellationToken);
            ncr.CreatorName = names.Name(raw.CreatorUserId);
            ncr.QualityApprovedByName = names.Name(raw.QualityApprovedByUserId);
            ncr.EngineeringApprovedByName = names.Name(raw.EngineeringApprovedByUserId);
            ncr.ManufacturingApprovedByName = names.Name(raw.ManufacturingApprovedByUserId);
            ncr.ConcessionApprovedByName = names.Name(raw.ConcessionApprovedByUserId);
            ncr.ClosedByName = names.Name(raw.ClosedByUserId);
            return ResponseDtoHelper.Ok(ncr);
        }

        private static void ClearApprovals(NonConformance ncr)
        {
            ncr.QualityApprovedAt = null; ncr.QualityApprovedByUserId = null;
            ncr.EngineeringApprovedAt = null; ncr.EngineeringApprovedByUserId = null;
            ncr.ManufacturingApprovedAt = null; ncr.ManufacturingApprovedByUserId = null;
            ncr.ConcessionApprovedAt = null; ncr.ConcessionApprovedByUserId = null;
        }

        public async Task<ResponseDto<NcrDto>> SaveNcrAsync(SaveNcrDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<NcrDto>(InventoryMessages.InvalidData);
            DateTimeOffset now = DateTimeOffset.Now;
            NonConformance ncr;
            if (dto.Id.HasValue)
            {
                ncr = await Ncrs.FirstOrDefaultAsync(n => n.Id == dto.Id.Value && !n.Deleted, cancellationToken);
                if (ncr == null) return ResponseDtoHelper.NotFound<NcrDto>("گزارش عدم انطباق پیدا نشد.");
                if (ncr.Status == NcrStatus.Closed || ncr.Status == NcrStatus.Cancelled) return ResponseDtoHelper.BadRequest<NcrDto>("گزارش بسته‌شده قابل ویرایش نیست.");
            }
            else
            {
                InventoryItem item = await _uow.Set<InventoryItem>().FirstOrDefaultAsync(i => i.Id == dto.ItemId && !i.Deleted, cancellationToken);
                if (item == null) return ResponseDtoHelper.BadRequest<NcrDto>(InventoryMessages.ItemNotFound);
                if (dto.Quantity <= 0) return ResponseDtoHelper.BadRequest<NcrDto>("مقدار باید بیشتر از صفر باشد.");
                List<string> serials = (dto.SerialNumbers ?? new List<string>()).Select(InventoryText.Clean).Where(s => s != null).ToList();
                if (item.TrackingMode == ItemTrackingMode.Serial && serials.Count != dto.Quantity)
                    return ResponseDtoHelper.BadRequest<NcrDto>("سریال قطعات نامنطبق را وارد کنید (تعداد سریال‌ها برابر مقدار).");
                if (dto.LotId.HasValue && !await _uow.Set<InventoryLot>().AnyAsync(l => l.Id == dto.LotId.Value && l.ItemId == item.Id, cancellationToken))
                    return ResponseDtoHelper.BadRequest<NcrDto>("بچ انتخاب‌شده متعلق به این کالا نیست.");
                if (dto.WarehouseId.HasValue && !await _uow.Set<Warehouse>().AnyAsync(w => w.Id == dto.WarehouseId.Value && !w.Deleted, cancellationToken))
                    return ResponseDtoHelper.BadRequest<NcrDto>(InventoryMessages.WarehouseNotFound);
                ncr = new NonConformance
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, InventoryNumbers.Ncr, "NCR", dto.ReportDate == default ? DateTime.Today : dto.ReportDate, cancellationToken),
                    Status = NcrStatus.Open,
                    ItemId = item.Id,
                    LotId = dto.LotId,
                    Quantity = dto.Quantity,
                    SerialNumbers = Join(serials),
                    WarehouseId = dto.WarehouseId,
                    ProviderId = dto.ProviderId,
                    WorkOrderId = dto.WorkOrderId,
                    ContractId = dto.ContractId,
                    CreateDate = now,
                    CreatorUserId = _user.UserId,
                };
                Ncrs.Add(ncr);
            }

            if (ncr.Status == NcrStatus.Open)
            {
                bool decisionChanged = ncr.Severity != dto.Severity || ncr.Disposition != dto.Disposition ||
                                       ncr.CustomerApprovalRequired != dto.CustomerApprovalRequired;
                ncr.ReportDate = dto.ReportDate == default ? ncr.ReportDate == default ? DateTime.Today : ncr.ReportDate : dto.ReportDate.Date;
                ncr.Source = Enum.IsDefined(typeof(NcrSource), dto.Source) ? dto.Source : NcrSource.InProcess;
                ncr.Severity = dto.Severity;
                ncr.Disposition = dto.Disposition;
                ncr.OwnedByContractor = dto.OwnedByContractor;
                ncr.Requirement = InventoryText.Clean(dto.Requirement);
                ncr.Description = InventoryText.Clean(dto.Description);
                ncr.DispositionNote = InventoryText.Clean(dto.DispositionNote);
                ncr.CommitteeMinutesNumber = InventoryText.Clean(dto.CommitteeMinutesNumber);
                ncr.CommitteeMembers = InventoryText.Clean(dto.CommitteeMembers);
                ncr.ConcessionNumber = InventoryText.Clean(dto.ConcessionNumber);
                ncr.CustomerApprovalRequired = dto.CustomerApprovalRequired;
                ncr.CustomerApprovalReference = InventoryText.Clean(dto.CustomerApprovalReference);
                ncr.TargetWarehouseId = dto.TargetWarehouseId;
                if (dto.ProviderId.HasValue) ncr.ProviderId = dto.ProviderId;
                if (dto.WorkOrderId.HasValue) ncr.WorkOrderId = dto.WorkOrderId;
                if (dto.ContractId.HasValue) ncr.ContractId = dto.ContractId;
                if (decisionChanged) ClearApprovals(ncr);
            }
            // cause analysis and corrective action can be completed until the NCR is closed
            ncr.CauseCategory = dto.CauseCategory;
            ncr.RootCause = InventoryText.Clean(dto.RootCause);
            ncr.CorrectiveAction = InventoryText.Clean(dto.CorrectiveAction);
            ncr.CorrectiveActionOwner = InventoryText.Clean(dto.CorrectiveActionOwner);
            ncr.CorrectiveActionDueDate = dto.CorrectiveActionDueDate;
            ncr.ModifyDate = now;
            ncr.ModifierUserId = _user.UserId;
            try
            {
                await _uow.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ResponseDtoHelper.BadRequest<NcrDto>(InventoryMessages.ConcurrencyConflict);
            }
            return await GetNcrAsync(ncr.Id, cancellationToken);
        }

        private static bool NeedsConcession(NcrDisposition disposition) =>
            disposition == NcrDisposition.Concession || disposition == NcrDisposition.Repair;

        public async Task<ResponseDto<NcrDto>> ApproveNcrAsync(ApproveNcrDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<NcrDto>(InventoryMessages.InvalidData);
            NonConformance ncr = await Ncrs.FirstOrDefaultAsync(n => n.Id == dto.Id && !n.Deleted, cancellationToken);
            if (ncr == null) return ResponseDtoHelper.NotFound<NcrDto>("گزارش عدم انطباق پیدا نشد.");
            if (ncr.Status != NcrStatus.Open) return ResponseDtoHelper.BadRequest<NcrDto>("فقط گزارش باز تایید می‌شود.");
            if (ncr.Severity == NcrSeverity.NotClassified) return ResponseDtoHelper.BadRequest<NcrDto>("ابتدا نوع عدم انطباق (جزئی / عمده) را مشخص کنید.");
            if (ncr.Disposition == NcrDisposition.None) return ResponseDtoHelper.BadRequest<NcrDto>("ابتدا تصمیم (مجوز ارفاقی، دوباره‌کاری، تعمیر، ضایعات، مرجوعی) را مشخص کنید.");
            if (ncr.Severity == NcrSeverity.Major && (ncr.CommitteeMinutesNumber == null || ncr.CommitteeMembers == null))
                return ResponseDtoHelper.BadRequest<NcrDto>("عدم انطباق عمده با تصمیم کمیته فنی است؛ شماره صورت‌جلسه و اعضای کمیته را وارد کنید.");
            if (ncr.Disposition == NcrDisposition.Scrap && ncr.OwnedByContractor)
                return ResponseDtoHelper.BadRequest<NcrDto>("کالای متعلق به پیمانکار/مشتری ضایعات شرکت نمی‌شود؛ تصمیم «مرجوعی» را انتخاب کنید.");

            int me = _user.UserId;
            DateTimeOffset now = DateTimeOffset.Now;
            var signers = new[] { ncr.QualityApprovedByUserId, ncr.EngineeringApprovedByUserId, ncr.ManufacturingApprovedByUserId };

            if (dto.Concession)
            {
                if (!NeedsConcession(ncr.Disposition)) return ResponseDtoHelper.BadRequest<NcrDto>("این تصمیم مجوز ارفاقی لازم ندارد.");
                if (!_user.IsInRole(InventoryRoles.RoleEngineering)) return ResponseDtoHelper.BadRequest<NcrDto>(InventoryMessages.NotAllowed + " (مجوز ارفاقی را معاونت طراحی و ساخت صادر می‌کند)");
                if (ncr.ConcessionNumber == null) return ResponseDtoHelper.BadRequest<NcrDto>("شماره مجوز ارفاقی را وارد کنید.");
                if (ncr.CustomerApprovalRequired && ncr.CustomerApprovalReference == null)
                    return ResponseDtoHelper.BadRequest<NcrDto>("قرارداد تایید انحراف توسط مشتری را لازم دارد؛ شماره نامه/تایید مشتری را وارد کنید.");
                ncr.ConcessionApprovedAt = now;
                ncr.ConcessionApprovedByUserId = me;
            }
            else
            {
                string role = dto.Kind == ApprovalKind.Quality ? InventoryRoles.RoleQuality
                    : dto.Kind == ApprovalKind.Engineering ? InventoryRoles.RoleEngineering
                    : dto.Kind == ApprovalKind.Manufacturing ? InventoryRoles.RoleManufacturing
                    : null;
                if (role == null) return ResponseDtoHelper.BadRequest<NcrDto>(InventoryMessages.InvalidData);
                if (!_user.IsInRole(role)) return ResponseDtoHelper.BadRequest<NcrDto>(InventoryMessages.NotAllowed);
                if (signers.Contains(me)) return ResponseDtoHelper.BadRequest<NcrDto>(InventoryMessages.SameUser);
                if (dto.Kind == ApprovalKind.Quality) { ncr.QualityApprovedAt = now; ncr.QualityApprovedByUserId = me; }
                else if (dto.Kind == ApprovalKind.Engineering) { ncr.EngineeringApprovedAt = now; ncr.EngineeringApprovedByUserId = me; }
                else { ncr.ManufacturingApprovedAt = now; ncr.ManufacturingApprovedByUserId = me; }
            }

            // PR-QC-002: QC, engineering and manufacturing managers sign; a concession also needs its permit
            if (ncr.QualityApprovedAt.HasValue && ncr.EngineeringApprovedAt.HasValue && ncr.ManufacturingApprovedAt.HasValue &&
                (!NeedsConcession(ncr.Disposition) || ncr.ConcessionApprovedAt.HasValue))
                ncr.Status = NcrStatus.Approved;

            ncr.ModifyDate = now;
            ncr.ModifierUserId = me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetNcrAsync(ncr.Id, cancellationToken);
        }

        public async Task<ResponseDto<NcrDto>> ExecuteNcrAsync(ExecuteNcrDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<NcrDto>(InventoryMessages.InvalidData);
            NonConformance ncr = await Ncrs.FirstOrDefaultAsync(n => n.Id == dto.Id && !n.Deleted, cancellationToken);
            if (ncr == null) return ResponseDtoHelper.NotFound<NcrDto>("گزارش عدم انطباق پیدا نشد.");
            if (ncr.Status != NcrStatus.Approved) return ResponseDtoHelper.BadRequest<NcrDto>("تصمیم این گزارش هنوز تایید نشده یا قبلاً اجرا شده است.");
            if (await _uow.Set<InventoryDocument>().AnyAsync(d => d.NcrId == ncr.Id && d.Status == InventoryDocumentStatus.Draft && !d.Deleted, cancellationToken))
                return ResponseDtoHelper.BadRequest<NcrDto>("سند انبار این گزارش ساخته شده و منتظر تایید انبار است.");
            DateTimeOffset now = DateTimeOffset.Now;

            // product not held in a warehouse (found in the workshop): the decision is applied there, no stock document
            if (!ncr.WarehouseId.HasValue)
            {
                ncr.Status = NcrStatus.Executed;
                ncr.ExecutedAt = now;
                ncr.ExecutedByUserId = _user.UserId;
                await _uow.SaveChangesAsync(cancellationToken);
                return await GetNcrAsync(ncr.Id, cancellationToken);
            }

            int? targetId = dto.TargetWarehouseId ?? ncr.TargetWarehouseId;
            Warehouse target = targetId.HasValue ? await _uow.Set<Warehouse>().FirstOrDefaultAsync(w => w.Id == targetId.Value && !w.Deleted && w.IsActive, cancellationToken) : null;
            InventoryDocumentType type;
            bool confirm = true;
            switch (ncr.Disposition)
            {
                case NcrDisposition.Concession:
                    type = InventoryDocumentType.QcRelease;
                    if (target == null || target.Kind == WarehouseKind.Quarantine || target.Kind == WarehouseKind.Scrap || target.Kind == WarehouseKind.Subcontractor)
                        return ResponseDtoHelper.BadRequest<NcrDto>("انبار مقصد کالای دارای مجوز ارفاقی را انتخاب کنید.");
                    break;
                case NcrDisposition.Rework:
                case NcrDisposition.Repair:
                    type = InventoryDocumentType.Transfer;
                    if (target == null || target.Kind == WarehouseKind.Quarantine || target.Kind == WarehouseKind.Scrap || target.Kind == WarehouseKind.Subcontractor)
                        return ResponseDtoHelper.BadRequest<NcrDto>("انبار مقصد (مثلاً انبار نیمه‌ساخته / کارگاه) را برای دوباره‌کاری انتخاب کنید.");
                    break;
                case NcrDisposition.Scrap:
                    type = InventoryDocumentType.Transfer;
                    if (target == null || target.Kind != WarehouseKind.Scrap) return ResponseDtoHelper.BadRequest<NcrDto>("انبار ضایعات را انتخاب کنید.");
                    break;
                case NcrDisposition.ReturnToSupplier:
                    type = InventoryDocumentType.SupplierReturn;
                    if (!ncr.ProviderId.HasValue) return ResponseDtoHelper.BadRequest<NcrDto>("تامین‌کننده یا پیمانکار این گزارش مشخص نیست.");
                    target = null;
                    confirm = false; // the warehouse completes the exit data (driver, vehicle, receiver) and confirms
                    break;
                default:
                    return ResponseDtoHelper.BadRequest<NcrDto>("تصمیم گزارش مشخص نیست.");
            }

            ResponseDto<DocumentDto> made = await _documents.CreateSystemDocumentAsync(new SaveDocumentDto
            {
                Type = type,
                DocumentDate = (dto.DocumentDate ?? DateTime.Today).Date,
                WarehouseId = ncr.WarehouseId.Value,
                DestinationWarehouseId = target?.Id,
                ProviderId = ncr.ProviderId,
                WorkOrderId = ncr.WorkOrderId,
                ContractId = ncr.ContractId,
                ReferenceNumber = ncr.Number,
                Description = $"اجرای تصمیم گزارش عدم انطباق {ncr.Number}",
                Lines = new List<SaveDocumentLineDto>
                {
                    new SaveDocumentLineDto
                    {
                        ItemId = ncr.ItemId,
                        Quantity = ncr.Quantity,
                        LotId = ncr.LotId,
                        LocationId = ncr.LocationId,
                        DestinationLocationId = target != null ? dto.TargetLocationId : null,
                        SerialNumbers = Lines(ncr.SerialNumbers),
                    },
                },
            }, new SystemDocumentLinks { NcrId = ncr.Id }, confirm, cancellationToken);

            if (!made.Success)
            {
                if (confirm)
                {
                    List<int> drafts = await _uow.Set<InventoryDocument>()
                        .Where(d => d.NcrId == ncr.Id && d.Status == InventoryDocumentStatus.Draft && !d.Deleted).Select(d => d.Id).ToListAsync(cancellationToken);
                    foreach (int id in drafts) await _documents.DeleteDraftAsync(id, cancellationToken);
                }
                return ResponseDtoHelper.BadRequest<NcrDto>("سند انبار ساخته نشد: " + made.Message);
            }

            // the confirm of the document marks the NCR as executed; a return waits for the warehouse
            NonConformance reloaded = await Ncrs.FirstAsync(n => n.Id == ncr.Id, cancellationToken);
            reloaded.TargetWarehouseId = target?.Id;
            if (!confirm) reloaded.ResultDocumentId = made.Data.Id;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetNcrAsync(ncr.Id, cancellationToken);
        }

        public async Task<ResponseDto<NcrDto>> CloseNcrAsync(CloseNcrDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<NcrDto>(InventoryMessages.InvalidData);
            NonConformance ncr = await Ncrs.FirstOrDefaultAsync(n => n.Id == dto.Id && !n.Deleted, cancellationToken);
            if (ncr == null) return ResponseDtoHelper.NotFound<NcrDto>("گزارش عدم انطباق پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Cancel)
            {
                if (ncr.Status != NcrStatus.Open) return ResponseDtoHelper.BadRequest<NcrDto>("فقط گزارش باز ابطال می‌شود.");
                string reason = InventoryText.Clean(dto.Reason);
                if (reason == null) return ResponseDtoHelper.BadRequest<NcrDto>("علت ابطال را بنویسید.");
                if (ncr.InspectionId.HasValue) return ResponseDtoHelper.BadRequest<NcrDto>("گزارش حاصل از بازرسی ورودی ابطال نمی‌شود؛ تصمیم «مجوز ارفاقی» یا «مرجوعی» بگیرید.");
                ncr.Status = NcrStatus.Cancelled;
                ncr.EffectivenessCheck = "ابطال: " + reason;
            }
            else
            {
                if (ncr.Status != NcrStatus.Executed) return ResponseDtoHelper.BadRequest<NcrDto>("گزارش پس از اجرای تصمیم بسته می‌شود.");
                if (ncr.CauseCategory == NcrCauseCategory.None || ncr.RootCause == null || ncr.CorrectiveAction == null)
                    return ResponseDtoHelper.BadRequest<NcrDto>("پیش از بستن، دسته علت ریشه‌ای، شرح علت و اقدام اصلاحی را ثبت کنید.");
                string effectiveness = InventoryText.Clean(dto.EffectivenessCheck);
                if (effectiveness == null) return ResponseDtoHelper.BadRequest<NcrDto>("نتیجه بررسی اثربخشی اقدام اصلاحی را بنویسید.");
                ncr.EffectivenessCheck = effectiveness;
                ncr.Status = NcrStatus.Closed;
            }
            ncr.ClosedAt = now;
            ncr.ClosedByUserId = _user.UserId;
            ncr.ModifyDate = now;
            ncr.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetNcrAsync(ncr.Id, cancellationToken);
        }

        public async Task<ResponseDto<SupplierReturnReportDto>> GetSupplierReturnReportAsync(int id, CancellationToken cancellationToken)
        {
            NonConformance ncr = await Ncrs.Include(n => n.Item).Include(n => n.Provider).Include(n => n.WorkOrder).Include(n => n.Contract)
                .FirstOrDefaultAsync(n => n.Id == id && !n.Deleted, cancellationToken);
            if (ncr == null) return ResponseDtoHelper.NotFound<SupplierReturnReportDto>("گزارش عدم انطباق پیدا نشد.");
            var report = new SupplierReturnReportDto
            {
                NcrNumber = ncr.Number,
                ProviderName = ncr.Provider?.Name,
                ItemCode = ncr.Item.Code,
                ItemTitle = ncr.Item.Title,
                Unit = ncr.Item.Unit,
                Quantity = ncr.Quantity,
                WorkOrderCode = ncr.WorkOrder?.Code,
                ContractCode = ncr.Contract?.Code,
                Description = ncr.Description,
            };
            if (ncr.ReceiptDocumentId.HasValue)
            {
                InventoryDocument receipt = await _uow.Set<InventoryDocument>().Include(d => d.BuyOrder)
                    .FirstOrDefaultAsync(d => d.Id == ncr.ReceiptDocumentId.Value, cancellationToken);
                if (receipt != null)
                {
                    report.ReceiptNumber = receipt.Number;
                    report.ReceiptDate = receipt.DocumentDate;
                    report.InvoiceNumber = receipt.ReferenceNumber;
                    QcInspection inspection = ncr.InspectionId.HasValue ? await Inspections.FirstOrDefaultAsync(q => q.Id == ncr.InspectionId.Value, cancellationToken) : null;
                    InventoryDocumentLine line = inspection == null ? null
                        : await _uow.Set<InventoryDocumentLine>().FirstOrDefaultAsync(l => l.Id == inspection.ReceiptLineId, cancellationToken);
                    report.UnitPrice = line?.UnitPrice;
                    report.Amount = line?.UnitPrice * ncr.Quantity;
                    BuyOrder order = receipt.BuyOrder;
                    if (order != null)
                    {
                        report.BuyOrderId = order.Id;
                        report.BuyOrderTitle = order.Title;
                        report.FinanceNumber = order.FinanceNumber;
                        report.FinancePrice = order.FinancePrice;
                        report.PaymentState = order.FinanceAcceptDate > DateTimeOffset.MinValue.AddYears(1)
                            ? "سند مالی تایید شده در " + InventoryText.PersianDate(order.FinanceAcceptDate.DateTime) + " — بازرگانی وضعیت پرداخت را با مالی (سپیدار) تطبیق دهد"
                            : "سند مالی هنوز تایید نشده است";
                    }
                    else
                    {
                        report.PaymentState = "سفارش خرید به رسید وصل نیست؛ بازرگانی وضعیت پرداخت را از سپیدار بررسی کند";
                    }
                }
            }
            report.ReturnDocumentNumber = await _uow.Set<InventoryDocument>()
                .Where(d => d.NcrId == ncr.Id && d.Type == InventoryDocumentType.SupplierReturn && !d.Deleted)
                .OrderByDescending(d => d.Id).Select(d => d.Number ?? "پیش‌نویس").FirstOrDefaultAsync(cancellationToken);
            return ResponseDtoHelper.Ok(report);
        }
    }
}
