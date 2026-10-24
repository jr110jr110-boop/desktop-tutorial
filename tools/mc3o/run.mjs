import { createRequire } from "node:module";
import fs from "node:fs";
const require = createRequire(import.meta.url);
const { JSDOM } = require("jsdom");
const ROLE_DUMMY = 0;
const FRONT = "/home/claude/front";
const only = process.argv[2] || "";
const enumValue = Number(process.argv[3] || 1);
const role = process.argv[4] || "FINANCEMANAGER";
const deep = process.argv[5] !== "shallow";

const mock = await import("./mockdata.mjs");
mock.setEnum(enumValue);
const dom = new JSDOM("<!doctype html><html><body><div id='app'></div></body></html>", { url: "http://localhost/", pretendToBeVisual: true, runScripts: "outside-only" });
const w = dom.window;
w.process = { env: { NODE_ENV: "development" } };
w.matchMedia = () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} });
w.print = () => {};
w.confirm = () => true;
w.alert = () => {};
w.URL.createObjectURL = () => "blob:x";
w.URL.revokeObjectURL = () => {};
w.scrollTo = () => {};
w.localStorage.setItem("token", "t");
w.localStorage.setItem("userRole", role);

const errors = [];
let where = "";
const note = (kind, msg) => errors.push({ kind, where, msg: String(msg).slice(0, 500) });
w.console.error = (...a) => note("console.error", a.map(String).join(" "));
w.console.warn = (...a) => note("console.warn", a.map(String).join(" "));
w.addEventListener("error", (e) => note("window-error", e.error?.stack || e.message));
process.on("unhandledRejection", (e) => note("unhandledRejection", e?.stack || e));

w.eval(fs.readFileSync("./bundle/chk.js", "utf8"));
const { Vue, VR, http, views } = w.__chk;
const unknownUrls = new Set();
http.defaults.adapter = async (config) => {
  const method = (config.method || "get").toLowerCase();
  const url = (config.url || "").replace(/^\//, "");
  let data;
  if (/cost-centers/.test(url)) data = { success: true, data: [{ id: 1, code: "CC1", title: "مرکز نمونه" }] };
  else if (/contracts/i.test(url) || /providers|suppliers/.test(url)) data = { success: true, data: [{ id: 1, code: "C1", title: "قرارداد نمونه", providerId: 1, providerName: "تامین‌کننده نمونه", name: "نمونه" }] };
  else {
    const a = mock.answer(method, url);
    if (a.unknown) { unknownUrls.add(method + " " + url); data = { success: true, data: [] }; }
    else if (a.file) return { data: new w.Blob(["x"]), status: 200, statusText: "OK", headers: { "content-type": "application/octet-stream" }, config };
    else data = a.body;
  }
  if (config.responseType === "blob") return { data: new w.Blob([JSON.stringify(data)]), status: 200, statusText: "OK", headers: { "content-type": "application/json" }, config };
  return { data, status: 200, statusText: "OK", headers: {}, config };
};

const src = fs.readFileSync(FRONT + "/src/router/index.ts", "utf8");
const defs = [...src.matchAll(/\{ path: "(\/office\/[^"]*)", name: "(Of\w+)",(?: alias: "[^"]*",)? component: \(\) => import\("@\/views\/office\/(\w+)\.vue"\)(?:, props: \{ kind: "([\w-]+)" \})?/g)].map((m) => ({ path: m[1], name: m[2], view: m[3], kind: m[4] }));
const stub = { render: () => null };
const settle = async (n = 6) => { for (let i = 0; i < n; i++) { await new Promise((r) => setTimeout(r, 12)); await Vue.nextTick(); } };

async function mountRoute(def, tab = -1) {
  const router = VR.createRouter({ history: VR.createMemoryHistory(), routes: defs.map((d) => ({ path: d.path, name: d.name, component: stub })) });
  await router.push(def.path.replace(":id", "1") + (def.name === "FnPayslips" ? "?runId=1" : ""));
  w.document.body.innerHTML = "<div id='app'></div>";
  const app = Vue.createApp(views[def.view], def.kind ? { kind: def.kind } : {});
  app.config.errorHandler = (err, inst, info) => note("vue-error", (err?.stack || err) + " | " + info);
  app.config.warnHandler = (msg) => { if (!/Extraneous non-props/i.test(msg)) note("vue-warn", msg); };
  app.use(router);
  const el = w.document.getElementById("app");
  app.mount(el);
  await settle();
  if (tab >= 0) { const t = el.querySelectorAll("a.nav-link")[tab]; if (t) { t.click(); await settle(); } }
  return { app, el };
}

const todo = defs.filter((d) => !only || only.split(",").includes(d.name));
let mounted = 0, clicks = 0;
const SEL = "button, a.nav-link";
for (const def of todo) {
  const label00 = def.name + (def.kind ? ":" + def.kind : "");
  where = label00;
  let probe;
  try { probe = await mountRoute(def); } catch (e) { note("mount-failed", e?.stack || e); continue; }
  mounted++;
  const nTabs = probe.el.querySelectorAll("a.nav-link").length;
  probe.app.unmount();
  for (let tab = -1; tab < nTabs; tab++) {
    const label0 = label00 + (tab >= 0 ? " tab" + tab : "");
    where = label0;
    let c;
    try { c = await mountRoute(def, tab); } catch (e) { note("mount-failed", e?.stack || e); continue; }
    const count = c.el.querySelectorAll("button").length;
    c.app.unmount();
    if (!deep) continue;
    for (let i = 0; i < count; i++) {
      let a;
      try { a = await mountRoute(def, tab); } catch (e) { continue; }
      const btn = a.el.querySelectorAll("button")[i];
      if (!btn) { a.app.unmount(); continue; }
      const name = (btn.textContent || "").trim().slice(0, 25);
      where = `${label0} click#${i} [${name}]`;
      btn.click(); clicks++;
      await settle();
      const mCount = a.el.querySelectorAll(".inv-modal button, .inv-modal-backdrop button").length;
      a.app.unmount();
      for (let j = 0; j < mCount; j++) {
        let b;
        try { b = await mountRoute(def, tab); } catch { continue; }
        const b1 = b.el.querySelectorAll("button")[i];
        if (b1) {
          b1.click(); await settle();
          const b2 = b.el.querySelectorAll(".inv-modal button, .inv-modal-backdrop button")[j];
          if (b2) { where = `${label0} click#${i} [${name}] > dialog#${j} [${(b2.textContent || "").trim().slice(0, 25)}]`; b2.click(); clicks++; await settle(); }
        }
        b.app.unmount();
      }
    }
  }
}
const uniq = new Map();
for (const e of errors) { const k = e.kind + "|" + e.where + "|" + e.msg.slice(0, 200); if (!uniq.has(k)) uniq.set(k, e); }
process.stdout.write(`routes mounted: ${mounted}/${todo.length}, clicks: ${clicks}, errors: ${uniq.size}, enum=${enumValue}, role=${role}\n`);
for (const e of uniq.values()) process.stdout.write(`- [${e.kind}] ${e.where}\n    ${e.msg.replace(/\n/g, "\n    ").slice(0, 420)}\n`);
if (unknownUrls.size) process.stdout.write("unmapped urls: " + [...unknownUrls].join(", ") + "\n");
process.exit(0);
