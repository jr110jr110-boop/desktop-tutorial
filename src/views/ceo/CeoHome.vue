<script setup lang="ts">
// The CEO's home page (release D1, route /ceo): the whole company in one place. Header (company, today's Shamsi date, "updated x minutes ago", refresh, theme, print);
// the morning line (one sentence composed from the data) with the daily brief beside it; the pulse of the nine modules as an annunciator panel (the page's one bold device,
// each window scrolls to its section); the 8 headline KPIs; the nine sections (KPIs, charts, details, AI analysis) beside the alerts column; the assistant drawer;
// the detail tables (drill panel) and their catalogue. Data: GET ceo/overview (api/CeoAPIs.ts). The brief and the assistant do not depend on the overview: they keep working
// when it cannot be read, and the overview keeps working when the AI is down. Nothing of the business data is stored in the browser; only the theme choice is (composables/ceo.ts).
//
// Voice mode (§7.3, "like Siri"): the page owns the voice session (composables/voice.ts) — the capsule at the bottom-left, holding Space anywhere outside a field, the «بخوان»
// buttons — and hands it to the assistant panel in the drawer, which asks the questions (with uiContext) and answers them. The UI actions of an answer (§7.1) are carried out
// here by runActions(), in order, each with a one-line confirmation: navigate (inside the app, after the spoken answer), openDrill (with params), analyze, read (GET ceo/speech,
// or the brief), scroll. Esc stops listening, speaking and the follow-up window.
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref } from "vue";
import { useRouter } from "vue-router";
import { analyzeCeo, getCeoDrills, getCeoOverview, getCeoSpeech, snapshotCeo, type CeoDrillDescriptor, type CeoKpi, type CeoOverview, type CeoSection } from "@/api/CeoAPIs";
import { getAiBrief, getAiBriefAudio, getAiNotes, type AiUiAction, type AiUiContext } from "@/api/AiAPIs";
import BriefCard from "@/components/ai/BriefCard.vue";
import CeoAnalysisBox from "@/components/ceo/CeoAnalysis.vue";
import CeoAssistantDrawer from "@/components/ceo/CeoAssistantDrawer.vue";
import CeoCatalogue from "@/components/ceo/CeoCatalogue.vue";
import CeoDrillPanel from "@/components/ceo/CeoDrillPanel.vue";
import CeoIcon from "@/components/ceo/CeoIcon.vue";
import CeoKpiCluster from "@/components/ceo/CeoKpiCluster.vue";
import CeoPulse from "@/components/ceo/CeoPulse.vue";
import CeoSectionPanel from "@/components/ceo/CeoSection.vue";
import CeoSideColumn from "@/components/ceo/CeoSideColumn.vue";
import CeoVoiceCapsule from "@/components/ceo/CeoVoiceCapsule.vue";
import { agoText, COMPANY_NAME, faDigits, faNum, morningLine, openableLink, suggestedQuestions, todayText, useCeoTheme, voiceCommands, type AnalysisState } from "@/composables/ceo";
import { fail, ok } from "@/composables/inventory";
import { AiRoles, hasRole } from "@/composables/roles";
import { useVoiceSession, VOICE_TIMING, type ActionOutcome, type ActionResult, type SpeechItem } from "@/composables/voice";

const router = useRouter();
const { theme, toggle: toggleTheme } = useCeoTheme();
const canBrief = hasRole(AiRoles.brief);

// ---------------------------------------------------------------- the overview
const overview = ref<CeoOverview | null>(null);
const loading = ref(false);
const error = ref("");
const lampTest = ref(false);
let firstPaint = true;

async function load(refresh = false) {
  if (loading.value) return;
  loading.value = true;
  error.value = "";
  const [data, err] = await getCeoOverview(refresh);
  loading.value = false;
  if (err) {
    // a failed refresh keeps the numbers on the screen and says so; a failed first load shows what happened
    if (overview.value) fail(err);
    else error.value = err.message || "داشبورد خوانده نشد.";
    return;
  }
  if (firstPaint) {
    lampTest.value = true;
    firstPaint = false;
  }
  // the contract names the alert level «severity»; the older CEO dashboard service wrote «level»: both are read, the contract name wins
  data.alerts = (data.alerts || []).map((a: any) => ({ ...a, severity: a.severity || a.level || "info" }));
  overview.value = data;
}

const now = ref(Date.now());
let ticker: ReturnType<typeof setInterval> | null = null;
const ago = computed(() => agoText(overview.value?.generatedAt, now.value));
const updatedTitle = computed(() => {
  const o = overview.value;
  if (!o) return "";
  return `تهیه در ${faNum(o.generatedInMs, 0)} میلی‌ثانیه${o.cached ? "؛ از حافظه‌ی موقت سرور (کمتر از ۲ دقیقه). «بازخوانی» داده‌ی تازه می‌خواند." : ""}`;
});
const today = todayText();
const morning = computed(() => (overview.value ? morningLine(overview.value) : null));
const pulseOf = (key: string) => overview.value?.pulse.find((p) => p.key === key)?.status || "neutral";

// ---------------------------------------------------------------- sections: scrolling from the pulse
function reduceMotion() {
  try {
    return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  } catch {
    return false;
  }
}
function goToSection(key: string) {
  const el = document.getElementById("ceo-sec-" + key);
  if (!el) return;
  el.scrollIntoView({ behavior: reduceMotion() ? "auto" : "smooth", block: "start" });
  el.querySelector<HTMLElement>(".sec-title")?.focus({ preventScroll: true });
}

// ---------------------------------------------------------------- analysis (kept for this visit only)
const analyses = reactive<Record<string, AnalysisState>>({});
async function analyze(section: CeoSection | null) {
  const id = section?.key || "*";
  const cur = analyses[id];
  if (cur?.busy) return;
  if (cur?.result && !cur.open) {
    cur.open = true;
    return;
  }
  analyses[id] = { busy: true, error: "", result: cur?.result || null, startedAt: Date.now(), open: true };
  const [data, err] = await analyzeCeo({ sectionKey: section?.key ?? null, question: section?.analysisPrompt || null });
  const state = analyses[id];
  state.busy = false;
  if (err) {
    state.error = err.message || "تحلیل انجام نشد.";
    return;
  }
  state.result = data;
}
function closeAnalysis(id: string) {
  if (analyses[id]) analyses[id].open = false;
}

// ---------------------------------------------------------------- the drill catalogue and the drill panel
const drills = ref<CeoDrillDescriptor[]>([]);
const drillsLoading = ref(false);
const drillsError = ref("");
async function loadDrills() {
  drillsLoading.value = true;
  drillsError.value = "";
  const [data, err] = await getCeoDrills();
  drillsLoading.value = false;
  if (err) {
    drillsError.value = err.message || "فهرست جدول‌ها خوانده نشد.";
    return;
  }
  drills.value = Array.isArray(data) ? data : [];
}
const drillTitle = (key: string, fallback: string) => drills.value.find((d) => d.key === key)?.title || fallback;

/** params: the filter of an assistant's openDrill action (sent to ceo/drill with the key; cleared when another table is picked) */
const drill = reactive({ open: false, key: "", related: [] as { key: string; title: string }[], params: null as Record<string, any> | null });
function relatedOf(items: { drillKey: string | null; title: string }[]) {
  const seen = new Set<string>();
  const out: { key: string; title: string }[] = [];
  for (const it of items) {
    if (!it.drillKey || seen.has(it.drillKey)) continue;
    seen.add(it.drillKey);
    out.push({ key: it.drillKey, title: drillTitle(it.drillKey, it.title) });
  }
  return out;
}
function openDrill(key: string, related: { key: string; title: string }[] = [], params: Record<string, any> | null = null) {
  catalogueOpen.value = false;
  drill.key = key;
  drill.params = params && Object.keys(params).length ? { ...params } : null;
  drill.related = related.some((r) => r.key === key) ? related : [{ key, title: drillTitle(key, "جزئیات") }, ...related];
  drill.open = true;
}
function openSectionDrill(section: CeoSection, key: string) {
  openDrill(key, relatedOf([...section.charts, ...section.kpis]));
}
function openKpi(k: CeoKpi) {
  if (k.drillKey) openDrill(k.drillKey, relatedOf(overview.value?.headline || []));
  else {
    const to = openableLink(router, k.link);
    if (to) router.push(to);
  }
}
const catalogueOpen = ref(false);
function openCatalogue() {
  catalogueOpen.value = true;
  if (!drills.value.length && !drillsLoading.value) loadDrills();
}

