using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.OfficeDtos;

namespace Services.NasbNiro.Office
{
    /// <summary>
    /// Import of the Excel export of the time-attendance software (PW Kara; devices US 10C and M Face 302) into the clock events, through a column mapping the admin chooses
    /// and can save as a named profile. ★ No real export file was available when this was written: the mapping is free on purpose (any column order, a date and a time column or one date-time column,
    /// Persian or Gregorian dates, optional direction and device columns) and must be confirmed on the first real file.
    /// </summary>
    public partial class AttendanceService
    {
        private const int MaxExcelBytes = 20 * 1024 * 1024;
        private const string DefaultExcelDevice = "PWKARA";

        private static readonly JsonSerializerOptions MappingJson = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private static ClockFileMappingDto ReadMapping(string json)
        {
            try { return JsonSerializer.Deserialize<ClockFileMappingDto>(json ?? "{}", MappingJson) ?? new ClockFileMappingDto(); }
            catch (JsonException) { return new ClockFileMappingDto(); }
        }

        // ---- profiles

        private static ClockImportProfileDto ToDto(OfClockImportProfile p) => new ClockImportProfileDto { Id = p.Id, Name = p.Name, Mapping = ReadMapping(p.MappingJson) };

        public async Task<ResponseDto<List<ClockImportProfileDto>>> GetImportProfilesAsync(CancellationToken ct)
        {
            if (!IsHr && !CanReadAll) return Bad<List<ClockImportProfileDto>>(OfficeText.NotAllowed);
            List<OfClockImportProfile> rows = await _uow.Set<OfClockImportProfile>().AsNoTracking().Where(p => !p.Deleted).OrderBy(p => p.Name).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(x => ToDto(x)).ToList());
        }

        /// <summary>Why a mapping cannot be used, or null. The columns are checked against the header row when there is one.</summary>
        internal static string CheckMapping(ClockFileMappingDto m, XlsxRow header, out ColumnMap map)
        {
            map = new ColumnMap();
            if (m == null) return OfficeText.InvalidData;
            if (m.HeaderRow < 0 || m.HeaderRow > 1000 || m.FirstDataRow < 0 || m.FirstDataRow > 1000000) return "ردیف عنوان یا ردیف شروع داده نامعتبر است.";
            int Resolve(string reference, string label, out string error)
            {
                error = null;
                string r = Clean(reference);
                if (r == null) return -1;
                int found = ResolveColumn(r, header);
                if (found < 0) { error = "ستون «" + r + "» برای " + label + " پیدا نشد (حرف ستون مثل A یا متن عنوان ستون را بنویسید)."; return -2; }
                return found;
            }
            string e;
            map.Code = Resolve(m.CodeColumn, "شماره پرسنلی", out e); if (e != null) return e;
            map.Date = Resolve(m.DateColumn, "تاریخ", out e); if (e != null) return e;
            map.Time = Resolve(m.TimeColumn, "ساعت", out e); if (e != null) return e;
            map.DateTime = Resolve(m.DateTimeColumn, "تاریخ و ساعت", out e); if (e != null) return e;
            map.Direction = Resolve(m.DirectionColumn, "ورود/خروج", out e); if (e != null) return e;
            map.Device = Resolve(m.DeviceColumn, "دستگاه", out e); if (e != null) return e;
            map.Site = Resolve(m.SiteColumn, "محل", out e); if (e != null) return e;
            if (map.Code < 0) return "ستون شماره پرسنلی (یا شماره کارت) را مشخص کنید.";
            if (!(map.Date >= 0 && map.Time >= 0) && map.DateTime < 0) return "ستون‌های تاریخ و ساعت (یا یک ستون تاریخ و ساعت) را مشخص کنید.";
            return null;
        }

        internal sealed class ColumnMap
        {
            public int Code = -1, Date = -1, Time = -1, DateTime = -1, Direction = -1, Device = -1, Site = -1;
        }

        /// <summary>A column reference is the text of a heading cell, else a column letter.</summary>
        internal static int ResolveColumn(string reference, XlsxRow header)
        {
            string r = Clean(reference);
            if (r == null) return -1;
            if (header != null)
                foreach (var cell in header.Cells.OrderBy(c => c.Key))
                    if (string.Equals((cell.Value.AsText() ?? "").Trim(), r, StringComparison.OrdinalIgnoreCase)) return cell.Key;
            return XlsxReader.ColumnIndex(r);
        }

