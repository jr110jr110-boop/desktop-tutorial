using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Production;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Attachments;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.AttachmentDtos;

namespace MsTests
{
    /// <summary>The files of a production operation: every role of the shop terminal reads them (the operator too), writing is unchanged, a role outside the terminal is still refused.</summary>
    [TestClass]
    public class ProductionOperationAttachmentTests
    {
        private static T Run<T>(AppHost host, int user, string role, Func<AttachmentService, Task<T>> call) =>
            host.Read(uow => call(new AttachmentService(uow, new TestUser(user, new[] { role }), new StubDocs(), null)).GetAwaiter().GetResult());

        private sealed class StubDocs : Services.Contracts.IDocumentService
        {
            public Task<Entities.Model.Document> GetDocumentByIdAsync(Guid documentId) => throw new NotSupportedException();
            public Task<Guid> UploadDocumentAsync(ViewModels.NasbNiro.DocumentDtos.UploadFileDto uploadFileDto) => Task.FromResult(Guid.NewGuid());
            public Task<ViewModels.NasbNiro.DocumentDtos.ResponseDocumentDto> DownloadDocumentAsync(Guid documentId) => throw new NotSupportedException();
            public Task<bool> DeleteDocumentAsync(Guid image) => throw new InvalidOperationException();
            public Task<bool> UpdateDocumentAsync(ViewModels.NasbNiro.DocumentDtos.UpdateDocumentDto updateDocumentDto) => Task.FromResult(true);
            public Task<Common.Results.ResponseDto<bool>> UpdateDocumentByUserAsync(ViewModels.NasbNiro.DocumentDtos.UpdateDocumentDto updateDocumentDto) => throw new NotSupportedException();
            public Task<Common.Results.ResponseDto<string>> ChangeFileAsync(ViewModels.NasbNiro.DocumentDtos.ChangeFileDto dto) => throw new NotSupportedException();
            public Task<Common.Results.ResponseDto<ViewModels.NasbNiro.DocumentDtos.ResponseFileDto>> GetFileInfoAsync(Guid fileId) => throw new NotSupportedException();
            public Task<bool> IsLetterFileAsync(Guid documentId) => Task.FromResult(false);
            public Task<string> GetFileHashAsync(Guid documentId) => Task.FromResult<string>(null);
        }

        private static UploadAttachmentForm Form(int operationId)
        {
            byte[] bytes = Encoding.ASCII.GetBytes("%PDF-1.4 test");
            return new UploadAttachmentForm { OwnerType = AttachmentOwnerType.ProductionOperation, OwnerId = operationId, Kind = AttachmentKind.Drawing, Title = "نقشه", Revision = "A", File = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "File", "drawing.pdf") };
        }

        private static int SeedOperation(AppHost host)
        {
            var op = new MfOperation { OrderId = 1, Sequence = 10, Title = "فرز کاری", WorkCenterId = 1, Status = MfOperationStatus.Ready };
            host.Write(uow => { AppHost.Add(uow, op); uow.SaveChanges(); });
            return op.Id;
        }

        [TestMethod]
        public void Every_Terminal_Role_Reads_The_Files_Of_An_Operation_And_A_Stranger_Does_Not()
        {
            AppHost host = FinanceTestSupport.NewHost("ProdOpAttach");
            int op = SeedOperation(host);
            foreach (string role in ProductionRoles.Terminal.Split(','))
                Assert.IsTrue(Run(host, 5, role, s => s.GetPanelAsync(AttachmentOwnerType.ProductionOperation, op, default)).Success, role + " reads");
            Assert.IsFalse(Run(host, 5, "OPERATOR", s => s.GetPanelAsync(AttachmentOwnerType.ProductionOperation, op, default)).Data.CanWrite, "reading does not open writing");
            Assert.IsFalse(Run(host, 6, "SALES", s => s.GetPanelAsync(AttachmentOwnerType.ProductionOperation, op, default)).Success, "a role outside the terminal is refused");
            Assert.IsFalse(Run(host, 7, "MAINTENANCE", s => s.GetPanelAsync(AttachmentOwnerType.ProductionOperation, op, default)).Success);
        }

        [TestMethod]
        public void The_Operator_Still_Cannot_Upload_To_An_Operation_And_The_Supervisor_Still_Can()
        {
            AppHost host = FinanceTestSupport.NewHost("ProdOpAttach");
            int op = SeedOperation(host);
            Assert.IsFalse(Run(host, 5, "OPERATOR", s => s.UploadAsync(Form(op), default)).Success, "the operator reads only");
            Assert.IsFalse(Run(host, 6, "SALES", s => s.UploadAsync(Form(op), default)).Success);
            var ok = Run(host, 8, "SHOPSUPERVISOR", s => s.UploadAsync(Form(op), default));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(1, Run(host, 5, "OPERATOR", s => s.GetPanelAsync(AttachmentOwnerType.ProductionOperation, op, default)).Data.Items.Count(i => i.Current), "the operator sees the file the supervisor added");
        }
    }
}
