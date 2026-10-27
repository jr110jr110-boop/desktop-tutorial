// The browser session of the signed-in user, and the password rule of the company.
//
// The session is five keys of localStorage (the names the first version of the app used; other code reads them):
//   token            access token (JWT, 60 minutes)
//   refreshToken     refresh token (7 days, works once)
//   tokenExpireDate  "exp" of the access token, seconds since the epoch
//   userRole         role claim of the access token (the first one is the role the UI uses, see composables/roles.ts)
//   accessList       claims of the roles, comma separated (only the sign-in answers carry it, a refresh keeps the stored list)
// Everything that starts or ends a session goes through storeSession() and clearSession(), so the keys always change together.
// Other keys of the storage (theme, selected project, ...) are not touched.

const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

const SESSION_KEYS = ["token", "refreshToken", "tokenExpireDate", "userRole", "accessList"];

/** What the sign-in, refresh, change-password and change-temporary-password answers carry (ResponseLoginUserDto of the API). */
export interface SessionTokens {
  accessToken: string;
  refreshToken?: string | null;
  /** only the sign-in style answers have it */
  accessList?: string[] | string | null;
}

function payloadOf(token: string | null | undefined): any | null {
  try {
    const part = (token || "").split(".")[1];
    if (!part) return null;
    const json = decodeURIComponent(
      atob(part.replace(/-/g, "+").replace(/_/g, "/"))
        .split("")
        .map((c) => "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2))
        .join("")
    );
    return JSON.parse(json);
  } catch {
    return null;
  }
}

/** The expiry (seconds since the epoch) and the role claim of an access token; exp is null when the token cannot be read. */
export function readToken(token: string | null | undefined): { exp: number | null; role: string } {
  const payload = payloadOf(token);
  const exp = payload ? Number(payload["exp"]) : NaN;
  const role = payload && payload[ROLE_CLAIM] != null ? String(payload[ROLE_CLAIM]) : "";
  return { exp: Number.isFinite(exp) && exp > 0 ? exp : null, role };
}

/** Keeps the tokens of a sign-in or a refresh the way the login page always kept them. */
export function storeSession(tokens: SessionTokens): void {
  const parsed = readToken(tokens.accessToken);
  localStorage.setItem("token", tokens.accessToken);
  if (tokens.refreshToken) localStorage.setItem("refreshToken", tokens.refreshToken);
  if (parsed.exp) localStorage.setItem("tokenExpireDate", String(parsed.exp));
  else localStorage.removeItem("tokenExpireDate");
  localStorage.setItem("userRole", parsed.role);
  if (tokens.accessList !== undefined && tokens.accessList !== null) localStorage.setItem("accessList", String(tokens.accessList));
}

/** Ends the session of this browser (the five keys only). */
export function clearSession(): void {
  try {
    SESSION_KEYS.forEach((key) => localStorage.removeItem(key));
  } catch {
    /* storage not available: nothing was kept */
  }
}

/** When the stored access token ends, in milliseconds since the epoch; null when it is unknown. */
export function sessionExpiryMs(): number | null {
  try {
    const stored = Number(localStorage.getItem("tokenExpireDate"));
    if (Number.isFinite(stored) && stored > 0) return stored * 1000;
    const exp = readToken(localStorage.getItem("token")).exp;
    return exp ? exp * 1000 : null;
  } catch {
    return null;
  }
}

/** Password rule of the API (PasswordPolicy in IdentityRules.cs): at least 10 characters with a letter and a digit. */
export const PASSWORD_MIN_LENGTH = 10;

export const passwordRulesText = "رمز عبور باید دست‌کم ۱۰ نویسه باشد و دست‌کم یک حرف و یک رقم داشته باشد.";

/** Persian text of the first broken rule, or null when the password is fine (the API checks again; the same letter and digit classes as .NET). */
export function passwordProblem(password: string | null | undefined): string | null {
  const text = password || "";
  if (text.length < PASSWORD_MIN_LENGTH) return "رمز عبور باید دست‌کم ۱۰ نویسه باشد.";
  if (!/\p{L}/u.test(text)) return "رمز عبور باید دست‌کم یک حرف داشته باشد.";
  if (!/\p{Nd}/u.test(text)) return "رمز عبور باید دست‌کم یک رقم داشته باشد.";
  return null;
}

/** Code the sign-in answer carries when the password is a temporary one set by an administrator (data.code). */
export const MUST_CHANGE_PASSWORD = "MUST_CHANGE_PASSWORD";
