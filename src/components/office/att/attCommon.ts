import { ref } from "vue";
import { getEmployeeDirectory } from "@/api/FnAPIs";
import { getMyOffice } from "@/api/OfficeAPIs";
import { camelize, latinDigits } from "@/composables/fn";

// Small helpers shared by the attendance, leave and month-close components. Nothing here talks to a page.

export const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);

/** Employee lookup for LookupBox: the directory every signed-in user may read (name, job title, unit). */
export const searchEmployees = async (text: string) => camelize(rowsOf((await getEmployeeDirectory(text))[0])).slice(0, 25);
export const employeeLabel = (e: any) => [e.name, e.jobTitle].filter(Boolean).join(" - ");

/** "8:5", "۰۸:۰۵" or "08:05:00" -> "08:05"; null when it is not a time of day. */
export function normalizeTime(text: any): string | null {
  const m = /^(\d{1,2}):(\d{1,2})(?::\d{1,2})?$/.exec(latinDigits(String(text ?? "")).trim());
  if (!m) return null;
  const h = Number(m[1]), mi = Number(m[2]);
  if (h > 23 || mi > 59) return null;
  return `${String(h).padStart(2, "0")}:${String(mi).padStart(2, "0")}`;
}

/** Local date-time text for the API (the server stores the wall-clock time, no time zone). */
export const stamp = (isoDate: string, time: string) => `${isoDate}T${time}:00`;

/** Days with two decimals, as the balance card and the sheets show them. */
export const days2 = (v: number | null | undefined) => (v === null || v === undefined ? "" : Number(v).toFixed(2));
/** A day count that is usually whole (absence, mission): no decimals when whole. */
export const daysShort = (v: number | null | undefined) => { const n = Number(v) || 0; return n ? (Number.isInteger(n) ? String(n) : n.toFixed(2)) : "-"; };

export const dayBadge = (status: number) => (status === 1 ? "ok" : status === 2 ? "bad" : status === 3 || status === 4 ? "info" : "warn");

/** The signed-in user's office profile (positions for acting-position, employee id), loaded once. */
const me = ref<any>(null);
let loading: Promise<void> | null = null;
export function useMyOffice() {
  if (!me.value && !loading) {
    loading = getMyOffice().then(([d]) => {
      me.value = d || { positions: [] };
    });
  }
  return me;
}
