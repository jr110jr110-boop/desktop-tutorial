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

export const getCharCandidates = () => fcall("get", "char-adoptions/candidates");
export const getCharAdoptions = () => fcall("get", "char-adoptions");
export const requestCharAdoption = (dto: any) => fcall("post", "char-adoptions", dto);
/** action: accept | void */
export const charAdoptionAction = (dto: { id: number; action: string; note?: string; rowVersion?: string }) => fcall("post", "char-adoptions/action", dto);

export const getConfigCandidates = () => fcall("get", "config-adoptions/candidates");
export const getConfigAdoptions = () => fcall("get", "config-adoptions");
export const requestConfigAdoption = (dto: any) => fcall("post", "config-adoptions", dto);
/** action: eng-review | pmo-review | void */
export const configAdoptionAction = (dto: { id: number; action: string; note?: string; rowVersion?: string }) => fcall("post", "config-adoptions/action", dto);

// ---- the controlled printed copy: the Persian stamp is drawn by the browser (B Zar, the font of the programme) and the server adds it to every page of the PDF as an image
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
  g.font = `bold ${Math.round(height * 0.5)}px "B Zar", Tahoma, sans-serif`;
  g.fillText(text, width - 12, height / 2, width - 24);
  const rgba = g.getImageData(0, 0, width, height).data;
  return new Blob([rgba.buffer.slice(rgba.byteOffset, rgba.byteOffset + rgba.byteLength) as ArrayBuffer]);
}

/** Returns the stamped PDF, or null when the PDF structure cannot be stamped directly (the page then shows the HTML stamp over the file). */
export async function stampPdf(pdf: Blob, header: string, footer: string): Promise<Blob | null> {
  const hw = 1200, hh = 56, fw = 1200, fh = 40;
  const form = new FormData();
  form.append("file", pdf, "in.pdf");
  form.append("headerRgba", renderBand(header, hw, hh, "#fff4f4", "#b00020"));
  form.append("footerRgba", renderBand(footer, fw, fh, "#fff4f4", "#b00020"));
  form.append("headerWidth", String(hw));
  form.append("headerHeight", String(hh));
  form.append("footerWidth", String(fw));
  form.append("footerHeight", String(fh));
  try {
    const { data } = await http.post("go-live/stamp-pdf", form, { responseType: "blob" });
    return data as Blob;
  } catch {
    return null;
  }
}
