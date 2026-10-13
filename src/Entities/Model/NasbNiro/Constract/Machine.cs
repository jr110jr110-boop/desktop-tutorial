using System;
using System.Collections.Generic;
using System.Text;

namespace Entities.Model.NasbNiro.Constract
{
    public class Machine
    {
        public Machine()
        {

        }
        public Machine(string title, bool isActive)
        {
            EditRequest(title, isActive);
        }
        public void EditRequest(string title, bool isActive)
        {
            Title = title;
            IsActive = isActive;
        }
        public int Id { get; set; }
        public string Title { get; set; }
        public bool IsActive { get; set; }
    }
}
