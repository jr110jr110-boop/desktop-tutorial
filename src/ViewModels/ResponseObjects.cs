using System;
using System.Collections.Generic;
using System.Text;

namespace ViewModels
{
    public class ResponseObjects
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public dynamic Data { get; set; }
    }
}
