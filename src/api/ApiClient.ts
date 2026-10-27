import axios, { type AxiosError, type AxiosInstance } from "axios";
import { refreshToken as requestRefresh } from "@/api/LoginAPIs";
import { clearSession, sessionExpiryMs, storeSession } from "@/composables/session";

// One axios client for the warehouse, finance and procurement APIs (api/v1/...).
// The token is read on every request (not once when the file loads), so a refreshed token is used at once.
// Every answer is { success, message, data }; the helpers return [data, null] or [null, error].

const apiUrl = import.meta.env.VITE_API_URL.toString();

// ------------------------------------------------------------------------------------------ the session: silent refresh
//
// The access token of the API lives 60 minutes, the refresh token 7 days, and a refresh token works once.
// installAuth() puts two interceptors on an axios instance:
//   1. before a request: when the access token ends within 5 minutes (or has ended) the session is refreshed first;
//   2. after a 401 answer: the session is refreshed once and the request is sent once more.
// Requests share one refresh at a time, and so do the tabs of the browser (Web Locks), because the refresh token works only once.
// A refresh the API refuses ends the session (storage cleared, login page, the usual notice). A refresh that could not reach the
// API keeps the session and is not tried again for a while, so a stopped server does not get a storm of refresh calls.

/** The access token is renewed when it ends within this time. */
const REFRESH_WINDOW_MS = 5 * 60 * 1000;
/** After a refresh no other refresh is started for this time because of the expiry date alone (a wrong clock of the PC must not refresh on every request). */
const MIN_REFRESH_GAP_MS = 60 * 1000;
/** After a refresh that could not reach the API nothing is tried for this time. */
const UNAVAILABLE_COOLDOWN_MS = 30 * 1000;
/** A 401 right after a refresh does not start another one: the fresh token was accepted and the request is refused for another reason. */
const RECENT_REFRESH_MS = 10 * 1000;
/** Failing requests that arrive together end the session once. */
const SESSION_END_GUARD_MS = 3000;
/** A refused refresh is checked again this many times, this far apart, before the session ends (see deniedUnlessRenewedElsewhere). */
const DENIED_LOOKS = 4;
const DENIED_LOOK_GAP_MS = 250;

/**
 * ok = new tokens are stored (or another tab stored them); denied = the API refused the refresh token;
 * unavailable = the API could not be reached or failed; none = there is no refresh token to try.
 */
export type RefreshOutcome = "ok" | "denied" | "unavailable" | "none";

let inflight: Promise<RefreshOutcome> | null = null;
let lastRefreshAt = 0;
let blockedUntil = 0;
let endingSession = false;
let sessionExpiredHandler: (() => void) | null = null;

/** main.ts registers what the user sees when the session ends (login page and notice); the router is not imported here to avoid a cycle. */
export function setSessionExpiredHandler(handler: (() => void) | null): void {
  sessionExpiredHandler = handler;
}

/** The session is over (the API refused the refresh token): the keys are removed and the user is taken to the login page once. */
export function endSession(): void {
  clearSession();
  if (endingSession) return;
  endingSession = true;
  setTimeout(() => {
    endingSession = false;
  }, SESSION_END_GUARD_MS);
  if (sessionExpiredHandler) sessionExpiredHandler();
  else window.location.assign("/login");
}

/** True when the access token ends within five minutes (or has ended) and a refresh may be started now. */
export function sessionNeedsRefresh(): boolean {
  try {
    if (!localStorage.getItem("token") || !localStorage.getItem("refreshToken")) return false;
  } catch {
    return false;
  }
  const end = sessionExpiryMs();
  const now = Date.now();
  if (end === null || end - now > REFRESH_WINDOW_MS) return false;
  return now - lastRefreshAt >= MIN_REFRESH_GAP_MS && now >= blockedUntil;
}

function unavailable(): RefreshOutcome {
  blockedUntil = Date.now() + UNAVAILABLE_COOLDOWN_MS;
  return "unavailable";
}

/**
 * The API refused the refresh token. A refresh token works once, so when two tabs refresh at the same moment (and the browser has no Web Locks,
 * which is the case for a page opened over plain http on the company network) one of them is refused although the user is fine: the other tab
 * has the new pair and stores it a moment later. So the storage is looked at for a short time before the session is ended.
 */
async function deniedUnlessRenewedElsewhere(sent: string): Promise<RefreshOutcome> {
  for (let look = 0; look < DENIED_LOOKS; look++) {
    const stored = localStorage.getItem("refreshToken");
    if (!stored) return "denied"; // signed out in another tab
    if (stored !== sent) {
      lastRefreshAt = Date.now();
      return "ok";
    }
    if (look < DENIED_LOOKS - 1) await new Promise((resolve) => setTimeout(resolve, DENIED_LOOK_GAP_MS));
  }
  return "denied";
}

