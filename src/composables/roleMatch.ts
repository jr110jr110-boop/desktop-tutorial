// Pure role matching (no browser storage), so it can be tested with node. composables/roles.ts reads "userRole" and calls these.

/**
 * Priority of the roles when ONE role must be chosen for a user who has several (default landing page, a label).
 * Leadership first, then project control, commerce, finance, quality/engineering, office, then the shop floor.
 * All 28 roles of the API are listed; a role that is not listed (a new one) comes after them in the order it was given.
 */
export const RolePriority: readonly string[] = [
  "CEO",
  "PMO",
  "PROJECTMANAGER",
  "PLANNING",
  "COMMERCIAL",
  "FINANCEMANAGER",
  "FINANCIAL",
  "PRODUCTIONMANAGER",
  "ENGINEERING",
  "QA",
  "QC",
  "DCC",
  "PAYROLL",
  "TREASURY",
  "AUDITOR",
  "HR",
  "SECRETARIAT",
  "SALES",
  "SERVICE",
  "WHSUPERVISOR",
  "WAREHOUSE",
  "SHOPSUPERVISOR",
  "CONSTRUCTION",
  "ENGINEER",
  "ASSEMBLER",
  "MAINTENANCE",
  "OPERATOR",
  "SECURITY",
];

/** All roles of the stored comma separated text: trimmed, upper case, without empty entries and duplicates (order kept). */
export function parseRoles(raw: string | null | undefined): string[] {
  const out: string[] = [];
  for (const part of String(raw ?? "").split(",")) {
    const role = part.trim().toUpperCase();
    if (role && !out.includes(role)) out.push(role);
  }
  return out;
}

/** The primary role of a list of roles by RolePriority, "" when the list is empty. */
export function primaryRole(roles: string[]): string {
  let best = "";
  let bestRank = Number.MAX_SAFE_INTEGER;
  roles.forEach((role, index) => {
    const listed = RolePriority.indexOf(role);
    // unlisted roles rank after every listed one, in the given order
    const rank = listed >= 0 ? listed : RolePriority.length + index;
    if (rank < bestRank) {
      best = role;
      bestRank = rank;
    }
  });
  return best;
}

/** True when the list is empty or null (no role check) or the user has any of the allowed roles. */
export function matchesAny(userRoles: string[], allowed: string[] | undefined | null): boolean {
  if (!allowed || allowed.length === 0) return true;
  return allowed.some((role) => userRoles.includes(String(role).trim().toUpperCase()));
}
