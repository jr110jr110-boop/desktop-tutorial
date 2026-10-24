using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Quality
{
    public interface IPersonnelService
    {
        Task<ResponseDto<PagedList<QualifiedPersonDto>>> GetPersonsAsync(QualifiedPersonQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<QualifiedPersonDto>> GetPersonAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<QualifiedPersonDto>> SavePersonAsync(SavePersonDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<QualificationDto>>> GetQualificationsAsync(QualificationQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<QualificationDto>> SaveQualificationAsync(SaveQualificationDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<QualificationDto>> QualificationActionAsync(QualificationActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<QualificationDto>>> GetContinuityAsync(ContinuityQuery query, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<WpsListDto>>> GetWpsListAsync(WpsQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<WpsDto>> GetWpsAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<WpsDto>> SaveWpsAsync(SaveWpsDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<WpsDto>> WpsActionAsync(WpsActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<PqrDto>>> GetPqrsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<PqrDto>> SavePqrAsync(SavePqrDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PqrDto>> PqrActionAsync(PqrActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<WeldLogDto>>> GetWeldLogsAsync(WeldLogQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<WeldLogDto>> SaveWeldLogAsync(SaveWeldLogDto dto, CancellationToken cancellationToken);
    }

    /// <summary>Qualified persons, welder and NDT qualifications, WPS/PQR and weld logs (WI-QC-010, ASME IX).</summary>
    public class PersonnelService : IPersonnelService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public PersonnelService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);

        // -------------------------------------------------------------------------------- qualification state

        private sealed class UseKey
        {
            public int PersonId { get; set; }
            public string Method { get; set; }
            public DateTime Last { get; set; }
        }

        private async Task<List<UseKey>> LastUsesAsync(DateTime? upTo, CancellationToken cancellationToken)
        {
            IQueryable<WeldLog> logs = _uow.Set<WeldLog>().Where(w => !w.Deleted);
            if (upTo.HasValue) logs = logs.Where(w => w.WeldDate <= upTo.Value);
            return await logs.GroupBy(w => new { w.Qualification.PersonId, w.Qualification.Method })
                .Select(g => new UseKey { PersonId = g.Key.PersonId, Method = g.Key.Method, Last = g.Max(x => x.WeldDate) }).ToListAsync(cancellationToken);
        }

        private static DateTime? LastUse(List<UseKey> uses, Qualification q) =>
            QualificationRules.NeedsContinuity(q.Kind) ? uses.FirstOrDefault(u => u.PersonId == q.PersonId && u.Method == q.Method)?.Last : null;

        private async Task<(int Months, int Warn)> ContinuityAsync(CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> s = await QualitySettingKeys.LoadAsync(_uow, cancellationToken);
            return ((int)s[QualitySettingKeys.WeldContinuityMonths], (int)s[QualitySettingKeys.ContinuityWarnDays]);
        }

        private static QualificationDto ToDto(Qualification q, QualifiedPerson person, List<UseKey> uses, DateTime today, int months, int warn)
        {
            DateTime? last = LastUse(uses, q);
            QualificationState state = QualificationRules.State(q, today, last, months, warn);
            DateTime? lapses = null;
            if (QualificationRules.NeedsContinuity(q.Kind) && !q.Revoked && !q.Superseded)
                lapses = ((last.HasValue && last.Value.Date > q.IssuedDate.Date) ? last.Value.Date : q.IssuedDate.Date).AddMonths(months);
            return new QualificationDto
            {
                Id = q.Id, PersonId = q.PersonId, PersonName = person?.Name, StampId = person?.StampId, Kind = q.Kind, Method = q.Method, Level = q.Level,
                Scheme = q.Scheme, Scope = q.Scope, CertificateNumber = q.CertificateNumber, CertificateDocumentId = q.CertificateDocumentId,
                IssuedDate = q.IssuedDate, ExpiryDate = q.ExpiryDate, State = state, StateText = QualificationRules.StateText(state), LastUse = last, LapsesOn = lapses,
                Revoked = q.Revoked, RevokeReason = q.RevokeReason, Superseded = q.Superseded, RenewsQualificationId = q.RenewsQualificationId,
            };
        }

        // -------------------------------------------------------------------------------- persons

        private async Task<QualifiedPersonDto> BuildPersonAsync(QualifiedPerson p, CancellationToken cancellationToken)
        {
            var (months, warn) = await ContinuityAsync(cancellationToken);
            List<UseKey> uses = await LastUsesAsync(null, cancellationToken);
            return new QualifiedPersonDto
            {
                Id = p.Id, Name = p.Name, PersonnelCode = p.PersonnelCode, Employer = p.Employer, ProviderId = p.ProviderId, StampId = p.StampId, UserId = p.UserId,
                IsActive = p.IsActive,
                Qualifications = p.Qualifications.Where(q => !q.Deleted).OrderBy(q => q.Id).Select(q => ToDto(q, p, uses, DateTime.Today, months, warn)).ToList(),
            };
        }

        public async Task<ResponseDto<PagedList<QualifiedPersonDto>>> GetPersonsAsync(QualifiedPersonQuery query, CancellationToken cancellationToken)
        {
            query ??= new QualifiedPersonQuery();
            IQueryable<QualifiedPerson> rows = _uow.Set<QualifiedPerson>().Where(p => !p.Deleted);
            if (query.Employer.HasValue) rows = rows.Where(p => p.Employer == query.Employer.Value);
            if (query.IsActive.HasValue) rows = rows.Where(p => p.IsActive == query.IsActive.Value);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(p => p.Name.Contains(text) || p.PersonnelCode.Contains(text) || p.StampId.Contains(text));
            int total = await rows.CountAsync(cancellationToken);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize;
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<QualifiedPerson> items = await rows.Include(p => p.Qualifications).OrderBy(p => p.Name).Skip((number - 1) * size).Take(size).ToListAsync(cancellationToken);
            var (months, warn) = await ContinuityAsync(cancellationToken);
            List<UseKey> uses = await LastUsesAsync(null, cancellationToken);
            List<QualifiedPersonDto> dtos = items.Select(p => new QualifiedPersonDto
            {
                Id = p.Id, Name = p.Name, PersonnelCode = p.PersonnelCode, Employer = p.Employer, ProviderId = p.ProviderId, StampId = p.StampId, UserId = p.UserId, IsActive = p.IsActive,
                Qualifications = p.Qualifications.Where(q => !q.Deleted).OrderBy(q => q.Id).Select(q => ToDto(q, p, uses, DateTime.Today, months, warn)).ToList(),
            }).ToList();
            PagedList<QualifiedPersonDto> page = await PagedList<QualifiedPersonDto>.CreateAsync(dtos, 1, 0, cancellationToken);
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size; page.TotalPages = (int)Math.Ceiling(total / (double)size);
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<QualifiedPersonDto>> GetPersonAsync(int id, CancellationToken cancellationToken)
        {
            QualifiedPerson p = await _uow.Set<QualifiedPerson>().Include(x => x.Qualifications).FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            return p == null ? QualityText.NotFound<QualifiedPersonDto>("شخص پیدا نشد.") : ResponseDtoHelper.Ok(await BuildPersonAsync(p, cancellationToken));
        }

        public async Task<ResponseDto<QualifiedPersonDto>> SavePersonAsync(SavePersonDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Name) == null || !Enum.IsDefined(typeof(EmployerKind), dto.Employer)) return Bad<QualifiedPersonDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<QualifiedPersonDto>(QualityText.NotAllowed);
            if (dto.Employer != EmployerKind.Own && !dto.ProviderId.HasValue) return Bad<QualifiedPersonDto>("برای پیمانکار یا تامین‌کننده، شرکت را مشخص کنید.");
            if (dto.ProviderId.HasValue && !await _uow.Set<Provider>().AnyAsync(p => p.Id == dto.ProviderId.Value, cancellationToken)) return Bad<QualifiedPersonDto>("شرکت پیدا نشد.");
            string code = Clean(dto.PersonnelCode);
            QualifiedPerson person;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                person = await _uow.Set<QualifiedPerson>().Include(x => x.Qualifications).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (person == null) return QualityText.NotFound<QualifiedPersonDto>("شخص پیدا نشد.");
            }
            else
            {
                person = new QualifiedPerson { CreateDate = now, CreatorUserId = Me, Qualifications = new List<Qualification>() };
                _uow.Set<QualifiedPerson>().Add(person);
            }
            if (code != null && await _uow.Set<QualifiedPerson>().AnyAsync(p => p.PersonnelCode == code && p.Id != person.Id && !p.Deleted, cancellationToken))
                return Bad<QualifiedPersonDto>("کد پرسنلی تکراری است.");
            person.Name = Clean(dto.Name); person.PersonnelCode = code; person.Employer = dto.Employer; person.ProviderId = dto.ProviderId;
            person.StampId = Clean(dto.StampId); person.UserId = dto.UserId; person.IsActive = dto.IsActive;
            person.ModifyDate = now; person.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await BuildPersonAsync(person, cancellationToken));
        }

        // -------------------------------------------------------------------------------- qualifications

        public async Task<ResponseDto<PagedList<QualificationDto>>> GetQualificationsAsync(QualificationQuery query, CancellationToken cancellationToken)
        {
            query ??= new QualificationQuery();
            IQueryable<Qualification> rows = _uow.Set<Qualification>().Include(q => q.Person).Where(q => !q.Deleted);
            if (query.PersonId.HasValue) rows = rows.Where(q => q.PersonId == query.PersonId.Value);
            if (query.Kind.HasValue) rows = rows.Where(q => q.Kind == query.Kind.Value);
            string method = QualityText.Upper(query.Method);
            if (method != null) rows = rows.Where(q => q.Method == method);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(q => q.CertificateNumber.Contains(text) || q.Person.Name.Contains(text) || q.Person.StampId.Contains(text));
            List<Qualification> items = await rows.OrderBy(q => q.Id).ToListAsync(cancellationToken);
            var (months, warn) = await ContinuityAsync(cancellationToken);
            List<UseKey> uses = await LastUsesAsync(null, cancellationToken);
            List<QualificationDto> list = items.Select(q => ToDto(q, q.Person, uses, DateTime.Today, months, warn)).ToList();
            if (query.State.HasValue) list = list.Where(q => q.State == query.State.Value).ToList();
            return ResponseDtoHelper.Ok(await PagedList<QualificationDto>.CreateAsync(list, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<PagedList<QualificationDto>>> GetContinuityAsync(ContinuityQuery query, CancellationToken cancellationToken)
        {
            query ??= new ContinuityQuery();
            int days = query.Days <= 0 ? 30 : query.Days;
            List<Qualification> items = await _uow.Set<Qualification>().Include(q => q.Person)
                .Where(q => !q.Deleted && !q.Revoked && !q.Superseded && q.Person.IsActive && (q.Kind == QualificationKind.Welder || q.Kind == QualificationKind.WeldingOperator))
                .ToListAsync(cancellationToken);
            var (months, warn) = await ContinuityAsync(cancellationToken);
            List<UseKey> uses = await LastUsesAsync(null, cancellationToken);
            DateTime limit = DateTime.Today.AddDays(days);
            List<QualificationDto> list = items.Select(q => ToDto(q, q.Person, uses, DateTime.Today, months, warn))
                .Where(q => q.State == QualificationState.Lapsed || (q.LapsesOn.HasValue && q.LapsesOn.Value <= limit))
                .OrderBy(q => q.LapsesOn).ToList();
            return ResponseDtoHelper.Ok(await PagedList<QualificationDto>.CreateAsync(list, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<QualificationDto>> SaveQualificationAsync(SaveQualificationDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || !Enum.IsDefined(typeof(QualificationKind), dto.Kind)) return Bad<QualificationDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<QualificationDto>(QualityText.NotAllowed);
            string method = QualityText.Upper(dto.Method);
            if (method == null) return Bad<QualificationDto>("فرایند جوشکاری یا روش NDT را وارد کنید (مثلاً GTAW یا UT).");
            if (Clean(dto.CertificateNumber) == null) return Bad<QualificationDto>("شماره گواهی را وارد کنید.");
            if (dto.IssuedDate == default) return Bad<QualificationDto>("تاریخ صدور (یا آزمون) را وارد کنید.");
            if (dto.IssuedDate.Date > DateTime.Today) return Bad<QualificationDto>("تاریخ صدور نمی‌تواند در آینده باشد.");
            if (dto.ExpiryDate.HasValue && dto.ExpiryDate.Value.Date <= dto.IssuedDate.Date) return Bad<QualificationDto>("تاریخ انقضا باید بعد از تاریخ صدور باشد.");
            bool ndt = dto.Kind == QualificationKind.NdtPersonnel;
            if (ndt && (dto.Level != NdtLevel.I && dto.Level != NdtLevel.II && dto.Level != NdtLevel.III)) return Bad<QualificationDto>("سطح NDT (I، II یا III) را مشخص کنید.");
            if (ndt && dto.Scheme != QualificationScheme.AsntSntTc1A && dto.Scheme != QualificationScheme.Iso9712) return Bad<QualificationDto>("طرح گواهی NDT را مشخص کنید (ASNT SNT-TC-1A با رویه نوشتاری، یا ISO 9712).");
            if (!ndt && dto.Level != NdtLevel.None) return Bad<QualificationDto>("سطح فقط برای پرسنل NDT است.");
            QualifiedPerson person = await _uow.Set<QualifiedPerson>().FirstOrDefaultAsync(p => p.Id == dto.PersonId && !p.Deleted, cancellationToken);
            if (person == null) return QualityText.NotFound<QualificationDto>("شخص پیدا نشد.");
            if (!person.IsActive) return Bad<QualificationDto>("شخص غیرفعال است.");
            if (await _uow.Set<Qualification>().AnyAsync(q => q.PersonId == person.Id && q.CertificateNumber == dto.CertificateNumber.Trim() && !q.Deleted, cancellationToken))
                return Bad<QualificationDto>("این شماره گواهی برای این شخص قبلاً ثبت شده است.");
            Qualification old = null;
            if (dto.RenewsQualificationId.HasValue)
            {
                old = await _uow.Set<Qualification>().FirstOrDefaultAsync(q => q.Id == dto.RenewsQualificationId.Value && !q.Deleted, cancellationToken);
                if (old == null || old.PersonId != person.Id || old.Kind != dto.Kind || old.Method != method) return Bad<QualificationDto>("گواهی قبلی برای تمدید با این شخص و روش هم‌خوان نیست.");
                if (old.Revoked) return Bad<QualificationDto>("گواهی ابطال‌شده تمدید نمی‌شود؛ آزمون جدید به عنوان گواهی تازه ثبت شود.");
                if (dto.IssuedDate.Date <= old.IssuedDate.Date) return Bad<QualificationDto>("تاریخ آزمون جدید باید بعد از گواهی قبلی باشد.");
            }
            DateTimeOffset now = DateTimeOffset.Now;
            var q2 = new Qualification
            {
                PersonId = person.Id, Kind = dto.Kind, Method = method, Level = ndt ? dto.Level : NdtLevel.None,
                Scheme = ndt ? dto.Scheme : dto.Kind == QualificationKind.Inspector ? QualificationScheme.Other : QualificationScheme.AsmeIX,
                Scope = Clean(dto.Scope), CertificateNumber = dto.CertificateNumber.Trim(), CertificateDocumentId = dto.CertificateDocumentId,
                IssuedDate = dto.IssuedDate.Date, ExpiryDate = dto.ExpiryDate?.Date, RenewsQualificationId = old?.Id,
                CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            if (old != null) { old.Superseded = true; old.ModifyDate = now; old.ModifierUserId = Me; }
            _uow.Set<Qualification>().Add(q2);
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetQualificationAsync(q2.Id, cancellationToken);
        }

        private async Task<ResponseDto<QualificationDto>> GetQualificationAsync(int id, CancellationToken cancellationToken)
        {
            Qualification q = await _uow.Set<Qualification>().Include(x => x.Person).FirstAsync(x => x.Id == id, cancellationToken);
            var (months, warn) = await ContinuityAsync(cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(q, q.Person, await LastUsesAsync(null, cancellationToken), DateTime.Today, months, warn));
        }

        public async Task<ResponseDto<QualificationDto>> QualificationActionAsync(QualificationActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<QualificationDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<QualificationDto>(QualityText.NotAllowed);
            Qualification q = await _uow.Set<Qualification>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (q == null) return QualityText.NotFound<QualificationDto>("گواهی پیدا نشد.");
            if (dto.Action.Trim().ToLowerInvariant() != "revoke") return Bad<QualificationDto>("عملیات نامعتبر است.");
            if (q.Revoked) return Bad<QualificationDto>("گواهی قبلاً ابطال شده است.");
            if (Clean(dto.Reason) == null) return Bad<QualificationDto>("دلیل ابطال را بنویسید.");
            q.Revoked = true; q.RevokeReason = Clean(dto.Reason); q.ModifyDate = DateTimeOffset.Now; q.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetQualificationAsync(q.Id, cancellationToken);
        }

        // -------------------------------------------------------------------------------- WPS and PQR

        private async Task<WpsDto> BuildWpsAsync(Wps w, CancellationToken cancellationToken)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow,
                new int?[] { w.QcApprovedByUserId, w.EngineeringApprovedByUserId }.Concat(w.Pqrs.Select(p => p.Pqr.ApprovedByUserId)), cancellationToken);
            return new WpsDto
            {
                Id = w.Id, Number = w.Number, Revision = w.Revision, Process = w.Process, BasePNo = w.BasePNo, Status = w.Status, PqrCount = w.Pqrs.Count,
                FillerFNo = w.FillerFNo, FillerANo = w.FillerANo, ThicknessMin = w.ThicknessMin, ThicknessMax = w.ThicknessMax, Positions = w.Positions,
                PreheatMinC = w.PreheatMinC, InterpassMaxC = w.InterpassMaxC, Pwht = w.Pwht, ShieldingGas = w.ShieldingGas, Note = w.Note,
                QcApprovedAt = w.QcApprovedAt, QcApprovedByName = names.Name(w.QcApprovedByUserId),
                EngineeringApprovedAt = w.EngineeringApprovedAt, EngineeringApprovedByName = names.Name(w.EngineeringApprovedByUserId),
                Pqrs = w.Pqrs.Select(p => ToDto(p.Pqr, names)).ToList(),
            };
        }

        private static PqrDto ToDto(Pqr p, Dictionary<int, string> names) => new PqrDto
        {
            Id = p.Id, Number = p.Number, CodeEdition = p.CodeEdition, Process = p.Process, ResultsDocumentId = p.ResultsDocumentId, Note = p.Note,
            Approved = p.Approved, ApprovedAt = p.ApprovedAt, ApprovedByName = names.Name(p.ApprovedByUserId),
        };

        public async Task<ResponseDto<PagedList<WpsListDto>>> GetWpsListAsync(WpsQuery query, CancellationToken cancellationToken)
        {
            query ??= new WpsQuery();
            IQueryable<Wps> rows = _uow.Set<Wps>().Where(w => !w.Deleted);
            if (query.Status.HasValue) rows = rows.Where(w => w.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(w => w.Number.Contains(text) || w.Process.Contains(text));
            IQueryable<WpsListDto> projection = rows.OrderBy(w => w.Number).ThenBy(w => w.Revision).Select(w => new WpsListDto
            {
                Id = w.Id, Number = w.Number, Revision = w.Revision, Process = w.Process, BasePNo = w.BasePNo, Status = w.Status, PqrCount = w.Pqrs.Count,
            });
            return ResponseDtoHelper.Ok(await PagedList<WpsListDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<WpsDto>> GetWpsAsync(int id, CancellationToken cancellationToken)
        {
            Wps w = await _uow.Set<Wps>().Include(x => x.Pqrs).ThenInclude(p => p.Pqr).FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            return w == null ? QualityText.NotFound<WpsDto>("WPS پیدا نشد.") : ResponseDtoHelper.Ok(await BuildWpsAsync(w, cancellationToken));
        }

        public async Task<ResponseDto<WpsDto>> SaveWpsAsync(SaveWpsDto dto, CancellationToken cancellationToken)
        {
            string process = dto == null ? null : QualityText.Upper(dto.Process);
            if (dto == null || Clean(dto.Number) == null || Clean(dto.Revision) == null || process == null) return Bad<WpsDto>("شماره، بازنگری و فرایند WPS را وارد کنید.");
            if (!_user.IsInRole(QualityRoles.Planners)) return Bad<WpsDto>(QualityText.NotAllowed);
            if (dto.ThicknessMin.HasValue && dto.ThicknessMax.HasValue && dto.ThicknessMin.Value > dto.ThicknessMax.Value) return Bad<WpsDto>("حداقل ضخامت از حداکثر بیشتر است.");
            List<int> pqrIds = (dto.PqrIds ?? new List<int>()).Distinct().ToList();
            List<Pqr> pqrs = await _uow.Set<Pqr>().Where(p => pqrIds.Contains(p.Id) && !p.Deleted).ToListAsync(cancellationToken);
            if (pqrs.Count != pqrIds.Count) return Bad<WpsDto>("PQR انتخاب‌شده پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            Wps w;
            if (dto.Id.HasValue)
            {
                w = await _uow.Set<Wps>().Include(x => x.Pqrs).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (w == null) return QualityText.NotFound<WpsDto>("WPS پیدا نشد.");
                if (w.Status != WpsStatus.Draft) return Bad<WpsDto>("WPS تاییدشده ویرایش نمی‌شود؛ بازنگری جدید ثبت کنید.");
            }
            else
            {
                w = new Wps { Status = WpsStatus.Draft, CreateDate = now, CreatorUserId = Me, Pqrs = new List<WpsPqr>() };
                _uow.Set<Wps>().Add(w);
            }
            string number = dto.Number.Trim(), revision = dto.Revision.Trim();
            if (await _uow.Set<Wps>().AnyAsync(x => x.Number == number && x.Revision == revision && x.Id != w.Id && !x.Deleted, cancellationToken))
                return Bad<WpsDto>("این شماره و بازنگری WPS قبلاً ثبت شده است.");
            w.Number = number; w.Revision = revision; w.Process = process; w.BasePNo = Clean(dto.BasePNo); w.FillerFNo = Clean(dto.FillerFNo);
            w.FillerANo = Clean(dto.FillerANo); w.ThicknessMin = dto.ThicknessMin; w.ThicknessMax = dto.ThicknessMax; w.Positions = Clean(dto.Positions);
            w.PreheatMinC = dto.PreheatMinC; w.InterpassMaxC = dto.InterpassMaxC; w.Pwht = Clean(dto.Pwht); w.ShieldingGas = Clean(dto.ShieldingGas); w.Note = Clean(dto.Note);
            w.QcApprovedAt = null; w.QcApprovedByUserId = null; w.EngineeringApprovedAt = null; w.EngineeringApprovedByUserId = null;
            w.Pqrs.Clear();
            foreach (Pqr p in pqrs) w.Pqrs.Add(new WpsPqr { PqrId = p.Id });
            w.ModifyDate = now; w.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetWpsAsync(w.Id, cancellationToken);
        }

        public async Task<ResponseDto<WpsDto>> WpsActionAsync(WpsActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<WpsDto>(QualityText.InvalidData);
            Wps w = await _uow.Set<Wps>().Include(x => x.Pqrs).ThenInclude(p => p.Pqr).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (w == null) return QualityText.NotFound<WpsDto>("WPS پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "approve-qc":
                case "approve-engineering":
                    bool qc = dto.Action.Trim().ToLowerInvariant() == "approve-qc";
                    if (!_user.IsInRole(qc ? QualityRoles.Inspectors : QualityRoles.Engineering)) return Bad<WpsDto>(QualityText.NotAllowed + (qc ? " (تایید کیفی را QC انجام می‌دهد)" : " (تایید فنی را مهندسی انجام می‌دهد)"));
                    if (w.Status != WpsStatus.Draft) return Bad<WpsDto>("فقط WPS پیش‌نویس تایید می‌شود.");
                    if (!w.Pqrs.Any(p => p.Pqr.Approved)) return Bad<WpsDto>("WPS باید حداقل یک PQR تاییدشده داشته باشد.");
                    if (w.CreatorUserId == Me) return Bad<WpsDto>(QualityText.SameUser);
                    if (qc ? w.QcApprovedByUserId.HasValue : w.EngineeringApprovedByUserId.HasValue) return Bad<WpsDto>("این تایید قبلاً ثبت شده است.");
                    int? other = qc ? w.EngineeringApprovedByUserId : w.QcApprovedByUserId;
                    if (other == Me) return Bad<WpsDto>(QualityText.SameUser + " (تایید کیفی و فنی باید توسط دو نفر متفاوت باشد)");
                    if (qc) { w.QcApprovedAt = now; w.QcApprovedByUserId = Me; } else { w.EngineeringApprovedAt = now; w.EngineeringApprovedByUserId = Me; }
                    if (w.QcApprovedByUserId.HasValue && w.EngineeringApprovedByUserId.HasValue) w.Status = WpsStatus.Approved;
                    break;
                case "obsolete":
                    if (!_user.IsInRole(QualityRoles.Planners)) return Bad<WpsDto>(QualityText.NotAllowed);
                    if (w.Status == WpsStatus.Obsolete) return Bad<WpsDto>("WPS قبلاً منسوخ شده است.");
                    w.Status = WpsStatus.Obsolete;
                    break;
                default:
                    return Bad<WpsDto>("عملیات نامعتبر است.");
            }
            w.ModifyDate = now; w.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetWpsAsync(w.Id, cancellationToken);
        }

        public async Task<ResponseDto<List<PqrDto>>> GetPqrsAsync(CancellationToken cancellationToken)
        {
            List<Pqr> rows = await _uow.Set<Pqr>().Where(p => !p.Deleted).OrderBy(p => p.Number).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(p => p.ApprovedByUserId), cancellationToken);
            return ResponseDtoHelper.Ok(rows.Select(p => ToDto(p, names)).ToList());
        }

        public async Task<ResponseDto<PqrDto>> SavePqrAsync(SavePqrDto dto, CancellationToken cancellationToken)
        {
            string process = dto == null ? null : QualityText.Upper(dto.Process);
            if (dto == null || Clean(dto.Number) == null || process == null) return Bad<PqrDto>("شماره و فرایند PQR را وارد کنید.");
            if (!_user.IsInRole(QualityRoles.Planners)) return Bad<PqrDto>(QualityText.NotAllowed);
            DateTimeOffset now = DateTimeOffset.Now;
            Pqr p;
            if (dto.Id.HasValue)
            {
                p = await _uow.Set<Pqr>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (p == null) return QualityText.NotFound<PqrDto>("PQR پیدا نشد.");
                if (p.Approved) return Bad<PqrDto>("PQR تاییدشده ویرایش نمی‌شود.");
            }
            else
            {
                p = new Pqr { CreateDate = now, CreatorUserId = Me };
                _uow.Set<Pqr>().Add(p);
            }
            string number = dto.Number.Trim();
            if (await _uow.Set<Pqr>().AnyAsync(x => x.Number == number && x.Id != p.Id && !x.Deleted, cancellationToken)) return Bad<PqrDto>("شماره PQR تکراری است.");
            p.Number = number; p.CodeEdition = Clean(dto.CodeEdition); p.Process = process; p.ResultsDocumentId = dto.ResultsDocumentId; p.Note = Clean(dto.Note);
            p.ModifyDate = now; p.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(p, await InventoryUsers.GetNamesAsync(_uow, new int?[] { p.ApprovedByUserId }, cancellationToken)));
        }

        public async Task<ResponseDto<PqrDto>> PqrActionAsync(PqrActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PqrDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.Engineering)) return Bad<PqrDto>(QualityText.NotAllowed + " (PQR را مهندسی تایید می‌کند)");
            Pqr p = await _uow.Set<Pqr>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (p == null) return QualityText.NotFound<PqrDto>("PQR پیدا نشد.");
            if (dto.Action.Trim().ToLowerInvariant() != "approve") return Bad<PqrDto>("عملیات نامعتبر است.");
            if (p.Approved) return Bad<PqrDto>("PQR قبلاً تایید شده است.");
            if (p.CreatorUserId == Me) return Bad<PqrDto>(QualityText.SameUser);
            if (p.ResultsDocumentId == null) return Bad<PqrDto>("سند نتایج آزمون PQR را پیوست کنید.");
            p.Approved = true; p.ApprovedAt = DateTimeOffset.Now; p.ApprovedByUserId = Me; p.ModifyDate = DateTimeOffset.Now; p.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(p, await InventoryUsers.GetNamesAsync(_uow, new int?[] { p.ApprovedByUserId }, cancellationToken)));
        }

        // -------------------------------------------------------------------------------- weld log

        private async Task<List<WeldLogDto>> LoadLogsAsync(IQueryable<WeldLog> rows, CancellationToken cancellationToken)
        {
            List<WeldLog> logs = await rows.Include(l => l.WorkOrder).Include(l => l.Wps).Include(l => l.Qualification).ThenInclude(q => q.Person)
                .Include(l => l.Reports).ThenInclude(r => r.TestReport).AsSplitQuery().ToListAsync(cancellationToken);
            return logs.Select(l => new WeldLogDto
            {
                Id = l.Id, WeldDate = l.WeldDate, WorkOrderId = l.WorkOrderId, WorkOrderCode = l.WorkOrder?.Code, ItemId = l.ItemId, SerialNumber = l.SerialNumber,
                JointNo = l.JointNo, WpsId = l.WpsId, WpsNumber = l.Wps == null ? null : l.Wps.Number + "/" + l.Wps.Revision, QualificationId = l.QualificationId,
                WelderName = l.Qualification?.Person?.Name, WelderStamp = l.Qualification?.Person?.StampId, Process = l.Qualification?.Method,
                FillerBatch = l.FillerBatch, PreheatC = l.PreheatC, InterpassC = l.InterpassC, Vt = l.Vt, Note = l.Note,
                ReportIds = l.Reports.Select(r => r.TestReportId).ToList(), ReportNumbers = l.Reports.Select(r => r.TestReport?.Number).ToList(),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<WeldLogDto>>> GetWeldLogsAsync(WeldLogQuery query, CancellationToken cancellationToken)
        {
            query ??= new WeldLogQuery();
            IQueryable<WeldLog> rows = _uow.Set<WeldLog>().Where(l => !l.Deleted);
            if (query.WorkOrderId.HasValue) rows = rows.Where(l => l.WorkOrderId == query.WorkOrderId.Value);
            if (query.PersonId.HasValue) rows = rows.Where(l => l.Qualification.PersonId == query.PersonId.Value);
            string serial = Clean(query.SerialNumber);
            if (serial != null) rows = rows.Where(l => l.SerialNumber == serial);
            int total = await rows.CountAsync(cancellationToken);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize;
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<WeldLogDto> list = await LoadLogsAsync(rows.OrderByDescending(l => l.WeldDate).ThenByDescending(l => l.Id).Skip((number - 1) * size).Take(size), cancellationToken);
            list = list.OrderByDescending(l => l.WeldDate).ThenByDescending(l => l.Id).ToList();
            PagedList<WeldLogDto> page = await PagedList<WeldLogDto>.CreateAsync(list, 1, 0, cancellationToken);
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size; page.TotalPages = (int)Math.Ceiling(total / (double)size);
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<WeldLogDto>> SaveWeldLogAsync(SaveWeldLogDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.JointNo) == null) return Bad<WeldLogDto>("شماره اتصال جوش را وارد کنید.");
            if (!_user.IsInRole(QualityRoles.Inspectors)) return Bad<WeldLogDto>(QualityText.NotAllowed + " (گزارش جوشکاری را بازرس QC ثبت می‌کند)");
            DateTime date = (dto.WeldDate ?? DateTime.Today).Date;
            if (date > DateTime.Today) return Bad<WeldLogDto>("تاریخ جوشکاری نمی‌تواند در آینده باشد.");
            if (!Enum.IsDefined(typeof(VtResult), dto.Vt)) return Bad<WeldLogDto>(QualityText.InvalidData);
            if (!await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId && !w.Deleted, cancellationToken)) return Bad<WeldLogDto>("دستور کار پیدا نشد.");
            Wps wps = await _uow.Set<Wps>().FirstOrDefaultAsync(w => w.Id == dto.WpsId && !w.Deleted, cancellationToken);
            if (wps == null) return Bad<WeldLogDto>("WPS پیدا نشد.");
            if (wps.Status != WpsStatus.Approved) return Bad<WeldLogDto>("WPS انتخاب‌شده تایید نهایی (کیفی و فنی) نشده است.");
            Qualification q = await _uow.Set<Qualification>().Include(x => x.Person).FirstOrDefaultAsync(x => x.Id == dto.QualificationId && !x.Deleted, cancellationToken);
            if (q == null) return Bad<WeldLogDto>("گواهی جوشکار پیدا نشد.");
            if (q.Kind != QualificationKind.Welder && q.Kind != QualificationKind.WeldingOperator) return Bad<WeldLogDto>("گواهی انتخاب‌شده گواهی جوشکار نیست.");
            if (q.Method != wps.Process) return Bad<WeldLogDto>($"فرایند گواهی جوشکار ({q.Method}) با فرایند WPS ({wps.Process}) هم‌خوان نیست.");
            if (!q.Person.IsActive) return Bad<WeldLogDto>("جوشکار غیرفعال است.");
            var (months, warn) = await ContinuityAsync(cancellationToken);
            List<UseKey> uses = await LastUsesAsync(date, cancellationToken);
            QualificationState state = QualificationRules.State(q, date, LastUse(uses, q), months, warn);
            if (!QualificationRules.Usable(state)) return Bad<WeldLogDto>($"گواهی جوشکار در تاریخ {Services.NasbNiro.Finance.FiscalCalendars.Text(date)} قابل استفاده نیست: {QualificationRules.StateText(state)}.");
            if (dto.PreheatC.HasValue && wps.PreheatMinC.HasValue && dto.PreheatC.Value < wps.PreheatMinC.Value) return Bad<WeldLogDto>($"دمای پیش‌گرم ({dto.PreheatC}) کمتر از حداقل WPS ({wps.PreheatMinC}) است.");
            if (dto.InterpassC.HasValue && wps.InterpassMaxC.HasValue && dto.InterpassC.Value > wps.InterpassMaxC.Value) return Bad<WeldLogDto>($"دمای بین‌پاسی ({dto.InterpassC}) بیشتر از حداکثر WPS ({wps.InterpassMaxC}) است.");
            List<int> reportIds = (dto.ReportIds ?? new List<int>()).Distinct().ToList();
            if (reportIds.Count > 0 && await _uow.Set<TestReport>().CountAsync(r => reportIds.Contains(r.Id) && !r.Deleted, cancellationToken) != reportIds.Count)
                return Bad<WeldLogDto>("گزارش NDT انتخاب‌شده پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            var log = new WeldLog
            {
                WeldDate = date, WorkOrderId = dto.WorkOrderId, ItemId = dto.ItemId, SerialNumber = Clean(dto.SerialNumber), JointNo = Clean(dto.JointNo), WpsId = wps.Id,
                QualificationId = q.Id, FillerBatch = Clean(dto.FillerBatch), PreheatC = dto.PreheatC, InterpassC = dto.InterpassC, Vt = dto.Vt, Note = Clean(dto.Note),
                Reports = reportIds.Select(id => new WeldLogReport { TestReportId = id }).ToList(),
                CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<WeldLog>().Add(log);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok((await LoadLogsAsync(_uow.Set<WeldLog>().Where(l => l.Id == log.Id), cancellationToken))[0]);
        }
    }
}
