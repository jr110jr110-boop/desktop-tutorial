using System;
using System.Collections.Generic;
using System.Text;

namespace Entities.Model.NasbNiro.Workflow
{
    public class WorkflowGroup
    {
        public WorkflowGroup()
        {

        }
        public WorkflowGroup(string title)
        {
            EditRequest(title);
        }
        public void EditRequest(string title)
        {
            Title = title;
        }
        public int Id { get; set; }
        public string Title { get; set; }
    }
}
