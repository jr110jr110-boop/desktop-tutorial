import moment from "moment-jalaali";
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import type { Router } from "vue-router";
import type { CeoAlert, CeoAnalysis, CeoKpi, CeoOverview, CeoSeriesRole, CeoStatus, CeoUnit } from "@/api/CeoAPIs";
import { frontLink } from "@/composables/ai";
import { hasRole } from "@/composables/roles";

// Names and helpers of the CEO executive dashboard (release D1). Everything a person reads is Persian with Persian digits (the IRANYekan FaNum font draws them, and the numbers
// formatted here are written with Persian digits and separators too, so a copied value stays Persian). The API JSON stays ISO and camelCase.

/** The company name in the header. A text placeholder until the company settings carry the name. */
export const COMPANY_NAME = "شرکت";
/** Shown instead of a payroll-protected value (the API sends value null with hidden=true). */
export const PROTECTED_TEXT = "محرمانه (حقوق)";

// ---------------------------------------------------------------- numbers
const faFormatters: Record<number, Intl.NumberFormat> = {};
const faFmt = (digits: number) => (faFormatters[digits] ||= new Intl.NumberFormat("fa-IR", { maximumFractionDigits: digits, minimumFractionDigits: 0 }));
/** A number with Persian digits and separators (۱۲٬۳۴۵٫۶). */
export function faNum(value: number | null | undefined, digits = 1): string {
  if (value === null || value === undefined || !Number.isFinite(Number(value))) return "—";
  return faFmt(digits).format(Number(value));
}
/** Latin digits of any text to Persian digits (codes and Shamsi dates written by the server with Latin digits). */
export function faDigits(text: string | number | null | undefined): string {
  return String(text ?? "").replace(/[0-9]/g, (d) => "۰۱۲۳۴۵۶۷۸۹"[Number(d)]);
}
/** Persian or Arabic digits to Latin, separators removed (for sorting and parsing). */
export function latinNumber(text: string): string {
  return String(text ?? "")
    .replace(/[۰-۹]/g, (d) => String("۰۱۲۳۴۵۶۷۸۹".indexOf(d)))
    .replace(/[٠-٩]/g, (d) => String("٠١٢٣٤٥٦٧٨٩".indexOf(d)))
    .replace(/[٬,]/g, "")
    .replace(/٫/g, ".");
}

const BILLION = 1e9;
const MILLION = 1e6;
/** Full rial value with Persian thousands separators: «۴۸٬۲۰۰٬۰۰۰٬۰۰۰ ریال». */
export const rialFull = (value: number | null | undefined) => (value === null || value === undefined ? "—" : faNum(Math.round(Number(value)), 0) + " ریال");
/** Large rials short: «۴۸٫۲ میلیارد ریال» (one decimal below a thousand billion, whole billions above), «۳۵۰ میلیون ریال», small values in full. */
export function rialShort(value: number | null | undefined, withUnit = true): string {
  if (value === null || value === undefined || !Number.isFinite(Number(value))) return "—";
  const v = Number(value);
  const a = Math.abs(v);
  const unit = withUnit ? " ریال" : "";
  if (a >= BILLION) return faNum(v / BILLION, a >= 1000 * BILLION ? 0 : 1) + " میلیارد" + unit;
  if (a >= MILLION) return faNum(v / MILLION, 1) + " میلیون" + unit;
  return faNum(v, 0) + unit;
}

/** Unit words of a KPI or a chart. */
export const unitTitles: Record<CeoUnit, string> = { rial: "ریال", count: "تعداد", percent: "درصد", days: "روز", hours: "ساعت" };

/** A value of a unit for a sentence or a cell (rial short; the title attribute carries the full rial value). */
export function unitValue(unit: CeoUnit | string, value: number | null | undefined, short = true): string {
  if (value === null || value === undefined || !Number.isFinite(Number(value))) return "—";
  switch (unit) {
    case "rial":
      return short ? rialShort(value) : rialFull(value);
    case "percent":
      return faNum(value, 1) + "٪";
    case "days":
      return faNum(value, 0) + " روز";
    case "hours":
      return faNum(value, 1) + " ساعت";
    default:
      return faNum(value, 1);
  }
}

