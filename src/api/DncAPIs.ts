import { call, clean, fileErrorText, http, saveBlob } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// DNC of the production module (api/v1/production/dnc, release P2 part A): the CNC program library with revisions, the machine links, the transfers to the machines and back, the run record and
// the terminal view of an operation's program. Same conventions as ProductionAPIs.ts: every helper returns [data, null] or [null, error]; lists are paged ({ data, totalItems }); enums are numbers
// (labels in composables/dnc.ts); "...Text" fields are Persian (Jalali). Rule and role refusals answer HTTP 200 with success=false and come back as errors with the Persian message of the API.
// The "Who" line of each comment is checked inside the service (DncRoles in composables/dnc.ts).
const dnc = "production/dnc/";

type Method = "get" | "post";
async function fcall(method: Method, url: string, body?: any, params?: any): Promise<[any, null] | [null, Error | any]> {
  const [data, error] = await call(method, url, body, params);
  return error ? [null, error] : [camelize(data), null];
}
const get = (url: string, params?: any) => fcall("get", url, undefined, clean(params));
const post = (url: string, body?: any) => fcall("post", url, body);

/** GET a file and save it; null when saved, otherwise the Persian message (a refused call answers JSON inside the blob). */
async function getFile(url: string, fallbackName: string): Promise<string | null> {
  try {
    const response = await http.get(url, { responseType: "blob" });
    const type = String(response.headers["content-type"] || "");
    if (type.includes("application/json")) {
      const json = JSON.parse(await (response.data as Blob).text());
      return json?.success === false ? json.message || "خطا" : "پاسخ نامعتبر";
    }
    const disposition = String(response.headers["content-disposition"] || "");
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    saveBlob(response.data as Blob, match ? decodeURIComponent(match[1]) : fallbackName);
    return null;
  } catch (error) {
    return fileErrorText(error);
  }
}

/** Paging + free text: MfPageQuery { PageNumber, PageSize (25, max 500), Contains }. */
export type DncPageParams = { PageNumber?: number; PageSize?: number; Contains?: string | null; [key: string]: any };

// ---------------------------------------------------------------- machine links
/**
 * params: MachineConnectionQuery { LinkKind 1..5, WorkCenterId } + paging. One row per machine (also machines with no link yet: id null, linkKind 1 manual).
 * MachineConnectionDto { id, machineId, machineTitle, machineIsActive, workCenterId, workCenterCode, machineStatus, controllerFamily, controllerModel, linkKind, host, port, remotePath, credentialKey,
 * credentialConfigured, programNameFormat, fileExtension, lineEnding, requirePercent, maxProgramBytes, axes, maxSpindleRpm, maxFeedMmPerMin, toolCapacity, isLathe, baudRate,
 * requireConfirmedProgram, hasGatewayKey, gatewayKeyRotatedAtText, allowedAddresses, lastSeenAtText, isActive, note, rowVersion }. Who: DncRoles.readers.
 */
export const getConnections = (params?: DncPageParams) => get(dnc + "connections", params);
export const getConnection = (machineId: number) => get(dnc + "connections/" + machineId);
/** SaveMachineConnectionDto (the fields above without the read-only ones). The secret of a credential lives in the server settings (Dnc:Credentials:key). Who: DncRoles.linkEditors. */
export const saveConnection = (dto: any) => post(dnc + "connections", dto);
/** GatewayKeyDto { machineId, key, rotatedAtText }: the key is shown ONCE. Only for link kind 5 (edge gateway). Who: linkEditors. */
export const newGatewayKey = (machineId: number) => post(dnc + "connections/gateway-key", { machineId });
/** LinkTestResultDto { ok, message }. Who: DncRoles.linkTesters (master editors, MAINTENANCE, SHOPSUPERVISOR). */
export const testConnection = (machineId: number) => post(dnc + "connections/test", { machineId });

// ---------------------------------------------------------------- programs and revisions
/**
 * params: NcProgramQuery { ItemId, OperationTemplateId, WorkCenterId, MachineId, Status 1..2, Pending } + paging.
 * NcProgramDto { id, code, title, itemId, itemCode, itemTitle, operationTemplateId, operationTitle, workCenterId, workCenterCode, machineId, machineTitle, controllerFamily, currentRevisionId,
 * currentRevision, currentApprovedAtText, pendingRevisions, status, note, rowVersion, revisions (detail only, newest first) }. Who: readers.
 */
