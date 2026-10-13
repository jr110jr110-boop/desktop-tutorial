using Entities.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace Entities.Model.NasbNiro.Workflow
{
    public class WorkflowStation
    {
        public WorkflowStation()
        {
        }
        public WorkflowStation(int workflowGroupId, int fromStation, int positionX, int positionY, string title, bool isStart,bool isEnd,int userId)
        {
            EditRequest(workflowGroupId, fromStation, positionX, positionY, title,isStart,isEnd,userId);
        }
        public void EditRequest(int workflowGroupId, int fromStation, int positionX, int positionY, string title, bool isStart, bool isEnd, int userId)
        {
            WorkflowGroupId = workflowGroupId;
            FromStation = fromStation;
            PositionX = positionX;
            PositionY = positionY;
            Title = title;
            IsStart = isStart;
            IsEnd = isEnd;
            UserId = userId;
        }
        public int Id { get; set; }
        public int WorkflowGroupId { get; set; }
        public virtual WorkflowGroup WorkflowGroup { get; set; }
        public int FromStation { get; set; }
        public int PositionX { get; set; }
        public int PositionY { get; set; }
        public string Title { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsStart { get; set; }
        public bool IsEnd { get; set; }
        public int UserId { get; set; }
        public virtual User User { get; set; }
    }
}
