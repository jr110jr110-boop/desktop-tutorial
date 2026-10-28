using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Common.GuardToolkit;
using Common.IdentityToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Quality;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.Identity.AccountServices;
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
        private readonly IHttpContextAccessor _http;
        readonly DbSet<Document> _documents;
        public DocumentService(
            IUnitOfWork uow,
            IWebHostEnvironment hostingEnvironment,
            ILogger<DocumentService> logger,
            IHttpContextAccessor http)
        {
            this._hostingEnvironment = (hostingEnvironment == null) ?
                throw new RepositoryInvalidException(nameof(hostingEnvironment))
                : hostingEnvironment;

            _uow = uow;
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _uow.ThrowIfNull(nameof(_uow));

            _documents = _uow.Set<Document>();
        }

        public async Task<Document> GetDocumentByIdAsync(Guid documentId)
        {
            Document result = await _documents.FindAsync(documentId);
            return result;
        }

        /// <summary>
        /// The update of a file by the module that owns it (the tender screen edits the description of a tender file): the controller of that module has checked the role,
        /// so only the lock of the letters applies here. The generic Documents endpoint uses <see cref="UpdateDocumentByUserAsync"/> instead.
        /// </summary>
        public async Task<bool> UpdateDocumentAsync(UpdateDocumentDto updateDocumentDto)
        {
            updateDocumentDto.ThrowIfNull(nameof(updateDocumentDto));

            Document document = await _documents
                .SingleOrDefaultAsync(d => d.Id == updateDocumentDto.Id);

            if (document is null)
                throw new FileNotFoundException("چنین فایلی یافت نشد.");

            string locked = await LetterLockAsync(document.Id);
            if (locked != null) throw new DomainInvalidException(locked);

            document
                .SetDescription(updateDocumentDto.Description)
                .SetName(updateDocumentDto.Name)
                .SetCode(updateDocumentDto.Code);

            _documents.Update(document);

            await _uow.SaveChangesAsync();

            return true;
        }

        /// <summary>
        /// R1a2 (H3): the generic edit of the name, description and code of a file (the Documents endpoint). A file that any record refers to is changed only through the module that owns
        /// it; a file nobody refers to yet is changed only by the user who uploaded it or by the PMO.
        /// </summary>
        public async Task<ResponseDto<bool>> UpdateDocumentByUserAsync(UpdateDocumentDto updateDocumentDto)
        {
            updateDocumentDto.ThrowIfNull(nameof(updateDocumentDto));

            Document document = await _documents.SingleOrDefaultAsync(d => d.Id == updateDocumentDto.Id);
            if (document is null) return ResponseDtoHelper.NotFound<bool>("فایل یافت نشد.");

            ResponseDto<bool> refusal = await GenericChangeRefusalAsync<bool>(document);
            if (refusal != null) return refusal;

            document
                .SetDescription(updateDocumentDto.Description)
                .SetName(updateDocumentDto.Name)
                .SetCode(updateDocumentDto.Code);

            _documents.Update(document);
            await _uow.SaveChangesAsync();
            return ResponseDtoHelper.Ok(true);
        }

        public async Task<Guid> UploadDocumentAsync(UploadFileDto uploadFileDto)
        {
            // the file on disk is named after the document's own id (as change-file already does), so a file can always be found from its id
            var documentModel = new Document(path: string.Empty, mimType: Path.GetExtension(uploadFileDto.File.FileName))
                .SetCode(uploadFileDto.Code)
                .SetDescription(uploadFileDto.Description)
                .SetName(uploadFileDto.Name)
                .SetUploadedBy(CallerId);   // R1a2 (H3) + R1q M16: until a record refers to the file, only the uploader (or the PMO) may replace or edit it

            EntityEntry<Document> entityEntry = await _documents.AddAsync(documentModel); // the Guid key is generated here, before the save

            string uniqeFilename = CreateUniqueFileName(uploadFileDto.File.FileName, documentModel.Id);
            string savedPath = await SaveDocumentAsync(uniqeFilename, uploadFileDto.File);
            documentModel.SetPath(savedPath);

            await this._uow.SaveChangesAsync();
            return documentModel.Id;
        }

        /// <summary>
        /// R1a2 (H3): replaces the bytes of a file (the generic Documents endpoint). Refused for a file that any record refers to (the owner module detaches and attaches files with its own role check)
        /// and, for a file nobody refers to, for everybody except the user who uploaded it and the PMO. Every replacement writes a row of the audit trail with the SHA-256 of the old and the new bytes.
        /// </summary>
        public async Task<ResponseDto<string>> ChangeFileAsync(ChangeFileDto dto)
        {
            Document doc = await _documents.SingleOrDefaultAsync(x => x.Id == dto.DocumentId);

            if (doc is null)
            {
                return ResponseDtoHelper.NotFound<string>("فایل یافت نشد.");
            }

            ResponseDto<string> refusal = await GenericChangeRefusalAsync<string>(doc);
            if (refusal != null) return refusal;

            if (dto.File == null || dto.File.Length == 0)
            {
                return ResponseDtoHelper.BadRequest<string>("فایلی برای جایگزینی ارسال نشده است.");
            }

            string path = GetRelativePath(doc.Path);
            string fileName = CreateUniqueFileName(dto.File.FileName, doc.Id);
            string oldHash = await HashOfAsync(path);

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

            // the row of the generic audit trail only appears when the path or the type changes; the replacement of the bytes under the same name is recorded here
            IdentityAudit.Add(_uow, _http, CallerId ?? 0, "Document", null, IdentityAudit.FileReplaced,
                "جایگزینی فایل «" + (doc.Name ?? doc.Code ?? doc.Id.ToString()) + "» (" + doc.Id + ")",
                new { documentId = doc.Id, sha256 = oldHash }, new { documentId = doc.Id, sha256 = await HashOfAsync(GetRelativePath(savedPath)) });

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

        /// <summary>
        /// Deletes a file and its bytes. R1a2 (H3): refused while any record refers to the file, so the owner module removes its own link first and then deletes the file
        /// (that is the order every detach action already follows).
        /// </summary>
        public async Task<bool> DeleteDocumentAsync(Guid documentId)
        {
            var document = await this._documents.FindAsync(documentId);
            if (document is null)
            {
                throw new DomainInvalidException(Messages.DocumentNotFound);
            }

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

        // ------------------------------------------------------------------------------------------ R1a2 (H3): who may change a file

        private int? CallerId => int.TryParse(_http.HttpContext?.User?.Identity?.GetUserId(), out int id) ? id : (int?)null;

        private bool CallerIsPmo => _http.HttpContext?.User?.IsInRole(IdentityRules.Pmo) == true;

        /// <summary>The answer of a refused generic change: the lock of the owner first, then the uploader rule; null when the change may go on.</summary>
        private async Task<ResponseDto<T>> GenericChangeRefusalAsync<T>(Document document)
        {
            string locked = await ImmutableReasonAsync(document.Id);
            if (locked != null) return ResponseDtoHelper.BadRequest<T>(locked);

            int? caller = CallerId;
            bool uploader = caller.HasValue && document.UploadedByUserId == caller;
            if (!uploader && !CallerIsPmo) return ResponseDtoHelper.Forbidden<T>(NotTheUploader);
            return null;
        }

        public const string NotTheUploader = "این فایل را فقط کاربری که بارگذاری کرده یا مدیر دفتر مدیریت پروژه می‌تواند تغییر دهد.";

        /// <summary>
        /// The reason a file must not be changed or deleted, or null. Two kinds of lock: the files of letters that left the draft (their hash covers the bytes) and the stored signature images,
        /// and (R1a2, H3) any file that a record of any module refers to: it changes only through the owner module's own, role-checked action.
        /// </summary>
        public async Task<string> ImmutableReasonAsync(Guid documentId) => await LetterLockAsync(documentId) ?? await ReferrerLockAsync(documentId);

        /// <summary>A file that is part of a signed letter (attachment of a letter that left the draft state, signature image of a letter, delivery receipt) or the stored signature image of a user must never change: the letter hash covers the bytes of these files.</summary>
        private async Task<string> LetterLockAsync(Guid documentId)
        {
            bool locked = await _uow.Set<OfLetterAttachment>().AnyAsync(a => a.DocumentId == documentId && a.Letter.Status != OfLetterStatus.Draft)
                || await _uow.Set<OfLetter>().AnyAsync(l => l.SignatureDocumentId == documentId || l.DeliveryReceiptDocumentId == documentId)
                || await _uow.Set<OfUserSignature>().AnyAsync(s => s.ImageDocumentId == documentId);
            if (locked) return "این فایل بخشی از یک نامه ارسال‌شده یا امضای ثبت‌شده است و تغییر یا حذف آن مجاز نیست.";
            // R1q M16: a file that proves an inspection and test plan activity was done must not be replaced or deleted afterwards
            return await _uow.Set<ItpActivity>().AnyAsync(a => a.EvidenceDocumentId == documentId)
                ? "این فایل مدرک انجام یک فعالیت برنامه بازرسی و آزمون است و تغییر یا حذف آن مجاز نیست." : null;
        }

        private async Task<string> ReferrerLockAsync(Guid documentId)
        {
            DocumentReferrer found = await FirstReferrerAsync(documentId);
            if (found == null) return null;
            return "این فایل به " + found.Owner + " پیوست شده است و فقط از همان بخش و با نقش مجاز آن قابل تغییر یا حذف است.";
        }

        private sealed class DocumentReferrer
        {
            public string Owner { get; set; }
        }

        private static readonly MethodInfo FindRecordMethod = typeof(DocumentService).GetMethod(nameof(FindRecordAsync), BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo FindParentMethod = typeof(DocumentService).GetMethod(nameof(FindParentAsync), BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>The first record of any module that holds the id of the file (one cheap query per column of the model that points at a file), described for a person; null when no record does.</summary>
        private async Task<DocumentReferrer> FirstReferrerAsync(Guid documentId)
        {
            if (!(_uow is DbContext context)) return null;
            foreach (DocumentLink link in DocumentLinks.Of(context.Model))
            {
                object record = await (Task<object>)FindRecordMethod.MakeGenericMethod(link.EntityType).Invoke(this, new object[] { link, documentId });
                if (record == null) continue;

                object parent = null;
                if (DocumentLinks.LinkTables.TryGetValue(link.EntityType.Name, out DocumentLinks.ParentOwner owner))
                {
                    object key = record.GetType().GetProperty(owner.ForeignKey)?.GetValue(record);
                    if (key is int parentId)
                        parent = await (Task<object>)FindParentMethod.MakeGenericMethod(owner.ParentType).Invoke(this, new object[] { parentId });
                }
                return new DocumentReferrer { Owner = DocumentLinks.Describe(link, record, parent) };
            }
            return null;
        }

        private async Task<object> FindRecordAsync<T>(DocumentLink link, Guid documentId) where T : class
        {
            if (link.IsText)
            {
                // a list of ids separated by commas (the files of a sales RFQ)
                string text = documentId.ToString();
                ParameterExpression row = Expression.Parameter(typeof(T), "r");
                Expression list = Expression.Call(typeof(EF), nameof(EF.Property), new[] { typeof(string) }, row, Expression.Constant(link.Property));
                Expression<Func<string>> wantedText = () => text;
                Expression contains = Expression.AndAlso(
                    Expression.NotEqual(list, Expression.Constant(null, typeof(string))),
                    Expression.Call(list, typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) }), wantedText.Body));
                return await _uow.Set<T>().AsNoTracking().IgnoreQueryFilters().Where(Expression.Lambda<Func<T, bool>>(contains, row)).FirstOrDefaultAsync();
            }

            // a closure variable, so that the query is parameterised (one cached plan per column) and not rebuilt with a literal for every file
            Expression<Func<Guid>> id = () => documentId;
            Type valueType = link.IsNullable ? typeof(Guid?) : typeof(Guid);
            ParameterExpression record = Expression.Parameter(typeof(T), "r");
            Expression column = Expression.Call(typeof(EF), nameof(EF.Property), new[] { valueType }, record, Expression.Constant(link.Property));
            Expression wanted = link.IsNullable ? Expression.Convert(id.Body, typeof(Guid?)) : id.Body;
            Expression<Func<T, bool>> predicate = Expression.Lambda<Func<T, bool>>(Expression.Equal(column, wanted), record);
            return await _uow.Set<T>().AsNoTracking().IgnoreQueryFilters().Where(predicate).FirstOrDefaultAsync();
        }

        private async Task<object> FindParentAsync<T>(int parentId) where T : class =>
            await _uow.Set<T>().AsNoTracking().IgnoreQueryFilters().Where(p => EF.Property<int>(p, "Id") == parentId).FirstOrDefaultAsync();

        private static async Task<string> HashOfAsync(string path)
        {
            if (!File.Exists(path)) return null;
            using (FileStream stream = File.OpenRead(path))
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return Convert.ToBase64String(await sha.ComputeHashAsync(stream));
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
