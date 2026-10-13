using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace Common.WebToolkit
{
    /// <summary>
    /// Validates the extension of an uploaded file (or files) against a comma separated list such as ".png,.jpg".
    /// Local copy of the attribute that used to come from DNTCommon.Web.Core.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
    public sealed class UploadFileExtensionsAttribute : ValidationAttribute
    {
        private readonly string[] _allowedExtensions;

        public UploadFileExtensionsAttribute(string fileExtensions)
        {
            _allowedExtensions = (fileExtensions ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(extension => extension.Trim())
                .ToArray();
        }

        public override bool IsValid(object value)
        {
            if (value == null)
            {
                return true; // `Required` attribute should be used to make it mandatory.
            }

            if (value is IFormFile file)
            {
                return isValidFile(file);
            }

            if (value is IEnumerable<IFormFile> files)
            {
                return files.All(isValidFile);
            }

            return true;
        }

        private bool isValidFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return true;
            }

            var fileExtension = Path.GetExtension(file.FileName);
            return !string.IsNullOrWhiteSpace(fileExtension) &&
                   _allowedExtensions.Any(ext => fileExtension.Equals(ext, StringComparison.OrdinalIgnoreCase));
        }
    }
}
