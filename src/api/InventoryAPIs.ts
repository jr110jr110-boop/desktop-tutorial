import { call, clean, http, postForFile, saveBlob, rowsOf, type Result } from "@/api/ApiClient";

// Warehouse, part traceability and Sepidar link (api/v1/inventory/...).
// The shared client (token on every request, [data, error] results) lives in ApiClient.ts.

export type { Result } from "@/api/ApiClient";
export { saveBlob, rowsOf };

const base = "inventory/";

// master data
export const getGroups = () => call("get", base + "groups");
export const saveGroup = (dto: any) => call("post", base + "groups", dto);
export const deleteGroup = (id: number) => call("delete", base + "groups/" + id);

export const getItems = (params: any) => call("get", base + "items", undefined, clean(params));
export const getItem = (id: number) => call("get", base + "items/" + id);
export const getItemByCode = (code: string) => call("get", base + "items/by-code", undefined, { code });
export const saveItem = (dto: any) => call("post", base + "items", dto);
export const deleteItem = (id: number) => call("delete", base + "items/" + id);
export const importItems = (rows: any[]) => call("post", base + "items/import", rows);
/**
 * R1q D11: QC, QA or PMO change the quality fields of an item. SaveItemQualityDto { itemId, requiresIncomingInspection, requiredCertificateType (1 = 2.2, 2 = 3.1, 3 = 3.2 | null), shelfLifeDays | null, reason }.
 * A reduction (no more inspection, a lower certificate type, a shorter or no shelf life) needs a reason and waits for the QA manager; a stricter control applies at once. Answer: the item (InventoryItemDto).
 */
export const saveItemQuality = (dto: any) => call("post", base + "items/quality", dto);
/** ItemQualityChangeQuery { Status? (0 pending, 1 approved, 2 rejected), ItemId?, PageNumber, PageSize }; QC, QA and PMO read it. */
export const getItemQualityChanges = (params: any) => call("get", base + "items/quality-changes", undefined, clean(params));
/** DecideItemQualityChangeDto { id, approve, note }: the second approval (QA only, never the person who asked); a rejection needs the note. */
export const decideItemQualityChange = (dto: any) => call("post", base + "items/quality-changes/decide", dto);

export const getWarehouses = () => call("get", base + "warehouses");
export const saveWarehouse = (dto: any) => call("post", base + "warehouses", dto);
export const saveLocation = (dto: any) => call("post", base + "warehouses/locations", dto);
export const deleteLocation = (id: number) => call("delete", base + "warehouses/locations/" + id);

// documents
/**
 * Paged list of warehouse documents. DocumentQuery { Type?, Status?, WarehouseId?, WorkOrderId?, SalesOrderId? (documents linked to a sales order), ServiceOrderId? (documents linked to a service order), From?, To?, Contains?, PageNumber, PageSize }.
 */
export const getDocuments = (params: any) => call("get", base + "documents", undefined, clean(params));
export const getDocument = (id: number) => call("get", base + "documents/" + id);
export const saveDocument = (dto: any) => call("post", base + "documents", dto);
export const deleteDocument = (id: number) => call("delete", base + "documents/" + id);
export const confirmDocument = (id: number) => call("post", base + "documents/" + id + "/confirm");
export const cancelDocument = (id: number, reason: string) => call("post", base + "documents/" + id + "/cancel", { reason });
/** kind: 1 warehouse supervisor, 2 planning, 4 quality (loading of a delivery), 6 finance (scrap sale). */
export const approveDocument = (id: number, kind: number) => call("post", base + "documents/" + id + "/approve", { kind });
export const exitCheckDocument = (id: number, note: string) => call("post", base + "documents/" + id + "/exit-check", { note });
export const importOpening = (dto: any) => call("post", base + "documents/import-opening", dto);

// coding (WI-WH-002) and requests for new item codes
export const getCodingValues = (scheme: string) => call("get", base + "coding/values", undefined, clean({ scheme }));
export const saveCodingValue = (dto: any) => call("post", base + "coding/values", dto);
export const checkCode = (code: string, itemId?: number | null) => call("get", base + "coding/check", undefined, clean({ code, itemId }));
export const getItemRequests = (status?: number | null) => call("get", base + "coding/requests", undefined, clean({ status }));
export const saveItemRequest = (dto: any) => call("post", base + "coding/requests", dto);
export const reviewItemRequest = (dto: any) => call("post", base + "coding/requests/review", dto);

// incoming inspection and non-conformance reports (NCR)
export const getInspections = (params: any) => call("get", base + "quality/inspections", undefined, clean(params));
export const recordInspection = (dto: any) => call("post", base + "quality/inspections", dto);
export const getNcrs = (params: any) => call("get", base + "quality/ncr", undefined, clean(params));
export const getNcr = (id: number) => call("get", base + "quality/ncr/" + id);
export const saveNcr = (dto: any) => call("post", base + "quality/ncr", dto);
export const approveNcr = (dto: any) => call("post", base + "quality/ncr/approve", dto);
export const executeNcr = (dto: any) => call("post", base + "quality/ncr/execute", dto);
export const closeNcr = (dto: any) => call("post", base + "quality/ncr/close", dto);
export const getSupplierReturnReport = (id: number) => call("get", base + "quality/ncr/" + id + "/supplier-return");

