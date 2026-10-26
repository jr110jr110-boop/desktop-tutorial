// Visual self-review of the CEO dashboard (release D1): serves the built front end (dist/), signs in with a fake CEO token, answers every api/v1 call from
// src/fixtures/ceo/*.json, and takes screenshots of /ceo into tools/ceo-preview/out/.
//
//   source /home/claude/netenv.sh && npx vite build     # build first (this script only serves dist/)
//   node tools/ceo-preview/preview.mjs                  # all shots
//   node tools/ceo-preview/preview.mjs 1440-light 390   # only the shots whose name contains one of the words
//   CEO_PREVIEW_CSS=".kpi-cluster{--kpi-num-weight:500}" CEO_PREVIEW_SUFFIX=-w500 node tools/ceo-preview/preview.mjs 1440-light
//                                                       # design A/B: extra CSS injected into the page, saved under a suffixed name
//
// Shots: ceo-1440-light, ceo-1440-dark (system dark, no stored choice), ceo-1024, ceo-390, and for the review ceo-1440-drill, ceo-1440-assistant,
// ceo-1440-analysis, ceo-1440-pmo (payroll mask), ceo-print (print media) plus ceo-print.pdf (A4).
// Voice mode (§7.3): ceo-1440-voice-listening and ceo-1440-voice-speaking come from the real pipeline — Space, the analyser, the silence that ends the question, the
// recording sent to ai/voice, the openDrill action, the spoken answer — with a synthetic microphone (getUserMedia replaced in the page by a WebAudio voice the script switches
// on and off, so nothing depends on a real device). The voice runs also check (and print) the follow-up window, the closed microphone tracks, barge-in, Esc, the «بخوان»
// buttons and the four voice-command chips; ceo-1440-voice-drawer and ceo-1440-voice-filter are their review shots.
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
const voiceFx = fx("voice-commands.json");
const speechFx = fx("speech.json");
const wrap = (data) => JSON.stringify({ success: true, message: "", data, httpStatusCode: 200 });

/** A silent WAV (16 kHz, mono, 16-bit) as base64: the shape the speech service answers; seconds sets how long the page is "speaking". */
function wavBase64(seconds) {
  const rate = 16000;
  const n = Math.round(rate * seconds);
  const data = Buffer.alloc(n * 2);
  const header = Buffer.alloc(44);
  header.write("RIFF", 0);
  header.writeUInt32LE(36 + data.length, 4);
  header.write("WAVEfmt ", 8);
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20);
  header.writeUInt16LE(1, 22);
  header.writeUInt32LE(rate, 24);
  header.writeUInt32LE(rate * 2, 28);
  header.writeUInt16LE(2, 32);
  header.writeUInt16LE(16, 34);
  header.write("data", 36);
  header.writeUInt32LE(data.length, 40);
  return Buffer.concat([header, data]).toString("base64");
}
const audio = { answer: wavBase64(6), speech: wavBase64(2) };

/** The voice command a question names (ai/chat), or the one the review "said" (ai/voice). */
const commandOf = (text) => voiceFx.commands.find((c) => c.match.some((w) => String(text || "").includes(w))) || null;
let spoken = voiceFx.commands[0].command;
function chatAnswer(question, command) {
  const c = command || voiceFx.fallback;
  return { conversationId: 77, answer: c.answer, citations: [], toolCalls: c.toolCalls, verified: true, degraded: false, model: voiceFx.model, actions: c.actions };
}
/** GET ceo/speech: the written headline and alerts; a section in sentences from its KPIs. */
function speechOf(what, sectionKey) {
  if (what === "section") {
    const s = overview.sections.find((x) => x.key === sectionKey);
    if (!s) return null;
    const text = [s.title + "."].concat(s.kpis.map((k) => `${k.title} ${k.hidden ? "محرمانه" : k.valueText}${k.deltaText ? "، " + k.deltaText + " نسبت به ماه پیش" : ""}.`)).join(" ");
    return { text, audioBase64: speechFx.sectionAudio ? audio.speech : null, degraded: false };
  }
  const f = speechFx[what];
  return f ? { text: f.text, audioBase64: f.audio ? audio.speech : null, degraded: false } : null;
}

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

