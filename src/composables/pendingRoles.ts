// FX2: sensitive roles (PMO, CEO, finance manager, ...) are only requested when a user is created or changed; they become active after a second person
// approves and the user signs in again. Until then the user has no active role, so a sign button would simply be missing. This store keeps the user's own
// open requests (one call after sign-in / app load) so that the warning bar and the disabled sign buttons can say why.

import { ref } from "vue";
import { getMyPendingRoles } from "@/api/UserAPIs";
import { hasRole, roleTitles } from "@/composables/roles";

export interface PendingRole {
  role: string;
  roleLabel?: string;
  warningText?: string;
}

/** The own open requests; empty when nothing waits or before the first call. */
export const myPendingRoles = ref<PendingRole[]>([]);

export function labelOfRole(role: string): string {
  const r = String(role || "").toUpperCase();
  return r === "PMO" ? "PMO" : roleTitles[r] || r;
}

/** The Persian warning for a list of pending roles; null when the list is empty. */
export function waitingText(roles: string[]): string | null {
  const labels = Array.from(new Set(roles.map(labelOfRole)));
  if (!labels.length) return null;
  return (labels.length === 1 ? "نقش " : "نقش‌های ") + labels.join("، ") + " شما منتظر تأیید نفر دوم است؛ پس از تأیید یک بار خارج و دوباره وارد شوید.";
}

/** Reads the own open requests (a failure leaves the list as it was: the bar is a help, never a blocker). */
export async function loadMyPendingRoles(): Promise<void> {
  if (!localStorage.getItem("token")) {
    myPendingRoles.value = [];
    return;
  }
  const result = await getMyPendingRoles();
  const data = result && !result[1] ? result[0]?.data : null;
  if (Array.isArray(data)) myPendingRoles.value = data;
}

export function clearMyPendingRoles(): void {
  myPendingRoles.value = [];
}

/**
 * What a sign button of a gate does for the signed-in user. `roles` are the roles the gate accepts.
 *  - show: the user holds one of them (active) or waits for one of them (pending); a user with neither does not see the button at all;
 *  - disabled/reason: only the pending case here (the service adds its own reasons where it exposes them, see ReviewsTab).
 */
export function signGate(roles: string[]): { show: boolean; pendingReason: string | null } {
  if (hasRole(roles)) return { show: true, pendingReason: null };
  const waiting = myPendingRoles.value.filter((p) => roles.includes(String(p.role).toUpperCase())).map((p) => p.role);
  if (waiting.length) return { show: true, pendingReason: waitingText(waiting) };
  return { show: false, pendingReason: null };
}
