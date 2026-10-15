import type {AxiosError, AxiosRequestConfig} from "axios";
import axios from "axios";
import type Tender from "@/types/Tender";
import type TenderGuaranty from "@/types/TenderGuaranty";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

const axiosInstance = axios.create({
    baseURL: apiUrl + "api/v1/",
});

export async function getTenderList(): Promise<[any, null] | [null, AxiosError] | undefined> {
    try {
        const {data} = await axiosInstance.get("Tenders", {
            headers: {
                Authorization: authToken,
            },
        });
        return [data, null];
    } catch (error) {
        return [null, error];
    }
}


export async function getTenderDetail(
    tenderId: number | string
): Promise<[any, null] | [null, AxiosError] | undefined> {
    try {
        const {data} = await axiosInstance.get("Tenders/" + tenderId, {
            headers: {
                Authorization: authToken,
            },
        });
        return [data, null];
    } catch (error) {
        return [null, error];
    }
}

export async function addTender(
    formData: Tender
): Promise<[any, null] | [null, AxiosError] | undefined> {
    try {
        const {data} = await axiosInstance.post("Tenders", formData, {
            headers: {
                Authorization: authToken,
            },
        });
        return [data, null];
    } catch (error) {
        return [null, error];
    }
}

export async function editTender(
    formData: Tender
): Promise<[any, null] | [null, AxiosError] | undefined> {
    try {
        const {data} = await axiosInstance.put("Tenders", formData, {
            headers: {
                Authorization: authToken,
            },
        });
        return [data, null];
    } catch (error) {
        return [null, error];
    }
}








export async function attachFileTender(
    id: number | string,
    description: string,
    code: string,
    name: string,
    formData: FormData,
    config: AxiosRequestConfig
): Promise<[any, null] | [null, AxiosError] | undefined> {
    try {
        const {data} = await axiosInstance.post(
            "Tenders/attach-file-to-tender?EntityId=" +id +"&Description=" +description +"&Name=" +name +"&Code=" +code ,formData ,config
        );
        return [data, null];
    } catch (error) {
        return [null, error];
    }
}


export async function editFileTender(
    id: number | string,
    description: string,
    code: string,
    name: string,
    formData: FormData,
    config: AxiosRequestConfig
): Promise<[any, null] | [null, AxiosError] | undefined> {
    try {
        const {data} = await axiosInstance.put(
            "Tenders/edit-file-to-tender?Id=" +id +"&Description=" +description +"&Name=" +name +"&Code=" +code ,formData ,config
        );
        return [data, null];
    } catch (error) {
        return [null, error];
    }
}


export async function detachFileTender(
    tenderId: number | string,
    fileId
): Promise<[any, null] | [null, AxiosError] | undefined> {
    try {
        const {data} = await axiosInstance.delete(
            "Tenders/detach-file-from-tender?tenderId=" +
            tenderId +
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


export async function addGuarantyTender(
    formData: TenderGuaranty
): Promise<[any, null] | [null, AxiosError] | undefined> {
    try {
        const {data} = await axiosInstance.post("Tenders/add-guaranty-to-tender", formData, {
            headers: {
                Authorization: authToken,
            },
        });
        return [data, null];
    } catch (error) {
        return [null, error];
    }
}

export async function editGuarantyTender(
    formData: TenderGuaranty
): Promise<[any, null] | [null, AxiosError] | undefined> {
    try {
        const {data} = await axiosInstance.put("Tenders/edit-guaranty-in-tender", formData, {
            headers: {
                Authorization: authToken,
            },
        });
        return [data, null];
    } catch (error) {
        return [null, error];
    }
}


export async function deleteGuarantyTender(
    guarantyId
): Promise<[any, null] | [null, AxiosError] | undefined> {
    try {
        const {data} = await axiosInstance.delete(
            "Tenders/delete-guaranty-from-tender?tenderGuarantyId=" +
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