/**
 * A display value split into the number and the words after it, so the number can be set large and the unit small: «۴۸٫۲ میلیارد ریال» -> { num: "۴۸٫۲", unit: "میلیارد ریال" },
 * «۴٫۳ از ۵» -> { num: "۴٫۳", unit: "از ۵" }, «۳۱٫۵٪» -> { num: "۳۱٫۵٪", unit: "" }. A text that does not start with a number comes back whole as the unit.
 */
export function splitValue(text: string | null | undefined): { num: string; unit: string } {
  const m = /^\s*([+\-−]?[0-9۰-۹٫٬.,]+\s*٪?)\s*(.*)$/.exec(String(text ?? ""));
  if (!m) return { num: "", unit: String(text ?? "") };
  return { num: m[1].trim(), unit: m[2].trim() };
}

/** The display value of a KPI: the short text of the server for large rials, otherwise its formatted value text; the payroll mask shows «محرمانه (حقوق)». */
export const kpiValue = (k: CeoKpi) => (k.hidden ? PROTECTED_TEXT : k.shortText || k.valueText || unitValue(k.unit, k.value));
/** The full value for the title attribute (hover). */
export const kpiFullValue = (k: CeoKpi) => (k.hidden ? PROTECTED_TEXT : k.unit === "rial" && k.value !== null ? rialFull(k.value) : k.valueText || unitValue(k.unit, k.value));

/** Direction of a delta (the arrow) and its meaning (the colour comes from deltaGood: true favourable, false unfavourable, null neither). */
export function deltaOf(k: Pick<CeoKpi, "delta" | "deltaText" | "deltaGood" | "unit" | "hidden">): { arrow: string; text: string; tone: "good" | "bad" | "flat"; label: string } | null {
  if (k.hidden || k.delta === null || k.delta === undefined) return null;
  const d = Number(k.delta);
  const arrow = d > 0 ? "▲" : d < 0 ? "▼" : "■";
  const text = (k.deltaText || unitValue(k.unit, Math.abs(d))).replace(/^[+−-]\s*/, "");
  const tone = k.deltaGood === true ? "good" : k.deltaGood === false ? "bad" : "flat";
  const word = d > 0 ? "افزایش" : d < 0 ? "کاهش" : "بدون تغییر";
  const meaning = tone === "good" ? "، مطلوب" : tone === "bad" ? "، نامطلوب" : "";
  return { arrow, text: d === 0 ? "بدون تغییر" : text, tone, label: `${word} ${d === 0 ? "" : text} نسبت به ماه قبل${meaning}` };
}

// ---------------------------------------------------------------- statuses
export const statusTitles: Record<CeoStatus, string> = { ok: "عادی", warn: "هشدار", danger: "بحرانی", neutral: "بدون ارزیابی" };
export const severityTitles: Record<CeoAlert["severity"], string> = { danger: "بحرانی", warning: "هشدار", info: "اطلاع" };
/** Severity of an alert as a status. */
export const statusOfSeverity = (s: string): CeoStatus | "info" => (s === "danger" ? "danger" : s === "warning" ? "warn" : "info");
/**
 * A status cell of a drill table: the server writes ok / warn / danger / neutral (or info), sometimes a Persian word; anything else is shown as text in a neutral chip.
 */
export function statusOfCell(value: any): { status: CeoStatus | "info"; text: string } {
  const v = String(value ?? "").trim();
  const map: Record<string, CeoStatus | "info"> = {
    ok: "ok", green: "ok", success: "ok", عادی: "ok", سبز: "ok",
    warn: "warn", warning: "warn", amber: "warn", yellow: "warn", هشدار: "warn", زرد: "warn",
    danger: "danger", red: "danger", critical: "danger", bad: "danger", بحرانی: "danger", قرمز: "danger",
    info: "info", neutral: "neutral",
  };
  const s = map[v.toLowerCase()] ?? map[v];
  if (!s) return { status: "neutral", text: v || "—" };
  const words: Record<string, string> = { ok: "در برنامه", warn: "نیاز به توجه", danger: "بحرانی", neutral: "—", info: "اطلاع" };
  return { status: s, text: /[a-z]/i.test(v) ? words[s] : v };
}

