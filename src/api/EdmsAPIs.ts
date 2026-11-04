import { call, clean, http, saveBlob } from "@/api/ApiClient";

// Engineering document control (api/v1/edms): revisions, check-out, links, tags, distribution, approver matrix, notices, shop floor, Excel, tender register.
const base = "edms";

// revisions
export const getDocHistory = (deliverableId: number) => call("get", `${base}/deliverables/${deliverableId}/history`);
export const getRevision = (id: number) => call("get", `${base}/revisions/${id}`);
/** kind: change | ncr | qc — the revisions linked to a change request, an NCR or a QC record. */
export const getRevisionsBy = (kind: string, id: number) => call("get", `${base}/revisions/by`, undefined, { kind, id });
export const compareRevisions = (a: number, b: number) => call("get", `${base}/revisions/compare`, undefined, { a, b });
export const createRevision = (dto: any) => call("post", `${base}/revisions`, dto);
export const addRevisionFile = (dto: any) => call("post", `${base}/revisions/files`, dto);
export const removeRevisionFile = (dto: { fileId: number; reason: string }) => call("post", `${base}/revisions/files/remove`, dto);
/** Action: submit, review, approve, reject, release, as-built, obsolete, link-change, link-quality. */
export const revisionAction = (dto: any) => call("post", `${base}/revisions/action`, dto);

// check-out
export const checkOut = (dto: any) => call("post", `${base}/checkout`, dto);
export const checkIn = (dto: any) => call("post", `${base}/checkin`, dto);
export const cancelCheckOut = (dto: any) => call("post", `${base}/checkout/cancel`, dto);

// links and tags
export const getDocLinks = (params: any) => call("get", `${base}/links`, undefined, clean(params));
export const saveDocLink = (dto: any) => call("post", `${base}/links`, dto);
export const voidDocLink = (dto: { id: number; reason: string }) => call("post", `${base}/links/void`, dto);
export const getPartDocuments = (params: any) => call("get", `${base}/part-documents`, undefined, clean(params));
export const getTags = (contractId: number) => call("get", `${base}/tags`, undefined, { contractId });
export const saveTag = (dto: any) => call("post", `${base}/tags`, dto);

// distribution, approver matrix, notices
export const getDistribution = () => call("get", `${base}/distribution`);
export const saveDistribution = (dto: any) => call("post", `${base}/distribution`, dto);
export const getApprovers = () => call("get", `${base}/approvers`);
export const saveApprover = (dto: any) => call("post", `${base}/approvers`, dto);
export const getNotices = (params: any) => call("get", `${base}/notices`, undefined, clean(params));
export const acknowledgeNotice = (id: number) => call("post", `${base}/notices/${id}/acknowledge`);

// shop floor
export const getShopDocuments = (orderId: number, operationId?: number | null) => call("get", `${base}/shop/orders/${orderId}/documents`, undefined, clean({ operationId }));
export const getControlledCopy = (revisionId: number, documentId?: string | null) => call("get", `${base}/controlled-copy`, undefined, clean({ revisionId, documentId }));
/** the print itself is logged: a POST */
export const printControlledCopy = (revisionId: number, documentId?: string | null) => call("post", `${base}/controlled-copy/print`, { revisionId, documentId });

// Excel
export async function downloadMdrExcel(contractId: number, opts: { clientOnly?: boolean; workOrderId?: number | null; template?: boolean } = {}): Promise<string | null> {
  try {
    const response = await http.get(`${base}/mdr/export`, { params: clean({ contractId, clientOnly: opts.clientOnly, workOrderId: opts.workOrderId, template: opts.template }), responseType: "blob" });
    if (response.data?.type?.includes("json")) return JSON.parse(await response.data.text())?.message || "خطا";
    saveBlob(response.data, opts.template ? "mdr-template.xlsx" : `mdr-${contractId}.xlsx`);
    return null;
  } catch (e: any) {
    return e?.message || "خطا";
  }
}
export function importMdrExcel(contractId: number, file: File) {
  const form = new FormData();
  form.append("ContractId", String(contractId));
  form.append("File", file);
  return call("post", `${base}/mdr/import`, form);
}

// tender register
export const getTenders = (params?: any) => call("get", `${base}/tenders`, undefined, clean(params || {}));
export const getTender = (id: number) => call("get", `${base}/tenders/${id}`);
export const getTendersOfContract = (contractId: number) => call("get", `${base}/tenders/of-contract/${contractId}`);
export const saveTender = (dto: any) => call("post", `${base}/tenders`, dto);
export const saveTenderDocument = (dto: any) => call("post", `${base}/tenders/documents`, dto);
/** Action: issue, void, win, lose, cancel, link-contract. */
export const tenderAction = (dto: any) => call("post", `${base}/tenders/action`, dto);
