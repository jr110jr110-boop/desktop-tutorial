using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Finance
{
    public interface IAuditChainService
    {
        /// <summary>
        /// Walks the hash chain of the audit trail from place <paramref name="from"/> (default 1) to place <paramref name="to"/> (default the last) and reports the first break.
        /// <paramref name="anchorSeq"/> and <paramref name="anchorHash"/> are an earlier head that the auditor wrote down: the row at that place must still carry that hash.
        /// </summary>
        Task<ResponseDto<AuditChainVerifyDto>> VerifyAsync(long? from, long? to, long? anchorSeq, string anchorHash, CancellationToken ct);

        /// <summary>
        /// Package D: the same walk without the check of the caller's role and without the limit of rows of one call, for the scheduled verification (<c>AuditIntegrityService</c>), which has no signed-in user.
        /// The arguments are the ones <see cref="VerifyAsync"/> has after its own checks: <paramref name="from"/> at least 1, <paramref name="anchorSeq"/> and <paramref name="anchorHash"/> both or none.
        /// </summary>
        Task<AuditChainVerifyDto> VerifyUncheckedAsync(long from, long? to, long? anchorSeq, string anchorHash, CancellationToken ct);
    }

    /// <summary>
    /// R1t D20: the check of the tamper-evident audit trail (see <see cref="AuditChain"/>). Read-only; the auditor and the finance manager run it. It reads the rows in pages by their place in the chain,
    /// so a trail of any size is walked with a small memory. What it can and cannot prove:
    /// a changed, deleted or slipped-in row, and a chain that was cut in the middle, show as the first break; a row cut off the END of the chain (the newest rows) cannot be seen from the table alone,
    /// so the answer carries the head (place and hash), and the auditor gives it back later as the anchor. With the key (<c>Identity:AuditHmacKey</c>) a person with access to the table alone cannot re-seal the chain.
    /// </summary>
    public class AuditChainService : IAuditChainService
    {
        public const string Roles = "AUDITOR,FINANCEMANAGER";
        public const string NotAllowed = "فقط حسابرس و مدیر مالی می‌توانند سلامت زنجیره سوابق را بررسی کنند.";
        internal const int PageSize = 2000;
        /// <summary>M12 (review R1t) ★: the most rows one verify call walks. A longer chain is verified in ranges (from = the row after the last range, which starts at the stored hash of its predecessor); the chain is not walked whole by one call that any finance manager can repeat.</summary>
        public const long MaxRowsPerCall = 50000;

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IConfiguration _configuration;

        public AuditChainService(IUnitOfWork uow, IInventoryUserContext user, IConfiguration configuration)
        {
            _uow = uow;
            _user = user;
            _configuration = configuration;
        }

        private IQueryable<FnAuditLog> Rows => _uow.Set<FnAuditLog>().AsNoTracking();

        public async Task<ResponseDto<AuditChainVerifyDto>> VerifyAsync(long? from, long? to, long? anchorSeq, string anchorHash, CancellationToken ct)
        {
            if (!_user.IsInRole(Roles)) return ResponseDtoHelper.Forbidden<AuditChainVerifyDto>(NotAllowed);
            long first = from ?? 1;
            if (first < 1 || (to.HasValue && to.Value < first)) return ResponseDtoHelper.BadRequest<AuditChainVerifyDto>("بازه ردیف‌های زنجیره نامعتبر است: «از» باید از ۱ کمتر نباشد و «تا» از «از» کمتر نشود.");
            string anchor = string.IsNullOrWhiteSpace(anchorHash) ? null : anchorHash.Trim();
            if ((anchorSeq.HasValue) != (anchor != null) || (anchorSeq.HasValue && anchorSeq.Value < 1))
                return ResponseDtoHelper.BadRequest<AuditChainVerifyDto>("برای نشانی (anchor) هم شماره ردیف و هم مهر آن ردیف را بدهید.");

            ResponseDto<AuditChainVerifyDto> tooLong = null;
            AuditChainVerifyDto result = await VerifyCoreAsync(first, to, anchorSeq, anchor, true, ct, message => tooLong = ResponseDtoHelper.BadRequest<AuditChainVerifyDto>(message));
            return tooLong ?? ResponseDtoHelper.Ok(result);
        }

        public Task<AuditChainVerifyDto> VerifyUncheckedAsync(long from, long? to, long? anchorSeq, string anchorHash, CancellationToken ct) =>
            VerifyCoreAsync(from, to, anchorSeq, string.IsNullOrWhiteSpace(anchorHash) ? null : anchorHash.Trim(), false, ct, null);

        private async Task<AuditChainVerifyDto> VerifyCoreAsync(long first, long? to, long? anchorSeq, string anchor, bool limit, CancellationToken ct, Action<string> refuse = null)
        {
            byte[] key = AuditChain.KeyFrom(_configuration);
            var head = await Rows.Where(a => a.Seq != null).OrderByDescending(a => a.Seq).Select(a => new { Seq = a.Seq.Value, a.Hash }).FirstOrDefaultAsync(ct);
            var dto = new AuditChainVerifyDto
            {
                FromSeq = first,
                ToSeq = to,
                HeadSeq = head?.Seq,
                HeadHash = head?.Hash,
                SealedRows = await Rows.LongCountAsync(a => a.Seq != null, ct),
                LegacyRows = await Rows.LongCountAsync(a => a.Seq == null, ct),
                KeyConfigured = key != null,
                VerifiedAt = DateTimeOffset.Now,
            };
            long last = Math.Min(to ?? long.MaxValue, head?.Seq ?? 0);
            if (limit && head != null && last - first + 1 > MaxRowsPerCall)
            {
                refuse?.Invoke($"بازه بررسی بیش از {MaxRowsPerCall.ToString("N0", CultureInfo.InvariantCulture)} ردیف است؛ زنجیره را بازه‌بازه بررسی کنید (مثلاً از {first.ToString(CultureInfo.InvariantCulture)} تا {(first + MaxRowsPerCall - 1).ToString(CultureInfo.InvariantCulture)} و بعد از آن بازه بعدی). آخرین ردیف زنجیره شماره {head.Seq.ToString(CultureInfo.InvariantCulture)} است.");
                return null;
            }
            dto.WholeChain = first == 1 && (!to.HasValue || (head != null && to.Value >= head.Seq));

            AuditChain.ChainBreak broken = await WalkAsync(dto, first, to, key, ct) ?? await UnsealedAsync(ct) ?? await AnchorAsync(anchorSeq, anchor, ct);
            if (broken != null)
            {
                dto.Ok = false;
                dto.FirstBreak = await Describe(broken, ct);
                dto.Message = "زنجیره سوابق سالم نیست؛ " + broken.Message;
                return dto;
            }

            dto.Ok = true;
            dto.Message = head == null
                ? "هنوز ردیفی در زنجیره سوابق نیست؛ ردیف‌های بعدی مهر می‌شوند."
                : "زنجیره سوابق سالم است: " + dto.Checked.ToString(CultureInfo.InvariantCulture) + " ردیف بررسی شد (ردیف "
                  + first.ToString(CultureInfo.InvariantCulture) + " تا " + (dto.WholeChain ? head.Seq : Math.Min(to ?? head.Seq, head.Seq)).ToString(CultureInfo.InvariantCulture) + ")"
                  + (anchor != null ? "؛ نشانی ثبت‌شده با ردیف " + anchorSeq.Value.ToString(CultureInfo.InvariantCulture) + " می‌خواند" : "")
                  + (dto.KeyConfigured ? "." : "؛ توجه: کلید مهر (Identity:AuditHmacKey) تنظیم نشده و ردیف‌ها بدون کلید مهر شده‌اند؛ بازنویسی کامل زنجیره با دسترسی به پایگاه داده را نشان نمی‌دهد.")
                  + (dto.LegacyRows > 0 ? " " + dto.LegacyRows.ToString(CultureInfo.InvariantCulture) + " ردیف قدیمی پیش از آغاز زنجیره مهر ندارند." : "");
            return dto;
        }

        /// <summary>Walks the sealed rows in the asked range, in pages ordered by (place, id), and returns the first break.</summary>
        private async Task<AuditChain.ChainBreak> WalkAsync(AuditChainVerifyDto dto, long first, long? to, byte[] key, CancellationToken ct)
        {
            AuditChain.Walker walker;
            if (first == 1) walker = new AuditChain.Walker(key, 0, null);
            else
            {
                // the row before the range is the anchor of the walk (its own content is checked when the walk starts earlier)
                var before = await Rows.Where(a => a.Seq == first - 1).Select(a => new { a.Id, a.Hash }).FirstOrDefaultAsync(ct);
                if (before == null)
                {
                    bool any = await Rows.AnyAsync(a => a.Seq != null && a.Seq >= first, ct);
                    if (!any) return null;   // nothing at or after the start: nothing to check
                    return new AuditChain.ChainBreak { Kind = AuditChain.BreakGap, Seq = first - 1, Id = 0, Message = "ردیف شماره " + (first - 1).ToString(CultureInfo.InvariantCulture) + " زنجیره در سوابق نیست؛ حذف شده است." };
                }
                walker = new AuditChain.Walker(key, first - 1, before.Hash);
            }

            long cursorSeq = first - 1;
            int cursorId = int.MaxValue;   // nothing at the place before the range is read again
            while (true)
            {
                IQueryable<FnAuditLog> q = Rows.Where(a => a.Seq != null && (a.Seq > cursorSeq || (a.Seq == cursorSeq && a.Id > cursorId)));
                if (to.HasValue) q = q.Where(a => a.Seq <= to.Value);
                List<FnAuditLog> page = await q.OrderBy(a => a.Seq).ThenBy(a => a.Id).Take(PageSize).ToListAsync(ct);
                if (page.Count == 0) break;
                foreach (FnAuditLog row in page)
                {
                    AuditChain.ChainBreak broken = walker.Feed(row);
                    if (broken != null)
                    {
                        dto.Checked = walker.Checked;
                        return broken;
                    }
                }
                cursorSeq = page[page.Count - 1].Seq.Value;
                cursorId = page[page.Count - 1].Id;
                if (page.Count < PageSize) break;
            }
            dto.Checked = walker.Checked;
            return null;
        }

        /// <summary>A row without a seal that was written after the chain began is a row somebody put into the table beside the chain.</summary>
        private async Task<AuditChain.ChainBreak> UnsealedAsync(CancellationToken ct)
        {
            var start = await Rows.Where(a => a.Seq != null).OrderBy(a => a.Seq).Select(a => new { a.Id, a.At }).FirstOrDefaultAsync(ct);
            if (start == null) return null;
            FnAuditLog loose = await Rows.Where(a => a.Seq == null && (a.Id > start.Id || a.At > start.At)).OrderBy(a => a.Id).FirstOrDefaultAsync(ct);
            return loose == null ? null : new AuditChain.ChainBreak
            {
                Kind = AuditChain.BreakUnsealed, Seq = null, Id = loose.Id,
                Message = "ردیف شناسه " + loose.Id.ToString(CultureInfo.InvariantCulture) + " در سوابق مهر زنجیره ندارد، در حالی که پس از آغاز زنجیره نوشته شده است.",
            };
        }

        private async Task<AuditChain.ChainBreak> AnchorAsync(long? anchorSeq, string anchorHash, CancellationToken ct)
        {
            if (!anchorSeq.HasValue) return null;
            var row = await Rows.Where(a => a.Seq == anchorSeq.Value).Select(a => new { a.Id, a.Hash }).FirstOrDefaultAsync(ct);
            if (row == null)
                return new AuditChain.ChainBreak { Kind = AuditChain.BreakAnchor, Seq = anchorSeq, Id = 0, Message = "ردیف شماره " + anchorSeq.Value.ToString(CultureInfo.InvariantCulture) + " که مهر آن را ثبت کرده بودید در سوابق نیست؛ ردیف‌های انتهای زنجیره حذف شده‌اند." };
            if (!string.Equals(row.Hash, anchorHash, StringComparison.Ordinal))
                return new AuditChain.ChainBreak { Kind = AuditChain.BreakAnchor, Seq = anchorSeq, Id = row.Id, Message = "مهر ردیف شماره " + anchorSeq.Value.ToString(CultureInfo.InvariantCulture) + " با مهری که ثبت کرده بودید نمی‌خواند؛ زنجیره از آن ردیف به بعد دوباره ساخته شده است." };
            return null;
        }

        private async Task<AuditChainBreakDto> Describe(AuditChain.ChainBreak broken, CancellationToken ct)
        {
            var dto = new AuditChainBreakDto { Kind = broken.Kind, Seq = broken.Seq, Id = broken.Id, Message = broken.Message };
            if (broken.Id > 0)
            {
                var row = await Rows.Where(a => a.Id == broken.Id).Select(a => new { a.At, a.EntityType, a.Action }).FirstOrDefaultAsync(ct);
                if (row != null) { dto.At = row.At; dto.EntityType = row.EntityType; dto.Action = row.Action; }
            }
            return dto;
        }
    }
}
