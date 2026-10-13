using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ViewModels.NasbNiro.Constract;

namespace Services.NasbNiro.Contracts
{
    public interface IMachineService
    {
        Task<int> Add(MachineInfoViewModel machine);
        Task<int> Edit(MachineViewModel machine);
        Task<MachineViewModel> GetById(int id);
        Task<List<MachineViewModel>> GetAll();
        Task<List<MachineViewModel>> GetAllActive();
    }
}
