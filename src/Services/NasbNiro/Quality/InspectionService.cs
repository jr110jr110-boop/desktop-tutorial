using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Quality
{
    public interface IInspectionService
    {
        Task<ResponseDto<PagedList<QcRecordListDto>>> GetRecordsAsync(QcRecordQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<QcRecordDto>> GetRecordAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<QcRecordDto>> SaveRecordAsync(SaveQcRecordDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<TestReportListDto>>> GetReportsAsync(TestReportQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<TestReportDto>> GetReportAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<TestReportDto>> SaveReportAsync(SaveTestReportDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<TestReportDto>> ReportActionAsync(TestReportActionDto dto, CancellationToken cancellationToken);
        ResponseDto<List<StandardParameterDto>> GetStandards(TestReportType type);
    }

    /// <summary>Inspection records (PR-QC-003) and test reports. Limits come from the standards, the ITP and engineering; the server computes pass and fail.</summary>
    public class InspectionService : IInspectionService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IItpService _itp;
        private readonly global::Services.NasbNiro.Office.INotificationService _notify;

        public InspectionService(IUnitOfWork uow, IInventoryUserContext user, IItpService itp, global::Services.NasbNiro.Office.INotificationService notify)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _itp = itp ?? throw new ArgumentNullException(nameof(itp));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);

        // -------------------------------------------------------------------------------- equipment and inspector checks

        /// <summary>The calibration must be valid on the entry day and on the record date (R1q D15); see <see cref="QualityChecks.EquipmentAsync"/>.</summary>
        private Task<string> CheckEquipmentAsync(IEnumerable<int> equipmentIds, DateTime recordDate, CancellationToken cancellationToken) =>
            QualityChecks.EquipmentAsync(_uow, equipmentIds, recordDate, cancellationToken);

        /// <summary>
        /// R1q D17 ★: once the company keeps "Inspector" qualifications, a QC inspector records an inspection only with a valid one of his own, on the
        /// record date and on the entry day. Before any is registered the register is not in use and nothing is refused.
        /// </summary>
        private Task<string> CheckInspectorAsync(DateTime recordDate, CancellationToken cancellationToken) =>
            QualityChecks.InspectorAsync(_uow, Me, recordDate, "ثبت بازرسی بدون گواهی صلاحیت معتبر مجاز نیست.", cancellationToken);

        // -------------------------------------------------------------------------------- inspection records

        private IQueryable<QcRecordListDto> ListProjection(IQueryable<QcRecord> rows) => rows.Select(r => new QcRecordListDto
        {
            Id = r.Id, Number = r.Number, Type = r.Type, WorkOrderId = r.WorkOrderId, WorkOrderCode = r.WorkOrder.Code, ItpActivityId = r.ItpActivityId,
            ItemCode = r.Item.Code, SerialNumber = r.SerialNumber, RecordDate = r.RecordDate, Result = r.Result, SelfInspection = r.SelfInspection,
            NcrId = r.NcrId, NcrNumber = r.Ncr.Number,
        });

        public async Task<ResponseDto<PagedList<QcRecordListDto>>> GetRecordsAsync(QcRecordQuery query, CancellationToken cancellationToken)
        {
            query ??= new QcRecordQuery();
            IQueryable<QcRecord> rows = _uow.Set<QcRecord>().Where(r => !r.Deleted);
            if (query.WorkOrderId.HasValue) rows = rows.Where(r => r.WorkOrderId == query.WorkOrderId.Value);
            if (query.ItpActivityId.HasValue) rows = rows.Where(r => r.ItpActivityId == query.ItpActivityId.Value);
            if (query.Result.HasValue) rows = rows.Where(r => r.Result == query.Result.Value);
            if (query.Type.HasValue) rows = rows.Where(r => r.Type == query.Type.Value);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(r => r.Number.Contains(text) || r.SerialNumber.Contains(text) || r.Item.Code.Contains(text) || r.WorkOrder.Code.Contains(text));
            PagedList<QcRecordListDto> page = await PagedList<QcRecordListDto>.CreateAsync(ListProjection(rows.OrderByDescending(r => r.Id)), query.PageNumber, query.PageSize, cancellationToken);
            List<int> ids = page.Select(p => p.Id).ToList();
            var inspectors = await _uow.Set<QcRecord>().Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.InspectorUserId }).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, inspectors.Select(i => (int?)i.InspectorUserId), cancellationToken);
            foreach (QcRecordListDto dto in page) dto.InspectorName = names.Name(inspectors.FirstOrDefault(i => i.Id == dto.Id)?.InspectorUserId);
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<QcRecordDto>> GetRecordAsync(int id, CancellationToken cancellationToken)
        {
            QcRecord r = await _uow.Set<QcRecord>().Include(x => x.WorkOrder).Include(x => x.Item).Include(x => x.Ncr)
                .Include(x => x.Measurements).ThenInclude(m => m.Equipment).AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (r == null) return QualityText.NotFound<QcRecordDto>("گزارش بازرسی پیدا نشد.");
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { r.InspectorUserId }, cancellationToken);
            return ResponseDtoHelper.Ok(new QcRecordDto
            {
                Id = r.Id, Number = r.Number, Type = r.Type, WorkOrderId = r.WorkOrderId, WorkOrderCode = r.WorkOrder?.Code, ItpActivityId = r.ItpActivityId,
                ItemId = r.ItemId, ItemCode = r.Item?.Code, ItemTitle = r.Item?.Title, LotId = r.LotId, SerialNumber = r.SerialNumber, Quantity = r.Quantity,
                RecordDate = r.RecordDate, Result = r.Result, SelfInspection = r.SelfInspection, InspectorName = names.Name(r.InspectorUserId),
                NcrId = r.NcrId, NcrNumber = r.Ncr?.Number, Operation = r.Operation, DrawingNumber = r.DrawingNumber, DrawingRevision = r.DrawingRevision,
                AttachmentDocumentId = r.AttachmentDocumentId, Note = r.Note,
                Measurements = r.Measurements.OrderBy(m => m.Id).Select(m => new QcMeasurementDto
                {
                    Id = m.Id, Characteristic = m.Characteristic, Nominal = m.Nominal, TolPlus = m.TolPlus, TolMinus = m.TolMinus, Measured = m.Measured,
                    Unit = m.Unit, EquipmentId = m.EquipmentId, EquipmentCode = m.Equipment?.Code, Pass = m.Pass,
                }).ToList(),
            });
        }

        public async Task<ResponseDto<QcRecordDto>> SaveRecordAsync(SaveQcRecordDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<QcRecordDto>(QualityText.InvalidData);
            if (!Enum.IsDefined(typeof(QcRecordType), dto.Type) || !Enum.IsDefined(typeof(QcResult), dto.Result)) return Bad<QcRecordDto>(QualityText.InvalidData);
            DateTime date = (dto.RecordDate ?? DateTime.Today).Date;
            string dateProblem = await QualityChecks.RecordDateAsync(_uow, date, "بازرسی", cancellationToken);
            if (dateProblem != null) return Bad<QcRecordDto>(dateProblem);

            // ITP-linked inspections are done by QC only; operators may check their own work on activities that are not on the ITP
            ItpActivity activity = null;
            Itp itp = null;
            if (dto.ItpActivityId.HasValue)
            {
                activity = await _uow.Set<ItpActivity>().FirstOrDefaultAsync(a => a.Id == dto.ItpActivityId.Value, cancellationToken);
                if (activity == null) return QualityText.NotFound<QcRecordDto>("فعالیت برنامه بازرسی پیدا نشد.");
                itp = await _uow.Set<Itp>().FirstAsync(i => i.Id == activity.ItpId, cancellationToken);
                if (dto.WorkOrderId.HasValue && dto.WorkOrderId.Value != itp.WorkOrderId) return Bad<QcRecordDto>("فعالیت انتخاب‌شده متعلق به این دستور کار نیست.");
                dto.WorkOrderId = itp.WorkOrderId;
                if (!_user.IsInRole(QualityRoles.Inspectors)) return Bad<QcRecordDto>(QualityText.NotAllowed + " (بازرسی فعالیت‌های برنامه بازرسی فقط توسط بازرس QC ثبت می‌شود)");
            }
            else if (!_user.IsInRole(QualityRoles.RecordWriters)) return Bad<QcRecordDto>(QualityText.NotAllowed);
            if (_user.IsInRole(QualityRoles.Inspectors))
            {
                string competence = await CheckInspectorAsync(date, cancellationToken);
                if (competence != null) return Bad<QcRecordDto>(competence);
            }
            if (!dto.WorkOrderId.HasValue && !dto.ItemId.HasValue) return Bad<QcRecordDto>("دستور کار یا کالا را مشخص کنید.");

            if (dto.WorkOrderId.HasValue && !await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && !w.Deleted, cancellationToken))
                return Bad<QcRecordDto>("دستور کار پیدا نشد.");
            if (dto.ItemId.HasValue && !await _uow.Set<InventoryItem>().AnyAsync(i => i.Id == dto.ItemId.Value && !i.Deleted, cancellationToken))
                return Bad<QcRecordDto>("کالا پیدا نشد.");
            if (dto.LotId.HasValue && (!dto.ItemId.HasValue || !await _uow.Set<InventoryLot>().AnyAsync(l => l.Id == dto.LotId.Value && l.ItemId == dto.ItemId.Value, cancellationToken)))
                return Bad<QcRecordDto>("بچ انتخاب‌شده متعلق به این کالا نیست.");

            List<SaveQcMeasurementDto> input = dto.Measurements ?? new List<SaveQcMeasurementDto>();
            if ((dto.Type == QcRecordType.Dimensional || dto.Type == QcRecordType.FirstArticle) && input.Count == 0)
                return Bad<QcRecordDto>("برای بازرسی ابعادی، حداقل یک اندازه‌گیری ثبت کنید.");
            var measurements = new List<QcMeasurement>();
            foreach (SaveQcMeasurementDto m in input)
            {
                if (Clean(m.Characteristic) == null) return Bad<QcRecordDto>("نام مشخصه اندازه‌گیری‌شده را وارد کنید.");
                if (m.TolPlus < 0 || m.TolMinus < 0) return Bad<QcRecordDto>("تلرانس‌ها را به صورت مقدار مثبت وارد کنید (+ و −).");
                // R1q D16: a measured value without the instrument that measured it cannot be traced to a calibration (ISO 9001 7.1.5.2)
                if (!m.EquipmentId.HasValue) return Bad<QcRecordDto>($"برای اندازه‌گیری «{Clean(m.Characteristic)}» تجهیز اندازه‌گیری (ابزار) را انتخاب کنید؛ مقدار بدون ابزار قابل پذیرش نیست.");
                measurements.Add(new QcMeasurement
                {
                    Characteristic = Clean(m.Characteristic), Nominal = m.Nominal, TolPlus = m.TolPlus, TolMinus = m.TolMinus, Measured = m.Measured,
                    Unit = Clean(m.Unit), EquipmentId = m.EquipmentId,
                    Pass = m.Measured >= m.Nominal - m.TolMinus && m.Measured <= m.Nominal + m.TolPlus,
                });
            }
            string equipmentProblem = await CheckEquipmentAsync(measurements.Where(m => m.EquipmentId.HasValue).Select(m => m.EquipmentId.Value), date, cancellationToken);
            if (equipmentProblem != null) return Bad<QcRecordDto>(equipmentProblem);
            if (dto.Result == QcResult.Accept && measurements.Any(m => !m.Pass))
                return Bad<QcRecordDto>("اندازه‌گیری خارج از تلرانس ثبت شده است؛ نتیجه «قبول» مجاز نیست.");
            // a reject and a rework both open a non-conformance report (the rework is decided on it)
            bool opensNcr = dto.Result == QcResult.Reject || dto.Result == QcResult.Rework;
            if (opensNcr && !dto.ItemId.HasValue)
                return Bad<QcRecordDto>("برای نتیجه «رد» یا «دوباره‌کاری»، کالا یا قطعه را مشخص کنید تا گزارش عدم انطباق ساخته شود.");
            if (opensNcr && Clean(dto.Note) == null && measurements.All(m => m.Pass))
                return Bad<QcRecordDto>("دلیل رد یا دوباره‌کاری را در توضیحات بنویسید.");

            if (dto.WorkOrderId.HasValue) await _itp.EnsureItpForWorkOrderAsync(dto.WorkOrderId.Value, cancellationToken);

            DateTimeOffset now = DateTimeOffset.Now;
            var record = new QcRecord
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.Record, "QIR", date, cancellationToken),
                Type = dto.Type, WorkOrderId = dto.WorkOrderId, ItpActivityId = dto.ItpActivityId, ItemId = dto.ItemId, LotId = dto.LotId,
                SerialNumber = Clean(dto.SerialNumber), Quantity = dto.Quantity > 0 ? dto.Quantity : 1, Operation = Clean(dto.Operation),
                DrawingNumber = Clean(dto.DrawingNumber), DrawingRevision = Clean(dto.DrawingRevision), InspectorUserId = Me,
                SelfInspection = !_user.IsInRole(QualityRoles.Inspectors), RecordDate = date, Result = dto.Result,
                AttachmentDocumentId = dto.AttachmentDocumentId, Note = Clean(dto.Note), Measurements = measurements,
                CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<QcRecord>().Add(record);
            NonConformance drafted = null;
            if (opensNcr)
            {
                string failed = string.Join("، ", measurements.Where(m => !m.Pass).Select(m => $"{m.Characteristic}: {m.Measured}"));
                NonConformance ncr = await QualityNcrFactory.DraftAsync(_uow, Me, dto.Type == QcRecordType.Final ? NcrSource.Final : NcrSource.InProcess,
                    dto.ItemId.Value, dto.LotId, record.Quantity, dto.SerialNumber, dto.WorkOrderId,
                    dto.DrawingNumber == null ? null : $"نقشه {dto.DrawingNumber}" + (dto.DrawingRevision == null ? string.Empty : $" بازنگری {dto.DrawingRevision}"),
                    $"{(dto.Result == QcResult.Rework ? "دوباره‌کاری" : "رد")} در بازرسی {record.Number}: {Clean(dto.Note) ?? failed}" + (failed.Length > 0 && Clean(dto.Note) != null ? $" ({failed})" : string.Empty),
                    record.Number, measurements.FirstOrDefault(m => !m.Pass && m.EquipmentId.HasValue)?.EquipmentId, cancellationToken);
                record.Ncr = ncr;
                drafted = ncr;
            }
            await _uow.SaveChangesAsync(cancellationToken);
            // R1q D34: the QC people get an inbox item with the due date as soon as the report is drafted
            if (drafted != null) await NcrGates.SyncAsync(_notify, drafted, cancellationToken);
            return await GetRecordAsync(record.Id, cancellationToken);
        }

        // -------------------------------------------------------------------------------- test reports

        public ResponseDto<List<StandardParameterDto>> GetStandards(TestReportType type) =>
            ResponseDtoHelper.Ok(QualityStandards.For(type).Select(p => new StandardParameterDto
            {
                Name = p.Name, Unit = p.Unit, Min = p.Min, Max = p.Max, Source = p.Source, Mandatory = p.Mandatory, RequiresLimits = p.RequiresLimits,
            }).ToList());

        private IQueryable<TestReport> FullReport() =>
            _uow.Set<TestReport>().Include(r => r.WorkOrder).Include(r => r.Item).Include(r => r.Ncr).Include(r => r.ExaminerQualification)
                .Include(r => r.Values).Include(r => r.Indications).Include(r => r.Witnesses)
                .Include(r => r.Equipment).ThenInclude(e => e.Equipment).AsSplitQuery();

        public async Task<ResponseDto<PagedList<TestReportListDto>>> GetReportsAsync(TestReportQuery query, CancellationToken cancellationToken)
        {
            query ??= new TestReportQuery();
            IQueryable<TestReport> rows = _uow.Set<TestReport>().Where(r => !r.Deleted);
            if (query.WorkOrderId.HasValue) rows = rows.Where(r => r.WorkOrderId == query.WorkOrderId.Value);
            if (query.ItpActivityId.HasValue) rows = rows.Where(r => r.ItpActivityId == query.ItpActivityId.Value);
            if (query.Type.HasValue) rows = rows.Where(r => r.Type == query.Type.Value);
            if (query.Status.HasValue) rows = rows.Where(r => r.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(r => r.Number.Contains(text) || r.SerialNumber.Contains(text) || r.WorkOrder.Code.Contains(text));
            var projection = rows.OrderByDescending(r => r.Id).Select(r => new TestReportListDto
            {
                Id = r.Id, Number = r.Number, Type = r.Type, WorkOrderId = r.WorkOrderId, WorkOrderCode = r.WorkOrder.Code, ItpActivityId = r.ItpActivityId,
                ReportDate = r.ReportDate, Status = r.Status, Result = r.Result, NcrId = r.NcrId, NcrNumber = r.Ncr.Number,
            });
            PagedList<TestReportListDto> page = await PagedList<TestReportListDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken);
            List<int> ids = page.Select(p => p.Id).ToList();
            var examiners = await _uow.Set<TestReport>().Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.ExaminerUserId }).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, examiners.Select(e => (int?)e.ExaminerUserId), cancellationToken);
            foreach (TestReportListDto dto in page) dto.ExaminerName = names.Name(examiners.FirstOrDefault(e => e.Id == dto.Id)?.ExaminerUserId);
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<TestReportDto>> GetReportAsync(int id, CancellationToken cancellationToken)
        {
            TestReport r = await FullReport().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (r == null) return QualityText.NotFound<TestReportDto>("گزارش آزمون پیدا نشد.");
            return ResponseDtoHelper.Ok(await BuildAsync(r, cancellationToken));
        }

        private async Task<TestReportDto> BuildAsync(TestReport r, CancellationToken cancellationToken)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { r.ExaminerUserId, r.ApprovedByUserId }, cancellationToken);
            var dto = new TestReportDto
            {
                Id = r.Id, Number = r.Number, Type = r.Type, WorkOrderId = r.WorkOrderId, WorkOrderCode = r.WorkOrder?.Code, ItpActivityId = r.ItpActivityId,
                ReportDate = r.ReportDate, Status = r.Status, Result = r.Result, ExaminerName = names.Name(r.ExaminerUserId), NcrId = r.NcrId, NcrNumber = r.Ncr?.Number,
                ItemId = r.ItemId, ItemCode = r.Item?.Code, SerialNumber = r.SerialNumber, ProcedureRef = r.ProcedureRef, ProcedureRevision = r.ProcedureRevision,
                CodeEdition = r.CodeEdition, ExaminerUserId = r.ExaminerUserId, ExaminerQualificationId = r.ExaminerQualificationId,
                ExaminerQualificationNumber = r.ExaminerQualification?.CertificateNumber, Environment = r.Environment, SubmittedAt = r.SubmittedAt,
                ApprovedAt = r.ApprovedAt, ApprovedByName = names.Name(r.ApprovedByUserId), RejectReason = r.RejectReason, DocumentId = r.DocumentId,
                Note = r.Note, RowVersion = r.RowVersion == null ? null : Convert.ToBase64String(r.RowVersion),
                Values = r.Values.OrderBy(v => v.Id).Select(v => new TestReportValueDto
                {
                    Id = v.Id, Parameter = v.Parameter, Unit = v.Unit, Min = v.Min, Max = v.Max, Measured = v.Measured, Pass = v.Pass, Source = v.Source, RequiresLimits = v.RequiresLimits,
                }).ToList(),
                Indications = r.Indications.OrderBy(i => i.Id).Select(i => new TestReportIndicationDto
                {
                    Id = i.Id, Location = i.Location, Type = i.Type, Size = i.Size, Orientation = i.Orientation, Accepted = i.Accepted,
                }).ToList(),
                Equipment = r.Equipment.OrderBy(e => e.Id).Select(e => new TestReportEquipmentDto
                {
                    EquipmentId = e.EquipmentId, Code = e.Equipment?.Code, Name = e.Equipment?.Name, NextDue = e.Equipment?.NextDue,
                }).ToList(),
                Witnesses = r.Witnesses.OrderBy(w => w.Id).Select(w => new TestReportWitnessDto { Party = w.Party, Name = w.Name, WitnessDate = w.WitnessDate }).ToList(),
            };
            if (r.Status == TestReportStatus.Draft || r.Status == TestReportStatus.Rejected)
                dto.Problems = await SubmitProblemsAsync(r, cancellationToken);
            return dto;
        }

        private static bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Builds the value rows: fixed standard limits win, unknown limits come only from the ITP (set by engineering).</summary>
        private static List<TestReportValue> BuildValues(TestReportType type, ItpActivity activity, List<SaveTestReportValueDto> input, out string error)
        {
            error = null;
            List<StandardParameter> standards = QualityStandards.For(type);
            List<ItpActivityParameter> itpParams = activity?.Parameters?.ToList() ?? new List<ItpActivityParameter>();
            var rows = new List<TestReportValue>();
            foreach (SaveTestReportValueDto v in input ?? new List<SaveTestReportValueDto>())
            {
                string name = Clean(v.Parameter);
                if (name == null) { error = "نام پارامتر آزمون را وارد کنید."; return null; }
                if (rows.Any(r => Same(r.Parameter, name))) { error = $"پارامتر «{name}» دو بار ثبت شده است."; return null; }
                rows.Add(Resolve(name, v.Measured, v.Unit, v.Min, v.Max, standards, itpParams));
            }
            // parameters of the ITP activity that the inspector did not enter are added empty: they must be measured before submitting
            foreach (ItpActivityParameter p in itpParams)
                if (!rows.Any(r => Same(r.Parameter, p.Name)))
                    rows.Add(Resolve(p.Name, null, null, null, null, standards, itpParams));
            return rows;
        }

        private static TestReportValue Resolve(string name, decimal? measured, string unit, decimal? min, decimal? max,
            List<StandardParameter> standards, List<ItpActivityParameter> itpParams)
        {
            StandardParameter std = standards.FirstOrDefault(s => Same(s.Name, name));
            ItpActivityParameter ip = itpParams.FirstOrDefault(p => Same(p.Name, name));
            var row = new TestReportValue { Parameter = name, Measured = measured, Unit = std?.Unit ?? ip?.Unit ?? Clean(unit) };
            bool itpHasLimits = ip != null && (ip.Min.HasValue || ip.Max.HasValue);
            if (std != null && !std.RequiresLimits)
            {
                // limits printed in the procedure; a stricter limit of the ITP narrows them
                row.Min = Higher(std.Min, ip?.Min);
                row.Max = Lower(std.Max, ip?.Max);
                row.Source = std.Source;
            }
            else if (std != null || ip != null)
            {
                // the limit is not known to the system: it is the value engineering sets on the ITP
                row.Min = ip?.Min; row.Max = ip?.Max;
                row.Source = ip?.Source ?? std?.Source;
                row.RequiresLimits = !itpHasLimits && (std?.RequiresLimits == true || ip?.RequiresLimits == true);
                if (!itpHasLimits && !row.RequiresLimits) { row.Min = min; row.Max = max; }
            }
            else
            {
                row.Min = min; row.Max = max; row.Source = "ثبت‌شده توسط بازرس";
            }
            row.Pass = row.RequiresLimits || !measured.HasValue ? (bool?)null
                : (!row.Min.HasValue || measured.Value >= row.Min.Value) && (!row.Max.HasValue || measured.Value <= row.Max.Value);
            return row;
        }

        private static decimal? Higher(decimal? a, decimal? b) => a.HasValue && b.HasValue ? Math.Max(a.Value, b.Value) : a ?? b;
        private static decimal? Lower(decimal? a, decimal? b) => a.HasValue && b.HasValue ? Math.Min(a.Value, b.Value) : a ?? b;

        private static ReportResult Compute(TestReport r)
        {
            if (r.Values.Any(v => v.Pass == false) || r.Indications.Any(i => !i.Accepted)) return ReportResult.Fail;
            if (r.Values.Count > 0 && r.Values.All(v => v.Pass == true)) return ReportResult.Pass;
            return ReportResult.NotEvaluated;
        }

        private async Task<string> CheckExaminerAsync(TestReport r, int? qualificationId, CancellationToken cancellationToken)
        {
            string method = QualityStandards.NdtMethod(r.Type);
            if (method == null) return null;
            if (!qualificationId.HasValue) return $"گواهی صلاحیت بازرس NDT (روش {method}) را انتخاب کنید.";
            Qualification q = await _uow.Set<Qualification>().Include(x => x.Person).FirstOrDefaultAsync(x => x.Id == qualificationId.Value && !x.Deleted, cancellationToken);
            if (q == null) return "گواهی صلاحیت پیدا نشد.";
            if (q.Kind != QualificationKind.NdtPersonnel) return "گواهی انتخاب‌شده مربوط به پرسنل NDT نیست.";
            if (!Same(q.Method, method)) return $"روش گواهی ({q.Method}) با نوع گزارش ({method}) هم‌خوان نیست.";
            if (q.Level != NdtLevel.II && q.Level != NdtLevel.III) return "تفسیر و تایید نتیجه NDT فقط با سطح II یا III مجاز است.";
            if (q.Person != null && q.Person.UserId.HasValue && q.Person.UserId.Value != r.ExaminerUserId)
                return "گواهی انتخاب‌شده متعلق به بازرس ثبت‌کننده این گزارش نیست.";
            QualificationState state = QualificationRules.State(q, r.ReportDate, null, 6, 0);
            if (!QualificationRules.Usable(state)) return $"گواهی صلاحیت در تاریخ گزارش معتبر نیست: {QualificationRules.StateText(state)}.";
            // R1q D15: a certificate that lapsed after the report date but before today cannot sign the report today either
            QualificationState today = QualificationRules.State(q, DateTime.Today, null, 6, 0);
            return QualificationRules.Usable(today) ? null : $"گواهی صلاحیت امروز معتبر نیست: {QualificationRules.StateText(today)}.";
        }

        private async Task<List<string>> SubmitProblemsAsync(TestReport r, CancellationToken cancellationToken)
        {
            var problems = new List<string>();
            if (r.Values.Count == 0) problems.Add("حداقل یک ردیف پارامتر آزمون لازم است.");
            foreach (StandardParameter std in QualityStandards.For(r.Type).Where(s => s.Mandatory))
                if (!r.Values.Any(v => Same(v.Parameter, std.Name))) problems.Add($"پارامتر الزامی «{std.Name}» ثبت نشده است.");
            foreach (TestReportValue v in r.Values)
            {
                if (v.RequiresLimits) problems.Add($"حدود پذیرش «{v.Parameter}» هنوز توسط مهندسی روی برنامه بازرسی تعیین نشده است.");
                else if (!v.Measured.HasValue) problems.Add($"مقدار اندازه‌گیری‌شده «{v.Parameter}» ثبت نشده است.");
            }
            string examiner = await CheckExaminerAsync(r, r.ExaminerQualificationId, cancellationToken);
            if (examiner != null) problems.Add(examiner);
            // R1q D16: measured values need the instruments that measured them
            if (r.Values.Any(v => v.Measured.HasValue) && r.Equipment.Count == 0)
                problems.Add("برای گزارش آزمون با مقدار اندازه‌گیری‌شده، حداقل یک تجهیز اندازه‌گیری (ابزار) انتخاب کنید.");
            string equipment = await CheckEquipmentAsync(r.Equipment.Select(e => e.EquipmentId), r.ReportDate, cancellationToken);
            if (equipment != null) problems.Add(equipment);
            if (Compute(r) == ReportResult.Fail && !r.NcrId.HasValue && !r.ItemId.HasValue)
                problems.Add("نتیجه آزمون مردود است؛ برای ساخت گزارش عدم انطباق، کالا یا قطعه را مشخص کنید.");
            return problems;
        }

        public async Task<ResponseDto<TestReportDto>> SaveReportAsync(SaveTestReportDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || !Enum.IsDefined(typeof(TestReportType), dto.Type)) return Bad<TestReportDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.Inspectors)) return Bad<TestReportDto>(QualityText.NotAllowed + " (گزارش آزمون را بازرس QC ثبت می‌کند)");
            DateTime date = (dto.ReportDate ?? DateTime.Today).Date;

            TestReport report = null;
            if (dto.Id.HasValue)
            {
                report = await FullReport().FirstOrDefaultAsync(r => r.Id == dto.Id.Value && !r.Deleted, cancellationToken);
                if (report == null) return QualityText.NotFound<TestReportDto>("گزارش آزمون پیدا نشد.");
                if (report.Status == TestReportStatus.Submitted) return Bad<TestReportDto>("گزارش ارسال‌شده منتظر تایید است و ویرایش نمی‌شود.");
                if (report.Status == TestReportStatus.Approved) return Bad<TestReportDto>("گزارش تاییدشده قابل ویرایش نیست.");
                if (report.ExaminerUserId != Me) return Bad<TestReportDto>("گزارش را فقط بازرس ثبت‌کننده ویرایش می‌کند.");
                // review M15: a draft or rejected report is judged by the date it was first entered with, not by today: the date stays unless a new one is typed, and a report whose
                // own date has fallen out of the back-date window cannot be revived by saving it again
                string originalProblem = await QualityChecks.RecordDateAsync(_uow, report.ReportDate, "گزارش آزمون", cancellationToken);
                if (originalProblem != null) return Bad<TestReportDto>(originalProblem + " گزارش پیش‌نویس یا ردشده‌ای که از مهلت ثبت گذشته ویرایش نمی‌شود؛ گزارش تازه‌ای با تاریخ واقعی آزمون ثبت کنید.");
                date = (dto.ReportDate ?? report.ReportDate).Date;
                dto.Type = report.Type;
                if (Clean(dto.RowVersion) != null)
                {
                    try { _uow.Entry(report).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); }
                    catch (FormatException) { return Bad<TestReportDto>(QualityText.InvalidData); }
                }
            }
            string dateProblem = await QualityChecks.RecordDateAsync(_uow, date, "گزارش آزمون", cancellationToken);
            if (dateProblem != null) return Bad<TestReportDto>(dateProblem);

            ItpActivity activity = null;
            if (dto.ItpActivityId.HasValue)
            {
                activity = await _uow.Set<ItpActivity>().Include(a => a.Parameters).FirstOrDefaultAsync(a => a.Id == dto.ItpActivityId.Value, cancellationToken);
                if (activity == null) return QualityText.NotFound<TestReportDto>("فعالیت برنامه بازرسی پیدا نشد.");
                if (activity.RequiredReportType.HasValue && activity.RequiredReportType.Value != dto.Type)
                    return Bad<TestReportDto>($"این فعالیت گزارش نوع {activity.RequiredReportType} می‌خواهد.");
                if (ItpService.IsTerminal(activity.Status) && (report == null || report.ItpActivityId != activity.Id))
                    return Bad<TestReportDto>("فعالیت انتخاب‌شده پایان یافته است.");
                int woId = await _uow.Set<Itp>().Where(i => i.Id == activity.ItpId).Select(i => i.WorkOrderId).FirstAsync(cancellationToken);
                if (dto.WorkOrderId.HasValue && dto.WorkOrderId.Value != woId) return Bad<TestReportDto>("فعالیت انتخاب‌شده متعلق به این دستور کار نیست.");
                dto.WorkOrderId = woId;
            }
            if (!dto.WorkOrderId.HasValue && !dto.ItemId.HasValue) return Bad<TestReportDto>("دستور کار یا کالا را مشخص کنید.");
            if (dto.WorkOrderId.HasValue && !await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && !w.Deleted, cancellationToken))
                return Bad<TestReportDto>("دستور کار پیدا نشد.");
            if (dto.ItemId.HasValue && !await _uow.Set<InventoryItem>().AnyAsync(i => i.Id == dto.ItemId.Value && !i.Deleted, cancellationToken))
                return Bad<TestReportDto>("کالا پیدا نشد.");

            List<SaveTestReportValueDto> inputValues = dto.Values ?? new List<SaveTestReportValueDto>();
            List<TestReportValue> values = BuildValues(dto.Type, activity, inputValues, out string valueError);
            if (valueError != null) return Bad<TestReportDto>(valueError);
            List<int> equipmentIds = (dto.EquipmentIds ?? new List<int>()).Distinct().ToList();
            string equipmentProblem = await CheckEquipmentAsync(equipmentIds, date, cancellationToken);
            if (equipmentProblem != null) return Bad<TestReportDto>(equipmentProblem);
            foreach (TestReportWitnessDto w in dto.Witnesses ?? new List<TestReportWitnessDto>())
                if (Clean(w.Name) == null || !Enum.IsDefined(typeof(ReleaseParty), w.Party)) return Bad<TestReportDto>("نام و طرف شاهد را وارد کنید.");

            if (dto.WorkOrderId.HasValue) await _itp.EnsureItpForWorkOrderAsync(dto.WorkOrderId.Value, cancellationToken);

            DateTimeOffset now = DateTimeOffset.Now;
            if (report == null)
            {
                report = new TestReport
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.TestReport, "TR", date, cancellationToken),
                    Type = dto.Type, ExaminerUserId = Me, CreateDate = now, CreatorUserId = Me,
                    Values = new List<TestReportValue>(), Indications = new List<TestReportIndication>(),
                    Equipment = new List<TestReportEquipment>(), Witnesses = new List<TestReportWitness>(),
                };
                _uow.Set<TestReport>().Add(report);
            }
            report.WorkOrderId = dto.WorkOrderId; report.ItpActivityId = dto.ItpActivityId; report.ItemId = dto.ItemId;
            report.SerialNumber = Clean(dto.SerialNumber); report.ProcedureRef = Clean(dto.ProcedureRef); report.ProcedureRevision = Clean(dto.ProcedureRevision);
            report.CodeEdition = Clean(dto.CodeEdition); report.ReportDate = date; report.Environment = Clean(dto.Environment);
            report.DocumentId = dto.DocumentId; report.Note = Clean(dto.Note);
            report.ExaminerQualificationId = dto.ExaminerQualificationId;
            report.Status = TestReportStatus.Draft; report.RejectReason = null;
            report.ModifyDate = now; report.ModifierUserId = Me;

            report.Values.Clear(); foreach (TestReportValue v in values) report.Values.Add(v);
            report.Indications.Clear();
            foreach (TestReportIndicationDto i in dto.Indications ?? new List<TestReportIndicationDto>())
                report.Indications.Add(new TestReportIndication { Location = Clean(i.Location), Type = Clean(i.Type), Size = Clean(i.Size), Orientation = Clean(i.Orientation), Accepted = i.Accepted });
            report.Equipment.Clear(); foreach (int id in equipmentIds) report.Equipment.Add(new TestReportEquipment { EquipmentId = id });
            report.Witnesses.Clear();
            foreach (TestReportWitnessDto w in dto.Witnesses ?? new List<TestReportWitnessDto>())
                report.Witnesses.Add(new TestReportWitness { Party = w.Party, Name = Clean(w.Name), WitnessDate = w.WitnessDate == default ? date : w.WitnessDate.Date });
            report.Result = Compute(report);
            try
            {
                await _uow.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<TestReportDto>(QualityText.Concurrency);
            }
            return await GetReportAsync(report.Id, cancellationToken);
        }

        public async Task<ResponseDto<TestReportDto>> ReportActionAsync(TestReportActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<TestReportDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.Inspectors)) return Bad<TestReportDto>(QualityText.NotAllowed);
            TestReport report = await FullReport().FirstOrDefaultAsync(r => r.Id == dto.Id && !r.Deleted, cancellationToken);
            if (report == null) return QualityText.NotFound<TestReportDto>("گزارش آزمون پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            int? completeActivity = null;
            NonConformance drafted = null;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "submit":
                    if (report.Status != TestReportStatus.Draft && report.Status != TestReportStatus.Rejected) return Bad<TestReportDto>("فقط گزارش پیش‌نویس ارسال می‌شود.");
                    if (report.ExaminerUserId != Me) return Bad<TestReportDto>("گزارش را فقط بازرس ثبت‌کننده ارسال می‌کند.");
                    List<string> problems = await SubmitProblemsAsync(report, cancellationToken);
                    if (problems.Count > 0) return Bad<TestReportDto>(string.Join(" ", problems));
                    report.Result = Compute(report);
                    if (report.Result == ReportResult.Fail && !report.NcrId.HasValue)
                    {
                        string failed = string.Join("، ", report.Values.Where(v => v.Pass == false).Select(v => $"{v.Parameter}: {v.Measured}"));
                        NonConformance ncr = await QualityNcrFactory.DraftAsync(_uow, Me, NcrSource.InProcess, report.ItemId.Value, null, 1, report.SerialNumber,
                            report.WorkOrderId, report.ProcedureRef, $"مردود شدن آزمون {report.Number} ({report.Type}): {failed}", report.Number,
                            report.Equipment.FirstOrDefault()?.EquipmentId, cancellationToken);
                        report.Ncr = ncr;
                        drafted = ncr;
                    }
                    report.Status = TestReportStatus.Submitted; report.SubmittedAt = now;
                    break;
                case "approve":
                    if (report.Status != TestReportStatus.Submitted) return Bad<TestReportDto>("فقط گزارش ارسال‌شده تایید می‌شود.");
                    if (report.ExaminerUserId == Me || report.CreatorUserId == Me) return Bad<TestReportDto>(QualityText.SameUser + " (تاییدکننده گزارش نباید خود آزمونگر باشد)");
                    report.Status = TestReportStatus.Approved; report.ApprovedAt = now; report.ApprovedByUserId = Me;
                    completeActivity = report.ItpActivityId;
                    break;
                case "reject":
                    if (report.Status != TestReportStatus.Submitted) return Bad<TestReportDto>("فقط گزارش ارسال‌شده رد می‌شود.");
                    if (report.ExaminerUserId == Me) return Bad<TestReportDto>(QualityText.SameUser);
                    if (Clean(dto.Reason) == null) return Bad<TestReportDto>("دلیل رد گزارش را بنویسید.");
                    report.Status = TestReportStatus.Rejected; report.RejectReason = Clean(dto.Reason);
                    break;
                default:
                    return Bad<TestReportDto>("عملیات نامعتبر است.");
            }
            report.ModifyDate = now; report.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<TestReportDto>(QualityText.Concurrency);
            }
            if (drafted != null) await NcrGates.SyncAsync(_notify, drafted, cancellationToken);
            // approving the last required report finishes the ITP activity when its control points are released
            if (completeActivity.HasValue) await _itp.TryCompleteActivityAsync(completeActivity.Value, cancellationToken);
            return await GetReportAsync(report.Id, cancellationToken);
        }
    }
}
