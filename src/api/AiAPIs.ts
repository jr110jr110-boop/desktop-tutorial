import { call, clean, http } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// Local AI (api/v1/ai, release A1): the assistant (text and voice), conversations, the notes the seats leave for people, the CEO brief, and the admin side
// (model endpoints, prompt versions, seats, evaluation, audit, document index). Every answer is { success, message, data }; a rule violation answers HTTP 200 with success=false
// (the helpers return it as the error, with the Persian message of the server). Lists are paged ({ data: [], currentPage, itemsPerPage, totalItems, totalPages }; read them with rowsOf() or usePagedList()),
// except the plain arrays: admin/endpoints, admin/prompts, admin/seats, admin/eval/cases. Enums are numbers (see composables/ai.ts). Dates in the JSON are ISO; the "...Text" fields are Persian (Jalali).
// The assistant acts as the signed-in user; the AI never approves anything: accepting a note only closes the note, the record is changed by a person in its own module behind its own gate.
// Who: chat, voice, conversations, notes: every signed-in user (a note is read and decided only by the people of the module it belongs to; the service decides).
// The brief: CEO. Admin reads: AiRoles.readers (PMO, CEO). Admin writes (endpoints, prompts, seats, evaluation start, index rebuild): AiRoles.admins (PMO).
const ai = "ai/";

type Method = "get" | "post";
async function fcall(method: Method, url: string, body?: any, params?: any): Promise<[any, null] | [null, Error | any]> {
  const [data, error] = await call(method, url, body, params);
  return error ? [null, error] : [camelize(data), null];
}
const get = (url: string, params?: any) => fcall("get", url, undefined, clean(params));
const post = (url: string, body?: any) => fcall("post", url, body);

/** Paging of every list: PageNumber (1), PageSize (0 = all rows; other sizes are capped at 200). usePagedList() adds both. */
export type AiPageParams = { PageNumber?: number; PageSize?: number; [key: string]: any };

// ---------------------------------------------------------------- chat
/** Where the question was asked (release D1 §7.1): the page and what is on it, so the model knows which UI actions make sense. */
export interface AiUiContext {
  page: string;
  sections: string[];
  theme: string;
}
/**
 * A UI action of an answer (release D1 §7.1), in the order the model called the ui_* tools. It runs NO server code: the page carries it out (navigate inside the app,
 * open a detail table, run a section analysis, read aloud, scroll) and confirms it in one line. The assistant never approves, finalises, pays or deletes anything.
 */
export interface AiUiAction {
  type: "navigate" | "openDrill" | "analyze" | "read" | "scroll" | string;
  link?: string | null;
  drillKey?: string | null;
  params?: Record<string, any> | null;
  sectionKey?: string | null;
  what?: "section" | "alerts" | "brief" | "headline" | string | null;
  title?: string | null;
}

/**
 * AiChatRequestDto { conversationId? (continue a conversation), text (1..2000 characters), uiContext? { page, sections, theme } (D1 §7.1, sent by the CEO page) }.
 * Returns AiChatResponseDto { conversationId, answer, citations: [{ type, id, title, link }], toolCalls: [{ name, allowed }], verified, degraded, model,
 * actions: [{ type: navigate | openDrill | analyze | read | scroll, link?, drillKey?, params?, sectionKey?, what?, title }] (D1 §7.1; empty or missing when the model called no ui_* tool) }.
 * verified=false: the model's numbers could not be checked, the answer then holds the direct results of the system. degraded=true: the model is down, the deterministic dashboard still works.
 * Who: every signed-in user.
 */
export const aiChat = (dto: { conversationId?: number | null; text: string; uiContext?: AiUiContext | null }) =>
  post(ai + "chat", dto.uiContext ? { conversationId: dto.conversationId || null, text: dto.text, uiContext: dto.uiContext } : { conversationId: dto.conversationId || null, text: dto.text });

/**
 * Push-to-talk: multipart form with the fields `file` (the recording: webm, wav or ogg, up to 10 MB), `conversationId` (optional) and, from the CEO page, the UI context as the
 * nested form fields `uiContext.page`, `uiContext.theme`, `uiContext.sections[0..n]` (bound by [FromForm] to AiVoiceForm.UiContext). Returns AiVoiceResponseDto
 * { conversationId, transcript, answer, audioBase64 (WAV, base64; empty when the speech services were not available), citations, toolCalls, verified, degraded, model, actions (as aiChat) }.
 * Who: every signed-in user.
 */
