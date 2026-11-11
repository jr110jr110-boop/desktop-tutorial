using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Equipments;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.PredecessorCheckDtos;

namespace Services.NasbNiro
{
    public class EquipmentPredecessorCheckService : IEquipmentPredecessorCheckService
    {
        public const string ReportNote = "این گزارش فقط‌خواندنی است و چیزی را تغییر نمی‌دهد. هر اصلاح یک پیشنهاد است و باید با تأیید نفر دوم (تولیدکننده و تأییدکننده متفاوت) انجام شود.";

        private readonly IUnitOfWork _uow;

        public EquipmentPredecessorCheckService(IUnitOfWork uow)
        {
            _uow = uow ?? throw new System.ArgumentNullException(nameof(uow));
        }

        private sealed class BomRow
        {
            public int Id { get; set; }
            public int? PredecessorId { get; set; }
            public bool Deleted { get; set; }
            public int EquipmentId { get; set; }
            public string Title { get; set; }
            public int ProcessCount { get; set; }
        }

        public async Task<ResponseDto<PredecessorCheckReportDto>> CheckAsync(CancellationToken cancellationToken)
        {
            var products = await _uow.Set<Equipment>().AsNoTracking().Where(e => !e.Deleted)
                .OrderBy(e => e.Id).Select(e => new { e.Id, e.Code, e.Title }).ToListAsync(cancellationToken);

            List<PredecessorNode> assemblyRows = await _uow.Set<EquipmentAssembly>().AsNoTracking()
                .Select(a => new PredecessorNode { Id = a.Id, PredecessorId = a.PredecessorId, Deleted = a.Deleted, EquipmentId = a.EquipmentId, Title = a.Title })
                .ToListAsync(cancellationToken);

            List<BomRow> bomRows = await _uow.Set<EquipmentBom>().AsNoTracking()
                .Select(b => new BomRow
                {
                    Id = b.Id,
                    PredecessorId = b.PredecessorId,
                    Deleted = b.Deleted,
                    EquipmentId = b.EquipmentAssembly.EquipmentId,
                    Title = b.Title,
                    ProcessCount = b.EquipmentProcesses.Count(p => !p.Deleted)
                })
                .ToListAsync(cancellationToken);

            PredecessorCheckReportDto report = new PredecessorCheckReportDto { ProductsChecked = products.Count, Note = ReportNote };
            Dictionary<int, PredecessorNode> assemblyById = assemblyRows.ToDictionary(x => x.Id);
            Dictionary<int, PredecessorNode> bomNodeById = bomRows.ToDictionary(x => x.Id,
                x => new PredecessorNode { Id = x.Id, PredecessorId = x.PredecessorId, Deleted = x.Deleted, EquipmentId = x.EquipmentId, Title = x.Title });

            foreach (var product in products)
            {
                int before = report.Rows.Count;

                List<PredecessorNode> assemblies = assemblyRows.Where(a => a.EquipmentId == product.Id && !a.Deleted).ToList();
                Describe(report, product.Id, product.Code, product.Title, "Assembly", "مونتاژ", assemblies, assemblyById);

                List<PredecessorNode> boms = bomRows.Where(b => b.EquipmentId == product.Id && !b.Deleted)
                    .Select(b => bomNodeById[b.Id]).ToList();
                Describe(report, product.Id, product.Code, product.Title, "Bom", "BOM", boms, bomNodeById);

                foreach (BomRow bom in bomRows.Where(b => b.EquipmentId == product.Id && !b.Deleted && b.ProcessCount == 0).OrderBy(b => b.Id))
                {
                    report.Rows.Add(new PredecessorCheckRowDto
                    {
                        EquipmentId = product.Id, EquipmentCode = product.Code, EquipmentTitle = product.Title,
                        ItemKind = "Bom", ItemId = bom.Id, ItemTitle = bom.Title, PredecessorId = bom.PredecessorId,
                        Problem = "NoProcesses",
                        Reason = "BOM «" + bom.Title + "» هیچ فرایندی ندارد.",
                        ProposedFix = "پیشنهاد: فرایندهای ساخت این BOM را ثبت کنید یا اگر لازم نیست، BOM را حذف (ابطال) کنید؛ تأیید نفر دوم لازم است."
                    });
                }

                if (report.Rows.Count > before) report.ProductsWithFindings++;
            }

            report.FindingCount = report.Rows.Count;
            return ResponseDtoHelper.Ok(report);
        }

        private static void Describe(PredecessorCheckReportDto report, int equipmentId, string code, string title, string kind, string label,
            List<PredecessorNode> items, Dictionary<int, PredecessorNode> allById)
        {
            PredecessorOrdering.Result<PredecessorNode> order = PredecessorOrdering.Sort(items, x => x.Id, x => x.PredecessorId);

            foreach (PredecessorNode item in order.MissingPredecessor)
            {
                int predecessorId = item.PredecessorId.Value;
                string problem, reason;
                if (!allById.TryGetValue(predecessorId, out PredecessorNode predecessor))
                {
                    problem = "Missing";
                    reason = label + " پیش‌نیاز با شناسهٔ " + predecessorId + " وجود ندارد.";
                }
                else if (predecessor.Deleted)
                {
                    problem = "Deleted";
                    reason = label + " پیش‌نیاز «" + predecessor.Title + "» حذف شده است.";
                }
                else
                {
                    problem = "OtherProduct";
                    reason = label + " پیش‌نیاز «" + predecessor.Title + "» متعلق به محصول دیگری است.";
                }

                report.Rows.Add(new PredecessorCheckRowDto
                {
                    EquipmentId = equipmentId, EquipmentCode = code, EquipmentTitle = title,
                    ItemKind = kind, ItemId = item.Id, ItemTitle = item.Title, PredecessorId = predecessorId,
                    Problem = problem,
                    Reason = label + " «" + item.Title + "»: " + reason,
                    ProposedFix = "پیشنهاد: پیش‌نیاز این مورد را بردارید یا پیش‌نیاز درست از همین محصول را انتخاب کنید؛ تأیید نفر دوم لازم است. تا اصلاح، ساخت دستور کار از این محصول رد می‌شود."
                });
            }

            foreach (PredecessorNode item in order.OnCycle)
            {
                report.Rows.Add(new PredecessorCheckRowDto
                {
                    EquipmentId = equipmentId, EquipmentCode = code, EquipmentTitle = title,
                    ItemKind = kind, ItemId = item.Id, ItemTitle = item.Title, PredecessorId = item.PredecessorId,
                    Problem = "Cycle",
                    Reason = label + " «" + item.Title + "» روی یک حلقهٔ پیش‌نیازی است.",
                    ProposedFix = "پیشنهاد: پیش‌نیاز یکی از موردهای حلقه را بردارید تا حلقه باز شود؛ تأیید نفر دوم لازم است."
                });
            }
        }
    }
}
