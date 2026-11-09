using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.ProjectControl;

namespace Services.NasbNiro.Quality
{
    /// <summary>What the system knows about the drawings and the released characteristic lists of one part, resolved through the EDMS (one answer for every quality gate).</summary>
    public sealed class ResolvedCharacteristics
    {
        public int ItemId { get; set; }
        public List<PartDrawing> Drawings { get; } = new List<PartDrawing>();
        public List<QcCharacteristicList> Lists { get; } = new List<QcCharacteristicList>();
        public List<CharLine> Lines { get; } = new List<CharLine>();
        public bool NoDrawingActive { get; set; }
        public string NoDrawingReason { get; set; }

        public bool HasDrawing => Drawings.Count > 0;
        /// <summary>A released list exists for the drawing in force (it may be an approved empty list: «بدون مشخصه قابل اندازه‌گیری»).</summary>
        public bool HasList => Lists.Count > 0;
        public bool HasLines => Lines.Count > 0;

        /// <summary>The numbers of the drawings in force, joined (cut to the column length).</summary>
        public string DrawingNumber => Cut(string.Join("، ", Drawings.Select(d => d.Number)), 200);

        /// <summary>The revision codes in the same order.</summary>
        public string DrawingRevision => Cut(string.Join("، ", Drawings.Select(d => d.RevisionCode)), 50);

        public int? DrawingRevisionId => Lists.Count > 0 ? Lists[0].RevisionId : (Drawings.Count > 0 ? Drawings[0].RevisionId : (int?)null);
        public int? ListId => Lists.Count > 0 ? Lists[0].Id : (int?)null;

        public QcCharacteristicList ListOf(CharLine line) => Lists.FirstOrDefault(l => l.Id == line.ListId);

        private static string Cut(string s, int max) => s == null ? null : (s.Length <= max ? s : s.Substring(0, max));
    }

    public static class CharacteristicResolver
    {
        /// <summary>The in-force drawings of the part with the released characteristic list of each, and the «بدون نقشه» flag.</summary>
        public static async Task<ResolvedCharacteristics> ForItemAsync(IUnitOfWork uow, int itemId, int? operationTemplateId, CancellationToken ct)
        {
            var result = new ResolvedCharacteristics { ItemId = itemId };
            result.Drawings.AddRange(await EdmsResolver.DrawingsForItemAsync(uow, itemId, operationTemplateId, ct));
            List<int> revisionIds = result.Drawings.Select(d => d.RevisionId).ToList();
            if (revisionIds.Count > 0)
            {
                List<QcCharacteristicList> lists = await uow.Set<QcCharacteristicList>().AsNoTracking().Include(l => l.Items)
                    .Where(l => !l.Deleted && l.Status == CharListStatus.Released && revisionIds.Contains(l.RevisionId)).AsSplitQuery().ToListAsync(ct);
                foreach (PartDrawing d in result.Drawings)
                {
                    QcCharacteristicList l = lists.Where(x => x.RevisionId == d.RevisionId).OrderByDescending(x => x.Version).FirstOrDefault();
                    if (l == null) continue;
                    result.Lists.Add(l);
                    foreach (QcCharacteristic c in l.Items.OrderBy(x => x.RowNumber))
                        result.Lines.Add(new CharLine
                        {
                            Id = c.Id, ListId = l.Id, No = c.No, Name = c.Name, Nominal = c.Nominal, TolPlus = c.TolPlus, TolMinus = c.TolMinus, Unit = c.Unit, Class = c.Class,
                            InstrumentClass = c.InstrumentClass, Method = c.Method, SamplePercent = c.SamplePercent, OperationSeq = c.OperationSeq, StageCode2 = c.StageCode2,
                        });
                }
            }
            NoDrawingFlag flag = await uow.Set<NoDrawingFlag>().AsNoTracking().FirstOrDefaultAsync(f => !f.Deleted && f.ItemId == itemId && f.Status == NoDrawingStatus.Active, ct);
            if (flag != null) { result.NoDrawingActive = true; result.NoDrawingReason = flag.Reason; }
            return result;
        }

