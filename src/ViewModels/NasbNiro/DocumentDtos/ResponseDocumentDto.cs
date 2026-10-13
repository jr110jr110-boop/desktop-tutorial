using System.IO;

namespace ViewModels.NasbNiro.DocumentDtos
{
    public class ResponseDocumentDto
    {
        public FileStream FileStream { get; set; }
        public string Name { get; set; }
    }
}
