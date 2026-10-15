import type AttachFile from "@/types/AttachFile";
import { getDocument } from "@/api/DocumentAPIs";
import showErrorNotification from "@/composables/showErrorNotification";

export const downloadFile = async (file: AttachFile, fileId) => {
  const [data, error] = await getDocument(fileId);
  if (!error) {
    const url = window.URL.createObjectURL(new Blob([data], { type: 'application/jpg' }));
    const link = document.createElement("a");
    link.href = url;
    link.setAttribute("download", file.description + file.mimeType);
    document.body.appendChild(link);
    link.click();
  } else showErrorNotification(error.message);
};