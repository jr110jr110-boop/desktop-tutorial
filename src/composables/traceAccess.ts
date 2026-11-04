import { hasRole, QualityRoles, TraceRoles } from "@/composables/roles";

// Who may open which tab of the trace page. The old pages had their own rules, which are kept: the label scan stays with the Traceability access,
// the quality tab with the quality readers; the product tree and the reverse trace are for the trace readers (TraceRoles in the API).
function hasClaim(name: string): boolean {
  try {
    const raw = localStorage.getItem("accessList") || "";
    return raw.includes(name);
  } catch {
    return false;
  }
}

export const canTraceTree = (): boolean => hasRole(TraceRoles.readers);
export const canTraceScan = (): boolean => hasClaim("Traceability");
export const canTraceQuality = (): boolean => hasRole(QualityRoles.readers);
export const canOpenTrace = (): boolean => canTraceTree() || canTraceScan() || canTraceQuality();
