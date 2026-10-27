import type { AxiosError } from "axios";
import axios from "axios";
import { installAuth } from "@/api/ApiClient";
import type Inquiry from "@/types/Inquiry";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});
// the bearer token is set on every request and the session is renewed silently (see ApiClient.ts); a token frozen in this file is replaced
installAuth(axiosInstance);

export async function getInquiryList(
  BuyOrderId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("Inquiries", {
      params: {
        BuyOrderId,
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

export async function addInquiry(
  formData: Inquiry
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("Inquiries", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editInquiry(
  formData: Inquiry
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("Inquiries", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function setInquiryScore(
  Id,
  technicalScore
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "Inquiries/set-technical-score",
      {
        Id,
        TechnicalScore: parseInt(technicalScore),
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

export async function deleteInquiry(
  inquiryId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.delete("Inquiries/" + inquiryId, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}
