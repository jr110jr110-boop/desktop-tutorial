// Test of the typed Jalali date parsing: node tools/test-jalali.mjs
// Bundles src/composables/fn.ts with esbuild (from node_modules) and checks jalaliToIso.
import { build } from "esbuild";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import assert from "node:assert/strict";

const out = join(mkdtempSync(join(tmpdir(), "jalali-test-")), "fn.mjs");
await build({
  entryPoints: [resolve("src/composables/fn.ts")],
  bundle: true,
  format: "esm",
  platform: "node",
  outfile: out,
  logLevel: "error",
  // the app modules fn.ts imports are not needed by jalaliToIso: stub them
  plugins: [
    {
      name: "stub-app-modules",
      setup(b) {
        b.onResolve({ filter: /^@\// }, (a) => ({ path: a.path, namespace: "stub" }));
        b.onLoad({ filter: /.*/, namespace: "stub" }, () => ({ contents: "export const fail = () => {}; export const apiErrorText = () => '';", loader: "js" }));
      },
    },
  ],
});
globalThis.localStorage = { getItem: () => null, setItem: () => {} };
const { jalaliToIso } = await import(pathToFileURL(out).href);

assert.equal(jalaliToIso("1404/07/15"), "2025-10-07", "plain date");
assert.equal(jalaliToIso("۱۴۰۴/۰۷/۱۵"), "2025-10-07", "Persian digits");
assert.equal(jalaliToIso("١٤٠٤/٠٧/١٥"), "2025-10-07", "Arabic digits");
assert.equal(jalaliToIso("1404-7-5"), "2025-09-27", "dash separator, single digits");
assert.equal(jalaliToIso("1403/12/30"), "2025-03-20", "leap year, last day is valid");
assert.equal(jalaliToIso("1404/12/30"), null, "non-leap year has 29 days in Esfand");
assert.equal(jalaliToIso("1404/12/29"), "2026-03-20", "last day of a common year");
assert.equal(jalaliToIso("1404/06/31"), "2025-09-22", "first six months have 31 days");
assert.equal(jalaliToIso("1404/07/31"), null, "second six months have 30 days");
assert.equal(jalaliToIso("1404/13/01"), null, "month 13");
assert.equal(jalaliToIso("1404/00/10"), null, "month 0");
assert.equal(jalaliToIso("1404/01/00"), null, "day 0");
for (const bad of ["", "abc", "1404/07", "14/07/15", "1404/07/15/1", "99999"]) assert.equal(jalaliToIso(bad), null, `garbage: ${bad}`);
console.log("test-jalali: OK");
