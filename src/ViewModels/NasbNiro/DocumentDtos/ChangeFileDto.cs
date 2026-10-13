using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ViewModels.NasbNiro.DocumentDtos
{
    public class ChangeFileDto
    {
        [Required]
        public Guid DocumentId { get; set; }

        [Required]
        public IFormFile File { get; set; }
    }
}
