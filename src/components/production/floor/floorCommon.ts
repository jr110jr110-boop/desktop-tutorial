import { jalaliToIso } from "@/composables/fn";
import { getEmployeeDirectory } from "@/api/FnAPIs";
import { getProductionOperations, getProductionOrders, getReasonCodes, getWorkCenters } from "@/api/ProductionAPIs";
import { camelize } from "@/composables/fn";

// Small helpers shared by the shop floor pages (terminal, bookings, schedule). Nothing here talks to a page.

export const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);

/** Active work centres for a select (the API answers a paged list). */
export async function loadWorkCenters(): Promise<any[]> {
  const [d] = await getWorkCenters({ IsActive: true, PageSize: 500 });
  return rowsOf(d);
}

/** Active reason codes of one kind (1 scrap, 2 downtime, 3 waiting). */
export async function loadReasons(): Promise<any[]> {
  const [d] = await getReasonCodes({ IsActive: true, PageSize: 500 });
  return rowsOf(d);
}

/** Employee lookup for LookupBox (the directory every signed-in user may read). */
export const searchEmployees = async (text: string) => camelize(rowsOf((await getEmployeeDirectory(text))[0])).slice(0, 25);
export const employeeLabel = (e: any) => [e.name, e.jobTitle].filter(Boolean).join(" - ");

/** Production order lookup for LookupBox. */
export const searchOrders = async (text: string) => rowsOf((await getProductionOrders({ Contains: text, PageSize: 20 }))[0]);
export const orderLabel = (o: any) => [o.number, o.itemTitle].filter(Boolean).join(" - ");

/** Operation (job card) lookup for LookupBox. */
export const searchOperations = async (text: string) => rowsOf((await getProductionOperations({ Contains: text, PageSize: 20 }))[0]);
export const operationLabel = (o: any) => [o.jobCard, o.title].filter(Boolean).join(" - ");

/** "1405/08/03 14:05" (the Persian stamp the API sends) to a Date; null when it cannot be read. */
export function dateOfStamp(text: string | null | undefined): Date | null {
  const m = /^(\S+)\s+(\d{1,2}):(\d{2})$/.exec(String(text || "").trim());
  if (!m) return null;
  const iso = jalaliToIso(m[1]);
  if (!iso) return null;
  return new Date(`${iso}T${m[2].padStart(2, "0")}:${m[3]}:00`);
}

/** Seconds as H:MM:SS for the running timer. */
export function clockText(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  return `${h}:${String(m).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`;
}
