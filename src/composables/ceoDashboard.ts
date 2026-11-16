import { getCeoDashboard } from "@/api/FinanceAPIs";

// One read of the CEO dashboard shared by the tiles of the CEO page (the engineering alarm, the engineering tiles, the resources tile): a call that arrives within a few seconds of the
// first one gets the same answer, so the page does not read the heavy dashboard three times.
let inflight: ReturnType<typeof getCeoDashboard> | null = null;
let at = 0;

export function loadCeoDashboardShared(): ReturnType<typeof getCeoDashboard> {
  const now = Date.now();
  if (inflight && now - at < 5000) return inflight;
  at = now;
  inflight = getCeoDashboard();
  return inflight;
}
