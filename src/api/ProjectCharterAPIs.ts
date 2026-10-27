import type { AxiosError, AxiosRequestConfig } from "axios";
import axios from "axios";
import { installAuth } from "@/api/ApiClient";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});
// the bearer token is set on every request and the session is renewed silently (see ApiClient.ts); a token frozen in this file is replaced
installAuth(axiosInstance);

export async function updateContractProjectCharterMaster(formData: {
  contractId: number | string;
  master: string;
}): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "ProjectCharter/add-project-charter-master",
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

export async function updateContractProjectCharterFile(
  id: number | string,
  description: string,
  code: string,
  name: string,
  formData: FormData,
  config: AxiosRequestConfig
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "ProjectCharter/add-project-charter-file?ContractId=" +
        id +
        "&Description=" +
        description +
        "&Name=" +
        name +
        "&Code=" +
        code,
      formData,
      config
    );
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}
