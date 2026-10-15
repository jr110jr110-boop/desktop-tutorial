import type _File from "@/types/_File";

export default interface _Process {
  id: number | string;
  title: string;
  exitCommodityCode: string;
  enterCommodityCode: string;
  mapDuration: number;
  masterDuration: number;
  processDuration: number;
  machineId: number;
  type: string;
  workflow: number | string;
  weightPercent: string;
  typeDescription?: string;
  files?: _File[];
  programPrice: number;
  bomId: number | string;
  timeSheet: number | string;
}
