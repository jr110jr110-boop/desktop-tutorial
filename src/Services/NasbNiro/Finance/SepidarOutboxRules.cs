using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// R1t D6: what goes to the tax office in the taxpayer (Moadian) package. The release of a retention is the same money that an earlier invoice already charged,
    /// so it is not a sale: <c>TaxService</c> leaves the type out of the VAT return and this class leaves it out of the package, with one rule for both.
    /// </summary>
    internal static class MoadianRules
    {
        public static bool Reported(FnInvoiceType type) => type != FnInvoiceType.RetentionRelease;

        public static IQueryable<FnSalesInvoice> Reportable(IQueryable<FnSalesInvoice> invoices) => invoices.Where(i => i.Type != FnInvoiceType.RetentionRelease);

        public static IQueryable<FnSalesInvoiceLine> ReportableLines(IQueryable<FnSalesInvoiceLine> lines) => lines.Where(l => l.Invoice.Type != FnInvoiceType.RetentionRelease);

        public static readonly string[] LineColumns =
        {
            "InvoiceNumber", "RowNumber", "ItemCode", "Description", "Quantity", "Unit", "UnitPrice", "Discount", "NetAmount", "VatRatePercent", "VatAmount", "LineTotal",
        };

        /// <summary>One row per invoice line of the posted, reportable invoices of the period, in the order of the invoice table (date, invoice, row).</summary>
        public static async Task<OutboxTable> LinesTableAsync(IUnitOfWork uow, DateTime from, DateTime to, CancellationToken ct)
        {
            var table = new OutboxTable { Name = "Lines", Columns = LineColumns.ToList() };
            var rows = await ReportableLines(uow.Set<FnSalesInvoiceLine>().AsNoTracking().Where(l => l.Invoice.Status == FnDocStatus.Posted && l.Invoice.Date >= from && l.Invoice.Date <= to))
                .OrderBy(l => l.Invoice.Date).ThenBy(l => l.InvoiceId).ThenBy(l => l.RowNumber)
                .Select(l => new { l.Invoice.Number, l.RowNumber, l.ItemCode, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.Discount, l.Amount, l.VatRatePercent, l.VatAmount, l.Total })
                .ToListAsync(ct);
            foreach (var r in rows)
                table.Rows.Add(new List<object> { r.Number, r.RowNumber, r.ItemCode, r.Description, r.Quantity, r.Unit, r.UnitPrice, r.Discount, r.Amount, r.VatRatePercent, r.VatAmount, r.Total });
            return table;
        }
    }

    /// <summary>Reading the stored tables of a package (the payload is the record of what was prepared, so the acknowledgement, the tie-out and the text export read it, not the live data).</summary>
    internal static class OutboxTables
    {
        /// <summary>Tables that describe the package or carry the detail of a row of another table: they are not counted in the row count of the package.</summary>
        public static readonly HashSet<string> NotRows = new HashSet<string>(StringComparer.Ordinal) { "Summary", "Workshop", "Lines", "Layout" };

        public static List<OutboxTableJson> Parse(string payloadJson) =>
            string.IsNullOrWhiteSpace(payloadJson) ? new List<OutboxTableJson>() : JsonSerializer.Deserialize<List<OutboxTableJson>>(payloadJson) ?? new List<OutboxTableJson>();

        public static OutboxTableJson Find(IEnumerable<OutboxTableJson> tables, string name) => tables.FirstOrDefault(t => t.Name == name);

        public static int Col(OutboxTableJson table, string column) => table == null ? -1 : table.Columns.IndexOf(column);

        public static string Text(List<JsonElement> row, int col)
        {
            if (col < 0 || col >= row.Count) return null;
            JsonElement e = row[col];
            switch (e.ValueKind)
            {
                case JsonValueKind.String: return e.GetString();
                case JsonValueKind.Number: return e.GetRawText();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                default: return null;
            }
        }

        public static decimal Number(List<JsonElement> row, int col)
        {
            if (col < 0 || col >= row.Count) return 0;
            JsonElement e = row[col];
            switch (e.ValueKind)
            {
                case JsonValueKind.Number: return e.TryGetDecimal(out decimal d) ? d : (decimal)e.GetDouble();
                case JsonValueKind.String: return decimal.TryParse(e.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal p) ? p : 0;
                default: return 0;
            }
        }

        /// <summary>The values of one column of a table by name (empty when the table or the column is missing).</summary>
        public static List<string> Column(OutboxTableJson table, string column)
        {
            int c = Col(table, column);
            return c < 0 ? new List<string>() : table.Rows.Select(r => Text(r, c)).ToList();
        }

        /// <summary>The sum of one numeric column of a table by name.</summary>
        public static decimal Sum(OutboxTableJson table, string column)
        {
            int c = Col(table, column);
            return c < 0 ? 0 : table.Rows.Sum(r => Number(r, c));
        }
    }

    /// <summary>
    /// R1t D6 / R1p merge seam: every sum of voucher lines made by the Sepidar tie-out goes through here. R1p flags the quarter-end labour variance lines (<c>FnVoucherLine.LabourProtected</c>) that only PAYROLL, FINANCEMANAGER and CEO may see;
    /// where the model has that column this leaves the flagged lines out for everybody else, exactly as <c>FnLabourMask.Visible</c> does. In the branch that does not have the column yet it is a pass-through.
    /// After the merge the body can simply call <c>FnLabourMask.Visible(lines, FnLabourMask.Sees(user))</c>.
    /// </summary>
    internal static class VoucherLineVisibility
    {
        private static readonly bool ModelHasLabourFlag = typeof(FnVoucherLine).GetProperty("LabourProtected") != null;

        /// <summary>The roles that read the protected labour lines (the payroll roles of production).</summary>
        public const string PayrollRoles = "PAYROLL,FINANCEMANAGER,CEO";

        public static bool Sees(IInventoryUserContext user) => user.IsInRole(PayrollRoles) && !AiActing.IsActing;

        public static IQueryable<FnVoucherLine> Visible(IQueryable<FnVoucherLine> lines, bool sees) =>
            sees || !ModelHasLabourFlag ? lines : lines.Where(l => !EF.Property<bool>(l, "LabourProtected"));
    }
}
