using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.GuardToolkit;
using Common.Results;

using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;
using Entities.Model.NasbNiro.Reports;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.ExternalServices;
using ViewModels.NasbNiro.ReportsDtos;

namespace Services.NasbNiro.Reports
{
    public class ReportsService :
        IEquipmentEstimationReports, IProgressReports, IMaterialBalanceReports
    {
        private readonly IUnitOfWork _uow;
        private readonly DbSet<Equipment> _equipments;
        private readonly DbSet<EquipmentAssembly> _equipmentAssemblies;
        private readonly DbSet<EquipmentBom> _equipmentBoms;
        private readonly DbSet<EquipmentProcess> _equipmentProcesses;
        private readonly DbSet<ProgressReport> _progressReports;
        private readonly DbSet<Bom> _boms;
        private readonly IMapper _mapper;
        private readonly StoreService _storeService;

        public ReportsService(IUnitOfWork uow, IMapper mapper, StoreService storeService)
        {
            _uow = uow.ThrowIfNull(nameof(uow));
            _equipments = uow.Set<Equipment>().ThrowIfNull(nameof(_equipments));
            _equipmentAssemblies = uow.Set<EquipmentAssembly>().ThrowIfNull(nameof(_equipmentAssemblies));
            _equipmentBoms = uow.Set<EquipmentBom>().ThrowIfNull(nameof(_equipmentBoms));
            _equipmentProcesses = uow.Set<EquipmentProcess>().ThrowIfNull(nameof(_equipmentProcesses));
            _progressReports = uow.Set<ProgressReport>().ThrowIfNull(nameof(_progressReports));
            _boms = uow.Set<Bom>().ThrowIfNull(nameof(_boms));
            _mapper = mapper;
            _storeService = storeService.ThrowIfNull(nameof(storeService));
        }

        #region IEquipmentEstimationReports
        public async ValueTask<ResponseDto<IEnumerable<EquipmentEstimationReportDto>>> GetAllEquipmentEstimationReportsAsync(CancellationToken cancellationToken)
        {
            int[] eqIds = await _equipments
                .Where(x => !x.Deleted)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken);

            List<EquipmentEstimationReportDto> result = new List<EquipmentEstimationReportDto>();
            StringBuilder sb = new StringBuilder();

            foreach (var id in eqIds)
            {
                try
                {
                    result.Add(await GetEquipmentEstimationReportAsync(id, cancellationToken));
                }
                catch (Exception ex)
                {
                    sb.AppendLine($" :: eqId: {id} <> message: {ex.Message} ::");
                }
            }

            return ResponseDtoHelper.Ok<IEnumerable<EquipmentEstimationReportDto>>(result, sb.ToString());
        }

        public async ValueTask<ResponseDto<EquipmentEstimationReportDto>> GetEquipmentEstimationReportByIdAsync(
            int equipmentId, CancellationToken cancellationToken)
        {
            EquipmentEstimationReportDto eer = await GetEquipmentEstimationReportAsync(equipmentId, cancellationToken);

            return eer == null
                 ? ResponseDtoHelper.NotFound<EquipmentEstimationReportDto>("محصول یافت نشد.")
                 : ResponseDtoHelper.Ok(eer);
        }

        private async ValueTask<EquipmentEstimationReportDto> GetEquipmentEstimationReportAsync(int id, CancellationToken cancellationToken)
        {
            if (!await _equipments.AnyAsync(x => x.Id == id && !x.Deleted))
            {
                return null;
            }

            string title = await _equipments.Where(x => x.Id == id).Select(x => x.Title).SingleOrDefaultAsync(cancellationToken);

            decimal priceEstimation = await _equipmentProcesses
                .Where(x => !x.Deleted && x.EquipmentBom.EquipmentAssembly.EquipmentId == id)
                .Select(x => x.ProgramPrice)
                .SumAsync(cancellationToken);

            int timeEstimation = await GetEquipmentTimeEstimationAsync(id, cancellationToken);

            var result = new EquipmentEstimationReportDto()
            {
                Id = id,
                Title = title,
                PriceEstimation = priceEstimation,
                TimeEstimation = timeEstimation
            };

            return result;
        }

