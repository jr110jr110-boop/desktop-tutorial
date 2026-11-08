using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;

namespace Services.NasbNiro.ProductBank
{
    /// <summary>Number series of the design control (kinds of <c>DocumentNumberSequence</c>, one counter per Persian year).</summary>
    internal static class DesignNumbers
    {
        public const int Plan = 440;
        public const int Input = 441;
        public const int Review = 442;
    }

    /// <summary>Package A1: the reads that the plan, input and review services share.</summary>
    internal static class DesignQueries
    {
        /// <summary>The inputs in force of a product revision: Open and Conflict (a replaced or voided input is history).</summary>
        public static Task<List<PbDesignInput>> LiveInputsAsync(IUnitOfWork uow, int productId, int revision, bool tracked, CancellationToken ct)
        {
            IQueryable<PbDesignInput> q = uow.Set<PbDesignInput>().Where(i => !i.Deleted && i.ProductId == productId && i.ProductRevision == revision && (i.Status == DesignInputStatus.Open || i.Status == DesignInputStatus.Conflict));
            return (tracked ? q : q.AsNoTracking()).OrderBy(i => i.Code).ToListAsync(ct);
        }

        /// <summary>Persian list of the inputs in conflict, or null.</summary>
        public static async Task<string> ConflictTextAsync(IUnitOfWork uow, int productId, int revision, CancellationToken ct)
        {
            List<string> codes = await uow.Set<PbDesignInput>().AsNoTracking().Where(i => !i.Deleted && i.ProductId == productId && i.ProductRevision == revision && i.Status == DesignInputStatus.Conflict).OrderBy(i => i.Code).Select(i => i.Code).ToListAsync(ct);
            return codes.Count == 0 ? null : "ورودی‌های طراحی در تعارض‌اند و تا رفع تعارض توسط نفر دوم پیشرفت طراحی متوقف است: " + string.Join("، ", codes);
        }

        /// <summary>
        /// The completeness statement of the plan is valid when it exists, nothing is missing and no live input was created or changed after it (a new or changed input needs a new statement).
        /// Returns the Persian reason it is not valid, or null.
        /// </summary>
        public static async Task<string> InputsIncompleteReasonAsync(IUnitOfWork uow, PbDesignPlan plan, CancellationToken ct)
        {
            if (!plan.InputsCompleteAt.HasValue || !plan.InputsCompleteByUserId.HasValue) return "کامل بودن ورودی‌های طراحی هنوز توسط نفر دوم اعلام نشده است.";
            List<PbDesignInput> live = await LiveInputsAsync(uow, plan.ProductId, plan.ProductRevision, false, ct);
            List<DesignInputKind> missing = DesignRules.MissingKinds(live, DesignRules.ParseKinds(plan.NotApplicableKinds));
            if (missing.Count > 0) return "برای این انواع ورودی نه ورودی ثبت شده و نه «لازم نیست» اعلام شده است: " + string.Join("، ", missing.Select(DesignRules.KindText));
            if (live.Any(i => i.ModifyDate > plan.InputsCompleteAt.Value || i.CreateDate > plan.InputsCompleteAt.Value)) return "پس از اعلام کامل بودن، ورودی جدید یا تغییر کرده ثبت شده است؛ کامل بودن را دوباره اعلام کنید.";
            return null;
        }

        /// <summary>Names of users; the key is missing for an unknown user.</summary>
        public static Task<Dictionary<int, string>> NamesAsync(IUnitOfWork uow, IEnumerable<int?> ids, CancellationToken ct) => InventoryUsers.GetNamesAsync(uow, ids, ct);

        /// <summary>The Persian reason a user cannot be named as a person in charge or a participant (unknown, inactive, an AI seat), or null.</summary>
        public static async Task<string> PersonProblemAsync(IUnitOfWork uow, IEnumerable<int> userIds, CancellationToken ct)
        {
            List<int> ids = userIds.Where(i => i > 0).Distinct().ToList();
            if (ids.Count == 0) return null;
            var users = await uow.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.UserName, u.IsActive }).ToListAsync(ct);
            foreach (int id in ids)
            {
                var u = users.FirstOrDefault(x => x.Id == id);
                if (u == null) return "یکی از افراد نام‌برده پیدا نشد.";
                if (!u.IsActive) return $"کاربر {u.UserName} غیرفعال است.";
                if (AiSeatNames.IsSeatUser(u.UserName)) return "صندلی هوش مصنوعی نمی‌تواند مسئول یا شرکت‌کننده باشد؛ فقط انسان.";
            }
            return null;
        }

        public static string B64(byte[] rowVersion) => rowVersion == null ? null : Convert.ToBase64String(rowVersion);
    }
}
