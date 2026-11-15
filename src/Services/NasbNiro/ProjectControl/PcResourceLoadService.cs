using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Agents.Steps;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>The Persian texts of the resource load, the over-allocation alerts and the levelling proposals (project control priority 4, phase 4c).</summary>
    internal static class PcLoadText
    {
        public const string NotAllowed = PcText.NotAllowed;
        public const string AiDecision = "اعمال جابه‌جایی برنامه و پذیرش بیش‌تخصیص با انسان است؛ ایجنت هوش مصنوعی فقط پیشنهاد می‌سازد و هیچ تغییری در برنامه نمی‌دهد.";
        public const string BadPeriod = "تاریخ پایان از تاریخ شروع زودتر است.";
        public const string TooLong = "بازه‌ی نمایش حداکثر ۳۷۰ روز است؛ بازه‌ی کوتاه‌تری انتخاب کنید.";
        public const string BadBucket = "نوع بازه نامعتبر است (روز یا هفته).";
        public const string OverloadNotFound = "بیش‌تخصیص پیدا نشد.";
        public const string ProposalNotFound = "پیشنهاد تسطیح پیدا نشد.";
        public const string NotOpen = "این بیش‌تخصیص دیگر باز نیست.";
        public const string AcceptNeedsPmo = "پذیرش بیش‌تخصیص (نگه‌داشتن آن به‌عمد) فقط با PMO است؛ برنامه‌ریز مالک طرح آن را نمی‌پذیرد.";
        public const string AcceptReason = "علت پذیرش بیش‌تخصیص را بنویسید.";
        public const string AcceptMaker = "شما سازنده یا ویرایشگر یکی از طرح‌های منابعِ این بیش‌تخصیص هستید؛ پذیرش آن با شخص دیگری است (تفکیک وظایف).";
        public const string ReasonTooLong = "علت حداکثر ۱۰۰۰ نویسه است.";
        public const string ActionInvalid = "عملیات نامعتبر است.";
        public const string DecideRoles = "اعمال یا رد پیشنهاد تسطیح با برنامه‌ریز یا PMO است.";
        public const string ConfirmRoles = "تایید یا عدم‌تایید جابه‌جایی برنامه با نفر دوم (PMO) است.";
        public const string NotProposed = "فقط پیشنهادِ در وضعیت «پیشنهادشده» اعمال یا رد می‌شود.";
        public const string NotPending = "فقط جابه‌جایی اعمال‌شده‌ای که منتظر تایید نفر دوم است تایید یا رد می‌شود.";
        public const string RejectReason = "علت رد پیشنهاد را بنویسید.";
        public const string RefuseReason = "علت عدم‌تایید جابه‌جایی را بنویسید.";
        public const string SameUser = "تایید جابه‌جایی باید توسط شخص دیگری غیر از کسی که آن را اعمال کرده انجام شود (تفکیک وظایف).";
        public const string NeedsChangeRequest = "قرارداد خط مبنای جاری دارد و برنامه فقط از مسیر «درخواست تغییر» عوض می‌شود؛ این پیشنهاد مستقیم اعمال نمی‌شود. درخواست تغییر ثبت و تایید کنید.";
        public const string Started = "فعالیت شروع شده است و جابه‌جا نمی‌شود.";
        public const string Stale = "تاریخ‌های فعالیت از زمان ساخت پیشنهاد عوض شده است و پیشنهاد کهنه شد؛ ایجنت پیشنهاد تازه می‌سازد.";
        public const string ConstraintBusy = "فعالیت قید زمانی دیگری دارد (غیر از «شروع نه زودتر از») و با این پیشنهاد جابه‌جا نمی‌شود.";
        public const string RevertChanged = "قید تاریخ فعالیت پس از اعمال پیشنهاد دوباره عوض شده است؛ بازگردانی خودکار ممکن نیست و باید از مسیر برنامه اصلاح شود.";
        public const string Concurrency = PcText.Concurrency;
        public const string Duplicate = "برای این منبع و تاریخ شروع، بیش‌تخصیص باز دیگری ثبت شده است؛ بررسی بعدی ایجنت آن‌ها را یکی می‌کند.";
        public const string SaveFailed = "ذخیره انجام نشد؛ دوباره تلاش کنید.";
        public const string CloseReason = "علت بستن بدون برگرداندن را بنویسید.";
        public const string RevertPossible = "قید فعالیت هنوز همان قید این پیشنهاد است و بازگرداندن خودکار ممکن است؛ به‌جای «بستن بدون برگرداندن» از «عدم‌تایید» استفاده کنید.";
        public const string ConstraintNote = " با اعمال، قید «شروع نه زودتر از» روی فعالیت می‌ماند.";
    }

    public sealed class PcLoadCheckResultDto
    {
        public int Opened { get; set; }
        public int Updated { get; set; }
        public int Resolved { get; set; }

        /// <summary>Runs that ended before today without being fixed or accepted (counted apart from the resolved ones).</summary>
        public int Lapsed { get; set; }
        public int Open { get; set; }
        public int Accepted { get; set; }
    }

    public interface IPcResourceLoadService
    {
        Task<ResponseDto<PcResourceLoadDto>> GetLoadAsync(PcResourceLoadQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcResourceOverloadDto>>> GetOverloadsAsync(PcResourceOverloadQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcResourceOverloadDto>> GetOverloadAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PcResourceOverloadDto>> AcceptOverloadAsync(PcOverloadAcceptDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcLevelingProposalDto>>> GetProposalsAsync(PcLevelingProposalQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcLevelingProposalDto>> GetProposalAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PcLevelingProposalDto>> ProposalActionAsync(PcLevelingActionDto dto, CancellationToken cancellationToken);

        /// <summary>The planning agent (step RES.LOAD.CHECK) only: recomputes the over-allocations of the next 180 days from the approved plans and opens, changes or resolves the rows.</summary>
        Task<ResponseDto<PcLoadCheckResultDto>> CheckOverloadsAsync(CancellationToken cancellationToken);

        /// <summary>The planning agent (step RES.LEVEL.PROPOSE) only: prepares one levelling proposal for an open over-allocation. Data is the id of the proposal, 0 when no activity can be moved.</summary>
        Task<ResponseDto<int>> ProposeLevelingAsync(int overloadId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The load of the resources over all projects, the over-allocation alerts and the levelling proposals. The load is computed in memory from the assignments of the approved resource plans (a fixed number of queries);
    /// no amount of money appears anywhere. The AI finds and proposes; it never applies a schedule change or accepts an over-allocation (maker and checker differ).
    /// </summary>
    public class PcResourceLoadService : IPcResourceLoadService
    {
        private const int MaxShiftDays = 120;
        private const int MaxCandidates = 8;
        private const int ProposalsPerOverload = 5;

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IPcScheduleService _schedule;

        public PcResourceLoadService(IUnitOfWork uow, IInventoryUserContext user, IPcScheduleService schedule)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        }

        private int Me => _user.UserId;
        private static bool IsAi => global::Services.NasbNiro.Ai.AiActing.IsActing;
        private static string Clean(string s) => PcText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => PcText.Bad<T>(m);
        private static ResponseDto<T> NotFound<T>(string m) => PcText.NotFound<T>(m);
        private static string Version(byte[] v) => v == null ? null : Convert.ToBase64String(v);
        private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        private static string Num(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>True when the client's row version is not the one of the row now (or is not a valid version at all): the caller refuses before any side effect. No version sent: no check.</summary>
        private static bool Stale(string client, byte[] current)
        {
            if (string.IsNullOrEmpty(client)) return false;
            var buffer = new byte[client.Length];
            if (!Convert.TryFromBase64String(client, buffer, out int n)) return true;
            byte[] sent = buffer.Take(n).ToArray();
            return !sent.SequenceEqual(current ?? Array.Empty<byte>());
        }

        /// <summary>Saves and turns a database refusal into a Persian message (null when saved): a concurrent change, or a second open row for the same resource and start day.</summary>
        private async Task<string> TrySaveAsync(CancellationToken ct)
        {
            try { await _uow.SaveChangesAsync(ct); return null; }
            catch (DbUpdateConcurrencyException) { return PcLoadText.Concurrency; }
            catch (DbUpdateException ex)
            {
                string text = ex.ToString();
                return text.Contains("OneOpenPerStart") || text.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) || text.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ? PcLoadText.Duplicate : PcLoadText.SaveFailed;
            }
        }

        /// <summary>One database transaction around a body on a relational database (everything the body saves is kept or none of it); the in-memory database of the tests has none, so the body undoes its own schedule change there.</summary>
        private async Task<T> InTransactionAsync<T>(Func<Task<(T Result, bool Commit)>> body, CancellationToken ct)
        {
            if (!(_uow is DbContext db) || !db.Database.IsRelational()) return (await body()).Result;
            int attempt = 0;
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                if (attempt++ > 0) db.ChangeTracker.Clear();
                await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(token);
                (T result, bool commit) = await body();
                if (commit) await tx.CommitAsync(token); else await tx.RollbackAsync(token);
                return result;
            }, ct);
        }

        // ============================================================================================== the histogram

        public async Task<ResponseDto<PcResourceLoadDto>> GetLoadAsync(PcResourceLoadQuery query, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PcResourceRules.Readers)) return Bad<PcResourceLoadDto>(PcLoadText.NotAllowed);
            query ??= new PcResourceLoadQuery();
            DateTime today = DateTime.Today;
            DateTime from = (query.From ?? today).Date, to = (query.To ?? from.AddDays(59)).Date;
            if (to < from) return Bad<PcResourceLoadDto>(PcLoadText.BadPeriod);
            if ((to - from).TotalDays + 1 > PcLoadRules.MaxSpanDays) return Bad<PcResourceLoadDto>(PcLoadText.TooLong);
            string bucket = (Clean(query.Bucket) ?? "day").ToLowerInvariant();
            if (bucket != "day" && bucket != "week") return Bad<PcResourceLoadDto>(PcLoadText.BadBucket);

            // priority 4d: the machine resources linked to a work centre also carry the production layer, and show even without a project assignment
            int[] linkedIds = (await PcWorkCentreLink.LinksAsync(_uow, cancellationToken)).Select(l => l.ResourceId).ToArray();
            LoadModel model = await LoadModel.LoadAsync(_uow, today, query.ResourceId, query.IncludeOpen, from, to, cancellationToken, true, null, query.ResourceId.HasValue ? null : linkedIds);
            if (query.ResourceId.HasValue && !model.Resources.ContainsKey(query.ResourceId.Value)) return NotFound<PcResourceLoadDto>(PcResourceText.ResourceNotFound);

            var result = new PcResourceLoadDto { From = from, To = to, Bucket = bucket, IncludeOpen = query.IncludeOpen };
            ILookup<int, LoadCell> cellsOf = model.Cells.ToLookup(c => c.ResourceId);
            foreach (LoadResource r in model.Resources.Values.OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Id))
            {
                // the cells are grouped by day once; a bucket is filled from the days it holds
                Dictionary<DateTime, List<LoadCell>> byDay = cellsOf[r.Id].GroupBy(c => c.Date).ToDictionary(g => g.Key, g => g.ToList());
                if (!query.ResourceId.HasValue && byDay.Count == 0) continue;
                var row = new PcResourceLoadRowDto { ResourceId = r.Id, Code = r.Code, Title = r.Title, Kind = r.Kind, Unit = r.Unit, Capacity = r.Capacity, WorkCenterId = r.WorkCenterId };
                var itemsOf = new Dictionary<PcLoadBucketDto, Dictionary<(int Activity, bool Draft), decimal>>();
                PcLoadBucketDto current = null;
                DateTime currentKey = DateTime.MinValue;
                for (DateTime d = from; d <= to; d = d.AddDays(1))
                {
                    DateTime key = bucket == "week" ? PcLoadRules.WeekStart(d) : d;
                    if (current == null || key != currentKey)
                    {
                        current = new PcLoadBucketDto { Start = d, End = d };
                        currentKey = key;
                        row.Buckets.Add(current);
                        itemsOf[current] = new Dictionary<(int, bool), decimal>();
                    }
                    current.End = d;
                    decimal cap = model.Capacity(r, d);
                    decimal load = 0m, draft = 0m, production = 0m;
                    if (byDay.TryGetValue(d, out List<LoadCell> list))
                        foreach (LoadCell c in list)
                        {
                            if (c.IsProduction) { production += c.Value; continue; }
                            load += c.Value;
                            if (c.IsDraft) draft += c.Value;
                            var k = (c.ActivityId, c.IsDraft);
                            itemsOf[current][k] = (itemsOf[current].TryGetValue(k, out decimal sum) ? sum : 0m) + c.Value;
                        }
                    current.Load += load; current.DraftLoad += draft; current.ProductionLoad += production; current.Capacity += cap; current.Over += Math.Max(0m, load + production - cap);
                }
                // the activities behind a bucket are returned only for one named resource (the screen asks for them when a resource is chosen)
                if (query.ResourceId.HasValue)
                    foreach (PcLoadBucketDto b in row.Buckets)
                    {
                        foreach (KeyValuePair<(int Activity, bool Draft), decimal> kv in itemsOf[b])
                        {
                            model.Activities.TryGetValue(kv.Key.Activity, out LoadActivity a);
                            b.Items.Add(new PcLoadItemDto { ContractId = a?.ContractId ?? 0, ContractCode = a?.ContractCode, ActivityId = kv.Key.Activity, ActivityCode = a?.Code, ActivityTitle = a?.Title, Load = kv.Value, IsDraft = kv.Key.Draft });
                        }
                        b.Items = b.Items.OrderByDescending(i => i.Load).ThenBy(i => i.ContractCode, StringComparer.Ordinal).ThenBy(i => i.ActivityCode, StringComparer.Ordinal).ThenBy(i => i.ActivityId).ToList();
                    }
                // the picture of all resources keeps the buckets that carry load; one named resource keeps every bucket of the period (its capacity line)
                if (!query.ResourceId.HasValue) row.Buckets = row.Buckets.Where(b => b.Load > 0m || b.ProductionLoad > 0m).ToList();
                row.PeakLoad = row.Buckets.Count == 0 ? 0m : row.Buckets.Max(b => b.Load);
                row.TotalOver = row.Buckets.Sum(b => b.Over);
                result.Resources.Add(row);
            }
            return ResponseDtoHelper.Ok(result);
        }

        // ============================================================================================== the rules of the actions (the DTOs carry the same answers)

        private sealed class PlanOwner
        {
            public int ResourceId { get; set; }
            public int ContractId { get; set; }
            public int Version { get; set; }
            public int MakerUserId { get; set; }
            public string EditorUserIds { get; set; }
            public DateTime? Start { get; set; }
            public DateTime? Finish { get; set; }
        }

        /// <summary>The owners (maker and editors) of the newest approved plan of every running contract that assigns one of the resources to an activity within the dates: one query.</summary>
        private async Task<List<PlanOwner>> OwnersAsync(IReadOnlyCollection<PcResourceOverload> rows, CancellationToken ct)
        {
            if (rows.Count == 0) return new List<PlanOwner>();
            int[] resourceIds = rows.Select(r => r.ResourceId).Distinct().ToArray();
            DateTime min = rows.Min(r => r.FromDate), max = rows.Max(r => r.ToDate);
            List<PlanOwner> all = await _uow.Set<PcActivityResource>().AsNoTracking()
                .Where(x => resourceIds.Contains(x.ResourceId) && x.Plan.Status == PcResourcePlanStatus.Approved && !x.Plan.Deleted && !x.Activity.Deleted && !x.Plan.Contract.Deleted
                    && x.Plan.Contract.Status == Entities.Model.NasbNiro.Contracts.ContractStatus.Current && x.Activity.EarlyStart <= max && x.Activity.EarlyFinish >= min)
                .OrderBy(x => x.Id)
                .Select(x => new PlanOwner { ResourceId = x.ResourceId, ContractId = x.Plan.ContractId, Version = x.Plan.Version, MakerUserId = x.Plan.MakerUserId, EditorUserIds = x.Plan.EditorUserIds, Start = x.Activity.EarlyStart, Finish = x.Activity.EarlyFinish })
                .ToListAsync(ct);
            // only the newest approved version of each contract counts
            Dictionary<int, int> newest = all.GroupBy(o => o.ContractId).ToDictionary(g => g.Key, g => g.Max(o => o.Version));
            return all.Where(o => o.Version == newest[o.ContractId]).ToList();
        }

        private string AcceptBlocked(PcResourceOverload o, List<PlanOwner> owners)
        {
            if (IsAi) return PcLoadText.AiDecision;
            if (!_user.IsInRole(ProjectControlRoles.Pmo)) return PcLoadText.AcceptNeedsPmo;
            if (o.Status != PcOverloadStatus.Open) return PcLoadText.NotOpen;
            // maker and checker differ: whoever made or edited a plan that loads the resource in this run does not accept the result
            if (owners.Any(w => w.ResourceId == o.ResourceId && w.Start <= o.ToDate && w.Finish >= o.FromDate && (w.MakerUserId == Me || PcResourceRules.IsEditor(w.EditorUserIds, Me)))) return PcLoadText.AcceptMaker;
            return null;
        }

        private string ApplyBlocked(PcLevelingProposal p, bool locked)
        {
            if (IsAi) return PcLoadText.AiDecision;
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return PcLoadText.DecideRoles;
            if (p.Status != PcLevelingStatus.Proposed) return PcLoadText.NotProposed;
            return locked ? PcLoadText.NeedsChangeRequest : null;
        }

        private string RejectBlocked(PcLevelingProposal p)
        {
            if (IsAi) return PcLoadText.AiDecision;
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return PcLoadText.DecideRoles;
            return p.Status != PcLevelingStatus.Proposed ? PcLoadText.NotProposed : null;
        }

        private string ConfirmBlocked(PcLevelingProposal p)
        {
            if (IsAi) return PcLoadText.AiDecision;
            if (!_user.IsInRole(ProjectControlRoles.Pmo)) return PcLoadText.ConfirmRoles;
            if (p.Status != PcLevelingStatus.Applied || p.ConfirmedAt != null) return PcLoadText.NotPending;
            return p.ApplierUserId == Me ? PcLoadText.SameUser : null;
        }

        private string CloseBlocked(PcLevelingProposal p, bool revertPossible) => ConfirmBlocked(p) ?? (revertPossible ? PcLoadText.RevertPossible : null);

        /// <summary>The automatic revert of an applied change is possible while the activity still carries the constraint that the change put there.</summary>
        private static bool CanRevert(PcLevelingProposal p, PcConstraint type, DateTime? date) => type == PcConstraint.StartNoEarlierThan && date?.Date == p.AppliedConstraintDate?.Date;

        private async Task<HashSet<int>> LockedContractsAsync(IEnumerable<int> contractIds, CancellationToken ct)
        {
            int[] ids = contractIds.Distinct().ToArray();
            if (ids.Length == 0) return new HashSet<int>();
            return new HashSet<int>(await _uow.Set<PcContractTerms>().AsNoTracking().Where(t => ids.Contains(t.ContractId) && !t.Deleted && t.CurrentBaselineId != null).Select(t => t.ContractId).ToListAsync(ct));
        }

        // ============================================================================================== the over-allocation alerts

        private async Task<List<PcLevelingProposalDto>> ProposalDtosAsync(List<PcLevelingProposal> rows, CancellationToken ct)
        {
            if (rows.Count == 0) return new List<PcLevelingProposalDto>();
            int[] activityIds = rows.Select(r => r.ActivityId).Distinct().ToArray();
            int[] contractIds = rows.Select(r => r.ContractId).Distinct().ToArray();
            var acts = await _uow.Set<PcActivity>().AsNoTracking().Where(a => activityIds.Contains(a.Id)).Select(a => new { a.Id, a.Code, a.Title, a.ConstraintType, a.ConstraintDate }).ToListAsync(ct);
            Dictionary<int, string> contracts = await _uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().AsNoTracking().Where(c => contractIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.ApplierUserId, r.ConfirmedByUserId, r.RejectedByUserId }), ct);
            HashSet<int> locked = await LockedContractsAsync(contractIds, ct);
            string Name(int? id) => id.HasValue && names.TryGetValue(id.Value, out string n) ? n : null;
            return rows.Select(r =>
            {
                var a = acts.FirstOrDefault(x => x.Id == r.ActivityId);
                bool pending = r.Status == PcLevelingStatus.Applied && r.ConfirmedAt == null;
                bool isLocked = locked.Contains(r.ContractId);
                return new PcLevelingProposalDto
                {
                    Id = r.Id, OverloadId = r.OverloadId, ContractId = r.ContractId, ContractCode = contracts.TryGetValue(r.ContractId, out string cc) ? cc : null, ActivityId = r.ActivityId, ActivityCode = a?.Code, ActivityTitle = a?.Title,
                    Kind = r.Kind, CurrentStart = r.CurrentStart, CurrentFinish = r.CurrentFinish, ProposedStart = r.ProposedStart, ProposedFinish = r.ProposedFinish, ShiftDays = r.ShiftDays, CriticalImpactDays = r.CriticalImpactDays,
                    NeedsChangeRequest = r.Status == PcLevelingStatus.Proposed ? isLocked : r.NeedsChangeRequest, Note = r.Note, Status = r.Status, PreparedByAgent = r.PreparedByAgent, PendingSecondPerson = pending, Reverted = r.Reverted,
                    ApplierUserId = r.ApplierUserId, ApplierName = Name(r.ApplierUserId), AppliedAt = r.AppliedAt, ConfirmedByUserId = r.ConfirmedByUserId, ConfirmedByName = Name(r.ConfirmedByUserId), ConfirmedAt = r.ConfirmedAt,
                    RejectedByUserId = r.RejectedByUserId, RejectedByName = Name(r.RejectedByUserId), RejectedAt = r.RejectedAt, RejectReason = r.RejectReason, CreateDate = r.CreateDate, RowVersion = Version(r.RowVersion),
                    ApplyBlockedReason = ApplyBlocked(r, isLocked), RejectBlockedReason = RejectBlocked(r), ConfirmBlockedReason = ConfirmBlocked(r),
                    CloseBlockedReason = CloseBlocked(r, a != null && CanRevert(r, a.ConstraintType, a.ConstraintDate)),
                };
            }).ToList();
        }

        private async Task<List<PcResourceOverloadDto>> OverloadDtosAsync(List<PcResourceOverload> rows, CancellationToken ct)
        {
            if (rows.Count == 0) return new List<PcResourceOverloadDto>();
            int[] ids = rows.Select(r => r.Id).ToArray();
            int[] resourceIds = rows.Select(r => r.ResourceId).Distinct().ToArray();
            Dictionary<int, PcResource> resources = await _uow.Set<PcResource>().AsNoTracking().Where(r => resourceIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => r.AcceptedByUserId), ct);
            // the newest few proposals of each over-allocation, in one bounded query (a correlated count, not all rows)
            IQueryable<PcLevelingProposal> all = _uow.Set<PcLevelingProposal>().AsNoTracking().Where(p => !p.Deleted);
            List<PcLevelingProposal> proposals = await all.Where(p => ids.Contains(p.OverloadId) && all.Count(q => q.OverloadId == p.OverloadId && q.Id > p.Id) < ProposalsPerOverload).OrderByDescending(p => p.Id).ToListAsync(ct);
            List<PcLevelingProposalDto> proposalDtos = await ProposalDtosAsync(proposals, ct);
            List<PlanOwner> owners = rows.Any(r => r.Status == PcOverloadStatus.Open) ? await OwnersAsync(rows.Where(r => r.Status == PcOverloadStatus.Open).ToList(), ct) : new List<PlanOwner>();
            return rows.Select(r =>
            {
                resources.TryGetValue(r.ResourceId, out PcResource res);
                return new PcResourceOverloadDto
                {
                    Id = r.Id, ResourceId = r.ResourceId, ResourceCode = res?.Code, ResourceTitle = res?.Title, ResourceUnit = res?.Unit ?? PcResourceUnit.Hour, FromDate = r.FromDate, ToDate = r.ToDate, PeakLoad = r.PeakLoad,
                    PeakCapacity = r.PeakCapacity, Status = r.Status, DetectedAt = r.DetectedAt, ResolvedAt = r.ResolvedAt, AcceptedByUserId = r.AcceptedByUserId,
                    AcceptedByName = r.AcceptedByUserId.HasValue && names.TryGetValue(r.AcceptedByUserId.Value, out string n) ? n : null, AcceptedAt = r.AcceptedAt, AcceptReason = r.AcceptReason, RowVersion = Version(r.RowVersion),
                    AcceptBlockedReason = AcceptBlocked(r, owners), Proposals = proposalDtos.Where(p => p.OverloadId == r.Id).ToList(),
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PcResourceOverloadDto>>> GetOverloadsAsync(PcResourceOverloadQuery query, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PcResourceRules.Readers)) return Bad<PagedList<PcResourceOverloadDto>>(PcLoadText.NotAllowed);
            query ??= new PcResourceOverloadQuery();
            IQueryable<PcResourceOverload> q = _uow.Set<PcResourceOverload>().AsNoTracking().Where(o => !o.Deleted);
            if (query.ResourceId.HasValue) q = q.Where(o => o.ResourceId == query.ResourceId.Value);
            if (query.Status.HasValue) q = q.Where(o => o.Status == query.Status.Value);
            IOrderedQueryable<PcResourceOverload> ordered = q.OrderBy(o => o.Status == PcOverloadStatus.Open ? 0 : o.Status == PcOverloadStatus.Accepted ? 1 : 2).ThenBy(o => o.FromDate).ThenBy(o => o.ResourceId).ThenBy(o => o.Id);
            PagedList<PcResourceOverload> page = await PagedList<PcResourceOverload>.CreateAsync(ordered, query.PageNumber, query.PageSize, cancellationToken);
            List<PcResourceOverloadDto> dtos = await OverloadDtosAsync(page.ToList(), cancellationToken);
            return ResponseDtoHelper.Ok((PagedList<PcResourceOverloadDto>)new PcPage<PcResourceOverloadDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcResourceOverloadDto>> GetOverloadAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PcResourceRules.Readers)) return Bad<PcResourceOverloadDto>(PcLoadText.NotAllowed);
            PcResourceOverload row = await _uow.Set<PcResourceOverload>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && !o.Deleted, cancellationToken);
            if (row == null) return NotFound<PcResourceOverloadDto>(PcLoadText.OverloadNotFound);
            return ResponseDtoHelper.Ok((await OverloadDtosAsync(new List<PcResourceOverload> { row }, cancellationToken))[0]);
        }

        public async Task<ResponseDto<PcResourceOverloadDto>> AcceptOverloadAsync(PcOverloadAcceptDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcResourceOverloadDto>(PcText.InvalidData);
            if (IsAi) return Bad<PcResourceOverloadDto>(PcLoadText.AiDecision);
            if (!_user.IsInRole(ProjectControlRoles.Pmo)) return Bad<PcResourceOverloadDto>(PcLoadText.AcceptNeedsPmo);
            PcResourceOverload row = await _uow.Set<PcResourceOverload>().FirstOrDefaultAsync(o => o.Id == dto.Id && !o.Deleted, cancellationToken);
            if (row == null) return NotFound<PcResourceOverloadDto>(PcLoadText.OverloadNotFound);
            if (Stale(dto.RowVersion, row.RowVersion)) return Bad<PcResourceOverloadDto>(PcLoadText.Concurrency);
            if (row.Status != PcOverloadStatus.Open) return Bad<PcResourceOverloadDto>(PcLoadText.NotOpen);
            string reason = Clean(dto.Reason);
            if (reason == null) return Bad<PcResourceOverloadDto>(PcLoadText.AcceptReason);
            if (reason.Length > 1000) return Bad<PcResourceOverloadDto>(PcLoadText.ReasonTooLong);
            string blocked = AcceptBlocked(row, await OwnersAsync(new List<PcResourceOverload> { row }, cancellationToken));
            if (blocked != null) return Bad<PcResourceOverloadDto>(blocked);
            DateTimeOffset now = DateTimeOffset.Now;
            row.Status = PcOverloadStatus.Accepted; row.AcceptedByUserId = Me; row.AcceptedAt = now; row.AcceptReason = reason; row.ModifyDate = now; row.ModifierUserId = Me;
            foreach (PcLevelingProposal p in await _uow.Set<PcLevelingProposal>().Where(p => p.OverloadId == row.Id && !p.Deleted && p.Status == PcLevelingStatus.Proposed).ToListAsync(cancellationToken))
            { p.Status = PcLevelingStatus.Superseded; p.ModifyDate = now; p.ModifierUserId = Me; }
            string error = await TrySaveAsync(cancellationToken);
            if (error != null) return Bad<PcResourceOverloadDto>(error);
            return ResponseDtoHelper.Ok((await OverloadDtosAsync(new List<PcResourceOverload> { row }, cancellationToken))[0]);
        }

        // ============================================================================================== the check of the agent

        public async Task<ResponseDto<PcLoadCheckResultDto>> CheckOverloadsAsync(CancellationToken cancellationToken)
        {
            if (!AgentMaker.Allows("RES.LOAD.CHECK") || !_user.IsInRole(PcResourceRules.Writers)) return Bad<PcLoadCheckResultDto>(PcLoadText.NotAllowed);
            DateTime today = DateTime.Today, horizonEnd = today.AddDays(PcLoadRules.HorizonDays - 1);
            LoadModel model = await LoadModel.LoadAsync(_uow, today, null, false, today, horizonEnd, cancellationToken, true);
            List<PcResourceOverload> existing = await _uow.Set<PcResourceOverload>().Where(o => !o.Deleted && (o.Status == PcOverloadStatus.Open || o.Status == PcOverloadStatus.Accepted)).OrderBy(o => o.ResourceId).ThenBy(o => o.FromDate).ThenBy(o => o.Id).ToListAsync(cancellationToken);
            var result = new PcLoadCheckResultDto();
            DateTimeOffset now = DateTimeOffset.Now;
            var changedIds = new HashSet<int>();
            var seen = new HashSet<int>();

            void Finish(PcResourceOverload o)
            {
                // a run that simply passed (it ended before today and nobody fixed or accepted it) lapses; a run that the plans no longer produce is resolved
                bool lapsed = o.ToDate.Date < today;
                o.Status = lapsed ? PcOverloadStatus.Lapsed : PcOverloadStatus.Resolved; o.ResolvedAt = now; o.ModifyDate = now; o.ModifierUserId = Me;
                changedIds.Add(o.Id);
                if (lapsed) result.Lapsed++; else result.Resolved++;
            }

            foreach (LoadResource r in model.Resources.Values.Where(x => x.Detectable).OrderBy(x => x.Id))
            {
                seen.Add(r.Id);
                List<PcResourceOverload> open = existing.Where(o => o.ResourceId == r.Id && o.Status == PcOverloadStatus.Open).ToList();
                List<PcResourceOverload> accepted = existing.Where(o => o.ResourceId == r.Id && o.Status == PcOverloadStatus.Accepted).ToList();
                var matchedOpen = new HashSet<int>();
                var touchedAccepted = new HashSet<int>();
                foreach (PcLoadRules.OverRun run in model.RunsOf(r, today, horizonEnd))
                {
                    foreach (PcResourceOverload a in accepted.Where(a => a.FromDate <= run.To && a.ToDate >= run.From)) touchedAccepted.Add(a.Id);
                    // Open rows are matched first; an accepted row takes a run only while the run stays inside its accepted dates and is not higher
                    PcResourceOverload o = open.FirstOrDefault(x => !matchedOpen.Contains(x.Id) && x.FromDate <= run.To && x.ToDate >= run.From);
                    if (o != null)
                    {
                        matchedOpen.Add(o.Id);
                        // a run that began before today keeps its first day (the days behind us are not computed again)
                        DateTime from = run.From == today && o.FromDate < today ? o.FromDate : run.From;
                        if (o.FromDate != from || o.ToDate != run.To || o.PeakLoad != run.PeakLoad || o.PeakCapacity != run.PeakCapacity)
                        {
                            o.FromDate = from; o.ToDate = run.To; o.PeakLoad = run.PeakLoad; o.PeakCapacity = run.PeakCapacity; o.NoProposalAt = null; o.ModifyDate = now; o.ModifierUserId = Me;
                            changedIds.Add(o.Id);
                            result.Updated++;
                        }
                        continue;
                    }
                    if (accepted.Any(a => a.FromDate <= run.From && a.ToDate >= run.To && run.PeakLoad <= a.PeakLoad)) continue;
                    _uow.Set<PcResourceOverload>().Add(new PcResourceOverload
                    {
                        ResourceId = r.Id, FromDate = run.From, ToDate = run.To, PeakLoad = run.PeakLoad, PeakCapacity = run.PeakCapacity, Status = PcOverloadStatus.Open, DetectedAt = now,
                        CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
                    });
                    result.Opened++;
                }
                foreach (PcResourceOverload o in open.Where(o => !matchedOpen.Contains(o.Id))) Finish(o);
                foreach (PcResourceOverload a in accepted.Where(a => !touchedAccepted.Contains(a.Id))) Finish(a);
            }
            // the rows of a resource that has no load any more (its assignments are gone) or that is not counted any more (a material or a subcontract) are finished too
            foreach (PcResourceOverload o in existing.Where(o => !seen.Contains(o.ResourceId))) Finish(o);

            // a proposal waits for a run that has changed or gone: it no longer fits and the agent makes a new one
            int[] changed = changedIds.ToArray();
            if (changed.Length > 0)
                foreach (PcLevelingProposal p in await _uow.Set<PcLevelingProposal>().Where(p => !p.Deleted && p.Status == PcLevelingStatus.Proposed && changed.Contains(p.OverloadId)).ToListAsync(cancellationToken))
                { p.Status = PcLevelingStatus.Superseded; p.ModifyDate = now; p.ModifierUserId = Me; }
            string error = await TrySaveAsync(cancellationToken);
            if (error != null) return Bad<PcLoadCheckResultDto>(error);
            result.Open = await _uow.Set<PcResourceOverload>().CountAsync(o => !o.Deleted && o.Status == PcOverloadStatus.Open, cancellationToken);
            result.Accepted = await _uow.Set<PcResourceOverload>().CountAsync(o => !o.Deleted && o.Status == PcOverloadStatus.Accepted, cancellationToken);
            return ResponseDtoHelper.Ok(result);
        }

        // ============================================================================================== the levelling proposal of the agent

        private static HashSet<DateTime> OverDays(IReadOnlyDictionary<DateTime, decimal> load, Func<DateTime, decimal> capacity, DateTime from, DateTime to)
        {
            var days = new HashSet<DateTime>();
            for (DateTime d = from.Date; d <= to.Date; d = d.AddDays(1))
                if ((load.TryGetValue(d, out decimal l) ? l : 0m) > capacity(d)) days.Add(d);
            return days;
        }

        private sealed class Candidate
        {
            public LoadActivity Activity;
            public LoadRow Row;
            public int Float;
            public bool Critical;
            public bool Locked;
            public PcPlanPreviewSession Session;
        }

        private sealed class Plan
        {
            public Candidate Candidate;
            public int Shift;
            public DateTime Start, Finish;
            public int Impact;
        }

        public async Task<ResponseDto<int>> ProposeLevelingAsync(int overloadId, CancellationToken cancellationToken)
        {
            if (!AgentMaker.Allows("RES.LEVEL.PROPOSE") || !_user.IsInRole(PcResourceRules.Writers)) return Bad<int>(PcLoadText.NotAllowed);
            PcResourceOverload overload = await _uow.Set<PcResourceOverload>().FirstOrDefaultAsync(o => o.Id == overloadId && !o.Deleted, cancellationToken);
            if (overload == null) return NotFound<int>(PcLoadText.OverloadNotFound);
            if (overload.Status != PcOverloadStatus.Open) return Bad<int>(PcLoadText.NotOpen);

            DateTime today = DateTime.Today, horizonEnd = today.AddDays(PcLoadRules.HorizonDays - 1);
            DateTimeOffset now = DateTimeOffset.Now;
            // a live proposal blocks a new one: proposed, or applied and not yet confirmed; but a proposal that only waits for a change request is looked at again when something it depends on moved since
            DateTimeOffset latestChange = await LvlFacts.LatestChangeAsync(_uow, cancellationToken);
            List<PcLevelingProposal> live = await _uow.Set<PcLevelingProposal>().Where(p => !p.Deleted && p.OverloadId == overloadId && (p.Status == PcLevelingStatus.Proposed || (p.Status == PcLevelingStatus.Applied && p.ConfirmedAt == null))).ToListAsync(cancellationToken);
            List<PcLevelingProposal> waiting = live.Where(p => p.Status == PcLevelingStatus.Proposed && p.NeedsChangeRequest && p.ModifyDate < latestChange).ToList();
            if (live.Count > waiting.Count) return ResponseDtoHelper.Ok(0);

            LoadModel model = await LoadModel.LoadAsync(_uow, today, overload.ResourceId, false, today, horizonEnd, cancellationToken, true);
            if (!model.Resources.TryGetValue(overload.ResourceId, out LoadResource res)) return NotFound<int>(PcResourceText.ResourceNotFound);
            DateTime runFrom = overload.FromDate < today ? today : overload.FromDate, runTo = overload.ToDate;
            Dictionary<DateTime, decimal> baseLoad = model.LoadOf(res.Id);
            HashSet<DateTime> overBefore = OverDays(baseLoad, d => model.Capacity(res, d), today, horizonEnd);
            HashSet<int> rejected = new HashSet<int>(await _uow.Set<PcLevelingProposal>().AsNoTracking().Where(p => !p.Deleted && p.OverloadId == overloadId && p.Status == PcLevelingStatus.Rejected).Select(p => p.ActivityId).ToListAsync(cancellationToken));

            // the activities that load the resource in the run, with the float of a fresh plan run of their project (each contract is loaded once for the whole run)
            int[] inRun = model.Cells.Where(c => !c.IsProduction && c.Date >= runFrom && c.Date <= runTo).Select(c => c.ActivityId).Distinct().OrderBy(i => i).ToArray();
            var sessions = new Dictionary<int, PcPlanPreviewSession>();
            HashSet<int> locked = await LockedContractsAsync(model.Activities.Values.Select(a => a.ContractId), cancellationToken);
            var candidates = new List<Candidate>();
            foreach (int activityId in inRun)
            {
                LoadActivity a = model.Activities[activityId];
                if (a.Started || rejected.Contains(a.Id) || (a.ConstraintType != PcConstraint.None && a.ConstraintType != PcConstraint.StartNoEarlierThan)) continue;
                if (!sessions.TryGetValue(a.ContractId, out PcPlanPreviewSession session)) sessions[a.ContractId] = session = await _schedule.OpenPlanPreviewAsync(a.ContractId, cancellationToken);
                if (session == null || !session.Base.Items.TryGetValue(a.Id, out CpmItem item)) continue;
                // the dates the load was computed from must be the dates of the plan today (the CPM step keeps them current)
                if (item.EarlyStart.Date != a.Start.Date || item.EarlyFinish.Date != a.Finish.Date) continue;
                LoadRow row = model.Rows.FirstOrDefault(r => r.ActivityId == a.Id && r.ResourceId == res.Id);
                if (row == null) continue;
                candidates.Add(new Candidate { Activity = a, Row = row, Float = item.TotalFloat, Critical = item.Critical, Locked = locked.Contains(a.ContractId), Session = session });
            }

            // an activity of a project whose plan is locked cannot be applied without a change request: the unlocked ones are tried first, a locked one only when no unlocked one can clear the run
            Plan chosen = await ChooseAsync(model, res, candidates.Where(c => !c.Locked).ToList(), baseLoad, overBefore, today, horizonEnd, runFrom, runTo, cancellationToken)
                ?? await ChooseAsync(model, res, candidates.Where(c => c.Locked).ToList(), baseLoad, overBefore, today, horizonEnd, runFrom, runTo, cancellationToken);
            if (chosen == null)
            {
                if (waiting.Count > 0) foreach (PcLevelingProposal w in waiting) { w.ModifyDate = now; w.ModifierUserId = Me; }   // looked at again, nothing better: it waits for the next change
                else overload.NoProposalAt = now;
                overload.ModifyDate = now; overload.ModifierUserId = Me;
                string e = await TrySaveAsync(cancellationToken);
                return e != null ? Bad<int>(e) : ResponseDtoHelper.Ok(0);
            }

            LoadActivity act = chosen.Candidate.Activity;
            bool isLocked = chosen.Candidate.Locked;
            PcWorkCalendar projectCalendar = model.ContractCalendarOf(act.ContractId);
            int shift = Math.Max(1, projectCalendar.WorkingDaysBetween(act.Start, chosen.Start));
            string where = $"{res.Code} ({Iso(overload.FromDate)} تا {Iso(overload.ToDate)}، اوج بار {Num(overload.PeakLoad)} در برابر ظرفیت {Num(overload.PeakCapacity)})";
            string note = chosen.Impact == 0
                ? $"جابه‌جایی فعالیت {act.Code} ({act.Title}) از {Iso(act.Start)} به {Iso(chosen.Start)} ({shift} روز کاری، در حدود شناوری {chosen.Candidate.Float} روز) بیش‌تخصیص {where} را برطرف می‌کند و مسیر بحرانی را تغییر نمی‌دهد."
                : $"شناوری فعالیت‌ها برای رفع بیش‌تخصیص {where} کافی نیست؛ کم‌اثرترین جابه‌جایی، تاخیر فعالیت {act.Code} ({act.Title}) از {Iso(act.Start)} به {Iso(chosen.Start)} ({shift} روز کاری) است و پایان پروژه {chosen.Impact} روز کاری دیرتر می‌شود.";
            if (isLocked) note += " " + PcLoadText.NeedsChangeRequest;
            note += PcLoadText.ConstraintNote;
            foreach (PcLevelingProposal w in waiting) { w.Status = PcLevelingStatus.Superseded; w.ModifyDate = now; w.ModifierUserId = Me; }
            var proposal = new PcLevelingProposal
            {
                OverloadId = overload.Id, ContractId = act.ContractId, ActivityId = act.Id, Kind = chosen.Impact == 0 ? PcLevelingKind.ShiftWithinFloat : PcLevelingKind.DelayBeyondFloat,
                CurrentStart = act.Start, CurrentFinish = act.Finish, ProposedStart = chosen.Start, ProposedFinish = chosen.Finish, ShiftDays = shift, CriticalImpactDays = chosen.Impact, NeedsChangeRequest = isLocked,
                Note = note.Length > 1000 ? note.Substring(0, 1000) : note, Status = PcLevelingStatus.Proposed, PreparedByAgent = true, MakerUserId = Me, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
            };
            _uow.Set<PcLevelingProposal>().Add(proposal);
            overload.NoProposalAt = null; overload.ModifyDate = now; overload.ModifierUserId = Me;
            string error = await TrySaveAsync(cancellationToken);
            return error != null ? Bad<int>(error) : ResponseDtoHelper.Ok(proposal.Id);
        }

        /// <summary>The best move among some candidates: within the float first (non-critical, the largest float, then the latest start), else the move with the smallest effect on the finish of the project. At most eight candidates, at most 120 working days.</summary>
        private async Task<Plan> ChooseAsync(LoadModel model, LoadResource res, List<Candidate> candidates, Dictionary<DateTime, decimal> baseLoad, HashSet<DateTime> overBefore, DateTime horizonFrom, DateTime horizonEnd,
            DateTime runFrom, DateTime runTo, CancellationToken ct)
        {
            if (candidates.Count == 0) return null;
            foreach (Candidate c in candidates.Where(c => !c.Critical && c.Float > 0).OrderByDescending(c => c.Float).ThenByDescending(c => c.Activity.Start).ThenBy(c => c.Activity.Id).Take(MaxCandidates))
            {
                Plan p = await FindShiftAsync(model, res, c, baseLoad, overBefore, horizonFrom, horizonEnd, runFrom, runTo, Math.Min(c.Float, MaxShiftDays), ct);
                if (p != null && p.Impact == 0) return p;
            }
            Plan best = null;
            foreach (Candidate c in candidates.OrderByDescending(c => c.Float).ThenByDescending(c => c.Activity.Start).ThenBy(c => c.Activity.Id).Take(MaxCandidates))
            {
                Plan p = await FindShiftAsync(model, res, c, baseLoad, overBefore, horizonFrom, horizonEnd, runFrom, runTo, MaxShiftDays, ct);
                if (p == null) continue;
                if (best == null || p.Impact < best.Impact || (p.Impact == best.Impact && (p.Shift < best.Shift || (p.Shift == best.Shift && p.Candidate.Activity.Id < best.Candidate.Activity.Id)))) best = p;
            }
            return best;
        }

        /// <summary>
        /// The smallest shift (in working days of the contract) of one activity, at most <paramref name="maxShift"/>, that clears the run and makes no new over-capacity day; found on the numbers first and then checked by a plan run
        /// with the constraint (in memory, on the plan context loaded once), so the proposed dates and the effect on the finish of the project are the ones the schedule really gives. Null when there is none.
        /// The check looks at the 180 days of the horizon only: a move can in theory create an over-allocation beyond it, which the next check finds and reports.
        /// </summary>
        private Task<Plan> FindShiftAsync(LoadModel model, LoadResource res, Candidate c, Dictionary<DateTime, decimal> baseLoad, HashSet<DateTime> overBefore, DateTime horizonFrom, DateTime horizonEnd,
            DateTime runFrom, DateTime runTo, int maxShift, CancellationToken ct)
        {
            LoadActivity a = c.Activity;
            PcWorkCalendar project = model.ContractCalendarOf(a.ContractId), own = model.CalendarOfActivity(a);
            var without = new Dictionary<DateTime, decimal>(baseLoad);
            foreach (LoadCell cell in model.Cells.Where(x => x.ResourceId == res.Id && x.ActivityId == a.Id))
                without[cell.Date] = without[cell.Date] - cell.Value;
            int checkedByPlan = 0;
            int length = model.DaysOf(a, a.Start, a.Finish).Count;
            for (int k = 1; k <= maxShift; k++)
            {
                ct.ThrowIfCancellationRequested();
                DateTime start = own.NextWorkingOnOrAfter(project.AddWorkingDays(a.Start, k));
                DateTime finish = a.CalendarId.HasValue ? own.AddWorkingDays(start, Math.Max(0, length - 1)) : project.AddWorkingDays(a.Finish, k);
                if (!Clears(model, res, c.Row, a, without, start, finish, horizonFrom, horizonEnd, runFrom, runTo, overBefore)) continue;
                // the plan run with "start no earlier than" gives the real dates and the real effect on the project
                CpmResult run = c.Session.Run(new Dictionary<int, DateTime> { [a.Id] = project.AddWorkingDays(a.Start, k) });
                if (run == null || !run.Items.TryGetValue(a.Id, out CpmItem item)) return Task.FromResult<Plan>(null);
                if (!Clears(model, res, c.Row, a, without, item.EarlyStart, item.EarlyFinish, horizonFrom, horizonEnd, runFrom, runTo, overBefore))
                {
                    if (++checkedByPlan >= 6) return Task.FromResult<Plan>(null);
                    continue;
                }
                return Task.FromResult(new Plan { Candidate = c, Shift = k, Start = item.EarlyStart.Date, Finish = item.EarlyFinish.Date, Impact = Math.Max(0, run.ProjectFinishOffset - c.Session.Base.ProjectFinishOffset) });
            }
            return Task.FromResult<Plan>(null);
        }

        private static bool Clears(LoadModel model, LoadResource res, LoadRow row, LoadActivity a, Dictionary<DateTime, decimal> without, DateTime start, DateTime finish, DateTime horizonFrom, DateTime horizonEnd,
            DateTime runFrom, DateTime runTo, HashSet<DateTime> overBefore)
        {
            var load = new Dictionary<DateTime, decimal>(without);
            List<DateTime> days = model.DaysOf(a, start, finish);
            decimal[] shares = PcLoadRules.Spread(row.Quantity, row.Distribution, days.Count);
            for (int i = 0; i < days.Count; i++)
                if (days[i] >= horizonFrom && days[i] <= horizonEnd) load[days[i]] = (load.TryGetValue(days[i], out decimal v) ? v : 0m) + shares[i];
            HashSet<DateTime> after = OverDays(load, d => model.Capacity(res, d), horizonFrom, horizonEnd);
            if (after.Any(d => d >= runFrom && d <= runTo)) return false;
            return after.IsSubsetOf(overBefore);
        }

        // ============================================================================================== the decisions of the people

        public async Task<ResponseDto<PagedList<PcLevelingProposalDto>>> GetProposalsAsync(PcLevelingProposalQuery query, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PcResourceRules.Readers)) return Bad<PagedList<PcLevelingProposalDto>>(PcLoadText.NotAllowed);
            query ??= new PcLevelingProposalQuery();
            IQueryable<PcLevelingProposal> q = _uow.Set<PcLevelingProposal>().AsNoTracking().Where(p => !p.Deleted);
            if (query.OverloadId.HasValue) q = q.Where(p => p.OverloadId == query.OverloadId.Value);
            if (query.ContractId.HasValue) q = q.Where(p => p.ContractId == query.ContractId.Value);
            if (query.Status.HasValue) q = q.Where(p => p.Status == query.Status.Value);
            PagedList<PcLevelingProposal> page = await PagedList<PcLevelingProposal>.CreateAsync(q.OrderByDescending(p => p.Id), query.PageNumber, query.PageSize, cancellationToken);
            List<PcLevelingProposalDto> dtos = await ProposalDtosAsync(page.ToList(), cancellationToken);
            return ResponseDtoHelper.Ok((PagedList<PcLevelingProposalDto>)new PcPage<PcLevelingProposalDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcLevelingProposalDto>> GetProposalAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PcResourceRules.Readers)) return Bad<PcLevelingProposalDto>(PcLoadText.NotAllowed);
            PcLevelingProposal row = await _uow.Set<PcLevelingProposal>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && !p.Deleted, cancellationToken);
            if (row == null) return NotFound<PcLevelingProposalDto>(PcLoadText.ProposalNotFound);
            return ResponseDtoHelper.Ok((await ProposalDtosAsync(new List<PcLevelingProposal> { row }, cancellationToken))[0]);
        }

        public async Task<ResponseDto<PcLevelingProposalDto>> ProposalActionAsync(PcLevelingActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcLevelingProposalDto>(PcText.InvalidData);
            if (IsAi) return Bad<PcLevelingProposalDto>(PcLoadText.AiDecision);
            string action = dto.Action.Trim().ToLowerInvariant();
            if (action != "apply" && action != "reject" && action != "confirm" && action != "refuse" && action != "close") return Bad<PcLevelingProposalDto>(PcLoadText.ActionInvalid);
            string reason = Clean(dto.Reason);
            if (reason != null && reason.Length > 1000) return Bad<PcLevelingProposalDto>(PcLoadText.ReasonTooLong);

            // the whole decision (the reading, the change of the schedule, the plan run and the save of the proposal) is one unit: all of it or none of it
            return await InTransactionAsync(async () =>
            {
                PcLevelingProposal row = await _uow.Set<PcLevelingProposal>().FirstOrDefaultAsync(p => p.Id == dto.Id && !p.Deleted, cancellationToken);
                if (row == null) return (NotFound<PcLevelingProposalDto>(PcLoadText.ProposalNotFound), false);
                // the version the person saw is compared with the row first, before anything is changed
                if (Stale(dto.RowVersion, row.RowVersion)) return (Bad<PcLevelingProposalDto>(PcLoadText.Concurrency), false);
                DateTimeOffset now = DateTimeOffset.Now;
                switch (action)
                {
                    case "apply":
                    {
                        // the lock is read now (a stored flag may be old): a contract with a current baseline changes its plan only through a change request
                        string blocked = ApplyBlocked(row, await PcPlanLock.IsLockedAsync(_uow, row.ContractId, cancellationToken));
                        if (blocked != null) return (Bad<PcLevelingProposalDto>(blocked), false);
                        (string error, bool commit) = await ApplyAsync(row, now, cancellationToken);
                        if (error != null) return (Bad<PcLevelingProposalDto>(error), commit);
                        break;
                    }
                    case "reject":
                    {
                        string blocked = RejectBlocked(row);
                        if (blocked != null) return (Bad<PcLevelingProposalDto>(blocked), false);
                        if (reason == null) return (Bad<PcLevelingProposalDto>(PcLoadText.RejectReason), false);
                        row.Status = PcLevelingStatus.Rejected; row.RejectedByUserId = Me; row.RejectedAt = now; row.RejectReason = reason;
                        break;
                    }
                    case "confirm":
                    {
                        string blocked = ConfirmBlocked(row);
                        if (blocked != null) return (Bad<PcLevelingProposalDto>(blocked), false);
                        row.ConfirmedByUserId = Me; row.ConfirmedAt = now;
                        break;
                    }
                    case "refuse":
                    {
                        string blocked = ConfirmBlocked(row);
                        if (blocked != null) return (Bad<PcLevelingProposalDto>(blocked), false);
                        if (reason == null) return (Bad<PcLevelingProposalDto>(PcLoadText.RefuseReason), false);
                        string error = await RevertAsync(row, cancellationToken);
                        if (error != null) return (Bad<PcLevelingProposalDto>(error), false);
                        row.Status = PcLevelingStatus.Rejected; row.Reverted = true; row.RejectedByUserId = Me; row.RejectedAt = now; row.RejectReason = reason;
                        break;
                    }
                    default:   // close
                    {
                        var current = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.Id == row.ActivityId).Select(a => new { a.ConstraintType, a.ConstraintDate }).FirstOrDefaultAsync(cancellationToken);
                        string blocked = CloseBlocked(row, current != null && CanRevert(row, current.ConstraintType, current.ConstraintDate));
                        if (blocked != null) return (Bad<PcLevelingProposalDto>(blocked), false);
                        if (reason == null) return (Bad<PcLevelingProposalDto>(PcLoadText.CloseReason), false);
                        row.Status = PcLevelingStatus.ClosedUnreverted; row.Reverted = false; row.RejectedByUserId = Me; row.RejectedAt = now; row.RejectReason = reason;
                        break;
                    }
                }
                row.ModifyDate = now; row.ModifierUserId = Me;
                string saveError = await TrySaveAsync(cancellationToken);
                if (saveError != null) return (Bad<PcLevelingProposalDto>(saveError), false);
                return (ResponseDtoHelper.Ok((await ProposalDtosAsync(new List<PcLevelingProposal> { row }, cancellationToken))[0]), true);
            }, cancellationToken);
        }

        /// <summary>
        /// Applies a proposal through the schedule service (the constraint "start no earlier than" on the activity, then the plan run), so every lock, calculation and audit rule of the schedule holds. Returns the Persian refusal (or null)
        /// and whether what was saved on the way (a proposal that went stale) is kept; on the in-memory database the change is taken back by hand when a step fails.
        /// </summary>
        private async Task<(string Error, bool Commit)> ApplyAsync(PcLevelingProposal row, DateTimeOffset now, CancellationToken ct)
        {
            PcResourceOverload overload = await _uow.Set<PcResourceOverload>().FirstOrDefaultAsync(o => o.Id == row.OverloadId, ct);
            if (overload == null || overload.Status != PcOverloadStatus.Open)
            {
                row.Status = PcLevelingStatus.Superseded; row.ModifyDate = now; row.ModifierUserId = Me;
                string e = await TrySaveAsync(ct);
                return (e ?? PcLoadText.NotOpen, e == null);
            }
            ResponseDto<PcActivityDto> current = await _schedule.GetActivityAsync(row.ActivityId, ct);
            if (!current.Success) return (current.Message, false);
            PcActivityDto act = current.Data;
            if (act.ActualStart.HasValue || act.Status != PcActivityStatus.NotStarted) return (PcLoadText.Started, false);
            if (act.ConstraintType != PcConstraint.None && act.ConstraintType != PcConstraint.StartNoEarlierThan) return (PcLoadText.ConstraintBusy, false);
            if (act.EarlyStart?.Date != row.CurrentStart.Date || act.EarlyFinish?.Date != row.CurrentFinish.Date)
            {
                row.Status = PcLevelingStatus.Superseded; row.ModifyDate = now; row.ModifierUserId = Me;
                string e = await TrySaveAsync(ct);
                return (e ?? PcLoadText.Stale, e == null);
            }
            PcConstraint priorType = act.ConstraintType; DateTime? priorDate = act.ConstraintDate;
            act.ConstraintType = PcConstraint.StartNoEarlierThan; act.ConstraintDate = row.ProposedStart.Date;
            ResponseDto<PcActivityDto> saved = await _schedule.SaveActivityAsync(act, ct);
            if (!saved.Success) return (saved.Message, false);
            ResponseDto<PcCpmRunDto> run = await _schedule.RunCpmAsync(new PcCpmRunRequestDto { ContractId = row.ContractId, Forecast = false, Mirror = true }, ct);
            if (!run.Success || run.Data == null || run.Data.HasCycle)
            {
                // taken back at once: a half-applied schedule is never left behind (on a relational database the transaction rolls it back as well)
                act.ConstraintType = priorType; act.ConstraintDate = priorDate;
                await _schedule.SaveActivityAsync(act, ct);
                await _schedule.RunCpmAsync(new PcCpmRunRequestDto { ContractId = row.ContractId, Forecast = false, Mirror = true }, ct);
                return (run.Success && run.Data != null ? run.Data.Message : run.Message, false);
            }
            row.Status = PcLevelingStatus.Applied; row.NeedsChangeRequest = false; row.ApplierUserId = Me; row.AppliedAt = now; row.PriorConstraintType = priorType; row.PriorConstraintDate = priorDate; row.AppliedConstraintDate = row.ProposedStart.Date;
            return (null, true);
        }

        /// <summary>Takes an applied proposal back through the schedule service. Returns the Persian refusal or null.</summary>
        private async Task<string> RevertAsync(PcLevelingProposal row, CancellationToken ct)
        {
            ResponseDto<PcActivityDto> current = await _schedule.GetActivityAsync(row.ActivityId, ct);
            if (!current.Success) return current.Message;
            PcActivityDto act = current.Data;
            if (!CanRevert(row, act.ConstraintType, act.ConstraintDate)) return PcLoadText.RevertChanged;
            act.ConstraintType = row.PriorConstraintType; act.ConstraintDate = row.PriorConstraintDate;
            ResponseDto<PcActivityDto> saved = await _schedule.SaveActivityAsync(act, ct);
            if (!saved.Success) return saved.Message;
            ResponseDto<PcCpmRunDto> run = await _schedule.RunCpmAsync(new PcCpmRunRequestDto { ContractId = row.ContractId, Forecast = false, Mirror = true }, ct);
            if (!run.Success) return run.Message;
            return null;
        }
    }
}
