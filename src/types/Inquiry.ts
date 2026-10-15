import type Provider from "@/types/Provider";

export default interface Inquiry {
  id?: string | number;
  count: number;
  price: number;
  technicalDoc: string;
  financialDoc: string;
  providerId: number | string;
  buyOrderId: number | string;

  technicalScore?: number;
  isAccept?: boolean;
  provider?: Provider;
}