// material requests
export const getMaterialRequests = (params: any) => call("get", base + "material-requests", undefined, clean(params));
export const getMaterialRequest = (id: number) => call("get", base + "material-requests/" + id);
export const saveMaterialRequest = (dto: any) => call("post", base + "material-requests", dto);
export const deleteMaterialRequest = (id: number) => call("delete", base + "material-requests/" + id);
/** action: submit, approve-planning, approve-manufacturing, reject, close. */
export const materialRequestAction = (id: number, action: string, reason?: string) =>
  call("post", base + "material-requests/action", { id, action, reason });

// stock counts
export const getStockCounts = (warehouseId?: number | null) => call("get", base + "counts", undefined, clean({ warehouseId }));
export const getStockCount = (id: number) => call("get", base + "counts/" + id);
export const startStockCount = (dto: any) => call("post", base + "counts", dto);
export const saveCountLines = (dto: any) => call("post", base + "counts/lines", dto);
/** action: submit, approve-warehouse, approve-planning, post, cancel. */
export const stockCountAction = (id: number, action: string) => call("post", base + "counts/action", { id, action });

// stock
export const getBalances = (params: any) => call("get", base + "stock", undefined, clean(params));
export const getItemStocks = (params: any) => call("get", base + "stock/items", undefined, clean(params));
export const getKardex = (params: any) => call("get", base + "stock/kardex", undefined, clean(params));
export const getLots = (params: any) => call("get", base + "stock/lots", undefined, clean(params));
/** LotExpiryQuery { WithinDays? (empty = the company setting ExpiryWarnDays), ItemId?, OnlyExpired?, PageNumber, PageSize } -> LotExpiryDto rows (R1q D32). */
export const getExpiringLots = (params: any) => call("get", base + "stock/expiry", undefined, clean(params));
export const saveLot = (dto: any) => call("post", base + "stock/lots", dto);
export const getSerials = (params: any) => call("get", base + "stock/serials", undefined, clean(params));

// traceability
export const traceSearch = (q: string) => call("get", base + "trace/search", undefined, { q });
export const traceSerial = (id: number) => call("get", base + "trace/serial/" + id);
export const traceLot = (id: number) => call("get", base + "trace/lot/" + id);
export const createSerial = (dto: any) => call("post", base + "trace/serials", dto);
export const updateSerial = (dto: any) => call("put", base + "trace/serials", dto);
export const getTemplates = (params: any) => call("get", base + "trace/templates", undefined, clean(params));
export const saveTemplate = (dto: any) => call("post", base + "trace/templates", dto);
export const deleteTemplate = (id: number) => call("delete", base + "trace/templates/" + id);
export const getOperations = (params: any) => call("get", base + "trace/operations", undefined, clean(params));
export const applyRouting = (dto: any) => call("post", base + "trace/operations/apply-routing", dto);
export const addOperation = (dto: any) => call("post", base + "trace/operations", dto);
export const updateOperation = (dto: any) => call("put", base + "trace/operations", dto);
export const qcOperation = (dto: any) => call("post", base + "trace/operations/qc", dto);
export const addOperationFile = (dto: any) => call("post", base + "trace/operations/files", dto);
export const installPart = (dto: any) => call("post", base + "trace/assembly/install", dto);
export const removePart = (dto: any) => call("post", base + "trace/assembly/remove", dto);

// sepidar
export const getSepidarOutbox = (params: any) => call("get", base + "sepidar/outbox", undefined, clean(params));
export const getSepidarCheck = () => call("get", base + "sepidar/check");
export const getSepidarMappings = (entityType: string) => call("get", base + "sepidar/mappings", undefined, { entityType });
export const saveSepidarMapping = (dto: any) => call("post", base + "sepidar/mappings", dto);

/** Downloads the CSV export. Returns an error message, or null when the file was saved. */
export const exportSepidar = (documentIds: number[], markExported: boolean): Promise<string | null> =>
  postForFile(base + "sepidar/export", { documentIds, markExported }, "sepidar-export.csv");

// shared helpers that use the existing endpoints
export const getProviders = () => call("get", "Provider", undefined, { PageSize: 0 });
export const getWorkOrders = () => call("get", "WorkOrders", undefined, { PageSize: 0 });
export const getContracts = () => call("get", "Contracts", undefined, { PageSize: 0 });
export const getMachines = () => call("get", "Machine");

/** Uploads a file with the existing Documents endpoint; returns its id. */
export async function uploadFile(file: File, title: string): Promise<Result> {
  const form = new FormData();
  form.append("File", file);
  const params = { Description: title || file.name, Name: file.name.replace(/\.[^.]+$/, ""), Code: "INV" };
  try {
    const { data } = await http.post("Documents", form, { params });
    const match = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i.exec(JSON.stringify(data ?? ""));
    if (!match) return [null, new Error(data?.message || "بارگذاری فایل انجام نشد.")];
    return [match[0], null];
  } catch (error: any) {
    return [null, error];
  }
}

export async function downloadFile(documentId: string, fileName: string) {
  try {
    const response = await http.get("Documents", { params: { DocumentId: documentId }, responseType: "blob" });
    saveBlob(response.data, fileName || "file");
  } catch {
    /* shown by the caller */
  }
}

/** Purchase orders that can still be received (released or partly received); used by the purchase receipt. */
export const getOpenPurchaseOrders = () => call("get", "procurement/orders", undefined, clean({ OpenForReceipt: true, PageSize: 200 }));
export const getPurchaseOrderForReceipt = (id: number) => call("get", "procurement/orders/" + id);
