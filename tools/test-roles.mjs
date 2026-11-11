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
