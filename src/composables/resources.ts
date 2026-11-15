import type { LevelingProposal, Resource, ResourceInput, ResourceOverload, ResourcePlan, ResourceRate } from "@/api/ResourceAPIs";
import { faDigits, faNum } from "@/composables/ceo";
import { CONFIDENTIAL, jalali } from "@/composables/inventory";
import { currentUserId, hasRole, isRole, ProjectControlRoles } from "@/composables/roles";

// Names, texts and rules of the resource pages (pool, standard rates, resource plan). The Persian texts of the refusals are the words of the API (PcResourceText, PcText in the services):
// a button the server would refuse is shown disabled with the same words, never enabled. The server checks everything again on every call.
// Who is who: the maker of a plan or a rate is a person; the plan is drafted by the planning agent («تهیه‌شده توسط ایجنت») and a person reviews, edits and approves it.

export const resourceKinds: Record<number, string> = { 1: "نیروی انسانی", 2: "ماشین", 3: "تجهیزات", 4: "مواد", 5: "پیمانکار فرعی" };
export const resourceUnits: Record<number, string> = { 1: "ساعت", 2: "روز", 3: "عدد" };
export const rateStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال‌شده", 2: "تاییدشده", 3: "ردشده" };
export const planStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال‌شده", 2: "تاییدشده", 3: "برگشت‌خورده" };
export const distributions: Record<number, string> = { 1: "یکنواخت", 2: "بیشتر در ابتدا", 3: "بیشتر در انتها" };
export const assignmentSources: Record<number, string> = { 1: "ایجنت", 2: "انسان", 3: "ورود" };
export const PREPARED_BY_AGENT = "تهیه‌شده توسط ایجنت";
export const NO_APPROVED_RATE = "بدون نرخ مصوب";
export { CONFIDENTIAL };

export const rateStatusBadge = (s: number) => (s === 2 ? "ok" : s === 3 ? "bad" : s === 1 ? "warn" : "info");
export const planStatusBadge = (s: number) => (s === 2 ? "ok" : s === 3 ? "bad" : s === 1 ? "warn" : "info");

/** The words of the API for the refusals the pages announce beforehand. */
export const ResourceTexts = {
  notAllowed: "شما مجوز این کار را ندارید.",
  sameUser: "این کار باید توسط شخص دیگری انجام شود (تفکیک وظایف).",
  rateApproveSameUser: "این کار باید توسط شخص دیگری انجام شود (تفکیک وظایف). (پیشنهاددهنده نرخ نمی‌تواند آن را تایید کند)",
  rateSubmitOnlyMaker: "نرخ پیش‌نویس را فقط پیشنهاددهنده آن ارسال می‌کند.",
  rateEditOnlyMaker: "نرخ پیش‌نویس را فقط پیشنهاددهنده آن ویرایش می‌کند.",
  ratePrivacy: "روی منبع مرتبط با یک فرد نرخ ثبت نمی‌شود (محرمانگی حقوق)؛ همان منبع را به منبع نقش مربوط (مثلا «جوشکار») وصل کنید و نرخ نقش را ثبت کنید.",
  planApproverOnly: "شما مجوز این کار را ندارید. (مدیر پروژه همین قرارداد)",
  planMakerCannotDecide: "این کار باید توسط شخص دیگری انجام شود (تفکیک وظایف). (سازنده یا ویرایش‌کننده طرح نمی‌تواند آن را تایید یا برگشت دهد)",
  resourceUsed: "منبع در طرح منابع یا خط مبنا به کار رفته است و حذف نمی‌شود؛ آن را غیرفعال کنید.",
  kindUnitLocked: "نوع و واحد شمارش منبعی که در طرح منابع به کار رفته یا نرخ دارد تغییر نمی‌کند؛ منبع تازه بسازید.",
  discardChange: "نسخه‌ای که با درخواست تغییر باز شده دور انداخته نمی‌شود.",
  noPlan: "برای این قرارداد هنوز طرح منابع ثبت نشده است.",
  oldVersion: "این نسخه قدیمی طرح است و فقط خواندنی است؛ ویرایش روی نسخه جاری انجام می‌شود.",
  unassigned: (n: number) => `برای ${n} فعالیت هنوز منبعی ثبت نشده است؛ برای هر فعالیت منبع بگذارید یا آن را «بدون منبع» علامت بزنید.`,
};

// ---------------------------------------------------------------- numbers and dates
export const faDate = (iso: string | null | undefined): string => (iso ? faDigits(jalali(iso)) : "—");
export const faDateTime = (iso: string | null | undefined): string => (iso ? faDigits(jalali(iso, true)) : "—");
export const qty = (v: number | null | undefined): string => faNum(v, 4);
export const money = (v: number | null | undefined): string => faNum(v === null || v === undefined ? null : Math.round(Number(v)), 0);

