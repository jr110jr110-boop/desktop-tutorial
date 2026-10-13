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
    public class WorkflowGroupService : IWorkflowGroupService
    {
        private readonly IUnitOfWork _uow;
        private readonly DbSet<WorkflowGroup> _workflowGroup;
        public WorkflowGroupService(IUnitOfWork uow)
        {
            _uow = uow;
            _workflowGroup = _uow.Set<WorkflowGroup>();
        }

        public async Task<int> Add(WorkflowGroupInfoViewModel workflowGroup)
        {
            WorkflowGroup workflowGroupModel = new WorkflowGroup(workflowGroup.Title);
            _workflowGroup.Add(workflowGroupModel);
            await _uow.SaveChangesAsync();
            return workflowGroupModel.Id;
        }

        public async Task<int> Edit(WorkflowGroupViewModel workflowGroup)
        {
            WorkflowGroup engineeringGroupObj = await _workflowGroup.Where(x => x.Id == workflowGroup.Id).FirstOrDefaultAsync();
            engineeringGroupObj.Title = workflowGroup.Title;
            await _uow.SaveChangesAsync();
            return workflowGroup.Id;
        }

        public async Task<List<WorkflowGroupViewModel>> GetAll()
        {
            List<WorkflowGroupViewModel> workflowGroupViewModel = await _workflowGroup.Select(x => new WorkflowGroupViewModel()
                                                                                        {
                                                                                            Id = x.Id,
                                                                                            Title = x.Title
                                                                                        })
                                                                                        .ToListAsync();
            return workflowGroupViewModel;
        }

        public async Task<WorkflowGroupViewModel> GetById(int id)
        {
            WorkflowGroupViewModel engineeringGroupViewModel = await _workflowGroup.Select(x => new WorkflowGroupViewModel()
                                                                                    {
                                                                                        Id = x.Id,
                                                                                        Title = x.Title
                                                                                    })
                                                                                    .Where(x => x.Id == id)
                                                                                    .FirstOrDefaultAsync();
            return engineeringGroupViewModel;
        }
    }
}