// ---------------------------------------------------------------- the assistant drawer
const drawerOpen = ref(false);
const drawerRef = ref<InstanceType<typeof CeoAssistantDrawer> | null>(null);
const width = ref(typeof window === "undefined" ? 1440 : window.innerWidth);
const beside = computed(() => width.value >= 1280);
const DRAWER_W = 400;
function onResize() {
  width.value = window.innerWidth;
}
/**
 * The layout follows the width the page really has (the app's menu and the assistant drawer take part of the window): xl = sections in two columns with the alerts beside them,
 * l = one column of sections under an alerts band, m = the same with the KPIs in two columns, s = a phone.
 */
const root = ref<HTMLElement | null>(null);
/** the content column (max 1360px, 32px gutters): its inner width decides the layout */
const pageBox = ref<HTMLElement | null>(null);
const pageWidth = ref(1100);
const size = computed(() => (pageWidth.value >= 1080 ? "xl" : pageWidth.value >= 820 ? "l" : pageWidth.value >= 560 ? "m" : "s"));
let observer: ResizeObserver | null = null;
function measurePage() {
  const el = pageBox.value;
  if (!el) return;
  const cs = getComputedStyle(el);
  pageWidth.value = el.clientWidth - parseFloat(cs.paddingLeft || "0") - parseFloat(cs.paddingRight || "0");
}
function askAssistant(question: string) {
  drill.open = false;
  drawerOpen.value = true;
  nextTick(() => (drawerRef.value as any)?.ask?.(question));
}
const suggestions = computed(() => {
  const rank: Record<string, number> = { danger: 0, warn: 1, ok: 2, neutral: 3 };
  return (overview.value?.sections || [])
    .map((s) => ({ key: s.key, title: s.title, questions: suggestedQuestions[s.key] || [], r: rank[pulseOf(s.key)] ?? 3 }))
    .filter((s) => s.questions.length)
    .sort((a, b) => a.r - b.r);
});
/** px the panels keep free at the left edge while the drawer is beside the page */
const panelOffset = computed(() => (drawerOpen.value && beside.value ? DRAWER_W : 0));

// ---------------------------------------------------------------- side column: AI notes, quick links, snapshot
const notesCount = ref<number | null>(null);
async function loadNotes() {
  const [data, err] = await getAiNotes({ Status: 1, PageNumber: 1, PageSize: 1 });
  if (err) return; // the count is a convenience: without it the row shows a dash
  notesCount.value = Number(data?.totalItems ?? (Array.isArray(data) ? data.length : 0)) || 0;
}
const quickLinks = computed(() => {
  const list = (overview.value?.sections || []).map((s) => ({ title: s.title, to: openableLink(router, s.link) || "" }));
  list.push({ title: "دستیار هوشمند", to: openableLink(router, "/ai/assistant") || "" }, { title: "یادداشت‌های هوش مصنوعی", to: openableLink(router, "/ai/notes") || "" });
  return list.filter((l) => l.to);
});
const snapshotBusy = ref(false);
async function snapshot() {
  snapshotBusy.value = true;
  const [data, err] = await snapshotCeo();
  snapshotBusy.value = false;
  if (err) return fail(err);
  ok(`نقطه‌ی امروز روندها برای ${faNum(data.keys, 0)} شاخص ثبت شد (${data.dateText}).`);
  load(true);
}

// ---------------------------------------------------------------- the brief beside the morning line: a few lines, the whole text on request (no scroll box inside the page)
const briefBox = ref<HTMLElement | null>(null);
const briefOpen = ref(false);
const briefLong = ref(false);
let briefObserver: ResizeObserver | null = null;
function checkBrief() {
  const text = briefBox.value?.querySelector<HTMLElement>(".brief-text");
  briefLong.value = !!text && text.scrollHeight > text.clientHeight + 2;
}

// ---------------------------------------------------------------- voice mode (§7.3): the session, the UI actions of the assistant (§7.1), reading aloud (§7.2)
const voice = useVoiceSession();
/** sent with every question of this page: the model learns which sections and UI actions exist here */
const uiContext = computed<AiUiContext>(() => ({ page: "ceo", sections: (overview.value?.sections || []).map((s) => s.key), theme: theme.value }));
/** the route prefixes the assistant may open (the same allow-list as the server's ui_navigate) */
const NAV_PREFIXES = ["/ceo", "/finance", "/fn", "/sales", "/service", "/production", "/project-control", "/quality", "/inventory", "/procurement", "/office", "/ai"];
const sectionOf = (key: string | null | undefined) => (key ? overview.value?.sections.find((s) => s.key === key) || null : null);
/** the places a read-out or a scroll can show besides the sections */
const ANCHORS: Record<string, string> = { headline: "ceo-headline", alerts: "ceo-alerts-title", morning: "ceo-morning" };
function reveal(target: string) {
  if (sectionOf(target)) return goToSection(target);
  document.getElementById(ANCHORS[target] || "")?.scrollIntoView({ behavior: reduceMotion() ? "auto" : "smooth", block: "start" });
}
type ReadWhat = "headline" | "section" | "alerts" | "brief";
const readTitles: Record<string, string> = { headline: "شاخص‌های اصلی", alerts: "هشدارها", brief: "گزارش روزانه" };
const readLabel = (what: string, sectionKey?: string | null) => (what === "section" ? `بخش «${sectionOf(sectionKey)?.title || sectionKey}»` : readTitles[what] || what);

/** The spoken summary: GET ceo/speech (headline, a section, the alerts; works without the language model), or the latest brief with its audio. */
async function speechFor(what: ReadWhat, sectionKey: string | null = null): Promise<{ item: SpeechItem | null; error: string }> {
  if (what === "brief") {
    if (!canBrief) return { item: null, error: "گزارش روزانه فقط برای مدیرعامل خوانده می‌شود" };
    const [brief, err] = await getAiBrief();
    if (err || !brief?.text) return { item: null, error: err?.message || "گزارش روزانه‌ای برای خواندن نیست" };
    const [blob] = await getAiBriefAudio();
    return { item: { text: brief.text, audioBlob: blob }, error: "" };
  }
  const [data, err] = await getCeoSpeech(what, what === "section" ? sectionKey : null);
  if (err || !data?.text) return { item: null, error: err?.message || "خلاصه‌ی صوتی " + readLabel(what, sectionKey) + " ساخته نشد" };
  return { item: { text: data.text, audioBase64: data.audioBase64 }, error: "" };
}

/** A value of an assistant's drill filter in words: the status words of the page, otherwise as it came. */
function paramText(params: Record<string, any> | null, key: string | null) {
  if (!params) return "";
  const d = drills.value.find((x) => x.key === key);
  const status: Record<string, string> = { danger: "قرمز (بحرانی)", red: "قرمز (بحرانی)", warn: "زرد (هشدار)", amber: "زرد (هشدار)", ok: "سبز (عادی)", green: "سبز (عادی)" };
  return Object.entries(params)
    .filter(([, v]) => v !== null && v !== undefined && v !== "")
    .map(([k, v]) => (d?.parameters.find((p) => p.name === k)?.description || k) + ": " + (status[String(v)] || (/^-?\d+(\.\d+)?$/.test(String(v)) ? faNum(Number(v), 0) : faDigits(String(v)))))
    .join("، ");
}

