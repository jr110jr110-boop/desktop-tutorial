import type EquipmentProcess from "@/types/EquipmentProcess";

export default interface EquipmentBom {
  id?: number | string;
  equipmentAssemblyId: number | string;
  positionNumber: number;
  title: string;
  count: number;
  unit: string;
  material: string;
  standard: string;
  diagonal: string;
  diagonalUnit: string;
  length: string;
  lengthUnit: string;
  width: string;
  widthUnit: string;
  height: string;
  heightUnit: string;
  notWetWeight: number;
  notWetWeightUnit: string;
  wetWeight: number;
  wetWeightUnit: string;
  description: string;
  totalDuration?: number | string;
  totalWeightPercent?: string;
  predecessorId?: string | number;
  sort?: number;
  equipmentProcesses?: EquipmentProcess[];
}
