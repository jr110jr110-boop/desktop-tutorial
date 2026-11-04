using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    // The tender (before P.O.) document register (E1, E2): its own numbering series; the package is linked to the contract when the tender is won.
    public partial class EdmsService
    {
        private async Task<List<PcTenderPackageDto>> TenderDtosAsync(List<PcTenderPackage> pkgs, bool withDocuments, CancellationToken ct)
        {
            List<int> ids = pkgs.Select(p => p.Id).ToList();
            List<PcTenderDocument> docs = ids.Count == 0 ? new List<PcTenderDocument>() : await _uow.Set<PcTenderDocument>().AsNoTracking().Where(d => ids.Contains(d.PackageId)).OrderBy(d => d.Id).ToListAsync(ct);
            List<int> cIds = pkgs.Where(p => p.ContractId.HasValue).Select(p => p.ContractId.Value).Distinct().ToList();
            Dictionary<int, string> titles = cIds.Count == 0 ? new Dictionary<int, string>() : (await _uow.Set<ContractEntity>().AsNoTracking().Where(c => cIds.Contains(c.Id)).Select(c => new { c.Id, c.Title }).ToListAsync(ct)).ToDictionary(c => c.Id, c => c.Title);
            return pkgs.Select(p => new PcTenderPackageDto
            {
                Id = p.Id, Code = p.Code, Title = p.Title, TenderId = p.TenderId, TenderCode = p.TenderCode, Status = p.Status, ContractId = p.ContractId, LinkedAt = p.LinkedAt, Note = p.Note,
                ContractTitle = p.ContractId.HasValue && titles.TryGetValue(p.ContractId.Value, out string t) ? t : null,
                DocumentCount = docs.Count(d => d.PackageId == p.Id && !d.Deleted),
                Documents = withDocuments ? docs.Where(d => d.PackageId == p.Id).Select(d => new PcTenderDocumentDto
                {
                    Id = d.Id, PackageId = d.PackageId, Number = d.Number, Title = d.Title, Discipline = d.Discipline, DocumentType = d.DocumentType, Revision = d.Revision, DocumentId = d.DocumentId,
                    IssuedAt = d.IssuedAt, IssuedByUserId = d.IssuedByUserId, VoidReason = d.VoidReason, Voided = d.Deleted,
                }).ToList() : new List<PcTenderDocumentDto>(),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PcTenderPackageDto>>> GetTenderPackagesAsync(PcTenderQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.TenderReaders)) return Bad<PagedList<PcTenderPackageDto>>(PcText.NotAllowed);
            query ??= new PcTenderQuery();
            IQueryable<PcTenderPackage> q = _uow.Set<PcTenderPackage>().AsNoTracking().Where(p => !p.Deleted);
            if (query.ContractId.HasValue) q = q.Where(p => p.ContractId == query.ContractId.Value);
            string c = Clean(query.Contains);
            if (c != null) q = q.Where(p => p.Code.Contains(c) || p.Title.Contains(c) || (p.TenderCode != null && p.TenderCode.Contains(c)));
            PagedList<PcTenderPackage> page = await PagedList<PcTenderPackage>.CreateAsync(q.OrderByDescending(p => p.Id), query.PageNumber, query.PageSize, ct);
            List<PcTenderPackageDto> dtos = await TenderDtosAsync(page.ToList(), false, ct);
            return ResponseDtoHelper.Ok((PagedList<PcTenderPackageDto>)new PcPage<PcTenderPackageDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcTenderPackageDto>> GetTenderPackageAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.TenderReaders)) return Bad<PcTenderPackageDto>(PcText.NotAllowed);
            PcTenderPackage p = await _uow.Set<PcTenderPackage>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (p == null) return PcText.NotFound<PcTenderPackageDto>("بسته مناقصه پیدا نشد.");
            return ResponseDtoHelper.Ok((await TenderDtosAsync(new List<PcTenderPackage> { p }, true, ct)).First());
        }

        public async Task<ResponseDto<List<PcTenderPackageDto>>> TendersOfContractAsync(int contractId, CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.TenderReaders)) return Bad<List<PcTenderPackageDto>>(PcText.NotAllowed);
            List<PcTenderPackage> p = await _uow.Set<PcTenderPackage>().AsNoTracking().Where(x => !x.Deleted && x.ContractId == contractId).OrderBy(x => x.Id).ToListAsync(ct);
            return ResponseDtoHelper.Ok(await TenderDtosAsync(p, true, ct));
        }

        public async Task<ResponseDto<PcTenderPackageDto>> SaveTenderPackageAsync(PcTenderPackageSaveDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Title) == null) return Bad<PcTenderPackageDto>(PcText.InvalidData);
            if (!_user.IsInRole(EdmsRoles.TenderWriters)) return Bad<PcTenderPackageDto>(PcText.NotAllowed);
            DateTimeOffset now = DateTimeOffset.Now;
            PcTenderPackage p;
            if (dto.Id.HasValue)
            {
                p = await _uow.Set<PcTenderPackage>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (p == null) return PcText.NotFound<PcTenderPackageDto>("بسته مناقصه پیدا نشد.");
                if (p.Status != PcTenderStatus.Open) return Bad<PcTenderPackageDto>("بسته مناقصه بسته شده و ویرایش نمی‌شود.");
            }
            else
            {
                p = new PcTenderPackage { Code = await InventoryNumbers.NextNumberAsync(_uow, PcNumbers.TenderPackage, "TP", now.Date, ct), CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcTenderPackage>().Add(p);
            }
            p.Title = Clean(dto.Title); p.TenderId = dto.TenderId; p.TenderCode = Clean(dto.TenderCode); p.Note = Clean(dto.Note); p.ModifyDate = now; p.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await TenderDtosAsync(new List<PcTenderPackage> { p }, true, ct)).First());
        }

        public async Task<ResponseDto<PcTenderPackageDto>> SaveTenderDocumentAsync(PcTenderDocumentSaveDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Title) == null) return Bad<PcTenderPackageDto>(PcText.InvalidData);
            if (!_user.IsInRole(EdmsRoles.TenderWriters)) return Bad<PcTenderPackageDto>(PcText.NotAllowed);
            PcTenderPackage p = await _uow.Set<PcTenderPackage>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.PackageId && !x.Deleted, ct);
            if (p == null) return PcText.NotFound<PcTenderPackageDto>("بسته مناقصه پیدا نشد.");
            if (p.Status != PcTenderStatus.Open) return Bad<PcTenderPackageDto>("بسته مناقصه بسته شده است.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcTenderDocument d;
            if (dto.Id.HasValue)
            {
                d = await _uow.Set<PcTenderDocument>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && x.PackageId == dto.PackageId && !x.Deleted, ct);
                if (d == null) return PcText.NotFound<PcTenderPackageDto>("سند مناقصه پیدا نشد.");
                if (d.IssuedAt.HasValue) return Bad<PcTenderPackageDto>("سند صادرشده تغییر نمی‌کند؛ ابطال کنید و سند تازه بسازید.");
            }
            else
            {
                d = new PcTenderDocument { PackageId = dto.PackageId, Number = await InventoryNumbers.NextNumberAsync(_uow, PcNumbers.TenderDocument, "TD", now.Date, ct), CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcTenderDocument>().Add(d);
            }
            d.Title = Clean(dto.Title); d.Discipline = Clean(dto.Discipline); d.DocumentType = Clean(dto.DocumentType); d.Revision = Clean(dto.Revision) ?? "00"; d.DocumentId = dto.DocumentId; d.ModifyDate = now; d.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return await GetTenderPackageAsync(dto.PackageId, ct);
        }

        public async Task<ResponseDto<PcTenderPackageDto>> TenderActionAsync(PcTenderActionDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcTenderPackageDto>(PcText.InvalidData);
            if (!_user.IsInRole(EdmsRoles.TenderWriters)) return Bad<PcTenderPackageDto>(PcText.NotAllowed);
            string action = dto.Action.Trim().ToLowerInvariant();
            DateTimeOffset now = DateTimeOffset.Now;
            string reason = Clean(dto.Reason);
            if (action == "issue" || action == "void")
            {
                PcTenderDocument d = dto.DocumentId.HasValue ? await _uow.Set<PcTenderDocument>().FirstOrDefaultAsync(x => x.Id == dto.DocumentId.Value && x.PackageId == dto.Id && !x.Deleted, ct) : null;
                if (d == null) return PcText.NotFound<PcTenderPackageDto>("سند مناقصه پیدا نشد.");
                if (action == "issue")
                {
                    if (d.IssuedAt.HasValue) return Bad<PcTenderPackageDto>("این سند قبلاً صادر شده است.");
                    d.IssuedAt = now; d.IssuedByUserId = Me;
                }
                else
                {
                    if (reason == null || reason.Length < 3) return Bad<PcTenderPackageDto>("علت ابطال سند الزامی است (حداقل ۳ حرف).");
                    d.VoidReason = reason; d.Delete();
                }
                d.ModifyDate = now; d.ModifierUserId = Me;
                await _uow.SaveChangesAsync(ct);
                return await GetTenderPackageAsync(dto.Id, ct);
            }
            PcTenderPackage p = await _uow.Set<PcTenderPackage>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (p == null) return PcText.NotFound<PcTenderPackageDto>("بسته مناقصه پیدا نشد.");
            switch (action)
            {
                case "win": case "lose": case "cancel":
                    if (p.Status != PcTenderStatus.Open) return Bad<PcTenderPackageDto>("وضعیت بسته مناقصه دیگر باز نیست.");
                    if (action != "win" && (reason == null || reason.Length < 3)) return Bad<PcTenderPackageDto>("علت الزامی است (حداقل ۳ حرف).");
                    p.Status = action == "win" ? PcTenderStatus.Won : action == "lose" ? PcTenderStatus.Lost : PcTenderStatus.Cancelled;
                    if (reason != null) p.Note = string.IsNullOrEmpty(p.Note) ? reason : p.Note + " | " + reason;
                    break;
                case "link-contract":
                    if (p.Status != PcTenderStatus.Won) return Bad<PcTenderPackageDto>("فقط بسته‌ی برنده‌شده به پروژه وصل می‌شود.");
                    if (p.ContractId.HasValue) return Bad<PcTenderPackageDto>("بسته قبلاً به پروژه وصل شده است.");
                    if (!dto.ContractId.HasValue || !await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId.Value && !c.Deleted, ct)) return PcText.NotFound<PcTenderPackageDto>(PcText.ContractNotFound);
                    p.ContractId = dto.ContractId; p.LinkedAt = now; p.LinkedByUserId = Me;
                    break;
                default: return Bad<PcTenderPackageDto>("عمل نامعتبر است.");
            }
            p.ModifyDate = now; p.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return await GetTenderPackageAsync(dto.Id, ct);
        }
    }
}
