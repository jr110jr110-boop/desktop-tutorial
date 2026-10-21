import { getAccounts, getDetailAccounts } from "@/api/FnAPIs";

// Search functions and labels for the LookupBox pickers of accounts and detail accounts.
const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);
export const searchAccounts = async (t: string) => rowsOf((await getAccounts({ Level: 3, Contains: t, PageSize: 15 }))[0]);
export const searchDetails = async (t: string) => rowsOf((await getDetailAccounts({ Contains: t, PageSize: 15 }))[0]);
export const accountLabel = (a: any) => `${a.code} - ${a.title}`;
