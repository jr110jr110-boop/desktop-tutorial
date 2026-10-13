using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using ViewModels.NasbNiro.ReportsDtos;

namespace Services.NasbNiro.Reports
{
    public interface IMaterialBalanceReports
    {
        Task<ResponseDto<MaterialBalanceDto>> GetMaterialBalanceReportByBomIdAsync(int bomId, CancellationToken cancellationToken);
        Task<ResponseDto<IEnumerable<MaterialBalanceDto>>> GetAllMaterialBalanceReportsAsync(int equipmentId, CancellationToken cancellationToken);
    }
}
