using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using ViewModels.NasbNiro.ReportsDtos;

namespace Services.NasbNiro.Reports
{
    public interface IProgressReports
    {
        Task<ResponseDto<ResponseProgressReportDto>> GetProgressReportByWorkOrderIdAsync(GetProgressReportDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ResponseProgressReportDto>> GetProgressReportByContractIdAsync(GetProgressReportDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<IEnumerable<ProgressReportDto>>> GetAllProgressReportAsync(DateTimeOffset toDate, CancellationToken cancellationToken);
    }
}
