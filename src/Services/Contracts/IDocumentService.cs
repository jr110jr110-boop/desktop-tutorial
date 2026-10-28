using System;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model;
using ViewModels.NasbNiro.DocumentDtos;

namespace Services.Contracts
{
    public interface IDocumentService
    {
        Task<Document> GetDocumentByIdAsync(Guid documentId);
        Task<Guid> UploadDocumentAsync(UploadFileDto uploadFileDto);
        Task<ResponseDocumentDto> DownloadDocumentAsync(Guid documentId);
        Task<bool> DeleteDocumentAsync(Guid image);

        /// <summary>The update of a file by the module that owns it (its controller has checked the role). Only the lock of the letters applies.</summary>
        Task<bool> UpdateDocumentAsync(UpdateDocumentDto updateDocumentDto);

        /// <summary>
        /// R1a2 (H3): the generic edit of name, description and code (the Documents endpoint). A file that any record refers to is refused (it changes only through its owner module);
        /// a file nobody refers to yet is changed only by its uploader or the PMO.
        /// </summary>
        Task<ResponseDto<bool>> UpdateDocumentByUserAsync(UpdateDocumentDto updateDocumentDto);

        Task<ResponseDto<string>> ChangeFileAsync(ChangeFileDto dto);
        Task<ResponseDto<ResponseFileDto>> GetFileInfoAsync(Guid fileId);

        /// <summary>True when the file is an attachment, signature image or delivery receipt of a letter: such files are served only through the letter, never by the generic download.</summary>
        Task<bool> IsLetterFileAsync(Guid documentId);

        /// <summary>SHA-256 (Base64) of the stored bytes of a file; null when the document or its file does not exist.</summary>
        Task<string> GetFileHashAsync(Guid documentId);
    }
}