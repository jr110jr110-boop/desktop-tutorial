using System;

namespace Entities.Model
{
    public class Document
    {
        public Document(string path, string mimType)
        {
            Path = path;
            MimType = mimType;
        }

        public Guid Id { get; protected set; }
        public string Path { get; protected set; }
        public DateTimeOffset RegisterDate { get; protected set; }
        public string MimType { get; protected set; }
        public string Description { get; protected set; }

        public Document SetDescription(string description)
        {
            Description = description;
            return this;
        }

        public string Name { get; protected set; }
        public Document SetName(string name)
        {
            Name = name;
            return this;
        }

        public string Code { get; protected set; }
        public Document SetCode(string code)
        {
            Code = code;
            return this;
        }

        public Document SetPath(string path)
        {
            this.Path = path;
            return this;
        }

        /// <summary>
        /// R1a2 (H3): the user who uploaded the file. A file that no record refers to yet may be replaced or edited only by this user or by the PMO;
        /// files uploaded before this column existed have no uploader (null), so only the PMO may change them.
        /// </summary>
        public int? UploadedByUserId { get; protected set; }

        public Document SetUploadedBy(int? userId)
        {
            UploadedByUserId = userId;
            return this;
        }

        public void SetMimeType(string mimType)
        {
            this.MimType = mimType;
        }
    }
}
