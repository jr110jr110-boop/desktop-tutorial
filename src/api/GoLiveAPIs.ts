import { call, http } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// Package G (go-live cut-over): api/v1/go-live. Every answer is { success, message, data }; a refused call answers HTTP 200 with success=false (the helpers return it as the error).
async function fcall(method: "get" | "post", url: string, body?: any): Promise<[any, null] | [null, any]> {
  const [data, error] = await call(method, "go-live/" + url, body);
  return error ? [null, error] : [camelize(data), null];
}

export const getGoLiveOverview = () => fcall("get", "overview");
export const getRevisionPreCheck = () => fcall("get", "edms-revision-precheck");
export const getCeoInbox = () => fcall("get", "ceo-inbox");
export const getStalePoLines = () => fcall("get", "po-stale-lines");
export const confirmPoLine = (dto: { lineId: number; reference: string; note?: string }) => fcall("post", "po-confirm", dto);

export const getPoUncitedLines = (q?: { search?: string; pageNumber?: number; pageSize?: number }) => fcall("get", "po-uncited-lines" + qs(q));
export const getDriftRehearsal = () => fcall("get", "drift-rehearsal");

function qs(q?: { search?: string; pageNumber?: number; pageSize?: number }) {
  if (!q) return "";
  const p = new URLSearchParams();
  if (q.search) p.set("Search", q.search);
  if (q.pageNumber) p.set("PageNumber", String(q.pageNumber));
  if (q.pageSize) p.set("PageSize", String(q.pageSize));
  const t = p.toString();
  return t ? "?" + t : "";
}

/** The candidate lists are paged and searchable: { total, pageNumber, pageSize, items }. */
export const getCharCandidates = (q?: { search?: string; pageNumber?: number; pageSize?: number }) => fcall("get", "char-adoptions/candidates" + qs(q));
export const getCharAdoptions = () => fcall("get", "char-adoptions");
export const requestCharAdoption = (dto: any) => fcall("post", "char-adoptions", dto);
/** action: accept | void */
export const charAdoptionAction = (dto: { id: number; action: string; note?: string; rowVersion?: string }) => fcall("post", "char-adoptions/action", dto);

export const getConfigCandidates = (q?: { search?: string; pageNumber?: number; pageSize?: number }) => fcall("get", "config-adoptions/candidates" + qs(q));
export const getConfigAdoptions = () => fcall("get", "config-adoptions");
export const requestConfigAdoption = (dto: any) => fcall("post", "config-adoptions", dto);
/** action: eng-review | pmo-review | void */
export const configAdoptionAction = (dto: { id: number; action: string; note?: string; rowVersion?: string }) => fcall("post", "config-adoptions/action", dto);

// ---- the controlled printed copy (fix round 1): the SERVER makes it. The browser asks for a print of a revision (the server checks that the revision is in force, numbers the copy, records the print and builds the text), draws the
// Persian text in the programme's font and sends only the two small bands back; the server loads the file of the revision from the store and adds the bands to every page. A PDF chosen by the user is never stamped.
export async function loadStampFont(): Promise<void> {
  try {
    await Promise.all([document.fonts.load('bold 28px "B Zar"'), document.fonts.load('28px "B Zar"')]);
  } catch {
    /* the band then uses the next font of the list */
  }
}

function renderBand(text: string, width: number, height: number, background: string, color: string): Blob {
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const g = canvas.getContext("2d")!;
  g.fillStyle = background;
  g.fillRect(0, 0, width, height);
  g.fillStyle = color;
  g.direction = "rtl";
  g.textAlign = "right";
  g.textBaseline = "middle";
  // the text is never squeezed: the font is made smaller until the line fits
  let size = Math.round(height * 0.5);
  const maxWidth = width - 24;
  g.font = `bold ${size}px "B Zar", Tahoma, sans-serif`;
  while (size > 8 && g.measureText(text).width > maxWidth) {
    size -= 1;
    g.font = `bold ${size}px "B Zar", Tahoma, sans-serif`;
  }
  g.fillText(text, width - 12, height / 2);
  const rgba = g.getImageData(0, 0, width, height).data;
  return new Blob([rgba.buffer.slice(rgba.byteOffset, rgba.byteOffset + rgba.byteLength) as ArrayBuffer]);
}

export const preparePrint = (dto: { revisionId: number; documentId?: string; uncontrolled?: boolean }) => fcall("post", "print/prepare", dto);

/** Returns the stamped PDF, or null when the server cannot stamp it directly (a rotated page, a big file, a structure it does not read, a stamp already running): the page then shows the HTML stamp over the file. */
export async function stampPrint(printId: number, header: string, footer: string, controlled: boolean): Promise<Blob | null> {
  await loadStampFont();
  const hw = 1200, hh = 56, fw = 1200, fh = 40;
  const form = new FormData();
  const back = controlled ? "#fff4f4" : "#f2f2f2";
  const color = controlled ? "#b00020" : "#333333";
  form.append("printId", String(printId));
  form.append("headerRgba", renderBand(header, hw, hh, back, color));
  form.append("footerRgba", renderBand(footer, fw, fh, back, color));
  form.append("headerWidth", String(hw));
  form.append("headerHeight", String(hh));
  form.append("footerWidth", String(fw));
  form.append("footerHeight", String(fh));
  try {
    const res = await http.post("go-live/print/stamp", form, { responseType: "blob" });
    // a refusal that is not an error status (the print is not yours, already stamped...) comes back as JSON
    if ((res.data as Blob).type && (res.data as Blob).type.includes("json")) return null;
    return res.data as Blob;
  } catch {
    return null;
  }
}
