using System;
using System.Collections.Generic;
using System.Text;

namespace ViewModels.NasbNiro.Constract
{
    public class MachineInfoViewModel
    {
        public string Title { get; set; }
    }
    public class MachineViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public bool IsActive { get; set; }
    }
}
