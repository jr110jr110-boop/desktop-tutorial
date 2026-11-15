// Test of the multi-role matching: node tools/test-roles.mjs
// Bundles src/composables/roles.ts with esbuild (from node_modules), stubs localStorage and checks hasRole / isRole / currentRole.
import { build } from "esbuild";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import assert from "node:assert/strict";

const out = join(mkdtempSync(join(tmpdir(), "roles-test-")), "roles.mjs");
await build({ entryPoints: [resolve("src/composables/roles.ts")], bundle: true, format: "esm", platform: "node", outfile: out, logLevel: "error" });

let stored = null;
globalThis.localStorage = { getItem: (k) => (k === "userRole" ? stored : null) };
const r = await import(pathToFileURL(out).href);
const as = (v) => { stored = v; };

as("PLANNING,PMO,PROJECTMANAGER");
assert.deepEqual(r.userRoles(), ["PLANNING", "PMO", "PROJECTMANAGER"]);
assert.equal(r.hasRole(["PMO"]), true, "second role of the list is honoured");
assert.equal(r.hasRole(["PROJECTMANAGER", "CEO"]), true, "third role is honoured");
assert.equal(r.hasRole(["COMMERCIAL", "CEO"]), false);
assert.equal(r.isRole("PMO"), true);
assert.equal(r.isRole("pmo"), true);
assert.equal(r.isRole("COMMERCIAL"), false);
assert.equal(r.currentRole(), "PMO", "primary role by priority, not alphabetical first");

as("PMO,COMMERCIAL");
assert.equal(r.isRole("PMO") && r.isRole("COMMERCIAL"), true, "both signature buttons for PMO + COMMERCIAL");

// empty allowed list = everybody (also for null / undefined)
as("OPERATOR");
assert.equal(r.hasRole([]), true);
assert.equal(r.hasRole(null), true);
assert.equal(r.hasRole(undefined), true);

// case, spaces, empty entries, duplicates
as(" hr , Payroll,,HR ,");
assert.deepEqual(r.userRoles(), ["HR", "PAYROLL"]);
assert.equal(r.hasRole(["PAYROLL"]), true);
assert.equal(r.currentRole(), "PAYROLL", "PAYROLL ranks before HR in RolePriority");

// priority
as("OPERATOR,CEO,PMO");
assert.equal(r.currentRole(), "CEO");
as("SECURITY,WAREHOUSE");
assert.equal(r.currentRole(), "WAREHOUSE");
as("NEWROLE,SECURITY");
assert.equal(r.currentRole(), "SECURITY", "listed roles rank before unknown ones");
as("NEWROLE,OTHERNEW");
assert.equal(r.currentRole(), "NEWROLE", "unknown roles keep their order");

// every role of the codebase has a priority
for (const name of Object.keys(r.Roles)) assert.ok(r.RolePriority.includes(name), name + " missing in RolePriority");
assert.equal(new Set(r.RolePriority).size, r.RolePriority.length, "no duplicates in RolePriority");

// resources (priority 4): readers are the project control readers plus the roles that handle rates; the writers are planning and the PMO
for (const role of ["FINANCEMANAGER", "PAYROLL", "AUDITOR", "PLANNING", "PROJECTMANAGER", "WHSUPERVISOR"]) {
  as(role);
  assert.equal(r.hasRole(r.ProjectControlRoles.resourceReaders), true, role + " reads the resources");
}
for (const role of ["SECURITY", "OPERATOR", "TREASURY", "HR", "SALES"]) {
  as(role);
  assert.equal(r.hasRole(r.ProjectControlRoles.resourceReaders), false, role + " does not read the resources");
}
as("PLANNING");
assert.equal(r.hasRole(r.ProjectControlRoles.resourceWriters), true);
assert.equal(r.hasRole(r.ProjectControlRoles.rateMakers), false);
as("PROJECTMANAGER");
assert.equal(r.hasRole(r.ProjectControlRoles.resourceWriters), false, "the project manager decides the plan, planning and the PMO edit it");
as("FINANCEMANAGER");
assert.equal(r.hasRole(r.ProjectControlRoles.rateMakers) && r.hasRole(r.ProjectControlRoles.rateApprovers), true);
assert.equal(r.hasRole(r.ProjectControlRoles.resourceWriters), false);
as("CEO");
assert.equal(r.hasRole(r.ProjectControlRoles.rateApprovers), true);
assert.equal(r.hasRole(r.ProjectControlRoles.rateMakers), false);
as("AUDITOR");
assert.equal(r.hasRole(r.ProjectControlRoles.rateReaders), false, "the auditor reads rates as «محرمانه»");
as("PMO");
assert.equal(r.hasRole(r.ProjectControlRoles.rateReaders), false, "the PMO no longer sees the rates of sensitive resources");
for (const role of ["CEO", "FINANCEMANAGER"]) { as(role); assert.equal(r.hasRole(r.ProjectControlRoles.rateReaders), true, role + " sees the rates"); }
as("PAYROLL");
assert.equal(r.hasRole(r.ProjectControlRoles.rateReaders), true);
assert.equal(r.hasRole(r.ProjectControlRoles.resourceWriters), false);
// the reader list is a superset of the project control readers
for (const role of r.ProjectControlRoles.readers) assert.ok(r.ProjectControlRoles.resourceReaders.includes(role), role + " is a project control reader");

// resource load and over-allocations (phase 4c): everybody who reads the pool reads the histogram and the alerts; applying a proposal is planning or the PMO, confirming it and accepting an over-allocation are the PMO only
as("PLANNING");
assert.equal(r.hasRole(r.ProjectControlRoles.planning), true, "planning applies and rejects proposals");
assert.equal(r.hasRole(r.ProjectControlRoles.pmo), false, "planning does not confirm a proposal and does not accept an over-allocation");
as("PMO");
assert.equal(r.hasRole(r.ProjectControlRoles.planning) && r.hasRole(r.ProjectControlRoles.pmo), true, "the PMO decides, confirms and accepts");
as("PROJECTMANAGER");
assert.equal(r.hasRole(r.ProjectControlRoles.resourceReaders), true, "the project manager reads the histogram");
assert.equal(r.hasRole(r.ProjectControlRoles.planning) || r.hasRole(r.ProjectControlRoles.pmo), false, "the project manager neither applies nor confirms");

// unknown / missing storage
as(null);
assert.deepEqual(r.userRoles(), []);
assert.equal(r.currentRole(), "");
assert.equal(r.hasRole(["PMO"]), false);
assert.equal(r.isRole("PMO"), false);
assert.equal(r.hasRole([]), true);
globalThis.localStorage = { getItem() { throw new Error("blocked"); } };
assert.deepEqual(r.userRoles(), []);

console.log("roles tests passed");
