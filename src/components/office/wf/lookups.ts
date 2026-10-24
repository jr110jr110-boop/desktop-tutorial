import { getEmployeeDirectory } from "@/api/FnAPIs";
import { getPositions, getUnits } from "@/api/OfficeAPIs";
import { getUserList } from "@/api/UserAPIs";
import { camelize } from "@/composables/fn";

// Local pickers' data for the workflow pages (inbox, org, meetings, decisions, requests). Nothing here is shared with the other office pages.
export const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);

/** Users (Accounts/get-all-users, the existing user list API); the label is the full name. */
export async function searchUsers(text: string): Promise<any[]> {
  const result = await getUserList((text || null) as any, 1 as any, 15 as any, null);
  if (!result || result[1]) return [];
  return rowsOf(camelize(result[0]));
}
export const userLabel = (u: any) => `${u?.name || ""} ${u?.lastName || ""}`.trim() || u?.userName || "";

/** Employee directory (name, job title and unit; open to every signed-in user). */
export async function searchEmployees(text: string): Promise<any[]> {
  const [d, error] = await getEmployeeDirectory(text || undefined);
  return error ? [] : rowsOf(camelize(d));
}
export const employeeLabel = (e: any) => [e?.name, e?.jobTitle].filter(Boolean).join(" - ");

let positionsCache: Promise<any[]> | null = null;
/** All positions (cached for the page; call resetPositions() after a save). PageSize 1000 keeps it one request. */
export function loadPositions(): Promise<any[]> {
  if (!positionsCache) {
    positionsCache = getPositions({ PageNumber: 1, PageSize: 1000 }).then(([d]: any) => rowsOf(camelize(d)));
  }
  return positionsCache;
}
export function resetPositions() {
  positionsCache = null;
}
export async function loadUnits(): Promise<any[]> {
  const [d] = await getUnits({ PageNumber: 1, PageSize: 1000 });
  return rowsOf(camelize(d));
}

/** "yyyy-MM-dd" + "HH:mm" to the local date-time text the API takes (no zone). */
export const joinDateTime = (date: string | null, time: string | null): string | null => (date ? `${date}T${time || "00:00"}:00` : null);
