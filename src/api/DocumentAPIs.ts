import type { AxiosError } from "axios";
import axios from "axios";
import type AttachFile from "@/types/AttachFile";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});

export async function getDocument(
  id: string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("Documents?DocumentId=" + id, {
      headers: {
        Authorization: authToken,
      },
      responseType: "blob",
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function getDocumentInfo(
  fileId: string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("Documents/get-file-info", {
      params: {
        fileId,
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

export async function changeDocumentInfo(
  document: AttachFile
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("Documents", document, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function changeDocumentFile(
  documentId: string | number,
  file: FormData
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("Documents/change-file", file, {
      params: {
        documentId,
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
