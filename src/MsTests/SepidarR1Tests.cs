using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.FinanceDtos;
using static MsTests.FinanceTestSupport;

namespace MsTests
{
    /// <summary>
    /// R1t D6: the Sepidar outbox. The taxpayer package carries the invoice lines and leaves the release of a retention out, the answer of the tax office is imported with its tax ids, a package that was sent is
    /// corrected by a new package and never regenerated, the manual status marks need a note, the insurance and tax lists carry the columns of the two offices, and a package is tied out to the ledger.
    /// </summary>
    [TestClass]
    public class SepidarR1Tests
    {
        private static readonly ClaimsPrincipal Payroll = AppHost.Principal(39, "pay.one", "PAYROLL");
        private static readonly ClaimsPrincipal Storekeeper = AppHost.Principal(77, "wh.one", "STOREKEEPER");

        private static AppHost NewHost() => FinanceTestSupport.NewHost("SepidarR1");

        private static (int Year, int Month) ThisMonth()
        {
            var calendar = new PersianCalendar();
            return (calendar.GetYear(DateTime.Today), calendar.GetMonth(DateTime.Today));
        }

        /// <summary>A customer, and a VAT rate in the rate table (the system has no rate of its own: an invoice cannot be saved without one). The rate is a round test figure, not a statutory one.</summary>
        private static async Task<int> CustomerAsync(AppHost host)
        {
            Ok(await host.As(Fin, sp => sp.GetRequiredService<ITaxService>().SaveRateAsync(new SaveTaxRateDto
            {
                Kind = FnTaxKind.Vat, Rate = 10m, EffectiveFrom = new DateTime(2000, 1, 1), Source = "test fixture", Verify = true,
            }, CancellationToken.None)));
            return Ok(await host.As(Fin, sp => sp.GetRequiredService<IReceivablesService>().SaveCustomerAsync(new SaveCustomerDto { Name = "مشتری آزمون " + Guid.NewGuid().ToString("N").Substring(0, 6) }, CancellationToken.None))).Id;
        }

        /// <summary>A posted sales invoice: drafted by the accountant and approved by the manager.</summary>
        private static async Task<SalesInvoiceDto> PostedInvoiceAsync(AppHost host, int customerId, FnInvoiceType type, decimal retentionPercent, params (string Item, decimal Quantity, decimal Price)[] lines)
        {
            var dto = new SaveInvoiceDto
            {
                Date = DateTime.Today, CustomerId = customerId, Type = type, PerformanceRetentionPercent = retentionPercent > 0 ? retentionPercent : (decimal?)null,
                Lines = lines.Select(l => new InvoiceLineDto { Description = "ردیف " + l.Item, ItemCode = l.Item, Quantity = l.Quantity, Unit = "عدد", UnitPrice = l.Price }).ToList(),
            };
            SalesInvoiceDto draft = Ok(await host.As(Fin, sp => sp.GetRequiredService<IReceivablesService>().SaveInvoiceAsync(dto, CancellationToken.None)));
            return Ok(await host.As(Fm, sp => sp.GetRequiredService<IReceivablesService>().InvoiceActionAsync(new DocActionDto { Id = draft.Id, Action = "approve" }, CancellationToken.None)));
        }

        private static Task<ResponseDto<OutboxPackageDto>> Generate(AppHost host, ClaimsPrincipal who, GenerateOutboxDto dto) =>
            host.As(who, sp => sp.GetRequiredService<ISepidarOutboxService>().GenerateAsync(dto, CancellationToken.None));

        private static Task<ResponseDto<OutboxPackageDto>> Action(AppHost host, ClaimsPrincipal who, int id, string action, string note = null, string reference = null) =>
            host.As(who, sp => sp.GetRequiredService<ISepidarOutboxService>().ActionAsync(new OutboxActionDto { Id = id, Action = action, Note = note, Reference = reference }, CancellationToken.None));

        private static Task<ResponseDto<OutboxPackageDto>> Package(AppHost host, ClaimsPrincipal who, int id) =>
            host.As(who, sp => sp.GetRequiredService<ISepidarOutboxService>().GetPackageAsync(id, CancellationToken.None));

        private static Task<ResponseDto<OutboxAckResultDto>> Ack(AppHost host, ClaimsPrincipal who, int id, string content) =>
            host.As(who, sp => sp.GetRequiredService<ISepidarOutboxService>().AckAsync(id, content, CancellationToken.None));

        private static Task<ResponseDto<OutboxTieOutDto>> TieOut(AppHost host, ClaimsPrincipal who, int id) =>
            host.As(who, sp => sp.GetRequiredService<ISepidarOutboxService>().TieOutAsync(id, CancellationToken.None));

        private static Task<T> Db<T>(AppHost host, Func<IUnitOfWork, Task<T>> read) =>
            host.As(Fm, sp => read(sp.GetRequiredService<IUnitOfWork>()));

        private static async Task<List<OutboxTableJson>> TablesOf(AppHost host, int packageId)
        {
            string json = await Db(host, uow => uow.Set<FnOutboxPackage>().AsNoTracking().Where(p => p.Id == packageId).Select(p => p.PayloadJson).FirstAsync());
            return OutboxTables.Parse(json);
        }

        private static async Task<(string TaxId, int Status)> InvoiceTaxAsync(AppHost host, int invoiceId)
        {
            var row = await Db(host, uow => uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => i.Id == invoiceId).Select(i => new { i.TaxId, i.TaxSentStatus }).FirstAsync());
            return (row.TaxId, row.TaxSentStatus);
        }

        /// <summary>A package of the current Persian month that the preparer drafted and the approver approved.</summary>
        private static async Task<OutboxPackageDto> ReadyPackageAsync(AppHost host, FnOutboxKind kind, ClaimsPrincipal preparer, ClaimsPrincipal approver, int? workshopId = null)
        {
            (int year, int month) = ThisMonth();
            OutboxPackageDto draft = Ok(await Generate(host, preparer, new GenerateOutboxDto { Kind = kind, Year = year, Month = month, WorkshopId = workshopId }));
            return Ok(await Action(host, approver, draft.Id, "approve"));
        }

