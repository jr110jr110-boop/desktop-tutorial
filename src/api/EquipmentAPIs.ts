import type { AxiosError, AxiosRequestConfig } from "axios";
import axios from "axios";
import { installAuth } from "@/api/ApiClient";
import type Equipment from "@/types/Equipment";
import type EquipmentAssembly from "@/types/EquipmentAssembly";
import type EquipmentBom from "@/types/EquipmentBom";
import type EquipmentProcess from "@/types/EquipmentProcess";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});
// the bearer token is set on every request and the session is renewed silently (see ApiClient.ts); a token frozen in this file is replaced
installAuth(axiosInstance);

export async function getEquipmentList(
  pageSize = -1
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("Equipments", {
      params: {
        isDeleted: false,
        pageSize: pageSize ?? 25,
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

export async function getEquipmentDetail(
  equipmentId: number | string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("Equipments/" + equipmentId, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function addEquipment(
  formData: Equipment
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("Equipments", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editEquipment(
  formData: Equipment
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("Equipments", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function getEquipmentAssemblyList(
  IncludeTypes = false,
  EquipmentId: number | string,
  Contains: string,
  PageSize,
  PageNumber
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get("EquipmentAssemblies", {
      params: {
        IncludeTypes,
        EquipmentId,
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

export async function addEquipmentAssembly(
  formData: EquipmentAssembly
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("EquipmentAssemblies", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editEquipmentAssembly(
  formData: EquipmentAssembly
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("EquipmentAssemblies", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function deleteEquipmentAssembly(
  id
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.delete("EquipmentAssemblies/" + id, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function setEquipmentAssemblyPredecessor(
  id,
  predecessorId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "EquipmentAssemblies/set-predecessor",
      {
        id,
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

export async function addEquipmentBom(
  formData: EquipmentBom
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("EquipmentBoms", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editEquipmentBom(
  formData: EquipmentBom
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("EquipmentBoms", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function getEquipmentBomListAll(
  id
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.get(
      "EquipmentBoms/get-equipmentBom-all?EquipmentId=" + id,
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

export async function deleteEquipmentBom(
  id
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.delete("EquipmentBoms/" + id, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function setEquipmentBomPredecessor(
  equipmentBomId,
  predecessorId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "EquipmentBoms/set-predecessor",
      {
        equipmentBomId,
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

export async function deleteEquipmentBomPredecessor(
  equipmentBomId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "EquipmentBoms/delete-predecessor",
      null,
      {
        params: {
          equipmentBomId,
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

export async function addEquipmentProcess(
  formData: EquipmentProcess
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post("EquipmentProcess", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editEquipmentProcess(
  formData: EquipmentProcess
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put("EquipmentProcess", formData, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function deleteEquipmentProcess(
  id
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.delete("EquipmentProcess/" + id, {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function editEquipmentProcessSort(formData: {
  id1: number | string;
  id2: number | string;
}): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "EquipmentProcess/update-sort",
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

export async function attachFileEquipmentProcess(
  id: number | string,
  description: string,
  name: string,
  code: string,
  formData: FormData,
  config: AxiosRequestConfig
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "EquipmentProcess/attach-file-to-equipmentProcess?EntityId=" +
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

export async function detachFileEquipmentProcess(
  equipmentProcessId: number | string,
  fileId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.delete(
      "EquipmentProcess/detach-file-from-equipmentProcess?equipmentProcessId=" +
        equipmentProcessId +
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

export async function finalizeFileEquipmentProcess(
  equipmentProcessId: number | string,
  documentId
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.put(
      "EquipmentProcess/finalizeFile",
      {
        equipmentProcessId,
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
