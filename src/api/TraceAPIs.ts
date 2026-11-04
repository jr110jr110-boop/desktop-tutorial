import { call, clean, fileErrorText, http, saveBlob } from "@/api/ApiClient";

// Trace centre (api/v1/trace): the product tree (as designed and as built), the reverse trace (where used) and the manufacturing genealogy.
// One read model per question; the API caps the depth and the number of parts, and masks the prices for readers who are not finance.
const base = "trace/";

export interface TraceQueryParams {
  Q?: string;
  /** product, part, serial, lot or label; empty = decide from the text */
  Kind?: string;
  WorkOrderId?: number;
  SerialUnitId?: number;
  LotId?: number;
  Depth?: number;
}

export interface ReverseQueryParams {
  Q?: string;
  /** lot, heat, supplier, po, pr, mr, serial or label; empty = decide from the text */
  Kind?: string;
  LotId?: number;
  ProviderId?: number;
  Depth?: number;
}

export const getTraceTree = (params: TraceQueryParams) => call("get", base + "tree", undefined, clean(params));
export const getReverseTrace = (params: ReverseQueryParams) => call("get", base + "reverse", undefined, clean(params));
export const getOrderGenealogy = (orderId: number) => call("get", base + "genealogy/order/" + orderId);
export const rebuildOrderGenealogy = (orderId: number) => call("post", base + "genealogy/order/" + orderId + "/rebuild");
export const backfillGenealogy = () => call("post", base + "genealogy/backfill");

/** Downloads an xlsx made by the API; returns an error text, or null when the file was saved. */
async function download(path: string, params: any, fallbackName: string): Promise<string | null> {
  try {
    const response = await http.get(base + path, { params: clean(params), responseType: "blob" });
    const type = response.headers["content-type"] || "";
    if (type.includes("application/json")) {
      const json = JSON.parse(await (response.data as Blob).text());
      return json.message || "خطا";
    }
    const disposition = response.headers["content-disposition"] || "";
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    saveBlob(response.data, match ? decodeURIComponent(match[1]) : fallbackName);
    return null;
  } catch (error: any) {
    return fileErrorText(error);
  }
}

export const exportTraceTree = (params: TraceQueryParams) => download("tree/export", params, "product-tree.xlsx");
export const exportReverseTrace = (params: ReverseQueryParams) => download("reverse/export", params, "reverse-trace.xlsx");
