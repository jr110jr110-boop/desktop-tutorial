// UI access crawl: a real browser signs in as every test user and opens every menu item and every page of the front end against the running API.
// It answers one question the API smoke cannot: does what a user SEES work for that user?
//
//   node crawl.js BASE USERS.tsv OUTDIR
//   USERS.tsv: label <tab> userName <tab> password
//
// Findings (OUTDIR/findings.tsv: user, kind, page, detail), one line each:
//   MENU-BLOCKED  an enabled menu item sends the user back to the dashboard (the route guard refuses what the menu offers)
//   HTTP-403      a page the user could open asked the API for something his roles may not read
//   HTTP-5XX      the API failed while a page loaded
//   HTTP-404      a page called an address the API does not have (only for pages without a record id in the address)
//   REFUSED       a GET answered success=false while a page loaded (only for pages without a record id in the address)
//   JS-ERROR      an uncaught error of the front end while a page loaded
//   LOGIN         the user could not sign in (the crawl of that user is skipped)
//   INFO-NO-PERSONNEL-FILE  a page told the user that he has no personnel file (true for the throwaway CI users; reported, not counted as a defect)
// OUTDIR/<label>.json keeps everything that was seen; OUTDIR/summary.txt counts per kind.
const { chromium } = require("playwright");
const fs = require("fs");
const [BASE, USERS, OUT] = process.argv.slice(2);
const WORKERS = +(process.env.UI_WORKERS || 4);
const ROLE = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
fs.mkdirSync(OUT, { recursive: true });
const users = fs.readFileSync(USERS, "utf8").split("\n").filter((l) => l.trim()).map((l) => { const [label, name, password] = l.split("\t"); return { label, name, password }; });
const findings = [];
const add = (user, kind, page, detail) => findings.push([user, kind, page, String(detail).replace(/\s+/g, " ").slice(0, 300)]);

async function login(u) {
  const r = await fetch(BASE + "/api/v1/Accounts/login", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ userName: u.name, password: u.password }) });
  const j = await r.json().catch(() => null);
  const d = j && j.data;
  if (!d || !d.accessToken) throw new Error("HTTP " + r.status + " " + ((j && j.message) || ""));
  const payload = JSON.parse(Buffer.from(d.accessToken.split(".")[1], "base64url").toString());
  const role = payload[ROLE];
  return { token: d.accessToken, refresh: d.refreshToken || "", exp: payload.exp, role: role == null ? "" : String(role), access: d.accessList == null ? "" : String(d.accessList) };
}

