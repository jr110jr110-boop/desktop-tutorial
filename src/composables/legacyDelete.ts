// What the user is told after a legacy delete (the generic list component, KsList, for contracts, work orders, providers, equipment ...).
//
// The legacy delete-collection calls answer HTTP 200 with { success, message, data: [{ id, success, message }] }, one row per id:
// - a row the API refuses (the delete guard names what is in the way: invoices, orders, stock ...) has success=false and that Persian text;
// - a row that WAS deleted can come back with success=false and a "not found" text too, because the answer is built by reading the record again
//   after the soft delete (ContractService, ProviderService, WorkOrderService, EquipmentService of the API).
// The row texts alone cannot tell the two apart, so the list is read again first: a row that is gone is deleted whatever its text says,
// and a row that is still on the list was refused, so the text the API wrote for it is shown.

export interface DeleteReport {
  /** How many of the asked rows are gone. */
  deleted: number;
  /** Persian texts to show as errors (empty when everything asked was deleted). */
  errors: string[];
}

const NO_TEXT = "حذف انجام نشد؛ توضیحی از سرور دریافت نشد.";
const ROW_REFUSED = "حذف یکی از موارد انتخاب‌شده انجام نشد.";
const MAX_TEXTS = 3;

/** The ids of all rows of a list, the children of a tree list included. */
export function collectIds(items: any[] | null | undefined, childrenProp: string | null = null, into: Set<any> = new Set()): Set<any> {
  (items || []).forEach((item) => {
    if (!item) return;
    into.add(item.id);
    const children = childrenProp ? item[childrenProp] : null;
    if (Array.isArray(children) && children.length) collectIds(children, childrenProp, into);
  });
  return into;
}

const text = (value: unknown) => (typeof value === "string" ? value.trim() : "");

/**
 * Decides what to tell after a delete call that was answered.
 * @param ids     the ids that were asked to be deleted
 * @param answer  the body of the answer ({ success, message, data: [...] })
 * @param listed  the ids that are on the list after it was read again; null when it could not be read
 */
export function legacyDeleteReport(ids: any[], answer: any, listed: Set<any> | null): DeleteReport {
  const rows: any[] = Array.isArray(answer?.data) ? answer.data : [];
  const topFailure = answer?.success === false;
  const rowText = (id: any) => text(rows.find((r) => r && String(r.id) === String(id))?.message);

  let refused: any[];
  if (listed !== null) {
    // the list is the truth: what is still on it was not deleted
    refused = ids.filter((id) => listed.has(id));
  } else if (topFailure) {
    // nothing could be checked and the whole request was refused
    return { deleted: 0, errors: [text(answer?.message) || NO_TEXT] };
  } else {
    // the list could not be read: the rows that report success=false are taken as refused
    refused = rows.filter((r) => r && r.success === false).map((r) => r.id);
  }
  if (!refused.length) return { deleted: ids.length, errors: [] };

  const texts: string[] = [];
  refused.forEach((id) => {
    const message = rowText(id) || (topFailure ? text(answer?.message) : "") || ROW_REFUSED;
    if (!texts.includes(message)) texts.push(message);
  });
  const errors = texts.slice(0, MAX_TEXTS);
  if (texts.length > MAX_TEXTS) errors.push("و " + (texts.length - MAX_TEXTS) + " توضیح دیگر هم هست؛ موارد باقی‌مانده را یکی‌یکی حذف کنید.");
  return { deleted: Math.max(ids.length - refused.length, 0), errors };
}
