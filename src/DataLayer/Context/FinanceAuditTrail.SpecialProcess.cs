using System;
using System.Linq;
using P = Entities.Model.NasbNiro.Production;
using Q = Entities.Model.NasbNiro.Quality;

namespace DataLayer.Context
{
    /// <summary>
    /// Package C (special processes and material verification): the process types, the qualifications, the run records, the supplier approvals, the material specifications and the certificate values join the
    /// tamper-evident chain (Package D mechanism). A direct edit of a limit, a result, a review, an approval or a certificate value in the database is then a row of the chain that nobody wrote.
    /// In its own file so that it merges cleanly with the other packages that add entries to <c>FinanceAuditTrail.cs</c>.
    /// </summary>
    internal static partial class FinanceAuditTrail
    {
        private static void AddSpecialProcess()
        {
            AuditPd(typeof(Q.SpProcessType), new[] { "Code", "Title", "RequiresMachine", "RequiresSurvey", "RequiredInstrumentClass", "ProcedureDeliverableId", "ExistingControl", "IsActive", "Deleted" }, "نوع فرایند ویژه");
            AuditPd(typeof(Q.SpParameter), new[] { "TypeId", "SortOrder", "Name", "Unit", "Min", "Max", "Required" }, "پارامتر فرایند ویژه");
            AuditPd(typeof(Q.SpQualification), new[] { "Number", "TypeId", "Title", "ProcedureDeliverableId", "ProcedureRevisionId", "ProcedureNumber", "ProcedureRevisionCode", "ValidFrom", "ValidTo", "Status", "SubmittedAt", "ApprovedByUserId", "ApprovedAt", "RejectReason", "VoidReason", "VoidedByUserId", "VoidedAt", "SupersedesId", "EditorUserIds", "Deleted" }, "صلاحیت فرایند ویژه");
            AuditPd(typeof(Q.SpQualLimit), new[] { "QualificationId", "SortOrder", "Name", "Unit", "Min", "Max", "Required" }, "حد پارامتر صلاحیت فرایند");
            AuditPd(typeof(Q.SpQualEquipment), new[] { "QualificationId", "Kind", "MachineId", "EquipmentId", "SurveyKind", "SurveyDate", "SurveyDueDate", "SurveyDocumentId" }, "تجهیز صلاحیت فرایند");
            AuditPd(typeof(Q.SpQualOperator), new[] { "QualificationId", "PersonId" }, "اپراتور صلاحیت فرایند");
            AuditPd(typeof(Q.SpRun), new[] { "Number", "TypeId", "QualificationId", "IsSubcontract", "SupplierId", "ReceiptDocumentId", "OrderId", "OperationId", "PartOperationId", "WorkOrderId", "ItemId", "LotId", "SerialNumbers", "Quantity", "OperatorPersonId", "MachineId", "StartedAt", "EndedAt", "ChartDocumentId", "CertificateDocumentId", "CertificateNumber", "Result", "Status", "ReviewStatus", "ReviewedByUserId", "ReviewedAt", "NcrId", "VoidReason", "VoidedByUserId", "VoidedAt", "ResultText", "Deleted" }, "اجرای فرایند ویژه");
            AuditPd(typeof(Q.SpRunValue), new[] { "RunId", "SortOrder", "Name", "Unit", "Value", "Min", "Max", "Pass" }, "مقدار پارامتر اجرای فرایند");
            AuditPd(typeof(Q.SpRunInstrument), new[] { "RunId", "EquipmentId" }, "ابزار اجرای فرایند");
            AuditPd(typeof(Q.SpSupplierApproval), new[] { "ProviderId", "TypeId", "Status", "ValidFrom", "ValidTo", "AccreditationNumber", "EvidenceDocumentId", "AuditDate", "ApprovedByUserId", "ApprovedAt", "RejectReason", "VoidReason", "VoidedByUserId", "VoidedAt", "EditorUserIds", "Deleted" }, "تایید تامین‌کننده برای فرایند ویژه");
            AuditPd(typeof(Q.MaterialSpec), new[] { "Grade", "Standard", "SpecKey", "RequiredCertificateType", "Version", "Status", "Reason", "SupersedesId", "SubmittedAt", "ApprovedByUserId", "ApprovedAt", "VoidReason", "ContentHash", "EditorUserIds", "Deleted" }, "مشخصات ماده");
            AuditPd(typeof(Q.MaterialSpecLimit), new[] { "SpecId", "SortOrder", "Kind", "Name", "Unit", "Min", "Max", "Required" }, "حد مشخصات ماده");
            AuditPd(typeof(Q.MaterialSpecItem), new[] { "SpecId", "ItemId" }, "کالای مشمول مشخصات ماده");
            AuditPd(typeof(Q.CertificateValue), new[] { "CertificateId", "SortOrder", "Kind", "Name", "Unit", "Value", "Min", "Max", "Status" }, "مقدار شیمیایی/مکانیکی گواهی");

            // existing rows that gained columns (Qualification, Wps and the operation standard are audited with every column)
            ExtendInspection(typeof(Q.MaterialCertificate), "HeatNumber", "Grade", "Standard", "SpecId", "InspectorOrganisation", "InspectorName", "InspectorStampNo", "ManufacturerSignatory", "Evaluation", "EvaluationText", "EvaluatedAt");
            ExtendInspection(typeof(P.MfOperation), "IsSpecialProcess", "SpecialProcessTypeId");
        }
    }
}