async function crawl(browser, u) {
  let s;
  try { s = await login(u); } catch (e) { add(u.label, "LOGIN", "-", e.message); return; }
  const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 }, locale: "fa-IR" });
  await ctx.addInitScript((x) => {
    if (localStorage.getItem("token")) return; // only the first document seeds the session; later the front end keeps (and refreshes) it
    localStorage.setItem("token", x.token); localStorage.setItem("refreshToken", x.refresh); localStorage.setItem("tokenExpireDate", String(x.exp));
    localStorage.setItem("userRole", x.role); localStorage.setItem("accessList", x.access);
  }, s);
  const page = await ctx.newPage();
  let inflight = 0, last = Date.now(), bucket = [], errs = [];
  page.on("request", () => { inflight++; last = Date.now(); });
  const done = () => { inflight = Math.max(0, inflight - 1); last = Date.now(); };
  page.on("requestfailed", done);
  page.on("requestfinished", done);
  page.on("response", async (r) => {
    const url = new URL(r.url());
    if (!/^\/api\//i.test(url.pathname)) return;
    const m = r.request().method(), st = r.status();
    const rec = { m, p: url.pathname + url.search, s: st, msg: "" };
    if (st !== 200 || m === "GET") {
      try { const ct = r.headers()["content-type"] || ""; if (ct.includes("json")) { const j = await r.json(); if (j && j.success === false) { rec.refused = true; rec.msg = j.message || ""; } else if (st !== 200) rec.msg = (j && j.message) || ""; } } catch { /* body gone (navigation) */ }
    }
    if (st !== 200 || rec.refused) bucket.push(rec);
  });
  page.on("pageerror", (e) => errs.push(String(e.message)));
  page.on("dialog", (d) => d.dismiss().catch(() => {}));
  const idle = async () => { const t0 = Date.now(); await page.waitForTimeout(300); while (Date.now() - t0 < 8000) { if (inflight === 0 && Date.now() - last > 400) break; await page.waitForTimeout(100); } };
  const take = () => { const r = { req: bucket, err: errs }; bucket = []; errs = []; return r; };
  const router = "document.querySelector('#app').__vue_app__.config.globalProperties.$router";
  const go = (p) => page.evaluate(([r, p]) => eval(r).push(p).catch(() => {}), [router, p]);
  const report = (where, seen, hasId) => {
    for (const q of seen.req) {
      const what = q.m + " " + q.p + (q.msg ? " : " + q.msg : "");
      if (q.s === 403) add(u.label, "HTTP-403", where, what);
      else if (q.s >= 500) add(u.label, "HTTP-5XX", where, q.s + " " + what);
      else if (q.s === 401) add(u.label, "HTTP-401", where, what);
      else if (!hasId && (q.s === 404 || q.s === 405)) add(u.label, "HTTP-404", where, q.s + " " + what);
      else if (!hasId && q.refused && q.s === 200) add(u.label, /پرونده پرسنلی/.test(q.msg) ? "INFO-NO-PERSONNEL-FILE" : "REFUSED", where, what);
    }
    for (const e of [...new Set(seen.err)]) add(u.label, "JS-ERROR", where, e);
  };
  const res = { user: u.label, roles: s.role, menu: [], routes: [] };
  await page.goto(BASE + "/"); await idle();
  await page.waitForSelector("li.sidebar-li-sub", { state: "attached", timeout: 30000 }).catch(() => {});
  await page.waitForTimeout(500); await idle();
  const landing = { path: await page.evaluate(() => location.pathname), ...take() };
  res.landing = landing; report("(پس از ورود) " + landing.path, landing, false);

  // pass 1: the menu as the user sees it
  const items = await page.evaluate(() => { const out = []; document.querySelectorAll("li.sidebar-li-sub").forEach((li, i) => { li.setAttribute("data-ax", i); const g = li.closest(".collapse")?.previousElementSibling?.innerText || ""; out.push({ i, title: li.innerText.trim().replace(/\s+/g, " "), group: g.trim().replace(/\s+/g, " "), disabled: li.classList.contains("no-pointer-event") }); }); return out; });
  const menuPaths = new Set();
  for (const it of items) {
    if (it.disabled) { res.menu.push(it); continue; }
    await go("/__neutral__"); await idle(); take();
    await page.evaluate((i) => document.querySelector('li.sidebar-li-sub[data-ax="' + i + '"] button').click(), it.i); await idle();
    const path = await page.evaluate(() => location.pathname); const seen = take();
    res.menu.push({ ...it, path, ...seen });
    const where = it.group + " ‹ " + it.title;
    if (path === "/" || path === "/__neutral__") add(u.label, "MENU-BLOCKED", where, "the item is enabled, the click lands on " + path);
    else { menuPaths.add(path); report(where + " [" + path + "]", seen, false); }
  }

  // pass 2: every other page the route guard lets this user open (record pages are opened with id 1)
  const routes = await page.evaluate((r) => eval(r).getRoutes().map((x) => ({ name: String(x.name || ""), path: x.path })), router);
  for (const rt of routes) {
    if (rt.name === "Login" || rt.path.includes("pathMatch") || rt.path === "/") continue;
    const hasId = rt.path.includes(":");
    const p = rt.path.replace(/:[A-Za-z_]+\([^)]*\)[?*+]?/g, "1").replace(/:[A-Za-z_]+[?*+]?/g, "1");
    if (menuPaths.has(p)) continue;
    await go(p); await idle();
    const landed = await page.evaluate(() => location.pathname); const seen = take();
    res.routes.push({ name: rt.name, path: p, landed, ...seen });
    if (landed === p) report(rt.name + " [" + p + "]", seen, hasId);
  }
  await ctx.close();
  fs.writeFileSync(OUT + "/" + u.label + ".json", JSON.stringify(res));
  console.log("done", u.label, "roles", s.role, "menu", res.menu.filter((m) => !m.disabled).length + "/" + res.menu.length, "routes", res.routes.filter((r) => r.landed === r.path).length + "/" + res.routes.length);
}

(async () => {
  const browser = await chromium.launch();
  const queue = [...users];
  await Promise.all(Array.from({ length: WORKERS }, async () => { while (queue.length) { const u = queue.shift(); try { await crawl(browser, u); } catch (e) { add(u.label, "CRAWL-ERROR", "-", String(e).slice(0, 300)); console.log("FAIL", u.label, String(e).slice(0, 300)); } } }));
  await browser.close();
  findings.sort((a, b) => (a[1] + a[2] + a[0]).localeCompare(b[1] + b[2] + b[0]));
  fs.writeFileSync(OUT + "/findings.tsv", findings.map((f) => f.join("\t")).join("\n") + (findings.length ? "\n" : ""));
  const kinds = {}; for (const f of findings) kinds[f[1]] = (kinds[f[1]] || 0) + 1;
  // the same defect seen by several users is one defect: group by kind + page + detail
  // (record ids and query strings are left out of the key, so one defect seen on several records is still one defect)
  const groups = {}; for (const f of findings) { const k = f[1] + "\t" + f[2] + "\t" + f[3].replace(/\?[^ ]*/, "").replace(/\/\d+(?=\/|\s|$)/g, "/{id}"); (groups[k] = groups[k] || []).push(f[0]); }
  // INFO kinds are expected for the throwaway CI users (they have no personnel file) and are reported, not counted
  const defects = Object.keys(groups).filter((k) => !k.startsWith("INFO-")).length;
  fs.writeFileSync(OUT + "/grouped.tsv", Object.entries(groups).map(([k, v]) => k + "\t" + v.join(",")).join("\n") + "\n");
  fs.writeFileSync(OUT + "/summary.txt", "users " + users.length + "\nfindings " + findings.length + "\ndistinct " + defects + "\n" + Object.entries(kinds).map(([k, v]) => k + " " + v).join("\n") + "\n");
  console.log(fs.readFileSync(OUT + "/summary.txt", "utf8"));
})();
