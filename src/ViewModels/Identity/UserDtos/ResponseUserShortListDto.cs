using System;
using System.Collections.Generic;
using System.Text;

namespace ViewModels.Identity.UserDtos
{
    public class ResponseUserShortListDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<ResponseUserShortDto> Data { get; set; }
    }
}
