import { call, clean } from "@/api/ApiClient";

// PB3: files attached to a record through the whole chain (api/v1/attachments) and the request dossier (api/v1/dossier).
// Owner types and kinds are numbers (see composables/attachments.ts). Nothing is deleted: before approval the uploader removes (logged),
// afterwards the file is voided with a reason or superseded by a newer revision.

export const getAttachmentPanel = (ownerType: number, ownerId: number) => call("get", "attachments", undefined, { ownerType, ownerId });

export interface AttachmentUpload {
  ownerType: number;
  ownerId: number;
  kind: number;
  title: string;
  revision: string;
  supersedesId?: number | null;
  file: File;
}

export function uploadAttachment(u: AttachmentUpload) {
  const form = new FormData();
  form.append("OwnerType", String(u.ownerType));
  form.append("OwnerId", String(u.ownerId));
  form.append("Kind", String(u.kind));
  if (u.title) form.append("Title", u.title);
  if (u.revision) form.append("Revision", u.revision);
  if (u.supersedesId) form.append("SupersedesId", String(u.supersedesId));
  form.append("File", u.file);
  return call("post", "attachments", form);
}

export const voidAttachment = (id: number, reason: string) => call("post", "attachments/void", { id, reason });
export const removeAttachment = (id: number) => call("delete", "attachments/" + id);

/** پرونده درخواست: the whole chain by any number (request, inquiry, order, receipt, inspection, NCR, invoice, payment, petty cash). */
export const getDossier = (number: string, kind?: string) => call("get", "dossier", undefined, clean({ number, kind }));
