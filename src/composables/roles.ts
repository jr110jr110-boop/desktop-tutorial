// Roles of the API (names in the JWT, upper case) and the role lists each endpoint accepts.
// The lists mirror the [Authorize(Roles = ...)] attributes of the controllers and the checks inside the services,
// so a page or a button is shown only to the people who can really use it. The API checks again on every call.
//
// The UI gives a user one role: the first one of the token (stored by the login page as "userRole").

export const Roles = {
  PMO: "PMO",
  COMMERCIAL: "COMMERCIAL",
  FINANCIAL: "FINANCIAL",
  ENGINEERING: "ENGINEERING",
  QC: "QC",
  WAREHOUSE: "WAREHOUSE",
  ASSEMBLER: "ASSEMBLER",
  ENGINEER: "ENGINEER",
  CONSTRUCTION: "CONSTRUCTION",
  SECURITY: "SECURITY",
  PLANNING: "PLANNING",
  WHSUPERVISOR: "WHSUPERVISOR",
  CEO: "CEO",
  QA: "QA",
  PROJECTMANAGER: "PROJECTMANAGER",
  DCC: "DCC",
} as const;

export const roleTitles: Record<string, string> = {
  PMO: "معاونت PMO",
  COMMERCIAL: "بازرگانی",
  FINANCIAL: "مالی",
  ENGINEERING: "مهندسی",
  QC: "کنترل کیفیت",
  WAREHOUSE: "انبار",
  ASSEMBLER: "مونتاژ",
  ENGINEER: "مهندس",
  CONSTRUCTION: "تولید",
  SECURITY: "انتظامات (کنترل خروج)",
  PLANNING: "برنامه‌ریزی و کنترل پروژه",
  WHSUPERVISOR: "سرپرست انبار",
  CEO: "مدیرعامل",
  QA: "تضمین کیفیت",
  PROJECTMANAGER: "مدیر پروژه",
  DCC: "کنترل مدارک پروژه",
};

const list = (csv: string) => csv.split(",");

/** Warehouse (InventoryRoles in the API). */
export const InventoryRoles = {
  masterData: list("PMO,PLANNING,WAREHOUSE,WHSUPERVISOR,ENGINEERING"),
  documents: list("PMO,WAREHOUSE,WHSUPERVISOR"),
  counts: list("PMO,WAREHOUSE,WHSUPERVISOR,PLANNING"),
  production: list("PMO,WAREHOUSE,ENGINEERING,ENGINEER,ASSEMBLER,QC"),
  routing: list("PMO,ENGINEERING"),
  qc: list("PMO,QC"),
  finance: list("PMO,FINANCIAL"),
  materialRequest: list("PMO,PLANNING,CONSTRUCTION,ENGINEER,ASSEMBLER,ENGINEERING,WAREHOUSE,WHSUPERVISOR"),
  approvers: list("PMO,PLANNING,WHSUPERVISOR,CONSTRUCTION,QC,ENGINEERING,WAREHOUSE,FINANCIAL"),
  exitCheck: list("PMO,WAREHOUSE,WHSUPERVISOR,SECURITY"),
  itemRequest: list("PMO,PLANNING,ENGINEERING,ENGINEER,WAREHOUSE,WHSUPERVISOR,QC,CONSTRUCTION"),
  planning: list("PMO,PLANNING"),
  // single roles checked inside the document service
  approveWarehouse: ["WHSUPERVISOR"],
  approvePlanning: ["PLANNING", "PMO"],
  approveQuality: ["QC"],
  executeNcr: list("PMO,QC,WAREHOUSE"),
};

/** Finance controller. */
export const FinanceRoles = {
  ceo: list("CEO,PMO"),
  readers: list("PMO,CEO,FINANCIAL,PLANNING"),
  writers: list("PMO,FINANCIAL"),
};

/** Procurement (ProcurementRoles in the API). */
export const ProcurementRoles = {
  requesters: list("PMO,PLANNING,WAREHOUSE,WHSUPERVISOR,ENGINEERING,ENGINEER,CONSTRUCTION,QC,ASSEMBLER,COMMERCIAL"),
  planning: list("PLANNING,PMO"),
  management: list("CEO,PMO"),
  commercial: list("COMMERCIAL,PMO"),
  technical: list("ENGINEERING,QC,CONSTRUCTION,PMO"),
  orderApproval: list("PMO,CEO"),
  finance: list("FINANCIAL,PMO"),
  serviceAcceptance: list("QC,ENGINEERING,CONSTRUCTION,PLANNING,PMO"),
  // the controller adds ASSEMBLER and ENGINEER to the readers of the services
  readers: list("PMO,CEO,PLANNING,COMMERCIAL,FINANCIAL,WAREHOUSE,WHSUPERVISOR,ENGINEERING,QC,CONSTRUCTION,ASSEMBLER,ENGINEER"),
  // the financial committee approval is by the PMO deputy only (see ProcurementService.InquiryActionAsync)
  committeeFinancial: ["PMO"],
};

/** The role of the signed-in user (upper case), or "" when it is unknown. */
export function currentRole(): string {
  try {
    const raw = localStorage.getItem("userRole") || "";
    return raw.split(",")[0].trim().toUpperCase();
  } catch {
    return "";
  }
}

