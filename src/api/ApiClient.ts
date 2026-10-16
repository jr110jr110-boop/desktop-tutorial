import axios, { type AxiosError } from "axios";

// One axios client for the warehouse, finance and procurement APIs (api/v1/...).
// The token is read on every request (not once when the file loads), so a refreshed token is used at once.
// Every answer is { success, message, data }; the helpers return [data, null] or [null, error].

const apiUrl = import.meta.env.VITE_API_URL.toString();

export const http = axios.create({ baseURL: apiUrl + "api/v1/" });
http.interceptors.request.use((config) => {
  const token = window.localStorage.getItem("token");
  if (token) {
    config.headers = config.headers || {};
    config.headers["Authorization"] = "Bearer " + token;
  }
  return config;
});

export type Result = [any, null] | [null, AxiosError | Error];

export async function call(method: "get" | "post" | "put" | "delete", url: string, body?: any, params?: any): Promise<Result> {
  try {
    const { data } = await http.request({ method, url, data: body, params });
    if (data && data.success === false) return [null, new Error(data.message || "خطا")];
    return [data && "data" in data ? data.data : data, null];
  } catch (error: any) {
    if (error?.response?.status === 403) return [null, new Error("شما اجازه این کار را ندارید.")];
    if (error?.response?.status === 401) return [null, new Error("دوباره وارد سامانه شوید.")];
    return [null, error];
  }
}

/** Drops empty values so they are not sent as query parameters. */
export function clean(params: any) {
  const result: any = {};
  Object.keys(params || {}).forEach((k) => {
    const v = params[k];
    if (v !== null && v !== undefined && v !== "") result[k] = v;
  });
  return result;
}

export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 2000);
}

/** Paged API answers carry { data: [...], totalItems, ... }; plain lists come back as arrays. */
export function rowsOf(value: any): any[] {
  if (Array.isArray(value)) return value;
  if (value && Array.isArray(value.data)) return value.data;
  return [];
}

/**
 * Posts a request whose answer is a file (CSV). Returns an error message, or null when the file was saved.
 * When the API refuses it answers JSON { success: false, message } instead of the file.
 */
export async function postForFile(url: string, body: any, defaultName: string): Promise<string | null> {
  try {
    const response = await http.post(url, body, { responseType: "blob" });
    const type = response.headers["content-type"] || "";
    if (type.includes("application/json")) {
      const text = await (response.data as Blob).text();
      const json = JSON.parse(text);
      return json.message || "خطا";
    }
    const disposition = response.headers["content-disposition"] || "";
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    saveBlob(response.data, match ? decodeURIComponent(match[1]) : defaultName);
    return null;
  } catch (error: any) {
    if (error?.response?.status === 403) return "شما اجازه این کار را ندارید.";
    return error?.message || "خطا";
  }
}
