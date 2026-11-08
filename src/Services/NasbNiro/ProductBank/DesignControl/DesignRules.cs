using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.ProductBankDtos;

namespace Services.NasbNiro.ProductBank
{
    /// <summary>Package A1: the settings of the design control (store Quality, whole numbers; every one is on the safety profile).</summary>
    public static class DesignSettingKeys
    {
        /// <summary>1: releasing a product BOM revision or a design-output drawing revision, the project engineering readiness and the functional configuration audit need a closed Accepted design verification.</summary>
        public const string VerificationRequired = "Design.VerificationRequired";

        /// <summary>1: the functional configuration audit at release needs a closed Accepted design validation too (a plan can waive it for its product with a reason and the approval of QA).</summary>
        public const string ValidationRequired = "Design.ValidationRequired";

        /// <summary>The number of participants of a review who are not the author of any reviewed output.</summary>
        public const string MinIndependent = "Design.MinIndependentReviewers";

        /// <summary>The shortest reason (characters) of a conflict resolution, a waiver, a conditional acceptance and a void.</summary>
        public const string ReasonMinChars = "Design.ReasonMinChars";

        /// <summary>Fix round 1 (H6): the cut-over day as a Shamsi number yyyyMMdd; 0 = the day recorded by the first gate decision (PbDesignActivation). Revisions released before it are legacy.</summary>
        public const string ControlStartDate = "Design.ControlStartDate";
        public const string Source = "★ تصمیم مدیرعامل (ISO 9001 بند 8.3 و ISO 10007؛ عدم تحمل انحراف)";

        public static IEnumerable<QualitySetting> Defaults()
        {
            yield return new QualitySetting { Key = VerificationRequired, Value = 1, Title = "انتشار BOM و نقشه خروجی طراحی، آمادگی پروژه و ممیزی پیکربندی عملکردی: بازبینی تایید طراحی (Verification) بسته‌شده و پذیرفته", Source = Source };
            yield return new QualitySetting { Key = ValidationRequired, Value = 1, Title = "ممیزی پیکربندی عملکردی هنگام ترخیص: صحه‌گذاری طراحی (Validation) بسته‌شده و پذیرفته", Source = Source };
            yield return new QualitySetting { Key = MinIndependent, Value = 1, Title = "حداقل شرکت‌کننده مستقل (غیر از تهیه‌کنندگان خروجی‌ها) در هر بازبینی طراحی", Source = Source };
            yield return new QualitySetting { Key = ControlStartDate, Value = 0, Title = "روز آغاز کنترل طراحی (تاریخ شمسی به‌صورت yyyyMMdd؛ ۰ = روز ثبت‌شده در نخستین تصمیم گیت): بازنگری‌های منتشرشده پیش از آن «قدیمی، بدون تایید طراحی» شمرده می‌شوند", Source = Source };
            yield return new QualitySetting { Key = ReasonMinChars, Value = 15, Title = "حداقل نویسه دلیل (رفع تعارض ورودی، معافیت صحه‌گذاری، پذیرش مشروط، ابطال)", Source = Source };
        }

        public static bool TryGetBounds(string key, out int min, out int max)
        {
            min = 0; max = 0;
            switch (key)
            {
                case VerificationRequired: case ValidationRequired: max = 1; return true;
                case MinIndependent: min = 1; max = 5; return true;
                case ReasonMinChars: min = 5; max = 200; return true;
                case ControlStartDate: min = 0; max = 99991231; return true;
                default: return false;
            }
        }
    }

    /// <summary>Roles of the design control. The services check the role of each step again; reads need a signed-in user only.</summary>
    /// <summary>Fix round 1 (M3): the maximum length of every free-text field; it is the column length (DesignControlConfigurations), so a too long text is refused in Persian and never reaches the database.</summary>
    public static class DesignLimits
    {
        public const int Note = 1000, Reason = 1000, Description = 1000, Evidence = 1000, EvidenceRef = 300, Minutes = 4000, Outcome = 2000, ValidationEvidence = 2000, CustomerRef = 500, Title = 300, Scope = 2000,
            StageNote = 1000, Role = 40, RoleText = 100, Interface = 500, Group = 100, AdoptionEvidence = 500;

        public static string Over(string text, int max, string label) => text != null && text.Trim().Length > max ? $"{label} بیش از {max.ToString(System.Globalization.CultureInfo.InvariantCulture)} نویسه است." : null;
    }

    public static class DesignRoles
    {
        /// <summary>Prepares a plan (engineering).</summary>
        public const string PlanMakers = "ENGINEERING,ENGINEER";

        /// <summary>Approves a plan: the engineering manager or QA, never the preparer. A plan that waives the validation is approved by QA only.</summary>
        public const string PlanApprovers = "ENGINEERING,QA";
        public const string QaOnly = "QA";

