using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Common.Results;

using ViewModels.NasbNiro.ReportsDtos;

namespace Services.NasbNiro.Reports
{
    public interface IEquipmentEstimationReports
    {
        ValueTask<ResponseDto<EquipmentEstimationReportDto>> GetEquipmentEstimationReportByIdAsync(int equipmentId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<EquipmentEstimationReportDto>>> GetAllEquipmentEstimationReportsAsync(CancellationToken cancellationToken);
    }
}
