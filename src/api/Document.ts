import axios from "axios";
import { installAuth } from "@/api/ApiClient";

// @ts-ignore
const apiUrl = import.meta.env.VITE_API_URL;

const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});
// the bearer token is set on every request and the session is renewed silently (see ApiClient.ts); a token frozen in this file is replaced
installAuth(axiosInstance);

const token = localStorage.getItem("token");

export async function addDocument(
  document: FormData,
  desc,
  name,
  code
): Promise<[any, null] | [null, Error] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "Documents?Description=" + desc + "&Name=" + name + "&Code=" + code,
      document,
      {
        headers: {
          Authorization: `Bearer ${token}`,
        },
      }
    );
    return [data, null];
  } catch (error: any) {
    return [null, error];
  }
}

export async function getDocument(
  DocumentId: string | number
): Promise<[any, null] | [null, Error] | undefined> {
  try {
    const { data } = await axiosInstance.get(
      "Documents?DocumentId=" + DocumentId,
      {
        responseType: "blob",
        headers: {
          Authorization: `Bearer ` + localStorage.getItem("token"),
        },
      }
    );
    return [data, null];
  } catch (error: any) {
    return [null, error];
  }
}

export async function getDocumentFileInfo(
  fileId: string | number
): Promise<[any, null] | [null, Error] | undefined> {
  try {
    const { data } = await axiosInstance.get(
      "Documents/get-file-info?fileId=" + fileId,
      {
        headers: {
          Authorization: `Bearer ` + localStorage.getItem("token"),
        },
      }
    );
    return [data, null];
  } catch (error: any) {
    return [null, error];
  }
}
