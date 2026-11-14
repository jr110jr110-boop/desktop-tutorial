import { call, clean } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// Agent layer (api/v1/agents). Every answer is { success, message, data }; a rule violation answers HTTP 200 with success=false
// (the helpers return it as the error, with the Persian message of the server). Lists are paged ({ data: [], currentPage, totalItems }; PageSize 0 = all rows, other sizes are capped at 200).
// Enums are numbers (see composables/agents.ts). Dates in the JSON are ISO; the "...Text" fields are Persian (Jalali).
// Who: the inbox (list, counts, return) every signed-in person (the service shows only what waits for his roles); tasks, task detail and the board AgentRoles.board (PMO, CEO, AUDITOR);
// steps (read) PMO, CEO; retry and steps/enable PMO only.
const ag = "agents/";

type Method = "get" | "post";
async function fcall(method: Method, url: string, body?: any, params?: any): Promise<[any, null] | [null, Error | any]> {
  const [data, error] = await call(method, url, body, params);
  return error ? [null, error] : [camelize(data), null];
}
const get = (url: string, params?: any) => fcall("get", url, undefined, clean(params));
const post = (url: string, body?: any) => fcall("post", url, body);

export type AgentPageParams = { PageNumber?: number; PageSize?: number; [key: string]: any };

/**
 * GET agents/inbox?Kind&Station&ContractId&PageNumber&PageSize. Kind: 2 HumanGate (waits for a decision), 3 Physical, 4 Input (also the tasks an agent that is off left to people), 1 Agent (only those fallback tasks).
 * Returns a paged list of AgentInboxItemDto { id, stepCode, station, stationTitle, agentCode, kind, state, title, targetType, targetId, targetNumber, contractId, summary, outputType, outputNumber,
 * gateRoles, link, createdAt, createdAtText, ageHours, dueAt, overdue, isFallback, lastError, returnReason }. Who: every signed-in person (a seat is refused).
 */
export const getAgentInbox = (params?: AgentPageParams) => get(ag + "inbox", params);
/** AgentInboxCountsDto { humanGate, physical, input, total, overdue }. */
export const getAgentInboxCounts = () => get(ag + "inbox/counts");
/** AgentReturnDto { id, reason (required) }. Sends the task back to the agent. Returns the AgentInboxItemDto. A refusal carries the Persian reason. */
export const returnAgentTask = (dto: { id: number; reason: string }) => post(ag + "inbox/return", { id: dto.id, reason: dto.reason });

/** GET agents/tasks?State&Agent&Station&ContractId&StepCode&OpenOnly&PageNumber&PageSize. Paged AgentTaskDto (the inbox item plus workOrderId, attemptCount, startedAt, closedAt, ...). Who: PMO, CEO, AUDITOR. */
export const getAgentTasks = (params?: AgentPageParams) => get(ag + "tasks", params);
/** AgentTaskDto with events [{ id, at, atText, fromState, toState, userId, seatCode, note }] in time order. Who: PMO, CEO, AUDITOR. */
export const getAgentTask = (id: number) => get(ag + "tasks/" + id);
/** AgentRetryDto { id }. A failed task goes back to the queue. Returns the AgentTaskDto. Who: PMO. */
export const retryAgentTask = (id: number) => post(ag + "tasks/retry", { id });
/** AgentBoardDto { generatedAt, stations: [{ number, title }], rows: [{ contractId, label, cells: [{ station, status, openCount, waitingOn, oldestHours, taskId, title }] }], stuck: [AgentStuckDto] }. Who: PMO, CEO, AUDITOR. */
export const getAgentBoard = (contractId?: number | null) => get(ag + "board", { contractId });
/** List of AgentStepDto { code, station, stationTitle, agentCode, title, kind, fallbackRoles, enabled, seatEnabled, maxAttempts, gateDueHours, openTasks }. Who: PMO, CEO. */
export const getAgentSteps = () => get(ag + "steps");
/** AgentStepEnableDto { code, enabled }. Returns the AgentStepDto. Who: PMO. */
export const enableAgentStep = (code: string, enabled: boolean) => post(ag + "steps/enable", { code, enabled });