export function hasRole(allowed: string[] | undefined | null): boolean {
  if (!allowed || allowed.length === 0) return true;
  return allowed.includes(currentRole());
}

/** The access list is stored by the login page as a comma separated text. */
export function hasAccess(claim: string | undefined | null): boolean {
  if (!claim) return true;
  try {
    return (localStorage.getItem("accessList") || "").split(",").includes(claim);
  } catch {
    return false;
  }
}

/**
 * Quality module (QualityRoles in the API). The controller only requires a reader role on the GET endpoints;
 * the write roles are checked inside the services, so they are listed here to show or hide the buttons.
 */
export const QualityRoles = {
  readers: list("PMO,CEO,QC,QA,ENGINEERING,ENGINEER,CONSTRUCTION,ASSEMBLER,PLANNING,WAREHOUSE,WHSUPERVISOR,COMMERCIAL"),
  /** only QC inspects, tests, calibrates, notifies and releases control points */
  inspectors: ["QC"],
  managers: list("QC,QA,PMO"),
  engineering: list("ENGINEERING,ENGINEER"),
  /** ITP, WPS and PQR are prepared by quality and engineering */
  planners: list("QC,QA,PMO,ENGINEERING,ENGINEER"),
  /** acceptance limits of an ITP parameter */
  limits: list("ENGINEERING,ENGINEER,PMO"),
  /** inspection records: QC always; operators only for activities that are not on an ITP (self-inspection) */
  recordWriters: list("QC,PMO,ENGINEERING,ENGINEER,CONSTRUCTION,ASSEMBLER"),
  capaStaff: list("QA,QC,PMO"),
  capaClosers: list("QA,QC"),
  certificateWriters: list("QC,QA,PMO,WAREHOUSE,WHSUPERVISOR,ENGINEERING"),
  documentApprovers: list("QA,PMO,CEO"),
  documentReviewers: list("QA,QC,PMO,ENGINEERING,ENGINEER"),
  audit: list("QA,PMO"),
  review: list("QA,PMO,CEO"),
  complaints: list("QA,QC,PMO,COMMERCIAL"),
  settings: list("QA,PMO,CEO"),
};

/**
 * Project control (PMO) module (ProjectControlRoles in PcCommon.cs). The GET endpoints accept the readers; every write checks
 * its role inside the service (segregation of duties), so these lists only decide which buttons are shown.
 */
export const ProjectControlRoles = {
  readers: list("PMO,CEO,PROJECTMANAGER,PLANNING,DCC,COMMERCIAL,FINANCIAL,ENGINEERING,ENGINEER,QC,QA,CONSTRUCTION,ASSEMBLER,WAREHOUSE,WHSUPERVISOR"),
  /** planning manager; the PMO deputy can do the same */
  planning: list("PLANNING,PMO"),
  projectManagers: list("PROJECTMANAGER,PMO"),
  planningOrPm: list("PLANNING,PROJECTMANAGER,PMO"),
  commercial: list("COMMERCIAL,PMO"),
  ceo: ["CEO"],
  pmo: ["PMO"],
  /** engineering documents: DCC and engineering move the document states */
  dcc: list("DCC,ENGINEERING,ENGINEER,PMO"),
  dccOnly: list("DCC,PMO"),
  /** unit managers propose manual progress, confirm daily reports and register change requests */
  unitManagers: list("ENGINEERING,ENGINEER,CONSTRUCTION,ASSEMBLER,QC,QA,COMMERCIAL,WAREHOUSE,WHSUPERVISOR,PLANNING,PROJECTMANAGER,PMO"),
  terms: list("PMO,PLANNING,COMMERCIAL,PROJECTMANAGER"),
  initiationStart: list("PMO,PLANNING,COMMERCIAL"),
  charter: list("PMO,PLANNING,PROJECTMANAGER"),
  reviews: list("COMMERCIAL,PMO,PLANNING"),
  /** guarantees, milestone payment steps */
  commercialFinance: list("COMMERCIAL,FINANCIAL,PMO"),
  penalties: list("COMMERCIAL,PMO,PROJECTMANAGER,PLANNING,CEO"),
  variations: list("COMMERCIAL,PMO,PROJECTMANAGER"),
  costs: list("FINANCIAL,PMO"),
  finalBooks: list("DCC,PMO,QC"),
  mdrDraft: list("PLANNING,DCC,ENGINEERING,PMO"),
  mdrTemplates: list("ENGINEERING,DCC,PMO"),
  internalApprove: list("ENGINEERING,PMO"),
  shipmentPack: list("PLANNING,PMO,WAREHOUSE,WHSUPERVISOR"),
  shipmentDeliver: list("PLANNING,PMO,COMMERCIAL,WAREHOUSE,WHSUPERVISOR"),
  shipmentHandover: list("PLANNING,PMO,COMMERCIAL"),
  reportGenerate: list("PLANNING,PROJECTMANAGER,PMO,CEO"),
  claims: list("PLANNING,PROJECTMANAGER,PMO,COMMERCIAL"),
  claimSettle: list("COMMERCIAL,PMO,CEO"),
  claimDesign: list("ENGINEERING,CONSTRUCTION,PMO"),
};