/** One UI action of an answer (§7.1). Leaving the page is queued in out.deferred (it runs after the spoken answer); read-outs are queued in out.speech. */
async function runAction(a: AiUiAction, out: ActionOutcome): Promise<ActionResult> {
  const title = String(a.title || "").trim();
  switch (a.type) {
    case "navigate": {
      const link = String(a.link || "").trim();
      const inside = link.startsWith("/") && !link.startsWith("//") && NAV_PREFIXES.some((p) => link === p || link.startsWith(p + "/") || link.startsWith(p + "?"));
      const to = inside ? openableLink(router, link) : null;
      if (!to) return { ok: false, text: `صفحه‌ی «${title || link}» در این سامانه یا برای نقش شما باز نمی‌شود` };
      if (router.resolve(to).path === router.currentRoute.value.path) return { ok: true, text: `همین صفحه باز است` };
      out.deferred.push(() => router.push(to));
      return { ok: true, text: `صفحه‌ی «${title || link}» پس از پایان پاسخ باز می‌شود` };
    }
    case "openDrill": {
      const key = String(a.drillKey || "");
      const known = drills.value.length ? drills.value.some((d) => d.key === key) : !!key;
      if (!known) return { ok: false, text: `جدول «${title || key}» در فهرست جدول‌های جزئیات نیست` };
      const section = overview.value?.sections.find((s) => [...s.charts, ...s.kpis].some((x) => x.drillKey === key));
      const params = a.params && typeof a.params === "object" ? a.params : null;
      openDrill(key, section ? relatedOf([...section.charts, ...section.kpis]) : [], params);
      const filter = paramText(drill.params, key);
      return { ok: true, text: `جدول «${drillTitle(key, title || "جزئیات")}» باز شد` + (filter ? `؛ فیلتر ${filter}` : "") };
    }
    case "analyze": {
      const section = sectionOf(a.sectionKey);
      if (a.sectionKey && !section) return { ok: false, text: `بخش «${title || a.sectionKey}» در این صفحه نیست` };
      drill.open = false;
      catalogueOpen.value = false;
      analyze(section);
      reveal(section ? section.key : "morning");
      return { ok: true, text: section ? `تحلیل بخش «${section.title}» آغاز شد؛ نتیجه در همان بخش می‌آید` : "تحلیل کل شرکت آغاز شد؛ نتیجه زیر خط امروز می‌آید" };
    }
    case "read": {
      const what = String(a.what || "") as ReadWhat;
      if (!["headline", "section", "alerts", "brief"].includes(what)) return { ok: false, text: `«${title || what}» خواندنی نیست` };
      if (what === "section" && !sectionOf(a.sectionKey)) return { ok: false, text: `بخش «${title || a.sectionKey}» در این صفحه نیست` };
      const got = await speechFor(what, a.sectionKey || null);
      if (!got.item) return { ok: false, text: got.error };
      out.speech.push(got.item);
      if (what !== "brief") {
        drill.open = false;
        catalogueOpen.value = false;
        reveal(what === "section" ? String(a.sectionKey) : what);
      }
      return { ok: true, text: `${readLabel(what, a.sectionKey)} خوانده می‌شود` };
    }
    case "scroll": {
      const key = String(a.sectionKey || "");
      if (!sectionOf(key) && !ANCHORS[key]) return { ok: false, text: `بخش «${title || key}» در این صفحه نیست` };
      drill.open = false;
      catalogueOpen.value = false;
      reveal(key);
      return { ok: true, text: `${sectionOf(key) ? "بخش «" + sectionOf(key)!.title + "»" : readTitles[key] || title} نمایش داده شد` };
    }
    default:
      return { ok: false, text: `کار «${title || a.type || "نامشخص"}» در این صفحه انجام‌شدنی نیست` };
  }
}

/** Carries out the UI actions of an answer in order (at most 8). On a tablet or a phone the drawer covers the page: it closes first, so what the actions show is seen. */
async function runActions(actions: AiUiAction[]): Promise<ActionOutcome> {
  const out: ActionOutcome = { results: [], speech: [], deferred: [] };
  const shows = actions.some((a) => a.type !== "navigate" && !(a.type === "read" && a.what === "brief"));
  if (shows && drawerOpen.value && !beside.value) drawerOpen.value = false;
  for (const a of actions.slice(0, 8)) out.results.push(await runAction(a || ({} as AiUiAction), out));
  return out;
}

/** The «بخوان» buttons: the spoken summary of the headline, a section or the alerts (the same button again stops it). */
const reading = ref<string | null>(null);
let readSeq = 0;
function stopReading() {
  readSeq++;
  reading.value = null;
  voice.cancel();
}
async function readAloud(what: "headline" | "section" | "alerts", sectionKey: string | null = null) {
  const id = what === "section" ? "section:" + sectionKey : what;
  if (reading.value === id) return stopReading();
  const mine = ++readSeq;
  voice.cancel(); // listening, a follow-up window or another read-out stop
  reading.value = id;
  const mark = voice.mark();
  voice.setThinking(true);
  const got = await speechFor(what, sectionKey);
  if (mine !== readSeq) return; // stopped, or another read-out took over
  voice.setThinking(false);
  if (!got.item) {
    voice.caption("system", got.error);
    reading.value = null;
    return;
  }
  await voice.say([got.item], { followup: false, epoch: mark });
  if (mine === readSeq) reading.value = null;
}

// Space anywhere outside a text field: held = push-to-talk, a short press = listen until a short silence, pressed while listening = send now, while the assistant speaks =
// barge-in. Space does not scroll the page or press the focused button here (Enter still does). Esc stops everything. Both in the capture phase, so Esc stops the voice before
// it closes a drawer or a slide-over (a second Esc closes them).
let spaceAt = 0;
let spaceHeld = false;
let spaceOurs = false;
function typingIn(target: EventTarget | null) {
  const el = target instanceof HTMLElement ? target : null;
  return !!el?.closest('input, textarea, select, [contenteditable=""], [contenteditable="true"], [role="textbox"]');
}
function onKeyDown(e: KeyboardEvent) {
  if (e.key === "Escape") {
    if (voice.state !== "idle" || voice.countdown || reading.value) {
      e.preventDefault();
      e.stopPropagation();
      stopReading();
    }
    return;
  }
  if (e.code !== "Space" || e.ctrlKey || e.altKey || e.metaKey || e.shiftKey || !voice.supported || typingIn(e.target)) return;
  e.preventDefault();
  e.stopPropagation();
  spaceOurs = true;
  if (e.repeat) return;
  if (voice.state === "listening") {
    spaceHeld = false;
    voice.stop();
    return;
  }
  if (voice.state === "thinking") return;
  readSeq++;
  reading.value = null;
  spaceHeld = true;
  spaceAt = Date.now();
  voice.start();
}
function onKeyUp(e: KeyboardEvent) {
  if (e.code !== "Space" || !spaceOurs) return;
  e.preventDefault(); // the focused button is not pressed by the release either
  e.stopPropagation();
  spaceOurs = false;
  if (!spaceHeld) return;
  spaceHeld = false;
  if (voice.state === "listening" && Date.now() - spaceAt >= VOICE_TIMING.hold) voice.stop();
}

// ---------------------------------------------------------------- print
function printPage() {
  stopReading();
  drawerOpen.value = false;
  drill.open = false;
  catalogueOpen.value = false;
  nextTick(() => window.print());
}

onMounted(() => {
  load();
  loadDrills();
  loadNotes();
  ticker = setInterval(() => (now.value = Date.now()), 30000);
  window.addEventListener("resize", onResize);
  window.addEventListener("keydown", onKeyDown, true);
  window.addEventListener("keyup", onKeyUp, true);
  measurePage();
  if (typeof ResizeObserver !== "undefined" && pageBox.value) {
    observer = new ResizeObserver(measurePage);
    observer.observe(pageBox.value);
  }
  if (typeof ResizeObserver !== "undefined" && briefBox.value) {
    briefObserver = new ResizeObserver(checkBrief);
    briefObserver.observe(briefBox.value);
  }
});
onBeforeUnmount(() => {
  if (ticker) clearInterval(ticker);
  window.removeEventListener("resize", onResize);
  window.removeEventListener("keydown", onKeyDown, true);
  window.removeEventListener("keyup", onKeyUp, true);
  observer?.disconnect();
  briefObserver?.disconnect();
});
</script>

