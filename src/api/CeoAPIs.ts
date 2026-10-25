import { call, clean } from "@/api/ApiClient";

// CEO executive dashboard (api/v1/ceo, release D1): one overview of the whole company (8 headline KPIs, 9 module sections with KPIs and charts, the alerts and the pulse of the modules),
// the drill catalogue (the detail tables behind every KPI and chart; the same service feeds the AI tool ceo_drill), the AI analysis of a section and the nightly KPI snapshot.
// Every answer is { success, message, data }; a refused call answers HTTP 200 with success=false and the Persian message of the server comes back as the error.
// The JSON is camelCase by contract, so nothing is camelized here: the rows of a drill table are keyed by the column keys exactly as the server writes them.
// Dates are ISO in the JSON; every date a person reads also comes as a "...Text" Shamsi string. Money is rial (decimal); the page formats it (composables/ceo.ts).
// Payroll privacy: no person's pay is ever in a row; aggregated labour amounts come with hidden=true (value null) for the roles the existing dashboards mask.
// Who: FinanceRoles.ceo (CEO, PMO) for every endpoint; the server checks each call again.
const base = "ceo/";

/** Overview (CeoOverviewDto). */
export interface CeoOverview {
  asOf: string;
  asOfText: string;
  generatedAt: string;
  generatedInMs: number;
  cached: boolean;
  headline: CeoKpi[];
  sections: CeoSection[];
  alerts: CeoAlert[];
  pulse: CeoPulse[];
}
export type CeoUnit = "rial" | "count" | "percent" | "days" | "hours";
export type CeoStatus = "ok" | "warn" | "danger" | "neutral";
/** KpiDto: one figure with its trend, delta and status. hidden=true: payroll mask (value null, valueText «محرمانه (حقوق)»). */
export interface CeoKpi {
  key: string;
  title: string;
  value: number | null;
  unit: CeoUnit;
  valueText: string;
  shortText: string | null;
  delta: number | null;
  deltaText: string | null;
  deltaGood: boolean | null;
  trend: number[] | null;
  trendLabels: string[] | null;
  status: CeoStatus;
  hidden: boolean;
  link: string | null;
  drillKey: string | null;
  note: string | null;
}
export type CeoSeriesRole = "primary" | "secondary" | "positive" | "negative" | "neutral" | "accent";
/** ChartDto. kind decides the drawing; hidden=true: the chart holds masked labour amounts. */
export interface CeoChart {
  key: string;
  title: string;
  kind: "line" | "bar" | "stackedBar" | "hbar" | "donut";
  unit: CeoUnit;
  labels: string[];
  series: { name: string; values: number[]; role: CeoSeriesRole }[];
  note: string | null;
  link: string | null;
  drillKey: string | null;
  hidden: boolean;
}
/** SectionDto: one module (finance, sales, projects, production, quality, inventory, procurement, service, office). */
export interface CeoSection {
  key: string;
  title: string;
  kpis: CeoKpi[];
  charts: CeoChart[];
  alertCount: number;
  link: string;
  analysisPrompt: string;
}
/** AlertDto of the existing CEO dashboard. */
export interface CeoAlert {
  severity: "danger" | "warning" | "info";
  text: string;
  link: string | null;
}
/** PulseDto: one indicator per section. */
export interface CeoPulse {
  key: string;
  title: string;
  status: CeoStatus;
  dangerCount: number;
  warnCount: number;
  link: string | null;
}
/** DrillDescriptorDto: one entry of the drill catalogue. */
export interface CeoDrillDescriptor {
  key: string;
  title: string;
  description: string;
  module: string;
  parameters: { name: string; type: "int" | "date" | "string" | "bool"; required: boolean; description: string }[];
  roles: string;
}
export type CeoColumnKind = "text" | "number" | "rial" | "percent" | "date" | "link" | "status";
/** DrillTableDto: one page of a detail table. Link columns hold a front route; date columns hold Shamsi text. hidden=true: masked, no rows. */
export interface CeoDrillTable {
  key: string;
  title: string;
  asOfText: string;
  columns: { key: string; title: string; kind: CeoColumnKind }[];
  rows: Record<string, any>[];
  totalCount: number;
  page: number;
  pageSize: number;
  link: string | null;
  note: string | null;
  hidden: boolean;
}
/** AiChatResponseDto (the answer of the assistant, see AiAPIs.aiChat). */
export interface CeoAnalysis {
  conversationId: number | null;
  answer: string;
  citations: { type: string; id: string; title: string; link: string }[];
  toolCalls: { name: string; allowed: boolean }[];
  verified: boolean;
  degraded: boolean;
  model: string | null;
}

