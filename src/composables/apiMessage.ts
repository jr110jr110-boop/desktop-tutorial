// Persian text for what the user is told when a call to the API fails or is refused.

/**
 * Text of a failed axios call: what the API wrote ({ message } of a 4xx / 5xx answer), a clear sentence for 401 / 403,
 * and a sentence about the connection when there was no answer at all (stopped server, no network, time out).
 * An error that is not an axios error (for example the Error that the ApiClient helpers return) keeps its own message.
 */
export function apiErrorText(error: any): string {
  const fromApi = error?.response?.data?.message;
  if (typeof fromApi === "string" && fromApi.trim()) return fromApi;

  const status = error?.response?.status;
  if (status === 401) return "دوباره وارد سامانه شوید.";
  if (status === 403) return "شما مجوز دسترسی به این عملیات را ندارید.";
  if (status) return "درخواست انجام نشد (کد " + status + ").";

  if (error && !error.isAxiosError && typeof error.message === "string" && error.message.trim()) return error.message;
  if (error?.code === "ECONNABORTED") return "پاسخ سرور دیر رسید؛ دوباره تلاش کنید.";
  return "ارتباط با سرور برقرار نشد.";
}

/**
 * Text for a call that came back but failed: the error when there is one, otherwise the message of the { success: false } answer.
 * The fallback is used when the API gave no text.
 */
export function failureText(data: any, error: any, fallback: string): string {
  if (error) return apiErrorText(error);
  const message = data?.message;
  return typeof message === "string" && message.trim() ? message : fallback;
}
