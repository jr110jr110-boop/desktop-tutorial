import type AttachFile from "@/types/AttachFile";
import type TenderGuaranty from "@/types/TenderGuaranty";

export default interface Tender {
  id?: number | string;
  code: string;
  pmrNo: string;
  description: string;
  count: string;
  company: string;
  subCompany: string;
  reciveDate: string;
  tenderDate: string;
  techSendDate: string;
  financeSendDate: string;
  callResult: string;
  customerResult: string;
  result: string;
  files?: AttachFile[];
  guaranties?: TenderGuaranty[];
  customerName: string;
  customerRole: string;
  customerTel: string;
  customerCode: string;
  customerMobile: string
}