/** A rate in the pool and rate lists: the amount per unit, «محرمانه» when the API holds it back, «بدون نرخ مصوب» when there is none. */
export function rateText(rate: number | null | undefined, hidden: boolean, unit?: number): string {
  if (hidden) return CONFIDENTIAL;
  if (rate === null || rate === undefined) return NO_APPROVED_RATE;
  return money(rate) + " ریال" + (unit ? " / " + resourceUnits[unit] : "");
}

/** The unit a resource of this kind and the amount of a budget line is counted in. */
export const unitText = (unit: number): string => resourceUnits[unit] || "";

// ---------------------------------------------------------------- the pool: shape of a resource (the words of PcResourceRules.ValidateShape)
export function validateResource(r: ResourceInput): string | null {
  const code = r.code.trim();
  const title = r.title.trim();
  if (!code || code.length > 50) return "کد منبع لازم است (حداکثر ۵۰ نویسه).";
  if (!title || title.length > 300) return "عنوان منبع لازم است (حداکثر ۳۰۰ نویسه).";
  if (!(r.capacity > 0) || r.capacity > 100000) return "ظرفیت منبع باید بیشتر از صفر باشد.";
  if ((r.responsibleUnit || "").length > 30) return "واحد مسئول حداکثر ۳۰ نویسه است.";
  if ((r.notes || "").length > 1000) return "یادداشت حداکثر ۱۰۰۰ نویسه است.";
  const links = (r.employeeId ? 1 : 0) + (r.workCenterId ? 1 : 0) + (r.providerId ? 1 : 0);
  if (links > 1) return "یک منبع حداکثر به یک مورد وصل می‌شود: کارمند، مرکز کار یا تامین‌کننده.";
  if (r.employeeId && r.kind !== 1) return "پیوند به کارمند فقط برای منبع نوع نیروی انسانی است.";
  if (r.workCenterId && r.kind !== 2) return "پیوند به مرکز کار فقط برای منبع نوع ماشین است.";
  if (r.providerId && r.kind !== 5) return "پیوند به تامین‌کننده فقط برای منبع نوع پیمانکار فرعی است.";
  if (r.rateResourceId && !r.employeeId) return "منبع نقش مبنای نرخ فقط برای منبع نیروی انسانی وصل‌شده به کارمند است.";
  if (r.employeeId && !r.rateResourceId) return "منبع وصل‌شده به کارمند نرخ ندارد؛ منبع نقش مربوط (مثلا «جوشکار») را برای نرخ انتخاب کنید.";
  return null;
}

/** The pool is edited by planning and the PMO. */
export const canEditPool = () => hasRole(ProjectControlRoles.resourceWriters);

/** The words of the link kind a resource of this kind may carry (null: none). */
export const linkLabel = (kind: number): string | null => (kind === 1 ? "شناسه کارمند" : kind === 2 ? "شناسه مرکز کار" : kind === 5 ? "شناسه تامین‌کننده" : null);

// ---------------------------------------------------------------- the rates
export interface Gate {
  /** the button is shown at all (the role can do this kind of thing) */
  show: boolean;
  /** the server would refuse: the button is shown disabled with this text */
  reason: string | null;
}

const hidden: Gate = { show: false, reason: null };

export function rateSubmitGate(rate: ResourceRate): Gate {
  if (rate.status !== 0 || !hasRole(ProjectControlRoles.rateMakers)) return hidden;
  return { show: true, reason: rate.makerUserId === currentUserId() ? null : ResourceTexts.rateSubmitOnlyMaker };
}

export function rateEditGate(rate: ResourceRate): Gate {
  if (rate.status !== 0 || !hasRole(ProjectControlRoles.rateMakers)) return hidden;
  return { show: true, reason: rate.makerUserId === currentUserId() ? null : ResourceTexts.rateEditOnlyMaker };
}

export function rateApproveGate(rate: ResourceRate): Gate {
  if (rate.status !== 1 || !hasRole(ProjectControlRoles.rateApprovers)) return hidden;
  return { show: true, reason: rate.makerUserId === currentUserId() ? ResourceTexts.rateApproveSameUser : null };
}

export function rateRejectGate(rate: ResourceRate): Gate {
  if (rate.status !== 1 || !hasRole(ProjectControlRoles.rateApprovers)) return hidden;
  return { show: true, reason: rate.makerUserId === currentUserId() ? ResourceTexts.sameUser : null };
}

// ---------------------------------------------------------------- the plan
/** The plan can be edited by this user now: planning or the PMO, the plan is a draft or returned, and it is the newest version. */
export function planEditable(plan: ResourcePlan | null, isLatest: boolean): boolean {
  return !!plan && plan.editable && isLatest && hasRole(ProjectControlRoles.resourceWriters);
}

export function planSubmitGate(plan: ResourcePlan, isLatest: boolean): Gate {
  if (!planEditable(plan, isLatest)) return hidden;
  return { show: true, reason: plan.unassignedCount > 0 ? ResourceTexts.unassigned(plan.unassignedCount) : null };
}

