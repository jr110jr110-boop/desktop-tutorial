export default interface AttachFileToProccess {
  documentId: string;
  processId: number | string;
  description: string;
  major: number;
  minor: number;
  uploadCondition: number;
}