        private struct BomTotalDuration
        {
            public int EquipmentBomId { get; set; }
            public int? EquipmentBomPredecessorId { get; set; }
            public int EquipmentAssemblyId { get; set; }
            public int? EquipmentAssemblyPredecessorId { get; set; }
            public int TotalBomDuration { get; set; }
            public int AssemblerDuration { get; set; }
        }

        private async Task<int> GetEquipmentTimeEstimationAsync(int eqId, CancellationToken cancellationToken)
        {
            IEnumerable<BomTotalDuration> epTimes = await _equipmentBoms
                .Where(x => !x.Deleted && x.EquipmentAssembly.EquipmentId == eqId)
                .Select(x => new BomTotalDuration
                {
                    EquipmentBomId = x.Id,
                    EquipmentBomPredecessorId = x.PredecessorId,
                    EquipmentAssemblyId = x.EquipmentAssemblyId,
                    EquipmentAssemblyPredecessorId = x.EquipmentAssembly.PredecessorId,
                    TotalBomDuration = x.EquipmentProcesses
                        .Where(ep => !ep.Deleted)
                        .Select(ep => ep.MapDuration + ep.ProcessDuration + ep.MasterDuration)
                        .Sum(),
                    AssemblerDuration = x.EquipmentAssembly.AssemblerDuration
                })
                .ToListAsync(cancellationToken);

            //epTimes = GetValidBomTotalDurations(epTimes);
            ValidatePredecessors(epTimes);

            List<ValueTuple<int, int?, int>> ebId_ebPreId_duration = CalculateEquipmentBomsDuration(epTimes);

            var durations = new Queue<BomTotalDuration>(epTimes);
            var eaId_eaPreId_duration = new List<ValueTuple<int, int?, int>>();

            while (durations.Any())
            {
                BomTotalDuration current = durations.Dequeue();
                int equipmentAssemblyTotalDuration = 0;

                if (current.EquipmentAssemblyPredecessorId != null)
                {
                    int? predecessorId = current.EquipmentAssemblyPredecessorId;
                    if (eaId_eaPreId_duration.Any(x => x.Item1 == current.EquipmentAssemblyPredecessorId))
                    {
                        while (predecessorId != null)
                        {
                            var pre = eaId_eaPreId_duration.Where(x => x.Item1 == predecessorId).First();
                            equipmentAssemblyTotalDuration += pre.Item3;

                            predecessorId = pre.Item2;
                        }
                    }
                    else
                    {
                        durations.Enqueue(current);
                        continue;
                    }
                }

                var equipmentBomsOfEquipmentAssembly = epTimes
                    .Where(x => x.EquipmentAssemblyId == current.EquipmentAssemblyId)
                    .Select(x => x.EquipmentBomId);

                var equipmentBomsTimes = ebId_ebPreId_duration
                      .Where(x => equipmentBomsOfEquipmentAssembly.Contains(x.Item1));

                if (equipmentBomsTimes.Any())
                {
                    equipmentAssemblyTotalDuration += equipmentBomsTimes.Max(x => x.Item3);
                }

                equipmentAssemblyTotalDuration += current.AssemblerDuration;

                eaId_eaPreId_duration.Add(
                    (current.EquipmentAssemblyId, current.EquipmentAssemblyPredecessorId, equipmentAssemblyTotalDuration));
            }

            if (eaId_eaPreId_duration.Any())
            {
                return eaId_eaPreId_duration.Select(x => x.Item3).Max();
            }

            return 0;
        }

