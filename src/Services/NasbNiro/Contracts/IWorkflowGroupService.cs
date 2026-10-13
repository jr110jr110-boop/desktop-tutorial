using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ViewModels.NasbNiro.Wrokflow;

namespace Services.NasbNiro.Contracts
{
    public interface IWorkflowGroupService
    {
        Task<int> Add(WorkflowGroupInfoViewModel workflowGroup);
        Task<int> Edit(WorkflowGroupViewModel workflowGroup);
        Task<WorkflowGroupViewModel> GetById(int id);
        Task<List<WorkflowGroupViewModel>> GetAll();
    }
}