        public static CharLine ToLine(QcCharacteristic c) => new CharLine
        {
            Id = c.Id, ListId = c.ListId, No = c.No, Name = c.Name, Nominal = c.Nominal, TolPlus = c.TolPlus, TolMinus = c.TolMinus, Unit = c.Unit, Class = c.Class,
            InstrumentClass = c.InstrumentClass, Method = c.Method, SamplePercent = c.SamplePercent, OperationSeq = c.OperationSeq, StageCode2 = c.StageCode2,
        };
    }

    /// <summary>What a picked record (QC decision, incoming inspection, quality record, first article) gets from the released list.</summary>
    public sealed class PickInput
    {
        public int? CharacteristicId { get; set; }
        public string Name { get; set; }
        public decimal Measured { get; set; }
        public int? EquipmentId { get; set; }
    }

    public sealed class PickOutcome
    {
        public string Error { get; set; }

        /// <summary>True when a released list governs the part: measurements were matched to it and the completeness rules applied.</summary>
        public bool Governed { get; set; }
        public ResolvedCharacteristics Resolved { get; set; }
        public List<QcMeasurement> Measurements { get; } = new List<QcMeasurement>();
        public List<CharValue> Values { get; } = new List<CharValue>();
        public List<CharLine> Scope { get; set; } = new List<CharLine>();
        public bool AnyFailed => Measurements.Any(m => !m.Pass);

        /// <summary>Package G: the acceptance is allowed in the legacy mode (the drawing revision was released before the cut-over and has no characteristic list); the note is shown on the record. The caller asks for the evidence.</summary>
        public bool Legacy { get; set; }
        public string LegacyNote { get; set; }
    }

    /// <summary>
    /// Package B (H5): the one place where typed measurements become server-side facts. The inspector picks a characteristic of the released list and types only the measured value;
    /// nominal, tolerances and unit come from the list, pass or fail is computed here, the instrument class is checked and an acceptance needs every required characteristic.
    /// </summary>
    public static class CharacteristicPicker
    {
        /// <summary>Package G: true (and the outcome says so) when every drawing in force of the part was released before the cut-over: the acceptance is allowed without a list, with a visible note.</summary>
        private static async Task<bool> LegacyAsync(IUnitOfWork uow, ResolvedCharacteristics resolved, PickOutcome outcome, CancellationToken ct)
        {
            DateTimeOffset start = await global::Services.NasbNiro.GoLive.CutOver.StartAsync(uow, ct);
            if (!global::Services.NasbNiro.GoLive.CutOver.DrawingsAreLegacy(resolved.Drawings, start)) return false;
            outcome.Legacy = true;
            outcome.LegacyNote = $"پذیرش در حالت قدیمی: نقشه {resolved.DrawingNumber} بازنگری {resolved.DrawingRevision} پیش از آغاز کنترل پیکربندی ({(global::Services.NasbNiro.GoLive.CutOver.Text(start))}) منتشر شده و فهرست مشخصه ندارد؛ مقدار اسمی و تلرانس ثبت نشده و تصمیم با مدرک است. برای ورود به کنترل، «پذیرش مشخصه‌های نقشه موجود» را ثبت کنید.";
            return true;
        }

