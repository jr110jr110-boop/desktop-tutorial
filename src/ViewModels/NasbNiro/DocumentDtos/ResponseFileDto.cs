﻿using System;

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

        /// <summary>R1t D8: SHA-256 (Base64) of the stored bytes at upload or at the last replacement; null for a file uploaded before the hash was kept.</summary>
        public string Sha256 { get; set; }
    }
}
