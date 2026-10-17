import { call, clean, postForFile } from "@/api/ApiClient";

// Cost accounting and the Sepidar link (api/v1/finance/...).
const base = "finance/";

export const getAccountingEvents = (params: any) => call("get", base + "events", undefined, clean(params));
export const getFinanceCheck = () => call("get", base + "check");

/** Downloads the voucher file (CSV) of the events. Returns an error message, or null when the file was saved. */
export const exportAccountingEvents = (eventIds: number[], markExported: boolean): Promise<string | null> =>
  postForFile(base + "events/export", { eventIds, markExported }, "sepidar-vouchers.csv");

/** status: 0 back to the queue, 1 sent, 3 exported by file. */
export const markAccountingEvents = (eventIds: number[], status: number, externalReference?: string) =>
  call("post", base + "events/mark", { eventIds, status, externalReference: externalReference || null });

export const getAccountMappings = () => call("get", base + "accounts");
export const saveAccountMapping = (dto: any) => call("post", base + "accounts", dto);
export const getCostCenters = () => call("get", base + "cost-centers");
export const saveCostCenter = (dto: any) => call("post", base + "cost-centers", dto);
export const getWorkOrderCosts = (contractId?: number | null) => call("get", base + "costs", undefined, clean({ contractId }));
export const getValuation = (contains?: string) => call("get", base + "valuation", undefined, clean({ contains }));

/** CEO dashboard: stock, projects (price, cost, commitments), procurement, payables, quality, Sepidar link. */
export const getCeoDashboard = () => call("get", base + "ceo-dashboard");
