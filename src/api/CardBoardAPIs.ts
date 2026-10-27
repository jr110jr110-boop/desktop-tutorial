import type { AxiosError } from "axios";
import axios from "axios";
import { installAuth } from "@/api/ApiClient";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});
// the bearer token is set on every request and the session is renewed silently (see ApiClient.ts); a token frozen in this file is replaced
installAuth(axiosInstance);

export async function getCardBoardUserProcesses(): Promise<
  [any, null] | [null, AxiosError] | undefined
> {
  try {
    const { data } = await axiosInstance.get(
      "CardBoard/get-all-user-processes",
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

export async function getCardBoardUserBuyOrders(
  PageSize,
  PageNumber
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get(
      "CardBoard/get-all-user-buyorders",
      {
        params: {
          PageSize,
          PageNumber,
        },
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
