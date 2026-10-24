using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Finance
{
    /// <summary>Which legal entity a request works on: the one asked for, else the user's default, else the main company.</summary>
    internal static class EntityScope
    {
        public const string DefaultKey = "DefaultLegalEntityId";

        public static async Task<int> ResolveAsync(IUnitOfWork uow, int? requested, int userId, CancellationToken ct)
        {
            if (requested.HasValue && requested.Value > 0) return requested.Value;
            string v = await uow.Set<FnUserSetting>().Where(x => x.UserId == userId && x.Key == DefaultKey).Select(x => x.Value).FirstOrDefaultAsync(ct);
            return int.TryParse(v, out int id) && id > 0 ? id : FinanceSeed.MainEntityId;
        }
    }

    public interface ILegalEntityService
    {
        Task<ResponseDto<List<LegalEntityDto>>> GetEntitiesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<LegalEntityDto>> SaveEntityAsync(SaveLegalEntityDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<LegalEntityDto>> SetDefaultEntityAsync(SetDefaultEntityDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<IntercompanyPairDto>>> GetPairsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<IntercompanyPairDto>> SavePairAsync(SaveIntercompanyPairDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<IntercompanyResultDto>> PostIntercompanyAsync(IntercompanyPostDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ConsolidationDto>> ConsolidationAsync(ConsolidationQuery query, CancellationToken cancellationToken);
    }

    public interface IGlControlsService
    {
        Task<ResponseDto<PagedList<AuditLogDto>>> AuditAsync(AuditQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<CloseChecklistDto>> GetChecklistAsync(int periodId, CancellationToken cancellationToken);
        Task<ResponseDto<CloseChecklistDto>> OverrideChecklistAsync(int periodId, ChecklistOverrideDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<FinanceSettingDto>>> GetSettingsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<FinanceSettingDto>> SaveSettingAsync(SaveFinanceSettingDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<VoucherAttachmentDto>>> GetAttachmentsAsync(int voucherId, CancellationToken cancellationToken);
        Task<ResponseDto<VoucherAttachmentDto>> AddAttachmentAsync(AddAttachmentDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> RemoveAttachmentAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<List<AttachmentRuleDto>>> GetRulesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<AttachmentRuleDto>> SaveRuleAsync(SaveAttachmentRuleDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<ApprovalThresholdDto>>> GetThresholdsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<ApprovalThresholdDto>> SaveThresholdAsync(SaveApprovalThresholdDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<VoucherDto>> CopyVoucherAsync(VoucherIdDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<VoucherTemplateDto>>> GetTemplatesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<VoucherTemplateDto>> SaveTemplateAsync(SaveVoucherTemplateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<GenerateTemplatesResultDto>> GenerateTemplatesAsync(GenerateTemplatesDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<MassVoucherResultDto>> MassImportAsync(MassVoucherImportDto dto, CancellationToken cancellationToken);
    }

    public interface IMigrationService
    {
        Task<ResponseDto<ImportReportDto>> ImportAccountsAsync(SepidarAccountImportDto dto, bool commit, CancellationToken cancellationToken);
        Task<ResponseDto<ImportReportDto>> ImportDetailsAsync(SepidarDetailImportDto dto, bool commit, CancellationToken cancellationToken);
        Task<ResponseDto<FnDetailLinkReportDto>> LinkDetailsAsync(FnDetailLinkDto dto, bool commit, CancellationToken cancellationToken);
        Task<ResponseDto<OpeningReportDto>> ImportOpeningBalancesAsync(SepidarOpeningImportDto dto, bool commit, CancellationToken cancellationToken);
        Task<ResponseDto<FiscalYearDto>> CreateImportedYearAsync(CreateImportedYearDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<HistoricalReportDto>> ImportHistoricalAsync(HistoricalImportDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ParallelRunDto>> CompareAsync(ParallelCompareDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<ParallelRunDto>>> GetRunsAsync(int? periodId, CancellationToken cancellationToken);
        Task<ResponseDto<ParallelRunDto>> GetRunAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<ParallelRunDto>> ExplainAsync(int runId, ExplainDifferenceDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ParallelRunDto>> SignOffAsync(int runId, SignOffDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ParallelStatusDto>> ParallelStatusAsync(int? legalEntityId, CancellationToken cancellationToken);
    }
}
