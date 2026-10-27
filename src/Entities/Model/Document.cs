using System;

namespace Entities.Model
{
    public class Document
    {
        public Document(string path, string mimType)
        {
            Path = path;
            MimType = mimType;
            RegisterDate = DateTimeOffset.Now; // R1q M16: the upload moment (rows loaded from the database overwrite it; old rows keep 0001-01-01 = unknown)
        }

        public Guid Id { get; protected set; }
        public string Path { get; protected set; }
        public DateTimeOffset RegisterDate { get; protected set; }

        /// <summary>The signed-in user who uploaded the file (R1q M16); null for files uploaded before this was recorded.</summary>
        public int? UploadedByUserId { get; protected set; }

        public Document SetUploader(int? userId)
        {
            UploadedByUserId = userId.HasValue && userId.Value > 0 ? userId : null;
            return this;
        }
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

        public void SetMimeType(string mimType)
        {
            this.MimType = mimType;
        }
    }
}
