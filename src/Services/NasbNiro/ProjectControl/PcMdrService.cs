using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.ProjectControl
{
    public interface IPcMdrService
    {
        /// <summary>Adds (without saving) the MDR lines of a work order from the template of its product type. Returns the number of lines added.</summary>
        Task<int> DraftMdrNoSaveAsync(int contractId, int workOrderId, ItpProductType productType, CancellationToken cancellationToken);

        Task<ResponseDto<PcMdrDraftResultDto>> DraftMdrAsync(PcMdrDraftDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcDeliverableDto>>> GetDeliverablesAsync(PcDeliverableQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcDeliverableDto>> SaveDeliverableAsync(PcDeliverableDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcDeliverableDto>> DeliverableActionAsync(PcDeliverableActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcMdrTemplateDto>>> GetTemplatesAsync(PaginationParams query, CancellationToken cancellationToken);
        Task<ResponseDto<PcMdrTemplateDto>> SaveTemplateAsync(PcMdrTemplateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcResponseCodeDto>>> GetResponseCodesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<List<PcResponseCodeDto>>> SaveResponseCodeAsync(PcResponseCodeDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcTransmittalDto>>> GetTransmittalsAsync(PcTransmittalQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcTransmittalDto>> GetTransmittalAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PcTransmittalDto>> SaveTransmittalAsync(PcTransmittalDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcTransmittalDto>> RecordResponseAsync(PcTransmittalResponseDto dto, CancellationToken cancellationToken);
        Task<int> OverdueResponsesAsync(int? contractId, CancellationToken cancellationToken);

        Task<ResponseDto<PcFinalBookDto>> GenerateFinalBookAsync(PcFinalBookRequestDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcFinalBookDto>>> GetFinalBooksAsync(PcFinalBookQuery query, CancellationToken cancellationToken);
    }

    /// <summary>MDR (the company register of documents), MDL (the client subset), transmittals, client response codes and the final books.</summary>
    public class PcMdrService : IPcMdrService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IReleaseService _release;

        public PcMdrService(IUnitOfWork uow, IInventoryUserContext user, IReleaseService release)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _release = release ?? throw new ArgumentNullException(nameof(release));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => PcText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => PcText.Bad<T>(m);

        // ============================================================================ response codes and templates (data, not code)

        private async Task EnsureResponseCodesAsync(CancellationToken cancellationToken)
        {
            if (await _uow.Set<PcClientResponseCode>().AnyAsync(cancellationToken)) return;
            _uow.Set<PcClientResponseCode>().AddRange(new[]
            {
                new PcClientResponseCode { Code = "A", Title = "Approved", CountsAsApproved = true, RequiresResubmit = false, Source = "★ company decision (common document review codes)" },
                new PcClientResponseCode { Code = "B", Title = "Approved with comments", CountsAsApproved = true, RequiresResubmit = false, Source = "★ company decision" },
                new PcClientResponseCode { Code = "C", Title = "Revise and resubmit", CountsAsApproved = false, RequiresResubmit = true, Source = "★ company decision" },
                new PcClientResponseCode { Code = "D", Title = "Rejected", CountsAsApproved = false, RequiresResubmit = true, Source = "★ company decision" },
                new PcClientResponseCode { Code = "I", Title = "For information", CountsAsApproved = false, RequiresResubmit = false, Source = "★ company decision" },
            });
            await _uow.SaveChangesAsync(cancellationToken);
        }

        public async Task<ResponseDto<List<PcResponseCodeDto>>> GetResponseCodesAsync(CancellationToken cancellationToken)
        {
            await EnsureResponseCodesAsync(cancellationToken);
            List<PcResponseCodeDto> list = await _uow.Set<PcClientResponseCode>().AsNoTracking().OrderBy(c => c.Code).Select(c => new PcResponseCodeDto
                { Id = c.Id, Code = c.Code, Title = c.Title, CountsAsApproved = c.CountsAsApproved, RequiresResubmit = c.RequiresResubmit, Source = c.Source }).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<List<PcResponseCodeDto>>> SaveResponseCodeAsync(PcResponseCodeDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Code) == null || Clean(dto.Title) == null) return Bad<List<PcResponseCodeDto>>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.DccOnly)) return Bad<List<PcResponseCodeDto>>(PcText.NotAllowed);
            await EnsureResponseCodesAsync(cancellationToken);
            string code = PcText.Upper(dto.Code);
            if (code.Length > 5) return Bad<List<PcResponseCodeDto>>("کد پاسخ حداکثر پنج نویسه است.");
            PcClientResponseCode row = dto.Id.HasValue ? await _uow.Set<PcClientResponseCode>().FirstOrDefaultAsync(c => c.Id == dto.Id.Value, cancellationToken) : null;
            if (dto.Id.HasValue && row == null) return PcText.NotFound<List<PcResponseCodeDto>>("کد پاسخ پیدا نشد.");
            if (await _uow.Set<PcClientResponseCode>().AnyAsync(c => c.Code == code && c.Id != (dto.Id ?? 0), cancellationToken)) return Bad<List<PcResponseCodeDto>>("این کد قبلاً تعریف شده است.");
            if (row == null) { row = new PcClientResponseCode(); _uow.Set<PcClientResponseCode>().Add(row); }
            row.Code = code; row.Title = Clean(dto.Title); row.CountsAsApproved = dto.CountsAsApproved; row.RequiresResubmit = dto.RequiresResubmit; row.Source = Clean(dto.Source);
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetResponseCodesAsync(cancellationToken);
        }

        private const string Verify = " (to be verified against licensed copy)";

        private static PcMdrTemplate Template(string code, string title, ItpProductType type, string standard, string note, params (string DocType, string Discipline, string Title, bool Client)[] lines)
        {
            var t = new PcMdrTemplate { Code = code, Title = title, ProductType = type, StandardSource = standard + Verify, Note = note, IsActive = true, CreateDate = DateTimeOffset.Now, Lines = new List<PcMdrTemplateLine>() };
            int seq = 0;
            foreach (var l in lines)
                t.Lines.Add(new PcMdrTemplateLine { Seq = ++seq, DocumentType = l.DocType, Discipline = l.Discipline, Title = l.Title, ClientSubmittal = l.Client, Required = true, StandardSource = standard + Verify });
            return t;
        }

        private async Task EnsureTemplatesAsync(CancellationToken cancellationToken)
        {
            if (await _uow.Set<PcMdrTemplate>().AnyAsync(cancellationToken)) return;
            const string star = "★ typical items only; engineering must confirm the list. No clause numbers are quoted.";
            var list = new List<PcMdrTemplate>
            {
                Template("MDR-TURBINE", "Steam turbine - typical document register", ItpProductType.SteamTurbine, "API 611 / API 612", star,
                    ("DWG", "Mechanical", "General arrangement and outline drawing", true), ("DWG", "Mechanical", "Foundation loads and anchor bolt plan", true), ("DS", "Mechanical", "Turbine data sheet", true),
                    ("DWG", "Mechanical", "Casing and nozzle assembly drawings", true), ("DWG", "Mechanical", "Rotor assembly and blading drawings", true), ("CAL", "Mechanical", "Rotor dynamics: lateral and torsional analysis", true),
                    ("CAL", "Mechanical", "Bearing and thrust bearing design calculation", false), ("CAL", "Thermal", "Steam path and performance calculation", true), ("DWG", "Instrument", "Governing and control system description and P&ID", true),
                    ("DWG", "Instrument", "Instrument list and wiring diagrams", true), ("PRO", "Welding", "Welding procedure specifications and qualification records", true), ("PRO", "Quality", "Non-destructive examination procedures", true),
                    ("ITP", "Quality", "Inspection and test plan", true), ("PRO", "Test", "Mechanical running and performance test procedure", true), ("PRO", "Production", "Balancing procedure", false),
                    ("PRO", "Production", "Painting and surface preparation specification", true), ("PRO", "Logistics", "Preservation, packing and shipping procedure", true), ("MAN", "Mechanical", "Installation, operation and maintenance manual", true),
                    ("LST", "Mechanical", "Spare parts list and recommended spares", true), ("LST", "Quality", "Material certificate list", false)),
                Template("MDR-PUMP", "Pump - typical document register", ItpProductType.Pump, "API 610", star,
                    ("DS", "Mechanical", "Pump data sheet", true), ("DWG", "Mechanical", "General arrangement and nozzle loads", true), ("DWG", "Mechanical", "Cross-sectional drawing and parts list", true),
                    ("CUR", "Hydraulic", "Performance curve", true), ("CAL", "Mechanical", "Shaft, bearing and seal selection calculation", false), ("DWG", "Mechanical", "Seal and piping plan", true),
                    ("PRO", "Welding", "Welding procedure specifications", true), ("ITP", "Quality", "Inspection and test plan", true), ("PRO", "Test", "Hydrostatic and performance test procedure", true),
                    ("PRO", "Logistics", "Preservation and packing procedure", true), ("MAN", "Mechanical", "Installation, operation and maintenance manual", true), ("LST", "Mechanical", "Spare parts list", true)),
                Template("MDR-LUBE", "Lube oil system - typical document register", ItpProductType.LubeOilSystem, "API 614", star,
                    ("DS", "Process", "Lube oil system data sheet", true), ("DWG", "Process", "Piping and instrument diagram", true), ("DWG", "Mechanical", "General arrangement and skid outline", true),
                    ("CAL", "Process", "Oil flow, pressure drop and cooler sizing calculation", true), ("DWG", "Piping", "Piping isometrics and support drawings", false), ("DS", "Instrument", "Instrument data sheets and loop diagrams", true),
                    ("PRO", "Production", "Flushing and cleanliness procedure", true), ("ITP", "Quality", "Inspection and test plan", true), ("PRO", "Test", "Functional test procedure", true), ("MAN", "Mechanical", "Operation and maintenance manual", true)),
                Template("MDR-CONDENSER", "Condenser (pressure vessel) - typical document register", ItpProductType.Condenser, "ASME Section VIII Division 1 / HEI", star,
                    ("DS", "Process", "Condenser data sheet", true), ("CAL", "Mechanical", "Pressure part design calculation", true), ("DWG", "Mechanical", "General arrangement and nozzle schedule", true),
                    ("DWG", "Mechanical", "Fabrication drawings: shell, tube sheets, tubes", true), ("CAL", "Thermal", "Thermal and hydraulic rating", true), ("PRO", "Welding", "Welding procedure specifications and qualification records", true),
                    ("PRO", "Quality", "Non-destructive examination procedures", true), ("ITP", "Quality", "Inspection and test plan", true), ("PRO", "Test", "Hydrostatic test procedure", true),
                    ("PRO", "Logistics", "Preservation and shipping procedure", true), ("MAN", "Mechanical", "Installation, operation and maintenance manual", true)),
                Template("MDR-ORC", "ORC unit - no register defined yet", ItpProductType.OrcUnit, "(none)", "No document register is defined for ORC units yet. Add lines here when engineering decides them."),
                Template("MDR-VALVE", "Valve - no register defined yet", ItpProductType.Valve, "(none)", "No document register is defined for valves yet. Add lines here when engineering decides them."),
            };
            _uow.Set<PcMdrTemplate>().AddRange(list);
            await _uow.SaveChangesAsync(cancellationToken);
        }

        public async Task<ResponseDto<PagedList<PcMdrTemplateDto>>> GetTemplatesAsync(PaginationParams query, CancellationToken cancellationToken)
        {
            query ??= new PaginationParams();
            await EnsureTemplatesAsync(cancellationToken);
            IQueryable<PcMdrTemplate> q = _uow.Set<PcMdrTemplate>().AsNoTracking().Include(t => t.Lines).Where(t => !t.Deleted).OrderBy(t => t.Id).AsSplitQuery();
            PagedList<PcMdrTemplate> page = await PagedList<PcMdrTemplate>.CreateAsync(q, query.PageNumber, query.PageSize, cancellationToken);
            List<PcMdrTemplateDto> dtos = page.Select(TemplateDto).ToList();
            return ResponseDtoHelper.Ok((PagedList<PcMdrTemplateDto>)new PcPage<PcMdrTemplateDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        private static PcMdrTemplateDto TemplateDto(PcMdrTemplate t) => new PcMdrTemplateDto
        {
            Id = t.Id, Code = t.Code, Title = t.Title, ProductType = t.ProductType, StandardSource = t.StandardSource, Note = t.Note, IsActive = t.IsActive,
            Lines = (t.Lines ?? new List<PcMdrTemplateLine>()).OrderBy(l => l.Seq).Select(l => new PcMdrTemplateLineDto
                { Id = l.Id, Seq = l.Seq, DocumentType = l.DocumentType, Discipline = l.Discipline, Title = l.Title, ClientSubmittal = l.ClientSubmittal, Required = l.Required, StandardSource = l.StandardSource }).ToList(),
        };

        public async Task<ResponseDto<PcMdrTemplateDto>> SaveTemplateAsync(PcMdrTemplateDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Code) == null || Clean(dto.Title) == null) return Bad<PcMdrTemplateDto>(PcText.InvalidData);
            if (!_user.IsInRole("ENGINEERING,DCC,PMO")) return Bad<PcMdrTemplateDto>(PcText.NotAllowed);
            if (!Enum.IsDefined(typeof(ItpProductType), dto.ProductType)) return Bad<PcMdrTemplateDto>("نوع محصول را مشخص کنید.");
            if (dto.Lines == null) dto.Lines = new List<PcMdrTemplateLineDto>();
            if (dto.Lines.Any(l => Clean(l.Title) == null)) return Bad<PcMdrTemplateDto>("عنوان همه ردیف‌ها لازم است.");
            if (dto.Lines.Any(l => Clean(l.StandardSource) == null && Clean(dto.StandardSource) == null)) return Bad<PcMdrTemplateDto>("منبع استاندارد هر ردیف را مشخص کنید.");
            await EnsureTemplatesAsync(cancellationToken);
            string code = PcText.Upper(dto.Code);
            if (await _uow.Set<PcMdrTemplate>().AnyAsync(t => t.Code == code && !t.Deleted && t.Id != (dto.Id ?? 0), cancellationToken)) return Bad<PcMdrTemplateDto>("این کد قالب قبلاً وجود دارد.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcMdrTemplate t;
            if (dto.Id.HasValue)
            {
                t = await _uow.Set<PcMdrTemplate>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (t == null) return PcText.NotFound<PcMdrTemplateDto>("قالب پیدا نشد.");
                _uow.Set<PcMdrTemplateLine>().RemoveRange(t.Lines);
                t.Lines = new List<PcMdrTemplateLine>();
            }
            else
            {
                t = new PcMdrTemplate { CreateDate = now, CreatorUserId = Me, Lines = new List<PcMdrTemplateLine>() };
                _uow.Set<PcMdrTemplate>().Add(t);
            }
            t.Code = code; t.Title = Clean(dto.Title); t.ProductType = dto.ProductType; t.StandardSource = Clean(dto.StandardSource); t.Note = Clean(dto.Note); t.IsActive = dto.IsActive;
            int seq = 0;
            foreach (PcMdrTemplateLineDto l in dto.Lines)
                t.Lines.Add(new PcMdrTemplateLine { Seq = ++seq, DocumentType = Clean(l.DocumentType), Discipline = Clean(l.Discipline), Title = Clean(l.Title), ClientSubmittal = l.ClientSubmittal, Required = l.Required, StandardSource = Clean(l.StandardSource) ?? t.StandardSource });
            t.ModifyDate = now; t.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(TemplateDto(t));
        }

        // ============================================================================ MDR lines

        public async Task<int> DraftMdrNoSaveAsync(int contractId, int workOrderId, ItpProductType productType, CancellationToken cancellationToken)
        {
            await EnsureTemplatesAsync(cancellationToken);
            if (await _uow.Set<PcEngineeringDeliverable>().AnyAsync(d => d.WorkOrderId == workOrderId && !d.Deleted, cancellationToken)) return 0;
            PcMdrTemplate t = await _uow.Set<PcMdrTemplate>().AsNoTracking().Include(x => x.Lines).Where(x => x.ProductType == productType && x.IsActive && !x.Deleted).OrderBy(x => x.Id).AsSplitQuery().FirstOrDefaultAsync(cancellationToken);
            if (t == null || t.Lines.Count == 0) return 0;
            PcContractTerms terms = await PcContext.TermsAsync(_uow, contractId, false, cancellationToken);
            string prefix = terms?.ProjectCode ?? ("C" + contractId.ToString(CultureInfo.InvariantCulture));
            DateTimeOffset now = DateTimeOffset.Now;
            int count = 0;
            foreach (PcMdrTemplateLine l in t.Lines.OrderBy(x => x.Seq))
            {
                _uow.Set<PcEngineeringDeliverable>().Add(new PcEngineeringDeliverable
                {
                    ContractId = contractId, WorkOrderId = workOrderId, DocumentNumber = $"{prefix}-{workOrderId:000}-{l.Seq:000}", Title = l.Title, ClientSubmittal = l.ClientSubmittal, StandardSource = l.StandardSource,
                    Discipline = l.Discipline, DocumentType = l.DocumentType, Required = l.Required, IssueState = PcIssueState.NotIssued, TemplateLineId = l.Id, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
                });
                count++;
            }
            return count;
        }

        public async Task<ResponseDto<PcMdrDraftResultDto>> DraftMdrAsync(PcMdrDraftDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcMdrDraftResultDto>(PcText.InvalidData);
            if (!_user.IsInRole("PLANNING,DCC,ENGINEERING,PMO")) return Bad<PcMdrDraftResultDto>(PcText.NotAllowed);
            WorkOrder wo = await _uow.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == dto.WorkOrderId && w.ContractId == dto.ContractId && !w.Deleted, cancellationToken);
            if (wo == null) return PcText.NotFound<PcMdrDraftResultDto>(PcText.WorkOrderNotFound);
            PcWorkOrderInfo info = await _uow.Set<PcWorkOrderInfo>().AsNoTracking().FirstOrDefaultAsync(i => i.WorkOrderId == wo.Id && !i.Deleted, cancellationToken);
            if (info == null) return Bad<PcMdrDraftResultDto>("ابتدا نوع محصول و کد پروژه تجهیز را برای دستور کار ثبت کنید.");
            int n = await DraftMdrNoSaveAsync(dto.ContractId, wo.Id, info.ProductType, cancellationToken);
            if (n == 0) return Bad<PcMdrDraftResultDto>("فهرست مدارک این دستور کار قبلاً ساخته شده یا برای نوع محصول قالبی تعریف نشده است.");
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(new PcMdrDraftResultDto { WorkOrderId = wo.Id, LinesDrafted = n, Message = "پیش‌نویس فهرست مدارک از قالب ساخته شد." });
        }

        private async Task<(decimal First, decimal Second, decimal Approved)> EngPercentsAsync(CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> s = await PcSettingKeys.LoadAsync(_uow, cancellationToken);
            return (s[PcSettingKeys.EngFirstIssue], s[PcSettingKeys.EngSecondIssue], s[PcSettingKeys.EngApproved]);
        }

        private static PcDeliverableDto ToDto(PcEngineeringDeliverable d, (decimal First, decimal Second, decimal Approved) p) => new PcDeliverableDto
        {
            Id = d.Id, ContractId = d.ContractId, WorkOrderId = d.WorkOrderId, DocumentNumber = d.DocumentNumber, Title = d.Title, ClientSubmittal = d.ClientSubmittal, StandardSource = d.StandardSource,
            Discipline = d.Discipline, DocumentType = d.DocumentType, Required = d.Required, PlannedDate = d.PlannedDate, IssueState = d.IssueState, Revision = d.Revision, DocumentId = d.DocumentId,
            ControlledDocumentId = d.ControlledDocumentId, LastTransmittalNumber = d.LastTransmittalNumber, LastTransmittalDate = d.LastTransmittalDate, ClientResponseCode = d.ClientResponseCode,
            ClientResponseDate = d.ClientResponseDate, ApprovedAt = d.ApprovedAt, RowVersion = d.RowVersion == null ? null : Convert.ToBase64String(d.RowVersion),
            ProgressPercent = d.IssueState == PcIssueState.Approved ? p.Approved : d.IssueState == PcIssueState.SecondIssue ? p.Second : d.IssueState == PcIssueState.FirstIssue ? p.First : 0m,
        };

        public async Task<ResponseDto<PagedList<PcDeliverableDto>>> GetDeliverablesAsync(PcDeliverableQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcDeliverableQuery();
            IQueryable<PcEngineeringDeliverable> q = _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => !d.Deleted);
            if (query.ContractId.HasValue) q = q.Where(d => d.ContractId == query.ContractId.Value);
            if (query.WorkOrderId.HasValue) q = q.Where(d => d.WorkOrderId == query.WorkOrderId.Value);
            if (query.ClientOnly) q = q.Where(d => d.ClientSubmittal);
            if (query.IssueState.HasValue) q = q.Where(d => d.IssueState == query.IssueState.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(d => d.DocumentNumber.Contains(text) || d.Title.Contains(text));
            PagedList<PcEngineeringDeliverable> page = await PagedList<PcEngineeringDeliverable>.CreateAsync(q.OrderBy(d => d.WorkOrderId).ThenBy(d => d.DocumentNumber).ThenBy(d => d.Id), query.PageNumber, query.PageSize, cancellationToken);
            var p = await EngPercentsAsync(cancellationToken);
            List<PcDeliverableDto> dtos = page.Select(d => ToDto(d, p)).ToList();
            return ResponseDtoHelper.Ok((PagedList<PcDeliverableDto>)new PcPage<PcDeliverableDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcDeliverableDto>> SaveDeliverableAsync(PcDeliverableDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Title) == null || Clean(dto.DocumentNumber) == null) return Bad<PcDeliverableDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Dcc)) return Bad<PcDeliverableDto>(PcText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken)) return PcText.NotFound<PcDeliverableDto>(PcText.ContractNotFound);
            if (dto.WorkOrderId.HasValue && !await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && w.ContractId == dto.ContractId && !w.Deleted, cancellationToken)) return Bad<PcDeliverableDto>("دستور کار متعلق به این قرارداد نیست.");
            string number = Clean(dto.DocumentNumber);
            if (await _uow.Set<PcEngineeringDeliverable>().AnyAsync(d => d.ContractId == dto.ContractId && d.DocumentNumber == number && !d.Deleted && d.Id != (dto.Id ?? 0), cancellationToken)) return Bad<PcDeliverableDto>("شماره سند در قرارداد تکراری است.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcEngineeringDeliverable d;
            if (dto.Id.HasValue)
            {
                d = await _uow.Set<PcEngineeringDeliverable>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (d == null) return PcText.NotFound<PcDeliverableDto>("سند پیدا نشد.");
                if (d.IssueState != PcIssueState.NotIssued && d.ClientSubmittal != dto.ClientSubmittal) return Bad<PcDeliverableDto>("پس از ارسال، وضعیت «فهرست کارفرما» تغییر نمی‌کند.");
            }
            else
            {
                d = new PcEngineeringDeliverable { ContractId = dto.ContractId, CreateDate = now, CreatorUserId = Me, IssueState = PcIssueState.NotIssued };
                _uow.Set<PcEngineeringDeliverable>().Add(d);
            }
            d.WorkOrderId = dto.WorkOrderId; d.DocumentNumber = number; d.Title = Clean(dto.Title); d.ClientSubmittal = dto.ClientSubmittal; d.StandardSource = Clean(dto.StandardSource);
            d.Discipline = Clean(dto.Discipline); d.DocumentType = Clean(dto.DocumentType); d.Required = dto.Required; d.PlannedDate = dto.PlannedDate?.Date;
            d.ModifyDate = now; d.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcDeliverableDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(d, await EngPercentsAsync(cancellationToken)));
        }

        public async Task<ResponseDto<PcDeliverableDto>> DeliverableActionAsync(PcDeliverableActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcDeliverableDto>(PcText.InvalidData);
            PcEngineeringDeliverable d = await _uow.Set<PcEngineeringDeliverable>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (d == null) return PcText.NotFound<PcDeliverableDto>("سند پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "first-issue":
                    if (!_user.IsInRole(ProjectControlRoles.Dcc)) return Bad<PcDeliverableDto>(PcText.NotAllowed);
                    if (d.ClientSubmittal) return Bad<PcDeliverableDto>("ارسال سند فهرست کارفرما با ترنسمیتال ثبت می‌شود.");
                    if (d.IssueState != PcIssueState.NotIssued) return Bad<PcDeliverableDto>("سند قبلاً صادر شده است.");
                    d.IssueState = PcIssueState.FirstIssue; d.IssuedByUserId = Me; d.Revision = Clean(dto.Revision) ?? d.Revision ?? "A";
                    break;
                case "second-issue":
                    if (!_user.IsInRole(ProjectControlRoles.Dcc)) return Bad<PcDeliverableDto>(PcText.NotAllowed);
                    if (d.ClientSubmittal) return Bad<PcDeliverableDto>("ارسال سند فهرست کارفرما با ترنسمیتال ثبت می‌شود.");
                    if (d.IssueState != PcIssueState.FirstIssue) return Bad<PcDeliverableDto>("ابتدا سند باید یک‌بار صادر شده باشد.");
                    d.IssueState = PcIssueState.SecondIssue; d.IssuedByUserId = Me; d.Revision = Clean(dto.Revision) ?? d.Revision;
                    break;
                case "internal-approve":
                    if (!_user.IsInRole("ENGINEERING,PMO")) return Bad<PcDeliverableDto>(PcText.NotAllowed + " (تایید داخلی با مدیر مهندسی است)");
                    if (d.ClientSubmittal) return Bad<PcDeliverableDto>("تایید سند فهرست کارفرما با کد پاسخ A یا B کارفرما انجام می‌شود.");
                    if (d.IssueState == PcIssueState.NotIssued) return Bad<PcDeliverableDto>("سند هنوز صادر نشده است.");
                    if (d.IssueState == PcIssueState.Approved) return Bad<PcDeliverableDto>("سند قبلاً تایید شده است.");
                    if (d.IssuedByUserId == Me) return Bad<PcDeliverableDto>(PcText.SameUser);
                    d.IssueState = PcIssueState.Approved; d.ApprovedAt = now; d.ApprovedByUserId = Me;
                    break;
                case "link-document":
                    if (!_user.IsInRole(ProjectControlRoles.Dcc)) return Bad<PcDeliverableDto>(PcText.NotAllowed);
                    if (!dto.DocumentId.HasValue && !dto.ControlledDocumentId.HasValue) return Bad<PcDeliverableDto>("فایل یا سند کنترل‌شده را مشخص کنید.");
                    if (dto.ControlledDocumentId.HasValue && !await _uow.Set<ControlledDocument>().AnyAsync(c => c.Id == dto.ControlledDocumentId.Value && !c.Deleted, cancellationToken)) return Bad<PcDeliverableDto>("سند کنترل‌شده پیدا نشد.");
                    d.DocumentId = dto.DocumentId ?? d.DocumentId; d.ControlledDocumentId = dto.ControlledDocumentId ?? d.ControlledDocumentId; d.Revision = Clean(dto.Revision) ?? d.Revision;
                    break;
                default:
                    return Bad<PcDeliverableDto>("عملیات نامعتبر است.");
            }
            d.ModifyDate = now; d.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcDeliverableDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(d, await EngPercentsAsync(cancellationToken)));
        }

        // ============================================================================ transmittals

        private async Task<PcTransmittalDto> TransmittalDtoAsync(PcTransmittal t, CancellationToken cancellationToken)
        {
            List<PcTransmittalLine> lines = t.Lines != null ? t.Lines.ToList() : await _uow.Set<PcTransmittalLine>().AsNoTracking().Where(l => l.TransmittalId == t.Id).ToListAsync(cancellationToken);
            List<int> ids = lines.Select(l => l.DeliverableId).ToList();
            var docs = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => ids.Contains(d.Id)).Select(d => new { d.Id, d.DocumentNumber, d.Title }).ToListAsync(cancellationToken);
            bool overdue = t.Direction == PcTransmittalDirection.Outgoing && t.ResponseDue.HasValue && t.ResponseDue.Value.Date < DateTime.Today && lines.Any(l => l.Purpose == PcTransmittalPurpose.ForApproval && l.ResponseCode == null);
            return new PcTransmittalDto
            {
                Id = t.Id, Number = t.Number, ContractId = t.ContractId, Direction = t.Direction, TransmittalDate = t.TransmittalDate, ResponseDue = t.ResponseDue, Reference = t.Reference, Note = t.Note, Overdue = overdue,
                Lines = lines.OrderBy(l => l.Id).Select(l => new PcTransmittalLineDto
                {
                    Id = l.Id, DeliverableId = l.DeliverableId, DocumentNumber = docs.FirstOrDefault(d => d.Id == l.DeliverableId)?.DocumentNumber, Title = docs.FirstOrDefault(d => d.Id == l.DeliverableId)?.Title,
                    Revision = l.Revision, Purpose = l.Purpose, ResponseCode = l.ResponseCode, ResponseDate = l.ResponseDate, ResponseNote = l.ResponseNote,
                }).ToList(),
            };
        }

        public async Task<ResponseDto<PagedList<PcTransmittalDto>>> GetTransmittalsAsync(PcTransmittalQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcTransmittalQuery();
            IQueryable<PcTransmittal> q = _uow.Set<PcTransmittal>().AsNoTracking().Include(t => t.Lines).Where(t => !t.Deleted);
            if (query.ContractId.HasValue) q = q.Where(t => t.ContractId == query.ContractId.Value);
            if (query.Direction.HasValue) q = q.Where(t => t.Direction == query.Direction.Value);
            if (query.OnlyOverdue)
            {
                DateTime today = DateTime.Today;
                q = q.Where(t => t.Direction == PcTransmittalDirection.Outgoing && t.ResponseDue != null && t.ResponseDue < today && t.Lines.Any(l => l.Purpose == PcTransmittalPurpose.ForApproval && l.ResponseCode == null));
            }
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(t => t.Number.Contains(text) || t.Reference.Contains(text));
            PagedList<PcTransmittal> page = await PagedList<PcTransmittal>.CreateAsync(q.OrderByDescending(t => t.Id).AsSplitQuery(), query.PageNumber, query.PageSize, cancellationToken);
            var dtos = new List<PcTransmittalDto>();
            foreach (PcTransmittal t in page) dtos.Add(await TransmittalDtoAsync(t, cancellationToken));
            return ResponseDtoHelper.Ok((PagedList<PcTransmittalDto>)new PcPage<PcTransmittalDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcTransmittalDto>> GetTransmittalAsync(int id, CancellationToken cancellationToken)
        {
            PcTransmittal t = await _uow.Set<PcTransmittal>().AsNoTracking().Include(x => x.Lines).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            return t == null ? PcText.NotFound<PcTransmittalDto>("ترنسمیتال پیدا نشد.") : ResponseDtoHelper.Ok(await TransmittalDtoAsync(t, cancellationToken));
        }

        public async Task<ResponseDto<PcTransmittalDto>> SaveTransmittalAsync(PcTransmittalDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || dto.Lines == null || dto.Lines.Count == 0) return Bad<PcTransmittalDto>("حداقل یک سند در ترنسمیتال لازم است.");
            if (dto.Id.HasValue) return Bad<PcTransmittalDto>("ترنسمیتال ثبت‌شده تغییر نمی‌کند؛ پاسخ کارفرما را ثبت کنید یا ترنسمیتال جدید بسازید.");
            if (!_user.IsInRole(ProjectControlRoles.DccOnly)) return Bad<PcTransmittalDto>(PcText.NotAllowed);
            if (!Enum.IsDefined(typeof(PcTransmittalDirection), dto.Direction)) return Bad<PcTransmittalDto>("جهت ترنسمیتال را مشخص کنید.");
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken)) return PcText.NotFound<PcTransmittalDto>(PcText.ContractNotFound);
            List<int> ids = dto.Lines.Select(l => l.DeliverableId).ToList();
            if (ids.Distinct().Count() != ids.Count) return Bad<PcTransmittalDto>("سند تکراری در ترنسمیتال است.");
            List<PcEngineeringDeliverable> docs = await _uow.Set<PcEngineeringDeliverable>().Where(d => ids.Contains(d.Id) && d.ContractId == dto.ContractId && !d.Deleted).ToListAsync(cancellationToken);
            if (docs.Count != ids.Count) return Bad<PcTransmittalDto>("سندی متعلق به این قرارداد نیست.");
            PcContractTerms terms = await PcContext.TermsAsync(_uow, dto.ContractId, false, cancellationToken);
            DateTime date = (dto.TransmittalDate == default ? DateTime.Today : dto.TransmittalDate).Date;
            DateTimeOffset now = DateTimeOffset.Now;
            var t = new PcTransmittal
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, PcNumbers.Transmittal, dto.Direction == PcTransmittalDirection.Outgoing ? "TR" : "TRI", date, cancellationToken), ContractId = dto.ContractId, Direction = dto.Direction,
                TransmittalDate = date, Reference = Clean(dto.Reference), Note = Clean(dto.Note), CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me, Lines = new List<PcTransmittalLine>(),
            };
            if (dto.Direction == PcTransmittalDirection.Outgoing) t.ResponseDue = date.AddDays(terms?.ClientReviewDays ?? 14);
            foreach (PcTransmittalLineDto l in dto.Lines)
            {
                PcEngineeringDeliverable d = docs.First(x => x.Id == l.DeliverableId);
                if (!Enum.IsDefined(typeof(PcTransmittalPurpose), l.Purpose)) return Bad<PcTransmittalDto>("منظور ارسال را مشخص کنید.");
                if (dto.Direction == PcTransmittalDirection.Outgoing && !d.ClientSubmittal) return Bad<PcTransmittalDto>("فقط اسناد فهرست کارفرما (MDL) برای کارفرما ارسال می‌شوند: " + d.DocumentNumber);
                string revision = Clean(l.Revision) ?? d.Revision ?? "A";
                t.Lines.Add(new PcTransmittalLine { DeliverableId = d.Id, Revision = revision, Purpose = l.Purpose });
                if (dto.Direction == PcTransmittalDirection.Outgoing)
                {
                    d.Revision = revision; d.LastTransmittalNumber = t.Number; d.LastTransmittalDate = date; d.IssuedByUserId = Me;
                    if (l.Purpose == PcTransmittalPurpose.ForApproval)
                    {
                        if (d.IssueState == PcIssueState.NotIssued) d.IssueState = PcIssueState.FirstIssue;
                        else if (d.IssueState == PcIssueState.FirstIssue) d.IssueState = PcIssueState.SecondIssue;
                        d.ClientResponseCode = null; d.ClientResponseDate = null;
                    }
                    d.ModifyDate = now; d.ModifierUserId = Me;
                }
            }
            _uow.Set<PcTransmittal>().Add(t);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await TransmittalDtoAsync(t, cancellationToken));
        }

        public async Task<ResponseDto<PcTransmittalDto>> RecordResponseAsync(PcTransmittalResponseDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.ResponseCode) == null) return Bad<PcTransmittalDto>("کد پاسخ کارفرما را وارد کنید.");
            if (!_user.IsInRole(ProjectControlRoles.DccOnly)) return Bad<PcTransmittalDto>(PcText.NotAllowed);
            await EnsureResponseCodesAsync(cancellationToken);
            string code = PcText.Upper(dto.ResponseCode);
            PcClientResponseCode rc = await _uow.Set<PcClientResponseCode>().AsNoTracking().FirstOrDefaultAsync(c => c.Code == code, cancellationToken);
            if (rc == null) return Bad<PcTransmittalDto>("کد پاسخ تعریف نشده است.");
            PcTransmittalLine line = await _uow.Set<PcTransmittalLine>().Include(l => l.Transmittal).FirstOrDefaultAsync(l => l.Id == dto.TransmittalLineId, cancellationToken);
            if (line == null) return PcText.NotFound<PcTransmittalDto>("ردیف ترنسمیتال پیدا نشد.");
            if (line.Transmittal.Direction != PcTransmittalDirection.Outgoing) return Bad<PcTransmittalDto>("پاسخ فقط برای ترنسمیتال ارسالی ثبت می‌شود.");
            if (line.ResponseCode != null) return Bad<PcTransmittalDto>("پاسخ این ردیف قبلاً ثبت شده است.");
            if (line.Purpose != PcTransmittalPurpose.ForApproval) return Bad<PcTransmittalDto>("پاسخ فقط برای ردیف «جهت تایید» ثبت می‌شود.");
            PcEngineeringDeliverable d = await _uow.Set<PcEngineeringDeliverable>().FirstAsync(x => x.Id == line.DeliverableId, cancellationToken);
            DateTime date = (dto.ResponseDate ?? DateTime.Today).Date;
            DateTimeOffset now = DateTimeOffset.Now;
            line.ResponseCode = rc.Code; line.ResponseDate = date; line.ResponseNote = Clean(dto.Note);
            d.ClientResponseCode = rc.Code; d.ClientResponseDate = date;
            if (rc.CountsAsApproved) { d.IssueState = PcIssueState.Approved; d.ApprovedAt = now; d.ApprovedByUserId = Me; }
            d.ModifyDate = now; d.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcTransmittalDto>(PcText.Concurrency); }
            PcTransmittal t = await _uow.Set<PcTransmittal>().AsNoTracking().Include(x => x.Lines).AsSplitQuery().FirstAsync(x => x.Id == line.TransmittalId, cancellationToken);
            return ResponseDtoHelper.Ok(await TransmittalDtoAsync(t, cancellationToken));
        }

        public async Task<int> OverdueResponsesAsync(int? contractId, CancellationToken cancellationToken)
        {
            DateTime today = DateTime.Today;
            IQueryable<PcTransmittalLine> q = _uow.Set<PcTransmittalLine>().AsNoTracking().Where(l => l.Purpose == PcTransmittalPurpose.ForApproval && l.ResponseCode == null && !l.Transmittal.Deleted
                && l.Transmittal.Direction == PcTransmittalDirection.Outgoing && l.Transmittal.ResponseDue != null && l.Transmittal.ResponseDue < today);
            if (contractId.HasValue) q = q.Where(l => l.Transmittal.ContractId == contractId.Value);
            return await q.CountAsync(cancellationToken);
        }

        // ============================================================================ final books

        public async Task<ResponseDto<PcFinalBookDto>> GenerateFinalBookAsync(PcFinalBookRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcFinalBookDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.DccOnly + ",QC")) return Bad<PcFinalBookDto>(PcText.NotAllowed);
            string variant = Clean(dto.Variant)?.ToLowerInvariant() ?? "company";
            if (variant != "company" && variant != "client") return Bad<PcFinalBookDto>("نوع کتاب نهایی باید company یا client باشد.");
            WorkOrder wo = await _uow.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == dto.WorkOrderId && !w.Deleted, cancellationToken);
            if (wo == null) return PcText.NotFound<PcFinalBookDto>(PcText.WorkOrderNotFound);
            ResponseDto<DataBookDto> built = await _release.GetDataBookAsync(wo.Id, variant, cancellationToken);
            if (!built.Success || built.Data == null) return Bad<PcFinalBookDto>(built.Message ?? "ساخت کتاب نهایی ممکن نشد.");
            DataBookDto book = built.Data;
            if (book.MissingCount > 0 && !dto.AllowIncomplete) return Bad<PcFinalBookDto>($"کتاب نهایی {book.MissingCount} مورد الزامی ناقص دارد؛ برای صدور موقت «صدور ناقص» را انتخاب کنید.");

            DateTimeOffset now = DateTimeOffset.Now;
            string json = JsonSerializer.Serialize(book);
            string hash = PcText.Sha256(json);
            string docCode = $"FB-{wo.Id:00000}-{(variant == "company" ? "C" : "L")}";
            ControlledDocument doc = await _uow.Set<ControlledDocument>().Include(d => d.Revisions).FirstOrDefaultAsync(d => d.Code == docCode && !d.Deleted, cancellationToken);
            if (doc == null)
            {
                doc = new ControlledDocument
                {
                    Code = docCode, Type = ControlledDocumentType.FinalBook, Department = "QC", Title = $"کتاب نهایی ({(variant == "company" ? "شرکتی" : "کارفرما")}) دستور کار {wo.Code}", Status = ControlledDocumentStatus.Draft,
                    RetentionClass = "FinalBook", ResponsibleUnit = "QC", ContractId = wo.ContractId, WorkOrderId = wo.Id, CreateDate = now, CreatorUserId = Me, Revisions = new List<DocumentRevisionRecord>(),
                };
                _uow.Set<ControlledDocument>().Add(doc);
            }
            int next = doc.Revisions.Count(r => !r.Deleted) + 1;
            string revision = "R" + next.ToString("00", CultureInfo.InvariantCulture);
            foreach (DocumentRevisionRecord old in doc.Revisions.Where(r => !r.Deleted && r.Status != ControlledDocumentStatus.Obsolete)) { old.Status = ControlledDocumentStatus.Obsolete; old.ModifyDate = now; old.ModifierUserId = Me; }
            doc.Revisions.Add(new DocumentRevisionRecord
            {
                Revision = revision, ChangeSummary = $"صدور {(variant == "company" ? "کتاب شرکتی" : "کتاب کارفرما")}؛ مورد ناقص: {book.MissingCount}؛ هش {hash.Substring(0, 12)}", Status = ControlledDocumentStatus.Effective,
                PreparedByUserId = Me, ApprovedByUserId = Me, ApprovedAt = now, EffectiveDate = now.Date, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
            });
            doc.CurrentRevision = revision; doc.Status = ControlledDocumentStatus.Effective; doc.EffectiveDate = now.Date; doc.ModifyDate = now; doc.ModifierUserId = Me;

            var issue = new PcReportIssue
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, PcNumbers.Report, variant == "company" ? "FBC" : "FBK", now.Date, cancellationToken), Kind = variant == "company" ? PcReportKind.FinalBookCompany : PcReportKind.FinalBookClient,
                ContractId = wo.ContractId, WorkOrderId = wo.Id, DataDate = now.Date, GeneratedAt = now, GeneratedByUserId = Me, Hash = hash, Content = json, ControlledRevision = revision,
            };
            _uow.Set<PcReportIssue>().Add(issue);
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Bad<PcFinalBookDto>("صدور هم‌زمان کتاب نهایی؛ دوباره تلاش کنید."); }
            issue.ControlledDocumentId = doc.Id;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(new PcFinalBookDto
            {
                ReportIssueId = issue.Id, Number = issue.Number, Variant = variant, WorkOrderId = wo.Id, WorkOrderCode = wo.Code, ControlledDocumentId = doc.Id, DocumentCode = doc.Code, Revision = revision,
                Hash = hash, MissingCount = book.MissingCount, EntryCount = book.Sections.Sum(s => s.Entries.Count), GeneratedAt = now,
            });
        }

        public async Task<ResponseDto<PagedList<PcFinalBookDto>>> GetFinalBooksAsync(PcFinalBookQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcFinalBookQuery();
            IQueryable<PcReportIssue> q = _uow.Set<PcReportIssue>().AsNoTracking().Where(r => r.Kind == PcReportKind.FinalBookCompany || r.Kind == PcReportKind.FinalBookClient);
            if (query.ContractId.HasValue) q = q.Where(r => r.ContractId == query.ContractId.Value);
            if (query.WorkOrderId.HasValue) q = q.Where(r => r.WorkOrderId == query.WorkOrderId.Value);
            IQueryable<PcFinalBookDto> projection = q.OrderByDescending(r => r.Id).Select(r => new PcFinalBookDto
            {
                ReportIssueId = r.Id, Number = r.Number, Variant = r.Kind == PcReportKind.FinalBookCompany ? "company" : "client", WorkOrderId = r.WorkOrderId ?? 0, ControlledDocumentId = r.ControlledDocumentId,
                Revision = r.ControlledRevision, Hash = r.Hash, GeneratedAt = r.GeneratedAt,
            });
            return ResponseDtoHelper.Ok(await PagedList<PcFinalBookDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }
    }
}
