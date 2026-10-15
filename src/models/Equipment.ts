import type EquipmentAssembly from "@/types/EquipmentAssembly";
import type EquipmentBom from "@/types/EquipmentBom";
import type EquipmentProcess from "@/types/EquipmentProcess";
import type AttachFile from "@/types/AttachFile";

export const EQUIPMENT_ASSEMBLY_MODEL: EquipmentAssembly = {
  equipmentId: null,
  id: null,
  assemblerDuration: null,
  code: "",
  title: "",
  children: [],
};

export const EQUIPMENT_BOM_MODEL: EquipmentBom = {
  id: null,
  title: null,
  count: 0,
  equipmentAssemblyId: null,
  description: null,
  material: null,
  unit: "0",
  diagonalUnit: "1",
  length: "0",
  lengthUnit: "1",
  width: "0",
  widthUnit: "1",
  height: "0",
  heightUnit: "1",
  wetWeight: 0,
  wetWeightUnit: "1",
  notWetWeight: 0,
  notWetWeightUnit: "1",
  standard: null,
  diagonal: "0",
  positionNumber: 0,
};

export const EQUIPMENT_BOM_PREDECESSOR_MODEL = {
  equipmentBomId: null,
  predecessorId: null,
  equipmentBomList: [] as EquipmentBom[],
};

export const EQUIPMENT_ASSEMBLY_UPDATE_MODAL_MODEL = {
  visible: false,
  isEdit: false,
  assembly: null as EquipmentAssembly,
  parentId: null,
  parentTitle: "",
};

export const EQUIPMENT_BOM_UPDATE_MODAL_MODEL = {
  visible: false,
  isEdit: false,
  bom: null as EquipmentBom,
  otherBoms: [] as EquipmentBom[],
  assemblyId: null,
};

export const EQUIPMENT_PROCESS_UPDATE_MODAL_MODEL = {
  visible: false,
  isEdit: false,
  process: null as EquipmentProcess,
  bomId: null,
};

export const EQUIPMENT_PROCESS_MODEL: EquipmentProcess = {
  id: null,
  title: "",
  equipmentBomId: null,
  enterCommodityCode: "",
  exitCommodityCode: "",
  processDuration: 0,
  mapDuration: 0,
  masterDuration: 0,
  programPrice: 0,
  weightPercent: "0.00",
  type: null,
  files: [],
  workflowId: null,
};

export const EQUIPMENT_PROCESS_FILE_MODEL: AttachFile = {
  id: null,
  description: "",
  name: "",
  code: "",
  size: "",
  file: null,
  mimeType: "",
};