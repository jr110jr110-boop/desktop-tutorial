using System;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.ProcessDtos;

namespace Services.NasbNiro.Extensions
{
    public static class ValidationExtensions
    {
        public static void Validate(this UploadFileDto uploadFileDto)
        {
            if (uploadFileDto == null || uploadFileDto.File == null || uploadFileDto.File.Length == 0)
            {
                throw new ArgumentNullException(nameof(uploadFileDto));
            }
        }

        public static void Validate(this AttachfileToProcessDto uploadFileDto)
        {
            if (uploadFileDto == null)
            {
                throw new ArgumentNullException(nameof(uploadFileDto));
            }
        }
    }
}
