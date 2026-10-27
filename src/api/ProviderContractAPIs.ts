import type { AxiosError, AxiosRequestConfig } from "axios";
import axios from "axios";
import { installAuth } from "@/api/ApiClient";
import type Contract from "@/types/Contract";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});
// the bearer token is set on every request and the session is renewed silently (see ApiClient.ts); a token frozen in this file is replaced
installAuth(axiosInstance);

export async function getContractProviderList(
  pageSize = -1
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("ContractsForProvider", {
      params: {
        isDeleted: false,
        pageSize,
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

export async function getContractProviderDetail(
  id: number | string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("ContractsForProvider/contracts", {
      params: {
        id,
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

export async function addContractProvider(
  formData: Contract
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "ContractsForProvider",
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

export async function editContractProvider(
  formData: Contract
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("ContractsForProvider", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function attachFileContractProvider(
  id: number | string,
  description: string,
  code: string,
  name: string,
  formData: FormData,
  config: AxiosRequestConfig
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "ContractsForProvider/attach-file-to-contract?EntityId=" +
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

export async function detachFileContractProvider(
  contractId: number | string,
  fileId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.delete(
      "Contracts/detach-file-from-contract?contractId=" +
        contractId +
        "&fileId=" +
        fileId,
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
