using Common.GuardToolkit;
using DataLayer.Context;
using Entities.Model.NasbNiro.Workflow;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ViewModels.NasbNiro.Wrokflow;

namespace Services.NasbNiro
{
    public class WorkflowStationService : IWorkflowStationService
    {
        private readonly IUnitOfWork _uow;
        private readonly DbSet<WorkflowStation> _workflowStation;
        public WorkflowStationService(IUnitOfWork uow)
        {
            _uow = uow;
            _workflowStation = _uow.Set<WorkflowStation>();
        }

        public async Task<int> Add(WorkflowStationViewModel workflowStation)
        {
            WorkflowStation engineeringStationModel = new WorkflowStation(workflowStation.WorkflowGroupId, workflowStation.FromStation, workflowStation.PositionX, workflowStation.PositionY, workflowStation.Title,workflowStation.IsStart,workflowStation.IsEnd,workflowStation.UserId);
            _workflowStation.Add(engineeringStationModel);
            await _uow.SaveChangesAsync();
            return engineeringStationModel.Id;
        }

        public async Task<bool> Delete(int id)
        {
            var engineeringStation = await _workflowStation.Where(x => x.Id == id).FirstOrDefaultAsync();
            if (engineeringStation == null || engineeringStation.IsDeleted)
                return false;
            engineeringStation.IsDeleted = true;
            await _uow.SaveChangesAsync();
            return true;
        }

        public async Task<int> Edit(WorkflowStationViewModel workflowStation)
        {
            WorkflowStation workflowStationModel = await _workflowStation.Where(x => x.Id == workflowStation.Id).FirstOrDefaultAsync();
            workflowStationModel.EditRequest(workflowStation.WorkflowGroupId, workflowStation.FromStation, workflowStation.PositionX, workflowStation.PositionY, workflowStation.Title,workflowStation.IsStart,workflowStation.IsEnd,workflowStation.UserId);
            await _uow.SaveChangesAsync();
            return workflowStation.Id;
        }

        public async Task<WorkflowStationViewModel> GetById(int id)
        {
            var workflowStationModel = await _workflowStation.Select(x => new WorkflowStationViewModel
            {
                Id = x.Id,
                FromStation = x.FromStation,
                PositionX = x.PositionX,
                PositionY = x.PositionY,
                Title = x.Title,
                IsStart = x.IsStart,
                IsEnd = x.IsEnd,
                IsDeleted = x.IsDeleted,
                UserId = x.UserId
            }).FirstOrDefaultAsync(x => x.Id == id);
            //workflowStationModel.RelationFull = await _workflowRelationService.GetByFromWorkflowStationId(workflowStationModel.Id);
            return workflowStationModel;
        }

        public async Task<List<WorkflowStationViewModel>> GetByWorkflowGroupId(int groupId)
        {
            var workflowStations = await _workflowStation
                                            .Include(x => x.User)
                                            .Where(x => x.WorkflowGroupId == groupId && !x.IsDeleted)
                                            .Select(x => new WorkflowStationViewModel
                                            {
                                                Id = x.Id,
                                                Title = x.Title,
                                                FromStation = x.FromStation,
                                                PositionX = x.PositionX,
                                                PositionY = x.PositionY,
                                                WorkflowGroupId = x.WorkflowGroupId,
                                                IsStart = x.IsStart,
                                                IsEnd = x.IsEnd,
                                                IsDeleted = x.IsDeleted,
                                                UserId = x.UserId,
                                                DisplayName = x.User.DisplayName
                                            }).ToListAsync();
            //foreach (var station in workflowStations)
            //{
            //    station.RelationFull = await _workflowRelationService.GetByFromWorkflowStationId(station.Id);
            //}
            return workflowStations;
        }

        public async Task<List<WorkflowStationViewModel>> Getter(int workflowGroupId)
        {
            var workflowStations = await GetByWorkflowGroupId(workflowGroupId);
            return workflowStations;
        }

        public async Task<int> Setter(List<WorkflowStationViewModel> WorkflowStations)
        {
            for (int i = 0; i < WorkflowStations.Count; i++)
            {
                if (WorkflowStations[i].IsDeleted)
                {
                    if (WorkflowStations[i].Id != 0)
                    {
                        await Delete(WorkflowStations[i].Id);
                    }
                }
                else
                {
                    if (WorkflowStations[i].Id == 0)
                    {
                        WorkflowStations[i].Id = await Add(WorkflowStations[i]);
                    }
                    else
                    {
                        await Edit(WorkflowStations[i]);
                    }
                }
            }
            return 0;
        }
    }
}
