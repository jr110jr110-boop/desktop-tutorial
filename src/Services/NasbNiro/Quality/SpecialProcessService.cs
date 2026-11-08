using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Quality
{
    public interface ISpecialProcessService
    {
        Task<ResponseDto<List<SpTypeDto>>> GetTypesAsync(SpTypeQuery query, CancellationToken ct);
        Task<ResponseDto<SpTypeDto>> SaveTypeAsync(SaveSpTypeDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<SpQualDto>>> GetQualificationsAsync(SpQualQuery query, CancellationToken ct);
        Task<ResponseDto<SpQualDto>> GetQualificationAsync(int id, CancellationToken ct);
        Task<ResponseDto<SpQualDto>> SaveQualificationAsync(SaveSpQualDto dto, CancellationToken ct);
        Task<ResponseDto<SpQualDto>> QualificationActionAsync(SpActionDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<SpRunDto>>> GetRunsAsync(SpRunQuery query, CancellationToken ct);
        Task<ResponseDto<SpRunDto>> GetRunAsync(int id, CancellationToken ct);
        Task<ResponseDto<SpRunDto>> SaveRunAsync(SaveSpRunDto dto, CancellationToken ct);
        Task<ResponseDto<SpRunDto>> RunActionAsync(SpActionDto dto, CancellationToken ct);
        Task<ResponseDto<SpGateStatusDto>> GateStatusAsync(int? operationId, int? partOperationId, CancellationToken ct);

        Task<ResponseDto<PagedList<SpSupplierApprovalDto>>> GetSupplierApprovalsAsync(SpSupplierQuery query, CancellationToken ct);
        Task<ResponseDto<SpSupplierApprovalDto>> SaveSupplierApprovalAsync(SaveSpSupplierApprovalDto dto, CancellationToken ct);
        Task<ResponseDto<SpSupplierApprovalDto>> SupplierApprovalActionAsync(SpActionDto dto, CancellationToken ct);

        /// <summary>The nightly step: tells QA about qualifications and supplier approvals that end soon (idempotent). Returns how many alerts are open.</summary>
        Task<int> ExpiryAlertsAsync(CancellationToken ct);
    }

    /// <summary>
    /// Package C (H7): special processes. The master list of process types, the qualification of a process (procedure, equipment, operators, validity; two-person approval), the run record with the parameters judged on the
    /// server (a failed run opens an NCR; a second person reviews), the approval of suppliers per process and the expiry alerts. The finish gate itself is <see cref="SpecialProcessGate"/>.
    /// </summary>
    public class SpecialProcessService : ISpecialProcessService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public const string Engineers = "ENGINEERING,ENGINEER";
        public const string QualMakers = "QA,QC,PMO,ENGINEERING,ENGINEER";
        public const string QualApprovers = "QA";
        public const string RunWriters = "QC,QA,PMO,ENGINEERING,ENGINEER,CONSTRUCTION,ASSEMBLER,PRODUCTIONMANAGER,SHOPSUPERVISOR";
        public const string RunReviewers = "QA,QC";
        public const string SupplierMakers = "QA,QC,PMO,COMMERCIAL";

        public SpecialProcessService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify ?? throw new ArgumentNullException(nameof(notify));
        }

        private int Me => _user.UserId;
        private static bool IsAi => Ai.AiActing.IsActing;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);
        private static string B64(byte[] v) => v == null ? null : Convert.ToBase64String(v);
        private static string Day(DateTime d) => FiscalCalendars.Text(d);

        private static PagedList<T> Page<T>(List<T> items, int total, int number, int size)
        {
            PagedList<T> page = PagedList<T>.CreateAsync(items, 1, 0, CancellationToken.None).GetAwaiter().GetResult();
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size; page.TotalPages = size <= 0 ? 1 : (int)Math.Ceiling(total / (double)size);
            return page;
        }

        private static (int Number, int Size) Paging(PaginationParams q) => (Math.Max(1, q.PageNumber), q.PageSize <= 0 ? 50 : Math.Min(q.PageSize, 200));

        // ================================================================== process types

        private static readonly (string Code, string Title, bool Machine, bool Survey, EquipmentType? Instrument, bool Existing, (string Name, string Unit)[] Parameters)[] Seed =
        {
            ("HT", "عملیات حرارتی", true, true, EquipmentType.TemperatureInstrument, false, new[] { ("دمای نگهداری", "C"), ("زمان نگهداری", "min"), ("نرخ گرمایش", "C/h"), ("نرخ سرمایش", "C/h") }),
            ("NIT", "نیتروره کردن", true, true, EquipmentType.TemperatureInstrument, false, new[] { ("دمای فرایند", "C"), ("زمان فرایند", "h"), ("پتانسیل نیتروژن", "Kn") }),
            ("COAT", "پوشش و رنگ", true, false, EquipmentType.Gauge, false, new[] { ("ضخامت لایه خشک", "um"), ("دمای پخت", "C"), ("زمان پخت", "min") }),
            ("BRZ", "لحیم‌کاری سخت (برازینگ)", true, true, EquipmentType.TemperatureInstrument, false, new[] { ("دمای براز", "C"), ("زمان نگهداری", "min") }),
            ("PLT", "آبکاری", true, false, EquipmentType.Gauge, false, new[] { ("ضخامت آبکاری", "um"), ("دمای حمام", "C"), ("زمان آبکاری", "min") }),
            ("NDT", "آزمون غیرمخرب (NDT)", false, false, null, true, new (string, string)[0]),
        };

        private async Task EnsureSeedAsync(CancellationToken ct)
        {
            if (await _uow.Set<SpProcessType>().IgnoreQueryFilters().AnyAsync(ct)) return;
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (var s in Seed)
            {
                var type = new SpProcessType
                {
                    Code = s.Code, Title = s.Title, RequiresMachine = s.Machine, RequiresSurvey = s.Survey, RequiredInstrumentClass = s.Instrument, ExistingControl = s.Existing, IsActive = true,
                    Description = s.Existing ? "کنترل با صلاحیت پرسنلی NDT موجود انجام می‌شود؛ دروازه فرایند ویژه برای آن اعمال نمی‌شود." : "حدود پارامتر در صلاحیت فرایند نوشته می‌شود؛ در این فهرست حد پیش‌فرضی گذاشته نشده است.",
                    CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
                };
                int order = 0;
                foreach (var p in s.Parameters) type.Parameters.Add(new SpParameter { SortOrder = order++, Name = p.Name, Unit = p.Unit, Required = true });
                _uow.Set<SpProcessType>().Add(type);
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { /* another request seeded at the same time */ foreach (var e in _uow.Set<SpProcessType>().Local.Where(t => t.Id == 0).ToList()) _uow.Set<SpProcessType>().Remove(e); }
        }

        private async Task<SpTypeDto> TypeDtoAsync(SpProcessType t, CancellationToken ct)
        {
            string procedure = t.ProcedureDeliverableId.HasValue ? await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => d.Id == t.ProcedureDeliverableId.Value).Select(d => d.DocumentNumber).FirstOrDefaultAsync(ct) : null;
            return new SpTypeDto
            {
                Id = t.Id, Code = t.Code, Title = t.Title, Description = t.Description, RequiresMachine = t.RequiresMachine, RequiresSurvey = t.RequiresSurvey, RequiredInstrumentClass = t.RequiredInstrumentClass,
                ProcedureDeliverableId = t.ProcedureDeliverableId, ProcedureNumber = procedure, ExistingControl = t.ExistingControl, IsActive = t.IsActive, RowVersion = B64(t.RowVersion),
                Parameters = t.Parameters.OrderBy(p => p.SortOrder).Select(p => new SpParameterDto { Id = p.Id, SortOrder = p.SortOrder, Name = p.Name, Unit = p.Unit, Min = p.Min, Max = p.Max, Required = p.Required }).ToList(),
            };
        }

        public async Task<ResponseDto<List<SpTypeDto>>> GetTypesAsync(SpTypeQuery query, CancellationToken ct)
        {
            await EnsureSeedAsync(ct);
            IQueryable<SpProcessType> rows = _uow.Set<SpProcessType>().AsNoTracking().Include(t => t.Parameters).Where(t => !t.Deleted);
            if (query == null || !query.IncludeInactive) rows = rows.Where(t => t.IsActive);
            var list = new List<SpTypeDto>();
            foreach (SpProcessType t in await rows.OrderBy(t => t.Id).ToListAsync(ct)) list.Add(await TypeDtoAsync(t, ct));
            return ResponseDtoHelper.Ok(list);
        }

        private static string ValidateParameters(List<SpParameterDto> parameters)
        {
            var names = new HashSet<string>();
            foreach (SpParameterDto p in parameters ?? new List<SpParameterDto>())
            {
                string name = Clean(p.Name);
                if (name == null || name.Length > 100) return "نام هر پارامتر الزامی است (حداکثر ۱۰۰ نویسه).";
                if (!names.Add(CharacteristicRules.Key(name))) return $"پارامتر «{name}» تکراری است.";
                if ((p.Unit ?? string.Empty).Length > 30) return $"واحد «{name}» بلند است.";
                if (p.Min.HasValue && p.Max.HasValue && p.Min.Value > p.Max.Value) return $"حداقل «{name}» از حداکثر آن بیشتر است.";
            }
            return null;
        }

        public async Task<ResponseDto<SpTypeDto>> SaveTypeAsync(SaveSpTypeDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SpTypeDto>(QualityText.InvalidData);
            if (!_user.IsInRole(Engineers + ",QA")) return Bad<SpTypeDto>(QualityText.NotAllowed);
            if (IsAi) return Bad<SpTypeDto>("فهرست فرایندهای ویژه را انسان ویرایش می‌کند.");
            await EnsureSeedAsync(ct);
            string code = QualityText.Upper(dto.Code), title = Clean(dto.Title);
            if (code == null || code.Length > 30 || title == null || title.Length > 200) return Bad<SpTypeDto>("کد (حداکثر ۳۰ نویسه) و عنوان فرایند را بنویسید.");
            if (dto.RequiredInstrumentClass.HasValue && !Enum.IsDefined(typeof(EquipmentType), dto.RequiredInstrumentClass.Value)) return Bad<SpTypeDto>("رده ابزار نامعتبر است.");
            string problem = ValidateParameters(dto.Parameters);
            if (problem != null) return Bad<SpTypeDto>(problem);
            if (dto.ProcedureDeliverableId.HasValue && !await _uow.Set<PcEngineeringDeliverable>().AnyAsync(d => d.Id == dto.ProcedureDeliverableId.Value && !d.Deleted, ct)) return Bad<SpTypeDto>("سند روش اجرایی در سامانه مدارک پیدا نشد.");
            if (await _uow.Set<SpProcessType>().AnyAsync(t => !t.Deleted && t.Code == code && t.Id != (dto.Id ?? 0), ct)) return Bad<SpTypeDto>("این کد قبلاً برای فرایند دیگری ثبت شده است.");
            DateTimeOffset now = DateTimeOffset.Now;
            SpProcessType type;
            if (dto.Id.HasValue)
            {
                type = await _uow.Set<SpProcessType>().Include(t => t.Parameters).FirstOrDefaultAsync(t => t.Id == dto.Id.Value && !t.Deleted, ct);
                if (type == null) return QualityText.NotFound<SpTypeDto>("فرایند پیدا نشد.");
                if (!string.IsNullOrEmpty(dto.RowVersion))
                    try { _uow.Entry(type).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<SpTypeDto>(QualityText.InvalidData); }
                _uow.Set<SpParameter>().RemoveRange(type.Parameters.ToList());
                type.Parameters.Clear();
            }
            else
            {
                type = new SpProcessType { CreateDate = now, CreatorUserId = Me };
                _uow.Set<SpProcessType>().Add(type);
            }
            type.Code = code; type.Title = title; type.Description = Clean(dto.Description); type.RequiresMachine = dto.RequiresMachine; type.RequiresSurvey = dto.RequiresSurvey && dto.RequiresMachine;
            type.RequiredInstrumentClass = dto.RequiredInstrumentClass; type.ProcedureDeliverableId = dto.ProcedureDeliverableId; type.ExistingControl = dto.ExistingControl; type.IsActive = dto.IsActive;
            int order = 0;
            foreach (SpParameterDto p in dto.Parameters ?? new List<SpParameterDto>()) type.Parameters.Add(new SpParameter { SortOrder = order++, Name = Clean(p.Name), Unit = Clean(p.Unit), Min = p.Min, Max = p.Max, Required = p.Required });
            type.ModifyDate = now; type.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SpTypeDto>(QualityText.Concurrency); }
            catch (DbUpdateException) { return Bad<SpTypeDto>("این کد یا نام پارامتر تکراری است."); }
            return ResponseDtoHelper.Ok(await TypeDtoAsync(type, ct));
        }

        // ================================================================== qualification

        private IQueryable<SpQualification> FullQual() => _uow.Set<SpQualification>().Include(q => q.Type).Include(q => q.Limits).Include(q => q.Equipments).Include(q => q.Operators);

        private async Task<SpQualDto> QualDtoAsync(SpQualification q, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { q.CreatorUserId, q.ApprovedByUserId }, ct);
            DateTime today = DateTime.Today;
            var dto = new SpQualDto
            {
                Id = q.Id, Number = q.Number, TypeId = q.TypeId, TypeCode = q.Type?.Code, TypeTitle = q.Type?.Title, Title = q.Title, ProcedureDeliverableId = q.ProcedureDeliverableId, ProcedureRevisionId = q.ProcedureRevisionId,
                ProcedureNumber = q.ProcedureNumber, ProcedureRevisionCode = q.ProcedureRevisionCode, ProcedureInForce = await SpecialProcessGate.ProcedureInForceAsync(_uow, q, ct), ValidFrom = q.ValidFrom, ValidTo = q.ValidTo,
                DaysLeft = SpecialProcessRules.DaysLeft(q.ValidTo, today), Status = q.Status, Note = q.Note, CreatorUserId = q.CreatorUserId, CreatorName = names.Name(q.CreatorUserId), CreateDate = q.CreateDate,
                SubmittedAt = q.SubmittedAt, ApprovedByName = names.Name(q.ApprovedByUserId), ApprovedAt = q.ApprovedAt, RejectReason = q.RejectReason, VoidReason = q.VoidReason, SupersedesId = q.SupersedesId, RowVersion = B64(q.RowVersion),
                Limits = q.Limits.OrderBy(l => l.SortOrder).Select(l => new SpParameterDto { Id = l.Id, SortOrder = l.SortOrder, Name = l.Name, Unit = l.Unit, Min = l.Min, Max = l.Max, Required = l.Required }).ToList(),
            };
            List<int> machineIds = q.Equipments.Where(e => e.MachineId.HasValue).Select(e => e.MachineId.Value).ToList();
            List<int> equipmentIds = q.Equipments.Where(e => e.EquipmentId.HasValue).Select(e => e.EquipmentId.Value).ToList();
            Dictionary<int, string> machines = await _uow.Set<Machine>().AsNoTracking().Where(m => machineIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Title, ct);
            var instruments = await _uow.Set<MeasuringEquipment>().AsNoTracking().Where(e => equipmentIds.Contains(e.Id)).Select(e => new { e.Id, e.Code, Title = e.Name }).ToListAsync(ct);
            foreach (SpQualEquipment e in q.Equipments)
            {
                var row = new SpQualEquipmentDto
                {
                    Id = e.Id, Kind = e.Kind, MachineId = e.MachineId, EquipmentId = e.EquipmentId, SurveyKind = e.SurveyKind, SurveyDate = e.SurveyDate, SurveyDueDate = e.SurveyDueDate, SurveyDocumentId = e.SurveyDocumentId, SurveyNote = e.SurveyNote,
                };
                if (e.MachineId.HasValue) { machines.TryGetValue(e.MachineId.Value, out string mt); row.MachineTitle = mt; if (q.Type != null && q.Type.RequiresSurvey) row.SurveyProblem = SpecialProcessRules.SurveyProblem(e, today, mt ?? "دستگاه"); }
                if (e.EquipmentId.HasValue)
                {
                    var inst = instruments.FirstOrDefault(i => i.Id == e.EquipmentId.Value);
                    row.EquipmentCode = inst?.Code; row.EquipmentTitle = inst?.Title;
                    row.CalibrationProblem = await QualityChecks.EquipmentAsync(_uow, new List<int> { e.EquipmentId.Value }, today, ct);
                }
                dto.Equipments.Add(row);
            }
            List<int> personIds = q.Operators.Select(o => o.PersonId).ToList();
            List<QualifiedPerson> people = await _uow.Set<QualifiedPerson>().AsNoTracking().Where(p => personIds.Contains(p.Id)).ToListAsync(ct);
            foreach (SpQualOperator o in q.Operators)
            {
                QualifiedPerson p = people.FirstOrDefault(x => x.Id == o.PersonId);
                dto.Operators.Add(new SpQualOperatorDto
                {
                    PersonId = o.PersonId, PersonName = p?.Name, StampId = p?.StampId,
                    CompetenceProblem = q.Type == null ? null : await SpecialProcessGate.CompetenceProblemAsync(_uow, p, q.Type, today, p?.Name ?? "اپراتور", ct),
                });
            }
            bool editor = SpEditors(q.EditorUserIds).Contains(Me) || q.CreatorUserId == Me;
            if ((q.Status == SpQualStatus.Draft || q.Status == SpQualStatus.Rejected) && _user.IsInRole(QualMakers)) { dto.Actions.Add("edit"); dto.Actions.Add("submit"); }
            if (q.Status == SpQualStatus.Submitted && _user.IsInRole(QualApprovers) && !editor && !IsAi) { dto.Actions.Add("approve"); dto.Actions.Add("reject"); }
            if ((q.Status == SpQualStatus.Draft || q.Status == SpQualStatus.Submitted || q.Status == SpQualStatus.Approved) && _user.IsInRole(QualApprovers + ",PMO")) dto.Actions.Add("void");
            return dto;
        }

        private static List<int> SpEditors(string text) => MaterialSpecService.Editors(text);

        public async Task<ResponseDto<PagedList<SpQualDto>>> GetQualificationsAsync(SpQualQuery query, CancellationToken ct)
        {
            query ??= new SpQualQuery();
            await EnsureSeedAsync(ct);
            IQueryable<SpQualification> rows = FullQual().AsNoTracking().Where(q => !q.Deleted);
            if (query.TypeId.HasValue) rows = rows.Where(q => q.TypeId == query.TypeId.Value);
            if (query.Status.HasValue) rows = rows.Where(q => q.Status == query.Status.Value);
            if (query.ExpiringInDays.HasValue)
            {
                DateTime limit = DateTime.Today.AddDays(query.ExpiringInDays.Value);
                rows = rows.Where(q => q.Status == SpQualStatus.Approved && q.ValidTo <= limit);
            }
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(q => q.Number.Contains(text) || q.Title.Contains(text) || q.ProcedureNumber.Contains(text));
            (int number, int size) = Paging(query);
            int total = await rows.CountAsync(ct);
            List<SpQualification> list = await rows.OrderByDescending(q => q.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            var dtos = new List<SpQualDto>();
            foreach (SpQualification q in list) dtos.Add(await QualDtoAsync(q, ct));
            return ResponseDtoHelper.Ok(Page(dtos, total, number, size));
        }

        public async Task<ResponseDto<SpQualDto>> GetQualificationAsync(int id, CancellationToken ct)
        {
            SpQualification q = await FullQual().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (q == null) return QualityText.NotFound<SpQualDto>("صلاحیت فرایند پیدا نشد.");
            return ResponseDtoHelper.Ok(await QualDtoAsync(q, ct));
        }

        public async Task<ResponseDto<SpQualDto>> SaveQualificationAsync(SaveSpQualDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SpQualDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualMakers)) return Bad<SpQualDto>(QualityText.NotAllowed);
            if (IsAi) return Bad<SpQualDto>("صلاحیت فرایند را انسان تهیه می‌کند؛ صندلی هوش مصنوعی آن را نمی‌نویسد.");
            await EnsureSeedAsync(ct);
            SpProcessType type = await _uow.Set<SpProcessType>().Include(t => t.Parameters).FirstOrDefaultAsync(t => t.Id == dto.TypeId && !t.Deleted, ct);
            if (type == null || !type.IsActive) return Bad<SpQualDto>("نوع فرایند پیدا نشد یا غیرفعال است.");
            if (type.ExistingControl) return Bad<SpQualDto>("این فرایند با صلاحیت پرسنلی موجود (NDT) کنترل می‌شود؛ صلاحیت فرایند لازم نیست.");
            if (dto.ValidTo.Date < dto.ValidFrom.Date) return Bad<SpQualDto>("پایان اعتبار پیش از آغاز آن است.");
            if ((dto.ValidTo.Date - dto.ValidFrom.Date).TotalDays > 366 * 3) return Bad<SpQualDto>("اعتبار صلاحیت فرایند بیش از سه سال نیست.");
            PcDocumentRevision revision = await _uow.Set<PcDocumentRevision>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == dto.ProcedureRevisionId && r.DeliverableId == dto.ProcedureDeliverableId && !r.Deleted, ct);
            if (revision == null) return Bad<SpQualDto>("ویرایش روش اجرایی پیدا نشد.");
            if (revision.Status != PcDocStatus.Released && revision.Status != PcDocStatus.AsBuilt) return Bad<SpQualDto>("روش اجرایی باید ویرایش منتشرشده (معتبر) باشد.");
            string docNumber = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => d.Id == dto.ProcedureDeliverableId).Select(d => d.DocumentNumber).FirstOrDefaultAsync(ct);

            // the limits of the qualification: typed or copied from the master; never wider than the master
            List<SpParameterDto> limits = dto.Limits != null && dto.Limits.Count > 0
                ? dto.Limits
                : type.Parameters.OrderBy(p => p.SortOrder).Select(p => new SpParameterDto { Name = p.Name, Unit = p.Unit, Min = p.Min, Max = p.Max, Required = p.Required }).ToList();
            string problem = ValidateParameters(limits);
            if (problem != null) return Bad<SpQualDto>(problem);
            foreach (SpParameterDto l in limits)
            {
                SpParameter master = type.Parameters.FirstOrDefault(p => CharacteristicRules.Key(p.Name) == CharacteristicRules.Key(l.Name));
                if (master == null) continue;
                if (master.Min.HasValue && (!l.Min.HasValue || l.Min.Value < master.Min.Value)) return Bad<SpQualDto>($"حداقل «{l.Name}» از حداقل مجاز فهرست فرایند ({SpecialProcessRules.Num(master.Min.Value)}) کمتر است.");
                if (master.Max.HasValue && (!l.Max.HasValue || l.Max.Value > master.Max.Value)) return Bad<SpQualDto>($"حداکثر «{l.Name}» از حداکثر مجاز فهرست فرایند ({SpecialProcessRules.Num(master.Max.Value)}) بیشتر است.");
            }

            var equipments = dto.Equipments ?? new List<SaveSpQualEquipmentDto>();
            foreach (SaveSpQualEquipmentDto e in equipments)
            {
                if (e.Kind == SpEquipmentKind.Machine)
                {
                    if (!e.MachineId.HasValue || !await _uow.Set<Machine>().AnyAsync(m => m.Id == e.MachineId.Value, ct)) return Bad<SpQualDto>("دستگاه (کوره/خط) پیدا نشد.");
                    if (e.SurveyKind != SpSurveyKind.None && (!e.SurveyDate.HasValue || !e.SurveyDueDate.HasValue || e.SurveyDueDate.Value.Date < e.SurveyDate.Value.Date)) return Bad<SpQualDto>("تاریخ بررسی دما و سررسید بعدی آن را درست وارد کنید.");
                }
                else if (e.Kind == SpEquipmentKind.Instrument)
                {
                    if (!e.EquipmentId.HasValue || !await _uow.Set<MeasuringEquipment>().AnyAsync(m => m.Id == e.EquipmentId.Value && !m.Deleted, ct)) return Bad<SpQualDto>("ابزار اندازه‌گیری پیدا نشد.");
                }
                else return Bad<SpQualDto>("نوع تجهیز نامعتبر است.");
            }
            if (equipments.Where(e => e.Kind == SpEquipmentKind.Machine).GroupBy(e => e.MachineId).Any(g => g.Count() > 1) || equipments.Where(e => e.Kind == SpEquipmentKind.Instrument).GroupBy(e => e.EquipmentId).Any(g => g.Count() > 1))
                return Bad<SpQualDto>("یک تجهیز دوبار آمده است.");
            List<int> personIds = (dto.OperatorPersonIds ?? new List<int>()).Distinct().ToList();
            if (personIds.Count > 0 && await _uow.Set<QualifiedPerson>().CountAsync(p => personIds.Contains(p.Id) && !p.Deleted && p.IsActive, ct) != personIds.Count) return Bad<SpQualDto>("یکی از اپراتورها در فهرست صلاحیت‌ها پیدا نشد یا غیرفعال است.");

            DateTimeOffset now = DateTimeOffset.Now;
            SpQualification q;
            if (dto.Id.HasValue)
            {
                q = await FullQual().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (q == null) return QualityText.NotFound<SpQualDto>("صلاحیت فرایند پیدا نشد.");
                if (q.Status != SpQualStatus.Draft && q.Status != SpQualStatus.Rejected) return Bad<SpQualDto>("فقط پیش‌نویس یا ردشده ویرایش می‌شود؛ برای تمدید، صلاحیت تازه با ارجاع به این یکی بسازید.");
                if (q.TypeId != type.Id) return Bad<SpQualDto>("نوع فرایند صلاحیت ثبت‌شده تغییر نمی‌کند.");
                if (!string.IsNullOrEmpty(dto.RowVersion))
                    try { _uow.Entry(q).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<SpQualDto>(QualityText.InvalidData); }
                _uow.Set<SpQualLimit>().RemoveRange(q.Limits.ToList());
                _uow.Set<SpQualEquipment>().RemoveRange(q.Equipments.ToList());
                _uow.Set<SpQualOperator>().RemoveRange(q.Operators.ToList());
                q.Limits.Clear(); q.Equipments.Clear(); q.Operators.Clear();
            }
            else
            {
                if (dto.SupersedesId.HasValue)
                {
                    SpQualification old = await _uow.Set<SpQualification>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.SupersedesId.Value && !x.Deleted, ct);
                    if (old == null || old.Status != SpQualStatus.Approved || old.TypeId != type.Id) return Bad<SpQualDto>("صلاحیت قبلی برای تمدید باید تاییدشده و از همین نوع فرایند باشد.");
                }
                q = new SpQualification
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.SpQualification, "SPQ", DateTime.Today, ct),
                    Status = SpQualStatus.Draft, SupersedesId = dto.SupersedesId, CreateDate = now, CreatorUserId = Me, Type = type,
                };
                _uow.Set<SpQualification>().Add(q);
            }
            q.TypeId = type.Id; q.Title = Clean(dto.Title); q.ProcedureDeliverableId = dto.ProcedureDeliverableId; q.ProcedureRevisionId = dto.ProcedureRevisionId;
            q.ProcedureNumber = docNumber; q.ProcedureRevisionCode = revision.Code; q.ValidFrom = dto.ValidFrom.Date; q.ValidTo = dto.ValidTo.Date; q.Note = Clean(dto.Note);
            q.Status = SpQualStatus.Draft; q.RejectReason = null;
            int order = 0;
            foreach (SpParameterDto l in limits) q.Limits.Add(new SpQualLimit { SortOrder = order++, Name = Clean(l.Name), Unit = Clean(l.Unit), Min = l.Min, Max = l.Max, Required = l.Required });
            foreach (SaveSpQualEquipmentDto e in equipments)
                q.Equipments.Add(new SpQualEquipment
                {
                    Kind = e.Kind, MachineId = e.Kind == SpEquipmentKind.Machine ? e.MachineId : null, EquipmentId = e.Kind == SpEquipmentKind.Instrument ? e.EquipmentId : null,
                    SurveyKind = e.Kind == SpEquipmentKind.Machine ? e.SurveyKind : SpSurveyKind.None, SurveyDate = e.Kind == SpEquipmentKind.Machine ? e.SurveyDate?.Date : null,
                    SurveyDueDate = e.Kind == SpEquipmentKind.Machine ? e.SurveyDueDate?.Date : null, SurveyDocumentId = e.Kind == SpEquipmentKind.Machine ? e.SurveyDocumentId : null,
                    SurveyNote = e.Kind == SpEquipmentKind.Machine ? Clean(e.SurveyNote) : null,
                });
            foreach (int id in personIds) q.Operators.Add(new SpQualOperator { PersonId = id });
            q.EditorUserIds = MaterialSpecService.WithEditor(q.EditorUserIds, Me);
            q.ModifyDate = now; q.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SpQualDto>(QualityText.Concurrency); }
            return ResponseDtoHelper.Ok(await QualDtoAsync(q, ct));
        }

        /// <summary>What a qualification must have before it is submitted or approved. Null = complete.</summary>
        private async Task<string> QualificationCompleteAsync(SpQualification q, CancellationToken ct)
        {
            SpProcessType type = q.Type ?? await _uow.Set<SpProcessType>().Include(t => t.Parameters).FirstAsync(t => t.Id == q.TypeId, ct);
            if (!await SpecialProcessGate.ProcedureInForceAsync(_uow, q, ct)) return "ویرایش روش اجرایی دیگر معتبر نیست (جایگزین یا ابطال شده)؛ صلاحیت را با ویرایش جاری بازنویسی کنید.";
            if (q.Limits.Count == 0) return "هیچ حد پارامتری ثبت نشده است.";
            SpQualLimit open = q.Limits.FirstOrDefault(l => !l.Min.HasValue && !l.Max.HasValue);
            if (open != null) return $"برای پارامتر «{open.Name}» نه حداقل ثبت شده نه حداکثر؛ حد مجاز فرایند را بنویسید (در فهرست فرایندها حد پیش‌فرض گذاشته نشده است).";
            if (type.RequiresMachine)
            {
                List<SpQualEquipment> machines = q.Equipments.Where(e => e.Kind == SpEquipmentKind.Machine).ToList();
                if (machines.Count == 0) return $"دست‌کم یک دستگاه (کوره/خط) برای {type.Title} لازم است.";
                if (type.RequiresSurvey)
                    foreach (SpQualEquipment m in machines)
                    {
                        string title = await _uow.Set<Machine>().AsNoTracking().Where(x => x.Id == m.MachineId).Select(x => x.Title).FirstOrDefaultAsync(ct) ?? "دستگاه";
                        string survey = SpecialProcessRules.SurveyProblem(m, q.ValidFrom, title);
                        if (survey != null) return survey;
                    }
            }
            if (type.RequiredInstrumentClass.HasValue)
            {
                List<int> ids = q.Equipments.Where(e => e.Kind == SpEquipmentKind.Instrument && e.EquipmentId.HasValue).Select(e => e.EquipmentId.Value).ToList();
                if (ids.Count == 0) return $"دست‌کم یک ابزار ({type.RequiredInstrumentClass.Value}) با کالیبراسیون معتبر لازم است.";
                string calibration = await QualityChecks.EquipmentAsync(_uow, ids, DateTime.Today, ct);
                if (calibration != null) return calibration;
            }
            if (q.Operators.Count == 0) return "دست‌کم یک اپراتور واجد صلاحیت لازم است.";
            foreach (SpQualOperator o in q.Operators)
            {
                QualifiedPerson p = await _uow.Set<QualifiedPerson>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == o.PersonId, ct);
                string problem = await SpecialProcessGate.CompetenceProblemAsync(_uow, p, type, DateTime.Today, p?.Name ?? "اپراتور", ct);
                if (problem != null) return problem;
            }
            return null;
        }

        public async Task<ResponseDto<SpQualDto>> QualificationActionAsync(SpActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<SpQualDto>(QualityText.InvalidData);
            SpQualification q = await FullQual().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (q == null) return QualityText.NotFound<SpQualDto>("صلاحیت فرایند پیدا نشد.");
            if (!string.IsNullOrEmpty(dto.RowVersion))
                try { _uow.Entry(q).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<SpQualDto>(QualityText.InvalidData); }
            string note = Clean(dto.Note);
            DateTimeOffset now = DateTimeOffset.Now;
            bool editor = SpEditors(q.EditorUserIds).Contains(Me) || q.CreatorUserId == Me;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "submit":
                    if (!_user.IsInRole(QualMakers)) return Bad<SpQualDto>(QualityText.NotAllowed);
                    if (q.Status != SpQualStatus.Draft && q.Status != SpQualStatus.Rejected) return Bad<SpQualDto>("فقط پیش‌نویس ارسال می‌شود.");
                    string incomplete = await QualificationCompleteAsync(q, ct);
                    if (incomplete != null) return Bad<SpQualDto>(incomplete);
                    q.Status = SpQualStatus.Submitted; q.SubmittedAt = now; q.RejectReason = null;
                    break;
                case "approve":
                case "reject":
                    bool approve = dto.Action.Trim().Equals("approve", StringComparison.OrdinalIgnoreCase);
                    if (!_user.IsInRole(QualApprovers)) return Bad<SpQualDto>(QualityText.NotAllowed + " (صلاحیت فرایند را QA تایید می‌کند)");
                    if (IsAi) return Bad<SpQualDto>("تایید صلاحیت فرایند با انسان است؛ صندلی هوش مصنوعی آن را تایید نمی‌کند.");
                    if (q.Status != SpQualStatus.Submitted) return Bad<SpQualDto>("فقط صلاحیت ارسال‌شده تصمیم می‌گیرد.");
                    if (editor) return Bad<SpQualDto>(QualityText.SameUser + " (تهیه‌کننده یا ویرایشگر صلاحیت نمی‌تواند آن را تایید کند)");
                    if (!approve && note == null) return Bad<SpQualDto>("دلیل رد را بنویسید.");
                    if (approve)
                    {
                        string again = await QualificationCompleteAsync(q, ct);
                        if (again != null) return Bad<SpQualDto>(again);
                        if (q.ValidTo.Date < DateTime.Today) return Bad<SpQualDto>("پایان اعتبار گذشته است؛ تاریخ‌ها را اصلاح کنید.");
                        if (q.SupersedesId.HasValue)
                        {
                            SpQualification old = await _uow.Set<SpQualification>().FirstOrDefaultAsync(x => x.Id == q.SupersedesId.Value, ct);
                            if (old != null && old.Status == SpQualStatus.Approved) { old.Status = SpQualStatus.Superseded; old.ModifyDate = now; old.ModifierUserId = Me; }
                        }
                        q.Status = SpQualStatus.Approved; q.ApprovedByUserId = Me; q.ApprovedAt = now;
                    }
                    else { q.Status = SpQualStatus.Rejected; q.RejectReason = note; }
                    break;
                case "void":
                    if (!_user.IsInRole(QualApprovers + ",PMO")) return Bad<SpQualDto>(QualityText.NotAllowed);
                    if (q.Status != SpQualStatus.Draft && q.Status != SpQualStatus.Submitted && q.Status != SpQualStatus.Approved) return Bad<SpQualDto>("این صلاحیت باطل نمی‌شود.");
                    if (note == null) return Bad<SpQualDto>("دلیل ابطال را بنویسید.");
                    q.Status = SpQualStatus.Void; q.VoidReason = note; q.VoidedByUserId = Me; q.VoidedAt = now;
                    break;
                default:
                    return Bad<SpQualDto>("عمل نامعتبر است (submit، approve، reject یا void).");
            }
            q.ModifyDate = now; q.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SpQualDto>(QualityText.Concurrency); }
            if (q.Status == SpQualStatus.Void || q.Status == SpQualStatus.Superseded) await _notify.CloseAsync("spq-expiry:" + q.Id, "صلاحیت فرایند دیگر فعال نیست.", ct);
            return ResponseDtoHelper.Ok(await QualDtoAsync(q, ct));
        }

        // ================================================================== run

        private IQueryable<SpRun> FullRun() => _uow.Set<SpRun>().Include(r => r.Type).Include(r => r.Qualification).Include(r => r.Values).Include(r => r.Instruments);

        private async Task<SpRunDto> RunDtoAsync(SpRun r, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { r.CreatorUserId, r.ReviewedByUserId }, ct);
            var dto = new SpRunDto
            {
                Id = r.Id, Number = r.Number, TypeId = r.TypeId, TypeCode = r.Type?.Code, TypeTitle = r.Type?.Title, QualificationId = r.QualificationId, QualificationNumber = r.Qualification?.Number,
                ProcedureText = r.Qualification == null ? null : $"{r.Qualification.ProcedureNumber} / {r.Qualification.ProcedureRevisionCode}", IsSubcontract = r.IsSubcontract, SupplierId = r.SupplierId,
                ReceiptDocumentId = r.ReceiptDocumentId, OrderId = r.OrderId, OperationId = r.OperationId, PartOperationId = r.PartOperationId, WorkOrderId = r.WorkOrderId, ItemId = r.ItemId, LotId = r.LotId,
                SerialNumbers = r.SerialNumbers, Quantity = r.Quantity, OperatorPersonId = r.OperatorPersonId, MachineId = r.MachineId, StartedAt = r.StartedAt, EndedAt = r.EndedAt, ChartDocumentId = r.ChartDocumentId,
                ChartFileName = r.ChartFileName, CertificateDocumentId = r.CertificateDocumentId, CertificateNumber = r.CertificateNumber, Result = r.Result, ResultText = r.ResultText, Status = r.Status,
                ReviewStatus = r.ReviewStatus, ReviewedByName = names.Name(r.ReviewedByUserId), ReviewedAt = r.ReviewedAt, ReviewNote = r.ReviewNote, NcrId = r.NcrId, VoidReason = r.VoidReason, Note = r.Note,
                CreatorUserId = r.CreatorUserId, CreatorName = names.Name(r.CreatorUserId), CreateDate = r.CreateDate, RowVersion = B64(r.RowVersion),
                Values = r.Values.OrderBy(v => v.SortOrder).Select(v => new SpRunValueDto { Name = v.Name, Unit = v.Unit, Value = v.Value, Min = v.Min, Max = v.Max, Pass = v.Pass }).ToList(),
            };
            if (r.SupplierId.HasValue) dto.SupplierName = await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => p.Id == r.SupplierId.Value).Select(p => p.Name).FirstOrDefaultAsync(ct);
            if (r.OrderId.HasValue) dto.OrderNumber = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => o.Id == r.OrderId.Value).Select(o => o.Number).FirstOrDefaultAsync(ct);
            if (r.OperationId.HasValue && r.OrderId.HasValue)
            {
                int? seq = await _uow.Set<MfOperation>().AsNoTracking().Where(o => o.Id == r.OperationId.Value).Select(o => (int?)o.Sequence).FirstOrDefaultAsync(ct);
                if (seq.HasValue) dto.OperationText = MfNames.Operation(dto.OrderNumber, seq.Value);
            }
            if (r.ItemId.HasValue) { var item = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == r.ItemId.Value).Select(i => new { i.Code, i.Title }).FirstOrDefaultAsync(ct); dto.ItemCode = item?.Code; dto.ItemTitle = item?.Title; }
            if (r.LotId.HasValue) dto.LotNumber = await _uow.Set<InventoryLot>().AsNoTracking().Where(l => l.Id == r.LotId.Value).Select(l => l.LotNumber).FirstOrDefaultAsync(ct);
            if (r.OperatorPersonId.HasValue) { var p = await _uow.Set<QualifiedPerson>().AsNoTracking().Where(x => x.Id == r.OperatorPersonId.Value).Select(x => new { x.Name, x.StampId }).FirstOrDefaultAsync(ct); dto.OperatorName = p?.Name; dto.OperatorStamp = p?.StampId; }
            if (r.MachineId.HasValue) dto.MachineTitle = await _uow.Set<Machine>().AsNoTracking().Where(m => m.Id == r.MachineId.Value).Select(m => m.Title).FirstOrDefaultAsync(ct);
            if (r.NcrId.HasValue) dto.NcrNumber = await _uow.Set<NonConformance>().AsNoTracking().Where(n => n.Id == r.NcrId.Value).Select(n => n.Number).FirstOrDefaultAsync(ct);
            List<int> instIds = r.Instruments.Select(i => i.EquipmentId).ToList();
            var insts = await _uow.Set<MeasuringEquipment>().AsNoTracking().Where(e => instIds.Contains(e.Id)).Select(e => new { e.Id, e.Code, Title = e.Name }).ToListAsync(ct);
            dto.Instruments = r.Instruments.Select(i => new SpRunInstrumentDto { EquipmentId = i.EquipmentId, Code = insts.FirstOrDefault(x => x.Id == i.EquipmentId)?.Code, Title = insts.FirstOrDefault(x => x.Id == i.EquipmentId)?.Title }).ToList();
            if (r.Status == SpRunStatus.Recorded)
            {
                int? operatorUser = r.OperatorPersonId.HasValue ? await _uow.Set<QualifiedPerson>().Where(p => p.Id == r.OperatorPersonId.Value).Select(p => p.UserId).FirstOrDefaultAsync(ct) : null;
                if (r.Result != SpRunResult.Pending && r.ReviewStatus == SpReviewStatus.None && _user.IsInRole(RunReviewers) && !IsAi && SpecialProcessRules.ReviewerAllowed(Me, r.CreatorUserId, operatorUser))
                { if (r.Result == SpRunResult.Pass) dto.Actions.Add("review"); dto.Actions.Add("reject-review"); }
                if (_user.IsInRole(RunReviewers + ",PMO")) dto.Actions.Add("void");
            }
            return dto;
        }

        public async Task<ResponseDto<PagedList<SpRunDto>>> GetRunsAsync(SpRunQuery query, CancellationToken ct)
        {
            query ??= new SpRunQuery();
            IQueryable<SpRun> rows = FullRun().AsNoTracking().Where(r => !r.Deleted);
            if (query.TypeId.HasValue) rows = rows.Where(r => r.TypeId == query.TypeId.Value);
            if (query.OperationId.HasValue) rows = rows.Where(r => r.OperationId == query.OperationId.Value);
            if (query.OrderId.HasValue) rows = rows.Where(r => r.OrderId == query.OrderId.Value);
            if (query.PartOperationId.HasValue) rows = rows.Where(r => r.PartOperationId == query.PartOperationId.Value);
            if (query.ReceiptDocumentId.HasValue) rows = rows.Where(r => r.ReceiptDocumentId == query.ReceiptDocumentId.Value);
            if (query.Result.HasValue) rows = rows.Where(r => r.Result == query.Result.Value);
            if (query.ReviewStatus.HasValue) rows = rows.Where(r => r.ReviewStatus == query.ReviewStatus.Value);
            if (query.Status.HasValue) rows = rows.Where(r => r.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(r => r.Number.Contains(text) || r.SerialNumbers.Contains(text) || r.CertificateNumber.Contains(text));
            (int number, int size) = Paging(query);
            int total = await rows.CountAsync(ct);
            List<SpRun> list = await rows.OrderByDescending(r => r.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            var dtos = new List<SpRunDto>();
            foreach (SpRun r in list) dtos.Add(await RunDtoAsync(r, ct));
            return ResponseDtoHelper.Ok(Page(dtos, total, number, size));
        }

        public async Task<ResponseDto<SpRunDto>> GetRunAsync(int id, CancellationToken ct)
        {
            SpRun r = await FullRun().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (r == null) return QualityText.NotFound<SpRunDto>("اجرای فرایند پیدا نشد.");
            return ResponseDtoHelper.Ok(await RunDtoAsync(r, ct));
        }

        public async Task<ResponseDto<SpRunDto>> SaveRunAsync(SaveSpRunDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SpRunDto>(QualityText.InvalidData);
            if (!_user.IsInRole(RunWriters)) return Bad<SpRunDto>(QualityText.NotAllowed);
            if (IsAi) return Bad<SpRunDto>("اجرای فرایند را انسان ثبت می‌کند؛ صندلی هوش مصنوعی آن را ثبت نمی‌کند.");
            if (dto.OperationId.HasValue == dto.PartOperationId.HasValue) return Bad<SpRunDto>("اجرا باید دقیقاً به یک گام (مرحله) تولید یا یک مرحله ردیابی وصل باشد.");
            if (!dto.Quantity.HasValue || dto.Quantity.Value <= 0) return Bad<SpRunDto>("تعداد قطعه‌های اجرا را وارد کنید.");
            if (dto.EndedAt < dto.StartedAt) return Bad<SpRunDto>("پایان اجرا پیش از آغاز آن است.");
            if (dto.EndedAt > DateTime.Now.AddMinutes(10)) return Bad<SpRunDto>("زمان پایان اجرا در آینده است.");
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(_uow, ct);
            if (dto.EndedAt.Date < DateTime.Today.AddDays(-(int)settings[QualitySettingKeys.MaxBackdateDays])) return Bad<SpRunDto>("تاریخ اجرا بیش از حد مجاز به گذشته است.");

            int typeId; int? orderId = null, workOrderId = null, itemId = null, supplierOfStep = null; bool stepOutside; string stepText; int? lotId = dto.LotId;
            if (dto.OperationId.HasValue)
            {
                MfOperation op = await _uow.Set<MfOperation>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == dto.OperationId.Value && !o.Deleted, ct);
                if (op == null) return QualityText.NotFound<SpRunDto>("گام تولید پیدا نشد.");
                if (!op.IsSpecialProcess || !op.SpecialProcessTypeId.HasValue) return Bad<SpRunDto>("این گام «فرایند ویژه» نیست؛ مهندسی استاندارد عملیات را علامت بزند.");
                if (op.Status == MfOperationStatus.Done || op.Status == MfOperationStatus.Skipped) return Bad<SpRunDto>("گام پایان یافته است؛ اجرای تازه ثبت نمی‌شود.");
                MfProductionOrder order = await _uow.Set<MfProductionOrder>().AsNoTracking().FirstAsync(o => o.Id == op.OrderId, ct);
                typeId = op.SpecialProcessTypeId.Value; orderId = order.Id; workOrderId = order.WorkOrderId; itemId = order.ItemId; stepOutside = op.IsOutside; supplierOfStep = op.OutsideSupplierId;
                stepText = MfNames.Operation(order.Number, op.Sequence);
            }
            else
            {
                PartOperation po = await _uow.Set<PartOperation>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == dto.PartOperationId.Value && !o.Deleted, ct);
                if (po == null) return QualityText.NotFound<SpRunDto>("مرحله پیدا نشد.");
                MfOperationStandard std = po.OperationTemplateId.HasValue ? await _uow.Set<MfOperationStandard>().AsNoTracking().FirstOrDefaultAsync(s => s.OperationTemplateId == po.OperationTemplateId.Value && !s.Deleted, ct) : null;
                if (std == null || !std.IsSpecialProcess || !std.SpecialProcessTypeId.HasValue) return Bad<SpRunDto>("این مرحله «فرایند ویژه» نیست.");
                typeId = std.SpecialProcessTypeId.Value; workOrderId = po.WorkOrderId; stepOutside = std.IsOutside; supplierOfStep = std.DefaultSupplierId; stepText = po.Title;
                lotId ??= po.LotId;
                itemId = po.LotId.HasValue ? await _uow.Set<InventoryLot>().Where(l => l.Id == po.LotId.Value).Select(l => (int?)l.ItemId).FirstOrDefaultAsync(ct) : null;
                if (itemId == null && po.SerialUnitId.HasValue) itemId = await _uow.Set<SerialUnit>().Where(s => s.Id == po.SerialUnitId.Value).Select(s => (int?)s.ItemId).FirstOrDefaultAsync(ct);
            }
            SpProcessType type = await _uow.Set<SpProcessType>().Include(t => t.Parameters).FirstOrDefaultAsync(t => t.Id == typeId, ct);
            if (type == null) return Bad<SpRunDto>("نوع فرایند ویژه پیدا نشد.");
            if (dto.IsSubcontract != stepOutside) return Bad<SpRunDto>(stepOutside ? "این گام برون‌سپاری شده است؛ اجرای پیمانکار ثبت کنید." : "این گام داخلی است؛ اجرای پیمانکار ثبت نمی‌شود.");

            var limits = new List<JudgeLimit>();
            int? qualificationId = null, personId = dto.OperatorPersonId;
            List<int> instruments = (dto.InstrumentIds ?? new List<int>()).Distinct().ToList();
            string problem;
            if (dto.IsSubcontract)
            {
                if (!dto.SupplierId.HasValue) return Bad<SpRunDto>("پیمانکار اجرا را مشخص کنید.");
                if (supplierOfStep.HasValue && supplierOfStep != dto.SupplierId) return Bad<SpRunDto>("پیمانکار اجرا با پیمانکار گام یکی نیست.");
                problem = await SpecialProcessGate.SupplierProblemAsync(_uow, dto.SupplierId, type, dto.EndedAt, ct);
                if (problem != null) return Bad<SpRunDto>(problem);
                if (!dto.ChartDocumentId.HasValue || !dto.CertificateDocumentId.HasValue || Clean(dto.CertificateNumber) == null)
                    if (SpecialProcessGate.On(settings, QualitySettingKeys.PkcSpSubcontractEvidence)) return Bad<SpRunDto>("برای اجرای پیمانکار، فایل نمودار، فایل گواهی و شماره گواهی لازم است.");
                limits = type.Parameters.Select(p => new JudgeLimit { Name = p.Name, Unit = p.Unit, Min = p.Min, Max = p.Max, Required = p.Required }).ToList();
                personId = null;
            }
            else
            {
                if (!dto.QualificationId.HasValue) return Bad<SpRunDto>("صلاحیت فرایند را انتخاب کنید.");
                SpQualification q = await _uow.Set<SpQualification>().AsNoTracking().Include(x => x.Limits).Include(x => x.Equipments).Include(x => x.Operators).FirstOrDefaultAsync(x => x.Id == dto.QualificationId.Value && !x.Deleted, ct);
                if (q == null) return QualityText.NotFound<SpRunDto>("صلاحیت فرایند پیدا نشد.");
                if (q.TypeId != type.Id) return Bad<SpRunDto>("صلاحیت انتخاب‌شده برای فرایند دیگری است.");
                problem = SpecialProcessGate.QualificationProblem(q, dto.StartedAt, dto.EndedAt);
                if (problem != null) return Bad<SpRunDto>(problem);
                if (!await SpecialProcessGate.ProcedureInForceAsync(_uow, q, ct)) return Bad<SpRunDto>($"ویرایش روش اجرایی صلاحیت {q.Number} دیگر معتبر نیست.");
                if (!personId.HasValue) return Bad<SpRunDto>("اپراتور اجرا را مشخص کنید.");
                if (!q.Operators.Any(o => o.PersonId == personId.Value)) return Bad<SpRunDto>("اپراتور جزو اپراتورهای این صلاحیت فرایند نیست.");
                QualifiedPerson person = await _uow.Set<QualifiedPerson>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == personId.Value, ct);
                problem = await SpecialProcessGate.CompetenceProblemAsync(_uow, person, type, dto.StartedAt, person?.Name ?? "اپراتور", ct);
                if (problem != null) return Bad<SpRunDto>(problem);
                problem = await SpecialProcessGate.EquipmentProblemAsync(_uow, q, type, dto.MachineId, instruments, dto.StartedAt, ct);
                if (problem != null) return Bad<SpRunDto>(problem);
                limits = q.Limits.Select(l => new JudgeLimit { Name = l.Name, Unit = l.Unit, Min = l.Min, Max = l.Max, Required = l.Required }).ToList();
                qualificationId = q.Id;
            }

            var given = new List<JudgeValue>();
            foreach (SaveSpRunValueDto v in dto.Values ?? new List<SaveSpRunValueDto>())
            {
                if (Clean(v.Name) == null) return Bad<SpRunDto>("نام هر پارامتر الزامی است.");
                if (!v.Value.HasValue) return Bad<SpRunDto>($"مقدار «{v.Name}» عدد نیست.");
                given.Add(new JudgeValue { Name = v.Name.Trim(), Value = v.Value });
            }
            if (given.GroupBy(v => CharacteristicRules.Key(v.Name)).Any(g => g.Count() > 1)) return Bad<SpRunDto>("یک پارامتر دوبار آمده است.");
            Judgement judged = SpecialProcessRules.Judge(limits, given);
            if (judged.Missing.Count > 0) return Bad<SpRunDto>("مقدار پارامتر الزامی ثبت نشده: " + string.Join("، ", judged.Missing) + ".");
            if (judged.Unknown.Count > 0) return Bad<SpRunDto>("پارامتر ناشناس برای این فرایند: " + string.Join("، ", judged.Unknown) + ".");

            DateTimeOffset now = DateTimeOffset.Now;
            var run = new SpRun
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.SpRun, "SPR", dto.EndedAt.Date, ct),
                TypeId = type.Id, QualificationId = qualificationId, IsSubcontract = dto.IsSubcontract, SupplierId = dto.IsSubcontract ? dto.SupplierId : null, ReceiptDocumentId = dto.ReceiptDocumentId,
                OrderId = orderId, OperationId = dto.OperationId, PartOperationId = dto.PartOperationId, WorkOrderId = workOrderId, ItemId = itemId, LotId = lotId, SerialNumbers = Clean(dto.SerialNumbers),
                Quantity = dto.Quantity.Value, OperatorPersonId = personId, MachineId = dto.IsSubcontract ? null : dto.MachineId, StartedAt = dto.StartedAt, EndedAt = dto.EndedAt, ChartDocumentId = dto.ChartDocumentId,
                ChartFileName = Clean(dto.ChartFileName), CertificateDocumentId = dto.CertificateDocumentId, CertificateNumber = Clean(dto.CertificateNumber), Result = judged.Pass ? SpRunResult.Pass : SpRunResult.Fail,
                Status = SpRunStatus.Recorded, ReviewStatus = SpReviewStatus.None, Note = Clean(dto.Note), ResultText = judged.Text(), CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            int order2 = 0;
            foreach (JudgedRow row in judged.Rows) run.Values.Add(new SpRunValue { SortOrder = order2++, Name = row.Name, Unit = row.Unit, Value = row.Value, Min = row.Min, Max = row.Max, Pass = row.Pass });
            foreach (int id in instruments) run.Instruments.Add(new SpRunInstrument { EquipmentId = id });
            _uow.Set<SpRun>().Add(run);
            if (run.Result == SpRunResult.Fail)
            {
                if (!itemId.HasValue) return Bad<SpRunDto>("اجرای مردود باید گزارش عدم انطباق بسازد و کالای این گام مشخص نیست.");
                NonConformance ncr = await QualityNcrFactory.DraftAsync(_uow, Me, NcrSource.InProcess, itemId.Value, lotId, run.Quantity, run.SerialNumbers, workOrderId,
                    $"فرایند ویژه {type.Title}: پارامترها در حد صلاحیت فرایند", $"اجرای {run.Number} در گام {stepText} مردود شد: {run.ResultText}", run.Number, null, ct);
                await _uow.SaveChangesAsync(ct);
                run.NcrId = ncr.Id;
            }
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await RunDtoAsync((await FullRun().AsNoTracking().FirstAsync(r => r.Id == run.Id, ct)), ct));
        }

        public async Task<ResponseDto<SpRunDto>> RunActionAsync(SpActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<SpRunDto>(QualityText.InvalidData);
            SpRun r = await FullRun().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return QualityText.NotFound<SpRunDto>("اجرای فرایند پیدا نشد.");
            if (!string.IsNullOrEmpty(dto.RowVersion))
                try { _uow.Entry(r).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<SpRunDto>(QualityText.InvalidData); }
            string note = Clean(dto.Note);
            DateTimeOffset now = DateTimeOffset.Now;
            if (r.Status != SpRunStatus.Recorded) return Bad<SpRunDto>("این اجرا باطل شده است.");
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "review":
                case "reject-review":
                    bool approve = dto.Action.Trim().Equals("review", StringComparison.OrdinalIgnoreCase);
                    if (!_user.IsInRole(RunReviewers)) return Bad<SpRunDto>(QualityText.NotAllowed + " (اجرا را QA/QC بازبینی می‌کند)");
                    if (IsAi) return Bad<SpRunDto>("بازبینی اجرای فرایند ویژه با انسان است؛ صندلی هوش مصنوعی آن را تایید نمی‌کند.");
                    if (r.ReviewStatus != SpReviewStatus.None) return Bad<SpRunDto>("این اجرا قبلاً بازبینی شده است.");
                    int? operatorUser = r.OperatorPersonId.HasValue ? await _uow.Set<QualifiedPerson>().Where(p => p.Id == r.OperatorPersonId.Value).Select(p => p.UserId).FirstOrDefaultAsync(ct) : null;
                    if (!SpecialProcessRules.ReviewerAllowed(Me, r.CreatorUserId, operatorUser)) return Bad<SpRunDto>(QualityText.SameUser + " (ثبت‌کننده یا اپراتور اجرا نمی‌تواند آن را بازبینی کند)");
                    if (approve && r.Result != SpRunResult.Pass) return Bad<SpRunDto>("اجرای مردود بازبینی-تایید نمی‌شود؛ گزارش عدم انطباق آن را پیگیری کنید.");
                    if (approve && !r.ChartDocumentId.HasValue && SpecialProcessGate.On(await QualitySettingKeys.LoadAsync(_uow, ct), QualitySettingKeys.PkcSpEnforce)) return Bad<SpRunDto>("نمودار یا سابقه فرایند پیوست نشده است؛ بازبین باید آن را ببیند.");
                    if (!approve && note == null) return Bad<SpRunDto>("دلیل رد بازبینی را بنویسید.");
                    r.ReviewStatus = approve ? SpReviewStatus.Approved : SpReviewStatus.Rejected; r.ReviewedByUserId = Me; r.ReviewedAt = now; r.ReviewNote = note;
                    break;
                case "void":
                    if (!_user.IsInRole(RunReviewers + ",PMO")) return Bad<SpRunDto>(QualityText.NotAllowed);
                    if (note == null) return Bad<SpRunDto>("دلیل ابطال را بنویسید.");
                    if (r.OperationId.HasValue)
                    {
                        MfOperationStatus? st = await _uow.Set<MfOperation>().Where(o => o.Id == r.OperationId.Value).Select(o => (MfOperationStatus?)o.Status).FirstOrDefaultAsync(ct);
                        if (st == MfOperationStatus.Done) return Bad<SpRunDto>("گام با این اجرا پایان یافته است؛ اجرای آن باطل نمی‌شود (مورد را با عدم انطباق پیگیری کنید).");
                    }
                    r.Status = SpRunStatus.Void; r.VoidReason = note; r.VoidedByUserId = Me; r.VoidedAt = now;
                    break;
                default:
                    return Bad<SpRunDto>("عمل نامعتبر است (review، reject-review یا void).");
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SpRunDto>(QualityText.Concurrency); }
            return ResponseDtoHelper.Ok(await RunDtoAsync(r, ct));
        }

        public async Task<ResponseDto<SpGateStatusDto>> GateStatusAsync(int? operationId, int? partOperationId, CancellationToken ct)
        {
            var status = new SpGateStatusDto();
            if (operationId.HasValue)
            {
                MfOperation op = await _uow.Set<MfOperation>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == operationId.Value && !o.Deleted, ct);
                if (op == null) return QualityText.NotFound<SpGateStatusDto>("گام پیدا نشد.");
                if (!op.IsSpecialProcess || !op.SpecialProcessTypeId.HasValue) return ResponseDtoHelper.Ok(status);
                MfProductionOrder order = await _uow.Set<MfProductionOrder>().AsNoTracking().FirstAsync(o => o.Id == op.OrderId, ct);
                SpProcessType type = await _uow.Set<SpProcessType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == op.SpecialProcessTypeId.Value, ct);
                status.Applies = type != null && !type.ExistingControl; status.TypeId = type?.Id; status.TypeTitle = type?.Title;
                if (status.Applies)
                {
                    decimal required = op.GoodQty > 0 ? op.GoodQty + op.ReworkQty : order.Quantity;
                    status.Problem = await SpecialProcessGate.FinishProblemAsync(_uow, order, op, required, op.ScrapQty, ct);
                    status.Ready = status.Problem == null;
                }
            }
            else if (partOperationId.HasValue)
            {
                PartOperation po = await _uow.Set<PartOperation>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == partOperationId.Value && !o.Deleted, ct);
                if (po == null) return QualityText.NotFound<SpGateStatusDto>("مرحله پیدا نشد.");
                MfOperationStandard std = po.OperationTemplateId.HasValue ? await _uow.Set<MfOperationStandard>().AsNoTracking().FirstOrDefaultAsync(s => s.OperationTemplateId == po.OperationTemplateId.Value && !s.Deleted, ct) : null;
                if (std == null || !std.IsSpecialProcess || !std.SpecialProcessTypeId.HasValue) return ResponseDtoHelper.Ok(status);
                SpProcessType type = await _uow.Set<SpProcessType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == std.SpecialProcessTypeId.Value, ct);
                status.Applies = type != null && !type.ExistingControl; status.TypeId = type?.Id; status.TypeTitle = type?.Title;
                if (status.Applies) { status.Problem = await SpecialProcessGate.PartOperationProblemAsync(_uow, po, ct); status.Ready = status.Problem == null; }
            }
            else return Bad<SpGateStatusDto>(QualityText.InvalidData);
            var runs = await FullRun().AsNoTracking().Where(r => !r.Deleted && ((operationId.HasValue && r.OperationId == operationId) || (partOperationId.HasValue && r.PartOperationId == partOperationId))).OrderBy(r => r.Id).ToListAsync(ct);
            foreach (SpRun r in runs) status.Runs.Add(await RunDtoAsync(r, ct));
            return ResponseDtoHelper.Ok(status);
        }

        // ================================================================== supplier approvals

        private async Task<SpSupplierApprovalDto> SupplierDtoAsync(SpSupplierApproval a, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { a.CreatorUserId, a.ApprovedByUserId }, ct);
            SpProcessType type = await _uow.Set<SpProcessType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == a.TypeId, ct);
            string provider = await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => p.Id == a.ProviderId).Select(p => p.Name).FirstOrDefaultAsync(ct);
            var dto = new SpSupplierApprovalDto
            {
                Id = a.Id, ProviderId = a.ProviderId, ProviderName = provider, TypeId = a.TypeId, TypeCode = type?.Code, TypeTitle = type?.Title, Status = a.Status, ValidFrom = a.ValidFrom, ValidTo = a.ValidTo,
                DaysLeft = SpecialProcessRules.DaysLeft(a.ValidTo, DateTime.Today), AccreditationNumber = a.AccreditationNumber, EvidenceDocumentId = a.EvidenceDocumentId, AuditDate = a.AuditDate, Note = a.Note,
                CreatorUserId = a.CreatorUserId, CreatorName = names.Name(a.CreatorUserId), ApprovedByName = names.Name(a.ApprovedByUserId), ApprovedAt = a.ApprovedAt, RejectReason = a.RejectReason, VoidReason = a.VoidReason, RowVersion = B64(a.RowVersion),
            };
            bool editor = SpEditors(a.EditorUserIds).Contains(Me) || a.CreatorUserId == Me;
            if (a.Status == SpApprovalStatus.Pending && _user.IsInRole(QualApprovers) && !editor && !IsAi) { dto.Actions.Add("approve"); dto.Actions.Add("reject"); }
            if ((a.Status == SpApprovalStatus.Pending || a.Status == SpApprovalStatus.Approved) && _user.IsInRole(QualApprovers + ",PMO")) dto.Actions.Add("void");
            return dto;
        }

        public async Task<ResponseDto<PagedList<SpSupplierApprovalDto>>> GetSupplierApprovalsAsync(SpSupplierQuery query, CancellationToken ct)
        {
            query ??= new SpSupplierQuery();
            IQueryable<SpSupplierApproval> rows = _uow.Set<SpSupplierApproval>().AsNoTracking().Where(a => !a.Deleted);
            if (query.ProviderId.HasValue) rows = rows.Where(a => a.ProviderId == query.ProviderId.Value);
            if (query.TypeId.HasValue) rows = rows.Where(a => a.TypeId == query.TypeId.Value);
            if (query.Status.HasValue) rows = rows.Where(a => a.Status == query.Status.Value);
            (int number, int size) = Paging(query);
            int total = await rows.CountAsync(ct);
            List<SpSupplierApproval> list = await rows.OrderByDescending(a => a.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            var dtos = new List<SpSupplierApprovalDto>();
            foreach (SpSupplierApproval a in list) dtos.Add(await SupplierDtoAsync(a, ct));
            return ResponseDtoHelper.Ok(Page(dtos, total, number, size));
        }

        public async Task<ResponseDto<SpSupplierApprovalDto>> SaveSupplierApprovalAsync(SaveSpSupplierApprovalDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SpSupplierApprovalDto>(QualityText.InvalidData);
            if (!_user.IsInRole(SupplierMakers)) return Bad<SpSupplierApprovalDto>(QualityText.NotAllowed);
            if (IsAi) return Bad<SpSupplierApprovalDto>("تایید تامین‌کننده را انسان ثبت می‌کند.");
            await EnsureSeedAsync(ct);
            SpProcessType type = await _uow.Set<SpProcessType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TypeId && !t.Deleted && t.IsActive, ct);
            if (type == null) return Bad<SpSupplierApprovalDto>("نوع فرایند پیدا نشد.");
            if (!await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AnyAsync(p => p.Id == dto.ProviderId && !p.Deleted, ct)) return Bad<SpSupplierApprovalDto>("تامین‌کننده پیدا نشد.");
            if (dto.ValidTo.Date < dto.ValidFrom.Date) return Bad<SpSupplierApprovalDto>("پایان اعتبار پیش از آغاز آن است.");
            if ((dto.ValidTo.Date - dto.ValidFrom.Date).TotalDays > 366 * 3) return Bad<SpSupplierApprovalDto>("اعتبار تایید بیش از سه سال نیست.");
            if (!dto.EvidenceDocumentId.HasValue && Clean(dto.AccreditationNumber) == null) return Bad<SpSupplierApprovalDto>("مدرک ممیزی/اعتبارنامه (فایل یا شماره) را ثبت کنید.");
            DateTimeOffset now = DateTimeOffset.Now;
            SpSupplierApproval a;
            if (dto.Id.HasValue)
            {
                a = await _uow.Set<SpSupplierApproval>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (a == null) return QualityText.NotFound<SpSupplierApprovalDto>("تایید تامین‌کننده پیدا نشد.");
                if (a.Status != SpApprovalStatus.Pending) return Bad<SpSupplierApprovalDto>("فقط تایید در انتظار ویرایش می‌شود.");
                if (!string.IsNullOrEmpty(dto.RowVersion))
                    try { _uow.Entry(a).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<SpSupplierApprovalDto>(QualityText.InvalidData); }
                if (a.ProviderId != dto.ProviderId || a.TypeId != dto.TypeId) return Bad<SpSupplierApprovalDto>("تامین‌کننده و فرایند ثبت‌شده تغییر نمی‌کند.");
            }
            else
            {
                SpSupplierApproval live = await _uow.Set<SpSupplierApproval>().FirstOrDefaultAsync(x => !x.Deleted && x.ProviderId == dto.ProviderId && x.TypeId == dto.TypeId && (x.Status == SpApprovalStatus.Pending || x.Status == SpApprovalStatus.Approved), ct);
                if (live != null)
                {
                    if (live.Status == SpApprovalStatus.Approved && live.ValidTo.Date < DateTime.Today)
                    {
                        // a renewal after the old approval ended: the old one is voided with the reason, the new one waits for the second person
                        live.Status = SpApprovalStatus.Void; live.VoidReason = "منقضی‌شده؛ با تایید تازه جایگزین شد."; live.VoidedByUserId = Me; live.VoidedAt = now; live.ModifyDate = now; live.ModifierUserId = Me;
                        await _uow.SaveChangesAsync(ct);
                    }
                    else return Bad<SpSupplierApprovalDto>("برای این تامین‌کننده و فرایند، تایید در انتظار یا معتبر وجود دارد؛ اول آن را باطل کنید.");
                }
                a = new SpSupplierApproval { ProviderId = dto.ProviderId, TypeId = dto.TypeId, Status = SpApprovalStatus.Pending, CreateDate = now, CreatorUserId = Me };
                _uow.Set<SpSupplierApproval>().Add(a);
            }
            a.ValidFrom = dto.ValidFrom.Date; a.ValidTo = dto.ValidTo.Date; a.AccreditationNumber = Clean(dto.AccreditationNumber); a.EvidenceDocumentId = dto.EvidenceDocumentId; a.AuditDate = dto.AuditDate?.Date; a.Note = Clean(dto.Note);
            a.EditorUserIds = MaterialSpecService.WithEditor(a.EditorUserIds, Me);
            a.ModifyDate = now; a.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SpSupplierApprovalDto>(QualityText.Concurrency); }
            catch (DbUpdateException) { return Bad<SpSupplierApprovalDto>("برای این تامین‌کننده و فرایند هم‌زمان تایید دیگری ثبت شد."); }
            return ResponseDtoHelper.Ok(await SupplierDtoAsync(a, ct));
        }

        public async Task<ResponseDto<SpSupplierApprovalDto>> SupplierApprovalActionAsync(SpActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<SpSupplierApprovalDto>(QualityText.InvalidData);
            SpSupplierApproval a = await _uow.Set<SpSupplierApproval>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (a == null) return QualityText.NotFound<SpSupplierApprovalDto>("تایید تامین‌کننده پیدا نشد.");
            if (!string.IsNullOrEmpty(dto.RowVersion))
                try { _uow.Entry(a).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<SpSupplierApprovalDto>(QualityText.InvalidData); }
            string note = Clean(dto.Note);
            DateTimeOffset now = DateTimeOffset.Now;
            bool editor = SpEditors(a.EditorUserIds).Contains(Me) || a.CreatorUserId == Me;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "approve":
                case "reject":
                    bool approve = dto.Action.Trim().Equals("approve", StringComparison.OrdinalIgnoreCase);
                    if (!_user.IsInRole(QualApprovers)) return Bad<SpSupplierApprovalDto>(QualityText.NotAllowed + " (تایید تامین‌کننده با QA است)");
                    if (IsAi) return Bad<SpSupplierApprovalDto>("تایید تامین‌کننده با انسان است؛ صندلی هوش مصنوعی آن را تایید نمی‌کند.");
                    if (a.Status != SpApprovalStatus.Pending) return Bad<SpSupplierApprovalDto>("فقط تایید در انتظار تصمیم‌گیری می‌شود.");
                    if (editor) return Bad<SpSupplierApprovalDto>(QualityText.SameUser + " (ثبت‌کننده نمی‌تواند تایید کند)");
                    if (!approve && note == null) return Bad<SpSupplierApprovalDto>("دلیل رد را بنویسید.");
                    if (approve && a.ValidTo.Date < DateTime.Today) return Bad<SpSupplierApprovalDto>("پایان اعتبار گذشته است.");
                    a.Status = approve ? SpApprovalStatus.Approved : SpApprovalStatus.Rejected; a.ApprovedByUserId = Me; a.ApprovedAt = now; a.RejectReason = approve ? null : note;
                    break;
                case "void":
                    if (!_user.IsInRole(QualApprovers + ",PMO")) return Bad<SpSupplierApprovalDto>(QualityText.NotAllowed);
                    if (a.Status != SpApprovalStatus.Pending && a.Status != SpApprovalStatus.Approved) return Bad<SpSupplierApprovalDto>("این تایید باطل نمی‌شود.");
                    if (note == null) return Bad<SpSupplierApprovalDto>("دلیل ابطال را بنویسید.");
                    a.Status = SpApprovalStatus.Void; a.VoidReason = note; a.VoidedByUserId = Me; a.VoidedAt = now;
                    break;
                default:
                    return Bad<SpSupplierApprovalDto>("عمل نامعتبر است (approve، reject یا void).");
            }
            a.ModifyDate = now; a.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SpSupplierApprovalDto>(QualityText.Concurrency); }
            if (a.Status == SpApprovalStatus.Void) await _notify.CloseAsync("spa-expiry:" + a.Id, "تایید تامین‌کننده باطل شد.", ct);
            return ResponseDtoHelper.Ok(await SupplierDtoAsync(a, ct));
        }

        // ================================================================== expiry alerts

        public async Task<int> ExpiryAlertsAsync(CancellationToken ct)
        {
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(_uow, ct);
            int days = (int)settings[QualitySettingKeys.PkcQualExpiryAlertDays];
            DateTime today = DateTime.Today, horizon = today.AddDays(days);
            int open = 0;
            List<SpQualification> quals = await _uow.Set<SpQualification>().AsNoTracking().Include(q => q.Type).Where(q => !q.Deleted).ToListAsync(ct);
            foreach (SpQualification q in quals)
            {
                string key = "spq-expiry:" + q.Id;
                if (q.Status == SpQualStatus.Approved && q.ValidTo.Date <= horizon)
                {
                    int left = SpecialProcessRules.DaysLeft(q.ValidTo, today);
                    string title = left < 0 ? $"صلاحیت فرایند {q.Number} ({q.Type?.Title}) منقضی شده است" : $"صلاحیت فرایند {q.Number} ({q.Type?.Title}) {left} روز دیگر منقضی می‌شود";
                    await _notify.NotifyAsync(new NotifyRequest { SourceKey = key, Module = "quality", SourceType = "sp-qualification-expiry", SourceId = q.Id, Title = title, Link = "/quality/special-process/qualifications/" + q.Id, Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Alert, Roles = "QA", DueDate = q.ValidTo }, ct);
                    open++;
                }
                else if (q.Status != SpQualStatus.Approved || q.ValidTo.Date > horizon) await _notify.CloseAsync(key, "صلاحیت فرایند دیگر در آستانه انقضا نیست.", ct);
            }
            List<SpSupplierApproval> approvals = await _uow.Set<SpSupplierApproval>().AsNoTracking().Include(a => a.Type).Where(a => !a.Deleted).ToListAsync(ct);
            foreach (SpSupplierApproval a in approvals)
            {
                string key = "spa-expiry:" + a.Id;
                if (a.Status == SpApprovalStatus.Approved && a.ValidTo.Date <= horizon)
                {
                    int left = SpecialProcessRules.DaysLeft(a.ValidTo, today);
                    string title = left < 0 ? $"تایید تامین‌کننده برای {a.Type?.Title} منقضی شده است" : $"تایید تامین‌کننده برای {a.Type?.Title} {left} روز دیگر منقضی می‌شود";
                    await _notify.NotifyAsync(new NotifyRequest { SourceKey = key, Module = "quality", SourceType = "sp-supplier-expiry", SourceId = a.Id, Title = title, Link = "/quality/special-process/suppliers", Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Alert, Roles = "QA", DueDate = a.ValidTo }, ct);
                    open++;
                }
                else if (a.Status != SpApprovalStatus.Approved || a.ValidTo.Date > horizon) await _notify.CloseAsync(key, "تایید تامین‌کننده دیگر در آستانه انقضا نیست.", ct);
            }
            return open;
        }
    }
}
