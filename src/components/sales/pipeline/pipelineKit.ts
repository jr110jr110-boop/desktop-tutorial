import { call, clean, rowsOf } from "@/api/ApiClient";
import { getSlCustomers, getSlInquiries } from "@/api/SalesAPIs";
import { downloadFile, uploadFile } from "@/api/InventoryAPIs";
import { camelize } from "@/composables/fn";
import { fail } from "@/composables/inventory";

// Small helpers shared by the sales pipeline pages (customers, inquiries, estimates, proposals). Plain arrays; an error gives an empty list.

/** Type-ahead source: customers of the sales module. */
export async function searchCustomers(text: string): Promise<any[]> {
  const [data] = await getSlCustomers({ Contains: text, PageSize: 15 });
  return rowsOf(camelize(data));
}
export const customerLabel = (c: any) => (c ? c.name : "");

/** Type-ahead source: inquiries (optionally one status only). */
export function inquirySearch(status?: number) {
  return async (text: string): Promise<any[]> => {
    const [data] = await getSlInquiries({ Contains: text, PageSize: 15, Status: status });
    return rowsOf(camelize(data));
  };
}
export const inquiryLabel = (i: any) => (i ? `${i.number} - ${i.title}` : "");

/** Equipment templates (Equipments endpoint): pick one to draft an estimate. */
export async function searchEquipments(text: string): Promise<any[]> {
  const [data] = await call("get", "Equipments", undefined, clean({ Contains: text, PageSize: 15, PageNumber: 1, IsDeleted: false }));
  return rowsOf(camelize(data));
}
export const equipmentLabel = (e: any) => (e ? `${e.code || ""} - ${e.title || ""}`.replace(/^ - /, "") : "");

/** Text or null: an empty or blank field is sent as null. */
export const blankToNull = (v: any) => (v === undefined || v === null || String(v).trim() === "" ? null : String(v).trim());

/** A number typed in an input (any digits) or null when empty; text that is not a number gives NaN-safe null. */
export function numOrNull(v: any): number | null {
  if (v === undefined || v === null || String(v).trim() === "") return null;
  const n = Number(String(v).replace(/[٬,،\s]/g, "").replace(/[۰-۹]/g, (d) => String("۰۱۲۳۴۵۶۷۸۹".indexOf(d))).replace(/[٠-٩]/g, (d) => String("٠١٢٣٤٥٦٧٨٩".indexOf(d))));
  return Number.isFinite(n) ? n : null;
}

/** Thousands separators while typing a rial amount (empty stays empty). */
export function groupDigits(v: any): string {
  const n = numOrNull(v);
  return n === null ? "" : Math.round(n).toLocaleString("en-US");
}

/** Uploads a file through the existing Documents endpoint and answers its id (null after showing the error). */
export async function uploadDoc(file: File): Promise<string | null> {
  const [id, error] = await uploadFile(file, file.name);
  if (error) {
    fail(error);
    return null;
  }
  return id as string;
}

export const openDoc = (id: string, name = "document") => downloadFile(id, name);

/** The comma separated attachment ids of an inquiry as an array. */
export const attachmentList = (text: string | null | undefined): string[] => String(text || "").split(",").map((s) => s.trim()).filter(Boolean);
