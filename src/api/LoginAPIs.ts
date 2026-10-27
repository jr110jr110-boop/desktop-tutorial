import type { AxiosError } from "axios";
import axios from "axios";
import type Login from "@/types/Login";

const apiUrl = import.meta.env.VITE_API_URL.toString();

// This file must not import ApiClient.ts (ApiClient refreshes the session through it).
// The sign-in calls are anonymous, so no bearer header is sent: when the refresh runs, the access token has usually ended.
const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});

export async function loginUser(
  loginInfo: Login
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("Accounts/Login", loginInfo);
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

/** POST Accounts/refresh-token. The refresh token works once; the answer carries the new pair. */
export async function refreshToken(
  token?: string | null
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    // the timeout keeps a stuck call from holding the refresh of every tab (the tabs take turns, see refreshSession)
    const { data } = await axiosInstance.post(
      "Accounts/refresh-token",
      { refreshToken: token ?? window.localStorage.getItem("refreshToken") },
      { timeout: 15000 }
    );
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

/**
 * POST Accounts/change-temporary-password: the user who signed in with a temporary password (the login answer said MUST_CHANGE_PASSWORD)
 * proves it and chooses a new one. The answer is a sign-in: { accessToken, refreshToken, accessList }.
 */
export async function changeTemporaryPassword(info: {
  userName: string;
  temporaryPassword: string;
  newPassword: string;
}): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("Accounts/change-temporary-password", info);
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

/**
 * GET Accounts/logout/{refreshToken}: the server forgets the refresh token, so a copy of it cannot be used after the user signed out.
 * Best effort: the caller does not wait for it and a failure changes nothing (the browser session is already gone).
 */
export async function logoutUser(token: string | null, accessToken?: string | null): Promise<void> {
  if (!token) return;
  try {
    await axiosInstance.get("Accounts/logout/" + encodeURIComponent(token), {
      headers: accessToken ? { Authorization: "Bearer " + accessToken } : {},
    });
  } catch {
    /* the session is over in this browser either way */
  }
}
