using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Attachments;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProjectControl;

namespace Services.NasbNiro.Tasks
{
    /// <summary>
    /// P-B7a: the owner rules of the generic attachment layer (RecordAttachment) for the records of this package.
    /// A task: whoever sees the task reads the files (a confidential task: its parties and the CEO); creator, a referrer, an assignee and the PMO add and void; a closed or rejected task takes no new file.
    /// A progress report: the project-control readers read; the submitter adds while the report is a draft or returned; nobody changes the files of a report in the chain or an approved one except by voiding (planning, PMO).
    /// A project photo: the project-control readers read; the unit managers and the PMO add the picture (nothing is deleted; a wrong one is voided with a reason).
    /// </summary>
    public sealed class RecordOwnerRules : IAttachmentRecordRules
    {
        private const string Nobody = "__NONE__";
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ITaskService _tasks;

        public RecordOwnerRules(IUnitOfWork uow, IInventoryUserContext user, ITaskService tasks)
        {
            _uow = uow; _user = user; _tasks = tasks;
        }

        public async Task<OwnerFacts> FactsAsync(AttachmentOwnerType type, int id, CancellationToken ct)
        {
            var f = new OwnerFacts();
            switch (type)
            {
                case AttachmentOwnerType.TmTask:
                {
                    TaskFileRights r = await _tasks.GetFileRightsAsync(id, ct);
                    if (!r.Exists) return f;
                    f.Exists = true; f.Number = r.Number; f.Closed = r.Closed;
                    f.ReadRoles = r.CanRead ? null : Nobody;
                    f.WriteRoles = r.CanWrite ? null : Nobody;
                    return f;
                }
                case AttachmentOwnerType.PcProgressSubmission:
                {
                    var s = await _uow.Set<PcProgressSubmission>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.State, x.SubmittedByUserId, x.CreatorUserId }).FirstOrDefaultAsync(ct);
                    if (s == null) return f;
                    f.Exists = true; f.Number = s.Number;
                    f.ReadRoles = ProjectControlRoles.Readers;
                    bool open = s.State == PcSubmissionState.Draft || s.State == PcSubmissionState.Returned;
                    f.Locked = !open;
                    // while the report is open only its maker adds; once it is in the chain or approved, planning and the PMO void with a reason
                    f.WriteRoles = open ? ProjectControlRoles.Readers : "PLANNING,PMO";
                    f.LockedWriteRoles = "PLANNING,PMO";
                    f.CreatorUserId = s.CreatorUserId; f.CreatorEditsWhileOpen = open;
                    return f;
                }
                case AttachmentOwnerType.PcProjectPhoto:
                {
                    var p = await _uow.Set<PcProjectPhoto>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Id, x.Caption, x.IsVoid }).FirstOrDefaultAsync(ct);
                    if (p == null) return f;
                    f.Exists = true; f.Number = p.Caption ?? ("#" + p.Id); f.Closed = p.IsVoid;
                    f.ReadRoles = ProjectControlRoles.Readers; f.WriteRoles = ProjectControlRoles.UnitManagers + ",PMO";
                    return f;
                }
                default:
                    return f;
            }
        }
    }
}
