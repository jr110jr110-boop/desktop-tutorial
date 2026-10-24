using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    public interface IPcProjectService
    {
        Task<ResponseDto<PcContractTermsDto>> GetTermsAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PcContractTermsDto>> SaveTermsAsync(PcContractTermsDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcContractTermsDto>> GenerateProjectCodeAsync(GenerateProjectCodeDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcCodingRowDto>>> GetCodingAsync(string table, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcCodingRowDto>>> SaveCodingAsync(PcCodingRowDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcWorkOrderInfoDto>>> GetWorkOrderInfosAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PcWorkOrderInfoDto>> SaveWorkOrderInfoAsync(PcWorkOrderInfoDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PcInitiationDto>> GetInitiationAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PcInitiationDto>> StartInitiationAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PcInitiationDto>> InitiationActionAsync(PcInitiationActionDto dto, CancellationToken cancellationToken);

        /// <summary>Marks a step done without saving (the caller saves in the same operation). Returns null when done or when there is no initiation.</summary>
        Task<string> MarkStepAsync(int contractId, PcInitiationStep step, int userId, bool enforceOrder, string note, CancellationToken cancellationToken);

        /// <summary>Initiation steps that are past their service level (dashboard).</summary>
        Task<List<PcInitiationStepDto>> OverdueStepsAsync(CancellationToken cancellationToken);
        Task<List<int>> OverdueContractIdsAsync(CancellationToken cancellationToken);

        Task<ResponseDto<PcCharterDto>> GetCharterAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PcCharterDto>> SaveCharterAsync(PcCharterDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcCharterDto>> CharterActionAsync(string action, PcApproveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcPlanDto>> GetPlanAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PcPlanDto>> SavePlanAsync(PcPlanDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcPlanDto>> ApprovePlanAsync(PcApproveDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcReviewDto>>> GetReviewsAsync(PcContractQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcReviewDto>> SaveReviewAsync(PcReviewDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcReviewDto>> ReviewActionAsync(PcReviewActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcAmendmentDto>>> GetAmendmentsAsync(PcContractQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcAmendmentDto>> SaveAmendmentAsync(PcAmendmentDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcAmendmentDto>> AmendmentActionAsync(PcAmendmentActionDto dto, CancellationToken cancellationToken);
    }

    /// <summary>Project and contract set-up: terms, coding (WI-PL-005), initiation (WI-PL-009), charter, PEP, contract review (ISO 9001 8.2.3).</summary>
    public class PcProjectService : IPcProjectService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public PcProjectService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => PcText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => PcText.Bad<T>(m);

        // ============================================================================ terms

        private async Task<PcContractTermsDto> TermsDtoAsync(ContractEntity contract, PcContractTerms t, CancellationToken cancellationToken)
        {
            List<PcVariationOrder> vos = await _uow.Set<PcVariationOrder>().AsNoTracking().Where(v => v.ContractId == contract.Id && !v.Deleted && v.Status == PcVariationStatus.Approved).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { t?.ProjectManagerUserId }, cancellationToken);
            decimal variationPrice = vos.Sum(v => v.PriceDelta);
            var dto = new PcContractTermsDto
            {
                ContractId = contract.Id, ContractCode = contract.Code, ContractTitle = contract.Title,
                ApprovedVariationPrice = variationPrice, ApprovedVariationDays = vos.Sum(v => v.DaysDelta),
            };
            if (t == null) return dto;
            dto.ProjectCode = t.ProjectCode; dto.Category = t.Category; dto.CurrencyCode = t.CurrencyCode; dto.ValueExVat = t.ValueExVat;
            dto.VatPercent = t.VatPercent; dto.AdvancePercent = t.AdvancePercent; dto.RetentionPercent = t.RetentionPercent;
            dto.LatePenaltyPercentPerWeek = t.LatePenaltyPercentPerWeek; dto.PenaltyCapPercent = t.PenaltyCapPercent; dto.GraceDays = t.GraceDays;
            dto.ChangeProcedureAgreed = t.ChangeProcedureAgreed; dto.ChangeProcedureDocumentId = t.ChangeProcedureDocumentId;
            dto.ProjectManagerUserId = t.ProjectManagerUserId; dto.ProjectManagerName = names.Name(t.ProjectManagerUserId);
            dto.CalendarId = t.CalendarId; dto.CurrentBaselineId = t.CurrentBaselineId; dto.ClientReportDayOfMonth = t.ClientReportDayOfMonth;
            dto.DataDateWeekday = t.DataDateWeekday; dto.ScheduleStartDate = t.ScheduleStartDate; dto.ContractDeliveryDate = t.ContractDeliveryDate;
            dto.ClientReportShowsIndices = t.ClientReportShowsIndices; dto.AccrualMode = t.AccrualMode; dto.ClientReviewDays = t.ClientReviewDays;
            dto.ManagementReserve = t.ManagementReserve; dto.AutoSnapshot = t.AutoSnapshot;
            dto.CurrentValue = t.ValueExVat.HasValue ? t.ValueExVat.Value + variationPrice : (decimal?)null;
            dto.RowVersion = t.RowVersion == null ? null : Convert.ToBase64String(t.RowVersion);
            return dto;
        }

        public async Task<ResponseDto<PcContractTermsDto>> GetTermsAsync(int contractId, CancellationToken cancellationToken)
        {
            ContractEntity c = await _uow.Set<ContractEntity>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == contractId && !x.Deleted, cancellationToken);
            if (c == null) return PcText.NotFound<PcContractTermsDto>(PcText.ContractNotFound);
            PcContractTerms t = await PcContext.TermsAsync(_uow, contractId, false, cancellationToken);
            return ResponseDtoHelper.Ok(await TermsDtoAsync(c, t, cancellationToken));
        }

        public async Task<ResponseDto<PcContractTermsDto>> SaveTermsAsync(PcContractTermsDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcContractTermsDto>(PcText.InvalidData);
            if (!_user.IsInRole("PMO,PLANNING,COMMERCIAL,PROJECTMANAGER")) return Bad<PcContractTermsDto>(PcText.NotAllowed);
            ContractEntity c = await _uow.Set<ContractEntity>().FirstOrDefaultAsync(x => x.Id == dto.ContractId && !x.Deleted, cancellationToken);
            if (c == null) return PcText.NotFound<PcContractTermsDto>(PcText.ContractNotFound);
            foreach (decimal? p in new[] { dto.VatPercent, dto.AdvancePercent, dto.RetentionPercent, dto.PenaltyCapPercent })
                if (p.HasValue && (p.Value < 0 || p.Value > 100)) return Bad<PcContractTermsDto>("درصدها باید بین ۰ تا ۱۰۰ باشند.");
            if (dto.LatePenaltyPercentPerWeek.HasValue && (dto.LatePenaltyPercentPerWeek.Value < 0 || dto.LatePenaltyPercentPerWeek.Value > 100)) return Bad<PcContractTermsDto>("درصد جریمه هفتگی نامعتبر است.");
            if (dto.GraceDays < 0 || dto.GraceDays > 3650) return Bad<PcContractTermsDto>("مهلت تاخیر مجاز نامعتبر است.");
            if (dto.DataDateWeekday < 0 || dto.DataDateWeekday > 6) return Bad<PcContractTermsDto>("روز هفته تاریخ داده باید بین ۰ (یکشنبه) و ۶ (شنبه) باشد.");
            if (dto.ClientReviewDays < 1 || dto.ClientReviewDays > 365) return Bad<PcContractTermsDto>("مهلت بررسی کارفرما نامعتبر است.");
            if (dto.ManagementReserve < 0) return Bad<PcContractTermsDto>("ذخیره مدیریتی منفی نیست.");
            if (dto.ClientReportDayOfMonth.HasValue && (dto.ClientReportDayOfMonth.Value < 1 || dto.ClientReportDayOfMonth.Value > 31)) return Bad<PcContractTermsDto>("روز گزارش ماهانه نامعتبر است.");
            if (dto.CalendarId.HasValue && !await _uow.Set<PcCalendar>().AnyAsync(x => x.Id == dto.CalendarId.Value && !x.Deleted, cancellationToken)) return Bad<PcContractTermsDto>("تقویم کاری پیدا نشد.");

            DateTimeOffset now = DateTimeOffset.Now;
            PcContractTerms t = await PcContext.TermsAsync(_uow, dto.ContractId, true, cancellationToken);
            if (t == null)
            {
                t = new PcContractTerms { ContractId = c.Id, CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcContractTerms>().Add(t);
            }
            t.CurrencyCode = PcText.Upper(dto.CurrencyCode); t.ValueExVat = dto.ValueExVat; t.VatPercent = dto.VatPercent; t.AdvancePercent = dto.AdvancePercent;
            t.RetentionPercent = dto.RetentionPercent; t.LatePenaltyPercentPerWeek = dto.LatePenaltyPercentPerWeek; t.PenaltyCapPercent = dto.PenaltyCapPercent;
            t.GraceDays = dto.GraceDays; t.ChangeProcedureAgreed = dto.ChangeProcedureAgreed; t.ChangeProcedureDocumentId = dto.ChangeProcedureDocumentId;
            t.CalendarId = dto.CalendarId; t.ClientReportDayOfMonth = dto.ClientReportDayOfMonth; t.DataDateWeekday = dto.DataDateWeekday;
            t.ScheduleStartDate = dto.ScheduleStartDate?.Date; t.ContractDeliveryDate = dto.ContractDeliveryDate?.Date; t.ClientReportShowsIndices = dto.ClientReportShowsIndices;
            t.AccrualMode = dto.AccrualMode == PcAccrualMode.AtEnd ? PcAccrualMode.AtEnd : PcAccrualMode.Linear; t.ClientReviewDays = dto.ClientReviewDays;
            t.ManagementReserve = dto.ManagementReserve; t.AutoSnapshot = dto.AutoSnapshot;
            t.ModifyDate = now; t.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcContractTermsDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(await TermsDtoAsync(c, t, cancellationToken));
        }

        public async Task<ResponseDto<PcContractTermsDto>> GenerateProjectCodeAsync(GenerateProjectCodeDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || !Enum.IsDefined(typeof(PcCategory), dto.Category)) return Bad<PcContractTermsDto>("گروه قرارداد (MA، SP یا SE) را مشخص کنید.");
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcContractTermsDto>(PcText.NotAllowed + " (کدگذاری با واحد برنامه‌ریزی است)");
            ContractEntity c = await _uow.Set<ContractEntity>().FirstOrDefaultAsync(x => x.Id == dto.ContractId && !x.Deleted, cancellationToken);
            if (c == null) return PcText.NotFound<PcContractTermsDto>(PcText.ContractNotFound);
            DateTimeOffset now = DateTimeOffset.Now;
            PcContractTerms t = await PcContext.TermsAsync(_uow, dto.ContractId, true, cancellationToken);
            if (t != null && t.ProjectCode != null) return Bad<PcContractTermsDto>("کد این قرارداد قبلاً صادر شده و تغییر نمی‌کند: " + t.ProjectCode);
            if (t == null)
            {
                t = new PcContractTerms { ContractId = c.Id, CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcContractTerms>().Add(t);
            }
            int year = PcText.PersianYear(DateTime.Today);
            int counter = await InventoryNumbers.NextAsync(_uow, PcNumbers.ContractYear, year, cancellationToken);
            int repeat = await InventoryNumbers.NextAsync(_uow, PcNumbers.CategoryKind(dto.Category), year, cancellationToken);
            if (counter > 99 || repeat > 99) return Bad<PcContractTermsDto>("شمارنده سالانه کد قرارداد از ۹۹ گذشته است.");
            t.ProjectCode = $"{year % 1000:000}{counter:00}{PcNumbers.CategoryLetters(dto.Category)}{repeat:00}";
            t.Category = dto.Category;
            t.ModifyDate = now; t.ModifierUserId = Me;
            string stepError = await MarkStepAsync(c.Id, PcInitiationStep.CodeAssigned, Me, false, t.ProjectCode, cancellationToken);
            if (stepError != null) return Bad<PcContractTermsDto>(stepError);
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Bad<PcContractTermsDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(await TermsDtoAsync(c, t, cancellationToken));
        }

        // ============================================================================ coding tables (WI-PL-005)

        private static PcCodingTable Row(string table, string family, string code, string title, ItpProductType? type, string note = null) =>
            new PcCodingTable { Table = table, Family = family, Code = code, Title = title, ProductType = type, Note = note, CreateDate = DateTimeOffset.Now };

        private async Task EnsureCodingAsync(CancellationToken cancellationToken)
        {
            if (await _uow.Set<PcCodingTable>().AnyAsync(cancellationToken)) return;
            const string src = "WI-PL-005";
            var rows = new List<PcCodingTable>();
            foreach (string c in new[] { "TS", "TT", "TF", "TL", "TG", "TZ", "TB" })
                rows.Add(Row("TYPE", "turbine", c, "Turbine type " + c + " (" + src + ")", ItpProductType.SteamTurbine));
            rows.Add(Row("TYPE", "lube-oil", "LB", "Lube oil system", ItpProductType.LubeOilSystem));
            rows.Add(Row("TYPE", "gearbox", "GB", "Gearbox", ItpProductType.Other));
            rows.Add(Row("TYPE", "control", "UC", "Unit control system", ItpProductType.Other));
            rows.Add(Row("TYPE", "baseplate", "BP", "Base plate", ItpProductType.Other));
            rows.Add(Row("TYPE", "coupling", "CP", "Coupling", ItpProductType.Other));
            rows.Add(Row("TYPE", "condenser", "SC", "Surface condenser", ItpProductType.Condenser));
            rows.Add(Row("TYPE", "pump", "PU", "Pump", ItpProductType.Pump));
            rows.Add(Row("TYPE", "generator", "EG", "Generator", ItpProductType.Other));
            foreach (string c in new[] { "PM", "HM", "PB", "MB" }) rows.Add(Row("OPTION", "turbine", c, "Governor type " + c, null));
            foreach (string c in new[] { "EE", "SE", "TE", "TM", "EM", "SM" }) rows.Add(Row("OPTION", "lube-oil", c, "Drive arrangement " + c, null));
            foreach (string c in new[] { "PB", "RA" }) rows.Add(Row("OPTION", "gearbox", c, "Gearbox option " + c, null));
            foreach (string c in new[] { "FS", "RD", "FR" }) rows.Add(Row("OPTION", "control", c, "Control option " + c, null));
            foreach (string c in new[] { "TB", "TG", "FT" }) rows.Add(Row("OPTION", "baseplate", c, "Base plate option " + c, null));
            foreach (string c in new[] { "RT", "FL", "RP", "EP", "GT" }) rows.Add(Row("OPTION", "coupling", c, "Coupling option " + c, null));
            foreach (string c in new[] { "EE", "SE", "TE", "ST" }) rows.Add(Row("OPTION", "condenser", c, "Condenser option " + c, null));
            foreach (string c in new[] { "LV", "MV" }) rows.Add(Row("OPTION", "generator", c, "Generator voltage " + c, null));
            foreach (string c in new[] { "PLC", "MMS", "GLC", "PMS", "GPL", "PMG" }) rows.Add(Row("SIZE", "control", c, "Control system combination " + c, null));
            foreach (string c in new[] { "SCM", "WGH", "ILB" }) rows.Add(Row("SIZE", "baseplate", c, "Base plate size " + c, null));
            rows.Add(Row("SIZE", "turbine", "###", "Size = turbine kW x 10", null, "three characters, digits"));
            rows.Add(Row("SIZE", "lube-oil", "###", "Size = litres per minute", null));
            rows.Add(Row("SIZE", "gearbox", "###", "Size = kW x 10", null));
            rows.Add(Row("SIZE", "coupling", "###", "Size = kW x 10", null));
            rows.Add(Row("SIZE", "condenser", "###", "Size = m3/h", null));
            rows.Add(Row("SIZE", "pump", "###", "Size = discharge flange diameter in mm", null));
            rows.Add(Row("SIZE", "generator", "###", "Size = kW x 10", null));
            rows.Add(Row("OPTION", "pump", "##", "Option = impeller diameter in cm", null, "two characters, digits"));
            rows.Add(Row("NOTE", "orc", "ORC", "ORC units and valves have no code in WI-PL-005", ItpProductType.OrcUnit, "Add TYPE / SIZE / OPTION rows here when the company defines them; no release is needed."));
            _uow.Set<PcCodingTable>().AddRange(rows);
            await _uow.SaveChangesAsync(cancellationToken);
        }

        public async Task<ResponseDto<List<PcCodingRowDto>>> GetCodingAsync(string table, CancellationToken cancellationToken)
        {
            await EnsureCodingAsync(cancellationToken);
            string tb = PcText.Upper(table);
            IQueryable<PcCodingTable> q = _uow.Set<PcCodingTable>().AsNoTracking().Where(r => !r.Deleted);
            if (tb != null) q = q.Where(r => r.Table == tb);
            List<PcCodingRowDto> list = await q.OrderBy(r => r.Table).ThenBy(r => r.Family).ThenBy(r => r.Code)
                .Select(r => new PcCodingRowDto { Id = r.Id, Table = r.Table, Family = r.Family, Code = r.Code, Title = r.Title, ProductType = r.ProductType, Note = r.Note }).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<List<PcCodingRowDto>>> SaveCodingAsync(PcCodingRowDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Code) == null || Clean(dto.Table) == null) return Bad<List<PcCodingRowDto>>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<List<PcCodingRowDto>>(PcText.NotAllowed);
            string table = PcText.Upper(dto.Table);
            if (table != "TYPE" && table != "OPTION" && table != "SIZE" && table != "NOTE") return Bad<List<PcCodingRowDto>>("جدول باید TYPE، OPTION، SIZE یا NOTE باشد.");
            await EnsureCodingAsync(cancellationToken);
            string code = PcText.Upper(dto.Code);
            string family = Clean(dto.Family)?.ToLowerInvariant();
            if (table == "TYPE" && code.Length != 2) return Bad<List<PcCodingRowDto>>("کد TYPE دو نویسه است.");
            if (table == "OPTION" && code.Length != 2) return Bad<List<PcCodingRowDto>>("کد OPTION دو نویسه است.");
            if (table == "SIZE" && code.Length != 3 && code != "###") return Bad<List<PcCodingRowDto>>("کد SIZE سه نویسه است.");
            PcCodingTable row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<PcCodingTable>().FirstOrDefaultAsync(r => r.Id == dto.Id.Value && !r.Deleted, cancellationToken);
                if (row == null) return PcText.NotFound<List<PcCodingRowDto>>("ردیف پیدا نشد.");
            }
            else
            {
                if (await _uow.Set<PcCodingTable>().AnyAsync(r => !r.Deleted && r.Table == table && r.Family == family && r.Code == code, cancellationToken))
                    return Bad<List<PcCodingRowDto>>("این کد در همین جدول و خانواده وجود دارد.");
                row = new PcCodingTable { Table = table, CreateDate = DateTimeOffset.Now, CreatorUserId = Me };
                _uow.Set<PcCodingTable>().Add(row);
            }
            row.Table = table; row.Family = family; row.Code = code; row.Title = Clean(dto.Title); row.ProductType = dto.ProductType; row.Note = Clean(dto.Note);
            row.ModifyDate = DateTimeOffset.Now; row.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetCodingAsync(table, cancellationToken);
        }

        private async Task<PcWorkOrderInfoDto> InfoDtoAsync(PcWorkOrderInfo i, CancellationToken cancellationToken)
        {
            string code = await _uow.Set<WorkOrder>().Where(w => w.Id == i.WorkOrderId).Select(w => w.Code).FirstOrDefaultAsync(cancellationToken);
            return new PcWorkOrderInfoDto
            {
                Id = i.Id, WorkOrderId = i.WorkOrderId, ContractId = i.ContractId, WorkOrderCode = code, ProductType = i.ProductType, TypeCode = i.TypeCode,
                SizeCode = i.SizeCode, OptionCode = i.OptionCode, EquipmentProjectCode = i.EquipmentProjectCode, RepeatCounter = i.RepeatCounter, PlannedDeliveryDate = i.PlannedDeliveryDate,
            };
        }

        public async Task<ResponseDto<List<PcWorkOrderInfoDto>>> GetWorkOrderInfosAsync(int contractId, CancellationToken cancellationToken)
        {
            List<PcWorkOrderInfoDto> list = await _uow.Set<PcWorkOrderInfo>().AsNoTracking().Where(i => i.ContractId == contractId && !i.Deleted).OrderBy(i => i.Id)
                .Select(i => new PcWorkOrderInfoDto
                {
                    Id = i.Id, WorkOrderId = i.WorkOrderId, ContractId = i.ContractId, WorkOrderCode = i.WorkOrder.Code, ProductType = i.ProductType, TypeCode = i.TypeCode,
                    SizeCode = i.SizeCode, OptionCode = i.OptionCode, EquipmentProjectCode = i.EquipmentProjectCode, RepeatCounter = i.RepeatCounter, PlannedDeliveryDate = i.PlannedDeliveryDate,
                }).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<PcWorkOrderInfoDto>> SaveWorkOrderInfoAsync(PcWorkOrderInfoDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.TypeCode) == null) return Bad<PcWorkOrderInfoDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcWorkOrderInfoDto>(PcText.NotAllowed + " (کدگذاری با واحد برنامه‌ریزی است)");
            WorkOrder wo = await _uow.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == dto.WorkOrderId && !w.Deleted, cancellationToken);
            if (wo == null) return PcText.NotFound<PcWorkOrderInfoDto>(PcText.WorkOrderNotFound);
            await EnsureCodingAsync(cancellationToken);
            string type = PcText.Upper(dto.TypeCode);
            PcCodingTable typeRow = await _uow.Set<PcCodingTable>().AsNoTracking().FirstOrDefaultAsync(r => !r.Deleted && r.Table == "TYPE" && r.Code == type, cancellationToken);
            if (typeRow == null) return Bad<PcWorkOrderInfoDto>("کد TYPE در جدول کدگذاری نیست؛ ابتدا ردیف آن را اضافه کنید.");
            string size = Clean(dto.SizeCode)?.ToUpperInvariant();
            string option = Clean(dto.OptionCode)?.ToUpperInvariant();
            if (size == null || option == null) return Bad<PcWorkOrderInfoDto>("اندازه (۳ نویسه) و گزینه (۲ نویسه) را وارد کنید.");
            if (size.All(char.IsDigit) && size.Length < 3) size = size.PadLeft(3, '0');
            if (size.Length != 3 || size.Any(ch => !char.IsLetterOrDigit(ch))) return Bad<PcWorkOrderInfoDto>("اندازه باید سه نویسه باشد.");
            if (option.Length != 2 || option.Any(ch => !char.IsLetterOrDigit(ch))) return Bad<PcWorkOrderInfoDto>("گزینه باید دو نویسه باشد.");
            List<string> sizeCodes = await _uow.Set<PcCodingTable>().AsNoTracking().Where(r => !r.Deleted && r.Table == "SIZE" && r.Family == typeRow.Family && r.Code != "###").Select(r => r.Code).ToListAsync(cancellationToken);
            if (sizeCodes.Count > 0 && !sizeCodes.Contains(size)) return Bad<PcWorkOrderInfoDto>("اندازه برای این نوع باید یکی از کدهای جدول باشد: " + string.Join("، ", sizeCodes));
            List<string> optionCodes = await _uow.Set<PcCodingTable>().AsNoTracking().Where(r => !r.Deleted && r.Table == "OPTION" && r.Family == typeRow.Family && r.Code != "##").Select(r => r.Code).ToListAsync(cancellationToken);
            if (optionCodes.Count > 0 && !optionCodes.Contains(option)) return Bad<PcWorkOrderInfoDto>("گزینه برای این نوع باید یکی از کدهای جدول باشد: " + string.Join("، ", optionCodes));

            DateTimeOffset now = DateTimeOffset.Now;
            PcWorkOrderInfo info = await _uow.Set<PcWorkOrderInfo>().FirstOrDefaultAsync(i => i.WorkOrderId == wo.Id && !i.Deleted, cancellationToken);
            if (info == null)
            {
                info = new PcWorkOrderInfo { WorkOrderId = wo.Id, ContractId = wo.ContractId, CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcWorkOrderInfo>().Add(info);
            }
            bool same = info.TypeCode == type && info.SizeCode == size && info.OptionCode == option && info.EquipmentProjectCode != null;
            if (!same)
            {
                int others = await _uow.Set<PcWorkOrderInfo>().CountAsync(i => i.ContractId == wo.ContractId && i.WorkOrderId != wo.Id && !i.Deleted && i.TypeCode == type && i.SizeCode == size && i.OptionCode == option, cancellationToken);
                info.RepeatCounter = others + 1;
                if (info.RepeatCounter > 99) return Bad<PcWorkOrderInfoDto>("شمارنده تکرار از ۹۹ گذشته است.");
                info.TypeCode = type; info.SizeCode = size; info.OptionCode = option;
                info.EquipmentProjectCode = $"{type}{size}{option}{info.RepeatCounter:00}";
            }
            info.ProductType = dto.ProductType != 0 && Enum.IsDefined(typeof(ItpProductType), dto.ProductType) ? dto.ProductType : (typeRow.ProductType ?? ItpProductType.Other);
            info.PlannedDeliveryDate = dto.PlannedDeliveryDate?.Date;
            info.ModifyDate = now; info.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Bad<PcWorkOrderInfoDto>("کد پروژه تجهیز تکراری است."); }
            return ResponseDtoHelper.Ok(await InfoDtoAsync(info, cancellationToken));
        }

        // ============================================================================ initiation state machine

        private static readonly (PcInitiationStep Step, string Title, string Gate, string SlaKey)[] StepDefs =
        {
            (PcInitiationStep.AnnouncementReceived, "دریافت ابلاغ قرارداد و فهرست اعلام پروژه", "COMMERCIAL", null),
            (PcInitiationStep.CodeAssigned, "صدور کد قرارداد", "PLANNING", null),
            (PcInitiationStep.UnitsNotified, "اطلاع‌رسانی به واحدها (پیش‌نویس FO-PL-001)", "PLANNING", null),
            (PcInitiationStep.ManagerSelected, "انتخاب مدیر پروژه", "CEO", PcSettingKeys.SlaManager),
            (PcInitiationStep.CharterApproved, "تایید منشور پروژه", "CEO", null),
            (PcInitiationStep.PepApproved, "تایید برنامه اجرایی پروژه (PEP)", "PMO", PcSettingKeys.SlaPep),
            (PcInitiationStep.KomHeld, "برگزاری جلسه شروع با کارفرما", "PROJECTMANAGER", null),
            (PcInitiationStep.BomRequested, "درخواست BOM از مهندسی", "PLANNING", PcSettingKeys.SlaBom),
            (PcInitiationStep.WbsDrafted, "پیش‌نویس WBS (سیستم)", "SYSTEM", null),
            (PcInitiationStep.BaselineApproved, "تایید مبنای زمان‌بندی (سیستم)", "SYSTEM", null),
        };

        private async Task<PcInitiationDto> InitiationDtoAsync(PcProjectInitiation init, CancellationToken cancellationToken)
        {
            ContractEntity c = await _uow.Set<ContractEntity>().AsNoTracking().FirstAsync(x => x.Id == init.ContractId, cancellationToken);
            List<PcInitiationStepRecord> steps = init.Steps != null ? init.Steps.ToList() : await _uow.Set<PcInitiationStepRecord>().AsNoTracking().Where(s => s.InitiationId == init.Id).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, steps.Select(s => s.DoneByUserId), cancellationToken);
            DateTime today = DateTime.Today;
            var dto = new PcInitiationDto { ContractId = c.Id, ContractCode = c.Code, CurrentStep = init.CurrentStep, Completed = init.Completed };
            foreach (var def in StepDefs)
            {
                PcInitiationStepRecord r = steps.FirstOrDefault(s => s.Step == def.Step);
                dto.Steps.Add(new PcInitiationStepDto
                {
                    Step = def.Step, Title = def.Title, GateRole = def.Gate, SlaDays = r?.SlaDays ?? 0, DueDate = r?.DueDate, DoneAt = r?.DoneAt,
                    DoneByName = names.Name(r?.DoneByUserId), Note = r?.Note, Overdue = r != null && !r.DoneAt.HasValue && r.DueDate.HasValue && r.DueDate.Value < today,
                });
            }
            PcContractTerms t = await PcContext.TermsAsync(_uow, c.Id, false, cancellationToken);
            dto.UnitsNoticeDraft = t?.ProjectCode == null ? null :
                $"فرم FO-PL-001 (پیش‌نویس سیستم): قرارداد {c.Code} - {c.Title} با کد پروژه {t.ProjectCode} ابلاغ شد. واحدهای مهندسی، تولید، کنترل کیفیت، بازرگانی و انبار برای شروع برنامه‌ریزی مطلع شوند. تاریخ: {(global::Services.NasbNiro.Finance.FiscalCalendars.Text(DateTime.Today))}";
            return dto;
        }

        private async Task<PcProjectInitiation> LoadInitiationAsync(int contractId, bool track, CancellationToken cancellationToken)
        {
            IQueryable<PcProjectInitiation> q = _uow.Set<PcProjectInitiation>().Include(i => i.Steps).Where(i => i.ContractId == contractId && !i.Deleted);
            if (!track) q = q.AsNoTracking();
            return await q.AsSplitQuery().FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<ResponseDto<PcInitiationDto>> GetInitiationAsync(int contractId, CancellationToken cancellationToken)
        {
            PcProjectInitiation init = await LoadInitiationAsync(contractId, false, cancellationToken);
            if (init == null) return PcText.NotFound<PcInitiationDto>("برای این قرارداد فرآیند شروع پروژه ثبت نشده است.");
            return ResponseDtoHelper.Ok(await InitiationDtoAsync(init, cancellationToken));
        }

        public async Task<ResponseDto<PcInitiationDto>> StartInitiationAsync(int contractId, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole("PMO,PLANNING,COMMERCIAL")) return Bad<PcInitiationDto>(PcText.NotAllowed);
            ContractEntity c = await _uow.Set<ContractEntity>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == contractId && !x.Deleted, cancellationToken);
            if (c == null) return PcText.NotFound<PcInitiationDto>(PcText.ContractNotFound);
            if (await _uow.Set<PcProjectInitiation>().AnyAsync(i => i.ContractId == contractId && !i.Deleted, cancellationToken)) return Bad<PcInitiationDto>("فرآیند شروع این پروژه قبلاً ثبت شده است.");
            DateTimeOffset now = DateTimeOffset.Now;
            var init = new PcProjectInitiation { ContractId = contractId, CurrentStep = PcInitiationStep.CodeAssigned, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me, Steps = new List<PcInitiationStepRecord>() };
            foreach (var def in StepDefs) init.Steps.Add(new PcInitiationStepRecord { Step = def.Step });
            PcInitiationStepRecord first = init.Steps.First(s => s.Step == PcInitiationStep.AnnouncementReceived);
            first.DoneAt = now; first.DoneByUserId = Me;
            _uow.Set<PcProjectInitiation>().Add(init);
            // WI-PL-013 6-2-2-1: a claims file is opened for every contract at project start
            _uow.Set<PcClaim>().Add(new PcClaim
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, PcNumbers.Claim, "CLM", DateTime.Today, cancellationToken), ContractId = contractId,
                Direction = PcClaimDirection.AgainstClient, Issue = "پرونده ادعا (Claims file) در شروع پروژه باز شد (WI-PL-013).", Status = PcClaimStatus.Issue, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
            });
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await InitiationDtoAsync(init, cancellationToken));
        }

        private async Task SetDueAsync(PcProjectInitiation init, PcInitiationStep justDone, CancellationToken cancellationToken)
        {
            int next = (int)justDone + 1;
            PcInitiationStepRecord nr = init.Steps.FirstOrDefault(s => (int)s.Step == next);
            if (nr == null) return;
            var def = StepDefs.First(d => d.Step == nr.Step);
            if (def.SlaKey == null) return;
            Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, cancellationToken);
            int sla = (int)settings[def.SlaKey];
            PcContractTerms t = await PcContext.TermsAsync(_uow, init.ContractId, false, cancellationToken);
            PcWorkCalendar cal = await PcContext.CalendarForContractAsync(_uow, t, cancellationToken);
            nr.SlaDays = sla;
            nr.DueDate = cal.AddWorkingDays(DateTime.Today.AddDays(1), sla - 1 < 0 ? 0 : sla - 1);
        }

        public async Task<string> MarkStepAsync(int contractId, PcInitiationStep step, int userId, bool enforceOrder, string note, CancellationToken cancellationToken)
        {
            PcProjectInitiation init = await LoadInitiationAsync(contractId, true, cancellationToken);
            if (init == null) return null;
            PcInitiationStepRecord rec = init.Steps.FirstOrDefault(s => s.Step == step);
            if (rec == null) return null;
            if (rec.DoneAt.HasValue) return enforceOrder ? "این مرحله قبلاً انجام شده است." : null;
            if (enforceOrder)
            {
                PcInitiationStepRecord pending = init.Steps.Where(s => (int)s.Step < (int)step && !s.DoneAt.HasValue).OrderBy(s => (int)s.Step).FirstOrDefault();
                if (pending != null) return "ابتدا مرحله «" + StepDefs.First(d => d.Step == pending.Step).Title + "» باید انجام شود.";
            }
            rec.DoneAt = DateTimeOffset.Now; rec.DoneByUserId = userId; rec.Note = Clean(note);
            PcInitiationStepRecord nextOpen = init.Steps.Where(s => !s.DoneAt.HasValue).OrderBy(s => (int)s.Step).FirstOrDefault();
            init.Completed = nextOpen == null;
            if (nextOpen != null) init.CurrentStep = nextOpen.Step;
            init.ModifyDate = DateTimeOffset.Now; init.ModifierUserId = userId;
            await SetDueAsync(init, step, cancellationToken);
            return null;
        }

        public async Task<ResponseDto<PcInitiationDto>> InitiationActionAsync(PcInitiationActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcInitiationDto>(PcText.InvalidData);
            PcProjectInitiation init = await LoadInitiationAsync(dto.ContractId, false, cancellationToken);
            if (init == null) return PcText.NotFound<PcInitiationDto>("برای این قرارداد فرآیند شروع پروژه ثبت نشده است.");
            switch (dto.Step)
            {
                case PcInitiationStep.CodeAssigned:
                    return Bad<PcInitiationDto>("کد قرارداد هنگام صدور کد ثبت می‌شود (generate-code).");
                case PcInitiationStep.CharterApproved:
                    return Bad<PcInitiationDto>("تایید منشور از مسیر تایید منشور (charter/approve) توسط مدیرعامل انجام می‌شود.");
                case PcInitiationStep.PepApproved:
                    return Bad<PcInitiationDto>("تایید PEP از مسیر تایید برنامه اجرایی (plan/approve) توسط معاون PMO انجام می‌شود.");
                case PcInitiationStep.WbsDrafted:
                case PcInitiationStep.BaselineApproved:
                case PcInitiationStep.AnnouncementReceived:
                    return Bad<PcInitiationDto>("این مرحله را سیستم ثبت می‌کند.");
                case PcInitiationStep.UnitsNotified:
                    if (!_user.IsInRole("PLANNING,PMO,COMMERCIAL")) return Bad<PcInitiationDto>(PcText.NotAllowed);
                    break;
                case PcInitiationStep.ManagerSelected:
                    if (!_user.IsInRole(ProjectControlRoles.Ceo)) return Bad<PcInitiationDto>(PcText.NotAllowed + " (انتخاب مدیر پروژه با مدیرعامل است)");
                    if (!dto.ProjectManagerUserId.HasValue || !await _uow.Set<User>().AnyAsync(u => u.Id == dto.ProjectManagerUserId.Value, cancellationToken)) return Bad<PcInitiationDto>("مدیر پروژه را انتخاب کنید.");
                    break;
                case PcInitiationStep.KomHeld:
                    if (!_user.IsInRole(ProjectControlRoles.ProjectManagers)) return Bad<PcInitiationDto>(PcText.NotAllowed);
                    break;
                case PcInitiationStep.BomRequested:
                    if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcInitiationDto>(PcText.NotAllowed);
                    break;
                default:
                    return Bad<PcInitiationDto>("مرحله نامعتبر است.");
            }
            if (dto.Step == PcInitiationStep.ManagerSelected)
            {
                PcContractTerms t = await PcContext.TermsAsync(_uow, dto.ContractId, true, cancellationToken);
                if (t == null) return Bad<PcInitiationDto>("ابتدا شرایط قرارداد را ثبت کنید.");
                t.ProjectManagerUserId = dto.ProjectManagerUserId;
                t.ModifyDate = DateTimeOffset.Now; t.ModifierUserId = Me;
            }
            string error = await MarkStepAsync(dto.ContractId, dto.Step, Me, true, dto.Note, cancellationToken);
            if (error != null) return Bad<PcInitiationDto>(error);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await InitiationDtoAsync((await LoadInitiationAsync(dto.ContractId, false, cancellationToken)), cancellationToken));
        }

        public async Task<List<PcInitiationStepDto>> OverdueStepsAsync(CancellationToken cancellationToken)
        {
            DateTime today = DateTime.Today;
            var rows = await _uow.Set<PcInitiationStepRecord>().AsNoTracking()
                .Where(s => !s.DoneAt.HasValue && s.DueDate.HasValue && s.DueDate.Value < today && !s.Initiation.Deleted)
                .Select(s => new { s.Step, s.SlaDays, s.DueDate, ContractId = s.Initiation.ContractId }).ToListAsync(cancellationToken);
            return rows.Select(r => new PcInitiationStepDto
            {
                Step = r.Step, Title = StepDefs.First(d => d.Step == r.Step).Title, GateRole = StepDefs.First(d => d.Step == r.Step).Gate, SlaDays = r.SlaDays, DueDate = r.DueDate, Overdue = true, Note = r.ContractId.ToString(),
            }).ToList();
        }

        public async Task<List<int>> OverdueContractIdsAsync(CancellationToken cancellationToken)
        {
            DateTime today = DateTime.Today;
            return await _uow.Set<PcInitiationStepRecord>().AsNoTracking()
                .Where(s => !s.DoneAt.HasValue && s.DueDate.HasValue && s.DueDate.Value < today && !s.Initiation.Deleted)
                .Select(s => s.Initiation.ContractId).Distinct().ToListAsync(cancellationToken);
        }

        // ============================================================================ charter and PEP

        private static PcCharterDto ToDto(PcProjectCharter c, int contractId, Dictionary<int, string> names) => new PcCharterDto
        {
            ContractId = contractId, Overview = c?.Overview, ScopeDescription = c?.ScopeDescription, Requirements = c?.Requirements, Deliverables = c?.Deliverables, Boundaries = c?.Boundaries,
            TimeBaselineEstimate = c?.TimeBaselineEstimate, CostEstimate = c?.CostEstimate, AlternativesAnalysis = c?.AlternativesAnalysis, Assumptions = c?.Assumptions, Constraints = c?.Constraints,
            Risks = c?.Risks, Stakeholders = c?.Stakeholders, PreAssignedResources = c?.PreAssignedResources, ManagerAuthority = c?.ManagerAuthority, ManagerAcceptedAt = c?.ManagerAcceptedAt,
            ApprovedAt = c?.ApprovedAt, ApprovedByName = names.Name(c?.ApprovedByUserId), RowVersion = c?.RowVersion == null ? null : Convert.ToBase64String(c.RowVersion),
        };

        public async Task<ResponseDto<PcCharterDto>> GetCharterAsync(int contractId, CancellationToken cancellationToken)
        {
            PcProjectCharter c = await _uow.Set<PcProjectCharter>().AsNoTracking().FirstOrDefaultAsync(x => x.ContractId == contractId && !x.Deleted, cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { c?.ApprovedByUserId }, cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(c, contractId, names));
        }

        public async Task<ResponseDto<PcCharterDto>> SaveCharterAsync(PcCharterDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcCharterDto>(PcText.InvalidData);
            if (!_user.IsInRole("PMO,PLANNING,PROJECTMANAGER")) return Bad<PcCharterDto>(PcText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(x => x.Id == dto.ContractId && !x.Deleted, cancellationToken)) return PcText.NotFound<PcCharterDto>(PcText.ContractNotFound);
            DateTimeOffset now = DateTimeOffset.Now;
            PcProjectCharter c = await _uow.Set<PcProjectCharter>().FirstOrDefaultAsync(x => x.ContractId == dto.ContractId && !x.Deleted, cancellationToken);
            if (c == null)
            {
                c = new PcProjectCharter { ContractId = dto.ContractId, CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcProjectCharter>().Add(c);
            }
            else if (c.ApprovedAt.HasValue) return Bad<PcCharterDto>("منشور تاییدشده با اطلاع مدیران ارشد قابل تغییر است؛ تغییر آن از مسیر کنترل تغییرات انجام شود.");
            c.Overview = Clean(dto.Overview); c.ScopeDescription = Clean(dto.ScopeDescription); c.Requirements = Clean(dto.Requirements); c.Deliverables = Clean(dto.Deliverables);
            c.Boundaries = Clean(dto.Boundaries); c.TimeBaselineEstimate = Clean(dto.TimeBaselineEstimate); c.CostEstimate = Clean(dto.CostEstimate);
            c.AlternativesAnalysis = Clean(dto.AlternativesAnalysis); c.Assumptions = Clean(dto.Assumptions); c.Constraints = Clean(dto.Constraints); c.Risks = Clean(dto.Risks);
            c.Stakeholders = Clean(dto.Stakeholders); c.PreAssignedResources = Clean(dto.PreAssignedResources); c.ManagerAuthority = Clean(dto.ManagerAuthority);
            c.ModifyDate = now; c.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcCharterDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(c, c.ContractId, new Dictionary<int, string>()));
        }

        public async Task<ResponseDto<PcCharterDto>> CharterActionAsync(string action, PcApproveDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcCharterDto>(PcText.InvalidData);
            PcProjectCharter c = await _uow.Set<PcProjectCharter>().FirstOrDefaultAsync(x => x.ContractId == dto.ContractId && !x.Deleted, cancellationToken);
            if (c == null) return PcText.NotFound<PcCharterDto>("منشور پروژه ثبت نشده است.");
            DateTimeOffset now = DateTimeOffset.Now;
            if (action == "accept")
            {
                PcContractTerms t = await PcContext.TermsAsync(_uow, dto.ContractId, false, cancellationToken);
                if (t?.ProjectManagerUserId == null) return Bad<PcCharterDto>("مدیر پروژه هنوز انتخاب نشده است.");
                if (t.ProjectManagerUserId.Value != Me) return Bad<PcCharterDto>("فقط مدیر پروژه منصوب‌شده می‌تواند اختیارات را بپذیرد.");
                if (c.ApprovedAt.HasValue) return Bad<PcCharterDto>("منشور قبلاً تایید شده است.");
                c.ManagerAcceptedAt = now;
            }
            else if (action == "approve")
            {
                if (!_user.IsInRole(ProjectControlRoles.Ceo)) return Bad<PcCharterDto>(PcText.NotAllowed + " (تایید منشور با مدیرعامل است)");
                if (c.ApprovedAt.HasValue) return Bad<PcCharterDto>("منشور قبلاً تایید شده است.");
                if (Clean(c.ScopeDescription) == null) return Bad<PcCharterDto>("شرح محدوده منشور خالی است.");
                if (!c.ManagerAcceptedAt.HasValue) return Bad<PcCharterDto>("مدیر پروژه باید اختیارات و تعهد خود را بپذیرد (charter/accept).");
                c.ApprovedAt = now; c.ApprovedByUserId = Me;
                string error = await MarkStepAsync(dto.ContractId, PcInitiationStep.CharterApproved, Me, true, dto.Note, cancellationToken);
                if (error != null) return Bad<PcCharterDto>(error);
            }
            else return Bad<PcCharterDto>("عملیات نامعتبر است.");
            c.ModifyDate = now; c.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { c.ApprovedByUserId }, cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(c, c.ContractId, names));
        }

        private static PcPlanDto ToDto(PcProjectPlan p, int contractId, Dictionary<int, string> names) => new PcPlanDto
        {
            ContractId = contractId, Overview = p?.Overview, ScopeStatement = p?.ScopeStatement, InScope = p?.InScope, OutOfScope = p?.OutOfScope, Constraints = p?.Constraints,
            Assumptions = p?.Assumptions, Milestones = p?.Milestones, LongLeadRules = p?.LongLeadRules, CommunicationPlan = p?.CommunicationPlan, MakeBuyPartner = p?.MakeBuyPartner,
            QualityPlan = p?.QualityPlan, ChangeManagement = p?.ChangeManagement, OtherSections = p?.OtherSections, ApprovedAt = p?.ApprovedAt, ApprovedByName = names.Name(p?.ApprovedByUserId),
            RowVersion = p?.RowVersion == null ? null : Convert.ToBase64String(p.RowVersion),
        };

        public async Task<ResponseDto<PcPlanDto>> GetPlanAsync(int contractId, CancellationToken cancellationToken)
        {
            PcProjectPlan p = await _uow.Set<PcProjectPlan>().AsNoTracking().FirstOrDefaultAsync(x => x.ContractId == contractId && !x.Deleted, cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { p?.ApprovedByUserId }, cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(p, contractId, names));
        }

        public async Task<ResponseDto<PcPlanDto>> SavePlanAsync(PcPlanDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcPlanDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm)) return Bad<PcPlanDto>(PcText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(x => x.Id == dto.ContractId && !x.Deleted, cancellationToken)) return PcText.NotFound<PcPlanDto>(PcText.ContractNotFound);
            DateTimeOffset now = DateTimeOffset.Now;
            PcProjectPlan p = await _uow.Set<PcProjectPlan>().FirstOrDefaultAsync(x => x.ContractId == dto.ContractId && !x.Deleted, cancellationToken);
            if (p == null)
            {
                p = new PcProjectPlan { ContractId = dto.ContractId, CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcProjectPlan>().Add(p);
            }
            else if (p.ApprovedAt.HasValue) return Bad<PcPlanDto>("برنامه اجرایی تاییدشده را مدیر پروژه از مسیر کنترل تغییرات به‌روز می‌کند.");
            p.Overview = Clean(dto.Overview); p.ScopeStatement = Clean(dto.ScopeStatement); p.InScope = Clean(dto.InScope); p.OutOfScope = Clean(dto.OutOfScope);
            p.Constraints = Clean(dto.Constraints); p.Assumptions = Clean(dto.Assumptions); p.Milestones = Clean(dto.Milestones); p.LongLeadRules = Clean(dto.LongLeadRules);
            p.CommunicationPlan = Clean(dto.CommunicationPlan); p.MakeBuyPartner = Clean(dto.MakeBuyPartner); p.QualityPlan = Clean(dto.QualityPlan);
            p.ChangeManagement = Clean(dto.ChangeManagement); p.OtherSections = Clean(dto.OtherSections);
            p.ModifyDate = now; p.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcPlanDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(p, p.ContractId, new Dictionary<int, string>()));
        }

        public async Task<ResponseDto<PcPlanDto>> ApprovePlanAsync(PcApproveDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcPlanDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Pmo)) return Bad<PcPlanDto>(PcText.NotAllowed + " (تایید PEP با معاون PMO است)");
            PcProjectPlan p = await _uow.Set<PcProjectPlan>().FirstOrDefaultAsync(x => x.ContractId == dto.ContractId && !x.Deleted, cancellationToken);
            if (p == null) return PcText.NotFound<PcPlanDto>("برنامه اجرایی پروژه ثبت نشده است.");
            if (p.ApprovedAt.HasValue) return Bad<PcPlanDto>("برنامه قبلاً تایید شده است.");
            if (Clean(p.ScopeStatement) == null) return Bad<PcPlanDto>("بیانیه محدوده در برنامه اجرایی خالی است.");
            p.ApprovedAt = DateTimeOffset.Now; p.ApprovedByUserId = Me; p.ModifyDate = p.ApprovedAt.Value; p.ModifierUserId = Me;
            string error = await MarkStepAsync(dto.ContractId, PcInitiationStep.PepApproved, Me, true, dto.Note, cancellationToken);
            if (error != null) return Bad<PcPlanDto>(error);
            await _uow.SaveChangesAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { p.ApprovedByUserId }, cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(p, p.ContractId, names));
        }

        // ============================================================================ contract review (ISO 9001 8.2.3)

        private static PcReviewDto ToDto(PcContractReview r) => new PcReviewDto
        {
            Id = r.Id, ContractId = r.ContractId, TenderCode = r.TenderCode, Kind = r.Kind, RequirementsDefined = r.RequirementsDefined, DifferencesResolved = r.DifferencesResolved,
            CapabilityChecked = r.CapabilityChecked, DeliveryFeasible = r.DeliveryFeasible, StandardsItpAgreed = r.StandardsItpAgreed, GuaranteesReviewed = r.GuaranteesReviewed,
            PenaltiesReviewed = r.PenaltiesReviewed, PaymentTermsReviewed = r.PaymentTermsReviewed, CurrencyReviewed = r.CurrencyReviewed, Notes = r.Notes, Decision = r.Decision,
            DecisionNote = r.DecisionNote, DecidedAt = r.DecidedAt, RowVersion = r.RowVersion == null ? null : Convert.ToBase64String(r.RowVersion),
        };

        public async Task<ResponseDto<PagedList<PcReviewDto>>> GetReviewsAsync(PcContractQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcContractQuery();
            IQueryable<PcContractReview> q = _uow.Set<PcContractReview>().AsNoTracking().Where(r => !r.Deleted);
            if (query.ContractId.HasValue) q = q.Where(r => r.ContractId == query.ContractId.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(r => r.TenderCode.Contains(text) || r.Notes.Contains(text));
            IQueryable<PcReviewDto> projection = q.OrderByDescending(r => r.Id).Select(r => new PcReviewDto
            {
                Id = r.Id, ContractId = r.ContractId, TenderCode = r.TenderCode, Kind = r.Kind, RequirementsDefined = r.RequirementsDefined, DifferencesResolved = r.DifferencesResolved,
                CapabilityChecked = r.CapabilityChecked, DeliveryFeasible = r.DeliveryFeasible, StandardsItpAgreed = r.StandardsItpAgreed, GuaranteesReviewed = r.GuaranteesReviewed,
                PenaltiesReviewed = r.PenaltiesReviewed, PaymentTermsReviewed = r.PaymentTermsReviewed, CurrencyReviewed = r.CurrencyReviewed, Notes = r.Notes, Decision = r.Decision,
                DecisionNote = r.DecisionNote, DecidedAt = r.DecidedAt,
            });
            return ResponseDtoHelper.Ok(await PagedList<PcReviewDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<PcReviewDto>> SaveReviewAsync(PcReviewDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || (!dto.ContractId.HasValue && Clean(dto.TenderCode) == null)) return Bad<PcReviewDto>("قرارداد یا کد مناقصه را مشخص کنید.");
            if (!_user.IsInRole("COMMERCIAL,PMO,PLANNING")) return Bad<PcReviewDto>(PcText.NotAllowed);
            if (!Enum.IsDefined(typeof(PcReviewKind), dto.Kind)) return Bad<PcReviewDto>("نوع بازنگری (محصول استاندارد یا سفارش ویژه) را مشخص کنید.");
            if (dto.ContractId.HasValue && !await _uow.Set<ContractEntity>().AnyAsync(x => x.Id == dto.ContractId.Value && !x.Deleted, cancellationToken)) return PcText.NotFound<PcReviewDto>(PcText.ContractNotFound);
            DateTimeOffset now = DateTimeOffset.Now;
            PcContractReview r;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<PcContractReview>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (r == null) return PcText.NotFound<PcReviewDto>("بازنگری پیدا نشد.");
                if (r.Decision != PcReviewDecision.Pending) return Bad<PcReviewDto>("بازنگری تصمیم‌گرفته‌شده تغییر نمی‌کند؛ بازنگری جدید ثبت کنید.");
            }
            else
            {
                r = new PcContractReview { CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcContractReview>().Add(r);
            }
            r.ContractId = dto.ContractId; r.TenderCode = Clean(dto.TenderCode); r.Kind = dto.Kind;
            r.RequirementsDefined = dto.RequirementsDefined; r.DifferencesResolved = dto.DifferencesResolved; r.DeliveryFeasible = dto.DeliveryFeasible;
            r.StandardsItpAgreed = dto.StandardsItpAgreed; r.GuaranteesReviewed = dto.GuaranteesReviewed; r.PenaltiesReviewed = dto.PenaltiesReviewed;
            r.PaymentTermsReviewed = dto.PaymentTermsReviewed; r.CurrencyReviewed = dto.CurrencyReviewed; r.Notes = Clean(dto.Notes);
            // the capability check belongs to Planning: it is set only by the capability-check action
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcReviewDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(r));
        }

        public async Task<ResponseDto<PcReviewDto>> ReviewActionAsync(PcReviewActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcReviewDto>(PcText.InvalidData);
            PcContractReview r = await _uow.Set<PcContractReview>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (r == null) return PcText.NotFound<PcReviewDto>("بازنگری پیدا نشد.");
            if (r.Decision != PcReviewDecision.Pending) return Bad<PcReviewDto>("درباره این بازنگری تصمیم گرفته شده است.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "capability-check":
                    if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcReviewDto>(PcText.NotAllowed + " (توانایی اجرا را برنامه‌ریزی تایید می‌کند)");
                    r.CapabilityChecked = true; r.CapabilityCheckedByUserId = Me;
                    break;
                case "decide":
                    if (!_user.IsInRole(ProjectControlRoles.Ceo)) return Bad<PcReviewDto>(PcText.NotAllowed + " (تصمیم با مدیرعامل است)");
                    if (dto.Decision != PcReviewDecision.Accept && dto.Decision != PcReviewDecision.Reject && dto.Decision != PcReviewDecision.Modify) return Bad<PcReviewDto>("تصمیم را مشخص کنید.");
                    if (dto.Decision == PcReviewDecision.Accept)
                    {
                        var missing = new List<string>();
                        if (!r.RequirementsDefined) missing.Add("الزامات تعریف‌شده");
                        if (!r.DifferencesResolved) missing.Add("رفع اختلافات");
                        if (!r.DeliveryFeasible) missing.Add("امکان تحویل در موعد");
                        if (!r.StandardsItpAgreed) missing.Add("استانداردها و ITP");
                        if (!r.GuaranteesReviewed) missing.Add("ضمانت‌نامه‌ها");
                        if (!r.PenaltiesReviewed) missing.Add("جرایم");
                        if (!r.PaymentTermsReviewed) missing.Add("شرایط پرداخت");
                        if (!r.CurrencyReviewed) missing.Add("ارز و نرخ تبدیل");
                        if (r.Kind == PcReviewKind.SpecialOrder && !r.CapabilityChecked) missing.Add("تایید توانایی اجرا توسط برنامه‌ریزی");
                        if (missing.Count > 0) return Bad<PcReviewDto>("پذیرش سفارش پیش از تکمیل بازنگری ممکن نیست: " + string.Join("، ", missing));
                    }
                    else if (Clean(dto.Note) == null) return Bad<PcReviewDto>("دلیل تصمیم را بنویسید.");
                    r.Decision = dto.Decision; r.DecisionNote = Clean(dto.Note); r.DecidedAt = now; r.DecidedByUserId = Me;
                    break;
                default:
                    return Bad<PcReviewDto>("عملیات نامعتبر است.");
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcReviewDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(r));
        }

        // ============================================================================ amendments

        private async Task<PcAmendmentDto> ToDtoAsync(PcContractAmendment a, CancellationToken cancellationToken)
        {
            List<PcAmendmentCopy> copies = a.Copies != null ? a.Copies.ToList() : await _uow.Set<PcAmendmentCopy>().AsNoTracking().Where(x => x.AmendmentId == a.Id).ToListAsync(cancellationToken);
            return new PcAmendmentDto
            {
                Id = a.Id, Number = a.Number, ContractId = a.ContractId, Type = a.Type, Origin = a.Origin, Description = a.Description, PriceDelta = a.PriceDelta, DaysDelta = a.DaysDelta,
                CeoApprovedAt = a.CeoApprovedAt, CustomerApprovalReference = a.CustomerApprovalReference, CustomerApprovalDate = a.CustomerApprovalDate,
                Copies = copies.OrderBy(c => c.Id).Select(c => new PcAmendmentCopyDto { Id = c.Id, Recipient = c.Recipient, SentAt = c.SentAt }).ToList(),
            };
        }

        public async Task<ResponseDto<PagedList<PcAmendmentDto>>> GetAmendmentsAsync(PcContractQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcContractQuery();
            IQueryable<PcContractAmendment> q = _uow.Set<PcContractAmendment>().AsNoTracking().Where(a => !a.Deleted);
            if (query.ContractId.HasValue) q = q.Where(a => a.ContractId == query.ContractId.Value);
            IQueryable<PcAmendmentDto> projection = q.OrderByDescending(a => a.Id).Select(a => new PcAmendmentDto
            {
                Id = a.Id, Number = a.Number, ContractId = a.ContractId, Type = a.Type, Origin = a.Origin, Description = a.Description, PriceDelta = a.PriceDelta, DaysDelta = a.DaysDelta,
                CeoApprovedAt = a.CeoApprovedAt, CustomerApprovalReference = a.CustomerApprovalReference, CustomerApprovalDate = a.CustomerApprovalDate,
            });
            return ResponseDtoHelper.Ok(await PagedList<PcAmendmentDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<PcAmendmentDto>> SaveAmendmentAsync(PcAmendmentDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Description) == null) return Bad<PcAmendmentDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Commercial)) return Bad<PcAmendmentDto>(PcText.NotAllowed);
            if (!Enum.IsDefined(typeof(PcAmendmentType), dto.Type) || !Enum.IsDefined(typeof(PcAmendmentOrigin), dto.Origin)) return Bad<PcAmendmentDto>("نوع و منشأ الحاقیه را مشخص کنید.");
            if (!await _uow.Set<ContractEntity>().AnyAsync(x => x.Id == dto.ContractId && !x.Deleted, cancellationToken)) return PcText.NotFound<PcAmendmentDto>(PcText.ContractNotFound);
            DateTimeOffset now = DateTimeOffset.Now;
            PcContractAmendment a;
            if (dto.Id.HasValue)
            {
                a = await _uow.Set<PcContractAmendment>().Include(x => x.Copies).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (a == null) return PcText.NotFound<PcAmendmentDto>("الحاقیه پیدا نشد.");
                if (a.CeoApprovedAt.HasValue) return Bad<PcAmendmentDto>("الحاقیه تاییدشده توسط مدیرعامل تغییر نمی‌کند.");
            }
            else
            {
                a = new PcContractAmendment
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, PcNumbers.Amendment, "AMD", DateTime.Today, cancellationToken), ContractId = dto.ContractId, CreateDate = now, CreatorUserId = Me,
                    Copies = new List<PcAmendmentCopy> { new PcAmendmentCopy { Recipient = "مشتری" }, new PcAmendmentCopy { Recipient = "واحد فروش" }, new PcAmendmentCopy { Recipient = "مالی" } },
                };
                _uow.Set<PcContractAmendment>().Add(a);
            }
            a.Type = dto.Type; a.Origin = dto.Origin; a.Description = Clean(dto.Description); a.PriceDelta = dto.PriceDelta; a.DaysDelta = dto.DaysDelta;
            a.ModifyDate = now; a.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await ToDtoAsync(a, cancellationToken));
        }

        public async Task<ResponseDto<PcAmendmentDto>> AmendmentActionAsync(PcAmendmentActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcAmendmentDto>(PcText.InvalidData);
            PcContractAmendment a = await _uow.Set<PcContractAmendment>().Include(x => x.Copies).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (a == null) return PcText.NotFound<PcAmendmentDto>("الحاقیه پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "ceo-approve":
                    if (!_user.IsInRole(ProjectControlRoles.Ceo)) return Bad<PcAmendmentDto>(PcText.NotAllowed);
                    if (a.CeoApprovedAt.HasValue) return Bad<PcAmendmentDto>("قبلاً تایید شده است.");
                    a.CeoApprovedAt = now; a.CeoApprovedByUserId = Me;
                    break;
                case "customer-approve":
                    if (!_user.IsInRole(ProjectControlRoles.Commercial)) return Bad<PcAmendmentDto>(PcText.NotAllowed);
                    if (!a.CeoApprovedAt.HasValue) return Bad<PcAmendmentDto>("ابتدا مدیرعامل باید الحاقیه را تایید کند.");
                    if (Clean(dto.Reference) == null) return Bad<PcAmendmentDto>("شماره نامه تایید مشتری را وارد کنید.");
                    a.CustomerApprovalReference = Clean(dto.Reference); a.CustomerApprovalDate = (dto.Date ?? DateTime.Today).Date;
                    break;
                case "copy-sent":
                    if (!_user.IsInRole(ProjectControlRoles.Commercial)) return Bad<PcAmendmentDto>(PcText.NotAllowed);
                    PcAmendmentCopy copy = a.Copies.FirstOrDefault(c => c.Id == dto.CopyId);
                    if (copy == null) return Bad<PcAmendmentDto>("نسخه پیدا نشد.");
                    copy.SentAt = (dto.Date ?? DateTime.Today).Date;
                    break;
                default:
                    return Bad<PcAmendmentDto>("عملیات نامعتبر است.");
            }
            a.ModifyDate = now; a.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await ToDtoAsync(a, cancellationToken));
        }
    }
}