        // ================================================================================== (a) (b) the lines and the exclusion

        [TestMethod]
        public async Task The_taxpayer_package_carries_the_invoice_lines_and_leaves_the_retention_release_out()
        {
            AppHost host = NewHost();
            int customer = await CustomerAsync(host);
            SalesInvoiceDto a = await PostedInvoiceAsync(host, customer, FnInvoiceType.Progress, 10m, ("IT-1", 2m, 1000000m), ("IT-2", 1m, 500000m));
            SalesInvoiceDto b = await PostedInvoiceAsync(host, customer, FnInvoiceType.Goods, 0m, ("IT-3", 3m, 200000m));
            SalesInvoiceDto release = await PostedInvoiceAsync(host, customer, FnInvoiceType.RetentionRelease, 0m, ("RET", 1m, 100000m));
            Assert.AreEqual(FnDocStatus.Posted, release.Status);

            (int year, int month) = ThisMonth();
            OutboxPackageDto package = Ok(await Generate(host, Fin, new GenerateOutboxDto { Kind = FnOutboxKind.SalesInvoicesTaxpayer, Year = year, Month = month }));
            Assert.AreEqual(2, package.RowCount, "the row count is the invoices, not their lines");
            Assert.AreEqual(2, package.Tables.Single(t => t.Name == "Invoices").Rows);
            Assert.AreEqual(3, package.Tables.Single(t => t.Name == "Lines").Rows, "two lines of the first invoice and one of the second");

            List<OutboxTableJson> tables = await TablesOf(host, package.Id);
            CollectionAssert.AreEquivalent(new[] { a.Number, b.Number }, OutboxTables.Column(OutboxTables.Find(tables, "Invoices"), "Number"));
            OutboxTableJson lines = OutboxTables.Find(tables, "Lines");
            CollectionAssert.AreEqual(new[] { "InvoiceNumber", "RowNumber", "ItemCode", "Description", "Quantity", "Unit", "UnitPrice", "Discount", "NetAmount", "VatRatePercent", "VatAmount", "LineTotal" }, lines.Columns);
            Assert.IsFalse(OutboxTables.Column(lines, "InvoiceNumber").Contains(release.Number), "the release of a retention is not a sale");
            CollectionAssert.AreEqual(new[] { a.Number, a.Number, b.Number }, OutboxTables.Column(lines, "InvoiceNumber"));
            CollectionAssert.AreEqual(new[] { "IT-1", "IT-2", "IT-3" }, OutboxTables.Column(lines, "ItemCode"));
            // the line figures add up to the invoice figures that went into the invoice table
            decimal vatOfLines = OutboxTables.Sum(lines, "VatAmount"), vatOfInvoices = OutboxTables.Sum(OutboxTables.Find(tables, "Invoices"), "VatAmount");
            Assert.AreEqual(vatOfInvoices, vatOfLines);
            Assert.AreEqual(a.VatAmount + b.VatAmount, vatOfInvoices);
            Assert.AreEqual(2500000m + 600000m, OutboxTables.Sum(lines, "NetAmount"));
        }

        [TestMethod]
        public async Task A_month_with_only_a_retention_release_has_nothing_to_send()
        {
            AppHost host = NewHost();
            int customer = await CustomerAsync(host);
            (int year, _) = ThisMonth();
            DateTime day = new PersianCalendar().ToDateTime(year - 1, 6, 10, 0, 0, 0, 0);
            host.Write(uow =>
            {
                FnSalesInvoice invoice = AppHost.Add(uow, new FnSalesInvoice
                {
                    Number = "SI-OLD-1", Date = day, CustomerId = customer, Type = FnInvoiceType.RetentionRelease, Status = FnDocStatus.Posted, NetAmount = 100000m, GrossAmount = 100000m,
                    ReceivableAmount = 100000m, CreatedByUserId = 31, CreatedAt = DateTimeOffset.Now,
                });
                invoice.Lines = new List<FnSalesInvoiceLine> { AppHost.Add(uow, new FnSalesInvoiceLine { RowNumber = 1, Description = "آزادسازی", Quantity = 1, UnitPrice = 100000m, Amount = 100000m, Total = 100000m }) };
                uow.SaveChanges();
            }, Fin);
            ResponseDto<OutboxPackageDto> none = await Generate(host, Fin, new GenerateOutboxDto { Kind = FnOutboxKind.SalesInvoicesTaxpayer, Year = year - 1, Month = 6 });
            Assert.IsFalse(none.Success, "the release alone is not a sale: there is nothing to report");

            Assert.IsFalse(MoadianRules.Reported(FnInvoiceType.RetentionRelease));
            foreach (FnInvoiceType type in new[] { FnInvoiceType.Advance, FnInvoiceType.Progress, FnInvoiceType.Final, FnInvoiceType.Goods, FnInvoiceType.Service, FnInvoiceType.CreditNote })
                Assert.IsTrue(MoadianRules.Reported(type), type.ToString());
        }

        // ================================================================================== (f) corrections

