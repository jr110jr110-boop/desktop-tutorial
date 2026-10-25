using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Sales;
using Entities.Model.NasbNiro.Tenders;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.SalesDtos;

namespace Services.NasbNiro.Sales
{
    public interface ISlCrmService
    {
        Task<ResponseDto<PagedList<SlCustomerDto>>> GetCustomersAsync(SlCustomerQuery query, CancellationToken ct);
        Task<ResponseDto<SlCustomerDto>> GetCustomerAsync(int id, CancellationToken ct);
        Task<ResponseDto<SlCustomerDto>> SaveCustomerAsync(SlSaveCustomerDto dto, CancellationToken ct);
        Task<ResponseDto<SlCustomerDto>> SetCreditAsync(SlSetCreditDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<SlContactDto>>> GetContactsAsync(SlContactQuery query, CancellationToken ct);
        Task<ResponseDto<SlContactDto>> SaveContactAsync(SlSaveContactDto dto, CancellationToken ct);
        Task<ResponseDto<PagedList<SlSiteDto>>> GetSitesAsync(SlSiteQuery query, CancellationToken ct);
        Task<ResponseDto<SlSiteDto>> SaveSiteAsync(SlSaveSiteDto dto, CancellationToken ct);
        Task<ResponseDto<PagedList<SlActivityDto>>> GetActivitiesAsync(SlActivityQuery query, CancellationToken ct);
        Task<ResponseDto<SlActivityDto>> SaveActivityAsync(SlSaveActivityDto dto, CancellationToken ct);

        Task<ResponseDto<SlLinkLegacyResultDto>> LinkLegacyAsync(CancellationToken ct);
        Task<ResponseDto<SlLinkDto>> LinkManualAsync(SlManualLinkDto dto, CancellationToken ct);
        Task<ResponseDto<PagedList<SlLinkDto>>> GetLinksAsync(SlLinkQuery query, CancellationToken ct);
        Task<ResponseDto<List<SlLinkSourceDto>>> GetUnlinkedAsync(CancellationToken ct);

        Task<ResponseDto<List<SlSettingDto>>> GetSettingsAsync(CancellationToken ct);
        Task<ResponseDto<SlSettingDto>> SaveSettingAsync(SlSaveSettingDto dto, CancellationToken ct);
        Task<ResponseDto<PagedList<SlReasonCodeDto>>> GetReasonCodesAsync(SlReasonCodeQuery query, CancellationToken ct);
        Task<ResponseDto<SlReasonCodeDto>> SaveReasonCodeAsync(SlSaveReasonCodeDto dto, CancellationToken ct);
    }

    /// <summary>Customers (side table of the finance customer), contacts, sites, activities, the link of legacy contracts and tenders, settings and reason codes.</summary>
    public class SlCrmService : ISlCrmService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public SlCrmService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => SlText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => SlText.Bad<T>(m);

        private static string Digits(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (c >= '0' && c <= '9') sb.Append(c);
                else if (c >= '۰' && c <= '۹') sb.Append((char)('0' + (c - '۰')));
                else if (c >= '٠' && c <= '٩') sb.Append((char)('0' + (c - '٠')));
            }
            return sb.ToString();
        }

        // ================================================================================== customers