<template>
  <div ref="root" :class="['w-' + size, { 'drawer-beside': drawerOpen && beside }]" :data-ceo-theme="theme" :style="'--ceo-drawer-w: ' + DRAWER_W + 'px'" class="ceo" dir="rtl">
    <!-- printout heading (A4, B Zar) -->
    <div class="ceo-print-head">
      <div>
        <div class="ceo-print-company">{{ COMPANY_NAME }}</div>
        <h1 class="ceo-print-title">گزارش وضعیت شرکت</h1>
      </div>
      <div class="ceo-print-meta">
        <div>{{ today }}</div>
        <div v-if="overview">داده‌ها تا {{ overview.asOfText }}، تهیه {{ ago }}</div>
      </div>
    </div>

    <!-- the header band: the page header and the morning line on the faint engineering grid (the only texture of the page) -->
    <div class="ceo-band">
      <div ref="pageBox" class="ceo-page">
    <header class="ceo-header">
      <div class="ceo-id">
        <span class="ceo-company">{{ COMPANY_NAME }}</span>
        <h1 class="ceo-title">داشبورد مدیرعامل</h1>
      </div>
      <div class="ceo-header-tools">
        <span class="ceo-today">{{ today }}</span>
        <span v-if="overview" :title="updatedTitle" class="ceo-updated">به‌روز شده {{ ago }}</span>
        <button :aria-busy="loading" :disabled="loading" class="ceo-btn ghost" title="خواندن دوباره‌ی همه‌ی داده‌ها از سرور" type="button" @click="load(true)">
          <CeoIcon :class="{ spinning: loading && !!overview }" :size="16" name="refresh" />
          بازخوانی
        </button>
        <button :aria-label="theme === 'dark' ? 'حالت روشن' : 'حالت تیره'" :title="theme === 'dark' ? 'حالت روشن' : 'حالت تیره'" class="ceo-icon-btn" type="button" @click="toggleTheme">
          <CeoIcon :name="theme === 'dark' ? 'sun' : 'moon'" />
        </button>
        <button aria-label="چاپ" class="ceo-icon-btn" title="چاپ (A4)" type="button" @click="printPage"><CeoIcon name="print" /></button>
      </div>
    </header>

    <!-- the morning line and the daily brief -->
    <section id="ceo-morning" :class="{ 'no-brief': !canBrief }" aria-label="خلاصه‌ی امروز" class="ceo-morning">
      <div class="morning-main">
        <p v-if="morning" class="morning-text">
          <span class="morning-lead">{{ morning.lead }}:</span>{{ " " }}
          <template v-for="(p, i) in morning.parts" :key="i">
            <span :class="{ attention: p.attention }" class="morning-part">{{ p.text }}</span><template v-if="i < morning.parts.length - 1">، </template>
          </template>
        </p>
        <div v-else-if="!error" aria-label="در حال بارگذاری" class="morning-skel">
          <span class="ceo-skel" style="width: 82%" />
          <span class="ceo-skel" style="width: 46%" />
        </div>
        <div v-if="overview" class="morning-actions">
          <button :disabled="analyses['*'] && analyses['*'].busy" class="ceo-btn" type="button" @click="analyze(null)">
            <CeoIcon :size="16" name="analysis" />
            {{ analyses["*"] && analyses["*"].result && !analyses["*"].open ? "نمایش تحلیل کل شرکت" : analyses["*"] && analyses["*"].open && analyses["*"].result ? "تحلیل دوباره‌ی کل شرکت" : "تحلیل هوشمند کل شرکت" }}
          </button>
          <button class="ceo-btn ghost" type="button" @click="drawerOpen = true">
            <CeoIcon :size="16" name="assistant" />
            پرسش از دستیار
          </button>
        </div>
        <CeoAnalysisBox v-if="analyses['*'] && analyses['*'].open" :busy="analyses['*'].busy" :error="analyses['*'].error" :result="analyses['*'].result" :started-at="analyses['*'].startedAt" @close="closeAnalysis('*')" @retry="analyze(null)" />
      </div>
      <div v-if="canBrief" ref="briefBox" :class="{ open: briefOpen }" class="morning-brief">
        <BriefCard />
        <button v-if="briefLong && !briefOpen" class="brief-more" type="button" @click="briefOpen = true">نمایش کامل گزارش</button>
      </div>
    </section>
      </div>
    </div>

    <div class="ceo-page ceo-body">

    <!-- what happened when the overview could not be read -->
    <div v-if="error && !overview" class="ceo-error" role="alert">
      <CeoIcon :size="22" name="danger" />
      <div>
        <strong>داشبورد خوانده نشد.</strong>
        <p>{{ error }}</p>
        <p class="ceo-error-hint">گزارش روزانه و دستیار جدا از داشبورد کار می‌کنند. اگر مشکل ادامه داشت، اتصال به سرور شرکت را بررسی کنید.</p>
      </div>
      <button class="ceo-btn" type="button" @click="load()">تلاش دوباره</button>
    </div>

    <!-- loading: the shape of the page -->
    <template v-else-if="!overview">
      <div aria-hidden="true" class="skel-pulse"><span v-for="i in 9" :key="i" class="ceo-skel" /></div>
      <div aria-hidden="true" class="skel-kpis"><span v-for="i in 8" :key="i" class="skel-kpi"><span class="ceo-skel" style="width: 50%" /><span class="ceo-skel tall" style="width: 70%" /><span class="ceo-skel" style="width: 60%" /></span></div>
      <div aria-hidden="true" class="skel-sections"><span v-for="i in 4" :key="i" class="skel-section"><span class="ceo-skel" style="width: 30%" /><span class="ceo-skel block" /></span></div>
    </template>

    <template v-else>
      <div :class="{ refreshing: loading }" class="ceo-data">
        <CeoPulse :class="{ compact: size === 'm' || size === 's' }" :lamp-test="lampTest" :pulse="overview.pulse" class="ceo-pulse" @go="goToSection" />
        <div id="ceo-headline" class="kpi-head">
          <h2 class="kpi-head-title">شاخص‌های اصلی</h2>
          <button
            :aria-label="reading === 'headline' ? 'توقف خواندن' : 'خلاصه‌ی صوتی شاخص‌های اصلی'"
            :aria-pressed="reading === 'headline'"
            :data-tip="reading === 'headline' ? 'توقف خواندن' : 'بخوان: خلاصه‌ی صوتی'"
            class="ceo-icon-btn ceo-read"
            type="button"
            @click="readAloud('headline')">
            <CeoIcon :name="reading === 'headline' ? 'stop' : 'speaker'" :size="17" />
          </button>
        </div>
        <CeoKpiCluster :class="{ narrow: size === 'm' || size === 's' }" :kpis="overview.headline" class="ceo-kpis" @open="openKpi" />

        <div class="ceo-main">
          <div class="ceo-sections">
            <CeoSectionPanel
              v-for="s in overview.sections"
              :key="s.key"
              :analysis="analyses[s.key] || null"
              :reading="reading === 'section:' + s.key"
              :section="s"
              :status="pulseOf(s.key)"
              :theme="theme"
              @analyze="analyze(s)"
              @close-analysis="closeAnalysis(s.key)"
              @drill="openSectionDrill(s, $event)"
              @read="readAloud('section', s.key)" />
          </div>
          <CeoSideColumn
            :alerts="overview.alerts"
            :layout="size === 'xl' ? 'column' : size === 's' ? 'stack' : 'band'"
            :notes-count="notesCount"
            :quick-links="quickLinks"
            :reading="reading === 'alerts'"
            :snapshot-busy="snapshotBusy"
            class="ceo-side"
            @catalogue="openCatalogue"
            @notes="openDrill('ai.notes_new', [{ key: 'ai.notes_new', title: drillTitle('ai.notes_new', 'یادداشت‌های تازه‌ی هوش مصنوعی') }, { key: 'ai.seat_runs', title: drillTitle('ai.seat_runs', 'اجراهای صندلی‌های هوش مصنوعی') }])"
            @read-alerts="readAloud('alerts')"
            @snapshot="snapshot" />
        </div>
      </div>
    </template>

    </div>

    <CeoVoiceCapsule v-if="!drawerOpen" :voice="voice" @open-drawer="drawerOpen = true" />
    <CeoAssistantDrawer
      ref="drawerRef"
      :action-runner="runActions"
      :beside="beside"
      :open="drawerOpen"
      :suggestions="suggestions"
      :ui-context="uiContext"
      :voice="voice"
      :voice-commands="voiceCommands"
      @close="drawerOpen = false" />
    <CeoDrillPanel
      :drill-key="drill.key"
      :filter-text="paramText(drill.params, drill.key)"
      :offset="panelOffset"
      :open="drill.open"
      :params="drill.params"
      :related="drill.related"
      @ask="askAssistant"
      @change="
        drill.key = $event;
        drill.params = null;
      "
      @clear-params="drill.params = null"
      @close="drill.open = false" />
    <CeoCatalogue :drills="drills" :error="drillsError" :loading="drillsLoading" :offset="panelOffset" :open="catalogueOpen" @close="catalogueOpen = false" @pick="openDrill($event)" @retry="loadDrills" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss">
/* ------------------------------------------------------------------ tokens (light default, dark selected; print forces light)
   rhythm: an 8-point scale (4 for tight inner steps); radius: 16 cards, 10 inner, 999 pills; the annunciator frame 18 */