        [TestMethod]
        public async Task A_sent_package_is_never_regenerated_a_correction_refers_to_it()
        {
            AppHost host = NewHost();
            int customer = await CustomerAsync(host);
            await PostedInvoiceAsync(host, customer, FnInvoiceType.Progress, 0m, ("IT-1", 1m, 1000000m));
            (int year, int month) = ThisMonth();
            var request = new GenerateOutboxDto { Kind = FnOutboxKind.SalesInvoicesTaxpayer, Year = year, Month = month };

            OutboxPackageDto first = await ReadyPackageAsync(host, FnOutboxKind.SalesInvoicesTaxpayer, Fin, Fm);
            // an approved package that was not sent is simply replaced by a new one
            OutboxPackageDto replacement = Ok(await Generate(host, Fin, request));
            Assert.AreEqual(2, replacement.Version);
            Assert.AreEqual(FnOutboxStatus.Superseded, Ok(await Package(host, Fin, first.Id)).Status);
            OutboxPackageDto ready = Ok(await Action(host, Fm, replacement.Id, "approve"));

            // the manual mark needs a note
            ResponseDto<OutboxPackageDto> noNote = await Action(host, Fin, ready.Id, "mark-sent", reference: "TRK-1");
            Assert.IsFalse(noNote.Success);
            StringAssert.Contains(noNote.Message, "یادداشت");
            OutboxPackageDto sent = Ok(await Action(host, Fin, ready.Id, "mark-sent", "فایل در سپیدار وارد و ارسال شد", "TRK-1"));
            Assert.AreEqual(FnOutboxStatus.Sent, sent.Status);

            // regenerating a sent package is refused
            ResponseDto<OutboxPackageDto> again = await Generate(host, Fin, request);
            Assert.IsFalse(again.Success);
            // a correction needs the original and the reason
            ResponseDto<OutboxPackageDto> noReason = await Generate(host, Fin, new GenerateOutboxDto { Kind = request.Kind, Year = year, Month = month, CorrectsPackageId = sent.Id });
            Assert.IsFalse(noReason.Success);
            StringAssert.Contains(noReason.Message, "دلیل اصلاح");
            ResponseDto<OutboxPackageDto> wrongTarget = await Generate(host, Fin, new GenerateOutboxDto { Kind = request.Kind, Year = year, Month = month, CorrectsPackageId = sent.Id + 99, Note = "اصلاح" });
            Assert.IsFalse(wrongTarget.Success);

            OutboxPackageDto correction = Ok(await Generate(host, Fin, new GenerateOutboxDto { Kind = request.Kind, Year = year, Month = month, CorrectsPackageId = sent.Id, Note = "اصلاح شناسه مشتری" }));
            Assert.AreEqual(sent.Id, correction.CorrectsPackageId);
            Assert.AreEqual(3, correction.Version);
            Assert.AreEqual(FnOutboxStatus.Draft, correction.Status);
            OutboxPackageDto originalNow = Ok(await Package(host, Fin, sent.Id));
            Assert.AreEqual(FnOutboxStatus.Sent, originalNow.Status, "the package that was sent stays as it was");
            Assert.IsNull(originalNow.SupersededByPackageId);

            // a draft is not something to correct; a second correction replaces the first draft and leaves the original alone
            ResponseDto<OutboxPackageDto> ofDraft = await Generate(host, Fin, new GenerateOutboxDto { Kind = request.Kind, Year = year, Month = month, CorrectsPackageId = correction.Id, Note = "اصلاح" });
            Assert.IsFalse(ofDraft.Success);
            OutboxPackageDto second = Ok(await Generate(host, Fin, new GenerateOutboxDto { Kind = request.Kind, Year = year, Month = month, CorrectsPackageId = sent.Id, Note = "اصلاح دوباره" }));
            Assert.AreEqual(FnOutboxStatus.Superseded, Ok(await Package(host, Fin, correction.Id)).Status);
            Assert.AreEqual(FnOutboxStatus.Sent, Ok(await Package(host, Fin, sent.Id)).Status);
            Assert.AreEqual(4, second.Version);

            // the trail has the manual mark and the corrections, each with its reason
            List<FnAuditLog> trail = await Db(host, uow => uow.Set<FnAuditLog>().AsNoTracking().Where(a => a.EntityType == "FnOutboxPackage").ToListAsync());
            Assert.IsTrue(trail.Any(a => a.Action == "OutboxMarkSent" && a.UserId == 31 && a.Summary.Contains("TRK-1")));
            Assert.AreEqual(2, trail.Count(a => a.Action == "OutboxCorrection"));
            Assert.IsTrue(trail.Any(a => a.Action == "OutboxApprove" && a.UserId == 32));
        }

        [TestMethod]
        public async Task The_manual_acceptance_and_rejection_need_a_note_and_are_audited()
        {
            AppHost host = NewHost();
            int customer = await CustomerAsync(host);
            await PostedInvoiceAsync(host, customer, FnInvoiceType.Progress, 0m, ("IT-1", 1m, 1000000m));
            OutboxPackageDto ready = await ReadyPackageAsync(host, FnOutboxKind.SalesInvoicesTaxpayer, Fin, Fm);
            Ok(await Action(host, Fin, ready.Id, "mark-sent", "ارسال دستی", "TRK-9"));

            Assert.IsFalse((await Action(host, Fin, ready.Id, "mark-accepted")).Success);
            Assert.IsFalse((await Action(host, Fin, ready.Id, "mark-rejected")).Success);
            Assert.AreEqual(FnOutboxStatus.Accepted, Ok(await Action(host, Fin, ready.Id, "mark-accepted", "پاسخ تلفنی سازمان؛ پیام پذیرش دریافت شد")).Status);
            List<FnAuditLog> trail = await Db(host, uow => uow.Set<FnAuditLog>().AsNoTracking().Where(a => a.EntityType == "FnOutboxPackage" && a.EntityId == ready.Id).ToListAsync());
            Assert.IsTrue(trail.Any(a => a.Action == "OutboxMarkOk" && a.Summary.Contains("پاسخ تلفنی")));
            Assert.IsTrue(trail.Any(a => a.Action == "OutboxMarkSent"));
        }

        // ================================================================================== (c) the answer of the tax office

