import type AttachFile from "@/types/AttachFile";
import type EquipmentBom from "@/types/EquipmentBom";

export default interface EquipmentProcess {
  id: number | string;
  title: string;
  exitCommodityCode: string;
  enterCommodityCode: string;
  mapDuration: number;
  masterDuration: number;
  processDuration: number;
  machineId: number;
  type: string;
  weightPercent: string;
  typeDescription?: string;
  files?: AttachFile[];
  equipmentBom?: EquipmentBom;
  programPrice: number;
  equipmentBomId: number | string;
  workflowId: number | string;
}
