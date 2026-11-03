using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// R1t D6: the insurance list in the text layout of the social-security list software (a workshop file and a workers file, fixed-width fields) as the second file type of an insurance package.
    /// It is built from the stored tables of the package, never from live data, so the file always agrees with the package that was approved.
    /// Verify: the field names, their order, the widths, the date and gender codes and the character set follow the DSKKAR (workshop) and DSKWOR (insured person) lists as the finance team described them; the layout is a
    /// structure to be checked against the office's current specification before the first official submission. The zip carries a LAYOUT.TXT with every field so that the check can be done field by field.
    /// </summary>
    internal static class DskLayout
    {
        public sealed class Field
        {
            public Field(string name, int width, bool numeric, string source, string title)
            {
                Name = name; Width = width; Numeric = numeric; Source = source; Title = title;
            }

            public string Name { get; }
            public int Width { get; }
            public bool Numeric { get; }
            /// <summary>The column of the package table that fills the field.</summary>
            public string Source { get; }
            public string Title { get; }
        }

        public const string VerifyText = "Verify: this layout must be checked against the current specification of the social-security list software before the first official submission.";

        public static readonly Field[] Workshop =
        {
            new Field("DSK_ID", 10, false, "WorkshopCode", "کد کارگاه"),
            new Field("DSK_NAME", 40, false, "WorkshopName", "نام کارگاه"),
            new Field("DSK_FARM", 40, false, "EmployerName", "نام کارفرما"),
            new Field("DSK_ADRS", 80, false, "Address", "نشانی"),
            new Field("DSK_YY", 4, true, "Year", "سال"),
            new Field("DSK_MM", 2, true, "Month", "ماه"),
            new Field("DSK_LISTNO", 3, true, "=1", "شماره لیست"),
            new Field("DSK_NUM", 6, true, "Employees", "تعداد بیمه‌شدگان"),
            new Field("DSK_TDD", 7, true, "TotalDays", "جمع روزهای کارکرد"),
            new Field("DSK_TROOZ", 14, true, "TotalDailyWage", "جمع دستمزد روزانه"),
            new Field("DSK_TMAH", 14, true, "TotalMonthlyWage", "جمع دستمزد ماهانه"),
            new Field("DSK_TMAZ", 14, true, "TotalBenefits", "جمع مزایای ماهانه"),
            new Field("DSK_TMASH", 14, true, "TotalInsurable", "جمع دستمزد و مزایای مشمول"),
            new Field("DSK_TBIME", 14, true, "TotalEmployeeShare", "جمع حق بیمه سهم بیمه‌شده"),
            new Field("DSK_TKOSO", 14, true, "TotalEmployerShare", "جمع حق بیمه سهم کارفرما"),
            new Field("DSK_BIC", 14, true, "TotalUnemployment", "جمع بیمه بیکاری"),
        };

        public static readonly Field[] Worker =
        {
            new Field("DSW_ID", 10, false, "=WorkshopCode", "کد کارگاه"),
            new Field("DSW_YY", 4, true, "=Year", "سال"),
            new Field("DSW_MM", 2, true, "=Month", "ماه"),
            new Field("DSW_LISTNO", 3, true, "=1", "شماره لیست"),
            new Field("DSW_RN", 5, true, "RowNumber", "ردیف"),
            new Field("DSW_ID1", 10, false, "InsuranceNumber", "شماره بیمه"),
            new Field("DSW_NID", 10, false, "NationalCode", "کد ملی"),
            new Field("DSW_FNAME", 30, false, "FirstName", "نام"),
            new Field("DSW_LNAME", 30, false, "LastName", "نام خانوادگی"),
            new Field("DSW_DNAME", 30, false, "FatherName", "نام پدر"),
            new Field("DSW_IDNO", 10, false, "IdentityCardNumber", "شماره شناسنامه"),
            new Field("DSW_IDATE", 10, false, "BirthDate", "تاریخ تولد"),
            new Field("DSW_SEX", 1, false, "GenderCode", "جنسیت (۱ مرد، ۲ زن)"),
            new Field("DSW_NAT", 20, false, "Nationality", "تابعیت"),
            new Field("DSW_OCP", 10, false, "JobCode", "کد شغل"),
            new Field("DSW_SDATE", 10, false, "ContractStart", "تاریخ شروع به کار"),
            new Field("DSW_EDATE", 10, false, "ContractEnd", "تاریخ ترک کار"),
            new Field("DSW_DD", 2, true, "Days", "روزهای کارکرد"),
            new Field("DSW_ROOZ", 12, true, "DailyWage", "دستمزد روزانه"),
            new Field("DSW_MAH", 14, true, "MonthlyWage", "دستمزد ماهانه"),
            new Field("DSW_MAZ", 14, true, "Benefits", "مزایای ماهانه مشمول"),
            new Field("DSW_MASH", 14, true, "InsurableTotal", "جمع دستمزد و مزایای مشمول"),
            new Field("DSW_BIME", 14, true, "EmployeeShare", "حق بیمه سهم بیمه‌شده"),
        };

        public static int Width(IEnumerable<Field> fields) => fields.Sum(f => f.Width);

        /// <summary>Text is cut or padded with spaces on the right; a number is written without separators and padded with zeros on the left (a negative number is written as zero).</summary>
        public static string Format(Field field, string text)
        {
            string value = text ?? string.Empty;
            if (field.Numeric)
            {
                decimal number = decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal d) ? Math.Max(0m, Math.Round(d, 0, MidpointRounding.AwayFromZero)) : 0m;
                string digits = number.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                return digits.Length >= field.Width ? digits.Substring(digits.Length - field.Width) : digits.PadLeft(field.Width, '0');
            }
            value = value.Replace("\r", " ").Replace("\n", " ");
            return value.Length >= field.Width ? value.Substring(0, field.Width) : value.PadRight(field.Width, ' ');
        }

        private static string Record(Field[] fields, Func<Field, string> valueOf) => string.Concat(fields.Select(f => Format(f, valueOf(f))));

        /// <summary>The workshop file, the workers file and the description of the layout, from the stored tables of an insurance package.</summary>
        public static (string Workshop, string Workers, string Description) Build(List<OutboxTableJson> tables)
        {
            OutboxTableJson header = OutboxTables.Find(tables, "Workshop"), workers = OutboxTables.Find(tables, "Workers");
            var headRow = header != null && header.Rows.Count > 0 ? header.Rows[0] : new List<JsonElement>();
            string Head(string column) => OutboxTables.Text(headRow, OutboxTables.Col(header, column));
            string workshopCode = Head("WorkshopCode"), year = Head("Year"), month = Head("Month");

            string kar = Record(Workshop, f => f.Source.StartsWith("=", StringComparison.Ordinal) ? f.Source.Substring(1) : Head(f.Source)) + "\r\n";
            var wor = new StringBuilder();
            if (workers != null)
                foreach (List<JsonElement> row in workers.Rows)
                {
                    wor.Append(Record(Worker, f =>
                    {
                        if (f.Source == "=WorkshopCode") return workshopCode;
                        if (f.Source == "=Year") return year;
                        if (f.Source == "=Month") return month;
                        if (f.Source.StartsWith("=", StringComparison.Ordinal)) return f.Source.Substring(1);
                        if (f.Source == "GenderCode") return PayrollListsR1.GenderCode(OutboxTables.Text(row, OutboxTables.Col(workers, "Gender")));
                        return OutboxTables.Text(row, OutboxTables.Col(workers, f.Source));
                    }));
                    wor.Append("\r\n");
                }

            var text = new StringBuilder();
            text.AppendLine(VerifyText);
            text.AppendLine("File format: UTF-8 text, one record per line (CRLF), fixed-width fields without separators. Text is padded with spaces on the right, numbers with zeros on the left. Dates are Persian (yyyy/mm/dd).");
            foreach ((string name, Field[] fields) in new[] { ("DSKKAR.TXT (one record: the workshop and the totals)", Workshop), ("DSKWOR.TXT (one record for each insured person)", Worker) })
            {
                text.AppendLine();
                text.AppendLine(name + " - record width " + Width(fields));
                int start = 1;
                foreach (Field f in fields)
                {
                    text.AppendLine($"{start,4}  {f.Width,3}  {(f.Numeric ? "N" : "C")}  {f.Name,-11} {(f.Source.StartsWith("=", StringComparison.Ordinal) ? "constant/" + f.Source.Substring(1) : f.Source)}  # {f.Title}");
                    start += f.Width;
                }
            }
            return (kar, wor.ToString(), text.ToString());
        }

        /// <summary>The zip that the export returns.</summary>
        public static byte[] Archive(List<OutboxTableJson> tables)
        {
            (string kar, string wor, string description) = Build(tables);
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    void Add(string name, string content)
                    {
                        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);   // the same package always gives the same bytes
                        using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false))) writer.Write(content);
                    }
                    Add("DSKKAR.TXT", kar);
                    Add("DSKWOR.TXT", wor);
                    Add("LAYOUT.TXT", description);
                }
                return stream.ToArray();
            }
        }
    }

    public partial class SepidarOutboxService
    {
        public async Task<ResponseDto<byte[]>> ExportDskAsync(int id, CancellationToken ct)
        {
            FnOutboxPackage p = await _uow.Set<FnOutboxPackage>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p == null) return ResponseDtoHelper.NotFound<byte[]>("بسته پیدا نشد.");
            if (!CanSee(p.Kind)) return Bad<byte[]>(NotAllowed);
            if (p.Kind != FnOutboxKind.PayrollInsuranceList) return Bad<byte[]>("فایل متنی لیست بیمه فقط برای بسته لیست بیمه ساخته می‌شود.");
            if (!p.WorkshopId.HasValue) return Bad<byte[]>("فایل متنی لیست بیمه برای بسته یک کارگاه ساخته می‌شود؛ بسته را برای یک کارگاه تولید کنید.");
            if (p.Status == FnOutboxStatus.Draft) return Bad<byte[]>("بسته هنوز تایید نشده است.");
            if (p.Status == FnOutboxStatus.Superseded) return Bad<byte[]>("این بسته با نسخه جدیدتر جایگزین شده است.");
            byte[] file = DskLayout.Archive(OutboxTables.Parse(p.PayloadJson));
            await MarkExportedAsync(p, ct);
            return ResponseDtoHelper.Ok(file);
        }
    }
}
