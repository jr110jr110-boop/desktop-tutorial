import type AttachFile from "@/types/AttachFile";
import type ContractGuaranty from "@/types/ContractGuaranty";

export default interface Contract {
  id?: number | string;
  code?: string;
  subCode?: string;
  tenderCode?: string;
  employerOrderNo?: string;
  employerContractNo?: string;
  employerProjectNo?: string;
  internalContractNo?: string;
  internalProjectNo?: string;
  contractDate?: string;
  announceDate?: string;
  startDate?: string;
  payDate?: string;
  price: number;
  duration: number;
  actualStartDate?: string;
  TPA?: string;
  site?: string;
  type?: string;
  title?: string;
  tempDate?: string;
  deliverDate?: string;
  gurantyDate?: string;
  endDate?: string;
  employer?: string;
  subEmployer?: string;
  master?: string;
  status: number;
  forContract?: string;
  services?: string;
  files?: AttachFile[];
  guaranties?: ContractGuaranty[];
  projectCharterFile?: AttachFile;
}