        public async Task<ResponseDto<ClockImportProfileDto>> SaveImportProfileAsync(SaveClockImportProfileDto dto, CancellationToken ct)
        {
            if (!IsHr) return Bad<ClockImportProfileDto>(OfficeText.NotAllowed);
            if (dto == null) return Bad<ClockImportProfileDto>(OfficeText.InvalidData);
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Delete)
            {
                OfClockImportProfile gone = dto.Id.HasValue ? await _uow.Set<OfClockImportProfile>().FirstOrDefaultAsync(p => p.Id == dto.Id.Value && !p.Deleted, ct) : null;
                if (gone == null) return OfficeText.NotFound<ClockImportProfileDto>("الگو پیدا نشد.");
                gone.Delete(); gone.ModifyDate = now; gone.ModifierUserId = Me;
                await _uow.SaveChangesAsync(ct);
                return ResponseDtoHelper.Ok(ToDto(gone));
            }
            string name = Clean(dto.Name);
            if (name == null || name.Length > 200) return Bad<ClockImportProfileDto>("نام الگو را وارد کنید (مثلاً PW Kara).");
            // the heading texts can only be checked against a file: here the shape is (the columns are checked again at every import)
            if (dto.Mapping == null || !HasRequiredColumns(dto.Mapping)) return Bad<ClockImportProfileDto>("ستون‌های شماره پرسنلی، تاریخ و ساعت (یا تاریخ و ساعت در یک ستون) را مشخص کنید.");
            if (dto.Mapping.HeaderRow < 0 || dto.Mapping.HeaderRow > 1000 || dto.Mapping.FirstDataRow < 0 || dto.Mapping.FirstDataRow > 1000000) return Bad<ClockImportProfileDto>(OfficeText.InvalidData);
            OfClockImportProfile p;
            if (dto.Id.HasValue)
            {
                p = await _uow.Set<OfClockImportProfile>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (p == null) return OfficeText.NotFound<ClockImportProfileDto>("الگو پیدا نشد.");
            }
            else
            {
                p = new OfClockImportProfile { CreateDate = now, CreatorUserId = Me };
                _uow.Set<OfClockImportProfile>().Add(p);
            }
            if (await _uow.Set<OfClockImportProfile>().AnyAsync(x => !x.Deleted && x.Name == name && x.Id != p.Id, ct)) return Bad<ClockImportProfileDto>("الگوی دیگری با همین نام وجود دارد.");
            p.Name = name; p.MappingJson = JsonSerializer.Serialize(dto.Mapping); p.ModifyDate = now; p.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<ClockImportProfileDto>("ذخیره الگو انجام نشد؛ دوباره تلاش کنید."); }
            return ResponseDtoHelper.Ok(ToDto(p));
        }

        private static bool HasRequiredColumns(ClockFileMappingDto m) =>
            Clean(m.CodeColumn) != null && ((Clean(m.DateColumn) != null && Clean(m.TimeColumn) != null) || Clean(m.DateTimeColumn) != null);

        // ---- preview

        private static readonly (string Field, string[] Words)[] HeaderWords =
        {
            ("datetime", new[] { "تاریخ و ساعت", "تاریخ وساعت", "date time", "datetime", "date/time", "زمان تردد" }),
            ("code", new[] { "کد پرسنلی", "شماره پرسنلی", "پرسنلی", "شماره کارت", "کد کارت", "کارت", "شماره", "personnel", "employee", "emp", "card", "badge", "user id", "userid", "no.", "کد" }),
            ("date", new[] { "تاریخ", "date", "روز" }),
            ("time", new[] { "ساعت", "زمان", "time", "hour" }),
            ("direction", new[] { "ورود/خروج", "ورود و خروج", "نوع تردد", "جهت", "direction", "in/out", "state", "status", "نوع", "type" }),
            ("device", new[] { "دستگاه", "ترمینال", "device", "terminal", "machine", "reader" }),
            ("site", new[] { "محل", "مکان", "واحد", "site", "location", "place" }),
        };

        private static string Guess(string header)
        {
            string h = (header ?? "").Trim().ToLowerInvariant();
            if (h.Length == 0) return null;
            foreach (var group in HeaderWords)
                foreach (string w in group.Words)
                    if (h.Contains(w)) return group.Field;
            return null;
        }

        public Task<ResponseDto<ClockFilePreviewDto>> PreviewClockFileAsync(byte[] file, string sheet, CancellationToken ct) => Task.FromResult(PreviewClockFile(file, sheet));

        private ResponseDto<ClockFilePreviewDto> PreviewClockFile(byte[] file, string sheet)
        {
            if (!IsHr) return Bad<ClockFilePreviewDto>(OfficeText.NotAllowed);
            if (file == null || file.Length == 0) return Bad<ClockFilePreviewDto>("فایل اکسل را انتخاب کنید.");
            if (file.Length > MaxExcelBytes) return Bad<ClockFilePreviewDto>("حجم فایل اکسل نباید از ۲۰ مگابایت بیشتر باشد.");
            XlsxSheet data;
            try { data = XlsxReader.Read(file, sheet); }
            catch (XlsxFormatException ex) { return Bad<ClockFilePreviewDto>(ex.Message); }
            catch (System.Xml.XmlException) { return Bad<ClockFilePreviewDto>("محتوای فایل اکسل خوانده نشد."); }

            var dto = new ClockFilePreviewDto { Sheets = data.SheetNames, Sheet = data.Name, RowCount = data.Rows.Count };
            // the heading row: the first of the first ten rows with at least two cells and no number, date or time in them
            XlsxRow header = null;
            foreach (XlsxRow r in data.Rows.Take(10))
            {
                if (r.Cells.Count < 2) continue;
                bool allText = r.Cells.Values.All(c => !c.Number.HasValue && !ClockCellParser.TryDateText(c.Text, out _) && !ClockCellParser.TryTimeText(c.Text, out _));
                if (allText) { header = r; break; }
            }
            foreach (XlsxRow r in data.Rows.Take(15))
                dto.Rows.Add(Enumerable.Range(0, data.MaxColumn).Select(i => DisplayCell(r.Cell(i))).ToList());
            for (int c = 0; c < data.MaxColumn; c++)
            {
                var col = new ClockFileColumnDto { Letter = XlsxReader.ColumnLetter(c), Header = header == null ? null : header.Cell(c)?.AsText()?.Trim() };
                foreach (XlsxRow r in data.Rows.Where(x => header == null || x.RowNumber > header.RowNumber).Take(5))
                {
                    string t = DisplayCell(r.Cell(c));
                    if (t.Length > 0) col.Samples.Add(t);
                }
                dto.Columns.Add(col);
            }
            var suggested = new ClockFileMappingDto { Sheet = data.Name, HeaderRow = header?.RowNumber ?? 0, FirstDataRow = header == null ? (data.Rows.Count > 0 ? data.Rows[0].RowNumber : 1) : header.RowNumber + 1, DefaultDeviceCode = null };
            if (header != null)
                foreach (var cell in header.Cells.OrderBy(x => x.Key))
                {
                    string field = Guess(cell.Value.AsText());
                    string letter = XlsxReader.ColumnLetter(cell.Key);
                    if (field == "code" && suggested.CodeColumn == null) suggested.CodeColumn = letter;
                    else if (field == "date" && suggested.DateColumn == null) suggested.DateColumn = letter;
                    else if (field == "time" && suggested.TimeColumn == null) suggested.TimeColumn = letter;
                    else if (field == "datetime" && suggested.DateTimeColumn == null) suggested.DateTimeColumn = letter;
                    else if (field == "direction" && suggested.DirectionColumn == null) suggested.DirectionColumn = letter;
                    else if (field == "device" && suggested.DeviceColumn == null) suggested.DeviceColumn = letter;
                    else if (field == "site" && suggested.SiteColumn == null) suggested.SiteColumn = letter;
                }
            dto.Suggested = suggested;
            return ResponseDtoHelper.Ok(dto);
        }

        /// <summary>A cell as a person reads it: dates and times as Persian date / hh:mm.</summary>
        private static string DisplayCell(XlsxCell c)
        {
            if (c == null) return "";
            if (c.Number.HasValue && c.IsDateFormat)
            {
                bool hasDate = ClockCellParser.TryDate(c, out DateTime d);
                bool hasTime = ClockCellParser.TryTime(c, out int m);
                if (hasDate && hasTime && m > 0) return OfficeText.Stamp(d.AddMinutes(m));
                if (hasDate) return OfficeText.Date(d);
                if (hasTime) return OfficeText.Minute(m);
            }
            string t = c.AsText() ?? "";
            return t.Length > 80 ? t.Substring(0, 80) : t;
        }

        // ---- import

        public async Task<ResponseDto<ClockImportResultDto>> ImportClockFileAsync(byte[] file, ClockFileImportDto dto, CancellationToken ct)
        {
            if (!IsHr) return Bad<ClockImportResultDto>(OfficeText.NotAllowed);
            if (dto == null) return Bad<ClockImportResultDto>(OfficeText.InvalidData);
            if (file == null || file.Length == 0) return Bad<ClockImportResultDto>("فایل اکسل را انتخاب کنید.");
            if (file.Length > MaxExcelBytes) return Bad<ClockImportResultDto>("حجم فایل اکسل نباید از ۲۰ مگابایت بیشتر باشد.");

            ClockFileMappingDto mapping = dto.Mapping;
            if (mapping == null && dto.ProfileId.HasValue)
            {
                OfClockImportProfile profile = await _uow.Set<OfClockImportProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.ProfileId.Value && !p.Deleted, ct);
                if (profile == null) return OfficeText.NotFound<ClockImportResultDto>("الگوی ستون‌بندی پیدا نشد.");
                mapping = ReadMapping(profile.MappingJson);
            }
            if (mapping == null) return Bad<ClockImportResultDto>("ستون‌بندی فایل یا یک الگوی ذخیره‌شده را انتخاب کنید.");
            string saveAs = Clean(dto.SaveProfileAs);
            if (saveAs != null && saveAs.Length > 200) return Bad<ClockImportResultDto>("نام الگو بیش از حد طولانی است.");

            XlsxSheet data;
            try { data = XlsxReader.Read(file, mapping.Sheet); }
            catch (XlsxFormatException ex) { return Bad<ClockImportResultDto>(ex.Message); }
            catch (System.Xml.XmlException) { return Bad<ClockImportResultDto>("محتوای فایل اکسل خوانده نشد."); }

            XlsxRow header = mapping.HeaderRow > 0 ? data.Rows.FirstOrDefault(r => r.RowNumber == mapping.HeaderRow) : null;
            string mappingError = CheckMapping(mapping, header, out ColumnMap map);
            if (mappingError != null) return Bad<ClockImportResultDto>(mappingError);
            int firstData = mapping.FirstDataRow > 0 ? mapping.FirstDataRow : (mapping.HeaderRow > 0 ? mapping.HeaderRow + 1 : 1);

            // personnel numbers as the file writes them -> the personnel code of the employee (leading zeros ignored); an ambiguous number stays unmatched and is reported
            var byKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            List<string> personnelCodes = await _uow.Set<FnEmployee>().AsNoTracking().Where(e => e.PersonnelCode != null).Select(e => e.PersonnelCode).ToListAsync(ct);
            foreach (string pc in personnelCodes)
            {
                string key = ClockCellParser.CodeKey(pc);
                if (!byKey.TryGetValue(key, out List<string> list)) byKey[key] = list = new List<string>();
                if (!list.Contains(pc)) list.Add(pc);
            }
            var personnelSet = new HashSet<string>(personnelCodes, StringComparer.Ordinal);
            List<OfDevice> devices = await _uow.Set<OfDevice>().AsNoTracking().Where(d => !d.Deleted).ToListAsync(ct);
            string fallbackDevice = (Clean(mapping.DefaultDeviceCode) ?? DefaultExcelDevice).ToUpperInvariant();

            var result = new ClockImportResultDto();
            var rows = new List<RawClock>();
            int badRows = 0;
            foreach (XlsxRow row in data.Rows)
            {
                if (row.RowNumber < firstData) continue;
                string codeText = OfficeText.Digits(Clean(row.Cell(map.Code)?.AsText()) ?? "");
                if (codeText.Length == 0 && row.Cells.Count == 0) continue;
                result.RowsRead++;
                if (codeText.Length == 0) { badRows++; AddFileError(result, row.RowNumber, "شماره پرسنلی خالی است."); continue; }
                DateTime when;
                if (map.Date >= 0 && map.Time >= 0)
                {
                    if (!ClockCellParser.TryDate(row.Cell(map.Date), out DateTime date)) { badRows++; AddFileError(result, row.RowNumber, "تاریخ نامعتبر است."); continue; }
                    if (!ClockCellParser.TryTime(row.Cell(map.Time), out int minute)) { badRows++; AddFileError(result, row.RowNumber, "ساعت نامعتبر است."); continue; }
                    when = date.AddMinutes(minute);
                }
                else if (!ClockCellParser.TryDateTime(row.Cell(map.DateTime), out when)) { badRows++; AddFileError(result, row.RowNumber, "تاریخ و ساعت نامعتبر است."); continue; }

                string code = codeText;
                if (!personnelSet.Contains(codeText) && byKey.TryGetValue(ClockCellParser.CodeKey(codeText), out List<string> matches) && matches.Count == 1) code = matches[0];
                string deviceText = Clean(map.Device >= 0 ? row.Cell(map.Device)?.AsText() : null) ?? Clean(map.Site >= 0 ? row.Cell(map.Site)?.AsText() : null);
                string device = fallbackDevice;
                if (deviceText != null)
                {
                    OfDevice known = devices.FirstOrDefault(d => string.Equals(d.Code, deviceText, StringComparison.OrdinalIgnoreCase) || string.Equals(d.Title, deviceText, StringComparison.OrdinalIgnoreCase));
                    device = known != null ? known.Code : deviceText;
                }
                OfClockDirection direction = map.Direction >= 0 ? ClockCellParser.Direction(row.Cell(map.Direction)?.AsText(), mapping.InValues, mapping.OutValues) : OfClockDirection.Unknown;
                rows.Add(new RawClock { Code = code, Timestamp = when, Device = device, Direction = direction, Line = "ردیف " + row.RowNumber });
            }
            if (rows.Count > 20000) return Bad<ClockImportResultDto>("حداکثر ۲۰٬۰۰۰ تردد در هر بار بارگذاری مجاز است.");

            var unknownCodes = new List<string>();
            var (added, duplicates, unknown, rejected) = await StoreAsync(rows, OfClockSource.Import, fallbackDevice, result.Errors, ct, unknownCodes);
            if (saveAs != null) await UpsertProfileAsync(saveAs, mapping, ct);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<ClockImportResultDto>("ذخیره ترددها انجام نشد؛ دوباره تلاش کنید."); }
            result.Imported = added;
            result.Duplicates = duplicates;
            result.Unknown = unknown;
            result.UnknownCodes = unknownCodes;
            result.Rejected = rejected + badRows;
            if (result.Errors.Count > 50) result.Errors = result.Errors.Take(50).ToList();
            if (added > 0) await RecomputeImportedRangeAsync(rows, ct);
            return ResponseDtoHelper.Ok(result);
        }

        private static void AddFileError(ClockImportResultDto result, int rowNumber, string message)
        {
            if (result.Errors.Count < 50) result.Errors.Add($"ردیف {rowNumber}: {message}");
        }

        private async Task UpsertProfileAsync(string name, ClockFileMappingDto mapping, CancellationToken ct)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            OfClockImportProfile p = await _uow.Set<OfClockImportProfile>().FirstOrDefaultAsync(x => !x.Deleted && x.Name == name, ct);
            if (p == null)
            {
                p = new OfClockImportProfile { Name = name, CreateDate = now, CreatorUserId = Me };
                _uow.Set<OfClockImportProfile>().Add(p);
            }
            p.MappingJson = JsonSerializer.Serialize(mapping); p.ModifyDate = now; p.ModifierUserId = Me;
        }

        /// <summary>Recomputes the days around the imported events (in steps of 60 days; a file spanning more than a year is left to the daily job and the month close).</summary>
        private async Task RecomputeImportedRangeAsync(List<RawClock> rows, CancellationToken ct)
        {
            DateTime min = rows.Min(r => r.Timestamp).Date.AddDays(-1);
            DateTime max = rows.Max(r => r.Timestamp).Date.AddDays(1);
            if ((max - min).TotalDays > 400) return;
            for (DateTime from = min; from <= max; from = from.AddDays(60))
                await ComputeCoreAsync(from, from.AddDays(59) < max ? from.AddDays(59) : max, null, ct);
        }
    }
}
