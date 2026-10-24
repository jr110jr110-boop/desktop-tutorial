using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Common.GuardToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Office;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.Resources;
using ViewModels.Exceptions;
using ViewModels.NasbNiro.DocumentDtos;

namespace Services.Services
{
    public class DocumentService : IDocumentService
    {
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<DocumentService> _logger;
        readonly DbSet<Document> _documents;
        public DocumentService(
            IUnitOfWork uow,
            IWebHostEnvironment hostingEnvironment,
            ILogger<DocumentService> logger)
        {
            this._hostingEnvironment = (hostingEnvironment == null) ?
                throw new RepositoryInvalidException(nameof(hostingEnvironment))
                : hostingEnvironment;

            _uow = uow;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _uow.ThrowIfNull(nameof(_uow));

            _documents = _uow.Set<Document>();
        }

        public async Task<Document> GetDocumentByIdAsync(Guid documentId)
        {
            Document result = await _documents.FindAsync(documentId);
            return result;
        }

        public async Task<bool> UpdateDocumentAsync(UpdateDocumentDto updateDocumentDto)
        {
            updateDocumentDto.ThrowIfNull(nameof(updateDocumentDto));

            Document document = await _documents
                .SingleOrDefaultAsync(d => d.Id == updateDocumentDto.Id);

            if (document is null)
                throw new FileNotFoundException("چنین فایلی یافت نشد.");

            string locked = await ImmutableReasonAsync(document.Id);
            if (locked != null) throw new DomainInvalidException(locked);

            document
                .SetDescription(updateDocumentDto.Description)
                .SetName(updateDocumentDto.Name)
                .SetCode(updateDocumentDto.Code);

            _documents.Update(document);

            await _uow.SaveChangesAsync();

            return true;
        }

        public async Task<Guid> UploadDocumentAsync(UploadFileDto uploadFileDto)
        {
            // the file on disk is named after the document's own id (as change-file already does), so a file can always be found from its id
            var documentModel = new Document(path: string.Empty, mimType: Path.GetExtension(uploadFileDto.File.FileName))
                .SetCode(uploadFileDto.Code)
                .SetDescription(uploadFileDto.Description)
                .SetName(uploadFileDto.Name);

            EntityEntry<Document> entityEntry = await _documents.AddAsync(documentModel); // the Guid key is generated here, before the save

            string uniqeFilename = CreateUniqueFileName(uploadFileDto.File.FileName, documentModel.Id);
            string savedPath = await SaveDocumentAsync(uniqeFilename, uploadFileDto.File);
            documentModel.SetPath(savedPath);

            await this._uow.SaveChangesAsync();
            return documentModel.Id;
        }

        public async Task<ResponseDto<string>> ChangeFileAsync(ChangeFileDto dto)
        {
            Document doc = await _documents.SingleOrDefaultAsync(x => x.Id == dto.DocumentId);

            if (doc is null)
            {
                return ResponseDtoHelper.NotFound<string>("فایل یافت نشد.");
            }

            string locked = await ImmutableReasonAsync(doc.Id);
            if (locked != null) return ResponseDtoHelper.BadRequest<string>(locked);

            string path = GetRelativePath(doc.Path);
            string fileName = CreateUniqueFileName(dto.File.FileName, doc.Id);

            try
            {
                File.Delete(path);
            }
            catch
            {
            }

            string savedPath = await SaveDocumentAsync(fileName, dto.File);

            doc.SetPath(savedPath)
                .SetMimeType(new FileInfo(dto.File.FileName).Extension);

            _documents.Update(doc);

            if (await _uow.SaveChangesAsync() > 0)
            {
                return ResponseDtoHelper.Ok<string>("فایل تغییر کرد.");
            }

            return ResponseDtoHelper.BadRequest<string>("فایل عوض نشد.");
        }

        public string CreateUniqueFileName(string fileName, Guid documentId)
        {
            FileInfo fileInfo = new FileInfo(fileName);
            return documentId + fileInfo.Extension;
        }

        public async Task<string> SaveDocumentAsync(string uniqeFilename, IFormFile document)
        {
            string documentPath = "UploadedDocuments";
            string rootPath = this._hostingEnvironment.ContentRootPath;
            string relativePath = Path.Combine(documentPath, uniqeFilename);
            string path = Path.Combine(rootPath, relativePath);

            string saveDirectory = Path.Combine(rootPath, documentPath);

            if (!Directory.Exists(saveDirectory))
                Directory.CreateDirectory(saveDirectory);

            using (var stream = new FileStream(path, FileMode.Create))
            {
                await document.CopyToAsync(stream);
            }

            return relativePath;
        }