        [TestMethod]
        public async Task The_answer_file_writes_the_tax_ids_and_moves_the_package()
        {
            AppHost host = NewHost();
            int customer = await CustomerAsync(host);
            SalesInvoiceDto a = await PostedInvoiceAsync(host, customer, FnInvoiceType.Progress, 0m, ("IT-1", 1m, 1000000m));
            SalesInvoiceDto b = await PostedInvoiceAsync(host, customer, FnInvoiceType.Goods, 0m, ("IT-2", 1m, 300000m));
            (int year, int month) = ThisMonth();

            // an answer for a package nobody approved is refused
            OutboxPackageDto draft = Ok(await Generate(host, Fin, new GenerateOutboxDto { Kind = FnOutboxKind.SalesInvoicesTaxpayer, Year = year, Month = month }));
            ResponseDto<OutboxAckResultDto> early = await Ack(host, Fin, draft.Id, "[]");
            Assert.IsFalse(early.Success);
            StringAssert.Contains(early.Message, "تایید نشده");
            OutboxPackageDto ready = Ok(await Action(host, Fm, draft.Id, "approve"));

            // the storekeeper has no part in the outbox
            Assert.IsFalse((await Ack(host, Storekeeper, ready.Id, "[]")).Success);

            // one accepted, one waiting: the package is sent (the answer proves it) and stays open
            string first = "[{\"invoiceNo\":\"" + a.Number + "\",\"taxId\":\"A1B2C3D4E5F6G7H8I9J0K1\",\"status\":\"accepted\",\"message\":\"\"},{\"invoiceNo\":\"" + b.Number + "\",\"status\":\"pending\"}]";
            OutboxAckResultDto r1 = Ok(await Ack(host, Fin, ready.Id, first));
            Assert.AreEqual(1, r1.Accepted);
            Assert.AreEqual(1, r1.Pending);
            Assert.AreEqual(0, r1.Unanswered);
            Assert.AreEqual(FnOutboxStatus.Sent, r1.Status);
            OutboxPackageDto afterFirst = Ok(await Package(host, Fin, ready.Id));
            Assert.AreEqual(FnOutboxStatus.Sent, afterFirst.Status);
            Assert.IsNotNull(afterFirst.SentAt);
            (string taxA, int statusA) = await InvoiceTaxAsync(host, a.Id);
            Assert.AreEqual("A1B2C3D4E5F6G7H8I9J0K1", taxA);
            Assert.AreEqual(2, statusA);
            (string taxB, int statusB) = await InvoiceTaxAsync(host, b.Id);
            Assert.IsNull(taxB);
            Assert.AreEqual(1, statusB);

            // a different tax id for an invoice that was accepted, an invoice of another package, an unknown status and an accepted row without a tax id: refused, nothing is written
            ResponseDto<OutboxAckResultDto> conflict = await Ack(host, Fin, ready.Id, "invoiceNo,taxId,status\n" + a.Number + ",ZZZ999,accepted\n");
            Assert.IsFalse(conflict.Success);
            StringAssert.Contains(conflict.Message, a.Number);
            ResponseDto<OutboxAckResultDto> stranger = await Ack(host, Fin, ready.Id, "[{\"invoiceNo\":\"SI-NOPE-1\",\"taxId\":\"X1\",\"status\":\"accepted\"}]");
            Assert.IsFalse(stranger.Success);
            StringAssert.Contains(stranger.Message, "SI-NOPE-1");
            ResponseDto<OutboxAckResultDto> badStatus = await Ack(host, Fin, ready.Id, "invoiceNo,taxId,status\n" + b.Number + ",T2,maybe\n");
            Assert.IsFalse(badStatus.Success);
            StringAssert.Contains(badStatus.Message, "maybe");
            ResponseDto<OutboxAckResultDto> noTax = await Ack(host, Fin, ready.Id, "invoiceNo,taxId,status\n" + b.Number + ",,accepted\n");
            Assert.IsFalse(noTax.Success);
            StringAssert.Contains(noTax.Message, b.Number);
            (string stillB, int stillStatusB) = await InvoiceTaxAsync(host, b.Id);
            Assert.IsNull(stillB, "a refused answer writes nothing");
            Assert.AreEqual(1, stillStatusB);
            Assert.AreEqual("A1B2C3D4E5F6G7H8I9J0K1", (await InvoiceTaxAsync(host, a.Id)).TaxId, "an accepted tax id never changes");

            // the second answer, as a semicolon CSV with a byte-order mark and a header: everything is accepted now
            OutboxAckResultDto r2 = Ok(await Ack(host, Fin, ready.Id, "﻿invoice_no;tax_id;status;message\r\n" + b.Number + ";TAXB0000000000000002;success;\r\n"));
            Assert.AreEqual(FnOutboxStatus.Accepted, r2.Status);
            Assert.AreEqual(0, r2.Unanswered);
            Assert.AreEqual(FnOutboxStatus.Accepted, Ok(await Package(host, Fin, ready.Id)).Status);
            (string taxB2, int statusB2) = await InvoiceTaxAsync(host, b.Id);
            Assert.AreEqual("TAXB0000000000000002", taxB2);
            Assert.AreEqual(2, statusB2);
            // the same file again changes nothing and is not an error
            OutboxAckResultDto again = Ok(await Ack(host, Fin, ready.Id, "invoice_no;tax_id;status;message\r\n" + b.Number + ";TAXB0000000000000002;success;\r\n"));
            Assert.AreEqual(1, again.Unchanged);
            Assert.AreEqual(FnOutboxStatus.Accepted, again.Status);

            List<FnAuditLog> trail = await Db(host, uow => uow.Set<FnAuditLog>().AsNoTracking().Where(l => l.Action == "TaxAck" || l.Action == "OutboxAck").ToListAsync());
            Assert.AreEqual(3, trail.Count(l => l.Action == "TaxAck"), "two rows of the first file and one of the second");
            Assert.AreEqual(2, trail.Count(l => l.Action == "OutboxAck"), "the repeated file leaves no row");
        }

