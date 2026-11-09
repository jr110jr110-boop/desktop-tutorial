using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.AuditSeatDtos;

namespace Services.NasbNiro.AuditSeat
{
    public interface IAusAcceptanceService
    {
        Task<ResponseDto<List<AusAcceptanceDto>>> GetAsync(CancellationToken ct);
        Task<ResponseDto<AusAcceptanceDto>> ProposeAsync(AusAcceptanceProposeDto dto, CancellationToken ct);
        Task<ResponseDto<AusAcceptanceDto>> StepAsync(AusAcceptanceStepDto dto, CancellationToken ct);
    }

    /// <summary>
    /// Accepted deviation («انحراف پذیرفته‌شده موقت»): QA proposes, PMO agrees and the CEO accepts; three distinct persons, never an AI seat. The acceptance has a reason and an expiry date (at most one year).
    /// While it is active the run reports the hit as accepted (in the reports, no finding, no alert); after the expiry date the hit is a finding again. Rows are never deleted.
    /// </summary>
    public class AusAcceptanceService : IAusAcceptanceService
    {
        private const int MinReason = 15;
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public AusAcceptanceService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);

        public static string StatusText(AusAcceptanceStatus s) => s == AusAcceptanceStatus.Proposed ? "پیشنهاد QA" : s == AusAcceptanceStatus.PmoAgreed ? "تایید PMO" : s == AusAcceptanceStatus.Active ? "پذیرفته‌شده" : "ردشده";

        public static AusAcceptanceDto ToDto(AusAcceptance a) => new AusAcceptanceDto
        {
            Id = a.Id, RuleCode = a.RuleCode, RuleTitle = AusCatalog.Find(a.RuleCode)?.Title ?? a.RuleCode, ObjectKey = a.ObjectKey, ObjectNumber = a.ObjectNumber, Reason = a.Reason, ExpiresOn = a.ExpiresOn, ExpiresOnText = AusEvidence.Day(a.ExpiresOn),
            Status = StatusText(a.Status), StatusValue = (int)a.Status, Valid = a.Status == AusAcceptanceStatus.Active && a.ExpiresOn.Date >= DateTime.Now.Date,
            QaUserId = a.QaUserId, PmoUserId = a.PmoUserId, CeoUserId = a.CeoUserId, RefusalReason = a.RefusalReason,
        };

