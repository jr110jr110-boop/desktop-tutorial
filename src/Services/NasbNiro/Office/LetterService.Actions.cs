using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.OfficeDtos;

namespace Services.NasbNiro.Office
{
    public partial class LetterService
    {
        private const string SameUser = "این کار باید توسط شخص دیگری غیر از تنظیم‌کننده نامه انجام شود (تفکیک وظایف).";

        private async Task<ResponseDto<LetterDto>> FinishAsync(OfLetter l, Ctx c, CancellationToken ct)
        {
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<LetterDto>(OfficeText.Concurrency); }
            catch (DbUpdateException) { return Bad<LetterDto>("ذخیره انجام نشد (احتمالاً هم‌زمان شماره گرفته شد)؛ دوباره تلاش کنید."); }
            return ResponseDtoHelper.Ok(await BuildAsync(await LoadAsync(l.Id, false, ct), c, ct));
        }

        private static ActingPosition Acting(Ctx c, int positionId, int? requested) =>
            c.Acting.FirstOrDefault(a => a.PositionId == positionId && (!requested.HasValue || requested.Value == positionId));

        public async Task<ResponseDto<LetterDto>> LetterActionAsync(LetterActionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<LetterDto>(OfficeText.InvalidData);
            Ctx c = await ContextAsync(ct);
            OfLetter l = await LoadAsync(dto.Id, true, ct);
            if (l == null || AccessOf(l, c) != Access.Full) return OfficeText.NotFound<LetterDto>("نامه پیدا نشد.");
            if (Clean(dto.RowVersion) != null)
            {
                try { _uow.Entry(l).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); }
                catch (FormatException) { return Bad<LetterDto>(OfficeText.InvalidData); }
            }
            switch ((dto.Action ?? "").Trim().ToLowerInvariant())
            {
                case "submit": return await SubmitAsync(l, c, dto, ct);
                case "approve": return await ApproveAsync(l, c, dto, ct);
                case "return": return await ReturnAsync(l, c, dto, ct);
                case "sign": return await SignAsync(l, c, dto, ct);
                case "register": return await RegisterAsync(l, c, ct);
                case "send": return await SendAsync(l, c, dto, ct);
                case "receipt": return await ReceiptAsync(l, c, dto, ct);
                case "archive": return await ArchiveAsync(l, c, ct);
                case "cancel": return await CancelAsync(l, c, dto, ct);
                default: return Bad<LetterDto>("عمل نامعتبر است.");
            }
        }

        private async Task<ResponseDto<LetterDto>> SubmitAsync(OfLetter l, Ctx c, LetterActionDto dto, CancellationToken ct)
        {
            if (l.Status != OfLetterStatus.Draft || l.CreatorUserId != Me || l.Kind == OfLetterKind.Incoming) return Bad<LetterDto>("فقط پیش‌نویس نامه صادره یا داخلی توسط تنظیم‌کننده برای تایید فرستاده می‌شود.");
            if (!l.SenderPositionId.HasValue || !c.Pos.Contains(l.SenderPositionId.Value)) return Bad<LetterDto>("سمت فرستنده نامه معتبر نیست.");
            if (!l.Recipients.Any(r => r.Type == OfRecipientType.To)) return Bad<LetterDto>("گیرنده نامه مشخص نیست.");
            int? signer = l.SignerPositionId;
            if (!signer.HasValue && await _uow.Set<OfPosition>().AnyAsync(p => p.Id == l.SenderPositionId.Value && p.CanSign && p.IsActive, ct)) signer = l.SenderPositionId;
            if (!signer.HasValue) return Bad<LetterDto>("امضاکننده نامه را مشخص کنید.");
            if (!await _uow.Set<OfPosition>().AnyAsync(p => p.Id == signer.Value && !p.Deleted && p.IsActive && p.CanSign, ct)) return Bad<LetterDto>("سمت امضاکننده مجاز به امضا نیست.");
            List<int> chain = dto.ApprovalChain;
            if (chain == null || chain.Count == 0)
            {
                if (l.TemplateId.HasValue)
                {
                    string csv = await _uow.Set<OfLetterTemplate>().AsNoTracking().Where(t => t.Id == l.TemplateId.Value).Select(t => t.ApprovalChain).FirstOrDefaultAsync(ct);
                    chain = OfficeText.Ids(csv);
                }
                else chain = new List<int>();
            }
            chain = chain.Where(p => p != signer.Value).Distinct().ToList();
            if (chain.Count > 0)
            {
                List<OfPosition> chainPositions = await _uow.Set<OfPosition>().AsNoTracking().Where(p => chain.Contains(p.Id) && !p.Deleted && p.IsActive).ToListAsync(ct);
                if (chainPositions.Count != chain.Count) return Bad<LetterDto>("یکی از سمت‌های زنجیره تایید پیدا نشد یا غیرفعال است.");
                if (chainPositions.Any(p => p.UserId == Me)) return Bad<LetterDto>("تنظیم‌کننده نامه نمی‌تواند در زنجیره تایید همان نامه باشد.");
            }
            l.SignerPositionId = signer; l.ApprovalRound++; l.Status = OfLetterStatus.InApproval; l.ModifyDate = DateTimeOffset.Now; l.ModifierUserId = Me;
            int seq = 1;
            foreach (int p in chain) l.Approvals.Add(new OfLetterApproval { Round = l.ApprovalRound, Seq = seq++, PositionId = p, Status = OfApprovalStatus.Pending });
            string key = "letter:" + l.Id + ":approval";
            await CloseKeyAsync(key, "نامه دوباره فرستاده شد", ct);
            if (chain.Count > 0) AddInbox(key, OfInboxKind.Approval, "تایید نامه: " + l.Subject, l.Id, chain[0], null, null, null, l.Urgency);
            else AddInbox(key, OfInboxKind.Approval, "امضای نامه: " + l.Subject, l.Id, signer, null, null, null, l.Urgency);
            return await FinishAsync(l, c, ct);
        }

        private OfLetterApproval NextPending(OfLetter l) =>
            l.Approvals.Where(x => x.Round == l.ApprovalRound && x.Status == OfApprovalStatus.Pending).OrderBy(x => x.Seq).FirstOrDefault();

        private async Task<ResponseDto<LetterDto>> ApproveAsync(OfLetter l, Ctx c, LetterActionDto dto, CancellationToken ct)
        {
            if (l.Status != OfLetterStatus.InApproval) return Bad<LetterDto>("نامه در مرحله تایید نیست.");
            OfLetterApproval next = NextPending(l);
            if (next == null) return Bad<LetterDto>("همه تاییدها انجام شده و نوبت امضاست.");
            ActingPosition acting = Acting(c, next.PositionId, dto.ActingPositionId);
            if (acting == null) return Bad<LetterDto>("نوبت تایید با سمت دیگری است و شما مجاز به تایید در این مرحله نیستید.");
            if (l.CreatorUserId == Me) return Bad<LetterDto>(SameUser);
            next.Status = OfApprovalStatus.Approved; next.ActedByUserId = Me; next.OnBehalf = acting.OnBehalf; next.Comment = Clean(dto.Comment); next.ActedAt = DateTimeOffset.Now;
            string key = "letter:" + l.Id + ":approval";
            await CloseKeyAsync(key, "تایید شد", ct);
            OfLetterApproval after = l.Approvals.Where(x => x.Round == l.ApprovalRound && x.Status == OfApprovalStatus.Pending && x.Id != next.Id).OrderBy(x => x.Seq).FirstOrDefault();
            if (after != null) AddInbox(key, OfInboxKind.Approval, "تایید نامه: " + l.Subject, l.Id, after.PositionId, null, null, null, l.Urgency);
            else AddInbox(key, OfInboxKind.Approval, "امضای نامه: " + l.Subject, l.Id, l.SignerPositionId, null, null, null, l.Urgency);
            l.ModifyDate = DateTimeOffset.Now; l.ModifierUserId = Me;
            return await FinishAsync(l, c, ct);
        }

        private async Task<ResponseDto<LetterDto>> ReturnAsync(OfLetter l, Ctx c, LetterActionDto dto, CancellationToken ct)
        {
            if (l.Status != OfLetterStatus.InApproval) return Bad<LetterDto>("نامه در مرحله تایید نیست.");
            if (Clean(dto.Comment) == null) return Bad<LetterDto>("علت برگشت را بنویسید.");
            OfLetterApproval next = NextPending(l);
            ActingPosition acting;
            if (next != null)
            {
                acting = Acting(c, next.PositionId, dto.ActingPositionId);
                if (acting == null) return Bad<LetterDto>("نوبت تایید با سمت دیگری است.");
                next.Status = OfApprovalStatus.Returned; next.ActedByUserId = Me; next.OnBehalf = acting.OnBehalf; next.Comment = Clean(dto.Comment); next.ActedAt = DateTimeOffset.Now;
            }
            else
            {
                acting = l.SignerPositionId.HasValue ? Acting(c, l.SignerPositionId.Value, dto.ActingPositionId) : null;
                if (acting == null) return Bad<LetterDto>("فقط امضاکننده نامه را برمی‌گرداند.");
                int seq = l.Approvals.Where(x => x.Round == l.ApprovalRound).Select(x => x.Seq).DefaultIfEmpty(0).Max() + 1;
                l.Approvals.Add(new OfLetterApproval { Round = l.ApprovalRound, Seq = seq, PositionId = acting.PositionId, Status = OfApprovalStatus.Returned, ActedByUserId = Me, OnBehalf = acting.OnBehalf, Comment = Clean(dto.Comment), ActedAt = DateTimeOffset.Now });
            }
            l.Status = OfLetterStatus.Draft; l.ModifyDate = DateTimeOffset.Now; l.ModifierUserId = Me;
            await CloseKeyAsync("letter:" + l.Id + ":approval", "نامه برگشت داده شد", ct);
            AddInbox("letter:" + l.Id + ":returned", OfInboxKind.Task, "نامه برای اصلاح برگشت داده شد: " + l.Subject, l.Id, null, l.CreatorUserId, null);
            return await FinishAsync(l, c, ct);
        }

        private async Task<ResponseDto<LetterDto>> SignAsync(OfLetter l, Ctx c, LetterActionDto dto, CancellationToken ct)
        {
            if (l.Status != OfLetterStatus.InApproval) return Bad<LetterDto>("نامه در مرحله امضا نیست.");
            if (NextPending(l) != null) return Bad<LetterDto>("هنوز تاییدهای زنجیره تکمیل نشده است.");
            if (!l.SignerPositionId.HasValue) return Bad<LetterDto>("امضاکننده مشخص نیست.");
            ActingPosition acting = Acting(c, l.SignerPositionId.Value, dto.ActingPositionId);
            if (acting == null) return Bad<LetterDto>("فقط سمت امضاکننده (یا جانشین رسمی او) می‌تواند نامه را امضا کند.");
            if (!await _uow.Set<OfPosition>().AnyAsync(p => p.Id == l.SignerPositionId.Value && p.IsActive && p.CanSign, ct)) return Bad<LetterDto>("این سمت حق امضا ندارد.");
            OfUserSignature signature = await _uow.Set<OfUserSignature>().AsNoTracking().FirstOrDefaultAsync(s => s.UserId == Me, ct);
            if (signature == null) return Bad<LetterDto>("تصویر امضای شما ثبت نشده است؛ ابتدا تصویر امضا را بارگذاری کنید.");
            var unit = await UnitOfAsync(l.SenderPositionId, ct);
            DateTime today = DateTime.Today;
            var number = await NextNumberAsync(l.Kind, unit.UnitId, unit.Code, today, ct);
            if (number.Error != null) return Bad<LetterDto>(number.Error);
            l.Number = number.Number; l.NumberRuleId = number.RuleId; l.Date = today;
            l.SignedAt = DateTimeOffset.Now; l.SignedByUserId = Me; l.SignedOnBehalfOfPositionId = acting.OnBehalf ? l.SignerPositionId : null; l.SignatureDocumentId = signature.ImageDocumentId;
            l.ContentHash = ComputeHash(l);
            l.ModifyDate = DateTimeOffset.Now; l.ModifierUserId = Me;
            await CloseKeyAsync("letter:" + l.Id + ":approval", "امضا شد", ct);
            if (l.Kind == OfLetterKind.Internal)
            {
                l.Status = OfLetterStatus.Sent; l.SentDate = today;
                DeliverToPositions(l);
            }
            else
            {
                l.Status = OfLetterStatus.Signed;
                AddInbox("letter:" + l.Id + ":dispatch", OfInboxKind.Task, "نامه امضا شده برای ثبت و ارسال: " + l.Number + " — " + l.Subject, l.Id, null, null, OfficeRoles.Secretariat, null, l.Urgency);
            }
            return await FinishAsync(l, c, ct);
        }

        private async Task<ResponseDto<LetterDto>> RegisterAsync(OfLetter l, Ctx c, CancellationToken ct)
        {
            if (!c.IsSec) return Bad<LetterDto>(OfficeText.NotAllowed);
            if (l.Kind == OfLetterKind.Incoming && l.Status == OfLetterStatus.Draft)
            {
                string error = await RegisterIncomingAsync(l, ct);
                if (error != null) return Bad<LetterDto>(error);
                l.ModifyDate = DateTimeOffset.Now; l.ModifierUserId = Me;
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { return Bad<LetterDto>(OfficeText.Concurrency); }
                catch (DbUpdateException) { return Bad<LetterDto>("ثبت انجام نشد (احتمالاً هم‌زمان شماره گرفته شد)؛ دوباره تلاش کنید."); }
                DeliverToPositions(l);
                return await FinishAsync(l, c, ct);
            }
            if (l.Kind == OfLetterKind.Outgoing && l.Status == OfLetterStatus.Signed)
            {
                l.Status = OfLetterStatus.Registered; l.RegisteredAt = DateTimeOffset.Now; l.RegisteredByUserId = Me; l.ModifyDate = DateTimeOffset.Now; l.ModifierUserId = Me;
                return await FinishAsync(l, c, ct);
            }
            return Bad<LetterDto>("در این وضعیت نامه ثبت نمی‌شود.");
        }

        private async Task<ResponseDto<LetterDto>> SendAsync(OfLetter l, Ctx c, LetterActionDto dto, CancellationToken ct)
        {
            if (!c.IsSec) return Bad<LetterDto>(OfficeText.NotAllowed);
            if (l.Kind != OfLetterKind.Outgoing || (l.Status != OfLetterStatus.Signed && l.Status != OfLetterStatus.Registered)) return Bad<LetterDto>("فقط نامه صادره امضاشده ارسال می‌شود.");
            if (!dto.DeliveryMethod.HasValue || !Enum.IsDefined(typeof(OfDelivery), dto.DeliveryMethod.Value)) return Bad<LetterDto>("روش ارسال (دستی، پست، پیک، ایمیل، فکس) را مشخص کنید.");
            if (l.RegisteredAt == null) { l.RegisteredAt = DateTimeOffset.Now; l.RegisteredByUserId = Me; }
            l.Status = OfLetterStatus.Sent; l.DeliveryMethod = dto.DeliveryMethod; l.SentDate = (dto.Date ?? DateTime.Today).Date; l.ModifyDate = DateTimeOffset.Now; l.ModifierUserId = Me;
            await CloseKeyAsync("letter:" + l.Id + ":dispatch", "ارسال شد", ct);
            DeliverToPositions(l);
            return await FinishAsync(l, c, ct);
        }

        private async Task<ResponseDto<LetterDto>> ReceiptAsync(OfLetter l, Ctx c, LetterActionDto dto, CancellationToken ct)
        {
            if (!c.IsSec) return Bad<LetterDto>(OfficeText.NotAllowed);
            if (l.Status != OfLetterStatus.Sent) return Bad<LetterDto>("رسید تحویل فقط برای نامه ارسال‌شده ثبت می‌شود.");
            if (Clean(dto.Receipt) == null && !dto.ReceiptDocumentId.HasValue) return Bad<LetterDto>("شرح رسید یا تصویر رسید را وارد کنید.");
            if (dto.ReceiptDocumentId.HasValue && !await DocumentsExistAsync(new[] { dto.ReceiptDocumentId.Value }, ct)) return Bad<LetterDto>("فایل رسید پیدا نشد.");
            l.DeliveryReceipt = Clean(dto.Receipt); l.DeliveryReceiptDocumentId = dto.ReceiptDocumentId; l.ReceiptDate = (dto.Date ?? DateTime.Today).Date;
            l.ModifyDate = DateTimeOffset.Now; l.ModifierUserId = Me;
            return await FinishAsync(l, c, ct);
        }

        private async Task<ResponseDto<LetterDto>> ArchiveAsync(OfLetter l, Ctx c, CancellationToken ct)
        {
            if (!c.IsSec) return Bad<LetterDto>(OfficeText.NotAllowed);
            if (l.Status != OfLetterStatus.Sent && l.Status != OfLetterStatus.Received && l.Status != OfLetterStatus.Registered) return Bad<LetterDto>("در این وضعیت نامه بایگانی نمی‌شود.");
            l.Status = OfLetterStatus.Archived; l.ModifyDate = DateTimeOffset.Now; l.ModifierUserId = Me;
            return await FinishAsync(l, c, ct);
        }

        private async Task<ResponseDto<LetterDto>> CancelAsync(OfLetter l, Ctx c, LetterActionDto dto, CancellationToken ct)
        {
            bool numbered = l.Number != null;
            if (l.Status == OfLetterStatus.Cancelled) return Bad<LetterDto>("نامه قبلاً باطل شده است.");
            if (l.Status == OfLetterStatus.Archived && l.Imported) return Bad<LetterDto>("نامه آرشیو قدیم باطل نمی‌شود.");
            if (!numbered)
            {
                if (l.CreatorUserId != Me) return Bad<LetterDto>(OfficeText.NotAllowed);
            }
            else if (!c.IsSec) return Bad<LetterDto>("نامه شماره‌دار فقط توسط دبیرخانه باطل می‌شود (شماره در دفتر با وضعیت باطل می‌ماند).");
            if (numbered && Clean(dto.Comment) == null) return Bad<LetterDto>("علت ابطال نامه شماره‌دار را بنویسید.");
            l.Status = OfLetterStatus.Cancelled; l.CancelReason = Clean(dto.Comment); l.CancelledAt = DateTimeOffset.Now; l.ModifyDate = DateTimeOffset.Now; l.ModifierUserId = Me;
            List<OfInboxItem> items = await _uow.Set<OfInboxItem>().Where(i => i.SourceModule == "office" && i.SourceType == "letter" && i.SourceId == l.Id && (i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read)).ToListAsync(ct);
            foreach (OfInboxItem i in items) { i.Status = OfInboxStatus.Cancelled; i.ClosedAt = DateTimeOffset.Now; i.ClosedReason = "نامه باطل شد"; }
            return await FinishAsync(l, c, ct);
        }

        // -------------------------------------------------------------------------------- referrals

        public async Task<ResponseDto<LetterDto>> ReferAsync(ReferLetterDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Instruction) == null) return Bad<LetterDto>("دستور ارجاع را بنویسید.");
            if (!Enum.IsDefined(typeof(OfReferralAction), dto.Action)) return Bad<LetterDto>(OfficeText.InvalidData);
            List<int> targets = (dto.ToPositionIds ?? new List<int>()).Distinct().ToList();
            if (targets.Count == 0) return Bad<LetterDto>("گیرنده ارجاع را انتخاب کنید.");
            Ctx c = await ContextAsync(ct);
            OfLetter l = await LoadAsync(dto.LetterId, true, ct);
            if (l == null || AccessOf(l, c) != Access.Full) return OfficeText.NotFound<LetterDto>("نامه پیدا نشد.");
            if (l.Status != OfLetterStatus.Sent && l.Status != OfLetterStatus.Received && l.Status != OfLetterStatus.Registered) return Bad<LetterDto>("نامه‌ای که به دست گیرنده نرسیده ارجاع نمی‌شود.");
            OfReferral parent = null;
            if (dto.ParentReferralId.HasValue)
            {
                parent = l.Referrals.FirstOrDefault(r => r.Id == dto.ParentReferralId.Value);
                if (parent == null || parent.Status == OfReferralStatus.Cancelled) return Bad<LetterDto>("ارجاع قبلی پیدا نشد.");
            }
            int? from = dto.FromPositionId;
            if (!from.HasValue && parent != null) from = parent.ToPositionId;
            if (!from.HasValue && c.Acting.Count == 1) from = c.Acting[0].PositionId;
            ActingPosition acting = from.HasValue ? c.Acting.FirstOrDefault(a => a.PositionId == from.Value) : null;
            if (acting == null) return Bad<LetterDto>("سمتی که از طرف آن ارجاع می‌دهید را مشخص کنید.");
            if (parent != null && parent.ToPositionId != acting.PositionId) return Bad<LetterDto>("فقط گیرنده یک ارجاع می‌تواند آن را ادامه دهد.");
            if (parent == null && !HoldsLetter(l, acting.PositionId)) return Bad<LetterDto>("این سمت نامه را در اختیار ندارد و نمی‌تواند ارجاع دهد.");
            if (await _uow.Set<OfPosition>().CountAsync(p => targets.Contains(p.Id) && !p.Deleted && p.IsActive, ct) != targets.Count) return Bad<LetterDto>("یکی از سمت‌های مقصد پیدا نشد.");
            if (targets.Contains(acting.PositionId)) return Bad<LetterDto>("ارجاع به همان سمت معنی ندارد.");
            DateTimeOffset now = DateTimeOffset.Now;
            var created = new List<OfReferral>();
            foreach (int target in targets)
            {
                var r = new OfReferral
                {
                    LetterId = l.Id, ParentId = parent?.Id, FromPositionId = acting.PositionId, ToPositionId = target, Instruction = Clean(dto.Instruction), Action = dto.Action,
                    DueDate = dto.DueDate?.Date, Status = OfReferralStatus.Pending, CreatedAt = now, CreatedByUserId = Me, OnBehalf = acting.OnBehalf, Entries = new List<OfReferralEntry>(),
                };
                l.Referrals.Add(r);
                created.Add(r);
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<LetterDto>("ثبت ارجاع انجام نشد."); }
            string label = dto.Action == OfReferralAction.ForAction ? "ارجاع برای اقدام: " : dto.Action == OfReferralAction.ForComment ? "ارجاع برای نظر: " : "ارجاع برای اطلاع: ";
            foreach (OfReferral r in created)
            {
                _uow.Set<OfInboxItem>().Add(new OfInboxItem
                {
                    RecipientPositionId = r.ToPositionId, Kind = OfInboxKind.Referral, Title = (label + l.Subject).Length > 300 ? (label + l.Subject).Substring(0, 300) : label + l.Subject, SourceModule = "office",
                    SourceType = "letter", SourceId = l.Id, SourceKey = "referral:" + r.Id, Link = "/office/letters/" + l.Id, DueDate = r.DueDate, Priority = l.Urgency, Status = OfInboxStatus.Unread, CreatedAt = now,
                });
            }
            return await FinishAsync(l, c, ct);
        }

        public async Task<ResponseDto<LetterDto>> ReferralActionAsync(ReferralActionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<LetterDto>(OfficeText.InvalidData);
            Ctx c = await ContextAsync(ct);
            OfReferral r = await _uow.Set<OfReferral>().Include(x => x.Entries).FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (r == null) return OfficeText.NotFound<LetterDto>("ارجاع پیدا نشد.");
            OfLetter l = await LoadAsync(r.LetterId, true, ct);
            if (l == null || AccessOf(l, c) != Access.Full) return OfficeText.NotFound<LetterDto>("ارجاع پیدا نشد.");
            if (r.Status != OfReferralStatus.Pending) return Bad<LetterDto>("این ارجاع دیگر باز نیست.");
            DateTimeOffset now = DateTimeOffset.Now;
            string text = Clean(dto.Text);
            switch ((dto.Action ?? "").Trim().ToLowerInvariant())
            {
                case "comment":
                {
                    ActingPosition acting = Acting(c, r.ToPositionId, dto.ActingPositionId);
                    if (acting == null) return Bad<LetterDto>("فقط گیرنده ارجاع اقدام یا نظر ثبت می‌کند.");
                    if (text == null) return Bad<LetterDto>("متن اقدام یا نظر را بنویسید.");
                    r.Entries.Add(new OfReferralEntry { UserId = Me, OnBehalf = acting.OnBehalf, Text = text, At = now });
                    break;
                }
                case "close":
                {
                    ActingPosition acting = Acting(c, r.ToPositionId, dto.ActingPositionId);
                    if (acting == null) return Bad<LetterDto>("فقط گیرنده ارجاع آن را می‌بندد.");
                    if (r.Action == OfReferralAction.ForAction && text == null && !r.Entries.Any()) return Bad<LetterDto>("برای ارجاع «برای اقدام» شرح اقدام انجام‌شده لازم است.");
                    r.Status = OfReferralStatus.Done; r.DoneAt = now; r.DoneByUserId = Me; r.DoneOnBehalf = acting.OnBehalf; r.DoneNote = text;
                    await CloseKeyAsync("referral:" + r.Id, "ارجاع بسته شد", ct);
                    if (r.FromPositionId.HasValue)
                        _uow.Set<OfInboxItem>().Add(new OfInboxItem
                        {
                            RecipientPositionId = r.FromPositionId, Kind = OfInboxKind.Alert, Title = ("ارجاع شما بسته شد: " + l.Subject).Length > 300 ? "ارجاع شما بسته شد" : "ارجاع شما بسته شد: " + l.Subject,
                            SourceModule = "office", SourceType = "letter", SourceId = l.Id, SourceKey = "referral-done:" + r.Id, Link = "/office/letters/" + l.Id, Status = OfInboxStatus.Unread, CreatedAt = now,
                        });
                    break;
                }
                case "cancel":
                    if (r.CreatedByUserId != Me && !c.IsSec) return Bad<LetterDto>(OfficeText.NotAllowed);
                    r.Status = OfReferralStatus.Cancelled;
                    await CloseKeyAsync("referral:" + r.Id, "ارجاع لغو شد", ct);
                    break;
                default:
                    return Bad<LetterDto>("عمل نامعتبر است.");
            }
            return await FinishAsync(l, c, ct);
        }
    }
}
