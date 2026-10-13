using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.DocumentDtos
{
    public class UpdateDocumentDto
    {
        [Required]
        public Guid Id { get; set; }

        [Required]
        public string Description { get; set; }

        [Required]
        public string Code { get; set; }

        [Required]
        public string Name { get; set; }
    }
}