// ---------------------------------------------------------------- dates
/** Today's Shamsi date with the weekday: «یکشنبه ۴ آبان ۱۴۰۵». */
export function todayText(): string {
  const m = moment();
  const weekdays = ["یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه", "شنبه"];
  return `${weekdays[m.day()]} ${faNum(m.jDate(), 0)} ${jMonths[m.jMonth()]} ${faDigits(m.jYear())}`;
}
export const jMonths = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];
/** «۳ آبان» of a Shamsi text "1405/08/03" (any digits). */
export function dayMonthOf(shamsi: string | null | undefined): string {
  const m = /(\d{4})\/(\d{1,2})\/(\d{1,2})/.exec(latinNumber(shamsi || ""));
  if (!m) return "";
  return `${faNum(Number(m[3]), 0)} ${jMonths[Number(m[2]) - 1] || ""}`.trim();
}
/** «همین حالا»، «۲ دقیقه پیش»، «۱ ساعت پیش» of an ISO date-time. */
export function agoText(iso: string | null | undefined, now = Date.now()): string {
  if (!iso) return "";
  const t = new Date(iso).getTime();
  if (!Number.isFinite(t)) return "";
  const minutes = Math.max(0, Math.floor((now - t) / 60000));
  if (minutes < 1) return "همین حالا";
  if (minutes < 60) return `${faNum(minutes, 0)} دقیقه پیش`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${faNum(hours, 0)} ساعت پیش`;
  return moment(iso).format("jYYYY/jMM/jDD HH:mm").replace(/\d/g, (d) => "۰۱۲۳۴۵۶۷۸۹"[Number(d)]);
}

// ---------------------------------------------------------------- the morning line
export interface MorningPart {
  text: string;
  /** the fragment needs attention: drawn with the brass underline */
  attention?: boolean;
}
/**
 * One sentence of the morning from the overview: the date, the red projects (projects section KPI projectsRed, else the danger count of the projects pulse), the cash (headline cash,
 * not when masked) and the critical alerts. «امروز ۳ آبان: ۲ پروژه قرمز، نقدینگی ۴۸٫۲ میلیارد ریال، ۵ هشدار بحرانی».
 */
export function morningLine(o: CeoOverview): { lead: string; parts: MorningPart[] } {
  const lead = `امروز ${dayMonthOf(o.asOfText) || dayMonthOf(moment(o.asOf).format("jYYYY/jMM/jDD"))}`;
  const parts: MorningPart[] = [];
  const projects = o.sections?.find((s) => s.key === "projects");
  const redKpi = projects?.kpis?.find((k) => k.key === "projectsRed");
  const red = redKpi && !redKpi.hidden && redKpi.value !== null ? Number(redKpi.value) : o.pulse?.find((p) => p.key === "projects")?.dangerCount;
  if (red !== undefined && red !== null) parts.push(red > 0 ? { text: `${faNum(red, 0)} پروژه قرمز`, attention: true } : { text: "هیچ پروژه‌ی قرمزی نیست" });
  const cash = o.headline?.find((k) => k.key === "cash");
  if (cash && !cash.hidden && cash.value !== null) parts.push({ text: `نقدینگی ${kpiValue(cash)}`, attention: cash.status === "danger" });
  const critical = (o.alerts || []).filter((a) => a.severity === "danger").length;
  parts.push(critical > 0 ? { text: `${faNum(critical, 0)} هشدار بحرانی`, attention: true } : { text: "بدون هشدار بحرانی" });
  return { lead, parts };
}

// ---------------------------------------------------------------- links
/** Some links of the API are written in the plural; the pages of this front end use these paths. */
const linkFix: Record<string, string> = { "/inventory/ncrs": "/inventory/ncr" };
/** A link of the API as a route of this front end that the signed-in role may open, or null (the chip or the button is then shown without a link). */
export function openableLink(router: Router, link: string | null | undefined): string | null {
  if (!link) return null;
  const fixed = linkFix[link] || link;
  const path = frontLink(router, fixed);
  if (!path) return null;
  const resolved = router.resolve(path);
  const roles = resolved.matched.map((r) => r.meta?.roles as string[] | undefined).filter(Boolean) as string[][];
  return roles.every((list) => hasRole(list)) ? path : null;
}

// ---------------------------------------------------------------- the assistant
/** Suggested questions of a section (the chips of the assistant drawer prefill the question; nothing is sent until the CEO sends it). */
export const suggestedQuestions: Record<string, string[]> = {
  finance: ["ریز مطالبات به تفکیک مشتری", "چک‌های سررسید ۳۰ روز آینده کدام‌اند؟"],
  sales: ["کدام پیشنهادها منتظر تایید من هستند؟", "دلایل باخت در ۱۲ ماه اخیر"],
  projects: ["چه پروژه‌هایی قرمزند و چرا؟", "جریمه‌ی تاخیر هر پروژه چقدر است؟"],
  production: ["سفارش‌های تولید عقب‌افتاده", "کدام مراکز کاری اضافه‌بار دارند؟"],
  quality: ["NCRهای باز عمده کدام‌اند؟", "اقدام‌های اصلاحی از مهلت گذشته"],
  inventory: ["کالاهای زیر حداقل که تولید را متوقف می‌کنند", "ارزش کالاهای راکد چقدر است؟"],
  procurement: ["ردیف‌های خرید با تاخیر تحویل", "تحویل به‌موقع تامین‌کنندگان اصلی"],
  service: ["درخواست‌های خارج از SLA", "هزینه‌ی گارانتی امسال به تفکیک ماه"],
  office: ["مصوبات جلسات از موعد گذشته", "نامه‌های منتظر اقدام من"],
};
/**
 * Voice commands shown as chips in the assistant drawer (voice mode, §7.3): a tap sends one at once and the answer is spoken, so the CEO learns what can be said.
 * Each maps to UI actions of §7.1 on the server (openDrill mf.orders_late; read alerts; analyze sales; scroll projects + openDrill pc.portfolio with the red filter).
 */
export const voiceCommands = ["باز کن سفارش‌های عقب‌افتاده تولید", "بخوان هشدارها", "تحلیل کن بخش فروش", "نشانم بده پروژه‌های قرمز"];

/** One AI analysis of the page (a section, or the whole company under the key «*»): kept for the visit only, never stored. */
export interface AnalysisState {
  busy: boolean;
  error: string;
  result: CeoAnalysis | null;
  startedAt: number;
  open: boolean;
}

/** The question that the drill panel prefills for its table. */
export const askAboutTable = (title: string) => `درباره‌ی جدول «${title}» توضیح بده: مهم‌ترین ردیف‌ها، تغییر نسبت به ماه قبل و ریسک‌ها کدام‌اند؟`;

// ---------------------------------------------------------------- theme (a per-viewer convenience: only the choice light / dark is stored in localStorage)
const THEME_KEY = "ceo.theme";
export type CeoTheme = "light" | "dark";
function readStored(): CeoTheme | null {
  try {
    const v = localStorage.getItem(THEME_KEY);
    return v === "light" || v === "dark" ? v : null;
  } catch {
    return null;
  }
}
function appTheme(): string {
  try {
    return localStorage.getItem("theme") || "";
  } catch {
    return "";
  }
}
/**
 * Theme of the CEO page: the stored choice (key «ceo.theme»), else the app theme (the moon of the top bar), else the system setting (prefers-color-scheme).
 * While the page is open the top bar and the menu follow it (html data-theme, as the top bar's own switch does); leaving the page puts back the app's saved theme.
 * The switch of the top bar is followed too: using it on this page becomes the page's choice.
 */
export function useCeoTheme() {
  const stored = ref<CeoTheme | null>(readStored());
  const systemDark = ref(false);
  let media: MediaQueryList | null = null;
  let observer: MutationObserver | null = null;
  let written: string | null = null;
  const theme = computed<CeoTheme>(() => stored.value || (appTheme() === "darkMode" ? "dark" : systemDark.value ? "dark" : "light"));

  function applyShell() {
    const value = theme.value === "dark" ? "darkMode" : "";
    written = value;
    document.documentElement.setAttribute("data-theme", value);
  }
  function onSystem(e: MediaQueryListEvent) {
    systemDark.value = e.matches;
    applyShell();
  }
  function toggle() {
    stored.value = theme.value === "dark" ? "light" : "dark";
    try {
      localStorage.setItem(THEME_KEY, stored.value);
    } catch {
      /* storage blocked: the choice lasts until the page is left */
    }
    applyShell();
  }
  onMounted(() => {
    try {
      media = window.matchMedia("(prefers-color-scheme: dark)");
      systemDark.value = media.matches;
      media.addEventListener?.("change", onSystem);
    } catch {
      media = null;
    }
    applyShell();
    observer = new MutationObserver(() => {
      const now = document.documentElement.getAttribute("data-theme") || "";
      if (now === written) return;
      // the top bar's switch was used on this page: it becomes the page's choice
      stored.value = now === "darkMode" ? "dark" : "light";
      written = now;
      try {
        localStorage.setItem(THEME_KEY, stored.value);
      } catch {
        /* ignore */
      }
    });
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
  });
  onBeforeUnmount(() => {
    observer?.disconnect();
    media?.removeEventListener?.("change", onSystem);
    document.documentElement.setAttribute("data-theme", appTheme() === "darkMode" ? "darkMode" : "");
  });
  return { theme, toggle };
}

// ---------------------------------------------------------------- chart colours (validated with the dataviz validator; see CEO_FOUNDATION.md)
export interface ChartTokens {
  card: string;
  ink: string;
  muted: string;
  grid: string;
  axis: string;
  categorical: string[];
  roles: Record<CeoSeriesRole, string>;
  other: string;
  sequential: string[];
}
/**
 * Categorical order (fixed, never cycled): steel, verdigris, khaki, rose, sky, violet — the hues of the visual brief, snapped to the nearest steps that pass the validator
 * (chroma floor, normal-vision floor), with their own dark steps validated on the dark card. Adjacent pairs pass in both modes. Verdigris and rose collapse for deuteranopes
 * (all-pairs), so a chart never pairs them by role: receipts/payments (positive/negative) use the validated steel/verdigris pair, and a repeated colour takes the next free slot.
 */
export const chartTokens: Record<CeoTheme, ChartTokens> = {
  light: {
    card: "#FFFFFF",
    ink: "#14202B",
    muted: "#5B6877",
    grid: "#EEF1F4",
    axis: "#D3DAE2",
    categorical: ["#235F94", "#008E7A", "#B9A24A", "#B35C7A", "#63A4DB", "#7D6BB0"],
    roles: { primary: "#235F94", secondary: "#008E7A", positive: "#008E7A", negative: "#235F94", neutral: "#8D99A6", accent: "#C2722B" },
    other: "#A3ADB8",
    sequential: ["#8AB2DB", "#5E93C8", "#3B75AD", "#235F94", "#123F69"],
  },
  dark: {
    card: "#171F29",
    ink: "#E6ECF2",
    muted: "#93A1B1",
    grid: "#222C38",
    axis: "#314051",
    categorical: ["#2578BE", "#1C9F8A", "#AD9118", "#D36D90", "#429ADF", "#775EB5"],
    roles: { primary: "#2578BE", secondary: "#1C9F8A", positive: "#1C9F8A", negative: "#2578BE", neutral: "#8C99A7", accent: "#E3A061" },
    other: "#A9B3BE",
    sequential: ["#255790", "#2F6CA8", "#4286C6", "#64A1DA", "#8EC0EC"],
  },
};
