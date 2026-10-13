using System;

namespace ViewModels.NasbNiro.DocumentDtos
{
    public class ResponseFileDto
    {
        public Guid Id { get; set; }
        //public Guid DocumentId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string MimeType { get; set; }
        public string Code { get; set; }
    }
}
