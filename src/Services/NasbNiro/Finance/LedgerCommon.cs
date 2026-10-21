using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Finance
{
    /// <summary>Role names (as written in the JWT) for the finance module. Segregation of duties is enforced by the services.</summary>
    public static class FinanceRoles
    {
        public const string Accountants = "FINANCIAL,FINANCEMANAGER";
        public const string Managers = "FINANCEMANAGER";
        public const string TreasuryUsers = "TREASURY,FINANCEMANAGER";
        public const string TreasuryAndAccountants = "TREASURY,FINANCIAL,FINANCEMANAGER";
        public const string Readers = "FINANCIAL,FINANCEMANAGER,TREASURY,CEO,PMO";
        /// <summary>Triggers the system jobs (post events, draft invoices).</summary>
        public const string SystemJobs = "FINANCIAL,FINANCEMANAGER,PMO";
        public const string Creator = "creator";
    }

    /// <summary>Counter kinds in <c>DocumentNumberSequence</c> (the finance module uses 441..480).</summary>
    internal static class FinanceNumbers
    {
        public const int Voucher = 441;
        public const int Receipt = 442;
        public const int Payment = 443;
        public const int SalesInvoice = 444;
        public const int PettyCashSettlement = 445;
        public const int Guarantee = 446;
        public const int Reconciliation = 447;
    }

    internal static class LedgerMath
    {
        public static decimal R(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
        public static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        public static string PersianDate(DateTime date) => Inventory.InventoryText.PersianDate(date);

        public static bool Has(string csv, FnDetailType type)
        {
            if (string.IsNullOrWhiteSpace(csv)) return false;
            return csv.Split(',').Any(x => string.Equals(x.Trim(), type.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Iranian IBAN: "IR" + 24 digits, ISO 7064 mod 97-10.</summary>
        public static bool IbanValid(string iban)
        {
            if (string.IsNullOrWhiteSpace(iban)) return false;
            string s = iban.Replace(" ", "").ToUpperInvariant();
            if (s.Length != 26 || !s.StartsWith("IR") || !s.Skip(2).All(char.IsDigit)) return false;
            string rearranged = s.Substring(4) + s.Substring(0, 4);
            var digits = new System.Text.StringBuilder();
            foreach (char c in rearranged)
            {
                if (char.IsDigit(c)) digits.Append(c);
                else digits.Append((c - 'A' + 10).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            int remainder = 0;
            foreach (char c in digits.ToString()) remainder = (remainder * 10 + (c - '0')) % 97;
            return remainder == 1;
        }

        /// <summary>Normalises Persian and Arabic digits to ASCII.</summary>
        public static string Digits(string text)
        {
            if (text == null) return null;
            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c >= '۰' && c <= '۹') chars[i] = (char)('0' + (c - '۰'));
                else if (c >= '٠' && c <= '٩') chars[i] = (char)('0' + (c - '٠'));
            }
            return new string(chars);
        }
    }

    /// <summary>Finds or creates detail accounts (تفصیلی) for the entities that appear in postings; humans never create these by hand.</summary>
    internal sealed class DetailResolver
    {
        private readonly IUnitOfWork _uow;
        private readonly Dictionary<string, FnDetailAccount> _cache = new Dictionary<string, FnDetailAccount>();

        public DetailResolver(IUnitOfWork uow)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        }

        private static string Prefix(FnDetailType t)
        {
            switch (t)
            {
                case FnDetailType.Supplier: return "SUP";
                case FnDetailType.Customer: return "CUS";
                case FnDetailType.Employee: return "EMP";
                case FnDetailType.Bank: return "BNK";
                case FnDetailType.CashBox: return "CSH";
                case FnDetailType.PettyCash: return "PTY";
                case FnDetailType.Project: return "PRJ";
                case FnDetailType.WorkOrder: return "WO";
                case FnDetailType.CostCenter: return "CC";
                case FnDetailType.Asset: return "AST";
                default: return "DT";
            }
        }

        private async Task<string> NameAsync(FnDetailType type, int id, CancellationToken ct)
        {
            string name = null;
            switch (type)
            {
                case FnDetailType.Supplier:
                    name = await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().Where(p => p.Id == id).Select(p => p.Name).FirstOrDefaultAsync(ct);
                    break;
                case FnDetailType.Customer:
                    name = await _uow.Set<FnCustomer>().Where(c => c.Id == id).Select(c => c.Name).FirstOrDefaultAsync(ct);
                    break;
                case FnDetailType.Project:
                {
                    var c = await _uow.Set<ContractEntity>().Where(x => x.Id == id).Select(x => new { x.Code, x.Title }).FirstOrDefaultAsync(ct);
                    name = c == null ? null : (c.Code + " " + c.Title).Trim();
                    break;
                }
                case FnDetailType.WorkOrder:
                {
                    var w = await _uow.Set<WorkOrder>().Where(x => x.Id == id).Select(x => new { x.Code, x.Title }).FirstOrDefaultAsync(ct);
                    name = w == null ? null : (w.Code + " " + w.Title).Trim();
                    break;
                }
                case FnDetailType.CostCenter:
                {
                    var c = await _uow.Set<CostCenter>().Where(x => x.Id == id).Select(x => new { x.Code, x.Title }).FirstOrDefaultAsync(ct);
                    name = c == null ? null : (c.Code + " " + c.Title).Trim();
                    break;
                }
                case FnDetailType.Bank:
                    name = await _uow.Set<FnBankAccount>().Where(x => x.Id == id).Select(x => x.Title).FirstOrDefaultAsync(ct);
                    break;
                case FnDetailType.CashBox:
                    name = await _uow.Set<FnCashBox>().Where(x => x.Id == id).Select(x => x.Title).FirstOrDefaultAsync(ct);
                    break;
                case FnDetailType.PettyCash:
                    name = await _uow.Set<FnPettyCash>().Where(x => x.Id == id).Select(x => x.HolderTitle).FirstOrDefaultAsync(ct);
                    break;
            }
            if (string.IsNullOrWhiteSpace(name)) name = Prefix(type) + "-" + id;
            return name.Length > 300 ? name.Substring(0, 300) : name;
        }

        public async Task<FnDetailAccount> GetAsync(FnDetailType type, int entityId, CancellationToken ct)
        {
            string key = ((int)type).ToString() + ":" + entityId;
            if (_cache.TryGetValue(key, out FnDetailAccount cached)) return cached;
            FnDetailAccount found = _uow.Set<FnDetailAccount>().Local.FirstOrDefault(d => d.Type == type && d.LinkedEntityId == entityId)
                ?? await _uow.Set<FnDetailAccount>().FirstOrDefaultAsync(d => d.Type == type && d.LinkedEntityId == entityId, ct);
            if (found == null)
            {
                found = new FnDetailAccount
                {
                    Code = Prefix(type) + "-" + entityId, Title = await NameAsync(type, entityId, ct), Type = type, LinkedEntityId = entityId, IsActive = true,
                };
                _uow.Set<FnDetailAccount>().Add(found);
            }
            _cache[key] = found;
            return found;
        }

        public static FnDetailType? PartyType(string party)
        {
            switch (party)
            {
                case "Customer": return FnDetailType.Customer;
                case "Supplier": return FnDetailType.Supplier;
                case "BankAccount": return FnDetailType.Bank;
                case "CashBox": return FnDetailType.CashBox;
                case "PettyCash": return FnDetailType.PettyCash;
                case "Employee": return FnDetailType.Employee;
                case "Asset": return FnDetailType.Asset;
                default: return null;
            }
        }
    }

    /// <summary>All fiscal years and periods in memory (a company has a handful), with the lookups posting needs.</summary>
    internal sealed class PeriodCache
    {
        public List<FnFiscalYear> Years { get; private set; } = new List<FnFiscalYear>();

        public static async Task<PeriodCache> LoadAsync(IUnitOfWork uow, CancellationToken ct)
        {
            var c = new PeriodCache();
            c.Years = await uow.Set<FnFiscalYear>().Include(y => y.Periods).OrderBy(y => y.Year).ToListAsync(ct);
            return c;
        }

        public void Add(FnFiscalYear year)
        {
            if (!Years.Any(y => y.Id == year.Id)) Years.Add(year);
        }

        public FnPeriod Find(DateTime date)
        {
            DateTime d = date.Date;
            foreach (FnFiscalYear y in Years)
                foreach (FnPeriod p in y.Periods)
                    if (p.StartDate <= d && d <= p.EndDate) return p;
            return null;
        }

        public IEnumerable<FnPeriod> OpenPeriods()
        {
            return Years.Where(y => y.Status == FnYearStatus.Open).SelectMany(y => y.Periods).Where(p => p.Status == FnPeriodStatus.Open).OrderBy(p => p.StartDate);
        }

        public FnPeriod FirstOpenAfter(DateTime date) => OpenPeriods().FirstOrDefault(p => p.StartDate > date.Date);

        public FnPeriod FirstOpen() => OpenPeriods().FirstOrDefault();
    }
}
