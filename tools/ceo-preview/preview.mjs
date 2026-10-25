// Visual self-review of the CEO dashboard (release D1): serves the built front end (dist/), signs in with a fake CEO token, answers every api/v1 call from
// src/fixtures/ceo/*.json, and takes screenshots of /ceo into tools/ceo-preview/out/.
//
//   source /home/claude/netenv.sh && npx vite build     # build first (this script only serves dist/)
//   node tools/ceo-preview/preview.mjs                  # all shots
//   node tools/ceo-preview/preview.mjs 1440-light 390   # only the shots whose name contains one of the words
//
// Shots: ceo-1440-light, ceo-1440-dark (system dark, no stored choice), ceo-1024, ceo-390, and for the review ceo-1440-drill, ceo-1440-assistant,
// ceo-1440-analysis, ceo-1440-pmo (payroll mask), ceo-print (print media) plus ceo-print.pdf (A4).
// Nothing here talks to a real server: every request under /api/v1/ is answered by the route handler below.
import { createRequire } from "module";
import { createServer } from "http";
import { existsSync, mkdirSync, readFileSync, statSync } from "fs";
import { dirname, extname, join, normalize, resolve } from "path";
import { fileURLToPath } from "url";

const require = createRequire(import.meta.url);
const { chromium } = require("playwright");

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, "../..");
const dist = join(root, "dist");
const fixtures = join(root, "src/fixtures/ceo");
const out = join(here, "out");
mkdirSync(out, { recursive: true });
if (!existsSync(join(dist, "index.html"))) {
  console.error("dist/index.html is missing: run `npx vite build` in the front folder first.");
  process.exit(1);
}

// ---------------------------------------------------------------- a tiny static server with the SPA fallback
const types = { ".html": "text/html; charset=utf-8", ".js": "text/javascript", ".css": "text/css", ".ttf": "font/ttf", ".woff": "font/woff", ".png": "image/png", ".jpg": "image/jpeg", ".svg": "image/svg+xml", ".ico": "image/x-icon", ".json": "application/json" };
const server = createServer((req, res) => {
  const path = decodeURIComponent((req.url || "/").split("?")[0]);
  let file = normalize(join(dist, path));
  if (!file.startsWith(dist)) {
    res.writeHead(403).end();
    return;
  }
  if (!existsSync(file) || statSync(file).isDirectory()) file = join(dist, "index.html");
  res.writeHead(200, { "Content-Type": types[extname(file)] || "application/octet-stream" });
  res.end(readFileSync(file));
});
await new Promise((ok) => server.listen(0, "127.0.0.1", ok));
const base = `http://127.0.0.1:${server.address().port}`;

// ---------------------------------------------------------------- fixtures and the fake API
const fx = (name) => JSON.parse(readFileSync(join(fixtures, name), "utf8"));
const overview = fx("overview.json");
const drills = fx("drills.json");
const wrap = (data) => JSON.stringify({ success: true, message: "", data, httpStatusCode: 200 });

/** The payroll mask as the server applies it for the PMO: the warranty cost KPI and chart are hidden. */
function masked(o) {
  const copy = JSON.parse(JSON.stringify(o));
  for (const s of copy.sections) {
    for (const k of s.kpis) if (k.key === "warrantyCostYtd") Object.assign(k, { value: null, valueText: "محرمانه (حقوق)", shortText: null, delta: null, deltaText: null, deltaGood: null, trend: null, trendLabels: null, hidden: true });
    for (const c of s.charts) if (c.key === "warrantyCostMonthly") Object.assign(c, { hidden: true, labels: [], series: [] });
  }
  return copy;
}