.ceo {
  --ceo-surface: #f6f7f9;
  --ceo-card: #ffffff;
  --ceo-card-hover: #f7f9fb;
  /* the two fills of inner cells (they alternate) */
  --ceo-cell: #f8fafb;
  --ceo-cell-alt: #f2f5f8;
  /* popovers: tooltips and the chart tip */
  --ceo-pop: #ffffff;
  --ceo-ink: #14202b;
  --ceo-muted: #5b6877;
  --ceo-faint: #8794a2;
  --ceo-line: #e3e8ee;
  --ceo-line-soft: #edf0f4;
  --ceo-line-strong: #d3dae2;
  --ceo-primary: #235f94;
  --ceo-primary-soft: #eaf1f8;
  --ceo-primary-line: #c6d8ea;
  --ceo-primary-btn: #235f94;
  --ceo-on-primary: #ffffff;
  --ceo-brass: #c2722b;
  --ceo-brass-ink: #9a5517;
  --ceo-ok: #1f7a5a;
  --ceo-ok-soft: #e7f4ee;
  --ceo-ok-line: #bfe0d1;
  --ceo-warn: #b9791a;
  --ceo-warn-ink: #8a5a0f;
  --ceo-warn-soft: #fbf3e2;
  --ceo-warn-line: #ecd5a8;
  --ceo-danger: #b4382f;
  --ceo-danger-ink: #9a2f27;
  --ceo-danger-soft: #fcecea;
  --ceo-danger-line: #f0c6c1;
  --ceo-info: #2f6fa8;
  --ceo-info-soft: #e8f0f8;
  --ceo-neutral-soft: #eef1f4;
  --ceo-focus: #2f6fa8;
  --ceo-focus-ring: rgba(35, 95, 148, 0.2);
  --ceo-shadow: 0 1px 2px rgba(16, 24, 40, 0.04), 0 12px 32px -24px rgba(16, 24, 40, 0.18);
  --ceo-shadow-hover: 0 1px 2px rgba(16, 24, 40, 0.05), 0 16px 32px -20px rgba(16, 24, 40, 0.26);
  --ceo-shadow-float: 0 2px 6px rgba(16, 24, 40, 0.06), 0 18px 40px -18px rgba(16, 24, 40, 0.3);
  --ceo-btn-shadow: 0 1px 2px rgba(16, 24, 40, 0.08);
  /* the engineering grid of the header band */
  --ceo-grid-line: rgba(20, 52, 84, 0.04);
  /* the annunciator: a graphite instrument in both themes; only lit windows glow */
  --ceo-bezel: #1a232d;
  --ceo-bezel-inset: inset 0 2px 6px rgba(0, 0, 0, 0.45), inset 0 0 0 1px rgba(255, 255, 255, 0.05);
  --ceo-tile: #242f3a;
  --ceo-tile-hover: #2a3642;
  --ceo-tile-edge: rgba(255, 255, 255, 0.06);
  --ceo-tile-ink: #e9eef3;
  --ceo-tile-muted: #a3b0bd;
  --ceo-glow-ok: #4cc79a;
  --ceo-glow-warn: #f3b648;
  --ceo-glow-danger: #ff6f61;
  --ceo-glow-test: #fff1c9;
  --ceo-lit-warn-fill: rgba(243, 182, 72, 0.16);
  --ceo-lit-warn-edge: rgba(243, 182, 72, 0.42);
  --ceo-lit-warn-glow: rgba(243, 182, 72, 0.2);
  --ceo-lit-danger-fill: rgba(255, 111, 97, 0.16);
  --ceo-lit-danger-edge: rgba(255, 111, 97, 0.45);
  --ceo-lit-danger-glow: rgba(255, 111, 97, 0.22);
  --ceo-lit-test-glow: rgba(255, 241, 201, 0.45);
  --ceo-focus-on-bezel: #9cc3e6;
  --ceo-spark-line: #235f94;
  --ceo-tip-bg: #14202b;
  --ceo-tip-ink: #f4f6f8;
  --ceo-scrim: rgba(15, 24, 32, 0.32);
  --ceo-skel: #e8ecf0;
  /* the assistant panel and the brief card (components/ai) read these */
  --ai-q-bg: var(--ceo-primary-soft);
  --ai-a-bg: var(--ceo-card);
  --ai-line: var(--ceo-line);
  --ai-muted: var(--ceo-muted);
  --ai-faint: var(--ceo-faint);
  --ai-bad-edge: var(--ceo-danger);
  --ai-bad-bg: var(--ceo-danger-soft);
  --ai-bad-ink: var(--ceo-danger-ink);
  --ai-chip-edge: var(--ceo-primary-line);
  --ai-chip-bg: var(--ceo-primary-soft);
  --ai-chip-ink: var(--ceo-primary);
  --ai-off-bg: var(--ceo-surface);
  /* voice mode: waveform, state marks, captions */
  --ai-wave: var(--ceo-primary);
  --ai-accent: var(--ceo-primary);
  --ai-accent-ring: rgba(35, 95, 148, 0.35);
  --ai-live: var(--ceo-danger);
  --ai-live-ring: rgba(180, 56, 47, 0.4);
  --ai-ok: var(--ceo-ok);
  --ai-voice-bg: var(--ceo-surface);
}
.ceo[data-ceo-theme="dark"] {
  --ceo-surface: #10171f;
  --ceo-card: #171f29;
  --ceo-card-hover: #1b2430;
  --ceo-cell: #1a232e;
  --ceo-cell-alt: #1d2733;
  --ceo-pop: #1f2935;
  --ceo-ink: #e6ecf2;
  --ceo-muted: #93a1b1;
  --ceo-faint: #6f7e8e;
  --ceo-line: #253140;
  --ceo-line-soft: #1f2a36;
  --ceo-line-strong: #314051;
  --ceo-primary: #7fb0db;
  --ceo-primary-soft: #1b2c3e;
  --ceo-primary-line: #2d4a66;
  --ceo-primary-btn: #2f6ca8;
  --ceo-on-primary: #ffffff;
  --ceo-brass: #e3a061;
  --ceo-brass-ink: #e3a061;
  --ceo-ok: #62c9a0;
  --ceo-ok-soft: #14302a;
  --ceo-ok-line: #1f4a3d;
  --ceo-warn: #eab863;
  --ceo-warn-ink: #eab863;
  --ceo-warn-soft: #33291a;
  --ceo-warn-line: #574423;
  --ceo-danger: #f4907f;
  --ceo-danger-ink: #f4907f;
  --ceo-danger-soft: #3a2225;
  --ceo-danger-line: #5e3333;
  --ceo-info: #86b6e0;
  --ceo-info-soft: #1a2b3d;
  --ceo-neutral-soft: #1f2935;
  --ceo-focus: #9cc3e6;
  --ceo-focus-ring: rgba(127, 176, 219, 0.28);
  --ceo-shadow: 0 1px 2px rgba(0, 0, 0, 0.32), 0 12px 32px -24px rgba(0, 0, 0, 0.7);
  --ceo-shadow-hover: 0 1px 2px rgba(0, 0, 0, 0.36), 0 16px 32px -20px rgba(0, 0, 0, 0.8);
  --ceo-shadow-float: 0 2px 6px rgba(0, 0, 0, 0.35), 0 18px 40px -18px rgba(0, 0, 0, 0.75);
  --ceo-btn-shadow: 0 1px 2px rgba(0, 0, 0, 0.3);
  --ceo-grid-line: rgba(170, 190, 210, 0.045);
  --ceo-bezel: #0b1117;
  --ceo-bezel-inset: inset 0 2px 6px rgba(0, 0, 0, 0.6), inset 0 0 0 1px rgba(255, 255, 255, 0.04);
  --ceo-tile: #19222c;
  --ceo-tile-hover: #1f2934;
  --ceo-tile-edge: rgba(255, 255, 255, 0.05);
  --ceo-glow-ok: #58d4a6;
  --ceo-glow-warn: #ffc25a;
  --ceo-glow-danger: #ff7e70;
  --ceo-lit-warn-fill: rgba(255, 194, 90, 0.18);
  --ceo-lit-warn-edge: rgba(255, 194, 90, 0.45);
  --ceo-lit-warn-glow: rgba(255, 194, 90, 0.24);
  --ceo-lit-danger-fill: rgba(255, 126, 112, 0.18);
  --ceo-lit-danger-edge: rgba(255, 126, 112, 0.48);
  --ceo-lit-danger-glow: rgba(255, 126, 112, 0.26);
  --ceo-spark-line: #7fb0db;
  --ceo-tip-bg: #e6ecf2;
  --ceo-tip-ink: #14202b;
  --ceo-scrim: rgba(0, 0, 0, 0.5);
  --ceo-skel: #1e2833;
  --ai-accent-ring: rgba(127, 176, 219, 0.35);
  --ai-live-ring: rgba(244, 144, 127, 0.4);
}