        public Guid GenerateDocumentId()
        {
            return Guid.NewGuid();
        }

        public async Task<string> SaveFileManagerDocumentAsync(string uniqeFilename, IFormFile document)
        {
            var documentPath = Path.Combine("wwwroot", "Documents");
            var rootPath = this._hostingEnvironment.ContentRootPath;
            var relativePath = Path.Combine(documentPath, uniqeFilename);
            var path = Path.Combine(rootPath, relativePath);

            var saveDirectory = Path.Combine(rootPath, documentPath);

            if (!Directory.Exists(saveDirectory))
                Directory.CreateDirectory(saveDirectory);

            using (var stream = new FileStream(path, FileMode.Create))
            {
                await document.CopyToAsync(stream);
            }

            return relativePath;
        }

        public async Task<ResponseDocumentDto> DownloadDocumentAsync(Guid documentId)
        {
            Document document = await this._documents.FindAsync(documentId);

            if (document is null)
            {
                throw new DomainInvalidException(Messages.DocumentNotFound);
            }

            var path = GetRelativePath(document.Path);

            return new ResponseDocumentDto()
            {
                FileStream = File.OpenRead(path),
                Name = $"{document.Name}{document.MimType}"
            };
        }

        public async Task<bool> DeleteDocumentAsync(Guid documentId)
        {
            var document = await this._documents.FindAsync(documentId);
            string locked = await ImmutableReasonAsync(documentId);
            if (locked != null) throw new DomainInvalidException(locked);
            string relativePath = GetRelativePath(document.Path); ;
            try
            {
                _documents.Remove(document);
                await _uow.SaveChangesAsync();
                File.Delete(relativePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
                throw new DomainInvalidException(Messages.DocumentNotFound);
            }
        }

        /// <summary>
        /// A file that is part of a signed letter (attachment of a letter that left the draft state, signature image of a letter, delivery receipt)
        /// or the stored signature image of a user must never change: the letter hash covers the bytes of these files.
        /// </summary>
        private async Task<string> ImmutableReasonAsync(Guid documentId)
        {
            bool locked = await _uow.Set<OfLetterAttachment>().AnyAsync(a => a.DocumentId == documentId && a.Letter.Status != OfLetterStatus.Draft)
                || await _uow.Set<OfLetter>().AnyAsync(l => l.SignatureDocumentId == documentId || l.DeliveryReceiptDocumentId == documentId)
                || await _uow.Set<OfUserSignature>().AnyAsync(s => s.ImageDocumentId == documentId);
            return locked ? "این فایل بخشی از یک نامه ارسال‌شده یا امضای ثبت‌شده است و تغییر یا حذف آن مجاز نیست." : null;
        }

        public async Task<bool> IsLetterFileAsync(Guid documentId) =>
            await _uow.Set<OfLetterAttachment>().AnyAsync(a => a.DocumentId == documentId)
            || await _uow.Set<OfLetter>().AnyAsync(l => l.SignatureDocumentId == documentId || l.DeliveryReceiptDocumentId == documentId);

        public async Task<string> GetFileHashAsync(Guid documentId)
        {
            Document document = await _documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId);
            if (document == null) return null;
            string path = GetRelativePath(document.Path);
            if (!File.Exists(path)) return null;
            using (FileStream stream = File.OpenRead(path))
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return Convert.ToBase64String(await sha.ComputeHashAsync(stream));
        }

        private string GetRelativePath(string path)
        {
            var rootPath = this._hostingEnvironment.ContentRootPath;
            return Path.Combine(rootPath, path);
        }

        public async Task<ResponseDto<ResponseFileDto>> GetFileInfoAsync(Guid fileId)
        {
            var result = await _documents.Select(x =>
                    new ResponseFileDto()
                    {
                        //DocumentId = x.Id,
                        Id = x.Id,
                        Code = x.Code,
                        Description = x.Description,
                        MimeType = x.MimType,
                        Name = x.Name
                    })
                //.SingleOrDefaultAsync(x => x.DocumentId == fileId);
                .SingleOrDefaultAsync(x => x.Id == fileId);

            if (result is null)
            {
                return ResponseDtoHelper.NotFound<ResponseFileDto>("فایل یافت نشد.");
            }

            return ResponseDtoHelper.Ok(result);
        }
    }
}
