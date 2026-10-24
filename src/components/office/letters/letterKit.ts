import { ref } from "vue";
import { getMyOffice, getPositions, getParties, getUnits } from "@/api/OfficeAPIs";
import { camelize } from "@/composables/fn";
import { fail } from "@/composables/inventory";

// Helpers shared by the letter pages: HTML sanitising for display, Persian digits, loading of positions / parties / units.

const ALLOWED = new Set(["p", "br", "b", "strong", "i", "em", "u", "ul", "ol", "li", "div", "span", "h3", "h4", "table", "thead", "tbody", "tr", "th", "td", "blockquote"]);
const KEEP_ATTR = new Set(["dir", "colspan", "rowspan", "align"]);

/** Whitelist sanitiser for letter HTML (the server sanitises too; this is the second line). Scripts, handlers, links and images are dropped. */
export function sanitizeHtml(html: string | null | undefined): string {
  if (!html) return "";
  const doc = new DOMParser().parseFromString("<body>" + html + "</body>", "text/html");
  const walk = (node: Node, out: HTMLElement) => {
    node.childNodes.forEach((child) => {
      if (child.nodeType === Node.TEXT_NODE) {
        out.appendChild(document.createTextNode(child.textContent || ""));
      } else if (child.nodeType === Node.ELEMENT_NODE) {
        const el = child as HTMLElement;
        const name = el.tagName.toLowerCase();
        if (["script", "style", "iframe", "object", "embed", "svg", "math", "form", "textarea", "select", "button", "noscript", "template"].includes(name)) return;
        if (!ALLOWED.has(name)) return walk(el, out);
        const copy = document.createElement(name);
        for (const attr of Array.from(el.attributes)) if (KEEP_ATTR.has(attr.name.toLowerCase())) copy.setAttribute(attr.name.toLowerCase(), attr.value);
        walk(el, copy);
        out.appendChild(copy);
      }
    });
  };
  const holder = document.createElement("div");
  walk(doc.body, holder);
  return holder.innerHTML;
}

/** Plain text of HTML (paragraph and line breaks become new lines). */
export function htmlToText(html: string | null | undefined): string {
  if (!html) return "";
  const doc = new DOMParser().parseFromString("<body>" + sanitizeHtml(html).replace(/<\/(p|div|li|tr|h3|h4)>/gi, "\n").replace(/<br\s*\/?>/gi, "\n") + "</body>", "text/html");
  return (doc.body.textContent || "").trim();
}

/** Latin digits of a text shown with Persian digits (print and display of numbers). */
export function fa(value: string | number | null | undefined): string {
  return String(value ?? "").replace(/\d/g, (d) => "۰۱۲۳۴۵۶۷۸۹"[Number(d)]);
}

export interface MyPosition { positionId: number; title: string; unitTitle?: string; onBehalf?: boolean; canSign?: boolean; isManager?: boolean }

/** The positions of the signed-in user (own and delegated). */
export async function loadMyPositions(): Promise<MyPosition[]> {
  const [data, error] = await getMyOffice();
  if (error) {
    fail(error);
    return [];
  }
  return (data?.positions || []) as MyPosition[];
}

async function loadAll(fetcher: (p: any) => Promise<any>, params: any = {}): Promise<any[]> {
  const rows: any[] = [];
  for (let page = 1; page <= 20; page++) {
    const [data, error] = await fetcher({ ...params, PageNumber: page, PageSize: 200 });
    if (error) {
      fail(error);
      break;
    }
    const d = camelize<any>(data);
    const part: any[] = Array.isArray(d) ? d : d?.data || [];
    rows.push(...part);
    if (Array.isArray(d) || rows.length >= (d?.totalItems ?? 0) || part.length === 0) break;
  }
  return rows;
}

export const loadAllPositions = (params: any = {}) => loadAll(getPositions, params);
export const loadAllParties = (params: any = {}) => loadAll(getParties, params);
export const loadAllUnits = (params: any = {}) => loadAll(getUnits, params);

/** Text of a position in a select: title and unit. */
export const positionText = (p: { title?: string; unitTitle?: string } | null | undefined) => (p ? (p.unitTitle ? `${p.title} - ${p.unitTitle}` : p.title || "") : "");

/** Shared reactive lists of a page (positions, parties). */
export function useDirectory() {
  const positions = ref<any[]>([]);
  const parties = ref<any[]>([]);
  async function loadPositions() {
    positions.value = (await loadAllPositions()).filter((p) => p.isActive !== false);
  }
  async function loadParties() {
    parties.value = (await loadAllParties()).filter((p) => p.isActive !== false);
  }
  return { positions, parties, loadPositions, loadParties };
}
