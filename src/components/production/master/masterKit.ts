import { getItems, getProviders } from "@/api/InventoryAPIs";
import { getAssets } from "@/api/FnAPIs";
import { getCalendars } from "@/api/ProjectControlAPIs";
import { getWorkCenters } from "@/api/ProductionAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// Small lookups shared by the master-data and order components of the production pages (everything returns plain arrays; an error gives an empty list).

/** Type-ahead source of LookupBox for items. */
export async function searchItems(text: string): Promise<any[]> {
  const [data] = await getItems({ Contains: text, PageSize: 15, IsActive: true });
  return rowsOf(camelize(data));
}
export const itemLabel = (i: any) => (i ? `${i.code} - ${i.title}` : "");

/** Type-ahead source for fixed assets (the asset behind a machine). */
export async function searchAssets(text: string): Promise<any[]> {
  const [data] = await getAssets({ Contains: text, PageSize: 15 });
  return rowsOf(camelize(data));
}
export const assetLabel = (a: any) => (a ? `${a.code} - ${a.title}` : "");

/** Active work centres (the first 500). */
export async function loadWorkCenters(activeOnly = true): Promise<any[]> {
  const [data] = await getWorkCenters({ PageSize: 500, IsActive: activeOnly ? true : null });
  return rowsOf(camelize(data));
}

/** Scheduling calendars of project control (may be refused for a production role: then the list is empty and the calendar stays optional). */
export async function loadCalendars(): Promise<any[]> {
  const [data] = await getCalendars();
  return rowsOf(camelize(data));
}

/** Providers (contractors for outside operations). */
export async function loadProviders(): Promise<any[]> {
  const [data] = await getProviders();
  return rowsOf(camelize(data));
}

/** Text or null: an empty or blank field is sent as null. */
export const blankToNull = (v: any) => (v === undefined || v === null || String(v).trim() === "" ? null : String(v).trim());

/** Item groups of the warehouse module (flat list with code and title). */
export async function loadGroups(): Promise<any[]> {
  const { getGroups } = await import("@/api/InventoryAPIs");
  const [data] = await getGroups();
  return rowsOf(camelize(data));
}
