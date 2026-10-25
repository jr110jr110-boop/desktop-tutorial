import { getSlCustomers, getSlPriceLists } from "@/api/SalesAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// Small helpers shared by the price list, quotation and sales order pages (lookups return plain arrays; an error gives an empty list).

/** Type-ahead source of LookupBox for customers. */
export async function searchCustomers(text: string): Promise<any[]> {
  const [data] = await getSlCustomers({ Contains: text, PageSize: 15 });
  return rowsOf(camelize(data));
}
export const customerLabel = (c: any) => (c ? c.name : "");

/** yyyy-mm-dd of an ISO date or date-time (what FnDate and the API take). */
export function isoDay(value: string | null | undefined): string | null {
  return value ? String(value).slice(0, 10) : null;
}

/** Today as yyyy-mm-dd in local time. */
export function todayIso(): string {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/** Amount of a document line (the server recomputes it): quantity x unit price less the discount percent. */
export function lineAmount(l: { quantity: any; unitPrice: any; discountPercent: any }): number {
  const q = Number(l.quantity) || 0;
  const p = Number(l.unitPrice) || 0;
  const d = Number(l.discountPercent) || 0;
  return Math.round(q * p * (1 - d / 100));
}

/** Approved price lists with their lines (valid on any date; the caller filters by date). */
export async function loadApprovedPriceLists(): Promise<any[]> {
  const [data] = await getSlPriceLists({ Status: 2, PageSize: 100 });
  return rowsOf(camelize(data));
}

/**
 * Price of an item from approved price lists on a date: the line with the highest min quantity not above the asked quantity.
 * priceListId limits the search to one list; empty searches all lists valid on the date. Returns null when none has the item.
 */
export function priceFromLists(lists: any[], itemId: number, quantity: number, date: string, priceListId?: number | null): { price: number; priceListNumber: string } | null {
  let best: { price: number; min: number; priceListNumber: string } | null = null;
  for (const pl of lists) {
    if (priceListId && pl.id !== priceListId) continue;
    const from = isoDay(pl.validFrom);
    const to = isoDay(pl.validTo);
    if ((from && from > date) || (to && to < date)) continue;
    for (const l of pl.lines || []) {
      if (l.itemId !== itemId || Number(l.minQuantity) > quantity) continue;
      if (!best || Number(l.minQuantity) > best.min) best = { price: Number(l.unitPrice), min: Number(l.minQuantity), priceListNumber: pl.number };
    }
  }
  return best ? { price: best.price, priceListNumber: best.priceListNumber } : null;
}
