using Common.GuardToolkit;
using DataLayer.Context;
using Entities.Model.NasbNiro.Constract;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ViewModels.NasbNiro.Constract;


namespace Services.NasbNiro
{
    public class MachineService : IMachineService
    {
        private readonly IUnitOfWork _uow;
        private readonly DbSet<Machine> _machine;
        public MachineService(IUnitOfWork uow)
        {
            _uow = uow;
            _machine = _uow.Set<Machine>();
        }

        public async Task<int> Add(MachineInfoViewModel machine)
        {
            Machine model = new Machine(machine.Title, true);
            _machine.Add(model);
            await _uow.SaveChangesAsync();
            return model.Id;
        }

        public async Task<int> Edit(MachineViewModel machine)
        {
            Machine model = await _machine.Where(x => x.Id == machine.Id).FirstOrDefaultAsync();
            model.Title = machine.Title;
            model.IsActive = machine.IsActive;
            await _uow.SaveChangesAsync();
            return machine.Id;
        }

        public async Task<List<MachineViewModel>> GetAll()
        {
            List<MachineViewModel> list = await _machine.Select(x => new MachineViewModel()
            {
                Id = x.Id,
                Title = x.Title,
                IsActive = x.IsActive
            })
            .ToListAsync();
            return list;
        }

        public async Task<List<MachineViewModel>> GetAllActive()
        {
            List<MachineViewModel> list = await _machine.Select(x => new MachineViewModel()
            {
                Id = x.Id,
                Title = x.Title,
                IsActive = x.IsActive
            })
            .Where(x => x.IsActive == true)
            .ToListAsync();
            return list;
        }

        public async Task<MachineViewModel> GetById(int id)
        {
            MachineViewModel list = await _machine.Select(x => new MachineViewModel()
            {
                Id = x.Id,
                Title = x.Title,
                IsActive = x.IsActive
            })
            .Where(x => x.Id == id)
            .FirstOrDefaultAsync();
            return list;
        }
    }
}
