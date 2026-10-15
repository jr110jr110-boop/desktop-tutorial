import type { AxiosError } from "axios";
import axios from "axios";
import type Login from "@/types/Login";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

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

export async function refreshToken(): Promise<
  [any, null] | [null, AxiosError] | undefined
> {
  try {
    const { data } = await axiosInstance.post(
      "Accounts/refresh-token",
      {
        refreshToken: window.localStorage.getItem("refreshToken"),
      },
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