        [TestMethod]
        public async Task A_refused_invoice_makes_the_package_rejected_when_all_are_answered()
        {
            AppHost host = NewHost();
            int customer = await CustomerAsync(host);
            SalesInvoiceDto a = await PostedInvoiceAsync(host, customer, FnInvoiceType.Progress, 0m, ("IT-1", 1m, 1000000m));
            SalesInvoiceDto b = await PostedInvoiceAsync(host, customer, FnInvoiceType.Goods, 0m, ("IT-2", 1m, 300000m));
            OutboxPackageDto ready = await ReadyPackageAsync(host, FnOutboxKind.SalesInvoicesTaxpayer, Fin, Fm);

            Assert.IsFalse((await Ack(host, Fin, ready.Id, "[{\"invoiceNo\":\"" + a.Number + "\",\"status\":\"rejected\"}]")).Success, "a refusal needs its reason");
            OutboxAckResultDto result = Ok(await Ack(host, Fin, ready.Id,
                "{\"rows\":[{\"invoiceNo\":\"" + a.Number + "\",\"status\":\"rejected\",\"message\":\"buyer economic code is not valid\"},{\"invoiceNo\":\"" + b.Number + "\",\"taxId\":\"TAXB1\",\"status\":\"accepted\"}]}"));
            Assert.AreEqual(FnOutboxStatus.Rejected, result.Status);
            Assert.AreEqual(1, result.Rejected);
            OutboxPackageDto package = Ok(await Package(host, Fin, ready.Id));
            Assert.AreEqual(FnOutboxStatus.Rejected, package.Status);
            StringAssert.Contains(package.Error, a.Number);
            StringAssert.Contains(package.Error, "buyer economic code");
            (string taxA, int statusA) = await InvoiceTaxAsync(host, a.Id);
            Assert.IsNull(taxA);
            Assert.AreEqual(3, statusA);
            (string taxB, int statusB) = await InvoiceTaxAsync(host, b.Id);
            Assert.AreEqual("TAXB1", taxB);
            Assert.AreEqual(2, statusB);
            // a rejected package takes no further answer: a correction package is made instead
            Assert.IsFalse((await Ack(host, Fin, ready.Id, "[{\"invoiceNo\":\"" + a.Number + "\",\"taxId\":\"TAXA1\",\"status\":\"accepted\"}]")).Success);
        }

        [TestMethod]
        public void The_answer_file_is_read_as_json_or_csv()
        {
            var problems = new List<string>();
            List<SepidarOutboxService.AckRow> rows = SepidarOutboxService.ParseAck("[{\"invoice_no\":\"SI-1\",\"Tax_Id\":\"T1\",\"State\":\"Accepted\"},{\"invoiceNo\":\"SI-2\",\"status\":\"pending\"}]", problems);
            Assert.AreEqual(0, problems.Count);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("SI-1", rows[0].InvoiceNo);
            Assert.AreEqual("T1", rows[0].TaxId);
            Assert.AreEqual(SepidarOutboxService.TaxStatus.Accepted, rows[0].Status);
            Assert.AreEqual(SepidarOutboxService.TaxStatus.Sent, rows[1].Status);

            // csv without a header: invoiceNo, taxId, status, message; a quoted message with a comma and a doubled quote
            rows = SepidarOutboxService.ParseAck("SI-3,T3,rejected,\"bad, \"\"value\"\"\"\nSI-4,T4,2,", problems);
            Assert.AreEqual(0, problems.Count);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("bad, \"value\"", rows[0].Message);
            Assert.AreEqual(SepidarOutboxService.TaxStatus.Rejected, rows[0].Status);
            Assert.AreEqual(SepidarOutboxService.TaxStatus.Accepted, rows[1].Status);

            // a header in another order, tab separated
            rows = SepidarOutboxService.ParseAck("status\tmessage\tinvoiceNo\ttaxId\nsuccess\t\tSI-5\tT5\n", problems);
            Assert.AreEqual(0, problems.Count);
            Assert.AreEqual("SI-5", rows.Single().InvoiceNo);
            Assert.AreEqual("T5", rows.Single().TaxId);

            // an object that is a single row
            rows = SepidarOutboxService.ParseAck("{\"invoiceNo\":\"SI-6\",\"taxId\":\"T6\",\"status\":\"ok\"}", problems);
            Assert.AreEqual(0, problems.Count);
            Assert.AreEqual(1, rows.Count);

            var bad = new List<string>();
            SepidarOutboxService.ParseAck("", bad);
            Assert.AreEqual(1, bad.Count);
            bad.Clear();
            SepidarOutboxService.ParseAck("[1,2]", bad);
            Assert.AreEqual(1, bad.Count);
            bad.Clear();
            SepidarOutboxService.ParseAck("{broken", bad);
            Assert.AreEqual(1, bad.Count);
            bad.Clear();
            SepidarOutboxService.ParseAck("SI-7,T7,unknown", bad);
            Assert.AreEqual(1, bad.Count);

            Assert.AreEqual(SepidarOutboxService.TaxStatus.Accepted, SepidarOutboxService.ParseTaxStatus("پذیرفته‌شده"));
            Assert.AreEqual(SepidarOutboxService.TaxStatus.Accepted, SepidarOutboxService.ParseTaxStatus(" Success "));
            Assert.AreEqual(SepidarOutboxService.TaxStatus.Rejected, SepidarOutboxService.ParseTaxStatus(" FAILED "));
            Assert.AreEqual(SepidarOutboxService.TaxStatus.Sent, SepidarOutboxService.ParseTaxStatus("in-progress"));
            Assert.IsNull(SepidarOutboxService.ParseTaxStatus("4"));
            Assert.IsNull(SepidarOutboxService.ParseTaxStatus(null));
        }

        // ================================================================================== (d) (e) the lists of the two offices

