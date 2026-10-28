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
            // R1a2 (C1): the correspondents list was open to every signed-in user; only the secretariat and the PMO keep it (the reading above stays open)
            if (!_user.IsInRole(OfficeRoles.PartyEditors)) return Bad<ExternalPartyDto>(OfficeText.NotAllowed);
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
                    Preview = FormatNumber(r.Pattern, year, (last.TryGetValue(r.Id, out int n) ? n : 0) + 1, code, r.Kind, null),
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<NumberingRuleDto>>> GetRulesAsync(OfficePageQuery query, CancellationToken ct)
        {
            query ??= new OfficePageQuery();
            // a read: the default rules are seeded at start-up (OfficeHostedService) and on the write paths, never here
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
            if (!Regex.IsMatch(pattern, @"\{seq(:\d{1,9})?\}")) return Bad<NumberingRuleDto>("الگوی شماره باید شامل {seq} (شماره ترتیبی) باشد؛ مثلا INN-{code}{yyyy}-{seq}.");
            string leftover = Regex.Replace(pattern, @"\{(yyyy|yy|unit|kind|code|seq(:\d{1,9})?)\}", "");
            if (leftover.Contains("{") || leftover.Contains("}")) return Bad<NumberingRuleDto>("الگوی شماره نشانه ناشناخته دارد. نشانه‌های مجاز: {yyyy} {yy} {seq} یا {seq:n} {unit} {kind} {code}.");
            if (pattern.Length > 200) return Bad<NumberingRuleDto>(OfficeText.InvalidData);
            if (dto.UnitId.HasValue && !await _uow.Set<OfUnit>().AnyAsync(u => u.Id == dto.UnitId.Value && !u.Deleted, ct)) return Bad<NumberingRuleDto>("واحد پیدا نشد.");
            await EnsureRulesAsync(ct);
            if (dto.IsActive)
            {
                string clash = await RuleCollisionAsync(dto.Kind, dto.UnitId, pattern, dto.Id, ct);
                if (clash != null) return Bad<NumberingRuleDto>(clash);
            }
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

        /// <summary>The pattern with every token replaced by a marker, so two patterns that produce the same numbers have the same shape. The unit token is written out for the unit being compared.</summary>
        private static string Shape(string pattern, string unitCode, OfLetterKind kind) => Shape(pattern, unitCode, kind, false);

        /// <summary>
        /// ★ The collision check is advisory: the number really issued is always checked against the numbers already taken (NextNumberAsync skips a taken number, and the unique index of
        /// kind and number backs it), so a collision this check misses costs a skipped number, never a duplicate.
        /// With <paramref name="codeMarker"/> the {code} token is written as a marker for "a tender or project code and its dash, or nothing".
        /// </summary>
        private static string Shape(string pattern, string unitCode, OfLetterKind kind, bool codeMarker) =>
            Regex.Replace(pattern, @"\{(yyyy|yy|unit|kind|code|seq(?::\d{1,9})?)\}", m =>
            {
                string t = m.Groups[1].Value;
                if (t == "yyyy") return "\u0001Y4";
                if (t == "yy") return "\u0001Y2";
                // the code is optional (a letter without a link prints nothing), so a pattern with {code} can produce the numbers of the same pattern without it: compared as if it were absent,
                // or (codeMarker) as a marker that CouldCollide expands to any code
                if (t == "code") return codeMarker ? "\u0002" : "";
                if (t == "unit") return unitCode ?? "CO";
                if (t == "kind") return KindCode(kind);
                return "\u0001S";
            });

        /// <summary>True when the numbers of two shapes (with the code marker) can be equal: the same shape, or one of them has {code} and the other equals it for some tender / project code (letters, digits, dashes).</summary>
        internal static bool CouldCollide(string a, string b)
        {
            if (a == b) return true;
            bool ac = a.Contains('\u0002'), bc = b.Contains('\u0002');
            if (ac == bc) return false;
            string withCode = ac ? a : b, plain = ac ? b : a;
            string regex = "^" + string.Join("(?:[A-Za-z0-9-]{1,40}-)?", withCode.Split('\u0002').Select(Regex.Escape)) + "$";
            return Regex.IsMatch(plain, regex, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// Two active rules of one kind must not be able to issue the same number (the second would hit the unique index for good). A rule is refused when its pattern has the same shape as another active
        /// rule of the kind, with the unit token written out as the code of the unit the two are compared for. A literal part that the other rule cannot produce keeps them apart.
        /// </summary>
        private async Task<string> RuleCollisionAsync(OfLetterKind kind, int? unitId, string pattern, int? ignoreId, CancellationToken ct)
        {
            List<OfNumberingRule> others = await _uow.Set<OfNumberingRule>().AsNoTracking().Where(x => !x.Deleted && x.IsActive && x.Kind == kind && (ignoreId == null || x.Id != ignoreId.Value)).ToListAsync(ct);
            if (others.Count == 0) return null;
            var unitIds = new List<int>();
            if (unitId.HasValue) unitIds.Add(unitId.Value);
            unitIds.AddRange(others.Where(o => o.UnitId.HasValue).Select(o => o.UnitId.Value));
            Dictionary<int, string> codes = await _uow.Set<OfUnit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Code, ct);
            string CodeOf(int? id) => id.HasValue && codes.TryGetValue(id.Value, out string c) ? c : "CO";
            foreach (OfNumberingRule o in others)
            {
                string mine, theirs;
                if (unitId.HasValue && o.UnitId.HasValue && unitId != o.UnitId) { mine = Shape(pattern, CodeOf(unitId), kind, true); theirs = Shape(o.Pattern, CodeOf(o.UnitId), kind, true); }
                else { string code = CodeOf(unitId ?? o.UnitId); mine = Shape(pattern, code, kind, true); theirs = Shape(o.Pattern, code, kind, true); }
                if (CouldCollide(mine, theirs))
                    return "این الگو می‌تواند شماره‌ای بسازد که قاعده فعال دیگری از همین نوع نامه ساخته است و شماره تکراری می‌دهد. در الگو یک حرف یا عبارت ثابت اضافه کنید (مثلاً INT-{yy}-{seq}).";
            }
            return null;
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

        // -------------------------------------------------------------------------------- legacy archive: one PDF per letter

        private const int MaxArchiveFiles = 300;
        private const long MaxArchiveFileBytes = 50L * 1024 * 1024;

        /// <summary>Compares old letter numbers and file names: Persian digits as Latin, Arabic ي/ك as ی/ک, characters a file name cannot hold as underscore, case ignored.</summary>
        internal static string ArchiveKey(string text)
        {
            string t = OfficeText.Digits(text ?? "").Trim().Replace('ي', 'ی').Replace('ك', 'ک');
            var sb = new System.Text.StringBuilder(t.Length);
            foreach (char ch in t) sb.Append("/\\:*?\"<>|".IndexOf(ch) >= 0 ? '_' : ch);
            return sb.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// The old letter number in a file name: the whole name without extension, or the group «number» (else the first group) of the pattern. Null when the pattern does not match.
        /// </summary>
        internal static string ArchiveNumberOf(string fileName, string pattern)
        {
            string name = fileName ?? "";
            int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
            if (slash >= 0) name = name.Substring(slash + 1);
            int dot = name.LastIndexOf('.');
            if (dot > 0) name = name.Substring(0, dot);
            name = name.Trim();
            if (Clean(pattern) == null) return name.Length == 0 ? null : name;
            Match m = Regex.Match(name, pattern.Trim(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!m.Success) return null;
            Group g = m.Groups["number"];
            if (!g.Success) g = m.Groups.Count > 1 ? m.Groups[1] : m.Groups[0];
            string value = g.Value.Trim();
            return value.Length == 0 ? null : value;
        }

        private static async Task<bool> LooksLikePdfAsync(Microsoft.AspNetCore.Http.IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length < 5) return false;
            using System.IO.Stream stream = file.OpenReadStream();
            var head = new byte[5];
            int read = 0;
            while (read < head.Length)
            {
                int n = await stream.ReadAsync(head, read, head.Length - read, ct);
                if (n == 0) break;
                read += n;
            }
            return read == 5 && head[0] == '%' && head[1] == 'P' && head[2] == 'D' && head[3] == 'F' && head[4] == '-';
        }

        /// <summary>Takes a letter (and its attachments) out of the tracking of the unit of work, so a failed file leaves nothing behind for the next save.</summary>
        private void Detach(OfLetter letter)
        {
            if (letter == null) return;
            foreach (OfLetterAttachment a in letter.Attachments.ToList()) _uow.Entry(a).State = EntityState.Detached;
            _uow.Entry(letter).State = EntityState.Detached;
        }

        /// <summary>A document row that was added but whose save failed would be retried by the next save: forget it.</summary>
        private void DetachPendingDocuments()
        {
            foreach (var d in _uow.Set<Entities.Model.Document>().Local.ToList())
                if (_uow.Entry(d).State == EntityState.Added) _uow.Entry(d).State = EntityState.Detached;
        }

        /// <summary>
        /// Attaches the PDFs of the Rahkaran export, one per imported letter, matched by file name. Every file is reported on its own line (attached, already-attached, unmatched, ambiguous, not-pdf, invalid).
        /// A file of the same name as an existing attachment of the letter is skipped (already-attached), so sending a batch again is safe; a file with a different name for the same letter is added as a second attachment.
        /// </summary>
        public async Task<ResponseDto<ArchiveFilesResultDto>> AttachArchiveFilesAsync(ArchiveFilesDto dto, List<ArchiveFileInput> files, CancellationToken ct)
        {
            if (!_user.IsInRole(OfficeRoles.Secretariat)) return Bad<ArchiveFilesResultDto>(OfficeText.NotAllowed);
            dto ??= new ArchiveFilesDto();
            files ??= new List<ArchiveFileInput>();
            string pattern = Clean(dto.NamePattern);
            if (pattern != null)
            {
                if (pattern.Length > 200) return Bad<ArchiveFilesResultDto>("الگوی نام فایل بیش از حد طولانی است.");
                try { _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); }
                catch (ArgumentException) { return Bad<ArchiveFilesResultDto>("الگوی نام فایل (عبارت منظم) نامعتبر است."); }
            }
            if (files.Count == 0 && Clean(dto.Csv) == null) return Bad<ArchiveFilesResultDto>("فایل PDF یا فهرست نامه‌ها را ارسال کنید.");
            if (files.Count > MaxArchiveFiles) return Bad<ArchiveFilesResultDto>("حداکثر " + MaxArchiveFiles + " فایل در هر بار.");
            var result = new ArchiveFilesResultDto();

            // the metadata list first (the same import as before), so the letters exist when the files are matched
            if (Clean(dto.Csv) != null)
            {
                ResponseDto<ImportLettersResultDto> imported = await ImportAsync(new ImportLettersDto { Csv = dto.Csv }, ct);
                if (!imported.Success) return Bad<ArchiveFilesResultDto>(imported.Message ?? OfficeText.InvalidData);
                result.Import = imported.Data;
            }

            // match: the old number of the file against the imported letters
            var letters = await _uow.Set<OfLetter>().AsNoTracking().Where(l => !l.Deleted && l.Imported).Select(l => new { l.Id, l.Number }).ToListAsync(ct);
            ILookup<string, int> byKey = letters.ToLookup(l => ArchiveKey(l.Number), l => l.Id);
            var numberOf = letters.ToDictionary(l => l.Id, l => l.Number);
            var toAttach = new List<(ArchiveFileInput File, ArchiveFileLineDto Line)>();
            foreach (ArchiveFileInput f in files)
            {
                string name = f?.FileName ?? f?.File?.FileName ?? "";
                int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
                if (slash >= 0) name = name.Substring(slash + 1);
                var line = new ArchiveFileLineDto { FileName = name };
                result.Files.Add(line);
                if (f?.File == null || f.File.Length == 0) { line.Result = "invalid"; line.Message = "فایل خالی است."; continue; }
                if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) { line.Result = "not-pdf"; line.Message = "فقط فایل PDF پذیرفته می‌شود."; continue; }
                if (f.File.Length > MaxArchiveFileBytes) { line.Result = "invalid"; line.Message = "حجم فایل از ۵۰ مگابایت بیشتر است."; continue; }
                if (!await LooksLikePdfAsync(f.File, ct)) { line.Result = "not-pdf"; line.Message = "محتوای فایل PDF معتبر نیست."; continue; }
                string number;
                try { number = ArchiveNumberOf(name, pattern); }
                catch (RegexMatchTimeoutException) { line.Result = "invalid"; line.Message = "خواندن شماره از نام فایل با این عبارت منظم زیاد طول کشید؛ عبارت را ساده‌تر کنید."; continue; }
                if (number == null) { line.Result = "unmatched"; line.Message = "نام فایل با الگوی شماره نامه نمی‌خواند."; result.Unmatched++; continue; }
                line.Number = number;
                List<int> hits = byKey[ArchiveKey(number)].Distinct().ToList();
                if (hits.Count == 0) { line.Result = "unmatched"; line.Message = "نامه‌ای با شماره «" + number + "» در آرشیو واردشده پیدا نشد."; result.Unmatched++; continue; }
                if (hits.Count > 1) { line.Result = "ambiguous"; line.Message = "چند نامه با این شماره وجود دارد (وارده، صادره یا داخلی)؛ نام فایل را یکتا کنید."; result.Unmatched++; continue; }
                line.LetterId = hits[0];
                line.Number = numberOf[hits[0]];
                toAttach.Add((f, line));
            }

            // each file is attached on its own: a failure of one (a full disk, a broken file, a conflicting save) is reported on its line, the state of the unit of work is cleaned,
            // and the next file starts from a clean slate, so the batch is never left half-reported
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (var item in toAttach)
            {
                OfLetter letter = null;
                OfLetterAttachment added = null;
                try
                {
                    letter = await _uow.Set<OfLetter>().Include(l => l.Attachments).FirstOrDefaultAsync(l => l.Id == item.Line.LetterId.Value && !l.Deleted, ct);
                    if (letter == null) { item.Line.Result = "unmatched"; item.Line.Message = "نامه پیدا نشد."; result.Unmatched++; continue; }
                    // a file of the same name is not attached twice (sending the batch again is safe); a file of another name for the same letter is a second attachment
                    if (letter.Attachments.Any(a => string.Equals(a.FileName, item.Line.FileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        item.Line.Result = "already-attached"; item.Line.Message = "این فایل قبلاً به نامه پیوست شده است."; result.AlreadyAttached++;
                        Detach(letter);
                        continue;
                    }
                    Guid documentId = await _documents.UploadDocumentAsync(new ViewModels.NasbNiro.DocumentDtos.UploadFileDto
                    {
                        File = item.File.File, Name = System.IO.Path.GetFileNameWithoutExtension(item.Line.FileName), Code = "LETTER-ARCHIVE", Description = "آرشیو نامه‌های قدیم — نامه " + letter.Number,
                    });
                    string hash = await _documents.GetFileHashAsync(documentId);
                    added = new OfLetterAttachment { DocumentId = documentId, FileName = item.Line.FileName, FileHash = hash };
                    letter.Attachments.Add(added);
                    letter.ModifyDate = now; letter.ModifierUserId = Me;
                    await _uow.SaveChangesAsync(ct);
                    item.Line.Result = "attached"; item.Line.Message = "به نامه پیوست شد."; result.Attached++;
                    Detach(letter);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    Detach(letter);
                    DetachPendingDocuments();
                    item.Line.Result = "invalid"; item.Line.Message = "پیوست این فایل انجام نشد؛ فایل‌های دیگر بررسی شدند و دوباره ارسال این یکی بی‌خطر است.";
                }
            }

            IQueryable<OfLetter> without = _uow.Set<OfLetter>().AsNoTracking().Where(l => !l.Deleted && l.Imported && !l.Attachments.Any());
            result.LettersWithoutFileCount = await without.CountAsync(ct);
            result.LettersWithoutFile = await without.OrderBy(l => l.Number).Select(l => l.Number).Take(200).ToListAsync(ct);
            return ResponseDtoHelper.Ok(result);
        }
    }
}
