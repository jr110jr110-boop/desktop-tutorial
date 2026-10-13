using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ViewModels.NasbNiro.Wrokflow;

namespace Services.NasbNiro.Contracts
{
    public interface IWorkflowStationService
    {
        Task<int> Add(WorkflowStationViewModel workflowStation);
        Task<int> Edit(WorkflowStationViewModel workflowStation);
        Task<int> Setter(List<WorkflowStationViewModel> WorkflowStationInfos);
        Task<List<WorkflowStationViewModel>> Getter(int workflowGroupId);
        Task<bool> Delete(int id);
        Task<WorkflowStationViewModel> GetById(int id);
        Task<List<WorkflowStationViewModel>> GetByWorkflowGroupId(int id);
    }
}