let lastVoiceForm = "";
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
    // the status filter of an assistant's openDrill (pc.portfolio, «پروژه‌های قرمز»)
    const status = url.searchParams.get("status");
    if (status && data.rows) {
      data.rows = data.rows.filter((r) => r.status === status);
      data.totalCount = data.rows.length;
    }
  } else if (path === "ceo/analyze") data = body && body.sectionKey === null ? fx("analyze-headline.json") : body && body.sectionKey === "sales" ? fx("analyze-sales.json") : fx("analyze-projects.json");
  else if (path === "ceo/speech") data = speechOf(url.searchParams.get("what"), url.searchParams.get("sectionKey"));
  else if (path === "ceo/snapshot") data = fx("snapshot.json");
  else if (path === "ai/brief/latest") data = fx("brief.json");
  else if (path === "ai/notes") data = fx("ai-notes-new.json");
  else if (path === "ai/conversations") data = { data: [], currentPage: 1, itemsPerPage: 20, totalItems: 0, totalPages: 0 };
  else if (path === "ai/chat") data = chatAnswer(body && body.text, commandOf(body && body.text));
  else if (path === "ai/voice") {
    const c = commandOf(spoken);
    data = { ...chatAnswer(spoken, c), transcript: spoken, audioBase64: audio.answer };
    lastVoiceForm = req.postDataBuffer() ? req.postDataBuffer().toString("latin1") : "";
  }
  else if (path === "office/inbox/counts") data = { open: 4 };
  else if (path === "Accounts/get-current-user-info") data = { firstName: "کاربر", lastName: "آزمایشی", profilePhotoId: null };
  // the analysis and the assistant take a moment on a real server: a short delay keeps the busy and thinking states honest
  const delay = path === "ceo/analyze" ? 350 : path === "ai/voice" ? 700 : path === "ai/chat" ? 300 : 0;
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
  // voice mode: these take their own screenshots (several per run) and print their checks
  { name: "ceo-1440-voice", viewport: { width: 1440, height: 900 }, scheme: "light", act: voiceFlow, ownShots: true },
  { name: "ceo-1440-voice-chips", viewport: { width: 1440, height: 900 }, scheme: "light", act: voiceChips, ownShots: true },
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
  await page.click(".cap-open");
  await page.waitForSelector(".assist-drawer .ai-panel");
  await page.click(".assist-chips:not(.cmds) .ceo-chip >> nth=0");
  await page.waitForTimeout(300);
}

// ---------------------------------------------------------------- voice mode
const checks = [];
function check(name, ok, detail = "") {
  checks.push({ name, ok: !!ok, detail });
}
const state = (page) => page.evaluate(() => {
  const cap = document.querySelector(".capsule");
  const panel = document.querySelector(".ai-voice");
  const of = (el, prefix) => (el ? [...el.classList].find((c) => c.startsWith(prefix)) || "" : "");
  return { capsule: of(cap, "cs-").slice(3), panel: of(panel, "vs-").slice(3), mic: window.__mic.stats() };
});
async function waitState(page, which, wanted, timeout = 10000) {
  const t0 = Date.now();
  while (Date.now() - t0 < timeout) {
    if ((await state(page))[which] === wanted) return true;
    await page.waitForTimeout(100);
  }
  return false;
}

/** Space → listening (waveform) → silence → ai/voice → openDrill → the answer spoken → the follow-up window → the microphone closed; then barge-in and Esc. */
async function voiceFlow(page) {
  spoken = voiceFx.commands[0].command;
  await page.evaluate(() => window.__mic.talk(false));
  await page.keyboard.down("Space");
  await page.waitForTimeout(80);
  await page.keyboard.up("Space"); // a short press: listening goes on until a short silence
  check("Space starts listening", await waitState(page, "capsule", "listening", 3000));
  await page.waitForTimeout(450); // the first 300 ms measure the room (silence here)
  await page.evaluate(() => window.__mic.talk(true));
  await page.waitForTimeout(1400);
  await page.screenshot({ path: join(out, "ceo-1440-voice-listening.png") });
  console.log("saved ceo-1440-voice-listening.png");
  const t0 = Date.now();
  await page.evaluate(() => window.__mic.talk(false)); // silence: the question is sent by itself
  check("silence ends the question (thinking)", await waitState(page, "capsule", "thinking", 4000), `after ${Date.now() - t0} ms of silence`);
  check("silence detection waited ≥ 1.2 s", Date.now() - t0 >= 1150, `${Date.now() - t0} ms`);
  check("the microphone closed after listening", (await state(page)).mic.live === 0, JSON.stringify((await state(page)).mic));
  check("the answer is spoken", await waitState(page, "capsule", "speaking", 6000));
  check("uiContext sent with the recording", /name="uiContext\.page"\r\n\r\nceo/.test(lastVoiceForm) && /name="uiContext\.sections\[8\]"/.test(lastVoiceForm));
  await page.waitForSelector(".drill-table tbody tr", { timeout: 6000 });
  await page.waitForTimeout(700); // the slide-over
  const title = await page.textContent("#ceo-drill-title");
  check("openDrill opened mf.orders_late", title.includes("سفارش‌های تولید با تاخیر"), title);
  await page.screenshot({ path: join(out, "ceo-1440-voice-speaking.png") });
  console.log("saved ceo-1440-voice-speaking.png");
  const acts = await page.$$eval(".ai-acts li", (els) => els.map((e) => e.textContent.trim()));
  check("the action is confirmed in the turn", acts.some((t) => t.startsWith("✓") && t.includes("باز شد")), acts.join(" | "));
  check("follow-up window after the answer", await waitState(page, "capsule", "listening", 9000));
  const count = await page.textContent(".cc-count").catch(() => "");
  check("follow-up countdown shown", !!count, count);
  check("the follow-up window closes by itself", await waitState(page, "capsule", "idle", 8000));
  check("no open microphone track at the end", (await state(page)).mic.live === 0, JSON.stringify((await state(page)).mic));
  // barge-in: Space while the assistant speaks stops the speech and listens
  await page.keyboard.press("Escape"); // closes the drill panel (the voice is idle)
  await page.click("#ceo-sec-production .ceo-read");
  check("«بخوان» reads a section", await waitState(page, "capsule", "speaking", 4000));
  const pressed = await page.getAttribute("#ceo-sec-production .ceo-read", "aria-pressed");
  check("«بخوان» shows «توقف» while reading", pressed === "true", pressed);
  await page.keyboard.press("Space");
  check("barge-in: Space while speaking listens", await waitState(page, "capsule", "listening", 3000));
  await page.keyboard.press("Escape");
  check("Esc stops listening", await waitState(page, "capsule", "idle", 2000));
  check("Esc closed the microphone", (await state(page)).mic.live === 0, JSON.stringify((await state(page)).mic));
}

