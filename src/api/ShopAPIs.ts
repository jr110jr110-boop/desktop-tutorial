import { call, clean, http } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// Production release P2 parts B and C: live machine monitoring (api/v1/production/monitor), shop cameras (production/cameras), in-process checklists (production/checklists) and staff performance
// events (production/staff-events). Same conventions as ProductionAPIs.ts: [data, null] or [null, error]; refusals are Persian messages; "...Text" fields are Jalali.
type Method = "get" | "post";
async function fcall(method: Method, url: string, body?: any, params?: any): Promise<[any, null] | [null, Error | any]> {
  const [data, error] = await call(method, url, body, params);
  return error ? [null, error] : [camelize(data), null];
}
const get = (url: string, params?: any) => fcall("get", url, undefined, clean(params));
const post = (url: string, body?: any) => fcall("post", url, body);

// ---------------------------------------------------------------- live monitoring
/** MachineMonitorDto per machine (source 0 none, 1 Modbus inputs, 2 gateway push; host, port, unitId, function 1|2, startAddress, runInput, alarmInput, powerInput, invertMask, pollSeconds, alarmCreatesRequest, mismatchMinutes, lastSeenAtText, lastError, hasGatewayLink). */
export const getMonitors = (params?: any) => get("production/monitor/machines", params);
/** SaveMachineMonitorDto. Who: PRODUCTIONMANAGER, PMO. */
export const saveMonitor = (dto: any) => post("production/monitor/machines", dto);
/** MonitorInputsTestDto { ok, message, inputs[], state }. Who: link testers. */
export const testMonitorInputs = (machineId: number) => post("production/monitor/machines/test", { machineId });
/** MonitorPollResultDto. Who: supervisors. */
export const pollMonitors = () => post("production/monitor/poll");
/** LiveBoardDto { asOfText, machines: LiveMachineDto[], running, idle, off, alarm, offline, mismatches }. */
export const getLiveBoard = (workCenterId?: number | null) => get("production/monitor/live", { workCenterId });
/** params { MachineId, From, To, State } + paging; StateEventDto { machineTitle, state, startAtText, endAtText, minutes, alarmCode, alarmText, maintenanceRequestId }. */
export const getStateEvents = (params?: any) => get("production/monitor/events", params);
/** [MachineUtilisationDto { machineTitle, minutes {running, idle, off, alarm, offline}, runPercent, alarms, measuredRunHours, bookedHours, bookingGapPercent }]. From/To ISO. */
export const getUtilisation = (from: string, to: string, workCenterId?: number | null) => get("production/monitor/utilisation", { From: from, To: to, WorkCenterId: workCenterId });

// ---------------------------------------------------------------- cameras
/** [CameraDto { id, title, machineTitle, workCenterCode, hasStream, isActive, snapshotUrl/streamUrl/credentialKey (editors only) }]. Who: production readers. */
export const getCameras = () => get("production/cameras");
/** CameraDto (snapshotUrl and streamUrl: http(s) with a private LAN IP; credentialKey = name in the server settings). Who: PRODUCTIONMANAGER, PMO. */
export const saveCamera = (dto: any) => post("production/cameras", dto);
/** The current picture of a camera as an object URL (revoke it when replaced), or [null, message]. */
export async function cameraSnapshotUrl(id: number): Promise<[string | null, string | null]> {
  try {
    const r = await http.get("production/cameras/" + id + "/snapshot", { responseType: "blob" });
    const type = String(r.headers["content-type"] || "");
    if (type.includes("application/json")) {
      const json = JSON.parse(await (r.data as Blob).text());
      return [null, json?.message || "خطا"];
    }
    return [URL.createObjectURL(r.data as Blob), null];
  } catch (e: any) {
    return [null, e?.message || "دوربین در دسترس نیست."];
  }
}

// ---------------------------------------------------------------- in-process checklists
/** ChecklistTemplateDto { id, operationTemplateId, operationTitle, title, requiredToFinish, isActive, items [{ id, sequence, text, kind 1 yes-no | 2 measure | 3 text, nominal, min, max, unit, mandatory }], rowVersion }. */
export const getChecklistTemplate = (operationTemplateId: number) => get("production/checklists/templates/" + operationTemplateId);
/** Who: standard editors (PRODUCTIONMANAGER, ENGINEERING, ENGINEER, PMO) and QC, QA. */
export const saveChecklistTemplate = (dto: any) => post("production/checklists/templates", dto);
/** OperationChecklistDto { hasTemplate, template, status 0..3, allPassed, answers [{ itemId, yesNo, value, text, passed }], allowedActions (save, submit, approve, reject), rowVersion }. */
export const getOperationChecklist = (operationId: number) => get("production/checklists/operations/" + operationId);
/** SaveChecklistAnswersDto { operationId, answers[], submit, rowVersion }. Who: terminal roles and QC. */
export const saveChecklistAnswers = (dto: any) => post("production/checklists/answers", dto);
/** ChecklistReviewDto { operationId, action approve | reject, note, rowVersion }. Who: QC, SHOPSUPERVISOR, PRODUCTIONMANAGER (never the filler). */
export const reviewChecklist = (dto: any) => post("production/checklists/review", dto);

// ---------------------------------------------------------------- staff performance events
/** params HrEventQuery { EmployeeId, Kind 1..5, Status 0..3, From, To, Mine } + paging. HrEventDto { employeeName, kind, eventDateText, title, description, score, status, createdByName, decidedByName, decisionNote, allowedActions }. */
export const getStaffEvents = (params?: any) => get("production/staff-events", params);
/** SaveHrEventDto { id?, employeeId, kind, eventDate (ISO), title, description, score (1..5, evaluations only), ncrId?, operationId?, rowVersion }. */
export const saveStaffEvent = (dto: any) => post("production/staff-events", dto);
/** HrEventActionDto { id, action submit | approve | reject (note) | delete, note, rowVersion }. */
export const staffEventAction = (dto: any) => post("production/staff-events/action", dto);
/** [HrEmployeeSummaryDto] of the approved events between from and to (ISO). */
export const getStaffSummary = (from: string, to: string) => get("production/staff-events/summary", { from, to });