        /// <summary>Enters, changes and replaces design inputs.</summary>
        public const string InputWriters = "ENGINEERING,ENGINEER,QA,PMO";

        /// <summary>Resolves a conflict between inputs and states that the inputs are complete (a second person).</summary>
        public const string Resolvers = "ENGINEERING,QA,PMO";

        /// <summary>Starts a stage, plans and holds a review, records findings and evidence.</summary>
        public const string ReviewMakers = "ENGINEERING,ENGINEER,QA,PMO";

        /// <summary>Closes a review and a finding, voids a finding.</summary>
        public const string ReviewClosers = "ENGINEERING,QA,PMO";
    }

    /// <summary>Texts and pure rules of the design control (Persian texts are the user-facing ones).</summary>
    public static class DesignRules
    {
        public const string AiNever = "تایید، بستن و قضاوت بازبینی طراحی با انسان است؛ صندلی هوش مصنوعی آن را انجام نمی‌دهد.";
        public const string Concurrent = "همزمان کس دیگری همین سند را تغییر داد؛ صفحه را دوباره بارگذاری کنید.";
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string SamePerson = "تفکیک وظایف: این کار را فردی غیر از تهیه‌کننده یا سازنده سند انجام می‌دهد.";

        public static readonly DesignInputKind[] AllKinds = (DesignInputKind[])Enum.GetValues(typeof(DesignInputKind));

        public static string KindText(DesignInputKind k)
        {
            switch (k)
            {
                case DesignInputKind.Functional: return "الزام کارکردی";
                case DesignInputKind.Performance: return "الزام عملکردی";
                case DesignInputKind.Statutory: return "الزام قانونی و مقرراتی";
                case DesignInputKind.Standard: return "استاندارد و آیین‌نامه";
                case DesignInputKind.PreviousDesign: return "طراحی قبلی مشابه";
                case DesignInputKind.CustomerSpec: return "مشخصات مشتری (قرارداد)";
                case DesignInputKind.FailureConsequence: return "پیامد بالقوه شکست";
                default: return k.ToString();
            }
        }

        public static string StageText(DesignStageKind k)
        {
            switch (k)
            {
                case DesignStageKind.Inputs: return "ورودی‌های طراحی";
                case DesignStageKind.Design: return "طراحی";
                case DesignStageKind.DesignReview: return "بازبینی طراحی";
                case DesignStageKind.Verification: return "تایید طراحی (Verification)";
                case DesignStageKind.Validation: return "صحه‌گذاری طراحی (Validation)";
                case DesignStageKind.Release: return "انتشار";
                default: return k.ToString();
            }
        }

        public static string StageStatusText(DesignStageStatus s)
        {
            switch (s)
            {
                case DesignStageStatus.NotStarted: return "شروع نشده";
                case DesignStageStatus.InProgress: return "در جریان";
                case DesignStageStatus.Done: return "انجام شد";
                default: return "لازم نیست";
            }
        }

        public static string TypeText(DesignReviewType t)
        {
            switch (t)
            {
                case DesignReviewType.Review: return "بازبینی (Review)";
                case DesignReviewType.Verification: return "تایید (Verification)";
                default: return "صحه‌گذاری (Validation)";
            }
        }

        public static string OutcomeText(DesignReviewOutcome o)
        {
            switch (o)
            {
                case DesignReviewOutcome.Accepted: return "پذیرفته";
                case DesignReviewOutcome.AcceptedWithConditions: return "پذیرفته با شرط";
                case DesignReviewOutcome.Rejected: return "رد";
                default: return "-";
            }
        }

        public static string OutputKindText(DesignOutputKind k) => k == DesignOutputKind.Drawing ? "نقشه / سند (EDMS)" : k == DesignOutputKind.Bom ? "BOM محصول" : "مسیر ساخت (OPC)";

        public static string FindingStatusText(DesignFindingStatus s) => s == DesignFindingStatus.Open ? "باز" : s == DesignFindingStatus.Done ? "اقدام انجام شد (در انتظار بستن)" : s == DesignFindingStatus.Closed ? "بسته" : "باطل";

        public static string InputStatusText(DesignInputStatus s) => s == DesignInputStatus.Open ? "معتبر" : s == DesignInputStatus.Conflict ? "تعارض" : s == DesignInputStatus.Superseded ? "جایگزین‌شده" : "باطل";

        public static string Rev(int revision) => PbText.Revision(revision);

        public static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);

        /// <summary>The stages every plan starts with (★ inputs, design, design review, verification, validation, release) and the role that normally owns each.</summary>
        public static IEnumerable<(DesignStageKind Kind, string Role)> DefaultStages()
        {
            yield return (DesignStageKind.Inputs, "ENGINEERING");
            yield return (DesignStageKind.Design, "ENGINEER");
            yield return (DesignStageKind.DesignReview, "ENGINEERING");
            yield return (DesignStageKind.Verification, "QA");
            yield return (DesignStageKind.Validation, "QA");
            yield return (DesignStageKind.Release, "ENGINEERING");
        }