/** The voice-command chips of the drawer (ai/chat with actions) and the read-out of the alerts. */
async function voiceChips(page) {
  await page.click(".cap-open");
  await page.waitForSelector(".assist-drawer .ai-panel");
  await page.click(".assist-chips.cmds .ceo-chip >> text=بخوان هشدارها");
  check("chip «بخوان هشدارها»: the alerts are read", await waitState(page, "panel", "speaking", 4000));
  await page.waitForTimeout(2000); // the answer first (shown for its reading time: no audio from ai/chat, no Persian voice here), then the alerts
  const acts = await page.$$eval(".ai-acts li", (els) => els.map((e) => e.textContent.trim()));
  const caption = await page.textContent(".ai-voice .ai-caption").catch(() => "");
  check("the read action is confirmed and its text captioned", acts.some((t) => t.includes("هشدارها خوانده می‌شود")) && caption.includes("سیزده هشدار"), acts.join(" | ") + " / " + caption.slice(0, 40));
  await page.screenshot({ path: join(out, "ceo-1440-voice-drawer.png") });
  console.log("saved ceo-1440-voice-drawer.png");
  await page.keyboard.press("Escape");
  check("Esc stops the read-out, the drawer stays", (await waitState(page, "panel", "idle", 2000)) && (await page.isVisible(".assist-drawer")));
  await page.click(".assist-chips.cmds .ceo-chip >> text=تحلیل کن بخش فروش");
  await page.waitForSelector("#ceo-sec-sales .analysis-text", { timeout: 6000 }).catch(() => null);
  const analysis = await page.textContent("#ceo-sec-sales .analysis-text").catch(() => "");
  check("chip «تحلیل کن بخش فروش»: the sales analysis runs", analysis.includes("۲٬۳۴۰"), analysis.slice(0, 60));
  await waitState(page, "panel", "idle", 8000);
  await page.click(".assist-chips.cmds .ceo-chip >> text=نشانم بده پروژه‌های قرمز");
  await page.waitForSelector(".drill-filter", { timeout: 6000 }).catch(() => null);
  await page.waitForTimeout(700);
  const rows = await page.$$eval(".drill-table tbody tr", (els) => els.length);
  const filter = await page.textContent(".drill-filter").catch(() => "");
  check("chip «پروژه‌های قرمز»: pc.portfolio with the red filter", rows === 2 && filter.includes("بحرانی"), `${rows} rows, ${filter.trim()}`);
  await page.screenshot({ path: join(out, "ceo-1440-voice-filter.png") });
  console.log("saved ceo-1440-voice-filter.png");
  await page.click(".drill-filter .ceo-chip");
  await page.waitForTimeout(500);
  const all = await page.$$eval(".drill-table tbody tr", (els) => els.length);
  check("«نمایش همه» drops the filter", all === 8, `${all} rows`);
}
async function runAnalysis(page) {
  // Bootstrap makes every scroll smooth; the review scrolls are instant so the shot is not taken mid-way
  await page.evaluate(() => document.getElementById("ceo-sec-projects").scrollIntoView({ block: "start", behavior: "instant" }));
  await page.click("#ceo-sec-projects .sec-analyze");
  await page.waitForSelector("#ceo-sec-projects .analysis-text");
  await page.evaluate(() => document.querySelector("#ceo-sec-projects .analysis").scrollIntoView({ block: "center", behavior: "instant" }));
  await page.waitForTimeout(500);
}
async function scrollToService(page) {
  await page.click(".ann-window >> text=خدمات");
  await page.waitForTimeout(1500);
}