        private async Task<List<SlCustomerDto>> CustomerDtosAsync(List<FnCustomer> rows, CancellationToken ct)
        {
            List<int> ids = rows.Select(r => r.Id).ToList();
            List<SlCustomerProfile> profiles = await _uow.Set<SlCustomerProfile>().AsNoTracking().Where(p => ids.Contains(p.CustomerId) && !p.Deleted).ToListAsync(ct);
            var contacts = await _uow.Set<SlContact>().AsNoTracking().Where(c => ids.Contains(c.CustomerId) && !c.Deleted).GroupBy(c => c.CustomerId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
            var sites = await _uow.Set<SlSite>().AsNoTracking().Where(c => ids.Contains(c.CustomerId) && !c.Deleted).GroupBy(c => c.CustomerId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, profiles.Select(p => p.OwnerUserId), ct);
            // the credit limit is finance's number: the others do not see it
            bool seesCredit = _user.IsInRole(SalesRoles.CreditViewers);
            return rows.Select(c =>
            {
                SlCustomerProfile p = profiles.FirstOrDefault(x => x.CustomerId == c.Id);
                return new SlCustomerDto
                {
                    Id = c.Id, Name = c.Name, EconomicCode = c.EconomicCode, NationalId = c.NationalId, PostalCode = c.PostalCode, Address = c.Address, Phone = c.Phone,
                    EmployerName = c.EmployerName, IsActive = c.IsActive, ProfileId = p?.Id, Type = p?.Type, Industry = p?.Industry, Country = p?.Country, City = p?.City,
                    Status = p?.Status ?? SlCustomerStatus.Active, OwnerUserId = p?.OwnerUserId, OwnerName = names.Name(p?.OwnerUserId), CreditLimitRial = seesCredit ? p?.CreditLimitRial : null,
                    PaymentTermDays = p?.PaymentTermDays, Note = p?.Note, ContactCount = contacts.FirstOrDefault(x => x.Key == c.Id)?.N ?? 0,
                    SiteCount = sites.FirstOrDefault(x => x.Key == c.Id)?.N ?? 0, RowVersion = SlConcurrency.Text(p?.RowVersion),
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<SlCustomerDto>>> GetCustomersAsync(SlCustomerQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<PagedList<SlCustomerDto>>(SlText.NotAllowed);
            query ??= new SlCustomerQuery();
            IQueryable<FnCustomer> q = _uow.Set<FnCustomer>().AsNoTracking();
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(c => c.Name.Contains(text) || c.EmployerName.Contains(text) || c.NationalId.Contains(text));
            IQueryable<SlCustomerProfile> profiles = _uow.Set<SlCustomerProfile>().AsNoTracking().Where(p => !p.Deleted);
            if (query.Status.HasValue)
            {
                SlCustomerStatus s = query.Status.Value;
                if (s == SlCustomerStatus.Active) q = q.Where(c => !profiles.Any(p => p.CustomerId == c.Id && p.Status != SlCustomerStatus.Active));
                else q = q.Where(c => profiles.Any(p => p.CustomerId == c.Id && p.Status == s));
            }
            if (query.Type.HasValue) { SlCustomerType t = query.Type.Value; q = q.Where(c => profiles.Any(p => p.CustomerId == c.Id && p.Type == t)); }
            if (query.OwnerUserId.HasValue) { int o = query.OwnerUserId.Value; q = q.Where(c => profiles.Any(p => p.CustomerId == c.Id && p.OwnerUserId == o)); }
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderBy(c => c.Name).ThenBy(c => c.Id), query.PageNumber, query.PageSize, rows => CustomerDtosAsync(rows, ct), ct));
        }

        public async Task<ResponseDto<SlCustomerDto>> GetCustomerAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<SlCustomerDto>(SlText.NotAllowed);
            FnCustomer row = await _uow.Set<FnCustomer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
            if (row == null) return SlText.NotFound<SlCustomerDto>("مشتری پیدا نشد.");
            return ResponseDtoHelper.Ok((await CustomerDtosAsync(new List<FnCustomer> { row }, ct))[0]);
        }

        public async Task<ResponseDto<SlCustomerDto>> SaveCustomerAsync(SlSaveCustomerDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.MasterWriters)) return Bad<SlCustomerDto>(SlText.NotAllowed);
            if (dto == null || Clean(dto.Name) == null) return Bad<SlCustomerDto>("نام مشتری لازم است.");
            if (!Enum.IsDefined(typeof(SlCustomerStatus), dto.Status)) return Bad<SlCustomerDto>(SlText.InvalidData);
            if (dto.Type.HasValue && !Enum.IsDefined(typeof(SlCustomerType), dto.Type.Value)) return Bad<SlCustomerDto>(SlText.InvalidData);
            if (dto.Industry.HasValue && !Enum.IsDefined(typeof(SlIndustry), dto.Industry.Value)) return Bad<SlCustomerDto>(SlText.InvalidData);
            if (dto.PaymentTermDays.HasValue && (dto.PaymentTermDays.Value < 0 || dto.PaymentTermDays.Value > 3650)) return Bad<SlCustomerDto>("مهلت پرداخت نامعتبر است.");
            string eco = Clean(dto.EconomicCode) == null ? null : Digits(dto.EconomicCode);
            string nat = Clean(dto.NationalId) == null ? null : Digits(dto.NationalId);
            if (eco != null && eco.Length != 12 && eco.Length != 14) return Bad<SlCustomerDto>("کد اقتصادی باید ۱۲ یا ۱۴ رقم باشد.");
            if (nat != null && nat.Length != 10 && nat.Length != 11) return Bad<SlCustomerDto>("شناسه یا کد ملی نامعتبر است.");
            string postal = Clean(dto.PostalCode) == null ? null : Digits(dto.PostalCode);
            if (postal != null && postal.Length != 10) return Bad<SlCustomerDto>("کد پستی باید ۱۰ رقم باشد.");
            string name = dto.Name.Trim();
            DateTimeOffset now = DateTimeOffset.Now;

            FnCustomer c;
            SlCustomerProfile profile = null;
            if (dto.Id.HasValue)
            {
                c = await _uow.Set<FnCustomer>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (c == null) return SlText.NotFound<SlCustomerDto>("مشتری پیدا نشد.");
                // the identity of a customer with posted invoices is finance's: the name and the codes are on the tax documents
                if ((c.Name != name || c.EconomicCode != eco || c.NationalId != nat) && !_user.IsInRole(SalesRoles.IdentityChangers)
                    && await _uow.Set<FnSalesInvoice>().AnyAsync(i => i.CustomerId == c.Id && i.Status == FnDocStatus.Posted, ct))
                    return Bad<SlCustomerDto>("این مشتری فاکتور ثبت‌شده دارد؛ نام، کد اقتصادی و شناسه ملی او را فقط مالی تغییر می‌دهد.");
                profile = await _uow.Set<SlCustomerProfile>().FirstOrDefaultAsync(p => p.CustomerId == c.Id && !p.Deleted, ct);
                if (profile != null && !SlConcurrency.Apply(_uow, profile, dto.RowVersion)) return Bad<SlCustomerDto>(SlText.InvalidData);
            }
            else
            {
                c = new FnCustomer { CreatedAt = now, CreatedByUserId = Me, IsActive = true };
            }
            int self = c.Id;
            if (await _uow.Set<FnCustomer>().AnyAsync(x => x.Name == name && x.Id != self, ct)) return Bad<SlCustomerDto>("مشتری دیگری با همین نام وجود دارد.");
            if (!dto.Id.HasValue) _uow.Set<FnCustomer>().Add(c);
            c.Name = name; c.EconomicCode = eco; c.NationalId = nat; c.PostalCode = postal; c.Address = Clean(dto.Address); c.Phone = Clean(dto.Phone);
            if (Clean(dto.EmployerName) != null || dto.Id.HasValue) c.EmployerName = Clean(dto.EmployerName);
            c.IsActive = dto.Status != SlCustomerStatus.Inactive;

            if (profile == null)
            {
                profile = new SlCustomerProfile { Customer = c, CreateDate = now, CreatorUserId = Me };
                _uow.Set<SlCustomerProfile>().Add(profile);
            }
            profile.Type = dto.Type; profile.Industry = dto.Industry; profile.Country = Clean(dto.Country); profile.City = Clean(dto.City); profile.Status = dto.Status;
            profile.OwnerUserId = dto.OwnerUserId ?? profile.OwnerUserId ?? Me; profile.PaymentTermDays = dto.PaymentTermDays; profile.Note = Clean(dto.Note);
            profile.ModifyDate = now; profile.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlCustomerDto>(SlText.Concurrency);
            }
            return await GetCustomerAsync(c.Id, ct);
        }

        public async Task<ResponseDto<SlCustomerDto>> SetCreditAsync(SlSetCreditDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.CreditSetters)) return Bad<SlCustomerDto>("سقف اعتبار مشتری را فقط مالی (مدیر مالی) تعیین می‌کند.");
            if (dto == null) return Bad<SlCustomerDto>(SlText.InvalidData);
            if (dto.CreditLimitRial.HasValue && dto.CreditLimitRial.Value < 0) return Bad<SlCustomerDto>("سقف اعتبار نمی‌تواند منفی باشد.");
            FnCustomer c = await _uow.Set<FnCustomer>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.CustomerId, ct);
            if (c == null) return SlText.NotFound<SlCustomerDto>("مشتری پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            SlCustomerProfile profile = await _uow.Set<SlCustomerProfile>().FirstOrDefaultAsync(p => p.CustomerId == c.Id && !p.Deleted, ct);
            if (profile == null)
            {
                profile = new SlCustomerProfile { CustomerId = c.Id, Status = c.IsActive ? SlCustomerStatus.Active : SlCustomerStatus.Inactive, CreateDate = now, CreatorUserId = Me };
                _uow.Set<SlCustomerProfile>().Add(profile);
            }
            profile.CreditLimitRial = dto.CreditLimitRial;
            profile.CreditSetByUserId = Me;
            profile.CreditSetAt = now;
            profile.ModifyDate = now;
            profile.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return await GetCustomerAsync(c.Id, ct);
        }

        // ================================================================================== contacts, sites, activities

        public async Task<ResponseDto<PagedList<SlContactDto>>> GetContactsAsync(SlContactQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<PagedList<SlContactDto>>(SlText.NotAllowed);
            query ??= new SlContactQuery();
            IQueryable<SlContact> q = _uow.Set<SlContact>().AsNoTracking().Where(c => !c.Deleted);
            if (query.CustomerId.HasValue) q = q.Where(c => c.CustomerId == query.CustomerId.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(c => c.FullName.Contains(text) || c.Phone.Contains(text) || c.Email.Contains(text));
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderBy(c => c.CustomerId).ThenByDescending(c => c.IsPrimary).ThenBy(c => c.FullName).ThenBy(c => c.Id), query.PageNumber, query.PageSize, async rows =>
            {
                Dictionary<int, string> customers = await CustomerNamesAsync(rows.Select(r => r.CustomerId), ct);
                return rows.Select(r => ToDto(r, customers)).ToList();
            }, ct));
        }

        private static SlContactDto ToDto(SlContact r, Dictionary<int, string> customers) => new SlContactDto
        {
            Id = r.Id, CustomerId = r.CustomerId, CustomerName = customers.TryGetValue(r.CustomerId, out string n) ? n : null, FullName = r.FullName, JobTitle = r.JobTitle,
            Phone = r.Phone, Email = r.Email, IsPrimary = r.IsPrimary, IsActive = r.IsActive,
        };

        private async Task<Dictionary<int, string>> CustomerNamesAsync(IEnumerable<int> ids, CancellationToken ct)
        {
            List<int> list = ids.Distinct().ToList();
            return list.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<FnCustomer>().AsNoTracking().Where(c => list.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        }

        public async Task<ResponseDto<SlContactDto>> SaveContactAsync(SlSaveContactDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.MasterWriters)) return Bad<SlContactDto>(SlText.NotAllowed);
            if (dto == null || Clean(dto.FullName) == null) return Bad<SlContactDto>("نام مخاطب لازم است.");
            if (!await _uow.Set<FnCustomer>().AnyAsync(c => c.Id == dto.CustomerId, ct)) return Bad<SlContactDto>("مشتری پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            SlContact row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<SlContact>().FirstOrDefaultAsync(c => c.Id == dto.Id.Value && !c.Deleted, ct);
                if (row == null) return SlText.NotFound<SlContactDto>("مخاطب پیدا نشد.");
            }
            else
            {
                row = new SlContact { CreateDate = now, CreatorUserId = Me };
                _uow.Set<SlContact>().Add(row);
            }
            row.CustomerId = dto.CustomerId; row.FullName = dto.FullName.Trim(); row.JobTitle = Clean(dto.JobTitle); row.Phone = Clean(dto.Phone); row.Email = Clean(dto.Email);
            row.IsPrimary = dto.IsPrimary; row.IsActive = dto.IsActive; row.ModifyDate = now; row.ModifierUserId = Me;
            if (dto.IsPrimary)
            {
                // one primary contact per customer
                foreach (SlContact other in await _uow.Set<SlContact>().Where(c => c.CustomerId == dto.CustomerId && c.IsPrimary && !c.Deleted && c.Id != row.Id).ToListAsync(ct))
                    other.IsPrimary = false;
            }
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(row, await CustomerNamesAsync(new[] { row.CustomerId }, ct)));
        }

        public async Task<ResponseDto<PagedList<SlSiteDto>>> GetSitesAsync(SlSiteQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<PagedList<SlSiteDto>>(SlText.NotAllowed);
            query ??= new SlSiteQuery();
            IQueryable<SlSite> q = _uow.Set<SlSite>().AsNoTracking().Where(c => !c.Deleted);
            if (query.CustomerId.HasValue) q = q.Where(c => c.CustomerId == query.CustomerId.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(c => c.Title.Contains(text) || c.Address.Contains(text));
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderBy(c => c.CustomerId).ThenBy(c => c.Title).ThenBy(c => c.Id), query.PageNumber, query.PageSize, async rows =>
            {
                Dictionary<int, string> customers = await CustomerNamesAsync(rows.Select(r => r.CustomerId), ct);
                return rows.Select(r => ToDto(r, customers)).ToList();
            }, ct));
        }

        private static SlSiteDto ToDto(SlSite r, Dictionary<int, string> customers) => new SlSiteDto
        {
            Id = r.Id, CustomerId = r.CustomerId, CustomerName = customers.TryGetValue(r.CustomerId, out string n) ? n : null, Title = r.Title, Address = r.Address,
            Country = r.Country, CoordinatesText = r.CoordinatesText,
        };

        public async Task<ResponseDto<SlSiteDto>> SaveSiteAsync(SlSaveSiteDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.MasterWriters)) return Bad<SlSiteDto>(SlText.NotAllowed);
            if (dto == null || Clean(dto.Title) == null) return Bad<SlSiteDto>("عنوان محل نصب لازم است.");
            if (!await _uow.Set<FnCustomer>().AnyAsync(c => c.Id == dto.CustomerId, ct)) return Bad<SlSiteDto>("مشتری پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            SlSite row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<SlSite>().FirstOrDefaultAsync(c => c.Id == dto.Id.Value && !c.Deleted, ct);
                if (row == null) return SlText.NotFound<SlSiteDto>("محل نصب پیدا نشد.");
            }
            else
            {
                row = new SlSite { CreateDate = now, CreatorUserId = Me };
                _uow.Set<SlSite>().Add(row);
            }
            row.CustomerId = dto.CustomerId; row.Title = dto.Title.Trim(); row.Address = Clean(dto.Address); row.Country = Clean(dto.Country); row.CoordinatesText = Clean(dto.CoordinatesText);
            row.ModifyDate = now; row.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(row, await CustomerNamesAsync(new[] { row.CustomerId }, ct)));
        }

        public async Task<ResponseDto<PagedList<SlActivityDto>>> GetActivitiesAsync(SlActivityQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<PagedList<SlActivityDto>>(SlText.NotAllowed);
            query ??= new SlActivityQuery();
            IQueryable<SlActivity> q = _uow.Set<SlActivity>().AsNoTracking().Where(c => !c.Deleted);
            if (query.CustomerId.HasValue) q = q.Where(c => c.CustomerId == query.CustomerId.Value);
            if (query.InquiryId.HasValue) q = q.Where(c => c.InquiryId == query.InquiryId.Value);
            if (query.ProposalId.HasValue) q = q.Where(c => c.ProposalId == query.ProposalId.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(c => c.Subject.Contains(text) || c.Notes.Contains(text));
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderByDescending(c => c.ActivityDate).ThenByDescending(c => c.Id), query.PageNumber, query.PageSize, async rows =>
            {
                Dictionary<int, string> customers = await CustomerNamesAsync(rows.Select(r => r.CustomerId), ct);
                Dictionary<int, string> users = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => (int?)r.OwnerUserId), ct);
                return rows.Select(r => ToDto(r, customers, users)).ToList();
            }, ct));
        }

        private static SlActivityDto ToDto(SlActivity r, Dictionary<int, string> customers, Dictionary<int, string> users) => new SlActivityDto
        {
            Id = r.Id, CustomerId = r.CustomerId, CustomerName = customers.TryGetValue(r.CustomerId, out string n) ? n : null, ContactId = r.ContactId, Kind = r.Kind,
            ActivityDate = r.ActivityDate, ActivityDateText = SlText.Date(r.ActivityDate), Subject = r.Subject, Notes = r.Notes, NextActionDate = r.NextActionDate,
            NextActionDateText = SlText.Date(r.NextActionDate), InquiryId = r.InquiryId, ProposalId = r.ProposalId, ServiceRequestId = r.ServiceRequestId, OwnerUserId = r.OwnerUserId,
            OwnerName = users.Name(r.OwnerUserId),
        };

        public async Task<ResponseDto<SlActivityDto>> SaveActivityAsync(SlSaveActivityDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.ActivityWriters)) return Bad<SlActivityDto>(SlText.NotAllowed);
            if (dto == null || Clean(dto.Subject) == null) return Bad<SlActivityDto>("موضوع فعالیت لازم است.");
            if (!Enum.IsDefined(typeof(SlActivityKind), dto.Kind)) return Bad<SlActivityDto>(SlText.InvalidData);
            if (!await _uow.Set<FnCustomer>().AnyAsync(c => c.Id == dto.CustomerId, ct)) return Bad<SlActivityDto>("مشتری پیدا نشد.");
            if (dto.ContactId.HasValue && !await _uow.Set<SlContact>().AnyAsync(c => c.Id == dto.ContactId.Value && c.CustomerId == dto.CustomerId && !c.Deleted, ct)) return Bad<SlActivityDto>("مخاطب انتخاب‌شده از این مشتری نیست.");
            if (dto.InquiryId.HasValue && !await _uow.Set<SlInquiry>().AnyAsync(c => c.Id == dto.InquiryId.Value && !c.Deleted, ct)) return Bad<SlActivityDto>("استعلام پیدا نشد.");
            if (dto.ProposalId.HasValue && !await _uow.Set<SlProposal>().AnyAsync(c => c.Id == dto.ProposalId.Value && !c.Deleted, ct)) return Bad<SlActivityDto>("پیشنهاد پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            SlActivity row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<SlActivity>().FirstOrDefaultAsync(c => c.Id == dto.Id.Value && !c.Deleted, ct);
                if (row == null) return SlText.NotFound<SlActivityDto>("فعالیت پیدا نشد.");
                if (row.OwnerUserId != Me && !_user.IsInRole("SALES")) return Bad<SlActivityDto>(SlText.NotAllowed);
            }
            else
            {
                row = new SlActivity { CreateDate = now, CreatorUserId = Me, OwnerUserId = Me };
                _uow.Set<SlActivity>().Add(row);
            }
            row.CustomerId = dto.CustomerId; row.ContactId = dto.ContactId; row.Kind = dto.Kind; row.ActivityDate = (dto.ActivityDate ?? DateTime.Today).Date; row.Subject = dto.Subject.Trim();
            row.Notes = Clean(dto.Notes); row.NextActionDate = dto.NextActionDate?.Date; row.InquiryId = dto.InquiryId; row.ProposalId = dto.ProposalId; row.ServiceRequestId = dto.ServiceRequestId;
            row.ModifyDate = now; row.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(row, await CustomerNamesAsync(new[] { row.CustomerId }, ct), await InventoryUsers.GetNamesAsync(_uow, new int?[] { row.OwnerUserId }, ct)));
        }

        // ================================================================================== legacy link

        /// <summary>
        /// One-off linker: the free text "Employer" of a legacy contract (and "Company" of a tender) is compared, after normalising letters and digits,
        /// with the employer name or name of the customers. Only an exact match on exactly one customer links; the rest is listed for a person.
        /// Running it again changes nothing that is already linked.
        /// </summary>
        public async Task<ResponseDto<SlLinkLegacyResultDto>> LinkLegacyAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.LegacyLinkers)) return Bad<SlLinkLegacyResultDto>(SlText.NotAllowed);
            var keys = new Dictionary<string, HashSet<int>>();
            foreach (var c in await _uow.Set<FnCustomer>().AsNoTracking().Select(c => new { c.Id, c.Name, c.EmployerName }).ToListAsync(ct))
            {
                foreach (string text in new[] { c.Name, c.EmployerName })
                {
                    string key = SlText.Normalize(text);
                    if (key == null) continue;
                    if (!keys.TryGetValue(key, out HashSet<int> set)) keys[key] = set = new HashSet<int>();
                    set.Add(c.Id);
                }
            }
            var existing = await _uow.Set<SlCustomerLink>().AsNoTracking().Where(l => !l.Deleted).Select(l => new { l.ContractId, l.TenderId }).ToListAsync(ct);
            var linked = new HashSet<(SlLinkSource, int)>(existing.Select(l => l.ContractId.HasValue ? (SlLinkSource.Contract, l.ContractId.Value) : (SlLinkSource.Tender, l.TenderId.Value)));
            var result = new SlLinkLegacyResultDto { AlreadyLinked = existing.Count };
            DateTimeOffset now = DateTimeOffset.Now;

            void Consider(SlLinkSource type, int id, string text)
            {
                if (linked.Contains((type, id))) return;
                string key = SlText.Normalize(text);
                if (key == null) return;
                if (keys.TryGetValue(key, out HashSet<int> set) && set.Count == 1)
                {
                    _uow.Set<SlCustomerLink>().Add(new SlCustomerLink
                    {
                        ContractId = type == SlLinkSource.Contract ? id : (int?)null, TenderId = type == SlLinkSource.Tender ? id : (int?)null, CustomerId = set.First(),
                        Method = SlLinkMethod.LegacyMatch, LinkedByUserId = Me, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
                    });
                    linked.Add((type, id));
                    if (type == SlLinkSource.Contract) result.LinkedContracts++; else result.LinkedTenders++;
                }
                else result.Unmatched.Add(new SlLinkSourceDto { SourceType = type, SourceId = id, Text = text });
            }

            foreach (var c in await _uow.Set<ContractEntity>().AsNoTracking().Where(c => !c.Deleted).OrderBy(c => c.Id).Select(c => new { c.Id, c.Employer }).ToListAsync(ct))
                Consider(SlLinkSource.Contract, c.Id, c.Employer);
            foreach (var t in await _uow.Set<Tender>().AsNoTracking().Where(t => !t.Deleted).OrderBy(t => t.Id).Select(t => new { t.Id, t.Company }).ToListAsync(ct))
                Consider(SlLinkSource.Tender, t.Id, t.Company);
            if (result.LinkedContracts + result.LinkedTenders > 0) await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<List<SlLinkSourceDto>>> GetUnlinkedAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<List<SlLinkSourceDto>>(SlText.NotAllowed);
            var linked = await _uow.Set<SlCustomerLink>().AsNoTracking().Where(l => !l.Deleted).Select(l => new { l.ContractId, l.TenderId }).ToListAsync(ct);
            var set = new HashSet<(SlLinkSource, int)>(linked.Select(l => l.ContractId.HasValue ? (SlLinkSource.Contract, l.ContractId.Value) : (SlLinkSource.Tender, l.TenderId.Value)));
            var rows = new List<SlLinkSourceDto>();
            foreach (var c in await _uow.Set<ContractEntity>().AsNoTracking().Where(c => !c.Deleted).OrderBy(c => c.Id).Select(c => new { c.Id, c.Employer }).ToListAsync(ct))
                if (!set.Contains((SlLinkSource.Contract, c.Id)) && SlText.Normalize(c.Employer) != null) rows.Add(new SlLinkSourceDto { SourceType = SlLinkSource.Contract, SourceId = c.Id, Text = c.Employer });
            foreach (var t in await _uow.Set<Tender>().AsNoTracking().Where(t => !t.Deleted).OrderBy(t => t.Id).Select(t => new { t.Id, t.Company }).ToListAsync(ct))
                if (!set.Contains((SlLinkSource.Tender, t.Id)) && SlText.Normalize(t.Company) != null) rows.Add(new SlLinkSourceDto { SourceType = SlLinkSource.Tender, SourceId = t.Id, Text = t.Company });
            return ResponseDtoHelper.Ok(rows);
        }

        public async Task<ResponseDto<SlLinkDto>> LinkManualAsync(SlManualLinkDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.LegacyLinkers)) return Bad<SlLinkDto>(SlText.NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(SlLinkSource), dto.SourceType)) return Bad<SlLinkDto>(SlText.InvalidData);
            if (!await _uow.Set<FnCustomer>().AnyAsync(c => c.Id == dto.CustomerId, ct)) return Bad<SlLinkDto>("مشتری پیدا نشد.");
            bool exists = dto.SourceType == SlLinkSource.Contract
                ? await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.SourceId && !c.Deleted, ct)
                : await _uow.Set<Tender>().AnyAsync(c => c.Id == dto.SourceId && !c.Deleted, ct);
            if (!exists) return Bad<SlLinkDto>(dto.SourceType == SlLinkSource.Contract ? "قرارداد پیدا نشد." : "مناقصه پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            bool contract = dto.SourceType == SlLinkSource.Contract;
            SlCustomerLink link = await _uow.Set<SlCustomerLink>().FirstOrDefaultAsync(l => !l.Deleted && (contract ? l.ContractId == dto.SourceId : l.TenderId == dto.SourceId), ct);
            if (link == null)
            {
                link = new SlCustomerLink { ContractId = contract ? dto.SourceId : (int?)null, TenderId = contract ? (int?)null : dto.SourceId, CreateDate = now, CreatorUserId = Me };
                _uow.Set<SlCustomerLink>().Add(link);
            }
            link.CustomerId = dto.CustomerId; link.Method = SlLinkMethod.Manual; link.LinkedByUserId = Me; link.ModifyDate = now; link.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await LinkDtosAsync(new List<SlCustomerLink> { link }, ct))[0]);
        }

        private async Task<List<SlLinkDto>> LinkDtosAsync(List<SlCustomerLink> rows, CancellationToken ct)
        {
            Dictionary<int, string> customers = await CustomerNamesAsync(rows.Select(r => r.CustomerId), ct);
            List<int> contractIds = rows.Where(r => r.SourceType == SlLinkSource.Contract).Select(r => r.SourceId).ToList();
            List<int> tenderIds = rows.Where(r => r.SourceType == SlLinkSource.Tender).Select(r => r.SourceId).ToList();
            Dictionary<int, string> contracts = contractIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<ContractEntity>().AsNoTracking().Where(c => contractIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Employer, ct);
            Dictionary<int, string> tenders = tenderIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<Tender>().AsNoTracking().Where(c => tenderIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Company, ct);
            return rows.Select(r => new SlLinkDto
            {
                Id = r.Id, SourceType = r.SourceType, SourceId = r.SourceId, CustomerId = r.CustomerId, CustomerName = customers.TryGetValue(r.CustomerId, out string n) ? n : null, Method = r.Method,
                SourceText = (r.SourceType == SlLinkSource.Contract ? contracts : tenders).TryGetValue(r.SourceId, out string t) ? t : null,
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<SlLinkDto>>> GetLinksAsync(SlLinkQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<PagedList<SlLinkDto>>(SlText.NotAllowed);
            query ??= new SlLinkQuery();
            IQueryable<SlCustomerLink> q = _uow.Set<SlCustomerLink>().AsNoTracking().Where(l => !l.Deleted);
            if (query.CustomerId.HasValue) q = q.Where(l => l.CustomerId == query.CustomerId.Value);
            if (query.SourceType.HasValue) q = query.SourceType.Value == SlLinkSource.Contract ? q.Where(l => l.ContractId != null) : q.Where(l => l.TenderId != null);
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderBy(l => l.ContractId == null).ThenBy(l => l.ContractId).ThenBy(l => l.TenderId), query.PageNumber, query.PageSize, rows => LinkDtosAsync(rows, ct), ct));
        }

        // ================================================================================== settings and reason codes

        private static readonly (string Key, string Title)[] KnownSettings =
        {
            (SlSettingKeys.ProposalApprovalThreshold, "سقف قیمت پیشنهادی که فروش خودش تأیید می‌کند (ریال، بدون مالیات)؛ بالاتر از آن فقط مدیرعامل تأیید می‌کند"),
        };

        public async Task<ResponseDto<List<SlSettingDto>>> GetSettingsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<List<SlSettingDto>>(SlText.NotAllowed);
            List<SlSetting> rows = await _uow.Set<SlSetting>().AsNoTracking().Where(s => !s.Deleted).ToListAsync(ct);
            return ResponseDtoHelper.Ok(KnownSettings.Select(k =>
            {
                SlSetting row = rows.FirstOrDefault(r => r.Key == k.Key);
                return new SlSettingDto { Key = k.Key, Title = k.Title, Value = row?.Value, Note = row?.Note };
            }).ToList());
        }

        public async Task<ResponseDto<SlSettingDto>> SaveSettingAsync(SlSaveSettingDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.SettingSetters)) return Bad<SlSettingDto>("این مقدار را فقط مدیرعامل تعیین می‌کند.");
            if (dto == null) return Bad<SlSettingDto>(SlText.InvalidData);
            var known = KnownSettings.FirstOrDefault(k => k.Key == dto.Key);
            if (known.Key == null) return Bad<SlSettingDto>("کلید تنظیم شناخته‌شده نیست.");
            if (dto.Value.HasValue && dto.Value.Value < 0) return Bad<SlSettingDto>("مقدار نمی‌تواند منفی باشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            SlSetting row = await _uow.Set<SlSetting>().FirstOrDefaultAsync(s => s.Key == dto.Key && !s.Deleted, ct);
            if (row == null)
            {
                row = new SlSetting { Key = dto.Key, CreateDate = now, CreatorUserId = Me };
                _uow.Set<SlSetting>().Add(row);
            }
            row.Title = known.Title;
            row.Value = dto.Value;
            row.ModifyDate = now;
            row.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new SlSettingDto { Key = row.Key, Title = known.Title, Value = row.Value, Note = row.Note });
        }

        public async Task<ResponseDto<PagedList<SlReasonCodeDto>>> GetReasonCodesAsync(SlReasonCodeQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<PagedList<SlReasonCodeDto>>(SlText.NotAllowed);
            query ??= new SlReasonCodeQuery();
            IQueryable<SlReasonCode> q = _uow.Set<SlReasonCode>().AsNoTracking().Where(r => !r.Deleted);
            if (query.Kind.HasValue) q = q.Where(r => r.Kind == query.Kind.Value);
            if (query.IsActive.HasValue) q = q.Where(r => r.IsActive == query.IsActive.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(r => r.Code.Contains(text) || r.Title.Contains(text));
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderBy(r => r.Kind).ThenBy(r => r.Code), query.PageNumber, query.PageSize,
                rows => Task.FromResult(rows.Select(r => new SlReasonCodeDto { Id = r.Id, Code = r.Code, Title = r.Title, Kind = r.Kind, IsActive = r.IsActive }).ToList()), ct));
        }

        public async Task<ResponseDto<SlReasonCodeDto>> SaveReasonCodeAsync(SlSaveReasonCodeDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.MasterWriters)) return Bad<SlReasonCodeDto>(SlText.NotAllowed);
            string code = Clean(dto?.Code), title = Clean(dto?.Title);
            if (code == null || title == null) return Bad<SlReasonCodeDto>("کد و عنوان دلیل را وارد کنید.");
            if (!Enum.IsDefined(typeof(SlReasonKind), dto.Kind)) return Bad<SlReasonCodeDto>(SlText.InvalidData);
            if (await _uow.Set<SlReasonCode>().AnyAsync(r => r.Code == code && r.Kind == dto.Kind && !r.Deleted && r.Id != (dto.Id ?? 0), ct)) return Bad<SlReasonCodeDto>("این کد قبلاً ثبت شده است.");
            DateTimeOffset now = DateTimeOffset.Now;
            SlReasonCode row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<SlReasonCode>().FirstOrDefaultAsync(r => r.Id == dto.Id.Value && !r.Deleted, ct);
                if (row == null) return SlText.NotFound<SlReasonCodeDto>("دلیل پیدا نشد.");
            }
            else
            {
                row = new SlReasonCode { CreateDate = now, CreatorUserId = Me };
                _uow.Set<SlReasonCode>().Add(row);
            }
            row.Code = code; row.Title = title; row.Kind = dto.Kind; row.IsActive = dto.IsActive; row.ModifyDate = now; row.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new SlReasonCodeDto { Id = row.Id, Code = row.Code, Title = row.Title, Kind = row.Kind, IsActive = row.IsActive });
        }
    }
}
