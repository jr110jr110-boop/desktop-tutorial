import { call, clean, http, fileErrorText, saveBlob } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// ENG-F1 engineering inbox (api/v1/engineering/inbox). Answers are { success, message, data }; a rule violation answers HTTP 200 with success=false (the helpers return the Persian message as the error).
// Who: reading EngInboxRoles.read; scan and dismiss EngInboxRoles.operate (ENGINEERING, PMO).
const base = "engineering/inbox/";

async function fcall(method: "get" | "post", url: string, body?: any, params?: any): Promise<[any, null] | [null, Error | any]> {
  const [data, error] = await call(method, url, body, params);
  return error ? [null, error] : [camelize(data), null];
}

/** EngInboxStatusDto { sourceName, isConfigured, location, notConfiguredReason, lastScanAt, lastScanNote, lastFound, lastIngested, lastRejected, lastDuplicates, counts: [{ status, count }], canScan, scanDisabledReason, canDismiss }. */
export const getEngInboxStatus = () => fcall("get", base + "status");
/** Paged EngInboxPackageDto. Params: Status (1 Ingested, 2 Rejected, 3 Applied, 4 ApplyBlocked, 5 Superseded, 6 Dismissed), Kind, Search, Id, PageNumber, PageSize. */
export const getEngInboxPackages = (params?: any) => fcall("get", base + "packages", undefined, clean(params));
/** EngInboxPackageDetailDto: the package with findings, bomLines, opcLines, documents, mdlLines and the product bank links. */
export const getEngInboxPackage = (id: number) => fcall("get", base + "packages/" + id);
/** EngInboxScanResultDto { notConfigured, found, ingested, duplicates, rejected, errors, note }. Who: ENGINEERING, PMO. */
export const scanEngInbox = () => fcall("post", base + "scan");
/** { id, reason (at least 15 characters), rowVersion }. Closes a rejected, blocked or waiting package. Who: ENGINEERING, PMO. */
export const dismissEngInboxPackage = (dto: { id: number; reason: string; rowVersion: string }) => fcall("post", base + "packages/" + dto.id + "/dismiss", dto);

/** Saves the empty template workbook or the fictional sample package (zip). Returns an error message, or null when the file was saved. */
export async function downloadEngInboxFile(which: "template" | "sample"): Promise<string | null> {
  try {
    const response = await http.get(base + which, { responseType: "blob" });
    saveBlob(response.data as Blob, which === "template" ? "ENG-PACKAGE-template.xlsx" : "ENG-PACKAGE-sample-TEST-PLT-01.zip");
    return null;
  } catch (e: any) {
    return await fileErrorText(e);
  }
}
