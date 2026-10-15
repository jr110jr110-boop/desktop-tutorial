import axios, { type AxiosError } from "axios";

// Warehouse, part traceability and Sepidar link (api/v1/inventory/...).
// The token is read on every request (not once when the file loads), so a refreshed token is used at once.

const apiUrl = import.meta.env.VITE_API_URL.toString();

const http = axios.create({ baseURL: apiUrl + "api/v1/" });
http.interceptors.request.use((config) => {
  const token = window.localStorage.getItem("token");
  if (token) {
    config.headers = config.headers || {};
    config.headers["Authorization"] = "Bearer " + token;
  }
  return config;
});

export type Result = [any, null] | [null, AxiosError | Error];

async function call(method: "get" | "post" | "put" | "delete", url: string, body?: any, params?: any): Promise<Result> {
  try {
    const { data } = await http.request({ method, url, data: body, params });
    if (data && data.success === false) return [null, new Error(data.message || "خطا")];
    return [data && "data" in data ? data.data : data, null];
  } catch (error: any) {
    if (error?.response?.status === 403) return [null, new Error("شما اجازه این کار را ندارید.")];
    if (error?.response?.status === 401) return [null, new Error("دوباره وارد سامانه شوید.")];
    return [null, error];
  }
}

function clean(params: any) {
  const result: any = {};
  Object.keys(params || {}).forEach((k) => {
    const v = params[k];
    if (v !== null && v !== undefined && v !== "") result[k] = v;
  });
  return result;
}

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

export const getWarehouses = () => call("get", base + "warehouses");
export const saveWarehouse = (dto: any) => call("post", base + "warehouses", dto);
export const saveLocation = (dto: any) => call("post", base + "warehouses/locations", dto);
export const deleteLocation = (id: number) => call("delete", base + "warehouses/locations/" + id);

// documents
export const getDocuments = (params: any) => call("get", base + "documents", undefined, clean(params));
export const getDocument = (id: number) => call("get", base + "documents/" + id);
export const saveDocument = (dto: any) => call("post", base + "documents", dto);
export const deleteDocument = (id: number) => call("delete", base + "documents/" + id);
export const confirmDocument = (id: number) => call("post", base + "documents/" + id + "/confirm");
export const cancelDocument = (id: number, reason: string) => call("post", base + "documents/" + id + "/cancel", { reason });

// stock
export const getBalances = (params: any) => call("get", base + "stock", undefined, clean(params));
export const getItemStocks = (params: any) => call("get", base + "stock/items", undefined, clean(params));
export const getKardex = (params: any) => call("get", base + "stock/kardex", undefined, clean(params));
export const getLots = (params: any) => call("get", base + "stock/lots", undefined, clean(params));
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
export async function exportSepidar(documentIds: number[], markExported: boolean): Promise<string | null> {
  try {
    const response = await http.post(base + "sepidar/export", { documentIds, markExported }, { responseType: "blob" });
    const type = response.headers["content-type"] || "";
    if (type.includes("application/json")) {
      const text = await (response.data as Blob).text();
      const json = JSON.parse(text);
      return json.message || "خطا";
    }
    const disposition = response.headers["content-disposition"] || "";
    const match = /filename="?([^";]+)"?/.exec(disposition);
    saveBlob(response.data, match ? match[1] : "sepidar-export.csv");
    return null;
  } catch (error: any) {
    return error?.message || "خطا";
  }
}

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

export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 2000);
}

/** Paged API answers carry { data: [...], totalItems, ... }; plain lists come back as arrays. */
export function rowsOf(value: any): any[] {
  if (Array.isArray(value)) return value;
  if (value && Array.isArray(value.data)) return value.data;
  return [];
}
