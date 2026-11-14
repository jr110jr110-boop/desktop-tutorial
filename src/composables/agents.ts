import type { Router } from "vue-router";
import { frontLink, seatTitleOf } from "@/composables/ai";
import { roleTitles } from "@/composables/roles";

// Names, colours and helpers of the agent layer. Enum values are numbers in the JSON and match Entities/Model/NasbNiro/Agents/AgentEntities.cs and ViewModels/NasbNiro/AgentDtos/AgentDtos.cs exactly.
// Everything a person reads is Persian (Jalali); the API JSON stays ISO. An agent never approves: this layer only shows what waits for a person and where to do it.

export { jalali } from "@/composables/inventory";

/** AgentStepKind. */
export const AgentStepKind = { Agent: 1, HumanGate: 2, Physical: 3, Input: 4 } as const;
export const stepKinds: Record<number, string> = { 1: "ایجنت", 2: "تأیید و امضا", 3: "کار فیزیکی", 4: "ورود اطلاعات" };

/** AgentTaskState (open while below 10). */
export const AgentTaskState = { Queued: 1, Running: 2, Returned: 3, WaitingHumanGate: 4, WaitingPhysical: 5, WaitingInput: 6, Failed: 7, Done: 10, Cancelled: 11 } as const;
export const taskStates: Record<number, string> = {
  1: "در صف ایجنت",
  2: "در حال اجرا توسط ایجنت",
  3: "برگشت‌خورده به ایجنت",
  4: "منتظر تأیید",
  5: "منتظر کار فیزیکی",
  6: "منتظر ورود اطلاعات",
  7: "ناموفق",
  10: "انجام‌شده",
  11: "لغو‌شده",
};
export function badgeOfState(state: number): string {
  if (state === 7) return "bad";
  if (state === 4 || state === 6) return "warn";
  if (state === 10) return "ok";
  return "info";
}

/** AgentCellStatus (a higher value is worse; the cell shows the worst). */
export const AgentCellStatus = { NotStarted: 0, Idle: 1, Agent: 2, WaitingInput: 3, WaitingPhysical: 4, WaitingGate: 5, Failed: 6 } as const;
export const cellStatuses: Record<number, { key: string; title: string; short: string }> = {
  0: { key: "none", title: "شروع نشده", short: "شروع نشده" },
  1: { key: "idle", title: "بدون کار باز", short: "انجام‌شده" },
  2: { key: "agent", title: "ایجنت در حال کار", short: "ایجنت کار می‌کند" },
  3: { key: "input", title: "منتظر ورود اطلاعات", short: "منتظر اطلاعات" },
  4: { key: "physical", title: "منتظر کار فیزیکی", short: "کار فیزیکی" },
  5: { key: "gate", title: "منتظر تأیید و امضا", short: "منتظر تأیید" },
  6: { key: "failed", title: "ناموفق", short: "ناموفق" },
};
/** The order of the legend (best to worst). */
export const cellStatusOrder = [0, 1, 2, 3, 4, 5, 6];

/** AgentStuckReason. */
export const stuckReasons: Record<number, string> = { 1: "ناموفق", 2: "گیت با تأخیر", 3: "ایجنت خاموش (دستی)" };
export function badgeOfStuck(reason: number): string {
  return reason === 1 ? "bad" : reason === 2 ? "warn" : "info";
}

/** The thirteen stations (AgentStations.Titles); the board also sends them, this is the fallback and the filter list. */
export const stationTitles: string[] = [
  "فروش و پیشنهاد",
  "قرارداد و بازنگری قرارداد",
  "آغاز پروژه",
  "برنامه‌ریزی و کنترل پروژه",
  "مهندسی",
  "تدارکات و خرید",
  "انبار",
  "تولید",
  "کیفیت",
  "بسته‌بندی",
  "ارسال و تحویل",
  "خدمات پس از فروش",
  "مالی و سپیدار",
];

/** Persian digits. */
export const fa = (n: number | string | null | undefined): string => (n === null || n === undefined || n === "" ? "" : Number(n).toLocaleString("fa-IR", { useGrouping: false }));

/** «۳ ساعت پیش», «۲ روز پیش» (hours as the API counts them). */
export function ageText(hours: number | null | undefined): string {
  const h = Math.max(0, Math.floor(Number(hours) || 0));
  if (h < 1) return "کمتر از یک ساعت پیش";
  if (h < 48) return `${fa(h)} ساعت پیش`;
  return `${fa(Math.floor(h / 24))} روز پیش`;
}
/** The same age without «پیش» (for «قدیمی‌ترین: ...»). */
export function ageShort(hours: number | null | undefined): string {
  const h = Math.max(0, Math.floor(Number(hours) || 0));
  if (h < 1) return "کمتر از ۱ ساعت";
  if (h < 48) return `${fa(h)} ساعت`;
  return `${fa(Math.floor(h / 24))} روز`;
}

/** "PMO,CEO" -> «معاونت PMO، مدیرعامل». */
export function rolesText(csv: string | null | undefined): string {
  if (!csv) return "";
  return csv
    .split(",")
    .map((r) => r.trim().toUpperCase())
    .filter(Boolean)
    .map((r) => roleTitles[r] || r)
    .join("، ");
}

/** «ایجنت: ...» title of an agent seat code (AG-*). */
export const agentTitleOf = (code: string | null | undefined) => seatTitleOf(code);

/** The router path of the link of a task, or null when the front end has no such page (the button is then hidden). */
export function taskLink(router: Router, link: string | null | undefined): string | null {
  return frontLink(router, link);
}

/** Which tab (kind) a waiting task belongs to, by its STATE (a fallback task of a switched-off agent has the kind Agent but waits as input). */
export function tabOfState(state: number): number | null {
  return state === 4 ? 2 : state === 5 ? 3 : state === 6 ? 4 : null;
}

/** Calls fn now and then every 60 s while the browser tab is visible; returns the stop function (clear it on unmount). */
export function startAutoRefresh(fn: () => void, ms = 60000): () => void {
  const tick = () => {
    if (!document.hidden) fn();
  };
  const timer = setInterval(tick, ms);
  const onVisible = () => {
    if (!document.hidden) fn();
  };
  document.addEventListener("visibilitychange", onVisible);
  return () => {
    clearInterval(timer);
    document.removeEventListener("visibilitychange", onVisible);
  };
}
