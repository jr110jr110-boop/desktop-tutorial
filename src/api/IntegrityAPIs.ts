import { call, clean } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// Package D: records integrity («سلامت سوابق») and the strict production profile («پروفایل سخت‌گیرانه تولید»).
// api/v1/integrity: summary (the two lights), chain/runs (history), chain/run (verify now: mode "incremental" from the checkpoint or "full"), restore-tests (+ import of the result files of deploy/restore-test.ps1).
// api/v1/safety: profile (strict value against the value now), deviations (only the settings that differ; also read by the AI internal auditor), changes (list, request, decide, cancel).
// Every answer is { success, message, data }; a refused call answers HTTP 200 with success=false (the helpers return it as the error). Lists are paged ({ data: [], totalItems }). Dates come as ISO and as "...Text" (Jalali).
type Method = "get" | "post";
async function fcall(method: Method, url: string, body?: any, params?: any): Promise<[any, null] | [null, Error | any]> {
  const [data, error] = await call(method, url, body, params);
  return error ? [null, error] : [camelize(data), null];
}
const get = (url: string, params?: any) => fcall("get", url, undefined, clean(params));
const post = (url: string, body?: any) => fcall("post", url, body);

export interface ChainRun {
  id: number;
  kind: "Incremental" | "Full";
  kindText: string;
  manual: boolean;
  requestedByName: string | null;
  startedAt: string;
  startedAtText: string;
  status: "Ok" | "Broken" | "Error" | "Running";
  statusText: string;
  fromSeq: number | null;
  toSeq: number | null;
  entriesChecked: number;
  headSeq: number | null;
  keyConfigured: boolean;
  legacyRows: number;
  breakKind: string | null;
  breakSeq: number | null;
  breakRowId: number | null;
  breakEntityType: string | null;
  breakEntityTypeText: string | null;
  breakColumn: string | null;
  breakAtText: string | null;
  message: string | null;
}
export interface RestoreTest {
  id: number;
  runId: string;
  startedAtText: string;
  success: boolean;
  backupFile: string | null;
  backupTakenAtText: string | null;
  migrationsMatch: boolean | null;
  chainOk: boolean | null;
  chainEntriesChecked: number | null;
  scratchDropped: boolean | null;
  message: string | null;
  checks: { name: string; passed: boolean; detail: string | null }[];
  rowCounts: Record<string, number>;
}
export interface IntegritySummary {
  chainStatus: string;
  chainStatusText: string;
  openBreak: boolean;
  pendingAcceptanceId: number | null;
  lastChainRun: ChainRun | null;
  lastGoodChainRun: ChainRun | null;
  lastFullRun: ChainRun | null;
  restoreStatus: "Ok" | "Failed" | "Stale" | "Never";
  restoreStatusText: string;
  lastRestoreTest: RestoreTest | null;
  lastRestoreSuccessAtText: string | null;
  daysSinceRestoreSuccess: number | null;
  restoreWarnDays: number;
  red: boolean;
}
export interface SafetyItem {
  store: string;
  key: string;
  title: string;
  strict: string;
  current: string | null;
  deviates: boolean;
  weaker: boolean;
  note: string | null;
  pendingRequestId: number | null;
}
export interface SafetyChange {
  id: number;
  store: string;
  key: string;
  title: string;
  oldValue: string;
  newValue: string;
  weakening: boolean;
  reason: string;
  requestedByUserId: number;
  requestedByName: string;
  requestedAtText: string;
  status: "Pending" | "Approved" | "Rejected" | "Cancelled" | "Expired" | "AppliedAwaitingConfirmation";
  statusText: string;
  decidedByName: string | null;
  decidedAtText: string | null;
  decisionNote: string | null;
}

export interface BreakAcceptance {
  id: number;
  breakRunId: number;
  breakKind: string | null;
  breakSeq: number | null;
  breakRowId: number | null;
  breakEntityType: string | null;
  breakEntityTypeText: string | null;
  breakColumn: string | null;
  reason: string;
  requestedByName: string;
  requestedAtText: string;
  qaName: string | null;
  qaAtText: string | null;
  pmoName: string | null;
  pmoAtText: string | null;
  ceoName: string | null;
  ceoAtText: string | null;
  signatures: number;
  status: "Pending" | "Approved" | "Cancelled";
  statusText: string;
  appliedAtText: string | null;
  anchorSeq: number | null;
}

export const getIntegritySummary = () => get("integrity/summary");
export const getChainRuns = (params: any) => get("integrity/chain/runs", params);
export const runChainCheck = (mode: "incremental" | "full") => post("integrity/chain/run", { mode });
export const getRestoreTests = (params: any) => get("integrity/restore-tests", params);
export const importRestoreTests = () => post("integrity/restore-tests/import");

export const getSafetyProfile = () => get("safety/profile");
export const getSafetyDeviations = () => get("safety/deviations");
export const getSafetyChanges = (params: any) => get("safety/changes", params);
export const requestSafetyChange = (body: { store: string; key: string; value: string; reason: string }) => post("safety/changes", body);
export const decideSafetyChange = (id: number, approve: boolean, note: string | null) => post("safety/changes/decide", { id, approve, note });
export const cancelSafetyChange = (id: number) => post(`safety/changes/${id}/cancel`);

export const getChainRun = (id: number) => get(`integrity/chain/runs/${id}`);
export const getAcceptances = (params: any) => get("integrity/acceptances", params);
export const requestAcceptance = (runId: number, reason: string) => post("integrity/acceptances", { runId, reason });
export const signAcceptance = (id: number, role: "QA" | "PMO" | "CEO") => post("integrity/acceptances/sign", { id, role });
export const cancelAcceptance = (id: number) => post(`integrity/acceptances/${id}/cancel`);