        public static string Join(IEnumerable<int> kinds) => string.Join(",", (kinds ?? new int[0]).Distinct().OrderBy(x => x).Select(x => x.ToString(CultureInfo.InvariantCulture)));

        public static List<int> ParseKinds(string text) =>
            (text ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0).Where(v => v > 0).Distinct().ToList();

        /// <summary>The Persian reason an input cannot be saved (null = fine).</summary>
        public static string InputProblem(DesignInputKind kind, string statement, string source, string criterion, string edition, string previousRef, int? refDeliverable, int? refRevision, string refText, string refRevisionText)
        {
            if (!Enum.IsDefined(typeof(DesignInputKind), kind)) return "نوع ورودی طراحی معتبر نیست.";
            if (string.IsNullOrWhiteSpace(statement)) return "متن الزام ورودی را بنویسید.";
            if (string.IsNullOrWhiteSpace(source)) return "منبع ورودی را بنویسید (قرارداد، استاندارد، نیاز مشتری، طراحی قبلی ...).";
            if (string.IsNullOrWhiteSpace(criterion)) return "معیار پذیرش ورودی را بنویسید؛ بدون آن تایید طراحی ممکن نیست.";
            if (statement.Trim().Length > 1000) return "متن الزام بیش از ۱۰۰۰ نویسه است.";
            if (source.Trim().Length > 500) return "منبع بیش از ۵۰۰ نویسه است.";
            if (criterion.Trim().Length > 1000) return "معیار پذیرش بیش از ۱۰۰۰ نویسه است.";
            if (kind == DesignInputKind.Standard && string.IsNullOrWhiteSpace(edition)) return "برای استاندارد یا آیین‌نامه شماره و ویرایش (سال انتشار) را بنویسید.";
            if (kind == DesignInputKind.PreviousDesign && string.IsNullOrWhiteSpace(previousRef)) return "برای ورودی از طراحی قبلی، طراحی مرجع را بنویسید (شماره برنامه طراحی، کد محصول یا سند).";
            if (kind == DesignInputKind.CustomerSpec)
            {
                bool edms = refDeliverable.HasValue && refRevision.HasValue;
                bool text = !string.IsNullOrWhiteSpace(refText) && !string.IsNullOrWhiteSpace(refRevisionText);
                if (!edms && !text) return "برای مشخصات مشتری، سند قرارداد یا مدرک EDMS همراه با شماره بازنگری آن را مشخص کنید.";
            }
            if (!string.IsNullOrWhiteSpace(edition) && edition.Trim().Length > 200) return "ویرایش استاندارد بیش از ۲۰۰ نویسه است.";
            if (!string.IsNullOrWhiteSpace(previousRef) && previousRef.Trim().Length > 300) return "مرجع طراحی قبلی بیش از ۳۰۰ نویسه است.";
            if (!string.IsNullOrWhiteSpace(refText) && refText.Trim().Length > 500) return "مرجع مشخصات بیش از ۵۰۰ نویسه است.";
            if (!string.IsNullOrWhiteSpace(refRevisionText) && refRevisionText.Trim().Length > 100) return "شماره بازنگری مرجع بیش از ۱۰۰ نویسه است.";
            return null;
        }

        /// <summary>The inputs that matter for a review type: a verification judges every live input; a validation judges what the product must do (function, performance, customer specification).</summary>
        public static bool InScope(DesignReviewType type, DesignInputKind kind) =>
            type == DesignReviewType.Verification || (type == DesignReviewType.Validation && (kind == DesignInputKind.Functional || kind == DesignInputKind.Performance || kind == DesignInputKind.CustomerSpec));

