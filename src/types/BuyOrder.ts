import type _Process from "@/types/Process";
import type Inquiry from "@/types/Inquiry";
import type AttachFile from "@/types/AttachFile";

export default interface BuyOrder {
  id?: string | number;
  exitCommodityCode: string;
  enterCommodityCode: string;
  count: number;
  title: string;
  reserveCount: number;
  buyCount: number;
  realBuyCount?: number;
  financeDocDate?: string;
  workOrderId: number | string;
  processes?: _Process[];
  buyOrderProcesses?: {
    processId: number | string;
    attribute: string;
  }[];
  files?: AttachFile[];
  inquiries?: Inquiry[];
  optionalFinanceDocDate?: string;
  financeAcceptDate?: string;
  tempReceiveDate?: string;
  logShitId?: string;
  receiveDate?: string;
  registerDate?: string;
  inquiryDate?: string;
  inquiryAcceptDate?: string;
  financeType?: string;
  financeNumber?: string;
  financePrice?: number;
  statusDescription?: string;
  status?: number;
  timeSheet?: string;
  workOrderTitle?: string;
}