/* ------------------------------------------------------------------ page: the band (header + morning line) full width, then the content column (max 1360, gutter 32) */
.ceo {
  position: relative;
  margin: -1rem;
  padding: 0 0 72px;
  /* nothing may scroll the page sideways (clip keeps the sticky side column working, unlike hidden) */
  overflow-x: clip;
  min-height: calc(100vh - 52px);
  background: var(--ceo-surface);
  color: var(--ceo-ink);
  font-family: IRANYekan, Tahoma, sans-serif;
  font-size: 14px;
  line-height: 22px;
  text-align: right;
}
.ceo.drawer-beside {
  padding-left: var(--ceo-drawer-w);
}
.ceo-page {
  max-width: 1424px;
  margin-inline: auto;
  padding-inline: 32px;
}
.ceo-band {
  position: relative;
  padding: 32px 0 40px;
  margin-bottom: 32px;
}
/* the engineering grid: 24px cells, hairlines at 4% — behind the morning line only, fading out towards the instruments */
.ceo-band::before {
  content: "";
  position: absolute;
  inset: 0;
  background-image: linear-gradient(var(--ceo-grid-line) 1px, transparent 1px), linear-gradient(90deg, var(--ceo-grid-line) 1px, transparent 1px);
  background-size: 24px 24px;
  background-position: right -1px top -1px;
  -webkit-mask-image: linear-gradient(180deg, #000 0%, #000 55%, transparent 100%);
  mask-image: linear-gradient(180deg, #000 0%, #000 55%, transparent 100%);
  pointer-events: none;
}
.ceo-band > .ceo-page {
  position: relative;
}
@media (max-width: 991.98px) {
  .ceo-page {
    padding-inline: 24px;
  }
}
@media (max-width: 575.98px) {
  .ceo {
    margin: -1rem 0;
    padding-bottom: 96px;
  }
  .ceo-page {
    padding-inline: 16px;
  }
  .ceo-band {
    padding: 24px 0 32px;
    margin-bottom: 24px;
  }
}
/* the app's global rules give every span / p / cell a colour, a line height and a 200ms transition on everything: inside the page, inherit and move only on purpose */
.ceo :where(span, p, th, td, h1, h2, h3, h4, h5, h6, div, label) {
  color: inherit;
  line-height: inherit;
}
.ceo,
.ceo * {
  transition: none;
}

/* ------------------------------------------------------------------ shared controls (hover: a 120ms lift on things that act) */
.ceo .ceo-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  height: 36px;
  padding: 0 16px;
  border: 1px solid var(--ceo-primary-btn);
  border-radius: 10px;
  background: var(--ceo-primary-btn);
  color: var(--ceo-on-primary);
  font: inherit;
  font-size: 13px;
  font-weight: 500;
  line-height: 20px;
  white-space: nowrap;
  cursor: pointer;
  text-decoration: none;
  box-shadow: var(--ceo-btn-shadow);
  transition: transform 120ms ease, box-shadow 120ms ease, background-color 120ms ease, border-color 120ms ease;
}
.ceo .ceo-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: var(--ceo-shadow-hover);
}
.ceo .ceo-btn:active:not(:disabled) {
  transform: none;
}
.ceo .ceo-btn.ghost {
  background: var(--ceo-card);
  border-color: var(--ceo-line-strong);
  color: var(--ceo-ink);
  font-weight: 400;
}
.ceo .ceo-btn.ghost:hover:not(:disabled) {
  border-color: var(--ceo-faint);
}
.ceo .ceo-btn.soft {
  background: var(--ceo-primary-soft);
  border-color: var(--ceo-primary-line);
  color: var(--ceo-primary);
  box-shadow: none;
}
.ceo .ceo-btn.small {
  height: 32px;
  padding: 0 12px;
  font-size: 13px;
}
.ceo .ceo-btn:disabled {
  opacity: 0.55;
  cursor: default;
  box-shadow: none;
}
.ceo .ceo-icon-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  border: 1px solid transparent;
  border-radius: 10px;
  background: transparent;
  color: var(--ceo-muted);
  cursor: pointer;
  transition: transform 120ms ease, background-color 120ms ease, border-color 120ms ease, color 120ms ease;
}
.ceo .ceo-icon-btn.small {
  width: 32px;
  height: 32px;
}
.ceo .ceo-icon-btn:hover:not(:disabled) {
  background: var(--ceo-card);
  border-color: var(--ceo-line);
  color: var(--ceo-ink);
  transform: translateY(-1px);
}
.ceo .ceo-icon-btn[aria-pressed="true"],
.ceo .ceo-icon-btn.on {
  background: var(--ceo-primary-soft);
  border-color: var(--ceo-primary-line);
  color: var(--ceo-primary);
}
.ceo .ceo-icon-btn:disabled {
  opacity: 0.5;
  cursor: default;
}
.ceo .ceo-tool {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  height: 28px;
  padding: 0 8px;
  border: 1px solid transparent;
  border-radius: 8px;
  background: transparent;
  color: var(--ceo-muted);
  font: inherit;
  font-size: 12px;
  line-height: 18px;
  cursor: pointer;
}
.ceo .ceo-tool:hover,
.ceo .ceo-tool[aria-pressed="true"] {
  border-color: var(--ceo-line);
  color: var(--ceo-ink);
  background: var(--ceo-card-hover);
}
.ceo .ceo-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 4px 12px;
  border: 1px solid var(--ai-chip-edge);
  border-radius: 999px;
  background: var(--ceo-primary-soft);
  color: var(--ceo-primary);
  font: inherit;
  font-size: 12px;
  line-height: 20px;
  text-align: right;
  cursor: pointer;
  transition: transform 120ms ease, border-color 120ms ease;
}
.ceo .ceo-chip:hover:not(:disabled) {
  border-color: var(--ceo-primary);
  transform: translateY(-1px);
}
.ceo .ceo-chip.active {
  background: var(--ceo-primary-btn);
  border-color: var(--ceo-primary-btn);
  color: var(--ceo-on-primary);
}
.ceo .ceo-chip:disabled {
  border-color: var(--ceo-line);
  background: var(--ceo-surface);
  color: var(--ceo-faint);
  cursor: default;
}
.ceo .ceo-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 1px 10px;
  border: 1px solid transparent;
  border-radius: 999px;
  font-size: 12px;
  line-height: 20px;
  white-space: nowrap;
}
.ceo .ceo-badge.ok {
  background: var(--ceo-ok-soft);
  border-color: var(--ceo-ok-line);
  color: var(--ceo-ok);
}
.ceo .ceo-badge.bad {
  background: var(--ceo-danger-soft);
  border-color: var(--ceo-danger-line);
  color: var(--ceo-danger-ink);
}
.ceo .ceo-badge.warn {
  background: var(--ceo-warn-soft);
  border-color: var(--ceo-warn-line);
  color: var(--ceo-warn-ink);
}
.ceo :focus-visible {
  outline: 2px solid var(--ceo-focus);
  outline-offset: 2px;
}
.ceo .ceo-skel {
  display: block;
  height: 14px;
  border-radius: 6px;
  background: var(--ceo-skel);
}
.ceo .spinning {
  animation: ceo-spin 0.9s linear infinite;
}
@keyframes ceo-spin {
  to {
    transform: rotate(360deg);
  }
}
/* tooltips of the icon buttons (data-tip): the name of the action, above the button; it exists only while shown (an invisible one would still widen the page) */
.ceo [data-tip] {
  position: relative;
}
.ceo [data-tip]:hover::after,
.ceo [data-tip]:focus-visible::after {
  content: attr(data-tip);
  position: absolute;
  bottom: calc(100% + 8px);
  left: 50%;
  z-index: 30;
  padding: 4px 10px;
  border: 1px solid var(--ceo-line);
  border-radius: 8px;
  background: var(--ceo-pop);
  color: var(--ceo-ink);
  box-shadow: var(--ceo-shadow-float);
  font-size: 12px;
  line-height: 20px;
  font-weight: 400;
  white-space: nowrap;
  pointer-events: none;
  transform: translateX(-50%);
  animation: ceo-tip-in 120ms ease both;
}
@keyframes ceo-tip-in {
  from {
    opacity: 0;
    transform: translate(-50%, 4px);
  }
}
.ceo .card {
  background: var(--ceo-card);
  border: 1px solid var(--ceo-line);
  border-radius: 16px;
  box-shadow: var(--ceo-shadow);
}

