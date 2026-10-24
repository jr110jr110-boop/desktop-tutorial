using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Providers;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.OfficeDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Office
{
    public partial class LetterService
    {
        // -------------------------------------------------------------------------------- external parties

        private static ExternalPartyDto ToDto(OfExternalParty p) => new ExternalPartyDto
        {
            Id = p.Id, Name = p.Name, Kind = p.Kind, ProviderId = p.ProviderId, CustomerId = p.CustomerId, Address = p.Address, Phone = p.Phone, Email = p.Email, IsActive = p.IsActive,
        };

        public async Task<ResponseDto<PagedList<ExternalPartyDto>>> GetPartiesAsync(OfficePageQuery query, CancellationToken ct)
        {
            query ??= new OfficePageQuery();
            IQueryable<OfExternalParty> q = _uow.Set<OfExternalParty>().AsNoTracking().Where(p => !p.Deleted);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(p => p.Name.Contains(text));
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfExternalParty> rows = await q.OrderBy(p => p.Name).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(rows.Select(x => ToDto(x)).ToList(), total, number, size));
        }

        public async Task<ResponseDto<ExternalPartyDto>> SavePartyAsync(SaveExternalPartyDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Name) == null) return Bad<ExternalPartyDto>("نام سازمان یا شخص را وارد کنید.");
            if (!Enum.IsDefined(typeof(OfPartyKind), dto.Kind)) return Bad<ExternalPartyDto>(OfficeText.InvalidData);
            if (dto.ProviderId.HasValue && !await _uow.Set<Provider>().AnyAsync(p => p.Id == dto.ProviderId.Value, ct)) return Bad<ExternalPartyDto>("تامین‌کننده پیدا نشد.");
            if (dto.CustomerId.HasValue && !await _uow.Set<FnCustomer>().AnyAsync(p => p.Id == dto.CustomerId.Value, ct)) return Bad<ExternalPartyDto>("مشتری پیدا نشد.");
            OfExternalParty p;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                p = await _uow.Set<OfExternalParty>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (p == null) return OfficeText.NotFound<ExternalPartyDto>("طرف مکاتبه پیدا نشد.");
            }
            else
            {
                p = new OfExternalParty { CreateDate = now, CreatorUserId = Me };
                _uow.Set<OfExternalParty>().Add(p);
            }
            p.Name = Clean(dto.Name); p.Kind = dto.Kind; p.ProviderId = dto.ProviderId; p.CustomerId = dto.CustomerId; p.Address = Clean(dto.Address); p.Phone = Clean(dto.Phone);
            p.Email = Clean(dto.Email); p.IsActive = dto.IsActive; p.ModifyDate = now; p.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(p));
        }

        // -------------------------------------------------------------------------------- numbering rules

        private async Task<List<NumberingRuleDto>> MapRulesAsync(List<OfNumberingRule> rows, CancellationToken ct)
        {
            List<int> unitIds = rows.Where(r => r.UnitId.HasValue).Select(r => r.UnitId.Value).Distinct().ToList();
            Dictionary<int, string> units = unitIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<OfUnit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Code, ct);
            int year = InventoryText.PersianYear(DateTime.Today);
            List<int> ruleIds = rows.Select(r => r.Id).ToList();
            Dictionary<int, int> last = await _uow.Set<OfNumberCounter>().AsNoTracking().Where(x => ruleIds.Contains(x.RuleId) && x.Year == year).ToDictionaryAsync(x => x.RuleId, x => x.Last, ct);
            return rows.Select(r =>
            {
                string code = r.UnitId.HasValue && units.TryGetValue(r.UnitId.Value, out string uc) ? uc : "CO";
                return new NumberingRuleDto
                {
                    Id = r.Id, Kind = r.Kind, UnitId = r.UnitId, UnitCode = r.UnitId.HasValue ? code : null, Pattern = r.Pattern, IsActive = r.IsActive,
                    Preview = FormatNumber(r.Pattern, year, (last.TryGetValue(r.Id, out int n) ? n : 0) + 1, code, r.Kind),
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<NumberingRuleDto>>> GetRulesAsync(OfficePageQuery query, CancellationToken ct)
        {
            query ??= new OfficePageQuery();
            await EnsureRulesAsync(ct);
            IQueryable<OfNumberingRule> q = _uow.Set<OfNumberingRule>().AsNoTracking().Where(r => !r.Deleted);
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfNumberingRule> rows = await q.OrderBy(r => r.Kind).ThenBy(r => r.UnitId).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await MapRulesAsync(rows, ct), total, number, size));
        }

        public async Task<ResponseDto<NumberingRuleDto>> SaveRuleAsync(SaveNumberingRuleDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(OfficeRoles.LetterAdmins)) return Bad<NumberingRuleDto>(OfficeText.NotAllowed);
            if (dto == null || Clean(dto.Pattern) == null || !Enum.IsDefined(typeof(OfLetterKind), dto.Kind)) return Bad<NumberingRuleDto>(OfficeText.InvalidData);
            string pattern = Clean(dto.Pattern);
            if (!Regex.IsMatch(pattern, @"\{seq(:\d{1,9})?\}")) return Bad<NumberingRuleDto>("الگوی شماره باید شامل {seq:n} (شماره ترتیبی) باشد؛ مثلا {yy}/{kind}/{unit}/{seq:5}.");
            string leftover = Regex.Replace(pattern, @"\{(yyyy|yy|unit|kind|seq(:\d{1,9})?)\}", "");
            if (leftover.Contains("{") || leftover.Contains("}")) return Bad<NumberingRuleDto>("الگوی شماره نشانه ناشناخته دارد. نشانه‌های مجاز: {yyyy} {yy} {seq:n} {unit} {kind}.");
            if (pattern.Length > 200) return Bad<NumberingRuleDto>(OfficeText.InvalidData);
            if (dto.UnitId.HasValue && !await _uow.Set<OfUnit>().AnyAsync(u => u.Id == dto.UnitId.Value && !u.Deleted, ct)) return Bad<NumberingRuleDto>("واحد پیدا نشد.");
            await EnsureRulesAsync(ct);
            OfNumberingRule r;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<OfNumberingRule>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (r == null) return OfficeText.NotFound<NumberingRuleDto>("قاعده پیدا نشد.");
                if (r.Kind != dto.Kind || r.UnitId != dto.UnitId) return Bad<NumberingRuleDto>("نوع و واحد قاعده پس از ایجاد تغییر نمی‌کند؛ قاعده جدید بسازید.");
            }
            else
            {
                if (await _uow.Set<OfNumberingRule>().AnyAsync(x => !x.Deleted && x.IsActive && x.Kind == dto.Kind && x.UnitId == dto.UnitId, ct) && dto.IsActive)
                    return Bad<NumberingRuleDto>("برای این نوع نامه و واحد قاعده فعال دیگری وجود دارد؛ آن را ویرایش کنید.");
                r = new OfNumberingRule { Kind = dto.Kind, UnitId = dto.UnitId, CreateDate = now, CreatorUserId = Me };
                _uow.Set<OfNumberingRule>().Add(r);
            }
            r.Pattern = pattern; r.IsActive = dto.IsActive; r.ModifyDate = now; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await MapRulesAsync(new List<OfNumberingRule> { r }, ct))[0]);
        }

        // -------------------------------------------------------------------------------- templates

        private static LetterTemplateDto ToDto(OfLetterTemplate t) => new LetterTemplateDto
        {
            Id = t.Id, Title = t.Title, LetterKind = t.LetterKind, HeaderHtml = t.HeaderHtml, FooterHtml = t.FooterHtml, BodyHtml = t.BodyHtml, ApprovalChain = OfficeText.Ids(t.ApprovalChain),
            SignerPositionId = t.SignerPositionId, IsActive = t.IsActive,
        };

        public async Task<ResponseDto<PagedList<LetterTemplateDto>>> GetTemplatesAsync(OfficePageQuery query, CancellationToken ct)
        {
            query ??= new OfficePageQuery();
            IQueryable<OfLetterTemplate> q = _uow.Set<OfLetterTemplate>().AsNoTracking().Where(t => !t.Deleted);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(t => t.Title.Contains(text));
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfLetterTemplate> rows = await q.OrderBy(t => t.Title).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(rows.Select(x => ToDto(x)).ToList(), total, number, size));
        }

        public async Task<ResponseDto<LetterTemplateDto>> SaveTemplateAsync(SaveLetterTemplateDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(OfficeRoles.LetterAdmins)) return Bad<LetterTemplateDto>(OfficeText.NotAllowed);
            if (dto == null || Clean(dto.Title) == null) return Bad<LetterTemplateDto>("عنوان قالب را وارد کنید.");
            List<int> chain = (dto.ApprovalChain ?? new List<int>()).Distinct().ToList();
            if (chain.Count > 0 && await _uow.Set<OfPosition>().CountAsync(p => chain.Contains(p.Id) && !p.Deleted, ct) != chain.Count) return Bad<LetterTemplateDto>("یکی از سمت‌های زنجیره تایید پیدا نشد.");
            if (dto.SignerPositionId.HasValue && !await _uow.Set<OfPosition>().AnyAsync(p => p.Id == dto.SignerPositionId.Value && !p.Deleted && p.CanSign, ct)) return Bad<LetterTemplateDto>("سمت امضاکننده مجاز به امضا نیست.");
            OfLetterTemplate t;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                t = await _uow.Set<OfLetterTemplate>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (t == null) return OfficeText.NotFound<LetterTemplateDto>("قالب پیدا نشد.");
            }
            else
            {
                t = new OfLetterTemplate { CreateDate = now, CreatorUserId = Me };
                _uow.Set<OfLetterTemplate>().Add(t);
            }
            t.Title = Clean(dto.Title); t.LetterKind = dto.LetterKind; t.HeaderHtml = OfficeHtml.Sanitize(dto.HeaderHtml); t.FooterHtml = OfficeHtml.Sanitize(dto.FooterHtml);
            t.BodyHtml = OfficeHtml.Sanitize(dto.BodyHtml); t.ApprovalChain = string.Join(",", chain); t.SignerPositionId = dto.SignerPositionId; t.IsActive = dto.IsActive;
            t.ModifyDate = now; t.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(t));
        }

        // -------------------------------------------------------------------------------- legacy archive import

        private static DateTime? ParseAnyDate(string text)
        {
            string s = OfficeText.Digits(Clean(text));
            if (s == null) return null;
            Match m = Regex.Match(s, @"^(\d{4})[/\-.](\d{1,2})[/\-.](\d{1,2})");
            if (m.Success)
            {
                int y = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), mo = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), d = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                try
                {
                    if (y < 1700) return new PersianCalendar().ToDateTime(y, mo, d, 0, 0, 0, 0).Date;
                    return new DateTime(y, mo, d);
                }
                catch (ArgumentException) { return null; }
            }
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed) ? parsed.Date : (DateTime?)null;
        }

        private static OfLetterKind? ParseKind(string text)
        {
            string s = (Clean(text) ?? "").ToLowerInvariant();
            if (s == "incoming" || s == "in" || s == "وارده") return OfLetterKind.Incoming;
            if (s == "outgoing" || s == "out" || s == "صادره") return OfLetterKind.Outgoing;
            if (s == "internal" || s == "داخلی") return OfLetterKind.Internal;
            return null;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var cells = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else if (ch == '"') quoted = false;
                    else cur.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',' || ch == '،' || ch == ';' || ch == '\t') { cells.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(ch);
            }
            cells.Add(cur.ToString());
            return cells;
        }

        public async Task<ResponseDto<ImportLettersResultDto>> ImportAsync(ImportLettersDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(OfficeRoles.Secretariat)) return Bad<ImportLettersResultDto>(OfficeText.NotAllowed);
            if (dto == null) return Bad<ImportLettersResultDto>(OfficeText.InvalidData);
            var rows = new List<ImportLetterRowDto>(dto.Rows ?? new List<ImportLetterRowDto>());
            if (Clean(dto.Csv) != null)
            {
                bool first = true;
                foreach (string raw in dto.Csv.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    List<string> cells = SplitCsvLine(raw);
                    if (cells.Count < 4) continue;
                    if (first && ParseAnyDate(cells[1]) == null) { first = false; continue; }
                    first = false;
                    rows.Add(new ImportLetterRowDto
                    {
                        Number = cells[0], Date = cells[1], Kind = cells[2], Subject = cells[3], Party = cells.Count > 4 ? cells[4] : null,
                        FileNames = cells.Count > 5 ? cells[5].Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToList() : new List<string>(),
                    });
                }
            }
            if (rows.Count == 0) return Bad<ImportLettersResultDto>("ردیفی برای ورود ارسال نشده است.");
            if (rows.Count > 2000) return Bad<ImportLettersResultDto>("حداکثر ۲۰۰۰ ردیف در هر بار.");
            var result = new ImportLettersResultDto();
            await OfficeActors.EnsureRootUnitAsync(_uow, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            var parties = new Dictionary<string, OfExternalParty>();
            var seen = new HashSet<string>();
            var added = new List<OfLetter>();
            int line = 0;
            foreach (ImportLetterRowDto row in rows)
            {
                line++;
                string number = Clean(row.Number);
                DateTime? date = ParseAnyDate(row.Date);
                OfLetterKind? kind = ParseKind(row.Kind);
                string subject = Clean(row.Subject);
                if (number == null || number.Length > 200) { result.Errors.Add($"ردیف {line}: شماره نامه نامعتبر است."); continue; }
                if (!date.HasValue) { result.Errors.Add($"ردیف {line}: تاریخ نامعتبر است."); continue; }
                if (!kind.HasValue) { result.Errors.Add($"ردیف {line}: نوع نامه (وارده، صادره، داخلی) نامعتبر است."); continue; }
                if (subject == null) { result.Errors.Add($"ردیف {line}: موضوع خالی است."); continue; }
                string identity = (int)kind.Value + "|" + number;
                if (!seen.Add(identity) || await _uow.Set<OfLetter>().AnyAsync(l => !l.Deleted && l.Imported && l.Kind == kind.Value && l.Number == number, ct))
                {
                    result.Skipped++;
                    continue;
                }
                var documentIds = (row.DocumentIds ?? new List<Guid>()).Where(g => g != Guid.Empty).Distinct().ToList();
                if (!await DocumentsExistAsync(documentIds, ct)) { result.Errors.Add($"ردیف {line}: فایل پیوست پیدا نشد."); continue; }
                int? partyId = null;
                string partyName = Clean(row.Party);
                if (partyName != null)
                {
                    if (!parties.TryGetValue(partyName, out OfExternalParty party))
                    {
                        party = await _uow.Set<OfExternalParty>().FirstOrDefaultAsync(p => !p.Deleted && p.Name == partyName, ct);
                        if (party == null)
                        {
                            party = new OfExternalParty { Name = partyName, Kind = OfPartyKind.Organisation, IsActive = true, CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me };
                            _uow.Set<OfExternalParty>().Add(party);
                            await _uow.SaveChangesAsync(ct);
                        }
                        parties[partyName] = party;
                    }
                    partyId = party.Id;
                }
                var letter = new OfLetter
                {
                    Kind = kind.Value, Number = number, Imported = true, Date = date.Value, Subject = subject, Status = OfLetterStatus.Archived, Classification = OfClassification.Normal,
                    RegisteredAt = now, RegisteredByUserId = Me, CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
                    Recipients = new List<OfLetterRecipient>(), Approvals = new List<OfLetterApproval>(), Links = new List<OfRecordLink>(), Referrals = new List<OfReferral>(),
                    Attachments = new List<OfLetterAttachment>(),
                };
                for (int i = 0; i < documentIds.Count; i++)
                    letter.Attachments.Add(new OfLetterAttachment { DocumentId = documentIds[i], FileName = (row.FileNames != null && i < row.FileNames.Count) ? Clean(row.FileNames[i]) : null });
                if (partyId.HasValue && kind.Value == OfLetterKind.Incoming) letter.SenderPartyId = partyId;
                else if (partyId.HasValue) letter.Recipients.Add(new OfLetterRecipient { Type = OfRecipientType.To, PartyId = partyId });
                _uow.Set<OfLetter>().Add(letter);
                added.Add(letter);
            }
            if (added.Count > 0)
            {
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException) { return Bad<ImportLettersResultDto>("ورود آرشیو انجام نشد؛ داده‌ها را بررسی کنید."); }
            }
            result.Imported = added.Count;
            result.LetterIds = added.Select(l => l.Id).ToList();
            return ResponseDtoHelper.Ok(result);
        }
    }
}
