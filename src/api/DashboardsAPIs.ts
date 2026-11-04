import { call, clean } from "@/api/ApiClient";

// Management dashboards (api/v1/dashboards): read only; the API decides which dashboards a role sees and hides the money from the roles without price access.
const base = "dashboards";

export const getDashboardIndex = () => call("get", base);
export const getDashboard = (key: string, contractId?: number | null) => call("get", `${base}/${key}`, undefined, clean({ contractId }));
export const getPortfolioEvm = (asOf?: string | null) => call("get", `${base}/portfolio-evm`, undefined, clean({ asOf }));
/** period: week | month */
export const getCommitments = (params: any) => call("get", `${base}/commitments`, undefined, clean(params));
export const getDelayCauses = (params: any) => call("get", `${base}/delay-causes`, undefined, clean(params));
