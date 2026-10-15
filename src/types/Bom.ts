import type _Process from "@/types/Process";

export default interface Bom {
  id?: number | string;
  assemblyId: number | string;
  positionNumber: number;
  title: string;
  count: number;
  unit: string;
  material: string;
  standard: string;
  diagonal: number;
  diagonalUnit: string;
  length: number;
  lengthUnit: string;
  width: number;
  widthUnit: string;
  height: number;
  heightUnit: string;
  notWetWeight: number;
  notWetWeightUnit: string;
  wetWeight: number;
  wetWeightUnit: string;
  description: string;
  sort?: number;
  predecessorId?: number | string;
  totalWeightPercent?: string;
  processes?: _Process[];
}
