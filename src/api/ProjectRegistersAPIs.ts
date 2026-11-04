import { call, clean } from "@/api/ApiClient";

// Progress-approval cartable and PMBOK project registers (api/v1/projectcontrol/progress-approval, .../registers).
const pa = "projectcontrol/progress-approval/";
const rg = "projectcontrol/registers/";

export const getChain = (contractId: number) => call("get", pa + "chain/" + contractId);
export const saveChain = (dto: any) => call("post", pa + "chain", dto);
/** cartable: mine, waiting, all. */
export const getSubmissions = (params?: any) => call("get", pa + "submissions", undefined, clean(params || {}));
export const getSubmission = (id: number) => call("get", pa + "submissions/" + id);
export const saveSubmission = (dto: any) => call("post", pa + "submissions", dto);
export const decideSubmission = (dto: any) => call("post", pa + "submissions/decide", dto);
export const getNotUpdated = (params?: any) => call("get", pa + "reports/not-updated", undefined, clean(params || {}));
export const getOverdueUpdates = () => call("get", pa + "reports/overdue-updates");

export const getOnePage = (contractId: number) => call("get", rg + "one-page/" + contractId);

export const getRisks = (params: any) => call("get", rg + "risks", undefined, clean(params));
export const saveRisk = (dto: any) => call("post", rg + "risks", dto);
export const riskStatus = (dto: any) => call("post", rg + "risks/status", dto);
export const getHeatMap = (contractId: number) => call("get", rg + "risks/heat-map/" + contractId);

export const getIssues = (params: any) => call("get", rg + "issues", undefined, clean(params));
export const saveIssue = (dto: any) => call("post", rg + "issues", dto);
export const issueAction = (dto: any) => call("post", rg + "issues/action", dto);

export const getStakeholders = (contractId: number) => call("get", rg + "stakeholders/" + contractId);
export const saveStakeholder = (dto: any) => call("post", rg + "stakeholders", dto);
export const getStakeholderMatrix = (contractId: number) => call("get", rg + "stakeholders/matrix/" + contractId);

export const getLessons = (params: any) => call("get", rg + "lessons", undefined, clean(params));
export const saveLesson = (dto: any) => call("post", rg + "lessons", dto);
export const decideLesson = (dto: any) => call("post", rg + "lessons/decide", dto);

export const getProjDeliverables = (params: any) => call("get", rg + "deliverables", undefined, clean(params));
export const saveProjDeliverable = (dto: any) => call("post", rg + "deliverables", dto);
export const projDeliverableAction = (dto: any) => call("post", rg + "deliverables/action", dto);

export const getPhotos = (params: any) => call("get", rg + "photos", undefined, clean(params));
export const savePhoto = (dto: any) => call("post", rg + "photos", dto);
export const voidPhoto = (dto: any) => call("post", rg + "photos/void", dto);

export const getEvaluationTemplate = (partyKind: number) => call("get", rg + "evaluations/template", undefined, { kind: partyKind });
export const getEvaluations = (params: any) => call("get", rg + "evaluations", undefined, clean(params));
export const saveEvaluation = (dto: any) => call("post", rg + "evaluations", dto);
export const decideEvaluation = (dto: any) => call("post", rg + "evaluations/decide", dto);

export const getWeeklyReports = (params: any) => call("get", rg + "weekly", undefined, clean(params));
export const generateWeekly = (dto: any) => call("post", rg + "weekly/generate", dto);
export const saveWeekly = (dto: any) => call("post", rg + "weekly", dto);
export const releaseWeekly = (id: number) => call("post", rg + "weekly/" + id + "/release");

export const getPhaseEvaluations = (params: any) => call("get", rg + "phase-evaluations", undefined, clean(params));
export const savePhaseEvaluation = (dto: any) => call("post", rg + "phase-evaluations", dto);
export const decidePhaseEvaluation = (dto: any) => call("post", rg + "phase-evaluations/decide", dto);