export function aiVoice(file: Blob, fileName: string, conversationId?: number | null, uiContext?: AiUiContext | null) {
  const form = new FormData();
  form.append("file", file, fileName);
  if (conversationId) form.append("conversationId", String(conversationId));
  if (uiContext) {
    form.append("uiContext.page", uiContext.page);
    form.append("uiContext.theme", uiContext.theme);
    uiContext.sections.forEach((s, i) => form.append(`uiContext.sections[${i}]`, s));
  }
  return fcall("post", ai + "voice", form);
}

/** params: paging. PAGED list of AiConversationListDto { id, title, createdAt, lastAt, messages } of the signed-in user, newest first. */
export const getAiConversations = (params?: AiPageParams) => get(ai + "conversations", params);
/** AiConversationDto { id, title, createdAt, lastAt, messages, messageList: [{ id, role, text, toolName, createdAt, verified, model }] }. Only the owner reads it. */
export const getAiConversation = (id: number) => get(ai + "conversations/" + id);

// ---------------------------------------------------------------- notes of the seats
/**
 * params: AiNoteQuery { TargetType (one, or several comma separated: QualityNcr, FinanceJournal, BankLine, SupplierIbanChange, PurchaseRfq, OfficeLetter, OfficeMeeting, PcWeeklyReport, CeoBrief), TargetId, Status (1 new, 2 accepted, 3 dismissed),
 * Seat (seat code), Rule (the rule that raised the note, FIN-ANOMALY: DUPLICATE, ROUND, OFFDAY, UNMATCHED, IBAN) } + paging. Newest first; only the notes the caller may read.
 * AiNoteDto { id, seatCode, targetType, targetId, ruleCode, title, text, status, createdAt, createdAtText, decidedAt, decidedAtText, decidedByUserId, decidedByName, decisionNote, verified, seatRunId, link }.
 */
export const getAiNotes = (params?: AiPageParams) => get(ai + "notes", params);
/** One AiNoteDto; a note the caller may not read answers «یادداشت پیدا نشد.» */
export const getAiNote = (id: number) => get(ai + "notes/" + id);
/** AiDecideNoteDto { id, decision: "accept" | "dismiss", note? (up to 1000 characters) }. Returns the AiNoteDto. Only closes the note; a seat user can never decide. Who: the people of the module of the note. */
export const decideAiNote = (dto: { id: number; decision: "accept" | "dismiss"; note?: string | null }) => post(ai + "notes/decide", { id: dto.id, decision: dto.decision, note: dto.note || null });

// ---------------------------------------------------------------- the daily brief of the CEO
/** AiBriefDto { date, dateText, text, verified, noteId }. Who: CEO. Answers «هنوز گزارش روزانه‌ای ساخته نشده است.» when none exists. */
export const getAiBrief = () => get(ai + "brief/latest");

/**
 * The latest brief spoken (audio/wav, made on demand): fetched as a blob with the Bearer token of the shared client. Returns [Blob, null], or [null, Error] with the Persian message
 * when the API answers JSON { success: false, message } instead of the file. Who: CEO.
 */
export async function getAiBriefAudio(): Promise<[Blob, null] | [null, Error]> {
  try {
    const response = await http.get(ai + "brief/latest/audio", { responseType: "blob" });
    const type = String(response.headers["content-type"] || "");
    if (type.includes("application/json")) {
      const json = JSON.parse(await (response.data as Blob).text());
      return [null, new Error(json.message || "گزارش صوتی ساخته نشد.")];
    }
    return [response.data as Blob, null];
  } catch (error: any) {
    if (error?.response?.status === 403) return [null, new Error("شما اجازه این کار را ندارید.")];
    if (error?.response?.status === 401) return [null, new Error("دوباره وارد سامانه شوید.")];
    return [null, error instanceof Error ? error : new Error("خطا")];
  }
}

// ---------------------------------------------------------------- admin: model endpoints
/**
 * PLAIN ARRAY of AiEndpointDto { id, name, kind (1 Chat, 2 Embeddings, 3 Stt, 4 Tts), url, modelName, priority, enabled, timeoutSeconds, lastHealthAt, lastHealthOk, lastError }.
 * Who: AiRoles.readers.
 */
export const getAiEndpoints = () => get(ai + "admin/endpoints");
/** AiSaveEndpointDto { id? (empty = new), name, kind (1..4), url, modelName, priority, enabled, timeoutSeconds }. Returns the AiEndpointDto. Who: AiRoles.admins (PMO). */
export const saveAiEndpoint = (dto: any) => post(ai + "admin/endpoints", dto);
/** Calls every endpoint once and stores the result. Returns the PLAIN ARRAY of AiEndpointDto with the new lastHealth* fields. Who: PMO. */
export const checkAiEndpoints = () => post(ai + "admin/endpoints/health-check");