/** A drill table for a key without its own fixture: a believable shape from the catalogue title. */
function genericDrill(key) {
  const d = drills.find((x) => x.key === key);
  const title = d ? d.title : key;
  const rows = ["ردیف نمونه‌ی اول", "ردیف نمونه‌ی دوم", "ردیف نمونه‌ی سوم", "ردیف نمونه‌ی چهارم", "ردیف نمونه‌ی پنجم"].map((t, i) => ({ title: `${title}: ${t}`, amount: (5 - i) * 1.37e9, date: `1405/07/${String(10 + i * 3).padStart(2, "0")}`, status: ["danger", "warn", "ok", "ok", "neutral"][i], link: null }));
  return {
    key, title, asOfText: "۱۴۰۵/۰۸/۰۳",
    columns: [{ key: "title", title: "عنوان", kind: "text" }, { key: "amount", title: "مبلغ", kind: "rial" }, { key: "date", title: "تاریخ", kind: "date" }, { key: "status", title: "وضعیت", kind: "status" }, { key: "link", title: "رکورد", kind: "link" }],
    rows, totalCount: rows.length, page: 1, pageSize: 50, link: null, note: "داده‌ی نمونه‌ی پیش‌نمایش (این جدول هنوز fixture اختصاصی ندارد).", hidden: false,
  };
}

