import type Equipment from "@/types/Equipment";
import type Contract from "@/types/Contract";

export default interface ContractEquipment {
  contractId: number | string;
  equipmentId: number | string;
  title?: string;
  equipment?: Equipment;
  contract?: Contract;
  count: number;
}
