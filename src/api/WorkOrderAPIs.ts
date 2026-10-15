import type { AxiosError } from "axios";
import axios from "axios";
import type WorkOrder from "@/types/WorkOrder";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});

export async function getWorkOrderList(
  IncludeTypes = false,
  ContractId: number | string,
  Contains: string,
  PageSize,
  PageNumber
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("WorkOrders", {
      params: {
        IncludeTypes,
        ContractId,
        Contains,
        PageSize,
        PageNumber,
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

export async function getWorkOrderDetail(
  contractId: number | string,
  id: number | string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("WorkOrders/get-workorder", {
      params: {
        contractId,
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

export async function addWorkOrder(
  formData: WorkOrder
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("WorkOrders", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editWorkOrder(
  formData: WorkOrder
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("WorkOrders", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function getWorkOrderProgressStatistic(
  workOrderId: number | string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get(
      "WorkOrders/calculate-progress-levels",
      {
        params: {
          workOrderId,
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
