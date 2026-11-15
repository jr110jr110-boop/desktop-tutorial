using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>The rules of the MS Project import that need no database (tested apart): the normalised title, the unit conversion and the match order.</summary>
    internal static class PcMspRules
    {
        public const string KeepNote = "ردیف‌های طرح که در فایل نیستند حذف نمی‌شوند؛ فقط ردیف‌های هم‌کلید (فعالیت و منبع) به‌روز می‌شوند.";
        public const string RatesNote = "نرخ و هزینهٔ فایل MS Project هرگز وارد نمی‌شود؛ هزینه فقط از نرخ استاندارد مصوبِ منبع در سامانه محاسبه می‌شود.";

        /// <summary>The title as it is compared at the third match level: trimmed, lower case, Arabic ي, ى and ك written as ی and ک, Persian and Arabic-Indic digits written as 0-9, the half space and the other blanks one space, runs of blanks one space.</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            bool blank = false;
            foreach (char raw in text.Trim())
            {
                char c = raw == 'ي' || raw == 'ى' ? 'ی' : raw == 'ك' ? 'ک' : raw;
                if (c >= '۰' && c <= '۹') c = (char)('0' + (c - '۰'));
                else if (c >= '٠' && c <= '٩') c = (char)('0' + (c - '٠'));
                if (c == '‌' || char.IsWhiteSpace(c)) { blank = true; continue; }
                if (blank && sb.Length > 0) sb.Append(' ');
                blank = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>The quantity of an assignment in the unit of the pool resource; null when the file's work cannot be counted in that unit (a count resource with work).</summary>
        public static decimal? Quantity(PcResourceUnit unit, decimal workHours, decimal dayHours, bool materialType, decimal units)
        {
            if (materialType) return unit == PcResourceUnit.Each ? PcResourceRules.Round4(units) : (decimal?)null;
            switch (unit)
            {
                case PcResourceUnit.Hour: return PcResourceRules.Round4(workHours);
                case PcResourceUnit.Day: return PcResourceRules.Round4(workHours / Math.Max(0.01m, dayHours));
                default: return null;
            }
        }
    }

    public partial class PcResourceService
    {
        private sealed class MspRow
        {
            public PcMspAssignmentRowDto Dto;
            public PcResource Resource;
            public decimal Quantity;
        }

        private sealed class MspAnalysis
        {
            public MspdiResourceFile File;
            public List<PcMspResourceRowDto> Resources = new List<PcMspResourceRowDto>();
            public List<MspRow> Rows = new List<MspRow>();
        }

        private static string StatusText(string status, string detail = null) =>
            status == "ok" ? "قابل ثبت" : status == "resourceUnmatched" ? "منبع بدون تطبیق" : status == "activityUnmatched" ? "فعالیت بدون تطبیق" : status == "zeroWork" ? (detail ?? "کار صفر") : "تکراری";

        /// <summary>The suggested pool resource of an MS Project resource: exact code, then exact title, then the normalised title; null when none or when more than one resource fits the level.</summary>
        private static (PcResource Resource, string Kind) Suggest(MspdiResource msp, List<PcResource> pool)
        {
            string code = (msp.Code ?? "").Trim(), name = (msp.Name ?? "").Trim();
            PcResource Unique(IEnumerable<PcResource> found) { List<PcResource> list = found.Take(2).ToList(); return list.Count == 1 ? list[0] : null; }
            PcResource hit = code.Length > 0 ? Unique(pool.Where(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase))) : null;
            if (hit == null && name.Length > 0) hit = Unique(pool.Where(r => string.Equals(r.Code, name, StringComparison.OrdinalIgnoreCase)));
            if (hit != null) return (hit, "code");
            hit = Unique(pool.Where(r => r.Title == name));
            if (hit != null) return (hit, "title");
            string norm = PcMspRules.Normalize(name);
            if (norm.Length > 0) hit = Unique(pool.Where(r => PcMspRules.Normalize(r.Title) == norm));
            return hit != null ? (hit, "normalized") : (null, "none");
        }

        /// <summary>The activity of a task of the file: by the WBS code (the code of its WBS node, else its own code - what the MS Project export of project control writes), then by the activity code, then by the normalised title. The first level with exactly one activity decides.</summary>
        private static (PcActivity Activity, string Kind) FindActivity(MspdiTask task, List<PcActivity> acts)
        {
            string wbs = (task.Wbs ?? "").Trim(), name = (task.Name ?? "").Trim();
            if (wbs.Length > 0)
            {
                List<PcActivity> byWbs = acts.Where(a => string.Equals((a.WbsNode?.Code ?? a.Code ?? "").Trim(), wbs, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
                if (byWbs.Count == 1) return (byWbs[0], "wbs");
                List<PcActivity> byCode = acts.Where(a => string.Equals((a.Code ?? "").Trim(), wbs, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
                if (byCode.Count == 1) return (byCode[0], "code");
            }
            if (name.Length > 0)
            {
                List<PcActivity> byName = acts.Where(a => string.Equals((a.Code ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
                if (byName.Count == 1) return (byName[0], "code");
                string norm = PcMspRules.Normalize(name);
                List<PcActivity> byTitle = acts.Where(a => PcMspRules.Normalize(a.Title) == norm).Take(2).ToList();
                if (byTitle.Count == 1) return (byTitle[0], "title");
            }
            return (null, "none");
        }

        /// <summary>The open plan the import writes into: draft or returned, tracked, with the rules of an edit of the plan.</summary>
        private async Task<(PcResourcePlan Plan, string Error)> ImportPlanAsync(int contractId, CancellationToken ct)
        {
            if (IsAi || !_user.IsInRole(PcResourceRules.Writers)) return (null, PcResourceText.NotAllowed);
            int planId = await _uow.Set<PcResourcePlan>().AsNoTracking().Where(p => p.ContractId == contractId && !p.Deleted && (p.Status == PcResourcePlanStatus.Draft || p.Status == PcResourcePlanStatus.Returned))
                .OrderByDescending(p => p.Version).Select(p => p.Id).FirstOrDefaultAsync(ct);
            if (planId == 0) return (null, PcResourceText.ImportNeedsOpenPlan);
            return await EditablePlanAsync(planId, ct);
        }

        /// <summary>Maps the file to the pool and to the activities. <paramref name="mapped"/> is the reviewed mapping (MS Project resource id to a pool resource, an existing or a new one); null means the suggestions are used (the preview).</summary>
        private async Task<MspAnalysis> AnalyseAsync(MspdiResourceFile file, PcResourcePlan plan, Dictionary<int, PcResource> mapped, CancellationToken ct)
        {
            var result = new MspAnalysis { File = file };
            List<PcResource> pool = await _uow.Set<PcResource>().AsNoTracking().Where(r => !r.Deleted && r.IsActive).OrderBy(r => r.Code).ThenBy(r => r.Id).ToListAsync(ct);
            List<PcActivity> acts = await _uow.Set<PcActivity>().AsNoTracking().Include(a => a.WbsNode).Where(a => a.ContractId == plan.ContractId && !a.Deleted).OrderBy(a => a.Id).ToListAsync(ct);
            PcContractTerms terms = await PcContext.TermsAsync(_uow, plan.ContractId, false, ct);
            decimal? projectHours = terms?.CalendarId == null ? null : await _uow.Set<PcCalendar>().AsNoTracking().Where(c => c.Id == terms.CalendarId.Value).Select(c => (decimal?)c.HoursPerDay).FirstOrDefaultAsync(ct);
            int[] calendarIds = acts.Where(a => a.CalendarId.HasValue).Select(a => a.CalendarId.Value).Distinct().ToArray();
            Dictionary<int, decimal> hours = calendarIds.Length == 0 ? new Dictionary<int, decimal>() : await _uow.Set<PcCalendar>().AsNoTracking().Where(c => calendarIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.HoursPerDay, ct);

            var suggestion = new Dictionary<int, PcResource>();
            foreach (MspdiResource r in file.Resources)
            {
                var s = Suggest(r, pool);
                if (s.Resource != null) suggestion[r.Uid] = s.Resource;
                result.Resources.Add(new PcMspResourceRowDto
                {
                    MspUid = r.Uid, Name = r.Name, Code = string.IsNullOrEmpty(r.Code) ? null : r.Code, Type = r.Type, MatchKind = s.Kind, SuggestedResourceId = s.Resource?.Id, SuggestedCode = s.Resource?.Code, SuggestedTitle = s.Resource?.Title,
                    AssignmentCount = file.Assignments.Count(a => a.ResourceUid == r.Uid),
                });
            }
            Dictionary<int, MspdiTask> tasks = file.Tasks.GroupBy(t => t.Uid).ToDictionary(g => g.Key, g => g.First());
            Dictionary<int, MspdiResource> mspResources = file.Resources.ToDictionary(r => r.Uid);
            var seen = new HashSet<(int, int, PcResource)>();
            foreach (MspdiAssignment a in file.Assignments)
            {
                tasks.TryGetValue(a.TaskUid, out MspdiTask task);
                mspResources.TryGetValue(a.ResourceUid, out MspdiResource msp);
                PcResource res = null;
                if (msp != null) { if (mapped != null) mapped.TryGetValue(msp.Uid, out res); else suggestion.TryGetValue(msp.Uid, out res); }
                var act = task == null || task.IsSummary ? (Activity: (PcActivity)null, Kind: "none") : FindActivity(task, acts);
                decimal workHours = a.WorkMinutes.HasValue ? Math.Round(a.WorkMinutes.Value / 60m, 4) : 0m;
                var dto = new PcMspAssignmentRowDto
                {
                    TaskUid = a.TaskUid, TaskName = task?.Name, Wbs = string.IsNullOrEmpty(task?.Wbs) ? null : task.Wbs, ActivityId = act.Activity?.Id, ActivityCode = act.Activity?.Code, ActivityMatch = act.Kind,
                    MspResourceUid = a.ResourceUid, MspResourceName = msp?.Name, ResourceId = res != null && res.Id > 0 ? res.Id : (int?)null, ResourceCode = res?.Code, Unit = res?.Unit, WorkHours = workHours,
                };
                decimal quantity = 0m;
                string status, detail = null;
                if (res == null) status = "resourceUnmatched";
                else if (act.Activity == null) status = "activityUnmatched";
                else
                {
                    decimal dayHours = PcResourceRules.DayHours(act.Activity.CalendarId.HasValue && hours.TryGetValue(act.Activity.CalendarId.Value, out decimal h) ? h : (decimal?)null, projectHours);
                    decimal? q = PcMspRules.Quantity(res.Unit, workHours, dayHours, msp != null && msp.Type == 0, a.Units);
                    if (!q.HasValue) { status = "zeroWork"; detail = "کار این تخصیص با واحد منبع قابل تبدیل نیست"; }
                    else if (q.Value <= 0m) status = "zeroWork";
                    else if (!seen.Add((act.Activity.Id, res.Id, res.Id == 0 ? res : null))) { status = "duplicate"; quantity = q.Value; }
                    else { status = "ok"; quantity = q.Value; }
                }
                dto.Quantity = quantity; dto.Status = status; dto.StatusText = StatusText(status, detail);
                result.Rows.Add(new MspRow { Dto = dto, Resource = res, Quantity = quantity });
            }
            return result;
        }

        private static PcMspCountsDto Counts(MspAnalysis a) => new PcMspCountsDto
        {
            Assignments = a.Rows.Count, Ok = a.Rows.Count(r => r.Dto.Status == "ok"), ResourceUnmatched = a.Rows.Count(r => r.Dto.Status == "resourceUnmatched"), ActivityUnmatched = a.Rows.Count(r => r.Dto.Status == "activityUnmatched"),
            ZeroWork = a.Rows.Count(r => r.Dto.Status == "zeroWork"), Duplicate = a.Rows.Count(r => r.Dto.Status == "duplicate"), ResourcesInFile = a.Resources.Count, ResourcesMatched = a.Resources.Count(r => r.SuggestedResourceId.HasValue),
        };

        public async Task<ResponseDto<PcMspPreviewDto>> PreviewImportAsync(PcMspImportRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcMspPreviewDto>(PcText.InvalidData);
            var open = await ImportPlanAsync(dto.ContractId, cancellationToken);
            if (open.Error != null) return Bad<PcMspPreviewDto>(open.Error);
            MspdiResourceFile file = PbMspdiResources.Read(dto.Xml);
            if (file.Error != null) return Bad<PcMspPreviewDto>(file.Error);
            MspAnalysis analysis = await AnalyseAsync(file, open.Plan, null, cancellationToken);
            var preview = new PcMspPreviewDto
            {
                ContractId = open.Plan.ContractId, PlanId = open.Plan.Id, ProjectName = file.ProjectName, RatesNote = PcMspRules.RatesNote, KeepNote = PcMspRules.KeepNote, Resources = analysis.Resources, Rows = analysis.Rows.Select(r => r.Dto).ToList(), Counts = Counts(analysis),
            };
            if (file.Assignments.Count == 0) preview.Warnings.Add("فایل هیچ تخصیص منبعی ندارد.");
            return ResponseDtoHelper.Ok(preview);
        }

        public async Task<ResponseDto<PcMspImportResultDto>> ImportAsync(PcMspImportRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcMspImportResultDto>(PcText.InvalidData);
            var open = await ImportPlanAsync(dto.ContractId, cancellationToken);
            if (open.Error != null) return Bad<PcMspImportResultDto>(open.Error);
            PcResourcePlan plan = open.Plan;
            MspdiResourceFile file = PbMspdiResources.Read(dto.Xml);
            if (file.Error != null) return Bad<PcMspImportResultDto>(file.Error);

            // the reviewed mapping: each MS Project resource goes to an existing pool resource, to a new one, or nowhere
            List<PcMspResourceMapDto> map = dto.Resources ?? new List<PcMspResourceMapDto>();
            if (map.GroupBy(m => m.MspUid).Any(g => g.Count() > 1)) return Bad<PcMspImportResultDto>("یک منبع MS Project بیش از یک بار در نگاشت آمده است.");
            var mapped = new Dictionary<int, PcResource>();
            var created = new List<PcResource>();
            DateTimeOffset now = DateTimeOffset.Now;
            var newCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PcMspResourceMapDto m in map)
            {
                if (!file.Resources.Any(r => r.Uid == m.MspUid)) return Bad<PcMspImportResultDto>("منبع شمارهٔ " + m.MspUid.ToString(CultureInfo.InvariantCulture) + " در فایل نیست.");
                if (m.ResourceId.HasValue && m.Create != null) return Bad<PcMspImportResultDto>("برای یک منبع MS Project یا منبع موجود را انتخاب کنید یا منبع تازه بسازید، نه هر دو.");
                if (m.ResourceId.HasValue)
                {
                    PcResource existing = await _uow.Set<PcResource>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == m.ResourceId.Value && !r.Deleted, cancellationToken);
                    if (existing == null) return NotFound<PcMspImportResultDto>(PcResourceText.ResourceNotFound);
                    if (!existing.IsActive) return Bad<PcMspImportResultDto>("منبع «" + existing.Code + "» غیرفعال است.");
                    mapped[m.MspUid] = existing;
                }
                else if (m.Create != null)
                {
                    string code = Clean(m.Create.Code), title = Clean(m.Create.Title);
                    string shape = PcResourceRules.ValidateShape(code, title, m.Create.Kind, m.Create.Unit, m.Create.Capacity, null, null, null, null);
                    if (shape != null) return Bad<PcMspImportResultDto>(shape);
                    string unit = PcResourceRules.Upper(m.Create.ResponsibleUnit);
                    if (unit != null && unit.Length > 30) return Bad<PcMspImportResultDto>("واحد مسئول حداکثر ۳۰ نویسه است.");
                    if (!newCodes.Add(code) || await _uow.Set<PcResource>().AnyAsync(r => !r.Deleted && r.Code == code, cancellationToken)) return Bad<PcMspImportResultDto>("کد منبع تکراری است.");
                    var res = new PcResource
                    {
                        Code = code, Title = title, Kind = m.Create.Kind, Unit = m.Create.Unit, Capacity = m.Create.Capacity, ResponsibleUnit = unit, IsActive = true,
                        CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
                    };
                    created.Add(res);
                    mapped[m.MspUid] = res;
                }
            }

            MspAnalysis analysis = await AnalyseAsync(file, plan, mapped, cancellationToken);
            PcMspCountsDto counts = Counts(analysis);
            if (analysis.Rows.Any(r => r.Quantity > PcResourceRules.MaxQuantity)) return Bad<PcMspImportResultDto>("مقدار یکی از تخصیص‌ها از حد مجاز بیشتر است.");

            List<PcActivityResource> existingRows = await _uow.Set<PcActivityResource>().Where(x => x.PlanId == plan.Id).ToListAsync(cancellationToken);
            var result = new PcMspImportResultDto
            {
                Total = counts.Assignments, SkippedResourceUnmatched = counts.ResourceUnmatched, SkippedActivityUnmatched = counts.ActivityUnmatched, SkippedZeroWork = counts.ZeroWork, SkippedDuplicate = counts.Duplicate, RatesNote = PcMspRules.RatesNote, KeepNote = PcMspRules.KeepNote,
            };
            var usedCreated = new HashSet<PcResource>();
            var touchedActivities = new HashSet<int>();
            foreach (MspRow row in analysis.Rows.Where(r => r.Dto.Status == "ok"))
            {
                int activityId = row.Dto.ActivityId.Value;
                PcActivityResource target = !created.Contains(row.Resource) ? existingRows.FirstOrDefault(x => x.ActivityId == activityId && x.ResourceId == row.Resource.Id) : null;
                if (target == null)
                {
                    target = new PcActivityResource { PlanId = plan.Id, ActivityId = activityId, Distribution = PcResourceDistribution.Uniform };
                    if (!created.Contains(row.Resource)) target.ResourceId = row.Resource.Id; else target.Resource = row.Resource;
                    _uow.Set<PcActivityResource>().Add(target);
                    result.Created++;
                }
                else result.Updated++;
                target.Quantity = row.Quantity; target.Source = PcAssignmentSource.Import; target.EditedByUserId = Me; target.EditedAt = now;
                touchedActivities.Add(activityId);
                if (created.Contains(row.Resource)) usedCreated.Add(row.Resource);
            }
            // only the new resources that an imported row uses are created: a resource nobody uses is not added to the pool by a file
            foreach (PcResource r in created.Where(c => usedCreated.Contains(c))) _uow.Set<PcResource>().Add(r);
            result.ResourcesCreated = usedCreated.Count;
            if (touchedActivities.Count > 0)
            {
                List<PcResourcePlanSkip> marks = await _uow.Set<PcResourcePlanSkip>().Where(s => s.PlanId == plan.Id && touchedActivities.Contains(s.ActivityId)).ToListAsync(cancellationToken);
                _uow.RemoveRange(marks);
                Touch(plan, now);
            }
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcMspImportResultDto>(PcText.Concurrency); }
            catch (DbUpdateException) { return Bad<PcMspImportResultDto>("ذخیره انجام نشد؛ کد منبع تکراری یا تخصیص تکراری است."); }
            result.Plan = await PlanDtoAsync(plan, true, cancellationToken);
            return ResponseDtoHelper.Ok(result);
        }
    }
}
