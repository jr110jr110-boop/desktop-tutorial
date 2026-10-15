import type Bom from "@/types/Bom";

export default interface Assembly {
  id: number | string;
  equipmentId?: number | string;
  title: string;
  code: string;
  assemblerDuration: number;
  totalWeightPercent?: string;
  boms?: Bom[];
  workOrderId?: number | string;
}
