import { call, clean } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// Production release P2 part D: the planning seat's plan (api/v1/production/dispatch/plan), the shop-floor overview (dispatch/floor) and the remote-access status (api/v1/remote/status).
// Read-only. Same conventions as the other production APIs: [data, null] or [null, error]; "...Text" fields are Jalali.
async function get(url: string, params?: any): Promise<[any, null] | [null, any]> {
  const [data, error] = await call("get", url, undefined, clean(params));
  return error ? [null, error] : [camelize(data), null];
}

/** DispatchPlanDto { asOfText, generatedAtText, horizonDays, openOrders, lateOrders, lateRiskOperations, bottleneckText, centers: DispatchCenterDto[], suggestions[], method }. */
export const getDispatchPlan = (params?: { WorkCenterId?: number | null; HorizonDays?: number | null }) => get("production/dispatch/plan", params);
/** FloorOverviewDto { asOfText, greetingText, tiles[], live: LiveBoardDto, waiting[], suggestions[], centers[], bottleneckText, remote }. */
export const getFloor = () => get("production/dispatch/floor");
/** RemoteStatusDto { enabled, remote, readOnly, allowed, text }. */
export const getRemoteStatus = () => get("remote/status");