// ---------------------------------------------------------------- admin: prompt versions
/** PLAIN ARRAY of AiPromptVersionDto { id, code, version, text, isActive, createdAt, createdByUserId, activatedAt, activatedByUserId }, by code then newest version. code empty = every code. Who: readers. */
export const getAiPrompts = (code?: string | null) => get(ai + "admin/prompts", { code });
/** AiNewPromptDto { code (empty = CHAT; upper-cased), text }. Creates the next version, not active. Returns the AiPromptVersionDto. Who: PMO. */
export const newAiPrompt = (dto: { code: string | null; text: string }) => post(ai + "admin/prompts", { code: dto.code || null, text: dto.text });
/** AiActivatePromptDto { id }: this version becomes the active one of its code (the previous one is switched off). Who: PMO. */
export const activateAiPrompt = (id: number) => post(ai + "admin/prompts/activate", { id });

// ---------------------------------------------------------------- admin: audit and index
/**
 * params: AiAuditQuery { Kind (chat, voice, tool, unverified, degraded, failover, admin, seat, note, eval), SeatCode, UserId, From, To (ISO dates, the whole day) } + paging.
 * PAGED list of AiAuditEventDto { id, at, atText, userId, seatCode, kind, promptVersionId, model, latencyMs, detail, promptHash }. Append-only. Who: readers.
 */
export const getAiAudit = (params?: AiPageParams) => get(ai + "admin/audit", params);
/** AiIndexRebuildResultDto { controlledDocuments, meetingMinutes, chunks, failed, message }. Rebuilds the search index of the controlled documents and meeting minutes. Who: PMO. */
export const rebuildAiIndex = () => post(ai + "index/rebuild");

// ---------------------------------------------------------------- admin: seats
/**
 * PLAIN ARRAY of AiSeatDto { id, code, title, serviceUserName, roles (comma separated), schedule, scheduleText (Persian), promptVersionCode, enabled, outputKind, lastRun: AiSeatRunDto }.
 * Who: readers.
 */
export const getAiSeats = () => get(ai + "admin/seats");
/** AiEnableSeatDto { code, enabled }. Returns the AiSeatDto. Who: PMO. */
export const enableAiSeat = (code: string, enabled: boolean) => post(ai + "admin/seats/enable", { code, enabled });
/**
 * AiRunSeatDto { code, asOf? (ISO date, the data day; today when empty; a future day needs the switch Ai:AllowFutureAsOf) }. Returns the AiSeatRunDto of the run that was STARTED
 * (status 1 Running): read it again with getAiSeatRun() until the status is no longer Running. Who: PMO.
 */
export const runAiSeat = (code: string, asOf?: string | null) => post(ai + "admin/seats/run", { code, asOf: asOf || null });
/** AiSeatRunDto { id, seatId, seatCode, startedAt, startedAtText, finishedAt, status (1 Running, 2 Succeeded, 3 Failed, 4 Skipped), outputs, error, asOf }. Who: readers. */
export const getAiSeatRun = (id: number) => get(ai + "admin/seats/runs/" + id);
/** params: AiSeatRunQuery { Seat (seat code), Status (1..4) } + paging. PAGED list of AiSeatRunDto, newest first. Who: readers. */
export const getAiSeatRuns = (params?: AiPageParams) => get(ai + "admin/seats/runs", params);

// ---------------------------------------------------------------- admin: evaluation
/** PLAIN ARRAY of AiEvalCaseDto { id, code, question, expectedTools, expectedFacts, enabled }. Who: readers. */
export const getAiEvalCases = () => get(ai + "admin/eval/cases");
/** Starts an evaluation in the background. Returns the AiEvalRunDto with running=true: read it again with getAiEvalRun() until running is false. Who: PMO. */
export const runAiEval = () => post(ai + "admin/eval/run");
/** PAGED list of AiEvalRunDto { id, startedAt, startedAtText, finishedAt, total, passed, running, passPercent, numericFailures, p50Ms, p95Ms, model } + paging. Who: readers. */
export const getAiEvalRuns = (params?: AiPageParams) => get(ai + "admin/eval/runs", params);
/**
 * One AiEvalRunDto with cases: [{ code, question, passed, expectedTools, calledTools[], toolsOk, factsOk, verified, degraded, ms, model, answer, facts: [{ fact, ok, reason }] }].
 * The answers and the reasons are null for everybody except the CEO (they hold the data the CEO sees); the PMO reads the counts. Who: readers.
 */
export const getAiEvalRun = (id: number) => get(ai + "admin/eval/runs/" + id);
