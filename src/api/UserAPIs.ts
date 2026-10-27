import type { AxiosError } from "axios";
import axios from "axios";
import { installAuth } from "@/api/ApiClient";
import type User from "@/types/User";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});
// the bearer token is set on every request and the session is renewed silently (see ApiClient.ts); a token frozen in this file is replaced
installAuth(axiosInstance);

/**
 * GET Accounts/users/lookup: the slim user list for pickers (id, userName, name, lastName, displayName) that every signed-in user may read.
 * Same paging and search as the full list: Contains (typed words), PageNumber, PageSize (at most 200), Role (exact role name).
 * The full list (Accounts/get-all-users) is for the PMO only and stays on the user administration page.
 */
export async function getUserLookup(
  Contains: string | null = null,
  PageNumber: number | null = null,
  PageSize: number | null = null,
  Role: string | null = null
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("Accounts/users/lookup", {
      params: {
        Contains,
        PageNumber,
        PageSize,
        Role,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function getUserListEngineerAll(): Promise<
  [any, null] | [null, AxiosError] | undefined
> {
  try {
    const { data } = await axiosInstance.get("Accounts/get-engineer-users", {
      params: {
        Contains: null,
        PageNumber: 1,
        PageSize: 25,
        IsDeleted: false,
      },
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function getUserDetail(
  userId: number | string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get(
      "Accounts/get-user-info?id=" + userId,
      {
        headers: {
          Authorization: authToken,
        },
      }
    );
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function addUser(
  formData: User
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "Accounts/register-user",
      formData,
      {
        headers: {
          Authorization: authToken,
        },
      }
    );
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editUser(
  formData: User
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("Accounts/update-user", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function getUserRoleList(): Promise<
  [any, null] | [null, AxiosError] | undefined
> {
  try {
    const { data } = await axiosInstance.post("Accounts/get-all-roles", null, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

/**
 * POST Accounts/change-password: the signed-in user changes the OWN password and must send the current one.
 * Nobody can change another user's password with it (the PMO uses adminResetPassword).
 * The answer is a sign-in: { accessToken, refreshToken, accessList }, because the old tokens of the user end with the old password.
 */
export async function changeOwnPassword(info: {
  currentPassword: string;
  newPassword: string;
}): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("Accounts/change-password", info);
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

/**
 * POST Accounts/admin-reset-password (PMO only, never the own account): sets a temporary password that the user must change at the next sign-in.
 * Without temporaryPassword the system makes one and returns it ONCE in data.temporaryPassword.
 */
export async function adminResetPassword(
  userId: number | string,
  temporaryPassword: string | null = null
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("Accounts/admin-reset-password", {
      userId: Number(userId),
      temporaryPassword: temporaryPassword || null,
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

/** GET Accounts/role-grants (PMO only): the sensitive roles that wait for the approval of a second PMO user. */
export async function getRoleGrants(): Promise<
  [any, null] | [null, AxiosError] | undefined
> {
  try {
    const { data } = await axiosInstance.get("Accounts/role-grants");
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

/** POST Accounts/role-grants/{id}/approve (PMO only; not the user who asked, not the user who receives the role). */
export async function approveRoleGrant(
  grantId: number
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "Accounts/role-grants/" + grantId + "/approve"
    );
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

/** POST Accounts/role-grants/{id}/reject (PMO only; the user who asked may also withdraw the request this way). */
export async function rejectRoleGrant(
  grantId: number
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "Accounts/role-grants/" + grantId + "/reject"
    );
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function getCurrentUserData(): Promise<
  [any, null] | [null, AxiosError] | undefined
> {
  try {
    const { data } = await axiosInstance.get("Accounts/get-current-user-info", {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editUserProfilePhoto(
  UserId,
  formData: FormData
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "Accounts/attach-profile-photo",
      formData,
      {
        params: {
          UserId,
          Description: ".",
          Name: ".",
          Code: ".",
        },
        headers: {
          "Content-Type": "multipart/form-data",
          Authorization: authToken,
        },
      }
    );
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}
