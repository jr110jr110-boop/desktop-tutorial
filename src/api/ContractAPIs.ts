import type { AxiosError, AxiosRequestConfig } from "axios";
import axios from "axios";
import type Contract from "@/types/Contract";
import type ContractEquipment from "@/types/ContractEquipment";
import type ContractProvisionalStatement from "@/types/ContractProvisionalStatement";
import type ContractGuaranty from "@/types/ContractGuaranty";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});

export async function getContractList(
  pageSize = -1
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("Contracts", {
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

export async function getContractDetail(
  id: number | string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("Contracts/contracts", {
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

export async function addContract(
  formData: Contract
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("Contracts", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editContract(
  formData: Contract
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("Contracts", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function attachFileContract(
  id: number | string,
  description: string,
  code: string,
  name: string,
  formData: FormData,
  config: AxiosRequestConfig
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "Contracts/attach-file-to-contract?EntityId=" +
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

export async function detachFileContract(
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

export async function getContractEquipmentList(
  ContractId: number | string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("ContractEquipments", {
      params: {
        ContractId,
        isDeleted: false,
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

export async function getContractEquipmentRemainingList(
  contractId: number | string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get(
      "ContractEquipments/get-all-remaining-of-contract/" + contractId,
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

export async function addContractEquipment(
  formData: ContractEquipment
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("ContractEquipments", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editContractEquipment(
  formData: ContractEquipment
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("ContractEquipments", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function deleteContractEquipment(
  contractId: number | string,
  equipmentId: number | string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.delete("ContractEquipments", {
      data: {
        contractId,
        equipmentId,
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

export async function getContractProvisionalStatementList(
  ContractId: number | string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get(
      "ContractProvisionalStatements/provisional-statements",
      {
        params: {
          ContractId,
          isDeleted: false,
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

export async function addContractProvisionalStatement(
  formData: ContractProvisionalStatement
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "ContractProvisionalStatements",
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

export async function editContractProvisionalStatement(
  formData: ContractProvisionalStatement
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "ContractProvisionalStatements",
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

export async function deleteContractProvisionalStatement(
  contractId: number | string,
  id: number | string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.delete(
      "ContractProvisionalStatements",
      {
        data: {
          contractId,
          id,
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

export async function addGuarantyContract(
    formData: ContractGuaranty
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const {data} = await axiosInstance.post("ContractGuaranty", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editGuarantyContract(
    formData: ContractGuaranty
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const {data} = await axiosInstance.put("ContractGuaranty", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}


export async function deleteGuarantyContract(
    guarantyId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const {data} = await axiosInstance.delete(
        "ContractGuaranty?contractGuarantyId=" +
        guarantyId,
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