export const getPrograms = (params?: DncPageParams) => get(dnc + "programs", params);
export const getProgram = (id: number) => get(dnc + "programs/" + id);
/** SaveNcProgramDto { id?, code, title, itemId?, operationTemplateId?, workCenterId?, machineId?, controllerFamily, status (1 active, 2 obsolete), note, rowVersion }. Who: DncRoles.writers. */
export const saveProgram = (dto: any) => post(dnc + "programs", dto);
/**
 * UploadNcRevisionDto { programId, fileName, contentBase64 (≤ 4 MB of file), changeNote }. Answers NcRevisionDto { id, revision, status, fileName, sizeBytes, lineCount, sha256, programNumber,
 * source (1 upload, 2 machine), baseRevisionId, baseRevision, diffAdded, diffRemoved, changeNote, findings [{ severity "error"|"warning", line, code, text }], errorCount, warningCount,
 * authorName, createdAtText, submittedByName, submittedAtText, approvedByName, approvedAtText, rejectedByName, rejectedAtText, rejectReason, allowedActions, rowVersion }. Who: writers.
 */
export const uploadRevision = (dto: { programId: number; fileName: string; contentBase64: string; changeNote?: string | null }) => post(dnc + "revisions", dto);
/** NcRevisionActionDto { revisionId, action: submit | approve | reject (reason) | withdraw, reason?, rowVersion }. The author never approves (Persian refusal). */
export const revisionAction = (dto: { revisionId: number; action: string; reason?: string | null; rowVersion?: string | null }) => post(dnc + "revisions/action", dto);
/** [NcValidationDto { machineId, machineTitle, targetName, findings, errorCount, warningCount }] for every machine the program may go to (or one). Who: readers. */
export const validateRevision = (revisionId: number, machineId?: number | null) => post(dnc + "revisions/validate", { revisionId, machineId: machineId ?? null });
/** NcDiffDto { revisionId, baseRevisionId, baseRevision, added, removed, lines ["  12 text", "- 13 old", "+ 13 new", "…"], truncated, approximate }. Who: readers. */
export const getRevisionDiff = (revisionId: number) => get(dnc + "revisions/" + revisionId + "/diff");
/** Saves the file of a revision (the stored bytes). Who: readers. */
export const downloadRevision = (revisionId: number, name: string) => getFile(dnc + "revisions/" + revisionId + "/content", name);

// ---------------------------------------------------------------- transfers, runs, terminal
/**
 * params: NcTransferQuery { MachineId, ProgramId, OperationId, Status 0..6, Direction 1..2 } + paging.
 * NcTransferDto { id, direction, machineId, machineTitle, programId, programCode, revisionId, revision, operationId, operationName, targetName, linkKind, status, attempts, lastError, sentSha256,
 * sentBytes, requestedByName, requestedAtText, deliveredAtText, confirmedByName, confirmedAtText, confirmNote, cancelReason, allowedActions (confirm, cancel, retry, download), rowVersion }.
 */
export const getTransfers = (params?: DncPageParams) => get(dnc + "transfers", params);
/** SendNcDto { programId, machineId, operationId? }: the current approved revision goes to the machine (the same one already on its way answers instead). Who: DncRoles.senders + supervisors. */
export const sendProgram = (dto: { programId: number; machineId: number; operationId?: number | null }) => post(dnc + "transfers/send", dto);
/** ReceiveNcDto { programId, machineId, programName?, changeNote? }: fetch the program back from the machine as a draft revision. Who: DncRoles.receivers. */
export const receiveProgram = (dto: { programId: number; machineId: number; programName?: string | null; changeNote?: string | null }) => post(dnc + "transfers/receive", dto);
/** NcTransferActionDto { transferId, action: confirm | cancel (note = reason) | retry, note?, rowVersion }. */
export const transferAction = (dto: { transferId: number; action: string; note?: string | null; rowVersion?: string | null }) => post(dnc + "transfers/action", dto);
/** Saves the file of a manual transfer (USB stick); the download is the delivery. Who: senders + supervisors. */
export const downloadTransfer = (transferId: number, name: string) => getFile(dnc + "transfers/" + transferId + "/file", name);
/** NcRunPendingResultDto { processed, delivered, received, failed, messages }. Who: supervisors. */
export const runPendingTransfers = () => post(dnc + "transfers/run-pending");
/** params: NcRunQuery { OperationId, OrderId, MachineId, ProgramId, RevisionId } + paging. NcRunDto { operationName, machineTitle, programCode, revision, sha256, transferId, employeeName (named roles only), kind, startedAtText }. */
export const getRuns = (params?: DncPageParams) => get(dnc + "runs", params);
/** TerminalNcDto { hasProgram, programId, programCode, programTitle, currentRevisionId, currentRevision, required, canStart, problem, lastTransfer }. Who: readers (operators included). */
export const getTerminalNc = (operationId: number, machineId: number) => get(dnc + "terminal", { OperationId: operationId, MachineId: machineId });

/** The base64 of a picked file (for uploadRevision). */
export function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const text = String(reader.result || "");
      resolve(text.substring(text.indexOf(",") + 1));
    };
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}