        private void ValidatePredecessors(IEnumerable<BomTotalDuration> epTimes)
        {
            #region validate equipment assembly predecessors
            var eaPreIds = epTimes
                 .Where(x => x.EquipmentAssemblyPredecessorId != null)
                 .Select(x => x.EquipmentAssemblyPredecessorId);

            if (!eaPreIds.All(x => epTimes.Any(ep => ep.EquipmentAssemblyId == x)))
            {
                throw new InvalidOperationException("پیشنیاز تامین نشده.");
            }
            #endregion

            #region validate equipment bom predecessors
            var ebPreIds = epTimes
               .Where(x => x.EquipmentBomPredecessorId != null)
               .Select(x => x.EquipmentBomPredecessorId);

            if (!ebPreIds.All(x => epTimes.Any(ep => ep.EquipmentBomPredecessorId == x)))
            {
                throw new InvalidOperationException("پیشنیاز تامین نشده.");
            }
            #endregion
        }

        //private IEnumerable<BomTotalDuration> GetValidBomTotalDurations(IEnumerable<BomTotalDuration> epTimes)
        //{
        //    #region validate equipment assembly predecessors
        //    var eaId_eaPreId = epTimes.Select(x => (x.EquipmentAssemblyId, x.EquipmentAssemblyPredecessorId));
        //    IEnumerable<int> validEaIds = RemoveIdsWherePredecessorNotFound(eaId_eaPreId);
        //    epTimes = epTimes.Where(x => validEaIds.Contains(x.EquipmentAssemblyId));

        //    #endregion

        //    #region validate equipment bom predecessors
        //    var ebId_ebPreId = epTimes.Select(x => (x.EquipmentBomId, x.EquipmentBomPredecessorId));
        //    IEnumerable<int> validEbIds = RemoveIdsWherePredecessorNotFound(ebId_ebPreId);
        //    epTimes = epTimes.Where(x => validEbIds.Contains(x.EquipmentBomId));

        //    //var ebPreIds = epTimes
        //    //   .Where(x => x.EquipmentBomPredecessorId != null)
        //    //   .Select(x => x.EquipmentBomPredecessorId);

        //    //if (!ebPreIds.All(x => epTimes.Any(ep => ep.EquipmentBomPredecessorId == x)))
        //    //{
        //    //    throw new InvalidOperationException("پیشنیاز تامین نشده.");
        //    //}
        //    #endregion

        //    return epTimes;
        //}

        private IEnumerable<int> RemoveIdsWherePredecessorNotFound(IEnumerable<(int Id, int? PreId)> id_preId, int? parrentId = null)
        {
            IEnumerable<int> ids = null;

            ids = id_preId.Where(x => x.PreId == parrentId).Select(x => x.Id);

            return Enumerable.Concat(ids, ids.Select(x => RemoveIdsWherePredecessorNotFound(id_preId, x)).SelectMany(x => x));
        }

        private List<(int, int?, int)> CalculateEquipmentBomsDuration(IEnumerable<BomTotalDuration> epTimes)
        {
            var ebId_ebPreId_duration = new List<(int, int?, int)>();
            var durations = new Queue<BomTotalDuration>(epTimes);

            while (durations.Any())
            {
                BomTotalDuration current = durations.Dequeue();
                int equipmentBomTotalDuration = current.TotalBomDuration;

                if (current.EquipmentBomPredecessorId != null)
                {
                    int? predecessorId = current.EquipmentBomPredecessorId;
                    if (ebId_ebPreId_duration.Any(x => x.Item1 == current.EquipmentBomPredecessorId))
                    {
                        while (predecessorId != null)
                        {
                            var pre = ebId_ebPreId_duration.Where(x => x.Item1 == predecessorId).Single();
                            equipmentBomTotalDuration += pre.Item3;

                            predecessorId = pre.Item2;
                        }
                    }
                    else
                    {
                        durations.Enqueue(current);
                        continue;
                    }
                }

                ebId_ebPreId_duration.Add(
                    (current.EquipmentBomId, current.EquipmentBomPredecessorId, equipmentBomTotalDuration));
            }

            return ebId_ebPreId_duration;
        }
        #endregion

        #region IProgressReports
        private IQueryable<ProgressReport> _progressReportsQuery => _progressReports
            .OrderBy(x => x.ContractId)
            .ThenBy(x => x.WorkOrderId)
            .ThenBy(x => x.ProcessId);

