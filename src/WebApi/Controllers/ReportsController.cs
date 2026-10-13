using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Reports;
using ViewModels.NasbNiro.ReportsDtos;

namespace WebApi.Controllers
{
    [Authorize]
    public class ReportsController : BaseApiController
    {
        private readonly IEquipmentEstimationReports _eer;
        private readonly IProgressReports _progressReports;
        private readonly IMaterialBalanceReports _materialBalanceReports;

        public ReportsController(
            IEquipmentEstimationReports _eer,
            IProgressReports progressReports,
            IMaterialBalanceReports materialBalanceReports)
        {
            this._eer = _eer;
            _progressReports = progressReports;
            _materialBalanceReports = materialBalanceReports;
        }

        [HttpGet("equipmentEstimationReports/{id:int}")]
        [ProducesResponseType(typeof(ResponseDto<EquipmentEstimationReportDto>), (int)HttpStatusCode.OK)]
        public async Task<ResponseDto<EquipmentEstimationReportDto>> GetEquipmentEstimationsReportByIdAsync(int id, CancellationToken cancellationToken)
        {
            var result = await _eer.GetEquipmentEstimationReportByIdAsync(id, cancellationToken);

            return result;
        }

        [HttpGet("equipmentEstimationReports/")]
        [ProducesResponseType(typeof(ResponseDto<EquipmentEstimationReportDto>), (int)HttpStatusCode.OK)]
        public async Task<ResponseDto<IEnumerable<EquipmentEstimationReportDto>>> GetAllEquipmentEstimationsReportAsync(CancellationToken cancellationToken)
        {
            var result = await _eer.GetAllEquipmentEstimationReportsAsync(cancellationToken);

            return result;
        }

        [HttpGet("progressReport_workOrderId")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProgressReportDto>), (int)HttpStatusCode.OK)]
        public async Task<ResponseDto<ResponseProgressReportDto>> GetGrogressReportByWorkOrderIdAsync([FromQuery] GetProgressReportDto dto, CancellationToken cancellationToken)
        {
            var result = await _progressReports.GetProgressReportByWorkOrderIdAsync(dto, cancellationToken);

            return result;
        }

        [HttpGet("progressReport_ContractId/")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProgressReportDto>), (int)HttpStatusCode.OK)]
        public async Task<ResponseDto<ResponseProgressReportDto>> GetProgressReportByContractIdAsync([FromQuery] GetProgressReportDto dto, CancellationToken cancellationToken)
        {
            var result = await _progressReports.GetProgressReportByContractIdAsync(dto, cancellationToken);

            return result;
        }

        [HttpGet("progressReport/")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<ProgressReportDto>>), (int)HttpStatusCode.OK)]
        public async Task<ResponseDto<IEnumerable<ProgressReportDto>>> GetAllGrogressReportsAsync(DateTimeOffset fromDate, CancellationToken cancellationToken)
        {
            var result = await _progressReports.GetAllProgressReportAsync(fromDate, cancellationToken);

            return result;
        }

        [HttpGet("materialBalance_bomId/{id:int}")]
        [ProducesResponseType(typeof(ResponseDto<MaterialBalanceDto>), (int)HttpStatusCode.OK)]
        public async Task<ResponseDto<MaterialBalanceDto>> GetMaterialBalanceReportByBomIdAsync(int id, CancellationToken cancellationToken)
        {
            var result = await _materialBalanceReports.GetMaterialBalanceReportByBomIdAsync(id, cancellationToken);

            return result;
        }

        [HttpGet("materialBalance_equipmentId/{id:int}")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<MaterialBalanceDto>>), (int)HttpStatusCode.OK)]
        public async Task<ResponseDto<IEnumerable<MaterialBalanceDto>>> GetAllMaterialBalanceReportsAsync(int id, CancellationToken cancellationToken)
        {
            var result = await _materialBalanceReports.GetAllMaterialBalanceReportsAsync(id, cancellationToken);

            return result;
        }
    }
}
