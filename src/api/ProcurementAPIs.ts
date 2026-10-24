import { call, clean } from "@/api/ApiClient";

// Procurement (api/v1/procurement/...): purchase request -> inquiry -> committee -> purchase order -> receipt / service
// -> supplier invoice (three-way match) -> payment request; approved vendor list and evaluations.
const base = "procurement/";

export const getProcurementSettings = () => call("get", base + "settings");
export const saveProcurementSetting = (key: string, value: number) => call("post", base + "settings", { key, value });

// purchase requests
export const getPurchaseRequests = (params: any) => call("get", base + "requests", undefined, clean(params));
export const getPurchaseRequest = (id: number) => call("get", base + "requests/" + id);
export const savePurchaseRequest = (dto: any) => call("post", base + "requests", dto);
/** action: submit, approve-planning, approve-management (reference = board resolution), reject (note), close. */
export const purchaseRequestAction = (id: number, action: string, note?: string, reference?: string) =>
  call("post", base + "requests/action", { id, action, note: note || null, reference: reference || null });

// inquiries and quotes
export const getInquiries = (purchaseRequestId?: number | null) => call("get", base + "inquiries", undefined, clean({ purchaseRequestId }));
export const getInquiry = (id: number) => call("get", base + "inquiries/" + id);
export const saveInquiry = (dto: any) => call("post", base + "inquiries", dto);
export const selectQuote = (inquiryId: number, quoteId: number, reason: string) => call("post", base + "inquiries/select", { inquiryId, quoteId, reason });
/** action: approve-financial, approve-commercial, approve-technical, approve-management (reference = committee minutes), cancel. */
export const inquiryAction = (id: number, action: string, note?: string, reference?: string) =>
  call("post", base + "inquiries/action", { id, action, note: note || null, reference: reference || null });

// purchase orders
export const getPurchaseOrders = (params: any) => call("get", base + "orders", undefined, clean(params));
export const getPurchaseOrder = (id: number) => call("get", base + "orders/" + id);
export const savePurchaseOrder = (dto: any) => call("post", base + "orders", dto);
export const createOrderFromInquiry = (inquiryId: number) => call("post", base + "orders/from-inquiry/" + inquiryId);
/** action: approve, release, close, cancel (note). */
export const purchaseOrderAction = (id: number, action: string, note?: string) => call("post", base + "orders/action", { id, action, note: note || null });
export const acceptService = (dto: any) => call("post", base + "orders/accept-service", dto);

// supplier invoices and payments
export const getSupplierInvoices = (params: any) => call("get", base + "invoices", undefined, clean(params));
export const getSupplierInvoice = (id: number) => call("get", base + "invoices/" + id);
export const saveSupplierInvoice = (dto: any) => call("post", base + "invoices", dto);
export const invoiceAction = (id: number, action: string, note?: string) => call("post", base + "invoices/action", { id, action, note: note || null });

export const getPaymentRequests = (params: any) => call("get", base + "payments", undefined, clean(params));
export const savePaymentRequest = (dto: any) => call("post", base + "payments", dto);
/** action: approve, paid (reference = payment reference), reject (note). */
export const paymentAction = (id: number, action: string, note?: string, reference?: string, bankAccountId?: number | null) =>
  call("post", base + "payments/action", { id, action, note: note || null, reference: reference || null, bankAccountId: bankAccountId || null });

// approved vendor list
export const getSuppliers = (contains?: string) => call("get", base + "suppliers", undefined, clean({ contains }));
export const saveSupplierProfile = (dto: any) => call("post", base + "suppliers", dto);
export const getSupplierEvaluations = (providerId: number) => call("get", base + `suppliers/${providerId}/evaluations`);
export const evaluateSupplier = (dto: any) => call("post", base + "suppliers/evaluate", dto);
