namespace Entities.Model.NasbNiro.Quality
{
    // Quality module (QC + QA) release 1. Enum values are stored as numbers and must not be renumbered.

    public enum ItpProductType
    {
        SteamTurbine = 1,
        OrcUnit = 2,
        Condenser = 3,
        Pump = 4,
        LubeOilSystem = 5,
        Valve = 6,
        Other = 9,
    }

    /// <summary>What an ITP activity checks (WI-QC-004 legend + the methods the procedures lack, spec 5.2).</summary>
    public enum QaTestType
    {
        ChemicalTest = 1,
        MechanicalTest = 2,
        Dimensional = 3,
        Visual = 4,
        UT = 5,
        MT = 6,
        PT = 7,
        RT = 8,
        PMI = 9,
        Hardness = 10,
        Hydro = 11,
        RunTest = 12,
        Performance = 13,
        Balancing = 14,
        Painting = 15,
        VisualWeld = 16,
        DocumentReview = 17,
        Packing = 18,
        Other = 99,
    }

    /// <summary>Inspection point of one party (WI-QC-004): hold, witness, review, review and acceptance, spot witness, approve.</summary>
    public enum ItpPoint
    {
        None = 0,
        H = 1,
        W = 2,
        R = 3,
        RA = 4,
        SW = 5,
        A = 6,
    }

    public enum TestReportType
    {
        PT = 1,
        MT = 2,
        UT = 3,
        RT = 4,
        Hardness = 5,
        PMI = 6,
        Hydro = 7,
        RunTest = 8,
        Performance = 9,
        Balancing = 10,
        Painting = 11,
        Dimensional = 12,
        Other = 99,
    }

    public enum ItpStatus
    {
        Draft = 0,
        Submitted = 1,
        ClientApproved = 2,
        InUse = 3,
        Closed = 4,
    }

    public enum ItpActivityStatus
    {
        Planned = 0,
        Notified = 1,
        Done = 2,
        Waived = 3,
        NotApplicable = 4,
    }

    /// <summary>V = INNCO (vendor), TPI = third party, C = client.</summary>
    public enum ReleaseParty
    {
        Vendor = 1,
        Tpi = 2,
        Client = 3,
    }

    public enum ReleaseAction
    {
        Witnessed = 1,
        Reviewed = 2,
        Approved = 3,
        Waived = 4,
    }

    public enum QcRecordType
    {
        InProcess = 1,
        Final = 2,
        Dimensional = 3,
        FirstArticle = 4,
        ContractorShop = 5,
        Layout = 6,
    }

    public enum QcResult
    {
        Accept = 1,
        Reject = 2,
        Rework = 3,
        Hold = 4,
    }

    public enum TestReportStatus
    {
        Draft = 0,
        Submitted = 1,
        Approved = 2,
        Rejected = 3,
    }

    public enum ReportResult
    {
        NotEvaluated = 0,
        Pass = 1,
        Fail = 2,
    }

    public enum EquipmentType
    {
        DimensionalHandTool = 1,
        Gauge = 2,
        PressureGauge = 3,
        TemperatureInstrument = 4,
        ElectricalMeter = 5,
        NdtEquipment = 6,
        TestStandInstrument = 7,
        Other = 99,
    }

    public enum EquipmentStatus
    {
        Calibrated = 0,
        DueSoon = 1,
        Overdue = 2,
        OutOfService = 3,
        NotRequired = 4,
    }

    public enum OotStatus
    {
        Open = 0,
        Closed = 1,
    }

    public enum OotAssessment
    {
        Pending = 0,
        NoImpact = 1,
        Reinspect = 2,
        Ncr = 3,
    }

    public enum QualificationKind
    {
        Welder = 1,
        WeldingOperator = 2,
        NdtPersonnel = 3,
        Inspector = 4,
    }

    public enum NdtLevel
    {
        None = 0,
        I = 1,
        II = 2,
        III = 3,
    }

    public enum QualificationScheme
    {
        AsmeIX = 1,
        AsntSntTc1A = 2,
        Iso9712 = 3,
        Other = 9,
    }

    public enum EmployerKind
    {
        Own = 1,
        Contractor = 2,
        Provider = 3,
    }

    /// <summary>Computed status of a qualification (never stored).</summary>
    public enum QualificationState
    {
        Valid = 0,
        ExpiringSoon = 1,
        Expired = 2,
        Lapsed = 3,
        Revoked = 4,
    }

    public enum WpsStatus
    {
        Draft = 0,
        Approved = 1,
        Obsolete = 2,
    }

    public enum VtResult
    {
        Pending = 0,
        Accept = 1,
        Reject = 2,
    }

    /// <summary>EN 10204 inspection document types. The value is the rank: 3.2 is better than 3.1, better than 2.2.</summary>
    public enum CertificateType
    {
        T22 = 1,
        T31 = 2,
        T32 = 3,
    }

    public enum CertificateResult
    {
        Pending = 0,
        Accepted = 1,
        Rejected = 2,
    }

    public enum RootCauseMethod
    {
        None = 0,
        FiveWhy = 1,
        Fishbone = 2,
        EightD = 3,
        Other = 9,
    }

    public enum CapaSource
    {
        Ncr = 1,
        AuditFinding = 2,
        ManagementReview = 3,
        CustomerComplaint = 4,
        DataAnalysis = 5,
        Risk = 6,
        Other = 9,
    }

    public enum CapaStatus
    {
        Open = 0,
        ActionsDefined = 1,
        Implemented = 2,
        EffectivenessCheck = 3,
        Closed = 4,
        Cancelled = 5,
    }

    public enum ReleaseNoteStatus
    {
        Draft = 0,
        Released = 1,
        Cancelled = 2,
    }

    public enum ComplaintStatus
    {
        Received = 0,
        UnderReview = 1,
        Responded = 2,
        Closed = 3,
    }

    public enum ControlledDocumentType
    {
        PR = 1,
        WI = 2,
        FO = 3,
        BL = 4,
        Drawing = 5,
        Spec = 6,
        Other = 9,
    }

    public enum ControlledDocumentStatus
    {
        Draft = 0,
        InReview = 1,
        Approved = 2,
        Effective = 3,
        Obsolete = 4,
    }

    public enum AuditStatus
    {
        Planned = 0,
        Done = 1,
        Closed = 2,
    }

    public enum AuditFindingType
    {
        MajorNC = 1,
        MinorNC = 2,
        Observation = 3,
        Opportunity = 4,
    }

    public enum ReviewStatus
    {
        Draft = 0,
        Held = 1,
        Closed = 2,
    }

    /// <summary>Inputs of a management review (ISO 9001:2015 9.3.2 a-f and the additions of spec gap G-16).</summary>
    public enum ReviewInputCategory
    {
        PreviousActions = 1,
        ChangesInIssues = 2,
        CustomerSatisfaction = 3,
        ObjectivesAchievement = 4,
        ProcessPerformance = 5,
        NonconformitiesAndCorrectiveActions = 6,
        MonitoringAndMeasurement = 7,
        AuditResults = 8,
        ExternalProviderPerformance = 9,
        ResourceAdequacy = 10,
        RiskActionEffectiveness = 11,
        ImprovementOpportunities = 12,
    }
}