        public static async Task<PickOutcome> BuildAsync(IUnitOfWork uow, int? itemId, QcRecordType type, int? operationSeq, decimal quantity, QcResult result,
            IReadOnlyList<PickInput> inputs, CancellationToken ct, bool incoming = false)
        {
            var outcome = new PickOutcome();
            inputs ??= new List<PickInput>();
            if (!itemId.HasValue)
            {
                if (inputs.Count > 0) outcome.Error = "برای ثبت اندازه‌گیری، قطعه (کالا) را مشخص کنید تا مشخصه‌ها از نقشه منتشرشده خوانده شوند.";
                return outcome;
            }
            ResolvedCharacteristics resolved = await CharacteristicResolver.ForItemAsync(uow, itemId.Value, null, ct);
            outcome.Resolved = resolved;
            if (!resolved.HasList)
            {
                if (inputs.Count > 0)
                    outcome.Error = resolved.HasDrawing
                        ? "برای نقشه منتشرشده این قطعه فهرست مشخصه‌های منتشرشده وجود ندارد؛ مهندسی باید فهرست مشخصه‌ها را تهیه و با نقشه منتشر کند. اندازه‌گیری با نام و تلرانس دستی پذیرفته نمی‌شود."
                        : "برای این قطعه نقشه منتشرشده و فهرست مشخصه وجود ندارد؛ اندازه‌گیری با نام و تلرانس دستی پذیرفته نمی‌شود.";
                else if (!incoming && resolved.HasDrawing && result == QcResult.Accept && type != QcRecordType.Dimensional && type != QcRecordType.FirstArticle
                    && await LegacyAsync(uow, resolved, outcome, ct)) { /* legacy mode: no typed nominal or tolerance, the decision needs evidence (checked by the caller) */ }
                else if (!incoming && resolved.HasDrawing && result == QcResult.Accept)
                    outcome.Error = "برای نقشه منتشرشده این قطعه فهرست مشخصه‌های منتشرشده وجود ندارد؛ بدون آن قبول بازرسی ممکن نیست. مهندسی باید فهرست مشخصه‌ها (یا فهرست خالی «بدون مشخصه قابل اندازه‌گیری» با دلیل) را تهیه و منتشر کند.";
                else if (!incoming && (type == QcRecordType.Dimensional || type == QcRecordType.FirstArticle))
                    outcome.Error = "بازرسی ابعادی و نمونه اول به فهرست مشخصه‌های منتشرشده نیاز دارد؛ برای این قطعه فهرستی منتشر نشده است.";
                return outcome;
            }
            outcome.Governed = true;
            outcome.Scope = CharacteristicRules.Scope(resolved.Lines, type, operationSeq);

            List<int> equipmentIds = inputs.Where(i => i.EquipmentId.HasValue).Select(i => i.EquipmentId.Value).Distinct().ToList();
            var equipment = (await uow.Set<MeasuringEquipment>().AsNoTracking().Where(e => equipmentIds.Contains(e.Id)).Select(e => new { e.Id, e.Code, e.Type }).ToListAsync(ct))
                .ToDictionary(e => e.Id);
            foreach (PickInput m in inputs)
            {
                CharLine line = CharacteristicRules.Find(resolved.Lines, m.CharacteristicId, m.Name);
                if (line == null)
                {
                    outcome.Error = $"مشخصه «{(string.IsNullOrWhiteSpace(m.Name) ? m.CharacteristicId?.ToString() : m.Name.Trim())}» در فهرست مشخصه‌های نقشه این قطعه نیست؛ از فهرست انتخاب کنید.";
                    return outcome;
                }
                if (!m.EquipmentId.HasValue)
                {
                    outcome.Error = $"برای اندازه‌گیری «{line.No} {line.Name}» تجهیز اندازه‌گیری (ابزار) را انتخاب کنید؛ مقدار بدون ابزار قابل پذیرش نیست.";
                    return outcome;
                }
                if (!equipment.TryGetValue(m.EquipmentId.Value, out var eq))
                {
                    outcome.Error = $"ابزار اندازه‌گیری انتخاب‌شده برای «{line.No} {line.Name}» در فهرست تجهیزات نیست.";
                    return outcome;
                }
                string instrument = CharacteristicRules.InstrumentProblem(line, eq.Type, eq.Code);
                if (instrument != null) { outcome.Error = instrument; return outcome; }
                outcome.Measurements.Add(new QcMeasurement
                {
                    Characteristic = line.Name, CharacteristicId = line.Id, CharacteristicNo = line.No, Nominal = line.Nominal, TolPlus = line.TolPlus, TolMinus = line.TolMinus,
                    Measured = m.Measured, Unit = line.Unit, EquipmentId = m.EquipmentId, Pass = CharacteristicRules.Pass(line, m.Measured),
                });
                outcome.Values.Add(new CharValue { CharacteristicId = line.Id, Measured = m.Measured });
            }

            if (result == QcResult.Accept)
            {
                if (outcome.AnyFailed)
                {
                    outcome.Error = "اندازه‌گیری خارج از تلرانس ثبت شده است؛ نتیجه «قبول» مجاز نیست (رد / گزارش عدم انطباق): " + string.Join("، ", CharacteristicRules.Failed(resolved.Lines, outcome.Values));
                    return outcome;
                }
                List<string> missing = CharacteristicRules.Missing(outcome.Scope, outcome.Values, quantity, type == QcRecordType.FirstArticle);
                if (missing.Count > 0)
                    outcome.Error = "قبول بازرسی ممکن نیست؛ مشخصه‌های الزامی اندازه‌گیری نشده‌اند: " + string.Join("، ", missing) + ".";
            }
            return outcome;
        }
    }
}
