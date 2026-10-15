export default interface AttachFile {
  id?: string;
  code?: string;
  name?: string;
  description?: string;
  size?: string;
  file?: File;
  src?: any;
  mimeType?: string;
  isFinal?: boolean;
}