        /// <summary>One workshop, one employee and one approved payroll run of the current Persian month with the lines the lists read.</summary>
        private static async Task<int> SeedPayslipAsync(AppHost host)
        {
            (int year, int month) = ThisMonth();
            return await Db(host, async uow =>
            {
                FnInsuranceWorkshop workshop = AppHost.Add(uow, new FnInsuranceWorkshop { Code = "W-1", Name = "کارگاه مرکزی", EmployerName = "شرکت آزمون", Address = "نشانی آزمون", Branch = "شعبه یک", IsActive = true });
                await uow.SaveChangesAsync();
                FnEmployee employee = AppHost.Add(uow, new FnEmployee
                {
                    PersonnelCode = "P-100", FirstName = "کارمند", LastName = "آزمون", FatherName = "پدر", NationalCode = "0012345679", BirthDate = new DateTime(1990, 5, 1), Gender = 1,
                    IdentityCardNumber = "4321", Nationality = "ایرانی", InsuranceNumber = "1234567890", InsuranceJobCode = "J-12", WorkshopId = workshop.Id, HireDate = new DateTime(2022, 3, 21),
                    DailyWage = 1000000m, IsActive = true, CreatedAt = DateTimeOffset.Now,
                });
                await uow.SaveChangesAsync();
                FnPayrollRun run = AppHost.Add(uow, new FnPayrollRun
                {
                    Number = "PR-T-1", Year = year, Month = month, Status = FnPayrollStatus.Approved, CreatedByUserId = 39, PreparedByUserId = 39, CreatedAt = DateTimeOffset.Now, CalculatedAt = DateTimeOffset.Now,
                    ApprovedByUserId = 32, ApprovedAt = DateTimeOffset.Now, EmployeeCount = 1,
                });
                await uow.SaveChangesAsync();
                AppHost.Add(uow, new FnPayslip
                {
                    RunId = run.Id, EmployeeId = employee.Id, WorkshopId = workshop.Id, DaysPaid = 29.5m, GrossEarnings = 31000000m, InsurableTotal = 31000000m, TaxableTotal = 28900000m,
                    EmployeeInsurance = 2100000m, EmployerInsurance = 6200000m, Tax = 450000m, Net = 28450000m,
                    Lines = new List<FnPayslipLine>
                    {
                        new FnPayslipLine { RowNumber = 1, ItemCode = "BASE_WAGE", Title = "مزد مبنا", Kind = FnPayrollItemKind.Earning, Base = 1000000m, Amount = 29500000m, Insurable = true, Taxable = true },
                        new FnPayslipLine { RowNumber = 2, ItemCode = "SS_EMPLOYEE", Title = "بیمه سهم کارگر", Kind = FnPayrollItemKind.Deduction, Base = 30000000m, Amount = 2100000m, AccountRole = AccountRoles.InsurancePayable },
                        new FnPayslipLine { RowNumber = 3, ItemCode = "SS_EMPLOYER", Title = "بیمه سهم کارفرما", Kind = FnPayrollItemKind.EmployerContribution, Amount = 5300000m, AccountRole = AccountRoles.InsurancePayable },
                        new FnPayslipLine { RowNumber = 4, ItemCode = "UNEMPLOYMENT", Title = "بیمه بیکاری", Kind = FnPayrollItemKind.EmployerContribution, Amount = 900000m, AccountRole = AccountRoles.InsurancePayable },
                        new FnPayslipLine { RowNumber = 5, ItemCode = "INCOME_TAX", Title = "مالیات", Kind = FnPayrollItemKind.Deduction, Amount = 450000m, AccountRole = AccountRoles.PayrollTaxPayable },
                    },
                });
                await uow.SaveChangesAsync();
                return workshop.Id;
            });
        }

        [TestMethod]
        public async Task The_insurance_list_package_has_the_social_security_columns_and_whole_insured_days()
        {
            AppHost host = NewHost();
            int workshopId = await SeedPayslipAsync(host);
            OutboxPackageDto ready = await ReadyPackageAsync(host, FnOutboxKind.PayrollInsuranceList, Payroll, Fm, workshopId);
            Assert.AreEqual(1, ready.RowCount);
            List<OutboxTableJson> tables = await TablesOf(host, ready.Id);
            OutboxTableJson workers = OutboxTables.Find(tables, "Workers");
            CollectionAssert.AreEqual(PayrollListsR1.WorkerColumns, workers.Columns);
            List<System.Text.Json.JsonElement> row = workers.Rows[0];
            string Text(string column) => OutboxTables.Text(row, OutboxTables.Col(workers, column));
            decimal Num(string column) => OutboxTables.Number(row, OutboxTables.Col(workers, column));
            Assert.AreEqual(1m, Num("RowNumber"));
            Assert.AreEqual("4321", Text("IdentityCardNumber"));
            Assert.AreEqual("مرد", Text("Gender"));
            Assert.AreEqual("ایرانی", Text("Nationality"));
            Assert.AreEqual(LedgerMath.PersianDate(new DateTime(2022, 3, 21)), Text("ContractStart"));
            Assert.IsNull(Text("ContractEnd"));
            Assert.AreEqual(30m, Num("Days"), "29.5 paid days: the fraction of the lateness deduction does not lower the insured days");
            Assert.AreEqual(1000000m, Num("DailyWage"));
            Assert.AreEqual(29500000m, Num("MonthlyWage"));
            Assert.AreEqual(1500000m, Num("Benefits"));
            Assert.AreEqual(2100000m, Num("EmployeeShare"));
            Assert.AreEqual(5300000m, Num("EmployerShare"));
            Assert.AreEqual(900000m, Num("Unemployment"));
            OutboxTableJson header = OutboxTables.Find(tables, "Workshop");
            Assert.AreEqual("شرکت آزمون", OutboxTables.Text(header.Rows[0], OutboxTables.Col(header, "EmployerName")));
            Assert.AreEqual(30m, OutboxTables.Sum(header, "TotalDays"));
            Assert.AreEqual("Verify", OutboxTables.Text(OutboxTables.Find(tables, "Layout").Rows[0], 1));

            // the second file type: the fixed-width text files of the list software, built from the stored tables
            byte[] zip = Ok(await host.As(Payroll, sp => sp.GetRequiredService<ISepidarOutboxService>().ExportDskAsync(ready.Id, CancellationToken.None)));
            var files = new Dictionary<string, string>();
            using (var archive = new ZipArchive(new MemoryStream(zip)))
                foreach (ZipArchiveEntry entry in archive.Entries)
                    using (var reader = new StreamReader(entry.Open(), new UTF8Encoding(false))) files[entry.FullName] = reader.ReadToEnd();
            CollectionAssert.AreEquivalent(new[] { "DSKKAR.TXT", "DSKWOR.TXT", "LAYOUT.TXT" }, files.Keys.ToList());
            string[] kar = files["DSKKAR.TXT"].Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries), wor = files["DSKWOR.TXT"].Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(1, kar.Length);
            Assert.AreEqual(1, wor.Length);
            Assert.AreEqual(DskLayout.Width(DskLayout.Workshop), kar[0].Length);
            Assert.AreEqual(DskLayout.Width(DskLayout.Worker), wor[0].Length);
            string Slice(string record, DskLayout.Field[] fields, string name)
            {
                int start = 0;
                foreach (DskLayout.Field f in fields)
                {
                    if (f.Name == name) return record.Substring(start, f.Width);
                    start += f.Width;
                }
                throw new InvalidOperationException(name);
            }
            Assert.AreEqual("W-1".PadRight(10), Slice(kar[0], DskLayout.Workshop, "DSK_ID"));
            Assert.AreEqual("0000030", Slice(kar[0], DskLayout.Workshop, "DSK_TDD"));
            Assert.AreEqual("1234567890", Slice(wor[0], DskLayout.Worker, "DSW_ID1"));
            Assert.AreEqual("30", Slice(wor[0], DskLayout.Worker, "DSW_DD"));
            Assert.AreEqual("1", Slice(wor[0], DskLayout.Worker, "DSW_SEX"));
            Assert.AreEqual("00000029500000", Slice(wor[0], DskLayout.Worker, "DSW_MAH"));
            StringAssert.Contains(files["LAYOUT.TXT"], "Verify");
            StringAssert.Contains(files["LAYOUT.TXT"], "DSW_IDNO");