/**
 * GET ceo/overview?refresh=false. Returns CeoOverviewDto { asOf, asOfText, generatedAt, generatedInMs, cached, headline: KpiDto[8] (keys in this order: cash, receivablesOpen,
 * payablesOverdue, salesBacklog, activeProjects, lateProductionOrders, openNcrs, serviceSlaBreaches), sections: SectionDto[9] (finance, sales, projects, production, quality,
 * inventory, procurement, service, office), alerts: [{ severity: danger | warning | info, text, link }], pulse: [{ key, title, status, dangerCount, warnCount, link }] }.
 * The server keeps the answer 120 seconds per role-mask variant; refresh=true reads it again (cached tells which one came back). Who: CEO, PMO.
 */
export const getCeoOverview = (refresh = false) => call("get", base + "overview", undefined, { refresh }) as Promise<[CeoOverview, null] | [null, Error]>;

/** GET ceo/section/{key}. One SectionDto { key, title, kpis (3-6), charts (1-3), alertCount, link, analysisPrompt }, read fresh (never cached). Who: CEO, PMO. */
export const getCeoSection = (key: string) => call("get", base + "section/" + encodeURIComponent(key)) as Promise<[CeoSection, null] | [null, Error]>;

/**
 * GET ceo/drills. PLAIN ARRAY of DrillDescriptorDto { key, title (fa), description (fa), module, parameters: [{ name, type: int | date | string | bool, required, description (fa) }], roles }.
 * The catalogue of the detail tables behind the KPIs and charts (each KPI and chart names its table in drillKey). Who: CEO, PMO.
 */
export const getCeoDrills = () => call("get", base + "drills") as Promise<[CeoDrillDescriptor[], null] | [null, Error]>;

/**
 * GET ceo/drill?key=...&top=50&page=1&<params>. One page of a detail table: DrillTableDto { key, title, asOfText, columns: [{ key, title (fa), kind: text | number | rial | percent |
 * date | link | status }], rows (keyed by the column keys; link columns hold a front route, date columns Shamsi text), totalCount, page, pageSize, link, note, hidden }.
 * params: the parameters of the descriptor (for example customerId, contractId, from, to); empty values are not sent. An unknown key answers the Persian message of the server. Who: CEO, PMO.
 */
export const getCeoDrill = (key: string, params: { top?: number; page?: number; [name: string]: any } = {}) =>
  call("get", base + "drill", undefined, clean({ top: 50, page: 1, ...params, key })) as Promise<[CeoDrillTable, null] | [null, Error]>;

/**
 * POST ceo/analyze { sectionKey (null = the headline of the whole company), question (null = the default analysis question), conversationId (continue a conversation) }.
 * Returns AiChatResponseDto { conversationId, answer, citations: [{ type, id, title, link }], toolCalls: [{ name, allowed }], verified, degraded, model }: a Persian narrative that compares
 * with the previous period, names the 3 most important points and the risks, every number taken from the data (verified=false when a number could not be checked).
 * degraded=true: the model is down and the answer is the standard degraded sentence; the dashboard keeps working. Who: CEO, PMO.
 */
export const analyzeCeo = (dto: { sectionKey: string | null; question?: string | null; conversationId?: number | null }) =>
  call("post", base + "analyze", { sectionKey: dto.sectionKey ?? null, question: dto.question || null, conversationId: dto.conversationId || null }) as Promise<[CeoAnalysis, null] | [null, Error]>;

/** Spoken summary (D1 §7.2). */
export interface CeoSpeech {
  text: string;
  audioBase64: string | null;
  degraded: boolean;
}
/**
 * GET ceo/speech?what=headline|section|alerts&sectionKey=. A deterministic spoken summary that works even when the language model is down: { text (Persian sentences built from
 * the KPIs or the alerts, numbers in words), audioBase64 (WAV, base64; null when no speech endpoint is up — the page then uses the browser's Persian voice or shows the text), degraded }.
 * sectionKey is required for what=section. Who: CEO, PMO.
 */
export const getCeoSpeech = (what: "headline" | "section" | "alerts", sectionKey?: string | null) =>
  call("get", base + "speech", undefined, clean({ what, sectionKey })) as Promise<[CeoSpeech, null] | [null, Error]>;

/** POST ceo/snapshot. Stores today's KPI snapshot now (the nightly job does the same at 00:30; one row per day and key). Returns { dateText, keys } (the number of KPI keys stored). Who: CEO, PMO. */
export const snapshotCeo = () => call("post", base + "snapshot") as Promise<[{ dateText: string; keys: number }, null] | [null, Error]>;