/* the existing AI components inside the page take the page's tokens (Bootstrap colours replaced) */
.ceo .inv-title {
  font-size: 16px;
  line-height: 24px;
  font-weight: 700;
  color: var(--ceo-ink);
}
.ceo .inv-muted {
  color: var(--ceo-muted);
  font-size: 12px;
}
.ceo .inv-badge {
  border-radius: 999px;
  padding: 1px 10px;
  font-size: 12px;
  line-height: 20px;
}
.ceo .inv-badge.ok {
  background: var(--ceo-ok-soft);
  color: var(--ceo-ok);
}
.ceo .inv-badge.bad {
  background: var(--ceo-danger-soft);
  color: var(--ceo-danger-ink);
}
.ceo .inv-badge.warn {
  background: var(--ceo-warn-soft);
  color: var(--ceo-warn-ink);
}
.ceo .ai-panel .btn,
.ceo .brief .btn {
  border-radius: 10px;
  font-size: 12px;
  line-height: 20px;
  padding: 4px 12px;
}
.ceo .ai-panel .btn-primary {
  background: var(--ceo-primary-btn);
  border-color: var(--ceo-primary-btn);
  color: var(--ceo-on-primary);
}
.ceo .ai-panel .btn-outline-primary,
.ceo .brief .btn-outline-primary {
  border-color: var(--ceo-primary-line);
  background: var(--ceo-primary-soft);
  color: var(--ceo-primary);
}
.ceo .ai-panel .btn-outline-primary:hover,
.ceo .brief .btn-outline-primary:hover {
  border-color: var(--ceo-primary);
}
.ceo .ai-panel .btn-outline-secondary,
.ceo .brief .btn-outline-secondary {
  border-color: var(--ceo-line-strong);
  color: var(--ceo-muted);
}
.ceo .ai-panel .btn-outline-secondary:hover:not(:disabled),
.ceo .brief .btn-outline-secondary:hover:not(:disabled) {
  background: var(--ceo-card-hover);
  color: var(--ceo-ink);
}
.ceo .ai-panel .form-control {
  background: var(--ceo-surface);
  border-color: var(--ceo-line);
  border-radius: 10px;
  color: var(--ceo-ink);
  font-size: 13px;
  line-height: 22px;
}
.ceo .ai-panel .form-control::placeholder {
  color: var(--ceo-faint);
}
.ceo .ai-panel .form-control:focus {
  border-color: var(--ceo-primary);
  box-shadow: 0 0 0 3px var(--ceo-focus-ring);
}
.ceo .ai-a,
.ceo .ai-q {
  border-radius: 10px;
}
.ceo .ai-text,
.ceo .brief-text {
  font-size: 14px;
  line-height: 26px;
  color: var(--ceo-ink);
}