function answer(route, role) {
  const req = route.request();
  const url = new URL(req.url());
  const path = url.pathname.replace(/^.*\/api\/v1\//, "");
  const body = (() => {
    try {
      return JSON.parse(req.postData() || "null");
    } catch {
      return null;
    }
  })();
  let data = {};
  if (path === "ceo/overview") data = role === "PMO" ? masked(overview) : overview;
  else if (path.startsWith("ceo/section/")) data = overview.sections.find((s) => s.key === path.split("/").pop());
  else if (path === "ceo/drills") data = drills;
  else if (path === "ceo/drill") {
    const key = url.searchParams.get("key");
    const file = role === "PMO" && key === "sv.warranty_cost_by_month" ? "drill-sv.warranty_cost_by_month.hidden.json" : `drill-${key}.json`;
    data = existsSync(join(fixtures, file)) ? fx(file) : genericDrill(key);
  } else if (path === "ceo/analyze") data = body && body.sectionKey === null ? fx("analyze-headline.json") : fx("analyze-projects.json");
  else if (path === "ceo/snapshot") data = fx("snapshot.json");
  else if (path === "ai/brief/latest") data = fx("brief.json");
  else if (path === "ai/notes") data = fx("ai-notes-new.json");
  else if (path === "ai/conversations") data = { data: [], currentPage: 1, itemsPerPage: 20, totalItems: 0, totalPages: 0 };
  else if (path === "ai/chat") data = { conversationId: 1, answer: "پاسخ نمونه‌ی پیش‌نمایش.", citations: [], toolCalls: [], verified: true, degraded: false, model: "preview" };
  else if (path === "office/inbox/counts") data = { open: 4 };
  else if (path === "Accounts/get-current-user-info") data = { firstName: "مدیر", lastName: "عامل", profilePhotoId: null };
  // the analysis takes a moment on a real server: a short delay keeps the busy state honest
  const delay = path === "ceo/analyze" ? 350 : 0;
  return new Promise((ok) => setTimeout(() => ok(route.fulfill({ status: 200, contentType: "application/json; charset=utf-8", body: wrap(data) })), delay));
}

// ---------------------------------------------------------------- the fake sign-in (the app reads localStorage: token, userRole, accessList, tokenExpireDate)
function b64url(obj) {
  return Buffer.from(JSON.stringify(obj)).toString("base64").replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
function fakeToken(role) {
  return [b64url({ alg: "HS256", typ: "JWT" }), b64url({ "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": role, "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier": "1", exp: 4102444800 }), "preview-signature"].join(".");
}

// ---------------------------------------------------------------- shots
const shots = [
  { name: "ceo-1440-light", viewport: { width: 1440, height: 900 }, scheme: "light" },
  { name: "ceo-1440-dark", viewport: { width: 1440, height: 900 }, scheme: "dark" },
  { name: "ceo-1024", viewport: { width: 1024, height: 768 }, scheme: "light" },
  { name: "ceo-390", viewport: { width: 390, height: 844 }, scheme: "light", mobile: true },
  { name: "ceo-1440-drill", viewport: { width: 1440, height: 900 }, scheme: "light", fullPage: false, act: openDrill },
  { name: "ceo-1440-assistant", viewport: { width: 1440, height: 900 }, scheme: "light", fullPage: false, act: openAssistant },
  { name: "ceo-1440-analysis", viewport: { width: 1440, height: 900 }, scheme: "dark", fullPage: false, act: runAnalysis },
  { name: "ceo-1440-pmo", viewport: { width: 1440, height: 900 }, scheme: "light", role: "PMO", fullPage: false, act: scrollToService },
  { name: "ceo-print", viewport: { width: 1100, height: 900 }, scheme: "light", print: true },
];
const wanted = process.argv.slice(2);
const list = wanted.length ? shots.filter((s) => wanted.some((w) => s.name.includes(w))) : shots;

async function openDrill(page) {
  await page.click(".kpi-cell >> nth=1");
  await page.waitForSelector(".drill-table tbody tr");
  await page.click(".drill-table thead th:nth-child(2) .sort-btn");
  await page.waitForTimeout(300);
}
async function openAssistant(page) {
  await page.click(".assist-handle");
  await page.waitForSelector(".assist-drawer .ai-panel");
  await page.click(".assist-chips .ceo-chip >> nth=0");
  await page.waitForTimeout(300);
}
async function runAnalysis(page) {
  // Bootstrap makes every scroll smooth; the review scrolls are instant so the shot is not taken mid-way
  await page.evaluate(() => document.getElementById("ceo-sec-projects").scrollIntoView({ block: "start", behavior: "instant" }));
  await page.click("#ceo-sec-projects .sec-actions .ceo-btn:not(.ghost)");
  await page.waitForSelector("#ceo-sec-projects .analysis-text");
  await page.evaluate(() => document.querySelector("#ceo-sec-projects .analysis").scrollIntoView({ block: "center", behavior: "instant" }));
  await page.waitForTimeout(500);
}
async function scrollToService(page) {
  await page.click(".ann-window >> text=خدمات");
  await page.waitForTimeout(1500);
}

const browser = await chromium.launch();
const errors = [];
for (const shot of list) {
  const context = await browser.newContext({ viewport: shot.viewport, colorScheme: shot.scheme, locale: "fa-IR", timezoneId: "Asia/Tehran", deviceScaleFactor: 1, isMobile: !!shot.mobile, hasTouch: !!shot.mobile });
  const role = shot.role || "CEO";
  await context.addInitScript(([token, role]) => {
    localStorage.setItem("token", token);
    localStorage.setItem("refreshToken", "preview");
    localStorage.setItem("tokenExpireDate", "4102444800");
    localStorage.setItem("userRole", role);
    localStorage.setItem("accessList", "");
  }, [fakeToken(role), role]);
  const page = await context.newPage();
  await page.clock.setFixedTime(new Date("2026-10-25T07:44:30+03:30"));
  page.on("pageerror", (e) => errors.push(`${shot.name}: ${e.message}`));
  page.on("console", (m) => m.type() === "error" && errors.push(`${shot.name} console: ${m.text()}`));
  await page.route("**/api/v1/**", (route) => answer(route, role));
  await page.goto(base + "/ceo", { waitUntil: "networkidle" });
  await page.waitForSelector(".ceo-kpis", { timeout: 15000 });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(1700); // the lamp test and the first chart draw
  if (shot.act) await shot.act(page);
  if (shot.print) {
    await page.emulateMedia({ media: "print" });
    await page.waitForTimeout(400);
    await page.pdf({ path: join(out, "ceo-print.pdf"), format: "A4", printBackground: true, margin: { top: "14mm", bottom: "16mm", left: "12mm", right: "12mm" } });
  }
  await page.screenshot({ path: join(out, shot.name + ".png"), fullPage: shot.fullPage !== false });
  console.log("saved", shot.name + ".png");
  await context.close();
}
await browser.close();
server.close();
if (errors.length) {
  console.log("\npage errors:");
  for (const e of errors) console.log("  " + e);
}
