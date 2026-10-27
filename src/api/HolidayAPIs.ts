import type { AxiosError } from "axios";
import axios from "axios";
import { installAuth } from "@/api/ApiClient";

const apiUrl = import.meta.env.VITE_API_URL.toString();
const authToken = "Bearer " + window.localStorage.getItem("token");

const axiosInstance = axios.create({
  baseURL: apiUrl + "api/v1/",
});
// the bearer token is set on every request and the session is renewed silently (see ApiClient.ts); a token frozen in this file is replaced
installAuth(axiosInstance);

export async function getHolidayDays(): Promise<
  [any, null] | [null, AxiosError] | undefined
> {
  try {
    const { data } = await axiosInstance.get("Holidays/get-all-holiday-days", {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function getHolidayDates(): Promise<
  [any, null] | [null, AxiosError] | undefined
> {
  try {
    const { data } = await axiosInstance.get("Holidays/get-all-holiday-dates", {
      headers: {
        Authorization: authToken,
      },
    });
    return [data, null];
  } catch (error) {
    return [null, error];
  }
}

export async function addHolidayDay(
  dayIndex
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "Holidays/add-day-of-week-to-holidays",
      {
        dayOfWeek: dayIndex,
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

export async function addHolidayDate(
  HolidayDate: string
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.post(
      "Holidays/add-date-to-holidays",
      { HolidayDate },
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

export async function deleteHolidayDay(
  DayOfWeek
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.delete(
      "Holidays/delete-day-of-week-from-holidays",
      {
        data: {
          DayOfWeek,
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

export async function deleteHolidayDate(
  Id
): Promise<[any, null] | [null, AxiosError] | undefined> {
  try {
    const { data } = await axiosInstance.delete(
      "Holidays/delete-date-from-holidays",
      {
        data: {
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