        /// <summary>
        /// The inputs of a review that its judgements do not cover. Fix round 1: the scope is the inputs the review selected (a supplementary review for the new inputs can close; the GATE decides over all live inputs). An input is
        /// covered when it has a PASSED criterion against every BOM of the review (or, with no BOM, against some output) and no failed one anywhere; one check does not cover everything.
        /// </summary>
        public static List<DesignUncoveredDto> Uncovered(DesignReviewType type, IEnumerable<PbDesignInput> liveInputs, ISet<int> reviewInputIds, IEnumerable<PbDesignTrace> traces, IReadOnlyCollection<(int Id, DesignOutputKind Kind, string Label)> outputs)
        {
            var result = new List<DesignUncoveredDto>();
            List<PbDesignTrace> rows = (traces ?? new PbDesignTrace[0]).ToList();
            var boms = outputs.Where(o => o.Kind == DesignOutputKind.Bom).ToList();
            foreach (PbDesignInput input in liveInputs.Where(i => InScope(type, i.Kind) && reviewInputIds.Contains(i.Id)).OrderBy(i => i.Code, StringComparer.Ordinal))
            {
                string reason = null;
                if (input.Status == DesignInputStatus.Conflict) reason = "ورودی در وضعیت تعارض است و هنوز رفع نشده است.";
                else if (rows.Any(t => t.InputId == input.Id && t.Result == DesignTraceResult.Failed)) reason = "معیار پذیرش این ورودی «رد» شده است.";
                else if (boms.Count > 0)
                {
                    var lacking = boms.FirstOrDefault(o => !rows.Any(t => t.InputId == input.Id && t.OutputId == o.Id && t.Result == DesignTraceResult.Passed));
                    if (lacking.Label != null || lacking.Id != 0) reason = $"برای {lacking.Label} معیار پذیرش قبول‌شده‌ای با مدرک ثبت نشده است (هر ورودی در برابر هر BOM جداگانه قضاوت می‌شود).";
                }
                else if (!rows.Any(t => t.InputId == input.Id && t.Result == DesignTraceResult.Passed)) reason = "هیچ معیار پذیرش قبول‌شده‌ای با مدرک برای این ورودی ثبت نشده است.";
                if (reason != null) result.Add(new DesignUncoveredDto { InputId = input.Id, Code = input.Code, Statement = input.Statement, Reason = reason });
            }
            return result;
        }

        /// <summary>The outputs of a review with no passed criterion at all or with a failed one (an output with zero checks is not verified).</summary>
        public static List<string> OutputsWithoutEvidence(IEnumerable<PbDesignTrace> traces, IEnumerable<(int Id, DesignOutputKind Kind, string Label)> outputs)
        {
            List<PbDesignTrace> rows = (traces ?? new PbDesignTrace[0]).ToList();
            var list = new List<string>();
            foreach (var o in outputs)
            {
                if (rows.Any(t => t.OutputId == o.Id && t.Result == DesignTraceResult.Failed)) list.Add($"{o.Label}: دست‌کم یک معیار مردود شده است.");
                else if (!rows.Any(t => t.OutputId == o.Id && t.Result == DesignTraceResult.Passed)) list.Add($"{o.Label}: هیچ معیار قبول‌شده‌ای برای این خروجی ثبت نشده است.");
            }
            return list;
        }

        public static List<DesignInputKind> MissingKinds(IEnumerable<PbDesignInput> liveInputs, IEnumerable<int> notApplicable)
        {
            HashSet<int> na = new HashSet<int>(notApplicable ?? new int[0]);
            HashSet<DesignInputKind> have = new HashSet<DesignInputKind>(liveInputs.Select(i => i.Kind));
            return AllKinds.Where(k => !have.Contains(k) && !na.Contains((int)k)).ToList();
        }

        /// <summary>SHA-256 of what the preparer submitted: fields, stages (role, person, dates) and interfaces. Database ids are not part of it.</summary>
        public static string PlanHash(PbDesignPlan plan, IEnumerable<PbDesignStage> stages, IEnumerable<PbDesignInterface> interfaces)
        {
            var sb = new StringBuilder();
            sb.Append(plan.ProductId).Append('|').Append(plan.ProductRevision).Append('|').Append(plan.Title).Append('|').Append(plan.Scope).Append('|').Append(plan.ValidationRequired ? 1 : 0).Append('|').Append(plan.ValidationWaiverReason).Append('\n');
            foreach (PbDesignStage s in stages.Where(x => !x.Deleted).OrderBy(x => x.Seq))
                sb.Append((int)s.Kind).Append('|').Append(s.ResponsibleRole).Append('|').Append(s.ResponsibleUserId).Append('|').Append(s.PlannedStart?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|').Append(s.PlannedEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
            foreach (PbDesignInterface i in interfaces.Where(x => !x.Deleted).OrderBy(x => x.FromGroup, StringComparer.Ordinal).ThenBy(x => x.ToGroup, StringComparer.Ordinal).ThenBy(x => x.Subject, StringComparer.Ordinal))
                sb.Append(i.FromGroup).Append('|').Append(i.ToGroup).Append('|').Append(i.Subject).Append('|').Append(i.ResponsibleRole).Append('|').Append(i.ResponsibleUserId).Append('\n');
            return PbText.Sha256(sb.ToString());
        }

        /// <summary>The independent participants: those who are not in the set of authors of any reviewed output.</summary>
        public static int IndependentCount(IEnumerable<int> participantUserIds, ISet<int> authors) => participantUserIds.Distinct().Count(u => !authors.Contains(u));

        public static string Quote(string s) => string.IsNullOrWhiteSpace(s) ? "-" : s.Trim();
    }
}
