import { jalaliToIso, latinDigits } from "@/composables/fn";

/** Persian date text (year below 1700) or an ISO date from a file (year above) to yyyy-mm-dd; null when it is neither. */
export function jaliToIsoSafe(text: string): string | null {
  const t = latinDigits(text).trim();
  const m = /^(\d{4})[/\-.](\d{1,2})[/\-.](\d{1,2})/.exec(t);
  if (!m) return null;
  if (Number(m[1]) >= 1700) return `${m[1]}-${m[2].padStart(2, "0")}-${m[3].padStart(2, "0")}`;
  return jalaliToIso(`${m[1]}/${m[2]}/${m[3]}`);
}
