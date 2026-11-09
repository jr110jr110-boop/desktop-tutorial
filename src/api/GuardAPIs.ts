import { call, clean } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// Production release P2 part E: the guarded machining process. api/v1/production/guard (data link to each control, start interlock, the machine's time line, the terminal's gate panel,
// the production manager's one-time overrides), api/v1/production/dnc/cam (post processors and the CAM evidence of a revision) and api/v1/production/measure (gauge stations and measurements).
// Same conventions as DncAPIs.ts: [data, null] or [null, error]; lists are paged ({ data, totalItems }) except where noted; enums are numbers (labels in composables/guard.ts).
type Method = "get" | "post";
async function fcall(method: Method, url: string, body?: any, params?: any): Promise<[any, null] | [null, Error | any]> {
  const [data, error] = await call(method, url, body, params);
  return error ? [null, error] : [camelize(data), null];
}
const get = (url: string, params?: any) => fcall("get", url, undefined, clean(params));
const post = (url: string, body?: any) => fcall("post", url, body);
const guard = "production/guard/";
const cam = "production/dnc/cam/";
const measure = "production/measure/";

// ---------------------------------------------------------------- data links and interlocks
/** MachineGuardDto[] (not paged): one per machine of the shop (live mode, program state, overrides, program, tool, link health, interlock state). Who: DncRoles.readers. */
export const getGuardMachines = (workCenterId?: number | null) => get(guard + "machines", { workCenterId });
/** SaveMachineGuardDto { machineId, dataLink 0/1/2, dataHost, dataPort, dataCredentialKey, plcMap, programFolder, resultFile, interlockEnabled, interlockHost, interlockPort, interlockUnitId, interlockCoil, interlockInvert }. Who: linkEditors. */
export const saveGuardMachine = (dto: any) => post(guard + "machines", dto);
/** GuardCapabilityDto { ok, message, lines[] }: what the control answers. Who: linkTesters. */
export const testGuardMachine = (machineId: number) => post(guard + "machines/test", { machineId });
/** GuardPollResultDto: polls every data link now. Who: supervisors. */
export const runGuardPoll = () => post(guard + "poll");
/** MachineActivityQuery { machineId, operationId, from, to } + paging; MachineActivityDto { id, machineTitle, operationId, atText, kind, kindText, value, text, tone }. */
export const getActivity = (params: any) => get(guard + "activity", params);
/** TerminalGuardDto { guarded, canStart, gates: [{ code, title, state ok/blocked/skipped/pending, text, overridden }], machine, overrides[] }: a look only, nothing is used up. */
export const getTerminalGuard = (operationId: number, machineId: number) => get(guard + "terminal", { operationId, machineId });
/** GateOverrideQuery { machineId, activeOnly } + paging. */
export const getOverrides = (params?: any) => get(guard + "overrides", params);
/** CreateGateOverrideDto { machineId, operationId, gate: program/tools/prove-out/interlock/measurement, reason (10+ characters) }. Who: PRODUCTIONMANAGER only; 30 minutes, used once. */
export const createOverride = (dto: { machineId: number; operationId: number; gate: string; reason: string }) => post(guard + "overrides", dto);

// ---------------------------------------------------------------- CAM evidence
/** PostProcessorQuery { contains, status } + paging. */
export const getPosts = (params?: any) => get(cam + "posts", params);
/** SavePostProcessorDto { code, title, camSystem, controllerFamily, machineId, fileName, contentBase64, changeNote }: a new version (draft). Who: DncRoles.writers. */
export const createPost = (dto: any) => post(cam + "posts", dto);
/** PostProcessorActionDto { id, action submit/withdraw/approve/reject/retire, reason, rowVersion }. */
export const postAction = (dto: any) => post(cam + "posts/action", dto);
export const getPostFile = (id: number) => get(cam + "posts/" + id + "/file");
/** RevisionCamDto { revisionId, postProcessorId, postProcessorText, camSystem, simulationAttachmentId, attachments[], tools[], missing[], editable }. */
export const getRevisionCam = (revisionId: number) => get(cam + "revisions/" + revisionId);
export const setRevisionCam = (dto: { revisionId: number; postProcessorId: number | null; camSystem: string | null }) => post(cam + "revisions", dto);
/** kind 1 simulation report, 2 setup sheet. */
export const uploadNcAttachment = (dto: { revisionId: number; kind: number; fileName: string; contentBase64: string }) => post(cam + "revisions/attachments", dto);
export const importCamTools = (dto: { revisionId: number; csv: string }) => post(cam + "revisions/tools", dto);
/** NcAttachmentFileDto { fileName, contentBase64 }. */
export const getNcAttachment = (id: number) => get(cam + "attachments/" + id);

// ---------------------------------------------------------------- measurements
/** MeasureStationDto[] (not paged). */
export const getStations = () => get(measure + "stations");
export const saveStation = (dto: any) => post(measure + "stations", dto);
/** StationKeyDto { stationId, key }: the key is shown once. */
export const issueStationKey = (id: number) => post(measure + "stations/" + id + "/key");
/** MeasurementQuery { operationId, machineId } + paging. */
export const getMeasurements = (params: any) => get(measure + "values", params);

/** Saves a base64 file the API returned. */
export function saveBase64(name: string, base64: string) {
  const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
  const url = URL.createObjectURL(new Blob([bytes]));
  const a = document.createElement("a");
  a.href = url;
  a.download = name;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 2000);
}