        public async Task<ResponseDto<ResponseProgressReportDto>> GetProgressReportByWorkOrderIdAsync(GetProgressReportDto dto, CancellationToken cancellationToken)
        {
            var result = await
                (
                    from report in _progressReportsQuery
                    where report.WorkOrderId == dto.Id && report.ReportDate <= dto.ToDate
                    orderby report.ReportDate descending
                    let prices_weightPercents = _progressReportsQuery
                        .Where(x => x.WorkOrderId == dto.Id && x.ReportDate <= dto.ToDate)
                        .Select(x => new
                        {
                            price_actual = x.ActualPrice,
                            price_planned = x.PlannedPrice,
                            date = x.ReportDate,
                            wp_actual = x.ActualWeightPercent,
                            wp_planned = x.PlannedWeightPercent
                        })
                    select new ResponseProgressReportDto
                    {
                        Id = report.Id,
                        ActualStartDate = report.ActualStartDate,
                        PlannedStartDate = report.PlannedStartDate,
                        ActualEndDate = report.ActualEndDate,
                        PlannedEndDate = report.PlannedEndDate,
                        ReportDate = report.ReportDate,
                        PlannedPrice = report.PlannedPrice,
                        ActualPrice = report.ActualPrice,
                        PlannedWeightPercent = report.PlannedWeightPercent,
                        ActualWeightPercent = report.ActualWeightPercent,
                        RemainingDays = report.WorkOrderRemainingDays,
                        WorkOrderTitle = report.WorkOrderTitle,
                        Prices = prices_weightPercents
                            .Select(x => new PriceReport()
                            {
                                Actual = x.price_actual,
                                Planned = x.price_planned,
                                Date = x.date
                            })
                            .ToArray(),
                        WeightPercents = prices_weightPercents
                            .Select(x => new WeightPercentReport()
                            {
                                Actual = x.wp_actual,
                                Planned = x.wp_planned,
                                Date = x.date
                            })
                            .ToArray(),
                    }
                )
                .FirstOrDefaultAsync();

            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<ResponseProgressReportDto>> GetProgressReportByContractIdAsync(GetProgressReportDto dto, CancellationToken cancellationToken)
        {
            var result = await
               (
                   from report in _progressReportsQuery
                   where report.ContractId == dto.Id && report.ReportDate <= dto.ToDate
                   orderby report.ReportDate descending
                   let prices_weightPercents = _progressReportsQuery
                       .Where(x => x.ContractId == dto.Id && x.ReportDate <= dto.ToDate)
                       .Select(x => new
                       {
                           price_actual = x.ActualPrice,
                           price_planned = x.PlannedPrice,
                           date = x.ReportDate,
                           wp_actual = x.ActualWeightPercent,
                           wp_planned = x.PlannedWeightPercent
                       })
                   select new ResponseProgressReportDto
                   {
                       Id = report.Id,
                       ActualStartDate = report.ContractActualStartDate,
                       PlannedStartDate = report.ContractPlannedStartDate,
                       ActualEndDate = report.ContractActualEndtDate,
                       PlannedEndDate = report.ContractPlannedEndDate,
                       ReportDate = report.ReportDate,
                       PlannedPrice = report.PlannedPrice,
                       ActualPrice = report.ActualPrice,
                       PlannedWeightPercent = report.ContractPlannedWeightPercent,
                       ActualWeightPercent = report.ContractActualWeightPercent,
                       RemainingDays = report.ContractRemainingDays,
                       WorkOrderTitle = report.WorkOrderTitle,
                       Prices = prices_weightPercents
                           .Select(x => new PriceReport()
                           {
                               Actual = x.price_actual,
                               Planned = x.price_planned,
                               Date = x.date
                           })
                           .ToArray(),
                       WeightPercents = prices_weightPercents
                           .Select(x => new WeightPercentReport()
                           {
                               Actual = x.wp_actual,
                               Planned = x.wp_planned,
                               Date = x.date
                           })
                           .ToArray(),
                   }
               )
               .FirstOrDefaultAsync();

            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<IEnumerable<ProgressReportDto>>> GetAllProgressReportAsync(DateTimeOffset toDate, CancellationToken cancellationToken)
        {
            var result = await _progressReportsQuery
                .Where(x => x.ReportDate <= toDate)
                 .ProjectTo<ProgressReportDto>(_mapper.ConfigurationProvider)
                 .ToArrayAsync(cancellationToken);

            return ResponseDtoHelper.Ok(result.AsEnumerable());
        }
        #endregion

        #region IMaterialBalanceReports
        public async Task<ResponseDto<MaterialBalanceDto>> GetMaterialBalanceReportByBomIdAsync(int bomId, CancellationToken cancellationToken)
        {
            var result = await GetMaterialBalanceReportOfAsync(bomId, cancellationToken);

            if (string.IsNullOrEmpty(result.Item2))
            {
                return ResponseDtoHelper.Ok(result.Item1);
            }

            return ResponseDtoHelper.BadRequest<MaterialBalanceDto>(result.Item2);
        }

        public async Task<ResponseDto<IEnumerable<MaterialBalanceDto>>> GetAllMaterialBalanceReportsAsync(int equipmentId, CancellationToken cancellationToken)
        {
            int[] bomIds = await _boms
                .Where(bom => !bom.Deleted && bom.Assembly.WorkOrder.EquipmentId == equipmentId)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken);

            IEnumerable<Task<ValueTuple<MaterialBalanceDto, string>>> tasks =
                bomIds.Select(bomId => GetMaterialBalanceReportOfAsync(bomId, cancellationToken));

            return ResponseDtoHelper.Ok(
                (await Task.WhenAll(tasks)).Select(result =>
                {
                    if (string.IsNullOrEmpty(result.Item2))
                    {
                        return result.Item1;
                    }
                    else
                    {
                        return new MaterialBalanceDto()
                        {
                            ApiError = result.Item2
                        };
                    }
                }));
        }

        private async Task<ValueTuple<MaterialBalanceDto, string>> GetMaterialBalanceReportOfAsync(int bomId, CancellationToken cancellationToken)
        {
            var result = await _boms
                .Where(bom => !bom.Deleted && bom.Id == bomId)
                .Select(bom => bom.EquipmentBom)
                .Where(equipmentBom => equipmentBom != null)
                .Select(equipmentBom => new
                {
                    BomId = bomId,
                    BomTitle = equipmentBom.Title,
                    NeededCount = equipmentBom.Count,
                    LastProcessEnterCommodityCode = equipmentBom
                        .EquipmentProcesses
                        .Where(p => !p.Deleted)
                        .OrderByDescending(p => p.Sort)
                        .Select(p => p.EnterCommodityCode)
                        .FirstOrDefault()
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (result == null)
            {
                return (null, "نتیجه ای یافت نشد.خطا در گزارشگیری");
            }

            if (string.IsNullOrWhiteSpace(result.LastProcessEnterCommodityCode))
            {
                return (null, "کد کالای ورودی نامعتبر");
            }

            (int existingCount, string apiErorr) = await GetExistingCountAsync(
                enterCommodityCode: result.LastProcessEnterCommodityCode,
                cancellationToken: cancellationToken);

            var materialBalance = new MaterialBalanceDto()
            {
                BomId = result.BomId,
                BomTitle = result.BomTitle,
                NeededCount = result.NeededCount,
                ExistingCount = existingCount,
                ApiError = apiErorr,
            };

            return (materialBalance, null);
        }

        private async Task<(int existingCount, string apiErorr)> GetExistingCountAsync(string enterCommodityCode, CancellationToken cancellationToken)
        {
            var result = await _storeService.GetGoodCountAsync(enterCommodityCode, cancellationToken);

            return (result.Data, result.Error.Message);
        }
        #endregion

    }
}