/** Approve and return: the decider the server names (the project manager, or the PMO when the contract has none or the manager made or edited the plan). The words follow PcResourceService.DeciderProblemAsync. */
export function planDecideGate(plan: ResourcePlan, isLatest: boolean): Gate {
  const me = currentUserId();
  if (plan.status !== 1 || !isLatest) return hidden;
  if (!hasRole(ProjectControlRoles.projectManagers) && plan.deciderUserId !== me) return hidden;
  const allowed = plan.deciderUserId !== null ? plan.deciderUserId === me : isRole("PMO");
  if (!allowed) return { show: true, reason: ResourceTexts.planApproverOnly };
  if (plan.makerUserId === me) return { show: true, reason: ResourceTexts.planMakerCannotDecide };
  return { show: true, reason: null };
}

/** Discarding an open plan opened by mistake: planning and the PMO; not a plan that a change request opened (the baseline check is the server's: its refusal is shown). */
export function planDiscardGate(plan: ResourcePlan, isLatest: boolean): Gate {
  if (plan.status === 2 || !isLatest || !hasRole(ProjectControlRoles.resourceWriters)) return hidden;
  return { show: true, reason: plan.changeRequestId ? ResourceTexts.discardChange : null };
}

/** Where the plan of a contract is opened (the link of the gate inbox). */
export const planPath = (contractId: number) => "/project-control/resources?contract=" + contractId;
export const ratesPath = (resourceId?: number) => "/project-control/resources?tab=rates" + (resourceId ? "&resource=" + resourceId : "");

export const resourceLabel = (r: Pick<Resource, "code" | "title">) => `${r.code} - ${r.title}`;

// ---------------------------------------------------------------- phase 4c: the load of the resources, the over-allocations, the levelling proposals
// The words are those of the API (PcLoadText in PcResourceLoadService): a button the server would refuse is shown disabled with the same words.
// The planning agent finds the over-allocations and proposes; the planner applies or rejects; a second person (PMO, not the one who applied) confirms or refuses; the PMO alone accepts an over-allocation on purpose.

export const overloadStatuses: Record<number, string> = { 1: "باز", 2: "رفع‌شده", 3: "پذیرفته‌شده", 4: "گذشته، بدون اصلاح" };
export const proposalStatuses: Record<number, string> = { 1: "پیشنهادشده", 2: "اعمال‌شده", 3: "ردشده", 4: "منسوخ", 5: "بسته‌شده بدون برگرداندن" };
export const proposalKinds: Record<number, string> = { 1: "جابه‌جایی در محدوده شناوری", 2: "تاخیر فراتر از شناوری" };
export const PENDING_SECOND = "در انتظار تأیید نفر دوم";
export const DRAFT_LAYER = "پیش‌نویس";
export const CONSTRAINT_NOTE = "با اعمال، قید «شروع نه زودتر از» روی فعالیت می‌ماند؛ برگرداندن آن با «عدم‌تایید» نفر دوم است.";
export const overloadStatusBadge = (s: number) => (s === 1 ? "bad" : s === 3 ? "warn" : s === 4 ? "info" : "ok");

// The buttons are gated by the SERVER: every proposal and over-allocation carries, for the caller, the reason the action would be refused (null: allowed now). The rules (roles, the lock of the project,
// the applier is not the confirmer, maker is not checker) are those of the actions; the front only shows the button for the status it belongs to, disabled with the server's words.

/** Apply: a proposal that is still «پیشنهادشده». */
export const proposalApplyGate = (p: LevelingProposal): Gate => (p.status === 1 ? { show: true, reason: p.applyBlockedReason } : hidden);
export const proposalRejectGate = (p: LevelingProposal): Gate => (p.status === 1 ? { show: true, reason: p.rejectBlockedReason } : hidden);

/** Confirm and refuse: an applied change that waits for the second person. */
export const proposalConfirmGate = (p: LevelingProposal): Gate => (p.pendingSecondPerson ? { show: true, reason: p.confirmBlockedReason } : hidden);

/** «بستن بدون برگرداندن»: an applied change whose automatic revert is impossible (the server allows it only then). */
export const proposalCloseGate = (p: LevelingProposal): Gate => (p.pendingSecondPerson ? { show: true, reason: p.closeBlockedReason } : hidden);

/** Accept an over-allocation on purpose. */
export const overloadAcceptGate = (o: ResourceOverload): Gate => (o.status === 1 ? { show: true, reason: o.acceptBlockedReason } : hidden);

/** Where the over-allocations of a resource, or one proposal, are opened (the links of the gate inbox). */
export const overloadsPath = (overloadId?: number, proposalId?: number) =>
  "/project-control/resources?tab=overloads" + (overloadId ? "&overload=" + overloadId : "") + (proposalId ? "&proposal=" + proposalId : "");
export const loadPath = (resourceId?: number) => "/project-control/resources?tab=load" + (resourceId ? "&resource=" + resourceId : "");

/** A number of the load in the unit of the resource, Persian digits. */
export const loadNum = (v: number | null | undefined): string => faNum(v, 2);