async function refreshOnce(before: string): Promise<RefreshOutcome> {
  const current = localStorage.getItem("refreshToken");
  if (!current) return "none"; // another tab signed out while this one waited
  if (current !== before) {
    // another tab refreshed while this one waited: its tokens are in the storage
    lastRefreshAt = Date.now();
    return "ok";
  }

  const [data, error] = (await requestRefresh(current)) as [any, any];

  if (error) {
    // 400 / 401 / 403 = the API will not take this refresh token; no answer, 5xx, 429 and the like = try again later
    const status = error?.response?.status;
    return status === 400 || status === 401 || status === 403 ? deniedUnlessRenewedElsewhere(current) : unavailable();
  }

  if (data && typeof data === "object") {
    if (data.success === true && data.data?.accessToken) {
      storeSession(data.data);
      lastRefreshAt = Date.now();
      return "ok";
    }
    // the API answers HTTP 200 with success=false for an unknown or used refresh token, a deactivated account, a must-change-password account
    if (data.success === false) return deniedUnlessRenewedElsewhere(current);
  }
  return unavailable();
}

async function runRefresh(): Promise<RefreshOutcome> {
  const before = localStorage.getItem("refreshToken");
  if (!before) return "none";
  const locks = (navigator as any).locks;
  if (locks && typeof locks.request === "function") return locks.request("pmis-token-refresh", () => refreshOnce(before));
  return refreshOnce(before);
}

/** Renews the session with the refresh token. Callers at the same time share one call; a refusal ends the session here. Never rejects. */
export function refreshSession(): Promise<RefreshOutcome> {
  if (!inflight) {
    inflight = runRefresh()
      .catch((): RefreshOutcome => unavailable())
      .then((outcome) => {
        if (outcome === "denied" || outcome === "none") endSession();
        return outcome;
      })
      .finally(() => {
        inflight = null;
      });
  }
  return inflight;
}

// sign-in style calls carry no bearer token and must never trigger a refresh
const AUTH_URL = /accounts\/(login|refresh-token|change-temporary-password)(\?|#|$)/i;

/** The address of our API (the base of every call), resolved against the page so a relative VITE_API_URL works too. */
const apiBase = (() => {
  try {
    return new URL(apiUrl, window.location.href).href.replace(/\/+$/, "");
  } catch {
    return apiUrl.replace(/\/+$/, "");
  }
})();

/** True when the request goes to our API (relative to its base address or an absolute address inside it): other addresses never get the token. */
function isOwnApi(config: any): boolean {
  try {
    const url = String(config?.url || "");
    const full = /^https?:\/\//i.test(url) ? url : String(config?.baseURL || "") + url;
    return new URL(full, window.location.href).href.startsWith(apiBase);
  } catch {
    return false;
  }
}

const installed = new WeakSet<object>();

/**
 * Puts the token and the silent refresh on an axios instance of the app (safe to call twice for the same instance).
 * The bearer header is set from the storage on every request; it replaces the token that older API files froze when they were loaded.
 */
export function installAuth<T extends AxiosInstance>(instance: T): T {
  if (installed.has(instance)) return instance;
  installed.add(instance);

  instance.interceptors.request.use(async (config) => {
    if (!isOwnApi(config) || AUTH_URL.test(String(config.url || ""))) return config;
    if (sessionNeedsRefresh()) await refreshSession();
    const token = localStorage.getItem("token");
    if (token) {
      config.headers = config.headers || {};
      config.headers["Authorization"] = "Bearer " + token;
    }
    return config;
  });

  instance.interceptors.response.use(undefined, async (error: AxiosError) => {
    const config: any = error?.config;
    if (error?.response?.status !== 401 || !config) throw error;
    if (!isOwnApi(config) || AUTH_URL.test(String(config.url || "")) || config.__authRetried) throw error;

    // the token this request carried; when the storage holds another one, a request or a tab has renewed the session since: send it again
    const sent = String(config.headers?.Authorization || config.headers?.authorization || "").replace(/^Bearer\s+/i, "");
    const stored = localStorage.getItem("token") || "";
    const renewedElsewhere = !!stored && !!sent && stored !== sent;

    if (!renewedElsewhere) {
      // no new attempt while the API is unreachable, and none right after a refresh: the fresh token was taken and this request is refused for another reason
      if (Date.now() < blockedUntil || Date.now() - lastRefreshAt < RECENT_REFRESH_MS) throw error;
    }

    const outcome: RefreshOutcome = renewedElsewhere ? "ok" : await refreshSession();
    if (outcome !== "ok") throw error;

    // the body was already turned into text by the first attempt; axios sends a text body as it is
    return instance.request({ ...config, __authRetried: true });
  });

  return instance;
}

export const http = installAuth(axios.create({ baseURL: apiUrl + "api/v1/" }));

/** For callers that build the whole address themselves (the generic list component). */
export const absoluteHttp = installAuth(axios.create());

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
    if (error?.response?.status === 401) return "دوباره وارد سامانه شوید.";
    return error?.message || "خطا";
  }
}
