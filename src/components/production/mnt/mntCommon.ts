import moment from "moment-jalaali";
import { ref } from "vue";
import { getProductionMachines } from "@/api/ProductionAPIs";
import { camelize } from "@/composables/fn";

// Local helpers of the maintenance / OEE / cost pages (dates go to the API as ISO, people read Persian dates only).

/** yyyy-mm-dd of today. */
export const todayIso = (): string => moment().format("YYYY-MM-DD");

/** [from, to] ISO dates: the last `days` days up to today. */
export function lastDays(days: number): { from: string; to: string } {
  return { from: moment().subtract(days, "days").format("YYYY-MM-DD"), to: todayIso() };
}

/** Joins a date (yyyy-mm-dd) and a time (HH:mm) into the ISO date-time the API takes; empty without a date. */
export function joinDateTime(date: string | null | undefined, time: string | null | undefined): string | null {
  if (!date) return null;
  return `${date}T${time && /^\d{2}:\d{2}/.test(time) ? time.slice(0, 5) : "00:00"}:00`;
}

/** Splits an ISO date-time into its date (yyyy-mm-dd) and time (HH:mm) parts. */
export function splitDateTime(value: string | null | undefined): { date: string | null; time: string } {
  if (!value) return { date: null, time: "" };
  const m = /^(\d{4}-\d{2}-\d{2})(?:T(\d{2}:\d{2}))?/.exec(String(value));
  return m ? { date: m[1], time: m[2] || "" } : { date: null, time: "" };
}

/** Machines for the pickers (production machine list, first 500). */
export function useMachineOptions() {
  const machines = ref<any[]>([]);
  async function loadMachines() {
    const [data] = await getProductionMachines({ PageNumber: 1, PageSize: 500 });
    const d = camelize(data);
    machines.value = Array.isArray(d) ? d : d?.data || [];
  }
  return { machines, loadMachines };
}
