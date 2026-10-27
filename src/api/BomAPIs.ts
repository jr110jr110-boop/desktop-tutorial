import type { AxiosError } from "axios";
import axios from "axios";
import { installAuth } from "@/api/ApiClient";
import type Bom from "@/types/Bom";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});
// the bearer token is set on every request and the session is renewed silently (see ApiClient.ts); a token frozen in this file is replaced
installAuth(axiosInstance);

export async function getBomList(
  AssemblyId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("Boms", {
      params: {
        AssemblyId,
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

export async function getBomDetail(
  Id: number | string,
  AssemblyId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("Boms/get-bom", {
      params: {
        Id,
        AssemblyId,
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

export async function addBom(
  formData: Bom
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("Boms", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editBom(
  formData: Bom
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("Boms", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function setBomPredecessor(
  bomId,
  predecessorId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "Boms/set-predecessor",
      {
        bomId,
        predecessorId,
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

export async function deleteBomPredecessor(
  bomId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("Boms/delete-predecessor", null, {
      params: {
        bomId,
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
