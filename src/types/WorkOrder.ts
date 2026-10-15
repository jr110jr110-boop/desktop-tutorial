import type Assembly from "@/types/Assembly";

export default interface WorkOrder {
  id?: string | number;
  contractId: string | number;
  equipmentId: string | number;
  code: string;
  title: string;
  description: string;
  weightPercent: number;
  count: number;
  condition: string;
  conditionDescription?: string;
  assemblies?: Assembly[];
}
