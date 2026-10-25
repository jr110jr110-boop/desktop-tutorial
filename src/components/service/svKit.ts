import moment from "moment-jalaali";
import { ref } from "vue";
import { getEmployeeDirectory } from "@/api/FnAPIs";
import { rowsOf } from "@/api/ApiClient";
import { getSerials } from "@/api/InventoryAPIs";
import { getSlCustomers, getSlSites } from "@/api/SalesAPIs";
import { getSvUnits } from "@/api/ServiceAPIs";
import { camelize } from "@/composables/fn";

// Local helpers of the after-sales service pages. Dates go to the API as ISO; people read Persian dates only.

/** yyyy-mm-dd of today. */
export const todayIso = (): string => moment().format("YYYY-MM-DD");

/** Joins a date (yyyy-mm-dd) and a time (HH:mm) into the ISO date-time the API takes; empty without a date. */
export function joinDateTime(date: string | null | undefined, time: string | null | undefined): string | null {
  if (!date) return null;
  return `${date}T${time && /^\d{2}:\d{2}/.test(time) ? time.slice(0, 5) : "00:00"}:00`;
}

/** yyyy-mm-dd of an ISO date or date-time; null when empty. */
export const isoDay = (value: string | null | undefined): string | null => (value ? String(value).slice(0, 10) : null);

/** Records of a lookup answer (paged or plain). */
export const listOf = (data: any): any[] => rowsOf(camelize(data));

/** Customers for a LookupBox. */
export async function searchCustomers(text: string): Promise<any[]> {
  const [data] = await getSlCustomers({ Contains: text, PageNumber: 1, PageSize: 15 });
  return listOf(data);
}
export const customerLabel = (c: any) => c.name;

/** Installed units for a LookupBox. */
export async function searchUnits(text: string): Promise<any[]> {
  const [data] = await getSvUnits({ Contains: text, PageNumber: 1, PageSize: 15 });
  return listOf(data);
}
export const unitLabel = (u: any) => [u.tag, u.title].filter(Boolean).join(" - ");

/** Sites of a customer. */
export async function sitesOf(customerId: number | null): Promise<any[]> {
  if (!customerId) return [];
  const [data] = await getSlSites({ CustomerId: customerId, PageNumber: 1, PageSize: 200 });
  return listOf(data);
}

/** The employee directory (name, job title) for the engineer and time sheet pickers; empty when it cannot be read. */
export function useEmployees() {
  const employees = ref<any[]>([]);
  async function loadEmployees() {
    const [data] = await getEmployeeDirectory();
    employees.value = listOf(data);
  }
  return { employees, loadEmployees };
}

/** Whole number from the digits of a typed amount (Persian digits and separators accepted). */
export function typedNumber(text: any): number | null {
  if (text === null || text === undefined || text === "") return null;
  const latin = String(text).replace(/[۰-۹]/g, (d) => String("۰۱۲۳۴۵۶۷۸۹".indexOf(d))).replace(/[٠-٩]/g, (d) => String("٠١٢٣٤٥٦٧٨٩".indexOf(d))).replace(/[,٬،\s]/g, "");
  if (latin === "") return null;
  const n = Number(latin);
  return Number.isFinite(n) ? n : null;
}

/** Serial units (traceability) for a LookupBox. */
export async function searchSerials(text: string): Promise<any[]> {
  const [data] = await getSerials({ Contains: text, PageNumber: 1, PageSize: 15 });
  return listOf(data);
}
export const serialLabel = (s: any) => [s.serialNumber, s.itemCode, s.itemTitle].filter(Boolean).join(" - ");
