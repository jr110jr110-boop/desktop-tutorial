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

        /// <summary>
        /// R1a2 (H3): the same uploader as <see cref="SetUploader"/> (one column, two names from two remediations). A file that no record refers to yet may be replaced
        /// or edited only by this user or by the PMO; files uploaded before this column existed have no uploader (null), so only the PMO may change them.
        /// </summary>
        public Document SetUploadedBy(int? userId) => SetUploader(userId);

        public void SetMimeType(string mimType)
        {
            this.MimType = mimType;
        }

        /// <summary>
        /// R1t D8: SHA-256 (Base64) of the stored bytes, written at upload and at every replacement of the file; null for files uploaded before this was recorded.
        /// Letters keep their own hash of each attachment at signature; this one lets anybody check later that the stored bytes are still the uploaded ones.
        /// </summary>
        public string Sha256 { get; protected set; }

        public Document SetSha256(string hash)
        {
            Sha256 = hash;
            return this;
        }
    }
}