            // the text file is for the insurance list only, and only payroll roles read it
            (int year, int month) = ThisMonth();
            OutboxPackageDto tax = Ok(await Generate(host, Payroll, new GenerateOutboxDto { Kind = FnOutboxKind.PayrollTaxList, Year = year, Month = month }));
            Ok(await Action(host, Fm, tax.Id, "approve"));
            Assert.IsFalse((await host.As(Payroll, sp => sp.GetRequiredService<ISepidarOutboxService>().ExportDskAsync(tax.Id, CancellationToken.None))).Success);
            Assert.IsFalse((await host.As(Fin, sp => sp.GetRequiredService<ISepidarOutboxService>().ExportDskAsync(ready.Id, CancellationToken.None))).Success, "an accountant does not read payroll packages");
        }

        [TestMethod]
        public void The_insured_days_are_whole_numbers_on_the_thirty_day_basis()
        {
            Assert.AreEqual(30, PayrollListsR1.InsuredDays(30m));
            Assert.AreEqual(30, PayrollListsR1.InsuredDays(29.3m));
            Assert.AreEqual(30, PayrollListsR1.InsuredDays(29.99m));
            Assert.AreEqual(29, PayrollListsR1.InsuredDays(29m));
            Assert.AreEqual(28, PayrollListsR1.InsuredDays(27.01m));
            Assert.AreEqual(30, PayrollListsR1.InsuredDays(31m), "a 31-day month is still 30 days");
            Assert.AreEqual(0, PayrollListsR1.InsuredDays(0m));
            Assert.AreEqual(0, PayrollListsR1.InsuredDays(-3m));
            Assert.AreEqual("مرد", PayrollListsR1.GenderWord(1));
            Assert.AreEqual("2", PayrollListsR1.GenderCode(PayrollListsR1.GenderWord(2)));
            Assert.AreEqual(string.Empty, PayrollListsR1.GenderCode(null));
            Assert.IsNull(PayrollListsR1.GenderWord(0));
        }

        [TestMethod]
        public void The_text_fields_are_cut_padded_and_zero_filled()
        {
            var text = new DskLayout.Field("T", 5, false, "x", "t");
            var number = new DskLayout.Field("N", 6, true, "x", "n");
            Assert.AreEqual("ab   ", DskLayout.Format(text, "ab"));
            Assert.AreEqual("abcde", DskLayout.Format(text, "abcdefg"));
            Assert.AreEqual("     ", DskLayout.Format(text, null));
            Assert.AreEqual("a  b ", DskLayout.Format(text, "a\r\nb"), "a line break would split the record");
            Assert.AreEqual("000042", DskLayout.Format(number, "42"));
            Assert.AreEqual("000000", DskLayout.Format(number, "-5"));
            Assert.AreEqual("000000", DskLayout.Format(number, "abc"));
            Assert.AreEqual("000043", DskLayout.Format(number, "42.5"));
            Assert.AreEqual("345678", DskLayout.Format(number, "12345678"), "a number that does not fit keeps its low digits");
            Assert.AreEqual(DskLayout.Workshop.Sum(f => f.Width), DskLayout.Width(DskLayout.Workshop));
            Assert.AreEqual(DskLayout.Workshop.Length, DskLayout.Workshop.Select(f => f.Name).Distinct().Count());
            Assert.AreEqual(DskLayout.Worker.Length, DskLayout.Worker.Select(f => f.Name).Distinct().Count());
        }

        [TestMethod]
        public async Task The_tax_list_package_has_gross_exemptions_taxable_and_tax_for_each_employee()
        {
            AppHost host = NewHost();
            await SeedPayslipAsync(host);
            OutboxPackageDto ready = await ReadyPackageAsync(host, FnOutboxKind.PayrollTaxList, Payroll, Fm);
            Assert.AreEqual(1, ready.RowCount);
            List<OutboxTableJson> tables = await TablesOf(host, ready.Id);
            OutboxTableJson rows = OutboxTables.Find(tables, "Employees");
            CollectionAssert.AreEqual(PayrollListsR1.TaxRowColumns, rows.Columns);
            List<System.Text.Json.JsonElement> row = rows.Rows[0];
            decimal Num(string column) => OutboxTables.Number(row, OutboxTables.Col(rows, column));
            Assert.AreEqual("0012345679", OutboxTables.Text(row, OutboxTables.Col(rows, "NationalCode")));
            (int year, int month) = ThisMonth();
            Assert.AreEqual((decimal)month, Num("Month"));
            Assert.AreEqual((decimal)year, Num("Year"));
            Assert.AreEqual(31000000m, Num("Gross"));
            Assert.AreEqual(2100000m, Num("Exemptions"), "gross minus taxable");
            Assert.AreEqual(28900000m, Num("Taxable"));
            Assert.AreEqual(450000m, Num("Tax"));
            OutboxTableJson summary = OutboxTables.Find(tables, "Summary");
            Assert.AreEqual(450000m, OutboxTables.Sum(summary, "TotalTax"));
            Assert.AreEqual("Verify", OutboxTables.Text(OutboxTables.Find(tables, "Layout").Rows[0], 1));
        }

        // ================================================================================== (g) tie-out

        [TestMethod]
        public async Task The_tie_out_compares_the_package_with_the_ledger_for_each_account()
        {
            AppHost host = NewHost();
            int customer = await CustomerAsync(host);
            SalesInvoiceDto a = await PostedInvoiceAsync(host, customer, FnInvoiceType.Progress, 0m, ("IT-1", 1m, 1000000m));
            SalesInvoiceDto b = await PostedInvoiceAsync(host, customer, FnInvoiceType.Goods, 0m, ("IT-2", 2m, 300000m));
            (int year, int month) = ThisMonth();
            OutboxPackageDto package = Ok(await Generate(host, Fin, new GenerateOutboxDto { Kind = FnOutboxKind.SalesInvoicesTaxpayer, Year = year, Month = month }));

            // before the events are posted the ledger has nothing: the whole VAT is the difference
            OutboxTieOutDto before = Ok(await TieOut(host, Fin, package.Id));
            OutboxTieOutRowDto vatBefore = before.Rows.Single();
            Assert.AreEqual(AccountRoles.VatSales, vatBefore.Key);
            Assert.AreEqual("30205", vatBefore.AccountCode);
            Assert.AreEqual(a.VatAmount + b.VatAmount, vatBefore.PackageAmount);
            Assert.IsTrue(vatBefore.PackageAmount > 0);
            Assert.AreEqual(0m, vatBefore.LedgerAmount);
            Assert.AreEqual(-vatBefore.PackageAmount, vatBefore.Difference);
            Assert.IsFalse(before.Ties);

            await host.As(Fin, sp => sp.GetRequiredService<IEventPostingService>().PostPendingAsync(CancellationToken.None));
            OutboxTieOutDto after = Ok(await TieOut(host, Fin, package.Id));
            OutboxTieOutRowDto vat = after.Rows.Single();
            Assert.AreEqual(vat.PackageAmount, vat.LedgerAmount);
            Assert.AreEqual(vat.PackageAmount, vat.LedgerFromSource);
            Assert.AreEqual(0m, vat.LedgerOther);
            Assert.AreEqual(0m, vat.Difference);
            Assert.IsTrue(after.Ties);

            // a manual voucher on the VAT account shows as a difference that comes from another source
            var manual = new SaveVoucherDto
            {
                Date = DateTime.Today, Description = "اصلاح دستی مالیات",
                Lines = new List<SaveVoucherLineDto> { new SaveVoucherLineDto { AccountCode = "80108", Debit = 7000m }, new SaveVoucherLineDto { AccountCode = "30205", Credit = 7000m } },
            };
            VoucherDto voucher = Ok(await host.As(Fin, sp => sp.GetRequiredService<IGlService>().SaveVoucherAsync(manual, CancellationToken.None)));
            Ok(await host.As(Fm, sp => sp.GetRequiredService<IGlService>().VoucherActionAsync(new VoucherActionDto { Id = voucher.Id, Action = "check" }, CancellationToken.None)));
            OutboxTieOutDto tampered = Ok(await TieOut(host, Fin, package.Id));
            OutboxTieOutRowDto changed = tampered.Rows.Single();
            Assert.AreEqual(7000m, changed.Difference);
            Assert.AreEqual(7000m, changed.LedgerOther);
            Assert.AreEqual(0m, changed.DifferenceFromSource, "the invoices still agree with the package");
            Assert.IsFalse(tampered.Ties);
            Assert.AreEqual(7000m, tampered.TotalAbsoluteDifference);

            // the storekeeper cannot read it and an unknown package is not found
            Assert.IsFalse((await TieOut(host, Storekeeper, package.Id)).Success);
            Assert.IsFalse((await TieOut(host, Fin, package.Id + 500)).Success);
        }

        [TestMethod]
        public async Task The_payroll_tie_out_reads_the_insurance_and_tax_accounts()
        {
            AppHost host = NewHost();
            int workshopId = await SeedPayslipAsync(host);
            (int year, int month) = ThisMonth();
            OutboxPackageDto insurance = Ok(await Generate(host, Payroll, new GenerateOutboxDto { Kind = FnOutboxKind.PayrollInsuranceList, Year = year, Month = month, WorkshopId = workshopId }));
            OutboxPackageDto tax = Ok(await Generate(host, Payroll, new GenerateOutboxDto { Kind = FnOutboxKind.PayrollTaxList, Year = year, Month = month }));
            OutboxTieOutDto i = Ok(await TieOut(host, Payroll, insurance.Id));
            OutboxTieOutDto t = Ok(await TieOut(host, Payroll, tax.Id));
            Assert.AreEqual("30202", i.Rows.Single().AccountCode);
            Assert.AreEqual(2100000m + 5300000m + 900000m, i.Rows.Single().PackageAmount);
            Assert.AreEqual("30203", t.Rows.Single().AccountCode);
            Assert.AreEqual(450000m, t.Rows.Single().PackageAmount);
            Assert.IsFalse(i.Ties, "nothing is posted to the ledger for the seeded run");
            Assert.IsFalse((await TieOut(host, Fin, insurance.Id)).Success, "an accountant does not read payroll packages");
        }
    }
}