// the synthetic microphone: getUserMedia answers a WebAudio "voice" (a buzzing tone with a syllable rhythm) that window.__mic.talk(on) switches on and off
const fakeMicrophone = () => {
  const mic = { on: false, gates: [], opened: 0, live: 0 };
  window.__mic = {
    talk(on) {
      mic.on = on;
      mic.gates.forEach((g) => g.gain.setValueAtTime(on ? 1 : 0, g.context.currentTime));
    },
    stats: () => ({ opened: mic.opened, live: mic.live }),
  };
  if (!navigator.mediaDevices) return;
  navigator.mediaDevices.getUserMedia = async () => {
    const ctx = new AudioContext();
    await ctx.resume();
    const dest = ctx.createMediaStreamDestination();
    const tone = ctx.createOscillator();
    tone.type = "sawtooth";
    tone.frequency.value = 150;
    const vibrato = ctx.createOscillator();
    vibrato.frequency.value = 5;
    const vibratoDepth = ctx.createGain();
    vibratoDepth.gain.value = 25;
    vibrato.connect(vibratoDepth).connect(tone.frequency);
    const syllables = ctx.createOscillator();
    syllables.frequency.value = 3.5;
    const envelope = ctx.createGain();
    envelope.gain.value = 0.25;
    const depth = ctx.createGain();
    depth.gain.value = 0.2;
    syllables.connect(depth).connect(envelope.gain);
    const gate = ctx.createGain();
    gate.gain.value = mic.on ? 1 : 0;
    tone.connect(envelope).connect(gate).connect(dest);
    [tone, vibrato, syllables].forEach((o) => o.start());
    mic.gates.push(gate);
    mic.opened++;
    mic.live++;
    for (const t of dest.stream.getAudioTracks()) {
      const stop = t.stop.bind(t);
      t.stop = () => {
        if (t.readyState !== "ended") mic.live--;
        stop();
        mic.gates = mic.gates.filter((g) => g !== gate);
        ctx.close();
      };
    }
    return dest.stream;
  };
};

const browser = await chromium.launch({ args: ["--autoplay-policy=no-user-gesture-required"] });
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
  await context.addInitScript(fakeMicrophone);
  const page = await context.newPage();
  await page.clock.setFixedTime(new Date("2026-10-25T07:44:30+03:30"));
  page.on("pageerror", (e) => errors.push(`${shot.name}: ${e.message}`));
  page.on("console", (m) => m.type() === "error" && errors.push(`${shot.name} console: ${m.text()}`));
  await page.route("**/api/v1/**", (route) => answer(route, role));
  await page.goto(base + "/ceo", { waitUntil: "networkidle" });
  await page.waitForSelector(".ceo-kpis", { timeout: 15000 });
  if (process.env.CEO_PREVIEW_CSS) await page.addStyleTag({ content: process.env.CEO_PREVIEW_CSS });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(1700); // the lamp test and the first chart draw
  if (shot.act) await shot.act(page);
  if (shot.print) {
    await page.emulateMedia({ media: "print" });
    await page.waitForTimeout(400);
    await page.pdf({ path: join(out, "ceo-print.pdf"), format: "A4", printBackground: true, margin: { top: "14mm", bottom: "16mm", left: "12mm", right: "12mm" } });
  }
  if (!shot.ownShots) {
    // two frames and a moment: every chart has finished its last redraw before the capture
    await page.evaluate(() => new Promise((ok) => requestAnimationFrame(() => requestAnimationFrame(ok))));
    await page.waitForTimeout(400);
    const file = shot.name + (process.env.CEO_PREVIEW_SUFFIX || "") + ".png";
    await page.screenshot({ path: join(out, file), fullPage: shot.fullPage !== false });
    console.log("saved", file);
  }
  await context.close();
}
await browser.close();
server.close();
if (checks.length) {
  console.log("\nvoice checks:");
  for (const c of checks) console.log(`  ${c.ok ? "ok  " : "FAIL"} ${c.name}${c.detail ? "  — " + c.detail : ""}`);
}
if (errors.length) {
  console.log("\npage errors:");
  for (const e of errors) console.log("  " + e);
}
