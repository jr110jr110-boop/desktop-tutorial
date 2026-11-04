using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.ProductBankDtos;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.ProductBank
{
    public interface IPbRoutingService
    {
        Task<ResponseDto<List<PbOpcListDto>>> GetListAsync(int? productId, string partNumber, CancellationToken ct);
        Task<ResponseDto<PbOpcDto>> GetAsync(int id, CancellationToken ct);
        Task<ResponseDto<PbOpcDto>> SaveAsync(SavePbOpcDto dto, CancellationToken ct);
        Task<ResponseDto<PbOpcDto>> SubmitAsync(PbOpcActionDto dto, CancellationToken ct);
        Task<ResponseDto<PbOpcDto>> ReturnAsync(PbOpcActionDto dto, CancellationToken ct);
        Task<ResponseDto<PbOpcDto>> CheckAsync(PbOpcActionDto dto, CancellationToken ct);
        Task<ResponseDto<PbOpcDto>> ApproveAsync(PbOpcActionDto dto, CancellationToken ct);
        Task<ResponseDto<PbOpcDto>> NewRevisionAsync(PbOpcActionDto dto, CancellationToken ct);
        Task<ResponseDto<PbPublishResultDto>> PublishToMesAsync(PbOpcActionDto dto, CancellationToken ct);
        Task<ResponseDto<bool>> DeleteDraftAsync(int id, CancellationToken ct);
    }

    /// <summary>
    /// PB1 part 3: the OPC (operation process chart) routing of a part or an assembly, with revisions: Draft, UnderReview (checked), Approved. The steps carry the two-letter stage code and the item of the part at that
    /// stage. Choice: the MES keeps ONE routing engine (OperationTemplate + MfOperationStandard + MfRoutingRevision with its two approvals); an approved OPC revision is PUBLISHED into it (the operations of each part
    /// become the templates of the part's item and a Draft MES revision is made through the MES service), not copied into a parallel engine.
    /// </summary>
    public class PbRoutingService : PbServiceBase, IPbRoutingService
    {
        public const string OpcTag = "OPC:";
        public const string PublishRoles = "PMO,ENGINEERING,ENGINEER,PLANNING,PRODUCTIONMANAGER";
        public static readonly string[] EvidenceKinds = { "DIM", "VT", "UT", "MT", "PT", "NDT", "TENSILE", "HARDNESS", "IMPACT", "CHEM", "HYDRO", "BALANCE", "RUNOUT" };

        private readonly IMfMasterService _mes;

        public PbRoutingService(IUnitOfWork uow, IInventoryUserContext user, IMfMasterService mes) : base(uow, user)
        {
            _mes = mes ?? throw new ArgumentNullException(nameof(mes));
        }

        private async Task<PbOpcRouting> LoadAsync(int id, bool tracked, CancellationToken ct)
        {
            IQueryable<PbOpcRouting> q = Uow.Set<PbOpcRouting>().Where(r => r.Id == id && !r.Deleted);
            q = tracked ? q.Include(r => r.Steps) : q.AsNoTracking().Include(r => r.Steps);
            return await q.FirstOrDefaultAsync(ct);
        }

        private static List<PbOpcStep> Live(PbOpcRouting r) => r.Steps.Where(s => !s.Deleted).OrderBy(s => s.PartNumber).ThenBy(s => s.Seq).ThenBy(s => s.Id).ToList();

        public static string HashOf(IEnumerable<PbOpcStep> steps)
        {
            var sb = new StringBuilder();
            foreach (PbOpcStep s in steps.Where(s => !s.Deleted).OrderBy(s => s.PartNumber).ThenBy(s => s.Seq).ThenBy(s => s.Id))
                sb.Append(string.Join("|", s.PartNumber, s.Seq.ToString(CultureInfo.InvariantCulture), s.StageCode2, ((int)s.Kind).ToString(CultureInfo.InvariantCulture), s.Title, s.WorkCenterId?.ToString(CultureInfo.InvariantCulture),
                    s.ResourceClass, PbText.Num(s.StandardHours), s.IsQcPoint ? "Q" : "-", s.RequiredEvidence, s.IsOutside ? "O" : "-", s.MakeOrBuy?.ToString())).Append('\n');
            return PbText.Sha256(sb.ToString());
        }

        public async Task<ResponseDto<List<PbOpcListDto>>> GetListAsync(int? productId, string partNumber, CancellationToken ct)
        {
            IQueryable<PbOpcRouting> q = Uow.Set<PbOpcRouting>().AsNoTracking().Where(r => !r.Deleted);
            if (productId.HasValue) q = q.Where(r => r.ProductId == productId.Value);
            string pn = PbText.PartNumber(partNumber);
            if (pn != null) q = q.Where(r => r.PartNumber == pn);
            List<PbOpcListDto> list = await q.OrderBy(r => r.PartNumber).ThenByDescending(r => r.Revision).Select(r => new PbOpcListDto
            {
                Id = r.Id, ProductId = r.ProductId, PartNumber = r.PartNumber, Title = r.Title, Revision = r.Revision, Status = r.Status, StepCount = r.Steps.Count(s => !s.Deleted), PublishedToMesAt = r.PublishedToMesAt,
            }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(list);
        }

        private async Task<PbOpcDto> ToDtoAsync(PbOpcRouting r, CancellationToken ct)
        {
            Dictionary<string, PbStage> stages = (await LoadStagesAsync(false, ct)).ToDictionary(s => s.Code);
            List<int> itemIds = r.Steps.Where(s => s.StageItemId.HasValue).Select(s => s.StageItemId.Value).Distinct().ToList();
            Dictionary<int, string> codes = itemIds.Count == 0 ? new Dictionary<int, string>() : await Uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, ct);
            var dto = new PbOpcDto
            {
                Id = r.Id, ProductId = r.ProductId, PartNumber = r.PartNumber, Title = r.Title, Revision = r.Revision, Status = r.Status, StepCount = r.Steps.Count(s => !s.Deleted), PublishedToMesAt = r.PublishedToMesAt,
                ProjectNumber = r.ProjectNumber, Platform = r.Platform, DocumentDate = r.DocumentDate, ChangeReason = r.ChangeReason, SupersedesId = r.SupersedesId, PreparedByUserId = r.PreparedByUserId,
                CheckedByUserId = r.CheckedByUserId, CheckedAt = r.CheckedAt, ApprovedByUserId = r.ApprovedByUserId, ApprovedAt = r.ApprovedAt, ImportedSigned = r.ImportedSigned,
            };
            foreach (PbOpcStep s in Live(r))
                dto.Steps.Add(new PbOpcStepDto
                {
                    Id = s.Id, Seq = s.Seq, PartNumber = s.PartNumber, PartName = s.PartName, StageCode2 = s.StageCode2, StageTitle = s.StageCode2 != null && stages.TryGetValue(s.StageCode2, out PbStage st) ? st.TitleFa : null,
                    Kind = s.Kind, Title = s.Title, WorkCenterId = s.WorkCenterId, ResourceClass = s.ResourceClass, StandardHours = s.StandardHours, IsQcPoint = s.IsQcPoint, RequiredEvidence = s.RequiredEvidence,
                    IsOutside = s.IsOutside, MakeOrBuy = s.MakeOrBuy, StageItemId = s.StageItemId, StageItemCode = s.StageItemId.HasValue && codes.TryGetValue(s.StageItemId.Value, out string c) ? c : null, Note = s.Note,
                });
            bool human = !IsAi;
            dto.CanEdit = human && r.Status == PbRoutingStatus.Draft && r.PreparedByUserId == User.UserId && InRole(PbRoles.Writers);
            dto.CanSubmit = dto.CanEdit;
            dto.CanCheck = human && r.Status == PbRoutingStatus.UnderReview && !r.CheckedAt.HasValue && InRole(PbRoles.CheckRoles) && r.PreparedByUserId != User.UserId;
            dto.CanApprove = human && r.Status == PbRoutingStatus.UnderReview && r.CheckedAt.HasValue && InRole(PbRoles.ApproveRoles) && r.PreparedByUserId != User.UserId && r.CheckedByUserId != User.UserId;
            dto.CanPublish = human && r.Status == PbRoutingStatus.Approved && InRole(PublishRoles);
            return dto;
        }

        public async Task<ResponseDto<PbOpcDto>> GetAsync(int id, CancellationToken ct)
        {
            PbOpcRouting r = await LoadAsync(id, false, ct);
            return r == null ? ResponseDtoHelper.NotFound<PbOpcDto>(PbMessages.NotFound) : ResponseDtoHelper.Ok(await ToDtoAsync(r, ct));
        }

        public static string NormalizeEvidence(string text, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(text)) return null;
            var list = new List<string>();
            foreach (string part in text.Split(new[] { ',', ';', '،', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string k = part.Trim().ToUpperInvariant();
                if (!EvidenceKinds.Contains(k)) { error = "نوع مدرک بازرسی نامعتبر است: " + part; return null; }
                if (!list.Contains(k)) list.Add(k);
            }
            return list.Count == 0 ? null : string.Join(",", list);
        }

        public async Task<ResponseDto<PbOpcDto>> SaveAsync(SavePbOpcDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.AiNeverApproves);
            if (!InRole(PbRoles.Writers)) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.NotAllowed);
            if (dto == null || dto.Steps == null) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.InvalidData);
            string pn = PbText.PartNumber(dto.PartNumber);
            if (pn == null) return ResponseDtoHelper.BadRequest<PbOpcDto>("شماره مجموعه یا قطعه چارت را وارد کنید.");
            if (PbCodeRules.ShapeError(pn) != null) return ResponseDtoHelper.BadRequest<PbOpcDto>("شماره چارت: " + PbMessages.PartNumberShape);
            if (dto.Steps.Count > 1500) return ResponseDtoHelper.BadRequest<PbOpcDto>("حداکثر ۱۵۰۰ مرحله در یک چارت.");
            Dictionary<string, PbStage> stages = (await LoadStagesAsync(false, ct)).Where(s => s.IsActive).ToDictionary(s => s.Code);
            var steps = new List<PbOpcStep>();
            var seen = new HashSet<string>();
            for (int i = 0; i < dto.Steps.Count; i++)
            {
                SavePbOpcStepDto s = dto.Steps[i];
                string where = $"مرحله {i + 1}: ";
                string stageCode = PbText.Stage(s.StageCode2);
                if (stageCode == null || !stages.TryGetValue(stageCode, out PbStage stage)) return ResponseDtoHelper.BadRequest<PbOpcDto>(where + PbMessages.StageUnknown);
                string stepPn = PbText.PartNumber(s.PartNumber) ?? pn;
                string stepShape = PbCodeRules.ShapeError(stepPn);
                if (stepShape != null) return ResponseDtoHelper.BadRequest<PbOpcDto>(where + stepShape);
                if (s.Seq <= 0) return ResponseDtoHelper.BadRequest<PbOpcDto>(where + "شماره ترتیب باید مثبت باشد.");
                if (!seen.Add(stepPn + "#" + s.Seq)) return ResponseDtoHelper.BadRequest<PbOpcDto>(where + "شماره ترتیب این قطعه تکراری است.");
                if (s.StandardHours < 0) return ResponseDtoHelper.BadRequest<PbOpcDto>(where + "زمان استاندارد منفی نیست.");
                PbStageKind kind = s.Kind ?? stage.Kind;
                if (!Enum.IsDefined(typeof(PbStageKind), kind)) return ResponseDtoHelper.BadRequest<PbOpcDto>(where + "نوع مرحله نامعتبر است.");
                string evidence = NormalizeEvidence(s.RequiredEvidence, out string evError);
                if (evError != null) return ResponseDtoHelper.BadRequest<PbOpcDto>(where + evError);
                if (s.WorkCenterId.HasValue && !await Uow.Set<MfWorkCenter>().AnyAsync(w => w.Id == s.WorkCenterId.Value && !w.Deleted, ct)) return ResponseDtoHelper.BadRequest<PbOpcDto>(where + "مرکز کار پیدا نشد.");
                var step = new PbOpcStep
                {
                    Seq = s.Seq, PartNumber = stepPn, PartName = PbText.Clean(s.PartName), StageCode2 = stageCode, Kind = kind, Title = PbText.Clean(s.Title) ?? stage.TitleFa, WorkCenterId = s.WorkCenterId,
                    ResourceClass = PbText.Clean(s.ResourceClass), StandardHours = s.StandardHours, IsQcPoint = s.IsQcPoint || kind == PbStageKind.Qc || kind == PbStageKind.InspectionHold, RequiredEvidence = evidence,
                    IsOutside = s.IsOutside, MakeOrBuy = s.MakeOrBuy, Note = PbText.Clean(s.Note),
                };
                Stamp(step, true);
                steps.Add(step);
            }

            PbOpcRouting r;
            if (dto.Id.HasValue)
            {
                r = await LoadAsync(dto.Id.Value, true, ct);
                if (r == null) return ResponseDtoHelper.NotFound<PbOpcDto>(PbMessages.NotFound);
                if (r.Status != PbRoutingStatus.Draft) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.OnlyDraft);
                if (r.PreparedByUserId != User.UserId) return ResponseDtoHelper.BadRequest<PbOpcDto>("پیش‌نویس را فقط تهیه‌کننده آن ویرایش می‌کند.");
                if (r.PartNumber != pn) return ResponseDtoHelper.BadRequest<PbOpcDto>("شماره قطعه یک نسخه تغییر نمی‌کند.");
                Uow.Set<PbOpcStep>().RemoveRange(r.Steps);
                r.Steps.Clear();
            }
            else
            {
                if (await Uow.Set<PbOpcRouting>().AnyAsync(x => !x.Deleted && x.PartNumber == pn, ct))
                    return ResponseDtoHelper.BadRequest<PbOpcDto>("برای " + pn + " چارت OPC ثبت شده است؛ از «نسخه جدید» استفاده کنید.");
                r = new PbOpcRouting { PartNumber = pn, Revision = 0, Status = PbRoutingStatus.Draft, PreparedByUserId = User.UserId };
                Uow.Set<PbOpcRouting>().Add(r);
                Stamp(r, true);
            }
            int? productId = dto.ProductId ?? await Uow.Set<PbProduct>().Where(p => !p.Deleted && p.Code == pn).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
            if (dto.ProductId.HasValue && !await Uow.Set<PbProduct>().AnyAsync(p => p.Id == dto.ProductId.Value && !p.Deleted, ct)) return ResponseDtoHelper.BadRequest<PbOpcDto>("محصول پیدا نشد.");
            r.ProductId = productId;
            r.Title = PbText.Clean(dto.Title);
            r.ProjectNumber = PbText.Clean(dto.ProjectNumber);
            r.Platform = PbText.Clean(dto.Platform);
            r.DocumentDate = dto.DocumentDate;
            r.ChangeReason = PbText.Clean(dto.ChangeReason) ?? r.ChangeReason;
            Stamp(r, false);
            foreach (PbOpcStep step in steps) r.Steps.Add(step);
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(await LoadAsync(r.Id, false, ct), ct));
        }

        public async Task<ResponseDto<PbOpcDto>> SubmitAsync(PbOpcActionDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.AiNeverApproves);
            PbOpcRouting r = dto == null ? null : await LoadAsync(dto.Id, true, ct);
            if (r == null) return ResponseDtoHelper.NotFound<PbOpcDto>(PbMessages.NotFound);
            if (r.Status != PbRoutingStatus.Draft) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.OnlyDraft);
            if (r.PreparedByUserId != User.UserId || !InRole(PbRoles.Writers)) return ResponseDtoHelper.BadRequest<PbOpcDto>("پیش‌نویس را فقط تهیه‌کننده آن برای بررسی می‌فرستد.");
            List<PbOpcStep> steps = Live(r);
            if (steps.Count == 0) return ResponseDtoHelper.BadRequest<PbOpcDto>("چارت هیچ مرحله‌ای ندارد.");
            if (r.Revision > 0 && PbText.Clean(r.ChangeReason) == null) return ResponseDtoHelper.BadRequest<PbOpcDto>("دلیل تغییر نسخه را بنویسید.");

            // every step at a stockable stage gets the item of the part at that stage (part number + separator + stage), so the stock of the stage can be held
            Dictionary<string, PbStage> stages = (await LoadStagesAsync(false, ct)).ToDictionary(s => s.Code);
            string separator = await SeparatorAsync(ct);
            foreach (PbOpcStep s in steps)
            {
                PbStage stage = stages[s.StageCode2];
                if (!stage.IsStockable || (s.Kind != PbStageKind.Operation && s.Kind != PbStageKind.Storage)) { s.StageItemId = null; continue; }
                var part = await FindOrCreatePartItemAsync(s.PartNumber, s.PartName ?? r.Title, null, null, null, ct);
                if (part.Error != null) return ResponseDtoHelper.BadRequest<PbOpcDto>(part.Error);
                if (Uow.Entry(part.Item).State == EntityState.Added) await Uow.SaveChangesAsync(ct);
                string code = PbCodeRules.StageItemCode(s.PartNumber, stage.Code, separator);
                InventoryItem item = await Uow.Set<InventoryItem>().FirstOrDefaultAsync(i => !i.Deleted && i.PartNumber == s.PartNumber && i.StageCode2 == stage.Code, ct);
                if (item == null)
                {
                    if (await Uow.Set<InventoryItem>().AnyAsync(i => !i.Deleted && i.Code == code, ct)) return ResponseDtoHelper.BadRequest<PbOpcDto>(InventoryMessages.DuplicateCode + code);
                    item = NewPartItem(s.PartNumber, stage.Code, PbCodeRules.StageItemTitle(part.Item.Title, stage), null, null, null, null, null, part.Item);
                    item.Code = code;
                    Uow.Set<InventoryItem>().Add(item);
                    await Uow.SaveChangesAsync(ct);
                }
                s.StageItemId = item.Id;
            }
            r.ContentHash = HashOf(steps);
            r.Status = PbRoutingStatus.UnderReview;
            Stamp(r, false);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return ResponseDtoHelper.BadRequest<PbOpcDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(r, ct));
        }

        public async Task<ResponseDto<PbOpcDto>> ReturnAsync(PbOpcActionDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.AiNeverApproves);
            PbOpcRouting r = dto == null ? null : await LoadAsync(dto.Id, true, ct);
            if (r == null) return ResponseDtoHelper.NotFound<PbOpcDto>(PbMessages.NotFound);
            if (r.Status != PbRoutingStatus.UnderReview) return ResponseDtoHelper.BadRequest<PbOpcDto>("فقط چارت در حال بررسی برگردانده می‌شود.");
            // the person whose turn it is sends it back: the checker before the check, the approver after it; never the preparer
            if (!InRole(r.CheckedAt.HasValue ? PbRoles.ApproveRoles : PbRoles.CheckRoles) || r.PreparedByUserId == User.UserId || (r.CheckedAt.HasValue && r.CheckedByUserId == User.UserId))
                return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.NotAllowed);
            if (PbText.Clean(dto.Note) == null) return ResponseDtoHelper.BadRequest<PbOpcDto>("دلیل برگرداندن را بنویسید.");
            r.Status = PbRoutingStatus.Draft;
            r.CheckedByUserId = null; r.CheckedAt = null;
            Stamp(r, false);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return ResponseDtoHelper.BadRequest<PbOpcDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(r, ct));
        }

        public async Task<ResponseDto<PbOpcDto>> CheckAsync(PbOpcActionDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.AiNeverApproves);
            PbOpcRouting r = dto == null ? null : await LoadAsync(dto.Id, true, ct);
            if (r == null) return ResponseDtoHelper.NotFound<PbOpcDto>(PbMessages.NotFound);
            if (r.Status != PbRoutingStatus.UnderReview || r.CheckedAt.HasValue) return ResponseDtoHelper.BadRequest<PbOpcDto>("این چارت منتظر کنترل نیست.");
            if (!InRole(PbRoles.CheckRoles)) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.NotAllowed);
            if (r.PreparedByUserId == User.UserId) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.SameUser);
            if (HashOf(r.Steps) != r.ContentHash) return ResponseDtoHelper.BadRequest<PbOpcDto>("مراحل بعد از ارسال تغییر کرده است.");
            r.CheckedByUserId = User.UserId; r.CheckedAt = DateTimeOffset.Now;
            Stamp(r, false);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return ResponseDtoHelper.BadRequest<PbOpcDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(r, ct));
        }

        public async Task<ResponseDto<PbOpcDto>> ApproveAsync(PbOpcActionDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.AiNeverApproves);
            PbOpcRouting r = dto == null ? null : await LoadAsync(dto.Id, true, ct);
            if (r == null) return ResponseDtoHelper.NotFound<PbOpcDto>(PbMessages.NotFound);
            if (r.Status != PbRoutingStatus.UnderReview || !r.CheckedAt.HasValue) return ResponseDtoHelper.BadRequest<PbOpcDto>("این چارت هنوز کنترل نشده یا در حال بررسی نیست.");
            if (!InRole(PbRoles.ApproveRoles)) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.NotAllowed);
            if (r.PreparedByUserId == User.UserId || r.CheckedByUserId == User.UserId) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.SameUser);
            if (HashOf(r.Steps) != r.ContentHash) return ResponseDtoHelper.BadRequest<PbOpcDto>("مراحل بعد از ارسال تغییر کرده است.");
            PbOpcRouting previous = await Uow.Set<PbOpcRouting>().FirstOrDefaultAsync(x => !x.Deleted && x.PartNumber == r.PartNumber && x.Status == PbRoutingStatus.Approved && x.Id != r.Id, ct);
            if (previous != null) { previous.Status = PbRoutingStatus.Superseded; Stamp(previous, false); }
            r.Status = PbRoutingStatus.Approved;
            r.ApprovedByUserId = User.UserId; r.ApprovedAt = DateTimeOffset.Now;
            Stamp(r, false);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return ResponseDtoHelper.BadRequest<PbOpcDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(r, ct));
        }

        public async Task<ResponseDto<PbOpcDto>> NewRevisionAsync(PbOpcActionDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.AiNeverApproves);
            if (!InRole(PbRoles.Writers)) return ResponseDtoHelper.BadRequest<PbOpcDto>(PbMessages.NotAllowed);
            PbOpcRouting from = dto == null ? null : await LoadAsync(dto.Id, false, ct);
            if (from == null) return ResponseDtoHelper.NotFound<PbOpcDto>(PbMessages.NotFound);
            if (from.Status != PbRoutingStatus.Approved) return ResponseDtoHelper.BadRequest<PbOpcDto>("نسخه جدید فقط از نسخه تاییدشده ساخته می‌شود.");
            if (PbText.Clean(dto.Note) == null) return ResponseDtoHelper.BadRequest<PbOpcDto>("دلیل تغییر را بنویسید.");
            if (await Uow.Set<PbOpcRouting>().AnyAsync(x => !x.Deleted && x.PartNumber == from.PartNumber && (x.Status == PbRoutingStatus.Draft || x.Status == PbRoutingStatus.UnderReview), ct))
                return ResponseDtoHelper.BadRequest<PbOpcDto>("یک نسخه پیش‌نویس یا در حال بررسی از این چارت وجود دارد.");
            int revision = await Uow.Set<PbOpcRouting>().Where(x => x.PartNumber == from.PartNumber).MaxAsync(x => x.Revision, ct) + 1;
            var r = new PbOpcRouting
            {
                ProductId = from.ProductId, PartNumber = from.PartNumber, Title = from.Title, ProjectNumber = from.ProjectNumber, Platform = from.Platform, Revision = revision, Status = PbRoutingStatus.Draft,
                DocumentDate = DateTime.Today, ChangeReason = PbText.Clean(dto.Note), SupersedesId = from.Id, PreparedByUserId = User.UserId,
            };
            Stamp(r, true);
            foreach (PbOpcStep s in Live(from))
            {
                var copy = new PbOpcStep
                {
                    Seq = s.Seq, PartNumber = s.PartNumber, PartName = s.PartName, StageCode2 = s.StageCode2, Kind = s.Kind, Title = s.Title, WorkCenterId = s.WorkCenterId, ResourceClass = s.ResourceClass,
                    StandardHours = s.StandardHours, IsQcPoint = s.IsQcPoint, RequiredEvidence = s.RequiredEvidence, IsOutside = s.IsOutside, MakeOrBuy = s.MakeOrBuy, StageItemId = s.StageItemId, Note = s.Note,
                };
                Stamp(copy, true);
                r.Steps.Add(copy);
            }
            Uow.Set<PbOpcRouting>().Add(r);
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(await LoadAsync(r.Id, false, ct), ct));
        }

        public async Task<ResponseDto<bool>> DeleteDraftAsync(int id, CancellationToken ct)
        {
            PbOpcRouting r = await LoadAsync(id, true, ct);
            if (r == null) return ResponseDtoHelper.NotFound<bool>(PbMessages.NotFound);
            if (r.Status != PbRoutingStatus.Draft) return ResponseDtoHelper.BadRequest<bool>(PbMessages.OnlyDraft);
            if (r.PreparedByUserId != User.UserId || !InRole(PbRoles.Writers)) return ResponseDtoHelper.BadRequest<bool>(PbMessages.NotAllowed);
            r.Delete();
            foreach (PbOpcStep s in r.Steps) s.Delete();
            Stamp(r, false);
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(true);
        }

        // -------------------------------------------------------------------------------- publish into the MES routing

        /// <summary>
        /// The operations of an OPC column as MES steps: operations are steps; a QC step after an operation makes that operation a QC point; a QC step before the first operation (vendor certificate check,
        /// incoming dimensions) becomes an inspection requirement of the first operation; transport, storage and purchase are not shop operations.
        /// </summary>
        internal static List<(PbOpcStep Operation, bool Qc, string Evidence)> MesSteps(IEnumerable<PbOpcStep> column)
        {
            var result = new List<(PbOpcStep, bool, string)>();
            var leading = new List<PbOpcStep>();
            foreach (PbOpcStep s in column.OrderBy(x => x.Seq))
            {
                if (s.Kind == PbStageKind.Operation && s.StageCode2 != "PO")
                {
                    bool qc = s.IsQcPoint;
                    string ev = s.RequiredEvidence;
                    if (result.Count == 0 && leading.Count > 0)
                    {
                        qc = true;
                        ev = string.Join(",", (string.Join(",", leading.Select(l => l.RequiredEvidence)) + "," + (ev ?? "")).Split(',', StringSplitOptions.RemoveEmptyEntries).Distinct());
                        if (ev.Length == 0) ev = null;
                    }
                    result.Add((s, qc, ev));
                }
                else if (s.Kind == PbStageKind.Qc || s.Kind == PbStageKind.InspectionHold)
                {
                    if (result.Count == 0) { leading.Add(s); continue; }
                    var last = result[result.Count - 1];
                    string ev = string.Join(",", ((last.Item3 ?? "") + "," + (s.RequiredEvidence ?? "")).Split(',', StringSplitOptions.RemoveEmptyEntries).Distinct());
                    result[result.Count - 1] = (last.Item1, true, ev.Length == 0 ? null : ev);
                }
            }
            return result;
        }

        private sealed class PublishPlan
        {
            public string PartNumber;
            public InventoryItem Item;
            public List<(PbOpcStep Operation, bool Qc, string Evidence)> Steps;
        }

        /// <summary>
        /// Writes the approved OPC steps into the MES routing as operation templates and a Draft MES routing revision (the two MES approvals stay). Nothing is written before every check has passed, and all parts are written in one transaction.
        /// A routing the MES has approved and that is in use is never touched: the earlier routing stays active until the shop changes it through its own revision (so production is never blocked by a publication).
        /// Operation steps need a standard time; a time marked with a star (taken from the plan, or a placeholder) is published with a warning.
        /// </summary>
        public async Task<ResponseDto<PbPublishResultDto>> PublishToMesAsync(PbOpcActionDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbPublishResultDto>(PbMessages.AiNeverApproves);
            if (!InRole(PublishRoles) || !InRole(ProductionRoles.RoutingWriters)) return ResponseDtoHelper.BadRequest<PbPublishResultDto>(PbMessages.NotAllowed);
            PbOpcRouting r = dto == null ? null : await LoadAsync(dto.Id, true, ct);
            if (r == null) return ResponseDtoHelper.NotFound<PbPublishResultDto>(PbMessages.NotFound);
            if (r.Status != PbRoutingStatus.Approved) return ResponseDtoHelper.BadRequest<PbPublishResultDto>("فقط چارت تاییدشده به مسیر ساخت (MES) منتقل می‌شود.");
            var result = new PbPublishResultDto { RoutingId = r.Id };
            List<PbOpcStep> live = Live(r);

            // ---- checks first (nothing is written)
            var plans = new List<PublishPlan>();
            var missing = new List<string>();
            foreach (var column in live.GroupBy(s => s.PartNumber))
            {
                var steps = MesSteps(column);
                if (steps.Count == 0) { result.Skipped.Add(column.Key + ": عملیات ساختی ندارد"); continue; }
                foreach (var st in steps.Where(x => !x.Operation.StandardHours.HasValue || x.Operation.StandardHours.Value <= 0))
                    missing.Add(column.Key + " / مرحله " + st.Operation.Seq + " (" + st.Operation.Title + ")");
                InventoryItem item = await Uow.Set<InventoryItem>().FirstOrDefaultAsync(i => !i.Deleted && i.PartNumber == column.Key && i.StageCode2 == null, ct);
                if (item == null) { result.Skipped.Add(column.Key + ": کالای قطعه ثبت نشده است"); continue; }
                List<OperationTemplate> existing = await Uow.Set<OperationTemplate>().AsNoTracking().Where(t => t.IsActive && !t.Deleted && t.ItemId == item.Id).ToListAsync(ct);
                if (existing.Any(t => t.Description == null || !t.Description.StartsWith(OpcTag, StringComparison.Ordinal)))
                { result.Skipped.Add(column.Key + ": مسیر ساخت دستی دارد و جایگزین نمی‌شود"); continue; }
                if (await Uow.Set<MfRoutingRevision>().AnyAsync(v => !v.Deleted && v.ItemId == item.Id && v.Status == MfRoutingStatus.Draft, ct))
                { result.Skipped.Add(column.Key + ": بازنگری پیش‌نویس مسیر ساخت باز است"); continue; }
                MfResolvedRouting resolved = await MfRouting.ResolveAsync(Uow, item, ct);
                if (resolved.KeyItemId == item.Id && resolved.Revision != null)
                { result.Skipped.Add(column.Key + ": مسیر ساخت تاییدشده و فعال دارد و تا تایید بازنگری تازهٔ مسیر ساخت (MES) دست نمی‌خورد"); continue; }
                plans.Add(new PublishPlan { PartNumber = column.Key, Item = item, Steps = steps });
            }
            if (missing.Count > 0)
                return ResponseDtoHelper.BadRequest<PbPublishResultDto>("زمان استاندارد مرحله‌های ساختی خالی است؛ ابتدا چارت را با زمان کامل کنید: " + string.Join("، ", missing.Take(8)) + (missing.Count > 8 ? " و ..." : ""));
            int starred = live.Count(s => s.Kind == PbStageKind.Operation && s.Note != null && s.Note.StartsWith("★", StringComparison.Ordinal));
            if (starred > 0) result.Warnings.Add(starred + " مرحله زمان تقریبی یا موقت (با ستاره) دارد؛ مهندسی پیش از تایید مسیر ساخت آن را اصلاح کند.");
            if (plans.Count == 0) { result.StageItemsCreated = live.Count(s => s.StageItemId.HasValue); return ResponseDtoHelper.Ok(result); }

            // ---- the writes: one transaction for all parts; a failure of the MES revision rolls everything back
            int routingId = r.Id;
            var plannedParts = plans.Select(x => x.PartNumber).ToList();
            ResponseDto<PbPublishResultDto> outcome = await InTransactionAsync(async () =>
            {
                PbOpcRouting routing = await LoadAsync(routingId, true, ct);
                var done = new PbPublishResultDto { RoutingId = routingId };
                foreach (PublishPlan plan in plans)
                {
                    InventoryItem item = await Uow.Set<InventoryItem>().FirstAsync(i => i.Id == plan.Item.Id, ct);
                    List<OperationTemplate> existing = await Uow.Set<OperationTemplate>().Where(t => t.IsActive && !t.Deleted && t.ItemId == item.Id).ToListAsync(ct);
                    // the steps of an earlier publication that no MES approval stands on are switched off (never deleted)
                    foreach (OperationTemplate old in existing) { old.IsActive = false; Stamp(old, false); }
                    int seq = 0;
                    var created = new List<(OperationTemplate T, PbOpcStep Op, bool Qc)>();
                    foreach (var st in plan.Steps)
                    {
                        seq += 10;
                        var t = new OperationTemplate
                        {
                            ItemId = item.Id, Sequence = seq, Title = st.Operation.Title, RequiresQc = st.Qc, IsActive = true,
                            Description = OpcTag + routing.PartNumber + ":rev" + PbText.Revision(routing.Revision) + ":" + st.Operation.StageCode2 + (st.Evidence == null ? "" : ":" + st.Evidence),
                        };
                        Stamp(t, true);
                        Uow.Set<OperationTemplate>().Add(t);
                        created.Add((t, st.Operation, st.Qc));
                    }
                    await Uow.SaveChangesAsync(ct);
                    foreach (var c in created)
                    {
                        var std = new MfOperationStandard
                        {
                            OperationTemplateId = c.T.Id, WorkCenterId = c.Op.WorkCenterId, RunHoursPerUnit = c.Op.StandardHours.Value, IsOutside = c.Op.IsOutside,
                            InspectionRequirement = c.Qc ? MfInspectionRequirement.Qc : MfInspectionRequirement.None,
                        };
                        Stamp(std, true);
                        Uow.Set<MfOperationStandard>().Add(std);
                        done.OperationsWritten++;
                    }
                    await Uow.SaveChangesAsync(ct);
                    ResponseDto<RoutingRevisionDto> rev = await _mes.SaveRoutingAsync(new SaveRoutingRevisionDto { ItemId = item.Id, Note = "از چارت OPC " + routing.PartNumber + " نسخه " + PbText.Revision(routing.Revision) }, ct);
                    if (!rev.Success) return ResponseDtoHelper.BadRequest<PbPublishResultDto>(plan.PartNumber + ": " + rev.Message);
                    done.Parts++;
                }
                routing.PublishedToMesAt = DateTimeOffset.Now;
                Stamp(routing, false);
                await Uow.SaveChangesAsync(ct);
                return ResponseDtoHelper.Ok(done);
            }, x => x.Success, ct);
            if (!outcome.Success) return outcome;
            result.Parts = outcome.Data.Parts;
            result.OperationsWritten = outcome.Data.OperationsWritten;
            result.StageItemsCreated = live.Count(s => s.StageItemId.HasValue);
            return ResponseDtoHelper.Ok(result);
        }
    }
}
