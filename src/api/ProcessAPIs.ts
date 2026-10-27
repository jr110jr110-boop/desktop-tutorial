import type { AxiosError, AxiosRequestConfig } from "axios";
import axios from "axios";
import { installAuth } from "@/api/ApiClient";
import type _Process from "@/types/Process";
import type AttachFileToProccess from "@/types/AttachFileToProccess";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});
// the bearer token is set on every request and the session is renewed silently (see ApiClient.ts); a token frozen in this file is replaced
installAuth(axiosInstance);

export async function getProcessDetail(
  Id: number | string,
  BomId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("Processes/get-process", {
      params: {
        Id,
        BomId,
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

export async function addProcess(
  formData: _Process
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("Processes", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editProcess(
  formData: _Process
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("Processes", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function attachFileProcess(
  attachFileProccess: AttachFileToProccess
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "Processes/attach-file-to-process",
      attachFileProccess,
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

export async function detachFileProcess(
  processId: number | string,
  fileId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.delete(
      "Processes/detach-file-from-process",
      {
        params: {
          processId,
          fileId,
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

export async function revertProcessStatus(
  processId: string | number,
  status: number
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "Processes/revert-process-status",
      {
        processId,
        status,
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

export async function processPMOAccept(
  Id: string | number
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "Processes/pmo-accept-process",
      { Id },
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

export async function processChangeSelectedUser(
  Id: string | number
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "Processes/change-user",
      {
        Id,
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

export async function processAcceptDocuments(
  Id: string | number,
  userMode
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "Processes/" +
        (userMode.toLowerCase() === "engineer"
          ? "engineer-accept-documents"
          : "engineering-accept-documents"),
      { Id },
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

export async function processNotAcceptDocuments(
  Id: string | number
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "Processes/engineering-not-accept-documents",
      { Id },
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

export async function processMasterApproved(
  Id: string | number
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "Processes/master-accept-process",
      { Id },
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

export async function finalizeFileProcess(
  processId: number | string,
  documentId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "Processes/finalizeFile",
      {
        processId,
        documentId,
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
