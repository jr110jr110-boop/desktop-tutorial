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
        Task<bool> UpdateDocumentAsync(UpdateDocumentDto updateDocumentDto);
        Task<ResponseDto<string>> ChangeFileAsync(ChangeFileDto dto);
        Task<ResponseDto<ResponseFileDto>> GetFileInfoAsync(Guid fileId);
    }
}