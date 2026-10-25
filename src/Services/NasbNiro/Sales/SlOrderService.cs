using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.SalesDtos;

namespace Services.NasbNiro.Sales
{
    public interface ISlOrderService
    {
        Task<ResponseDto<PagedList<SlPriceListDto>>> GetPriceListsAsync(SlPriceListQuery query, CancellationToken ct);
        Task<ResponseDto<SlPriceListDto>> GetPriceListAsync(int id, CancellationToken ct);
        Task<ResponseDto<SlPriceListDto>> SavePriceListAsync(SlSavePriceListDto dto, CancellationToken ct);
        Task<ResponseDto<SlPriceListDto>> PriceListActionAsync(SlPriceListActionDto dto, CancellationToken ct);
        Task<ResponseDto<List<SlPriceSuggestionDto>>> SuggestPricesAsync(SlPriceSuggestDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<SlQuotationDto>>> GetQuotationsAsync(SlQuotationQuery query, CancellationToken ct);
        Task<ResponseDto<SlQuotationDto>> GetQuotationAsync(int id, CancellationToken ct);
        Task<ResponseDto<SlQuotationDto>> SaveQuotationAsync(SlSaveQuotationDto dto, CancellationToken ct);
        Task<ResponseDto<SlQuotationDto>> QuotationActionAsync(SlQuotationActionDto dto, CancellationToken ct);
        Task<ResponseDto<SlOrderDto>> QuotationToOrderAsync(SlQuotationToOrderDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<SlOrderDto>>> GetOrdersAsync(SlOrderQuery query, CancellationToken ct);
        Task<ResponseDto<SlOrderDto>> GetOrderAsync(int id, CancellationToken ct);
        Task<ResponseDto<SlOrderDto>> SaveOrderAsync(SlSaveOrderDto dto, CancellationToken ct);
        Task<ResponseDto<SlOrderDto>> OrderActionAsync(SlOrderActionDto dto, CancellationToken ct);
        Task<ResponseDto<SlOrderDto>> CreditApproveAsync(SlCreditApproveDto dto, CancellationToken ct);
        Task<ResponseDto<SlOrderDto>> DraftInvoiceAsync(SlDraftInvoiceDto dto, CancellationToken ct);
    }

    /// <summary>Spare parts and small sales: price lists, quotations, sales orders with the credit-limit gate and reservations. Deliveries and invoices are made by <see cref="SlFulfilment"/> when the warehouse confirms the delivery document.</summary>
    public class SlOrderService : ISlOrderService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public SlOrderService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => SlText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => SlText.Bad<T>(m);

        private async Task<Dictionary<int, InventoryItem>> ItemsAsync(IEnumerable<int> ids, CancellationToken ct)
        {
            List<int> list = ids.Distinct().ToList();
            return list.Count == 0 ? new Dictionary<int, InventoryItem>() : await _uow.Set<InventoryItem>().AsNoTracking().Where(i => list.Contains(i.Id) && !i.Deleted).ToDictionaryAsync(i => i.Id, ct);
        }

        // ================================================================================== price lists

        private async Task<List<SlPriceListDto>> PriceListDtosAsync(List<SlPriceList> rows, CancellationToken ct)
        {
            Dictionary<int, InventoryItem> items = await ItemsAsync(rows.SelectMany(r => r.Lines.Select(l => l.ItemId)), ct);
            return rows.Select(r => new SlPriceListDto
            {
                Id = r.Id, Number = r.Number, Title = r.Title, ValidFrom = r.ValidFrom, ValidFromText = SlText.Date(r.ValidFrom), ValidTo = r.ValidTo, ValidToText = SlText.Date(r.ValidTo), Status = r.Status,
                Note = r.Note, ApprovedByUserId = r.ApprovedByUserId, ApprovedAt = r.ApprovedAt, ApprovedAtText = SlText.Stamp(r.ApprovedAt),
                Lines = r.Lines.OrderBy(l => l.Id).Select(l => new SlPriceListLineDto
                {
                    Id = l.Id, ItemId = l.ItemId, ItemCode = items.TryGetValue(l.ItemId, out InventoryItem i) ? i.Code : null, ItemName = i?.Title, UnitPrice = l.UnitPrice, MinQuantity = l.MinQuantity,
                }).ToList(),
                RowVersion = SlConcurrency.Text(r.RowVersion),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<SlPriceListDto>>> GetPriceListsAsync(SlPriceListQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<PagedList<SlPriceListDto>>(SlText.NotAllowed);
            query ??= new SlPriceListQuery();
            IQueryable<SlPriceList> q = _uow.Set<SlPriceList>().AsNoTracking().Where(p => !p.Deleted);
            if (query.Status.HasValue) q = q.Where(p => p.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(p => p.Number.Contains(text) || p.Title.Contains(text));
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderByDescending(p => p.Id), query.PageNumber, query.PageSize, async rows =>
            {
                List<int> ids = rows.Select(r => r.Id).ToList();
                List<SlPriceList> full = await _uow.Set<SlPriceList>().AsNoTracking().Include(p => p.Lines).Where(p => ids.Contains(p.Id)).ToListAsync(ct);
                return await PriceListDtosAsync(ids.Select(id => full.First(f => f.Id == id)).ToList(), ct);
            }, ct));
        }

        public async Task<ResponseDto<SlPriceListDto>> GetPriceListAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<SlPriceListDto>(SlText.NotAllowed);
            SlPriceList row = await _uow.Set<SlPriceList>().AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id && !p.Deleted, ct);
            if (row == null) return SlText.NotFound<SlPriceListDto>("لیست قیمت پیدا نشد.");
            return ResponseDtoHelper.Ok((await PriceListDtosAsync(new List<SlPriceList> { row }, ct))[0]);
        }

        public async Task<ResponseDto<SlPriceListDto>> SavePriceListAsync(SlSavePriceListDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlPriceListDto>(SlText.NotAllowed);
            if (dto == null || Clean(dto.Title) == null) return Bad<SlPriceListDto>("عنوان لیست قیمت را وارد کنید.");
            DateTime from = (dto.ValidFrom ?? DateTime.Today).Date;
            if (dto.ValidTo.HasValue && dto.ValidTo.Value.Date < from) return Bad<SlPriceListDto>("پایان اعتبار لیست قیمت قبل از شروع آن است.");
            List<SlSavePriceListLineDto> lines = (dto.Lines ?? new List<SlSavePriceListLineDto>()).Where(l => l != null).ToList();
            if (lines.Count == 0) return Bad<SlPriceListDto>("لیست قیمت حداقل یک ردیف لازم دارد.");
            if (lines.Any(l => l.UnitPrice <= 0 || l.MinQuantity < 0)) return Bad<SlPriceListDto>("قیمت هر ردیف باید بیشتر از صفر و حداقل تعداد غیرمنفی باشد.");
            if (lines.GroupBy(l => new { l.ItemId, l.MinQuantity }).Any(g => g.Count() > 1)) return Bad<SlPriceListDto>("یک کالا با یک حداقل تعداد فقط یک بار می‌آید.");
            Dictionary<int, InventoryItem> items = await ItemsAsync(lines.Select(l => l.ItemId), ct);
            if (lines.Any(l => !items.ContainsKey(l.ItemId))) return Bad<SlPriceListDto>("کالای یکی از ردیف‌ها پیدا نشد.");

            DateTimeOffset now = DateTimeOffset.Now;
            SlPriceList row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<SlPriceList>().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == dto.Id.Value && !p.Deleted, ct);
                if (row == null) return SlText.NotFound<SlPriceListDto>("لیست قیمت پیدا نشد.");
                if (row.Status != SlPriceListStatus.Draft) return Bad<SlPriceListDto>("فقط لیست قیمت پیش‌نویس ویرایش می‌شود.");
                if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlPriceListDto>(SlText.InvalidData);
                _uow.RemoveRange(row.Lines.ToList());
                row.Lines.Clear();
            }
            else
            {
                row = new SlPriceList { CreateDate = now, CreatorUserId = Me };
                row.Number = await InventoryNumbers.NextNumberAsync(_uow, SlNumbers.PriceList, "PL", now.LocalDateTime, ct);
                _uow.Set<SlPriceList>().Add(row);
            }
            row.Title = dto.Title.Trim(); row.ValidFrom = from; row.ValidTo = dto.ValidTo?.Date; row.Note = Clean(dto.Note);
            foreach (SlSavePriceListLineDto l in lines) row.Lines.Add(new SlPriceListLine { ItemId = l.ItemId, UnitPrice = l.UnitPrice, MinQuantity = l.MinQuantity });
            row.EditorUserIds = LedgerMath.WithEditor(row.EditorUserIds, Me);
            row.ModifyDate = now; row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlPriceListDto>(SlText.Concurrency);
            }
            return await GetPriceListAsync(row.Id, ct);
        }

        public async Task<ResponseDto<SlPriceListDto>> PriceListActionAsync(SlPriceListActionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SlPriceListDto>(SlText.InvalidData);
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            if (action != "approve" && action != "retire") return Bad<SlPriceListDto>("عمل نامعتبر است (approve یا retire).");
            if (!_user.IsInRole(SalesRoles.PriceListApprovers)) return Bad<SlPriceListDto>(SlText.NotAllowed);
            SlPriceList row = await _uow.Set<SlPriceList>().FirstOrDefaultAsync(p => p.Id == dto.Id && !p.Deleted, ct);
            if (row == null) return SlText.NotFound<SlPriceListDto>("لیست قیمت پیدا نشد.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlPriceListDto>(SlText.InvalidData);
            DateTimeOffset now = DateTimeOffset.Now;
            if (action == "approve")
            {
                if (row.Status != SlPriceListStatus.Draft) return Bad<SlPriceListDto>("فقط لیست قیمت پیش‌نویس تأیید می‌شود.");
                if (LedgerMath.Touched(row.CreatorUserId, row.EditorUserIds, Me)) return Bad<SlPriceListDto>(SlText.SameUser);
                row.Status = SlPriceListStatus.Approved; row.ApprovedByUserId = Me; row.ApprovedAt = now;
            }
            else
            {
                if (row.Status != SlPriceListStatus.Approved) return Bad<SlPriceListDto>("فقط لیست قیمت تأییدشده کنار گذاشته می‌شود.");
                row.Status = SlPriceListStatus.Retired;
            }
            row.ModifyDate = now; row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlPriceListDto>(SlText.Concurrency);
            }
            return await GetPriceListAsync(row.Id, ct);
        }

        /// <summary>Cost plus markup. The cost is the average cost of the stock (else the last purchase price, else the last purchase order); the markup is the percentage the user enters, none is assumed.</summary>
        public async Task<ResponseDto<List<SlPriceSuggestionDto>>> SuggestPricesAsync(SlPriceSuggestDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.CommercialReaders)) return Bad<List<SlPriceSuggestionDto>>(SlText.NotAllowed);
            if (dto == null || dto.ItemIds == null || dto.ItemIds.Count == 0) return Bad<List<SlPriceSuggestionDto>>("حداقل یک کالا انتخاب کنید.");
            if (!dto.MarkupPercent.HasValue) return Bad<List<SlPriceSuggestionDto>>("درصد سود (markup) را وارد کنید؛ سیستم درصدی را فرض نمی‌کند.");
            if (dto.MarkupPercent.Value < 0 || dto.MarkupPercent.Value > 10000) return Bad<List<SlPriceSuggestionDto>>("درصد سود نامعتبر است.");
            Dictionary<int, InventoryItem> items = await ItemsAsync(dto.ItemIds, ct);
            Dictionary<int, (decimal Cost, SlCostSource Source)> costs = await SlCosts.ItemCostsAsync(_uow, items.Keys, true, ct);
            var result = new List<SlPriceSuggestionDto>();
            foreach (int id in dto.ItemIds.Distinct())
            {
                if (!items.TryGetValue(id, out InventoryItem item)) continue;
                var row = new SlPriceSuggestionDto { ItemId = id, ItemCode = item.Code, ItemName = item.Title };
                if (costs.TryGetValue(id, out var c))
                {
                    row.UnitCost = c.Cost; row.CostSource = c.Source;
                    row.SuggestedPrice = SlText.R0(c.Cost * (1m + dto.MarkupPercent.Value / 100m));
                }
                else row.Note = "هزینه‌ای برای این کالا در دفاتر ثبت نشده است.";
                result.Add(row);
            }
            return ResponseDtoHelper.Ok(result);
        }

        // ================================================================================== shared line pricing

        private class PricedLine
        {
            public InventoryItem Item;
            public SlSaveDocLineDto Source;
            public decimal UnitPrice;
            public decimal Amount;
        }

        /// <summary>Validates the lines and prices them: the price typed by the user, else the approved price list.</summary>
        private async Task<(List<PricedLine> Lines, string Error)> PriceLinesAsync(List<SlSaveDocLineDto> input, DateTime date, int? priceListId, CancellationToken ct)
        {
            List<SlSaveDocLineDto> lines = (input ?? new List<SlSaveDocLineDto>()).Where(l => l != null).ToList();
            if (lines.Count == 0) return (null, "حداقل یک ردیف لازم است.");
            Dictionary<int, InventoryItem> items = await ItemsAsync(lines.Select(l => l.ItemId), ct);
            List<SlPriceList> lists = new List<SlPriceList>();
            if (priceListId.HasValue)
            {
                SlPriceList chosen = await _uow.Set<SlPriceList>().AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == priceListId.Value && !p.Deleted, ct);
                if (chosen == null || chosen.Status != SlPriceListStatus.Approved) return (null, "لیست قیمت انتخاب‌شده پیدا نشد یا تأیید نشده است.");
                lists.Add(chosen);
            }
            else if (lines.Any(l => !l.UnitPrice.HasValue))
            {
                lists = await _uow.Set<SlPriceList>().AsNoTracking().Include(p => p.Lines)
                    .Where(p => !p.Deleted && p.Status == SlPriceListStatus.Approved && p.ValidFrom <= date && (p.ValidTo == null || p.ValidTo >= date)).ToListAsync(ct);
            }
            var result = new List<PricedLine>();
            int n = 0;
            foreach (SlSaveDocLineDto l in lines)
            {
                n++;
                string row = $"ردیف {n}: ";
                if (!items.TryGetValue(l.ItemId, out InventoryItem item)) return (null, row + "کالا پیدا نشد.");
                if (!item.IsActive) return (null, row + $"کالای {item.Code} غیرفعال است.");
                if (l.Quantity <= 0) return (null, row + "تعداد باید بیشتر از صفر باشد.");
                if (l.DiscountPercent < 0 || l.DiscountPercent > 100) return (null, row + "درصد تخفیف باید بین ۰ و ۱۰۰ باشد.");
                decimal? price = l.UnitPrice;
                if (!price.HasValue)
                {
                    price = lists.OrderByDescending(p => p.ValidFrom).ThenByDescending(p => p.Id)
                        .SelectMany(p => p.Lines.Where(x => x.ItemId == l.ItemId && x.MinQuantity <= l.Quantity).OrderByDescending(x => x.MinQuantity).Take(1)).Select(x => (decimal?)x.UnitPrice).FirstOrDefault();
                    if (!price.HasValue) return (null, row + $"برای کالای {item.Code} در لیست قیمت تأییدشده قیمتی نیست؛ قیمت را وارد کنید.");
                }
                if (price.Value <= 0) return (null, row + "قیمت باید بیشتر از صفر باشد.");
                result.Add(new PricedLine { Item = item, Source = l, UnitPrice = price.Value, Amount = SlText.LineAmount(l.Quantity, price.Value, l.DiscountPercent).Amount });
            }
            return (result, null);
        }

        private static List<SlDocLineDto> DocLines(IEnumerable<(int Id, int Row, int ItemId, string Description, decimal Qty, decimal Price, decimal Discount, decimal Amount, decimal Reserved, decimal Delivered, decimal Invoiced)> lines, Dictionary<int, InventoryItem> items) =>
            lines.OrderBy(l => l.Row).Select(l => new SlDocLineDto
            {
                Id = l.Id, RowNumber = l.Row, ItemId = l.ItemId, ItemCode = items.TryGetValue(l.ItemId, out InventoryItem i) ? i.Code : null, ItemName = i?.Title, Unit = i?.Unit, Description = l.Description,
                Quantity = l.Qty, UnitPrice = l.Price, DiscountPercent = l.Discount, Amount = l.Amount, ReservedQuantity = l.Reserved, DeliveredQuantity = l.Delivered, InvoicedQuantity = l.Invoiced,
            }).ToList();

        // ================================================================================== quotations

        private async Task<List<SlQuotationDto>> QuotationDtosAsync(List<SlQuotation> rows, CancellationToken ct)
        {
            Dictionary<int, InventoryItem> items = await ItemsAsync(rows.SelectMany(r => r.Lines.Select(l => l.ItemId)), ct);
            List<int> customerIds = rows.Select(r => r.CustomerId).Distinct().ToList();
            Dictionary<int, string> customers = await _uow.Set<FnCustomer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            return rows.Select(r => new SlQuotationDto
            {
                Id = r.Id, Number = r.Number, QuotationDate = r.QuotationDate, QuotationDateText = SlText.Date(r.QuotationDate), CustomerId = r.CustomerId,
                CustomerName = customers.TryGetValue(r.CustomerId, out string n) ? n : null, ContactId = r.ContactId, ValidUntil = r.ValidUntil, ValidUntilText = SlText.Date(r.ValidUntil),
                Status = r.Status, PriceListId = r.PriceListId, Note = r.Note, NetAmount = r.NetAmount, SalesOrderId = r.SalesOrderId,
                Lines = DocLines(r.Lines.Select(l => (l.Id, l.RowNumber, l.ItemId, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Amount, 0m, 0m, 0m)), items),
                RowVersion = SlConcurrency.Text(r.RowVersion),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<SlQuotationDto>>> GetQuotationsAsync(SlQuotationQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<PagedList<SlQuotationDto>>(SlText.NotAllowed);
            query ??= new SlQuotationQuery();
            IQueryable<SlQuotation> q = _uow.Set<SlQuotation>().AsNoTracking().Where(x => !x.Deleted);
            if (query.Status.HasValue) q = q.Where(x => x.Status == query.Status.Value);
            if (query.CustomerId.HasValue) q = q.Where(x => x.CustomerId == query.CustomerId.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(x => x.Number.Contains(text));
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderByDescending(x => x.Id), query.PageNumber, query.PageSize, async rows =>
            {
                List<int> ids = rows.Select(r => r.Id).ToList();
                List<SlQuotation> full = await _uow.Set<SlQuotation>().AsNoTracking().Include(x => x.Lines).Where(x => ids.Contains(x.Id)).ToListAsync(ct);
                return await QuotationDtosAsync(ids.Select(id => full.First(f => f.Id == id)).ToList(), ct);
            }, ct));
        }

        public async Task<ResponseDto<SlQuotationDto>> GetQuotationAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<SlQuotationDto>(SlText.NotAllowed);
            SlQuotation row = await _uow.Set<SlQuotation>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (row == null) return SlText.NotFound<SlQuotationDto>("پیشنهاد قیمت پیدا نشد.");
            return ResponseDtoHelper.Ok((await QuotationDtosAsync(new List<SlQuotation> { row }, ct))[0]);
        }

        private async Task<string> CheckCustomerAsync(int customerId, int? contactId, CancellationToken ct)
        {
            FnCustomer customer = await _uow.Set<FnCustomer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct);
            if (customer == null) return "مشتری پیدا نشد.";
            if (!customer.IsActive) return "مشتری غیرفعال است.";
            if (contactId.HasValue && !await _uow.Set<SlContact>().AnyAsync(c => c.Id == contactId.Value && c.CustomerId == customerId && !c.Deleted, ct)) return "مخاطب انتخاب‌شده از این مشتری نیست.";
            return null;
        }

        public async Task<ResponseDto<SlQuotationDto>> SaveQuotationAsync(SlSaveQuotationDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlQuotationDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SlQuotationDto>(SlText.InvalidData);
            DateTime date = (dto.QuotationDate ?? DateTime.Today).Date;
            if (!dto.ValidUntil.HasValue) return Bad<SlQuotationDto>("تاریخ پایان اعتبار پیشنهاد قیمت را وارد کنید.");
            if (dto.ValidUntil.Value.Date < date) return Bad<SlQuotationDto>("پایان اعتبار قبل از تاریخ پیشنهاد قیمت است.");
            string customerProblem = await CheckCustomerAsync(dto.CustomerId, dto.ContactId, ct);
            if (customerProblem != null) return Bad<SlQuotationDto>(customerProblem);
            var (priced, error) = await PriceLinesAsync(dto.Lines, date, dto.PriceListId, ct);
            if (error != null) return Bad<SlQuotationDto>(error);

            DateTimeOffset now = DateTimeOffset.Now;
            SlQuotation row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<SlQuotation>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (row == null) return SlText.NotFound<SlQuotationDto>("پیشنهاد قیمت پیدا نشد.");
                if (row.Status != SlQuotationStatus.Draft) return Bad<SlQuotationDto>("فقط پیشنهاد قیمت پیش‌نویس ویرایش می‌شود.");
                if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlQuotationDto>(SlText.InvalidData);
                _uow.RemoveRange(row.Lines.ToList());
                row.Lines.Clear();
            }
            else
            {
                row = new SlQuotation { CreateDate = now, CreatorUserId = Me };
                row.Number = await InventoryNumbers.NextNumberAsync(_uow, SlNumbers.Quotation, "QUO", date, ct);
                _uow.Set<SlQuotation>().Add(row);
            }
            row.QuotationDate = date; row.CustomerId = dto.CustomerId; row.ContactId = dto.ContactId; row.ValidUntil = dto.ValidUntil.Value.Date; row.PriceListId = dto.PriceListId; row.Note = Clean(dto.Note);
            int n = 0;
            foreach (PricedLine l in priced)
                row.Lines.Add(new SlQuotationLine { RowNumber = ++n, ItemId = l.Item.Id, Description = Clean(l.Source.Description), Quantity = l.Source.Quantity, UnitPrice = l.UnitPrice, DiscountPercent = l.Source.DiscountPercent, Amount = l.Amount });
            row.NetAmount = row.Lines.Sum(l => l.Amount);
            row.ModifyDate = now; row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlQuotationDto>(SlText.Concurrency);
            }
            return await GetQuotationAsync(row.Id, ct);
        }

        public async Task<ResponseDto<SlQuotationDto>> QuotationActionAsync(SlQuotationActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlQuotationDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SlQuotationDto>(SlText.InvalidData);
            SlQuotation row = await _uow.Set<SlQuotation>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (row == null) return SlText.NotFound<SlQuotationDto>("پیشنهاد قیمت پیدا نشد.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlQuotationDto>(SlText.InvalidData);
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            switch (action)
            {
                case "send":
                    if (row.Status != SlQuotationStatus.Draft) return Bad<SlQuotationDto>("فقط پیشنهاد قیمت پیش‌نویس ارسال می‌شود.");
                    if (row.ValidUntil < DateTime.Today) return Bad<SlQuotationDto>("اعتبار این پیشنهاد قیمت گذشته است.");
                    row.Status = SlQuotationStatus.Sent;
                    break;
                case "accept":
                    if (row.Status != SlQuotationStatus.Sent) return Bad<SlQuotationDto>("فقط پیشنهاد قیمت ارسال‌شده پذیرفته می‌شود.");
                    if (row.ValidUntil < DateTime.Today) return Bad<SlQuotationDto>("اعتبار این پیشنهاد قیمت گذشته است.");
                    row.Status = SlQuotationStatus.Accepted;
                    break;
                case "reject":
                    if (row.Status != SlQuotationStatus.Sent) return Bad<SlQuotationDto>("فقط پیشنهاد قیمت ارسال‌شده رد می‌شود.");
                    row.Status = SlQuotationStatus.Rejected;
                    break;
                case "expire":
                    if (row.Status != SlQuotationStatus.Sent || row.ValidUntil >= DateTime.Today) return Bad<SlQuotationDto>("فقط پیشنهاد قیمت ارسال‌شده‌ای که اعتبارش تمام شده منقضی می‌شود.");
                    row.Status = SlQuotationStatus.Expired;
                    break;
                case "cancel":
                    if (row.Status != SlQuotationStatus.Draft && row.Status != SlQuotationStatus.Sent) return Bad<SlQuotationDto>("این پیشنهاد قیمت لغو نمی‌شود.");
                    row.Status = SlQuotationStatus.Cancelled;
                    break;
                default:
                    return Bad<SlQuotationDto>("عمل نامعتبر است (send، accept، reject، expire یا cancel).");
            }
            row.ModifyDate = DateTimeOffset.Now;
            row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlQuotationDto>(SlText.Concurrency);
            }
            return await GetQuotationAsync(row.Id, ct);
        }

        public async Task<ResponseDto<SlOrderDto>> QuotationToOrderAsync(SlQuotationToOrderDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlOrderDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SlOrderDto>(SlText.InvalidData);
            SlQuotation q = await _uow.Set<SlQuotation>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.QuotationId && !x.Deleted, ct);
            if (q == null) return SlText.NotFound<SlOrderDto>("پیشنهاد قیمت پیدا نشد.");
            if (q.Status != SlQuotationStatus.Accepted) return Bad<SlOrderDto>("فقط از پیشنهاد قیمت پذیرفته‌شده سفارش فروش ساخته می‌شود.");
            if (q.SalesOrderId.HasValue) return Bad<SlOrderDto>("برای این پیشنهاد قیمت قبلاً سفارش فروش ساخته شده است.");
            if (q.ValidUntil < DateTime.Today) return Bad<SlOrderDto>("اعتبار این پیشنهاد قیمت گذشته است.");
            string customerProblem = await CheckCustomerAsync(q.CustomerId, q.ContactId, ct);
            if (customerProblem != null) return Bad<SlOrderDto>(customerProblem);
            DateTimeOffset now = DateTimeOffset.Now;
            var order = new SlSalesOrder
            {
                OrderDate = DateTime.Today, CustomerId = q.CustomerId, ContactId = q.ContactId, QuotationId = q.Id, CustomerOrderNumber = Clean(dto.CustomerOrderNumber),
                RequestedDeliveryDate = dto.RequestedDeliveryDate?.Date, Status = SlOrderStatus.Draft, NetAmount = q.NetAmount, EditorUserIds = LedgerMath.WithEditor(null, Me),
                CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
            };
            order.Number = await InventoryNumbers.NextNumberAsync(_uow, SlNumbers.SalesOrder, "SO", DateTime.Today, ct);
            foreach (SlQuotationLine l in q.Lines.OrderBy(l => l.RowNumber))
                order.Lines.Add(new SlSalesOrderLine { RowNumber = l.RowNumber, ItemId = l.ItemId, Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice, DiscountPercent = l.DiscountPercent, Amount = l.Amount });
            _uow.Set<SlSalesOrder>().Add(order);
            q.ModifyDate = now; q.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
                // the key of the new order is known only now
                q.SalesOrderId = order.Id;
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlOrderDto>(SlText.Concurrency);
            }
            return await GetOrderAsync(order.Id, ct);
        }

        // ================================================================================== sales orders

        private async Task<List<SlOrderDto>> OrderDtosAsync(List<SlSalesOrder> rows, CancellationToken ct)
        {
            Dictionary<int, InventoryItem> items = await ItemsAsync(rows.SelectMany(r => r.Lines.Select(l => l.ItemId)), ct);
            List<int> customerIds = rows.Select(r => r.CustomerId).Distinct().ToList();
            Dictionary<int, string> customers = await _uow.Set<FnCustomer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            List<int> orderIds = rows.Select(r => r.Id).ToList();
            List<SlOrderDelivery> deliveries = await _uow.Set<SlOrderDelivery>().AsNoTracking().Where(d => orderIds.Contains(d.SalesOrderId) && !d.Deleted).OrderBy(d => d.Id).ToListAsync(ct);
            List<int> docIds = deliveries.Select(d => d.DocumentId).ToList();
            Dictionary<int, string> docNumbers = docIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<InventoryDocument>().AsNoTracking().Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number, ct);
            List<int> invoiceIds = deliveries.Where(d => d.InvoiceId.HasValue).Select(d => d.InvoiceId.Value).ToList();
            Dictionary<int, string> invoiceNumbers = invoiceIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<FnSalesInvoice>().AsNoTracking().Where(d => invoiceIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number, ct);
            return rows.Select(r => new SlOrderDto
            {
                Id = r.Id, Number = r.Number, OrderDate = r.OrderDate, OrderDateText = SlText.Date(r.OrderDate), CustomerId = r.CustomerId, CustomerName = customers.TryGetValue(r.CustomerId, out string cn) ? cn : null,
                ContactId = r.ContactId, QuotationId = r.QuotationId, CustomerOrderNumber = r.CustomerOrderNumber, RequestedDeliveryDate = r.RequestedDeliveryDate,
                RequestedDeliveryDateText = SlText.Date(r.RequestedDeliveryDate), Status = r.Status, StatusText = SlText.OrderStatus(r.Status), NetAmount = r.NetAmount,
                CreditApprovalRequired = r.CreditApprovalRequired, CreditReason = r.CreditReason, CreditExcess = r.CreditExcess, CreditApprovedByUserId = r.CreditApprovedByUserId,
                CreditApprovedAt = r.CreditApprovedAt, CreditApprovedAtText = SlText.Stamp(r.CreditApprovedAt), CreditApprovedNetAmount = r.CreditApprovedNetAmount, CreditApprovalNote = r.CreditApprovalNote,
                ConfirmedAt = r.ConfirmedAt, ConfirmedAtText = SlText.Stamp(r.ConfirmedAt), CancelReason = r.CancelReason,
                Lines = DocLines(r.Lines.Select(l => (l.Id, l.RowNumber, l.ItemId, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Amount, l.ReservedQuantity, l.DeliveredQuantity, l.InvoicedQuantity)), items),
                Deliveries = deliveries.Where(d => d.SalesOrderId == r.Id).Select(d => new SlOrderDeliveryDto
                {
                    Id = d.Id, DocumentId = d.DocumentId, DocumentNumber = docNumbers.TryGetValue(d.DocumentId, out string dn) ? dn : null, InvoiceId = d.InvoiceId,
                    InvoiceNumber = d.InvoiceId.HasValue && invoiceNumbers.TryGetValue(d.InvoiceId.Value, out string inn) ? inn : null, DraftNote = d.DraftNote, Reversed = d.Reversed,
                }).ToList(),
                RowVersion = SlConcurrency.Text(r.RowVersion),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<SlOrderDto>>> GetOrdersAsync(SlOrderQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<PagedList<SlOrderDto>>(SlText.NotAllowed);
            query ??= new SlOrderQuery();
            IQueryable<SlSalesOrder> q = _uow.Set<SlSalesOrder>().AsNoTracking().Where(x => !x.Deleted);
            if (query.Status.HasValue) q = q.Where(x => x.Status == query.Status.Value);
            if (query.CustomerId.HasValue) q = q.Where(x => x.CustomerId == query.CustomerId.Value);
            if (query.CreditPending == true) q = q.Where(x => x.Status == SlOrderStatus.Draft && x.CreditApprovalRequired && x.CreditApprovedNetAmount != x.NetAmount);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(x => x.Number.Contains(text) || x.CustomerOrderNumber.Contains(text));
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderByDescending(x => x.Id), query.PageNumber, query.PageSize, async rows =>
            {
                List<int> ids = rows.Select(r => r.Id).ToList();
                List<SlSalesOrder> full = await _uow.Set<SlSalesOrder>().AsNoTracking().Include(x => x.Lines).Where(x => ids.Contains(x.Id)).ToListAsync(ct);
                return await OrderDtosAsync(ids.Select(id => full.First(f => f.Id == id)).ToList(), ct);
            }, ct));
        }

        public async Task<ResponseDto<SlOrderDto>> GetOrderAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<SlOrderDto>(SlText.NotAllowed);
            SlSalesOrder row = await _uow.Set<SlSalesOrder>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (row == null) return SlText.NotFound<SlOrderDto>("سفارش فروش پیدا نشد.");
            return ResponseDtoHelper.Ok((await OrderDtosAsync(new List<SlSalesOrder> { row }, ct))[0]);
        }

        public async Task<ResponseDto<SlOrderDto>> SaveOrderAsync(SlSaveOrderDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlOrderDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SlOrderDto>(SlText.InvalidData);
            DateTime date = (dto.OrderDate ?? DateTime.Today).Date;
            string customerProblem = await CheckCustomerAsync(dto.CustomerId, dto.ContactId, ct);
            if (customerProblem != null) return Bad<SlOrderDto>(customerProblem);
            var (priced, error) = await PriceLinesAsync(dto.Lines, date, null, ct);
            if (error != null) return Bad<SlOrderDto>(error);

            DateTimeOffset now = DateTimeOffset.Now;
            SlSalesOrder row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<SlSalesOrder>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (row == null) return SlText.NotFound<SlOrderDto>("سفارش فروش پیدا نشد.");
                if (row.Status != SlOrderStatus.Draft) return Bad<SlOrderDto>("فقط سفارش فروش پیش‌نویس ویرایش می‌شود.");
                if (row.QuotationId.HasValue && row.CustomerId != dto.CustomerId) return Bad<SlOrderDto>("مشتری سفارشی که از پیشنهاد قیمت ساخته شده تغییر نمی‌کند.");
                if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlOrderDto>(SlText.InvalidData);
                _uow.RemoveRange(row.Lines.ToList());
                row.Lines.Clear();
            }
            else
            {
                row = new SlSalesOrder { CreateDate = now, CreatorUserId = Me };
                row.Number = await InventoryNumbers.NextNumberAsync(_uow, SlNumbers.SalesOrder, "SO", date, ct);
                _uow.Set<SlSalesOrder>().Add(row);
            }
            row.OrderDate = date; row.CustomerId = dto.CustomerId; row.ContactId = dto.ContactId; row.CustomerOrderNumber = Clean(dto.CustomerOrderNumber); row.RequestedDeliveryDate = dto.RequestedDeliveryDate?.Date;
            int n = 0;
            foreach (PricedLine l in priced)
                row.Lines.Add(new SlSalesOrderLine { RowNumber = ++n, ItemId = l.Item.Id, Description = Clean(l.Source.Description), Quantity = l.Source.Quantity, UnitPrice = l.UnitPrice, DiscountPercent = l.Source.DiscountPercent, Amount = l.Amount });
            row.NetAmount = row.Lines.Sum(l => l.Amount);
            // an edit takes back the credit finding: the order is tested again when it is confirmed
            row.CreditApprovalRequired = false; row.CreditReason = null; row.CreditExcess = null;
            row.EditorUserIds = LedgerMath.WithEditor(row.EditorUserIds, Me);
            row.ModifyDate = now; row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlOrderDto>(SlText.Concurrency);
            }
            await SlGates.CreditAsync(_notify, row, ct);
            return await GetOrderAsync(row.Id, ct);
        }

        private static bool CreditApproved(SlSalesOrder o) => o.CreditApprovedByUserId.HasValue && o.CreditApprovedNetAmount.HasValue && o.CreditApprovedNetAmount.Value == o.NetAmount;

        public async Task<ResponseDto<SlOrderDto>> OrderActionAsync(SlOrderActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlOrderDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SlOrderDto>(SlText.InvalidData);
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            if (action != "confirm" && action != "cancel" && action != "close") return Bad<SlOrderDto>("عمل نامعتبر است (confirm، cancel یا close).");
            SlSalesOrder row = await _uow.Set<SlSalesOrder>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (row == null) return SlText.NotFound<SlOrderDto>("سفارش فروش پیدا نشد.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlOrderDto>(SlText.InvalidData);
            DateTimeOffset now = DateTimeOffset.Now;
            string reason = Clean(dto.Reason);

            if (action == "confirm")
            {
                if (row.Status != SlOrderStatus.Draft) return Bad<SlOrderDto>("فقط سفارش فروش پیش‌نویس تأیید می‌شود.");
                if (row.Lines.Count == 0) return Bad<SlOrderDto>("سفارش فروش ردیف ندارد.");
                string customerProblem = await CheckCustomerAsync(row.CustomerId, row.ContactId, ct);
                if (customerProblem != null) return Bad<SlOrderDto>(customerProblem);
                Dictionary<int, InventoryItem> items = await ItemsAsync(row.Lines.Select(l => l.ItemId), ct);
                if (row.Lines.Any(l => !items.TryGetValue(l.ItemId, out InventoryItem i) || !i.IsActive)) return Bad<SlOrderDto>("یکی از کالاهای سفارش پیدا نشد یا غیرفعال است.");
                if (!CreditApproved(row))
                {
                    decimal? vat = await TaxRates.RateAtAsync(_uow, FnTaxKind.Vat, DateTime.Today, ct);
                    if (!vat.HasValue) return Bad<SlOrderDto>("نرخ مالیات بر ارزش افزوده در جدول نرخ‌ها ثبت نشده است؛ بدون آن سقف اعتبار مشتری سنجیده نمی‌شود.");
                    decimal? limit = await _uow.Set<SlCustomerProfile>().AsNoTracking().Where(p => p.CustomerId == row.CustomerId && !p.Deleted).Select(p => p.CreditLimitRial).FirstOrDefaultAsync(ct);
                    decimal open = await SlReceivables.OpenAsync(_uow, row.CustomerId, ct);
                    decimal orderAmount = row.NetAmount + SlText.R0(row.NetAmount * vat.Value / 100m);
                    SlCredit.Result decision = SlCredit.Evaluate(limit, open, orderAmount);
                    if (decision.NeedsApproval)
                    {
                        row.CreditApprovalRequired = true; row.CreditReason = decision.Reason; row.CreditExcess = decision.Excess;
                        row.ModifyDate = now; row.ModifierUserId = Me;
                        try
                        {
                            await _uow.SaveChangesAsync(ct);
                        }
                        catch (DbUpdateConcurrencyException)
                        {
                            return Bad<SlOrderDto>(SlText.Concurrency);
                        }
                        await SlGates.CreditAsync(_notify, row, ct);
                        return Bad<SlOrderDto>(decision.Reason);
                    }
                    row.CreditApprovalRequired = false; row.CreditReason = null; row.CreditExcess = null;
                }
                row.Status = SlOrderStatus.Confirmed; row.ConfirmedAt = now; row.ConfirmedByUserId = Me;
                // stock is held for the customer: the plant's availability checks subtract it
                foreach (SlSalesOrderLine l in row.Lines) l.ReservedQuantity = l.Quantity - l.DeliveredQuantity;
            }
            else if (action == "cancel")
            {
                if (reason == null) return Bad<SlOrderDto>("دلیل لغو سفارش را بنویسید.");
                if (row.Status != SlOrderStatus.Draft && row.Status != SlOrderStatus.Confirmed) return Bad<SlOrderDto>($"سفارش در وضعیت «{SlText.OrderStatus(row.Status)}» لغو نمی‌شود.");
                if (await _uow.Set<SlOrderDelivery>().AnyAsync(d => d.SalesOrderId == row.Id && !d.Reversed && !d.Deleted, ct)) return Bad<SlOrderDto>("برای این سفارش تحویل ثبت شده است؛ ابتدا سند تحویل را باطل کنید.");
                row.Status = SlOrderStatus.Cancelled; row.CancelReason = reason;
                foreach (SlSalesOrderLine l in row.Lines) l.ReservedQuantity = 0;
            }
            else
            {
                if (row.Status != SlOrderStatus.Delivered && row.Status != SlOrderStatus.Invoiced && row.Status != SlOrderStatus.PartlyDelivered) return Bad<SlOrderDto>($"سفارش در وضعیت «{SlText.OrderStatus(row.Status)}» بسته نمی‌شود.");
                if (row.Status == SlOrderStatus.PartlyDelivered && reason == null) return Bad<SlOrderDto>("برای بستن سفارش با تحویل ناقص، دلیل را بنویسید.");
                row.Status = SlOrderStatus.Closed; row.CancelReason = reason;
                foreach (SlSalesOrderLine l in row.Lines) l.ReservedQuantity = 0;
            }
            row.ModifyDate = now; row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlOrderDto>(SlText.Concurrency);
            }
            await SlGates.CreditAsync(_notify, row, ct);
            return await GetOrderAsync(row.Id, ct);
        }

        public async Task<ResponseDto<SlOrderDto>> CreditApproveAsync(SlCreditApproveDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.CreditApprovers)) return Bad<SlOrderDto>("عبور از سقف اعتبار را فقط مدیر مالی تأیید می‌کند.");
            if (dto == null) return Bad<SlOrderDto>(SlText.InvalidData);
            SlSalesOrder row = await _uow.Set<SlSalesOrder>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (row == null) return SlText.NotFound<SlOrderDto>("سفارش فروش پیدا نشد.");
            if (row.Status != SlOrderStatus.Draft || !row.CreditApprovalRequired) return Bad<SlOrderDto>("این سفارش منتظر تأیید سقف اعتبار نیست.");
            if (CreditApproved(row)) return Bad<SlOrderDto>("سقف اعتبار این سفارش قبلاً تأیید شده است.");
            if (LedgerMath.Touched(row.CreatorUserId, row.EditorUserIds, Me)) return Bad<SlOrderDto>(SlText.SameUser);
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlOrderDto>(SlText.InvalidData);
            DateTimeOffset now = DateTimeOffset.Now;
            row.CreditApprovedByUserId = Me; row.CreditApprovedAt = now; row.CreditApprovedNetAmount = row.NetAmount; row.CreditApprovalNote = Clean(dto.Note);
            row.ModifyDate = now; row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlOrderDto>(SlText.Concurrency);
            }
            await SlGates.CreditAsync(_notify, row, ct);
            return await GetOrderAsync(row.Id, ct);
        }

        /// <summary>Drafts the invoice of deliveries that have none (the VAT rate was not in the table when the delivery was confirmed).</summary>
        public async Task<ResponseDto<SlOrderDto>> DraftInvoiceAsync(SlDraftInvoiceDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlOrderDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SlOrderDto>(SlText.InvalidData);
            string error = await SlFulfilment.DraftMissingInvoicesAsync(_uow, Me, dto.OrderId, ct);
            if (error != null) return Bad<SlOrderDto>(error);
            await _uow.SaveChangesAsync(ct);
            return await GetOrderAsync(dto.OrderId, ct);
        }
    }
}
