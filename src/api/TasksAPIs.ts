import { call, clean } from "@/api/ApiClient";

// Task management (api/v1/tasks). Every signed-in user has a cartable; the service decides what a person sees and does.
const base = "tasks";

/** cartable: mine, assigned-to-me, assigned-by-me, overdue, waiting, done, unit, watching, all. */
export const getTasks = (params?: any) => call("get", base, undefined, clean(params || {}));
export const getTask = (id: number) => call("get", base + "/" + id);
export const saveTask = (dto: any) => call("post", base, dto);
/** Action: start, complete, approve, return, reject, suspend, resume, reopen. */
export const taskAction = (dto: { id: number; action: string; note?: string }) => call("post", base + "/action", dto);
export const assignTask = (dto: any) => call("post", base + "/assign", dto);
export const transferTask = (dto: any) => call("post", base + "/transfer", dto);
export const withdrawTask = (dto: any) => call("post", base + "/withdraw", dto);
export const getTaskReferrals = (id: number) => call("get", base + "/" + id + "/referrals");
export const taskProgress = (dto: any) => call("post", base + "/progress", dto);
/** Action: add, toggle, remove. */
export const taskChecklist = (dto: any) => call("post", base + "/checklist", dto);
export const taskComment = (dto: any) => call("post", base + "/comment", dto);
export const watchTask = (id: number, watch: boolean) => call("post", base + "/" + id + "/watch", undefined, { watch });
export const attachToTask = (dto: any) => call("post", base + "/attach", dto);
export const voidTaskAttachment = (dto: any) => call("post", base + "/attach/void", dto);
export const getTaskBoard = (params?: any) => call("get", base + "/board", undefined, clean(params || {}));
/** Mode: day, week, month. */
export const getTaskAgenda = (params?: any) => call("get", base + "/agenda", undefined, clean(params || {}));
export const getTasksByActivity = (activityId: number) => call("get", base + "/by-activity/" + activityId);
export const getTaskSummary = (contractId?: number | null) => call("get", base + "/summary", undefined, clean({ contractId }));
export const getTaskCounts = () => call("get", base + "/counts");
export const tasksFromDecision = (decisionId: number) => call("post", base + "/from-decision/" + decisionId);
export const runTaskReminders = () => call("post", base + "/reminders/run");
