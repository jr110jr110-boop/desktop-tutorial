import axios, { type AxiosError, type AxiosRequestConfig } from "axios";
import type BuyOrder from "@/types/BuyOrder";
import type Assembler from "@/types/Assembler";
import type Constructor from "@/types/Constructor";
import type AcceptReject from "@/types/AcceptReject";
import type DeleteBuyOrderProcess from "@/types/DeleteBuyOrderProcess";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});

export async function getBuyOrderList(
  WorkOrderId: number | string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get(
      "BuyOrders/get-buyorders-and-processes/" + WorkOrderId,
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

export async function getBuyOrderDetail(
  workOrderId: number | string,
  id: number | string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("BuyOrders/get-buyorder", {
      params: {
        workOrderId,
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

export async function addBuyOrder(
  formData: BuyOrder
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("BuyOrders", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editBuyOrder(
  formData: BuyOrder
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("BuyOrders", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function buyOrderSyncCounts(
  Id
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get(
      "BuyOrders/GetBuyOrderChangedCountsAsync",
      {
        params: {
          Id,
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

export async function buyOrderInquiriesAdded(
  Id,
  FinanceType
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "BuyOrders/inquiries-added/",
      { Id, FinanceType },
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

export async function buyOrderInquiriesScoreAdded(
  Id
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "BuyOrders/technical-score-added/",
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

export async function buyOrderSetWinnerInquiry(
  Id,
  WinnerInquiryId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "BuyOrders/winner-selected/",
      {
        Id,
        WinnerInquiryId,
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

export async function buyOrderSetFinancialDocuments(
  Id: number | string,
  OptionalFinanceDocDate: string,
  FinanceType: string,
  FinanceNumber: string,
  FinancePrice: number
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "BuyOrders/finance-document-setted",
      {
        Id,
        OptionalFinanceDocDate,
        FinanceType,
        FinanceNumber,
        FinancePrice,
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

export async function buyOrderAskTempReceipt(
  Id,
  RealBuyCount
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "BuyOrders/commercial-bought",
      {
        Id,
        RealBuyCount,
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

export async function buyOrderSetTempReceipt(
  Id,
  LogShitId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "BuyOrders/add-temp-receipt",
      { Id, LogShitId },
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

export async function buyOrderQcRespondTempReceipt(
  Id,
  isAccept: boolean
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      `BuyOrders/qc-${isAccept ? "accepted" : "rejected"}`,
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

export async function buyOrderEngineeringRespondTempReceipt(
  Id,
  status: "reject-give-to-salvage" | "accepted" | "says-needs-fixes"
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      `BuyOrders/engineering-${status}`,
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

export async function buyOrderWarehouseFinalRespond(
  Id
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      `BuyOrders/warehouse-finally-accepted`,
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

export async function buyOrderAssemblerAssembled(
  assembler: Assembler
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "BuyOrders/assembler-assembled",
      assembler,
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

export async function buyOrderConstructorConstructed(
  constructor: Constructor
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "BuyOrders/constructor-constructed",
      constructor,
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
export async function buyOrderPMOAcceptReject(
  acceptReject: AcceptReject
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "BuyOrders/pmo-accept-reject",
      acceptReject,
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
export async function buyOrderAcceptReject(
  acceptReject: AcceptReject
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "BuyOrders/warehouse-accept-reject",
      acceptReject,
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
export async function buyOrderPMOAcceptGivenToWareHouse(
  Id
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "BuyOrders/pmo-accepted-given-to-warehouse",
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

export async function detachFileBuyOrder(
  buyOrderId: number | string,
  fileId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.delete(
      "BuyOrders/detach-file-from-buyOrder?buyOrderId=" +
        buyOrderId +
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

export async function attachFileBuyOrder(
  id: number | string,
  description: string,
  name: string,
  code: string,
  formData: FormData,
  config: AxiosRequestConfig
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "BuyOrders/attach-file-to-buyOrder?EntityId=" +
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

export async function revertBuyOrderStatus(
  buyOrderId: string | number,
  status: number
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "BuyOrders/revert-buyOrder-status",
      {
        buyOrderId,
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

export async function getBuyOrderReadyProcessList(
  WorkOrderId: number | string,
  EnterCommodityCode: string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get(
      "BuyOrderProcesses/get-ready-processes-for-buyorder",
      {
        params: {
          WorkOrderId,
          EnterCommodityCode,
          IsDeleted: false,
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
export async function getBuyOrderProcessList(
  buyOrderId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get(
      "BuyOrderProcesses/get-processes-for-buyOrder?buyOrderId=" + buyOrderId,
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

export async function addBuyOrderProcess(
  formData
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("BuyOrderProcesses", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editBuyOrderProcess(
  formData
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("BuyOrderProcesses", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function deleteBuyOrderProcess(
  BuyOrder: DeleteBuyOrderProcess
): Promise<[any, null] | [null, Error] | undefined> {
  try {
    const { data } = await axiosInstance.delete("BuyOrderProcesses", {
      data: BuyOrder,
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}
