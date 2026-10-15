import type AttachFile from "@/types/AttachFile";

export default interface ProjectCharter {
  contractId: string | number;
  master: string;
  projectCharterFile: AttachFile;
}