        public async Task<ResponseDto<List<AusAcceptanceDto>>> GetAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(AusRoles.Readers)) return Bad<List<AusAcceptanceDto>>(QualityText.NotAllowed);
            List<AusAcceptance> rows = await _uow.Set<AusAcceptance>().AsNoTracking().Where(a => !a.Deleted).OrderByDescending(a => a.Id).Take(500).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(ToDto).ToList());
        }

        public async Task<ResponseDto<AusAcceptanceDto>> ProposeAsync(AusAcceptanceProposeDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<AusAcceptanceDto>(QualityText.InvalidData);
            if (AiActing.IsActing) return Bad<AusAcceptanceDto>("پذیرش انحراف با انسان است.");
            if (!_user.IsInRole(AusRoles.Qa)) return Bad<AusAcceptanceDto>(QualityText.NotAllowed);
            string rule = dto.RuleCode?.Trim(), key = dto.ObjectKey?.Trim(), number = dto.ObjectNumber?.Trim(), reason = dto.Reason?.Trim();
            if (AusCatalog.Find(rule) == null) return Bad<AusAcceptanceDto>("قاعده‌ی ممیزی شناخته نشد.");
            if (string.IsNullOrEmpty(key) || key.Length > 200) return Bad<AusAcceptanceDto>("کلید شیء (حداکثر ۲۰۰ نویسه) لازم است.");
            if (number != null && number.Length > 80) return Bad<AusAcceptanceDto>("شماره‌ی شیء حداکثر ۸۰ نویسه است.");
            if (string.IsNullOrEmpty(reason) || reason.Length < MinReason) return Bad<AusAcceptanceDto>("دلیل پذیرش را (دست‌کم ۱۵ نویسه) بنویسید.");
            if (reason.Length > 1000) return Bad<AusAcceptanceDto>("دلیل پذیرش حداکثر ۱۰۰۰ نویسه است.");
            DateTime today = DateTime.Now.Date, exp = dto.ExpiresOn.Date;
            if (exp <= today) return Bad<AusAcceptanceDto>("تاریخ انقضا باید پس از امروز باشد.");
            if (exp > today.AddDays(365)) return Bad<AusAcceptanceDto>("پذیرش موقت حداکثر یک سال اعتبار دارد.");
            bool dup = await _uow.Set<AusAcceptance>().AnyAsync(a => !a.Deleted && a.RuleCode == rule && a.ObjectKey == key && (a.Status == AusAcceptanceStatus.Proposed || a.Status == AusAcceptanceStatus.PmoAgreed || (a.Status == AusAcceptanceStatus.Active && a.ExpiresOn >= today)), ct);
            if (dup) return Bad<AusAcceptanceDto>("برای این قاعده و شیء، پذیرش در جریان یا معتبر وجود دارد.");
            DateTimeOffset now = DateTimeOffset.Now;
            var a = new AusAcceptance { RuleCode = rule, ObjectKey = key, ObjectNumber = number, Reason = reason, ExpiresOn = exp, Status = AusAcceptanceStatus.Proposed, QaUserId = _user.UserId, QaAt = now, CreateDate = now, ModifyDate = now };
            _uow.Set<AusAcceptance>().Add(a);
            try { await _uow.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Bad<AusAcceptanceDto>(QualityText.Concurrency); }
            await Notify(a, AusRoles.Pmo, "پذیرش موقت انحراف منتظر تایید PMO است: " + rule, ct);
            return ResponseDtoHelper.Ok(ToDto(a));
        }

        public async Task<ResponseDto<AusAcceptanceDto>> StepAsync(AusAcceptanceStepDto dto, CancellationToken ct)
        {
            if (dto == null || dto.AcceptanceId <= 0) return Bad<AusAcceptanceDto>(QualityText.InvalidData);
            if (AiActing.IsActing) return Bad<AusAcceptanceDto>("پذیرش انحراف با انسان است.");
            string step = dto.Step?.Trim().ToLowerInvariant(), note = dto.Note?.Trim();
            if (step != "pmo" && step != "ceo") return Bad<AusAcceptanceDto>(QualityText.InvalidData);
            if (!_user.IsInRole(step == "pmo" ? AusRoles.Pmo : AusRoles.Ceo)) return Bad<AusAcceptanceDto>(QualityText.NotAllowed);
            if (note != null && note.Length > 1000) return Bad<AusAcceptanceDto>("یادداشت حداکثر ۱۰۰۰ نویسه است.");
            AusAcceptance a = await _uow.Set<AusAcceptance>().FirstOrDefaultAsync(x => x.Id == dto.AcceptanceId && !x.Deleted, ct);
            if (a == null) return QualityText.NotFound<AusAcceptanceDto>("پذیرش پیدا نشد.");
            int me = _user.UserId; DateTimeOffset now = DateTimeOffset.Now;
            if (step == "pmo" ? a.Status != AusAcceptanceStatus.Proposed : a.Status != AusAcceptanceStatus.PmoAgreed) return Bad<AusAcceptanceDto>("این مرحله اکنون در نوبت نیست.");
            if (dto.Refuse)
            {
                if (note == null || note.Length < 10) return Bad<AusAcceptanceDto>("دلیل رد را (دست‌کم ۱۰ نویسه) بنویسید.");
                a.Status = AusAcceptanceStatus.Rejected; a.RefusalReason = note;
            }
            else
            {
                if (step == "pmo")
                {
                    if (me == a.QaUserId) return Bad<AusAcceptanceDto>("پیشنهاددهنده و تاییدکننده باید دو نفر متفاوت باشند.");
                    a.PmoUserId = me; a.PmoAt = now; a.Status = AusAcceptanceStatus.PmoAgreed;
                }
                else
                {
                    if (me == a.QaUserId || me == a.PmoUserId) return Bad<AusAcceptanceDto>("سه امضاکننده باید سه نفر متفاوت باشند.");
                    if (a.ExpiresOn.Date <= DateTime.Now.Date) return Bad<AusAcceptanceDto>("تاریخ انقضا گذشته است؛ پیشنهاد تازه ثبت کنید.");
                    a.CeoUserId = me; a.CeoAt = now; a.Status = AusAcceptanceStatus.Active;
                }
            }
            a.ModifyDate = now;
            try { await _uow.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Bad<AusAcceptanceDto>(QualityText.Concurrency); }
            if (!dto.Refuse && step == "pmo") await Notify(a, AusRoles.Ceo, "پذیرش موقت انحراف منتظر تایید مدیرعامل است: " + a.RuleCode, ct);
            return ResponseDtoHelper.Ok(ToDto(a));
        }

        private async Task Notify(AusAcceptance a, string role, string title, CancellationToken ct)
        {
            if (_notify == null) return;
            await _notify.NotifyAsync(new NotifyRequest { SourceKey = "aus-accept:" + a.Id + ":" + (int)a.Status, Module = "AUDIT", SourceType = "AusAcceptance", SourceId = a.Id, Kind = OfInboxKind.Approval, Title = title, Link = "/audit/acceptances", Roles = role }, ct);
        }
    }
}
