import type EquipmentBom from "@/types/EquipmentBom";

export default interface EquipmentAssembly {
  id: number | string;
  equipmentId: number | string;
  title: string;
  code: string;
  assemblerDuration: number;
  totalWeightPercent?: string;
  parentId?: string | number;
  equipmentBoms?: EquipmentBom[];
  children: EquipmentAssembly[];
}
