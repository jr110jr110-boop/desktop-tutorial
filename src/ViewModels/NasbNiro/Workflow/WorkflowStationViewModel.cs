using System;
using System.Collections.Generic;
using System.Text;

namespace ViewModels.NasbNiro.Wrokflow
{
    public class WorkflowStationViewModel
    {
        public int Id { get; set; }
        public int WorkflowGroupId { get; set; }
        public int FromStation { get; set; }
        public int PositionX { get; set; }
        public int PositionY { get; set; }
        public string Title { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsStart { get; set; }
        public bool IsEnd { get; set; }
        public int UserId { get; set; }
        public string DisplayName { get; set; }  
    }

    public class WorkflowStationInfoViewModel
    {
        public int WorkflowGroupId { get; set; }
        public int FromStation { get; set; }
        public int PositionX { get; set; }
        public int PositionY { get; set; }
        public string Title { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsStart { get; set; }
        public bool IsEnd { get; set; }
        public int UserId { get; set; }
    }
}