/* ------------------------------------------------------------------ header */
.ceo-header {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  justify-content: space-between;
  gap: 16px 24px;
  margin-bottom: 32px;
}
.ceo-id {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.ceo-company {
  font-size: 13px;
  line-height: 20px;
  color: var(--ceo-muted);
}
.ceo-title {
  margin: 0;
  font-size: 22px;
  line-height: 32px;
  font-weight: 700;
  color: var(--ceo-ink);
}
.ceo-header-tools {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
}
.ceo-today {
  font-size: 14px;
  line-height: 22px;
  font-weight: 500;
}
.ceo-updated {
  font-size: 13px;
  line-height: 20px;
  color: var(--ceo-muted);
  padding-inline-start: 12px;
  border-inline-start: 1px solid var(--ceo-line-strong);
}
.ceo-header-tools .ceo-btn.ghost {
  margin-inline-start: 4px;
}

/* ------------------------------------------------------------------ morning line: the page's sentence, set large and light; the brief card beside it */
.ceo-morning {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(320px, 408px);
  gap: 40px;
  align-items: start;
}
.ceo-morning.no-brief {
  grid-template-columns: minmax(0, 1fr);
}
.morning-main {
  display: flex;
  flex-direction: column;
  gap: 24px;
  min-width: 0;
  padding-top: 8px;
}
.morning-text {
  margin: 0;
  max-width: 26em;
  font-size: 30px;
  line-height: 52px;
  font-weight: 300;
  color: var(--ceo-ink);
}
.morning-lead {
  color: var(--ceo-muted);
}
.morning-part {
  white-space: nowrap;
}
.morning-part.attention {
  font-weight: 500;
  text-decoration: underline;
  text-decoration-color: var(--ceo-brass);
  text-decoration-thickness: 2px;
  text-underline-offset: 12px;
}
.morning-skel {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 8px 0;
}
.morning-skel .ceo-skel {
  height: 24px;
}
.morning-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.morning-brief {
  min-width: 0;
  padding: 20px 24px;
  background: var(--ceo-card);
  border: 1px solid var(--ceo-line);
  border-radius: 16px;
  box-shadow: var(--ceo-shadow);
}
.morning-brief:not(.open) .brief-text {
  max-height: 128px;
  overflow: hidden;
  -webkit-mask-image: linear-gradient(180deg, #000 60%, transparent);
  mask-image: linear-gradient(180deg, #000 60%, transparent);
}
.brief-more {
  display: block;
  margin-top: 8px;
  padding: 0;
  border: 0;
  background: none;
  color: var(--ceo-primary);
  font: inherit;
  font-size: 13px;
  line-height: 20px;
  font-weight: 500;
  cursor: pointer;
}
.brief-more:hover {
  text-decoration: underline;
  text-underline-offset: 4px;
}
.morning-brief .brief-text {
  font-size: 13px;
  line-height: 24px;
}
/* the brief card's own header (components/ai): the title on its own line, then the date, the badge and the two actions in one row */
.ceo .morning-brief .brief > .d-flex:first-child {
  flex-direction: column;
  align-items: flex-start !important;
  gap: 8px !important;
  margin-bottom: 12px !important;
}
.ceo .morning-brief .brief > .d-flex:first-child > .d-flex {
  flex-wrap: wrap;
  width: 100%;
}
/* the brief's «بازخوانی» sits on the title line, at its end */
.morning-brief {
  position: relative;
}
.ceo .morning-brief .brief > .d-flex:first-child > .d-flex > .btn-outline-secondary {
  position: absolute;
  top: 18px;
  left: 20px;
}
.ceo .brief .d-flex {
  flex-wrap: wrap;
}
.ceo .brief .btn {
  white-space: nowrap;
}

/* ------------------------------------------------------------------ error and loading */
.ceo-error {
  display: flex;
  align-items: flex-start;
  gap: 16px;
  padding: 24px;
  border: 1px solid var(--ceo-line);
  border-radius: 16px;
  background: var(--ceo-card);
  box-shadow: inset -3px 0 0 var(--ceo-danger), var(--ceo-shadow);
  color: var(--ceo-danger);
}
.ceo-error > div {
  flex: 1;
  color: var(--ceo-ink);
}
.ceo-error strong {
  font-weight: 700;
}
.ceo-error p {
  margin: 4px 0 0;
}
.ceo-error-hint {
  color: var(--ceo-muted);
  font-size: 13px;
}
.skel-pulse {
  display: grid;
  grid-template-columns: repeat(9, 1fr);
  gap: 6px;
  padding: 8px;
  border-radius: 18px;
  background: var(--ceo-bezel);
  margin-bottom: 32px;
}
.skel-pulse .ceo-skel {
  height: 84px;
  border-radius: 10px;
  background: var(--ceo-tile);
}
.skel-kpis {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 1px;
  border: 1px solid var(--ceo-line);
  border-radius: 16px;
  overflow: hidden;
  background: var(--ceo-line-soft);
  margin-bottom: 40px;
}
.skel-kpi {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 20px 24px;
  background: var(--ceo-card);
  min-height: 176px;
}
.skel-kpi .tall {
  height: 32px;
}
.skel-sections {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 24px;
}
.skel-section {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 24px;
  border: 1px solid var(--ceo-line);
  border-radius: 16px;
  background: var(--ceo-card);
  box-shadow: var(--ceo-shadow);
}
.skel-section .block {
  height: 224px;
}

/* ------------------------------------------------------------------ data */
.ceo-data.refreshing {
  opacity: 0.6;
}
.ceo-pulse {
  margin-bottom: 40px;
}
.ceo-kpis {
  margin-bottom: 48px;
}
.ceo-main {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 24px;
  align-items: start;
}
.ceo-sections {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 24px;
  align-items: start;
  min-width: 0;
}
.ceo-side {
  order: -1;
}
/* xl: two columns of sections with the alerts column beside them, kept in view */
.ceo.w-xl .ceo-main {
  grid-template-columns: minmax(0, 1fr) 304px;
  gap: 24px;
}
.ceo.w-xl .ceo-sections {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}
.ceo.w-xl .ceo-side {
  order: 0;
  position: sticky;
  top: 76px;
  max-height: calc(100vh - 92px);
  overflow-y: auto;
  scrollbar-width: thin;
  /* room for the cards' shadows inside the scroll box */
  padding: 2px 2px 16px;
  margin: -2px -2px 0;
}
/* the morning line beside the brief while there is room */
.ceo.w-m .ceo-morning,
.ceo.w-s .ceo-morning {
  grid-template-columns: minmax(0, 1fr);
  gap: 24px;
}
.ceo.w-m .skel-sections,
.ceo.w-s .skel-sections,
.ceo.w-l .skel-sections {
  grid-template-columns: 1fr;
}

/* ------------------------------------------------------------------ the heading of the headline KPIs (with its «بخوان») */
.kpi-head {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 0 0 12px;
  scroll-margin-top: 76px;
}
.kpi-head-title {
  margin: 0;
  font-size: 16px;
  line-height: 24px;
  font-weight: 700;
  color: var(--ceo-ink);
}

/* ------------------------------------------------------------------ action-driven motion (slide-overs) */
.ceo .ceo-slide-enter-active,
.ceo .ceo-slide-leave-active {
  transition: transform 220ms cubic-bezier(0.2, 0.7, 0.2, 1);
}
.ceo .ceo-slide-enter-from,
.ceo .ceo-slide-leave-to {
  transform: translateX(-100%);
}
.ceo .ceo-scrim-enter-active,
.ceo .ceo-scrim-leave-active {
  transition: opacity 200ms ease;
}
.ceo .ceo-scrim-enter-from,
.ceo .ceo-scrim-leave-to {
  opacity: 0;
}
@media (prefers-reduced-motion: reduce) {
  .ceo .ceo-slide-enter-active,
  .ceo .ceo-slide-leave-active,
  .ceo .ceo-scrim-enter-active,
  .ceo .ceo-scrim-leave-active,
  .ceo .ceo-btn,
  .ceo .ceo-icon-btn,
  .ceo .ceo-chip {
    transition: none;
  }
  .ceo [data-tip]:hover::after,
  .ceo [data-tip]:focus-visible::after {
    animation: none;
  }
  .ceo .ceo-btn:hover:not(:disabled),
  .ceo .ceo-icon-btn:hover:not(:disabled),
  .ceo .ceo-chip:hover:not(:disabled) {
    transform: none;
  }
  .ceo .spinning {
    animation: none;
  }
}

/* ------------------------------------------------------------------ the window itself (the rest follows the page width classes above) */
@media (max-width: 991.98px) {
  .morning-text {
    font-size: 26px;
    line-height: 46px;
  }
}
@media (max-width: 640px) {
  .ceo-header {
    align-items: flex-start;
    margin-bottom: 24px;
  }
  .ceo-title {
    font-size: 20px;
    line-height: 28px;
  }
  .ceo-updated {
    border: 0;
    padding: 0;
    width: 100%;
    order: 5;
  }
  .morning-text {
    font-size: 22px;
    line-height: 40px;
  }
  .morning-part.attention {
    text-underline-offset: 9px;
  }
  .skel-pulse {
    grid-template-columns: repeat(3, 1fr);
  }
  .skel-kpis {
    grid-template-columns: repeat(2, 1fr);
  }
  .ceo-kpis {
    margin-bottom: 32px;
  }
  .ceo-pulse {
    margin-bottom: 32px;
  }
}

/* ------------------------------------------------------------------ print: A4, right to left, B Zar, light, no drawer or buttons, no shadow or texture */
.ceo-print-head {
  display: none;
}
@media print {
  .ceo,
  .ceo[data-ceo-theme="dark"] {
    --ceo-surface: #ffffff;
    --ceo-card: #ffffff;
    --ceo-cell: #ffffff;
    --ceo-cell-alt: #f6f7f9;
    --ceo-ink: #000000;
    --ceo-muted: #333333;
    --ceo-line: #9aa5b1;
    --ceo-line-soft: #c9d0d8;
    --ceo-bezel: #ffffff;
    --ceo-tile: #ffffff;
    --ceo-tile-ink: #000000;
    --ceo-tile-muted: #333333;
    --ceo-tile-edge: #6b7785;
    --ceo-glow-warn: #b9791a;
    --ceo-glow-danger: #b4382f;
    --ceo-glow-ok: #1f7a5a;
    --ceo-primary: #235f94;
    --ceo-ok: #1f7a5a;
    --ceo-warn: #b9791a;
    --ceo-warn-ink: #8a5a0f;
    --ceo-danger: #b4382f;
    --ceo-danger-ink: #9a2f27;
    --ceo-spark-line: #235f94;
    --ceo-shadow: none;
    margin: 0 !important;
    padding: 0 !important;
    min-height: 0;
    background: #fff !important;
    font-family: "B Zar", IRANYekan, Tahoma, sans-serif;
    font-size: 13px;
  }
  .ceo * {
    font-family: "B Zar", IRANYekan, Tahoma, sans-serif !important;
    box-shadow: none !important;
  }
  .ceo-page {
    max-width: none;
    padding: 0 !important;
  }
  .ceo-band {
    padding: 0 !important;
    margin: 0 0 8px !important;
    border: 0 !important;
  }
  .ceo-band::before {
    display: none;
  }
  .ceo-print-head {
    display: flex;
    justify-content: space-between;
    align-items: flex-end;
    border-bottom: 2px solid #1f3a5f;
    padding-bottom: 6px;
    margin-bottom: 10px;
  }
  .ceo-print-company {
    font-size: 13px;
  }
  .ceo-print-title {
    margin: 0;
    font-size: 22px;
    font-weight: bold;
  }
  .ceo-print-meta {
    font-size: 12px;
    text-align: left;
  }
  .ceo-header,
  .ceo-read,
  .kpi-head,
  .assist-root,
  .drill-root,
  .cat-root,
  .morning-actions,
  .morning-brief,
  .sec-actions,
  .chart-tools,
  .ceo-side .side-block:not(:first-child),
  .ceo-error button,
  .analysis .ceo-icon-btn {
    display: none !important;
  }
  /* report.scss hides every nav and button when printing: the pulse and the KPI cells are buttons inside a nav, they stay on paper */
  .ceo .annunciator {
    display: block !important;
  }
  .ceo .ann-window,
  .ceo .kpi-cell,
  .ceo .sec-kpi {
    display: flex !important;
  }
  .alert-go,
  .alert-fold,
  .chart-tip {
    display: none !important;
  }
  /* the alerts are compact on paper so the first page holds them with the headline */
  .ceo .side-block {
    padding: 8px 12px !important;
  }
  .ceo .alert-item {
    padding: 2px 0 !important;
    font-size: 11.5px !important;
    line-height: 17px !important;
  }
  .ceo .alert-group + .alert-group {
    margin-top: 4px !important;
  }
  .ceo .alert-group-title {
    margin-bottom: 0 !important;
  }
  .ceo-morning {
    display: block;
    margin-bottom: 8px;
  }
  .morning-text {
    font-size: 17px;
    line-height: 30px;
    max-width: none;
    font-weight: 400;
  }
  .annunciator {
    padding: 0 !important;
    border: 1px solid #6b7785;
  }
  .ann-window {
    min-height: 0 !important;
    padding: 8px 4px 6px !important;
  }
  .ceo-pulse,
  .ceo-kpis {
    margin-bottom: 10px !important;
  }
  .kpi-cell {
    padding: 8px 10px !important;
  }
  .kpi-num {
    font-size: 18px !important;
    line-height: 28px !important;
    font-weight: 400 !important;
  }
  .ceo .kpi-value,
  .ceo .kpi-delta-row {
    margin-top: 2px !important;
    min-height: 0 !important;
  }
  .ceo .kpi-spark {
    padding-top: 4px !important;
    min-height: 0 !important;
  }
  /* the alerts take the full width on paper (the other blocks of the column are not printed) */
  .ceo .side {
    display: block !important;
  }
  .ceo-main {
    display: flex !important;
    flex-direction: column;
    align-items: stretch !important;
    gap: 10px;
  }
  .ceo-side {
    position: static !important;
    max-height: none !important;
    overflow: visible !important;
    order: -1;
    display: block !important;
    padding: 0 !important;
    margin: 0 !important;
  }
  /* two sections side by side on A4 (each about as wide as on a laptop screen) */
  .ceo .ceo-sections {
    display: grid !important;
    grid-template-columns: repeat(2, minmax(0, 1fr)) !important;
    gap: 8px !important;
    align-items: start;
  }
  .ceo-section {
    break-inside: avoid;
    page-break-inside: avoid;
    padding: 10px 12px !important;
    gap: 10px !important;
    border-radius: 8px !important;
  }
  .morning-brief:not(.open) .brief-text {
    max-height: none;
  }
  .ceo .chart-canvas {
    height: 150px !important;
  }
  .ceo .chart-canvas.is-donut {
    max-width: 170px;
  }
  .ceo .sec-charts {
    gap: 12px !important;
  }
  .chart-canvas canvas {
    max-width: 100% !important;
  }
  .ceo-data.refreshing {
    opacity: 1;
  }
}
</style>
