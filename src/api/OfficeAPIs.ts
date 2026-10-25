import { call, clean, http, rowsOf, saveBlob } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// Office automation (api/v1/office, release O1). Every answer is { success, message, data }; a rule violation answers HTTP 200 with success=false
// (the helpers return it as the error). Lists are paged ({ data: [], currentPage, totalItems }; read them with rowsOf() or usePagedList()),
// except shifts, devices, request-types and org/tree, which answer plain arrays / a tree.
// Enums are numbers (see composables/office.ts for the labels). Dates in the JSON are ISO (yyyy-mm-dd or ISO date-time); the "...Text" fields are Persian.
// The office controllers need only a signed-in user; the "Who" part of each doc comment is checked inside the services. Rejected calls come back as errors.
//
// Files: upload with uploadFile() of InventoryAPIs (returns the document id), then pass the id in attachments / documentId / receiptDocumentId.
// Letter files are read ONLY with downloadLetterFile() (authenticated, blob); never put a download URL in an <a href> or <img src>.
const of = "office/";
const at = "office/attendance/";

type Method = "get" | "post" | "put" | "delete";
async function fcall(method: Method, url: string, body?: any, params?: any): Promise<[any, null] | [null, Error | any]> {
  const [data, error] = await call(method, url, body, params);
  return error ? [null, error] : [camelize(data), null];
}
const get = (url: string, params?: any) => fcall("get", url, undefined, clean(params));
const post = (url: string, body?: any) => fcall("post", url, body);

/** Paging + free-text filter of every list: { PageNumber, PageSize, Contains }. usePagedList() adds PageNumber and PageSize. */
export type OfficePageParams = { PageNumber?: number; PageSize?: number; Contains?: string | null; [key: string]: any };

// ---------------------------------------------------------------- organisation, me
/** UnitDto { id, code, title, parentId, parentTitle, managerPositionId, managerPositionTitle, isActive, positionCount }. Who: everyone. */
export const getUnits = (params?: OfficePageParams) => get(of + "units", params);
/** SaveUnitDto { id?, code, title, parentId?, isActive }. Who: HR, PMO, CEO (OfficeRoles.orgAdmins). */
export const saveUnit = (dto: any) => post(of + "units", dto);

/** params: PositionQuery { unitId, userId, signersOnly } + paging. PositionDto { id, title, unitId, unitTitle, userId, userName, employeeId, isManager, canSign, isActive, holderHasSignature }. Who: everyone. */
export const getPositions = (params?: OfficePageParams) => get(of + "positions", params);
/**
 * SavePositionDto { id?, title, unitId, userId?, employeeId?, isManager, canSign, isActive }. Who: HR, PMO, CEO.
 * Assigning or changing the holder: HR; the CEO only for his own or a vacant position; never yourself.
 */
export const savePosition = (dto: any) => post(of + "positions", dto);

/** params: DelegationQuery { positionId, deputyUserId, currentOnly } + paging. DelegationDto has status 0 approved / 1 pending / 2 rejected, fromDateText, toDateText, current. Who: everyone. */
export const getDelegations = (params?: OfficePageParams) => get(of + "delegations", params);
/**
 * SaveDelegationDto { positionId, deputyUserId, fromDate, toDate, reason }. Who: the holder of the position (approved at once), or HR / PMO / CEO.
 * A delegation made by somebody else is "pending" until the holder (or the CEO) approves it with approveDelegation().
 */
export const saveDelegation = (dto: any) => post(of + "delegations", dto);
/** Who: the holder or the creator of the delegation, or HR, PMO, CEO. */
export const cancelDelegation = (id: number) => post(of + `delegations/${id}/cancel`);
/** Approves a pending delegation. Who: the holder of the position or the CEO, never the one who made it. (Route found in the controller; not in the O1 report.) */
export const approveDelegation = (id: number) => post(of + `delegations/${id}/approve`);
/** Rejects a pending delegation. Who: the holder of the position or the CEO. (Route found in the controller; not in the O1 report.) */
export const rejectDelegation = (id: number) => post(of + `delegations/${id}/reject`);

/** OrgNodeDto tree { id, code, title, managerPositionId, positions: PositionDto[], children: OrgNodeDto[] }. Who: everyone. */
export const getOrgTree = () => get(of + "org/tree");
/** MyOfficeDto { userId, userName, positions: [{ positionId, title, unitTitle, onBehalf, canSign, isManager }], hasSignature, signatureDocumentId, employeeId }. Who: everyone. */
export const getMyOffice = () => get(of + "me");
/** SaveSignatureDto { documentId } of an uploaded image; returns MyOfficeDto. Who: everyone (own signature). */
export const saveMySignature = (documentId: string) => post(of + "me/signature", { documentId });

// ---------------------------------------------------------------- inbox
/**
 * params: InboxQuery { status 0..3, includeClosed, kind 1..6, sourceModule, sourceKey } + paging; default = open items (Unread, Read).
 * InboxItemDto { id, kind, title, sourceModule, sourceType, sourceId, sourceKey, link, dueDate, dueDateText, overdue, priority, status, createdAt, createdAtText,
 * readAt, closedAt, recipientPositionId, recipientPositionTitle, recipientRole, closedReason }. Who: everyone (own items: own position, delegated position, own user, own role).
 */
export const getInbox = (params?: OfficePageParams) => get(of + "inbox", params);
/** InboxCountsDto { unread, open, overdue, byKind: { Letter, Referral, Approval, Task, Alert, MeetingAction } }. Who: everyone. */
export const getInboxCounts = () => get(of + "inbox/counts");
/** action: read, unread, done (done only for tasks, alerts and information items; approvals, referrals and meeting actions close by their own action). Who: the owner of the item. */
export const inboxAction = (id: number, action: "read" | "unread" | "done") => post(of + "inbox/act", { id, action });

// ---------------------------------------------------------------- letters
/**
 * params: LetterQuery { kind 1..3, status 0..7, classification 0..2, number, subject, party, dateFrom, dateTo, relatedKind, relatedRecordId, mineOnly, waitingForMe } + paging (Contains too).
 * LetterListDto { id, kind, number, date, dateText, subject, status, classification, urgency, senderName, recipientNames, imported, metadataOnly }.
 * Who: everyone, only the letters the caller may see. metadataOnly=true (the secretariat on a Confidential or Secret letter): no subject, body, attachments or notes.
 */
export const getLetters = (params?: OfficePageParams) => get(of + "letters", params);
/**
 * LetterDto = list fields + bodyHtml, senderPositionId, senderPartyId, incomingNumber, incomingDate(Text), signerPositionId/Title, approvalRound, signedAt(Text), signedByName,
 * signedOnBehalfOfPosition, signatureDocumentId, contentHash, paperCopy, deliveryMethod, sentDate(Text), deliveryReceipt, deliveryReceiptDocumentId, replyToLetterId, followUpLetterId,
 * templateId, cancelReason, createdByName, recipients, approvals, attachments [{ documentId, fileName }], links, referrals (tree with children and entries), allowedActions, rowVersion.
 * allowedActions values: save, submit, approve, return, sign, register, send, receipt, archive, refer, cancel.
 * Who: the writer, the positions of the chain / recipients / referral tree, the CEO; SECRETARIAT for numbered letters (Normal and Confidential in full, files and search included; Secret as register data only, metadataOnly). Others get "not found".
 */
export const getLetter = (id: number) => get(of + "letters/" + id);
/**
 * SaveLetterDto { id?, kind, date?, subject, bodyHtml, senderPositionId?, senderPartyId?, classification, urgency, incomingNumber?, incomingDate?, signerPositionId?, paperCopy,
 * replyToLetterId?, templateId?, recipients [{ type 1 to / 2 cc, positionId?, partyId? }], attachments [{ documentId, fileName }], links [{ kind, recordId?, text? }], register, rowVersion }.
 * tenderId? or contractId? (at most one; the code of the tender or project goes into the number; free while the letter is a draft, frozen once it is numbered). The answer has tenderId, contractId, linkCode, linkTitle.
 * Who: everyone for outgoing / internal drafts; incoming letters: SECRETARIAT only (register=true numbers and delivers at once). No edit after signing; a refused call takes no number.
 */
export const saveLetter = (dto: any) => post(of + "letters", dto);
/**
 * LetterActionDto { id, action, approvalChain? (submit: positions in order; empty = template chain), actingPositionId? (approve/return/sign when you hold several), comment?,
 * deliveryMethod? (send: 1 hand, 2 post, 3 courier, 4 email, 5 fax), date?, receipt?, receiptDocumentId? (receipt), rowVersion }.
 * action: submit, approve, return, sign, register, send, receipt, archive, cancel.
 * Who: submit - the writer; approve / return / sign - the position whose turn it is (or its deputy); register / send / receipt / archive - SECRETARIAT;
 * cancel - the writer (unnumbered) or SECRETARIAT (numbered, comment = reason required). Show a button only when the action is in letter.allowedActions.
 */
export const letterAction = (dto: { id: number; action: string; approvalChain?: number[] | null; actingPositionId?: number | null; comment?: string | null; deliveryMethod?: number | null; date?: string | null; receipt?: string | null; receiptDocumentId?: string | null; rowVersion?: string | null }) =>
  post(of + "letters/action", {
    id: dto.id, action: dto.action, approvalChain: dto.approvalChain || null, actingPositionId: dto.actingPositionId || null, comment: dto.comment || null,
    deliveryMethod: dto.deliveryMethod || null, date: dto.date || null, receipt: dto.receipt || null, receiptDocumentId: dto.receiptDocumentId || null, rowVersion: dto.rowVersion || null,
  });
/** LetterVerifyDto { letterId, number, signed, valid, storedHash, computedHash, changedDocumentIds[] }. Who: anyone with full access to the letter. */
export const verifyLetter = (id: number) => get(of + `letters/${id}/verify`);
/**
 * ReferLetterDto { letterId, parentReferralId?, fromPositionId?, toPositionIds[], instruction, action 1 for action / 2 for information / 3 for comment, dueDate? }.
 * Who: a holder of the letter (a position that received it / is in the referral tree). Show the button when "refer" is in allowedActions.
 */
export const referLetter = (dto: any) => post(of + "letters/refer", dto);
/** ReferralActionDto { id, action: comment | close | cancel, text, actingPositionId? }. Who: the receiving position (comment, close); cancel - the sender or SECRETARIAT. */
export const referralAction = (id: number, action: "comment" | "close" | "cancel", text?: string | null, actingPositionId?: number | null) =>
  post(of + "letters/referral-action", { id, action, text: text || null, actingPositionId: actingPositionId || null });

/**
 * Downloads a file of a letter (attachment, signature image or delivery receipt) with the authenticated client (Bearer token) as a blob and saves it.
 * Returns an error message, or null when the file was saved. This is the ONLY way letter files are read (the generic Documents download answers 404 for them).
 * Who: somebody who may read the letter in full; everybody else (and the secretariat on Confidential / Secret letters) gets 404.
 */
export async function downloadLetterFile(letterId: number, documentId: string, fileName?: string): Promise<string | null> {
  try {
    const response = await http.get(of + `letters/${letterId}/attachments/${documentId}`, { responseType: "blob" });
    const type = response.headers["content-type"] || "";
    if (type.includes("application/json")) {
      const json = JSON.parse(await (response.data as Blob).text());
      return json.message || "خطا";
    }
    const disposition = response.headers["content-disposition"] || "";
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    saveBlob(response.data, fileName || (match ? decodeURIComponent(match[1]) : "letter-file"));
    return null;
  } catch (error: any) {
    if (error?.response?.status === 404) return "فایل پیدا نشد یا اجازه دسترسی به آن را ندارید.";
    if (error?.response?.status === 403) return "شما اجازه این کار را ندارید.";
    if (error?.response?.status === 401) return "دوباره وارد سامانه شوید.";
    return error?.message || "خطا";
  }
}

/**
 * Same file as an object URL for an inline preview (image / PDF in an <img> or <iframe>). The caller must URL.revokeObjectURL() it.
 * Returns [url, null] or [null, message]. Uses the authenticated client; never a bare URL.
 */
export async function openLetterFile(letterId: number, documentId: string): Promise<[string, null] | [null, string]> {
  try {
    const response = await http.get(of + `letters/${letterId}/attachments/${documentId}`, { responseType: "blob" });
    const type = response.headers["content-type"] || "";
    if (type.includes("application/json")) {
      const json = JSON.parse(await (response.data as Blob).text());
      return [null, json.message || "خطا"];
    }
    return [URL.createObjectURL(response.data as Blob), null];
  } catch (error: any) {
    if (error?.response?.status === 404) return [null, "فایل پیدا نشد یا اجازه دسترسی به آن را ندارید."];
    return [null, error?.message || "خطا"];
  }
}

/** Authenticated download of a stored document (sick-leave certificate, decision evidence). Returns an error text or null. */
export async function downloadDocument(documentId: string, fileName?: string): Promise<string | null> {
  try {
    const response = await http.get("Documents", { params: { DocumentId: documentId }, responseType: "blob" });
    const type = response.headers["content-type"] || "";
    if (type.includes("application/json")) {
      const json = JSON.parse(await (response.data as Blob).text());
      return json.message || "خطا";
    }
    saveBlob(response.data, fileName || "file");
    return null;
  } catch (error: any) {
    if (error?.response?.status === 404) return "فایل پیدا نشد یا اجازه دسترسی به آن را ندارید.";
    return error?.message || "خطا";
  }
}

/** A stored document as an object URL for a preview (the caller revokes it). Returns [url, null] or [null, message]. */
export async function openDocument(documentId: string): Promise<[string, null] | [null, string]> {
  try {
    const response = await http.get("Documents", { params: { DocumentId: documentId }, responseType: "blob" });
    const type = response.headers["content-type"] || "";
    if (type.includes("application/json")) {
      const json = JSON.parse(await (response.data as Blob).text());
      return [null, json.message || "خطا"];
    }
    return [URL.createObjectURL(response.data as Blob), null];
  } catch (error: any) {
    return [null, error?.message || "خطا"];
  }
}

/** ExternalPartyDto { id, name, kind 1 customer / 2 supplier / 3 organisation, providerId, customerId, address, phone, email, isActive } + paging (Contains). Who: everyone. */
export const getParties = (params?: OfficePageParams) => get(of + "parties", params);
/** SaveExternalPartyDto { id?, name, kind, providerId?, customerId?, address, phone, email, isActive }. Who: everyone (no role check in the service). */
export const saveParty = (dto: any) => post(of + "parties", dto);

/** NumberingRuleDto { id, kind, unitId, unitCode, pattern, isActive, preview }. Who: everyone reads. */
export const getNumberingRules = (params?: OfficePageParams) => get(of + "numbering-rules", params);
/**
 * SaveNumberingRuleDto { id?, kind, unitId?, pattern, isActive }. Tokens {yyyy} {yy} {seq} or {seq:n} (required; plain {seq} is not padded) {unit} {kind} and {code}.
 * {code} is the code of the tender or project the letter is linked to followed by a dash ("T-1405-17-"), and nothing when the letter has no link.
 * Company defaults: incoming INN-و-{code}{yyyy}-{seq}, outgoing INN-{code}{yyyy}-{seq}, internal INN-د-{code}{yyyy}-{seq}. Who: SECRETARIAT, PMO, CEO (OfficeRoles.letterAdmins).
 */
export const saveNumberingRule = (dto: any) => post(of + "numbering-rules", dto);

/** LetterTemplateDto { id, title, letterKind, headerHtml, footerHtml, bodyHtml, approvalChain[], signerPositionId, isActive }. Who: everyone reads. */
export const getLetterTemplates = (params?: OfficePageParams) => get(of + "letter-templates", params);
/** SaveLetterTemplateDto (same fields). Who: SECRETARIAT, PMO, CEO. */
export const saveLetterTemplate = (dto: any) => post(of + "letter-templates", dto);

/**
 * ImportLettersDto { rows: [{ number, date (Persian 1403/05/17 or ISO), kind (text), subject, party, fileNames[], documentIds[] }] } or { csv } (number, date, kind, subject, party, file names separated by |).
 * Returns { imported, skipped, errors[], letterIds[] }. Numbers are kept and the sequence is not consumed. Who: SECRETARIAT only.
 */
export const importLetters = (dto: { rows?: any[]; csv?: string | null }) => post(of + "letters/import", { rows: dto.rows || [], csv: dto.csv || null });
/**
 * The PDFs of the Rahkaran archive export (multipart: Files, NamePattern?, Csv?). Each PDF is attached to the imported letter whose old number is its file name without the extension
 * (or the group «number» of NamePattern, a regular expression); characters a file name cannot hold (/ \\ :) match _ . An optional manifest CSV is imported first.
 * Returns ArchiveFilesResultDto { import?, attached, alreadyAttached, unmatched, files: [{ fileName, number, letterId, result: attached | already-attached | unmatched | ambiguous | not-pdf | invalid, message }],
 * lettersWithoutFile[] (at most 200 numbers), lettersWithoutFileCount }. At most 300 files of 50 MB. Who: SECRETARIAT only.
 */
export const importLetterFiles = (files: File[], opts: { namePattern?: string | null; csv?: string | null } = {}) => {
  const form = new FormData();
  files.forEach((f) => form.append("Files", f, f.name));
  if (opts.namePattern?.trim()) form.append("NamePattern", opts.namePattern.trim());
  if (opts.csv?.trim()) form.append("Csv", opts.csv);
  return fcall("post", of + "letters/import/files", form);
};

/** Tenders and contracts (projects) a letter can be linked to: the existing list endpoints, all rows. Tender { id, code, description, company }, contract { id, code, title }. */
export const getLinkTenders = async (): Promise<any[]> => {
  const [data] = await call("get", "Tenders", undefined, { PageSize: 0 });
  return rowsOf(data);
};
export const getLinkContracts = async (): Promise<any[]> => {
  const [data] = await call("get", "Contracts", undefined, { PageSize: 0 });
  return rowsOf(data);
};

// ---------------------------------------------------------------- meetings and decisions
/** params: MeetingQuery { status 0..4, kind 1..6, dateFrom, dateTo, mineOnly } + paging. MeetingListDto { id, number, title, kind, startAt, startAtText, place, status, chairPositionTitle, openDecisions, overdueDecisions }. Who: everyone (own meetings). */
export const getMeetings = (params?: OfficePageParams) => get(of + "meetings", params);
/**
 * MeetingDto = list fields + endAt, chairPositionId, secretaryPositionId(Title), minutesApprovedAt(Text), minutesApprovedByName, cancelReason, createdByName, agenda [{ id, seq, title, minutes }],
 * attendees [{ id, positionId, positionTitle, userId, userName, externalName, present, signedAt(Text) }], decisions, links, allowedActions (save, hold, submit-minutes, approve-minutes, sign-minutes, cancel), rowVersion.
 * Who: creator, chair, secretary, attendees, decision owners, CEO.
 */
export const getMeeting = (id: number) => get(of + "meetings/" + id);
/** SaveMeetingDto { id?, title, kind, startAt, endAt?, place, chairPositionId?, secretaryPositionId?, agenda[], attendees[], links[], decisions [{ text, ownerPositionId, dueDate, agendaSeq }], rowVersion }. Who: everyone (creates); edit by creator / chair / secretary. */
export const saveMeeting = (dto: any) => post(of + "meetings", dto);
/**
 * MeetingActionDto { id, action, reason?, actingPositionId?, presentAttendeeIds? (hold), rowVersion }.
 * action: hold, submit-minutes, approve-minutes, sign-minutes, cancel (reason required).
 * Who: hold, submit-minutes, cancel - creator, chair, secretary; approve-minutes - the chair only (decisions then land in the owners' inboxes); sign-minutes - a present attendee.
 */
export const meetingAction = (dto: { id: number; action: string; reason?: string | null; actingPositionId?: number | null; presentAttendeeIds?: number[] | null; rowVersion?: string | null }) =>
  post(of + "meetings/action", { id: dto.id, action: dto.action, reason: dto.reason || null, actingPositionId: dto.actingPositionId || null, presentAttendeeIds: dto.presentAttendeeIds || null, rowVersion: dto.rowVersion || null });

/** params: DecisionQuery { status 0..2, overdueOnly, mineOnly, meetingId, ownerPositionId } + paging. DecisionDto { id, meetingId, meetingNumber, meetingTitle, seq, text, ownerPositionId, ownerPositionTitle, dueDate, dueDateText, status, overdue, doneNote, doneAt, doneByName, evidenceDocumentId }. Who: decisions of meetings with approved minutes that the caller may see. */
export const getDecisions = (params?: OfficePageParams) => get(of + "decisions", params);
/** action: done (note, evidenceDocumentId?) - the owner position or its deputy; cancel (note) - the chair, the creator or the CEO. */
export const decisionAction = (dto: { id: number; action: "done" | "cancel"; note?: string | null; evidenceDocumentId?: string | null; actingPositionId?: number | null }) =>
  post(of + "decisions/action", { id: dto.id, action: dto.action, note: dto.note || null, evidenceDocumentId: dto.evidenceDocumentId || null, actingPositionId: dto.actingPositionId || null });
/** Alerts the chairs of overdue decisions now (the daily job does the same). Returns { overdueDecisions, alertsCreated }. Who: SECRETARIAT, PMO, CEO, HR (OfficeRoles.overdueCheck). */
export const checkOverdueDecisions = () => post(of + "decisions/check-overdue");

// ---------------------------------------------------------------- attendance (api/v1/office/attendance)
/** ShiftDto[] (plain array, not paged) { id, title, start "HH:mm", end, breakMinutes, nightStart, nightEnd, workDays[] (0 = Sunday .. 6 = Saturday), isActive, crossesMidnight, scheduledMinutes }. Who: everyone. */
export const getShifts = () => get(at + "shifts");
/** SaveShiftDto { id?, title, start, end, breakMinutes, nightStart, nightEnd, workDays[], isActive }. Who: HR, PMO, CEO (OfficeRoles.attendanceAdmins). */
export const saveShift = (dto: any) => post(at + "shifts", dto);
/** params: ShiftAssignmentQuery { employeeId, shiftId } + paging. ShiftAssignmentDto { id, employeeId, employeeName, personnelCode, shiftId, shiftTitle, fromDate, fromDateText, toDate, toDateText }. Who: HR, PMO, CEO (controller verified: the service refuses others on read). */
export const getShiftAssignments = (params?: OfficePageParams) => get(at + "shift-assignments", params);
/** SaveShiftAssignmentDto { employeeId, shiftId, fromDate, toDate? }. Who: HR, PMO, CEO. */
export const saveShiftAssignment = (dto: any) => post(at + "shift-assignments", dto);

/** DeviceDto[] (plain array) { id, code, title, location, isActive, lastPushAt, lastPushAtText }. Who: HR, PMO, CEO (the service refuses others on read too). */
export const getDevices = () => get(at + "devices");
/** SaveDeviceDto { id?, code, title, location, isActive, rotateKey }. Returns DeviceKeyDto { device, apiKey }: the key is shown ONCE (only its hash is stored). Who: HR, PMO, CEO. */
export const saveDevice = (dto: any) => post(at + "devices", dto);

/** params: ClockQuery { employeeId, from, to, source 1..3 } + paging. ClockEventDto { id, employeeId, employeeName, personnelCode, timestamp, timestampText, deviceCode, direction, source, reason, createdByName }. Who: HR, CEO see all; others only their own. */
export const getClockEvents = (params?: OfficePageParams) => get(at + "clock", params);
/** ManualClockDto { employeeId, timestamp, direction 0 unknown / 1 in / 2 out, reason (required) }. Audited; refused in a month whose payroll is approved or in the future. Who: HR. */
export const addManualClock = (dto: any) => post(at + "clock/manual", dto);
/** ImportClockDto { deviceCode?, rows: [{ code, date (Persian or ISO), time, device, direction }] } or { csv } (code, date, time, device, direction). Returns { imported, duplicates, rejected, errors[] }. Who: HR. */
export const importClock = (dto: { deviceCode?: string | null; rows?: any[]; csv?: string | null }) => post(at + "clock/import", { deviceCode: dto.deviceCode || null, rows: dto.rows || [], csv: dto.csv || null });

/**
 * Excel export of the attendance software (PW Kara; devices US 10C and M Face 302), multipart File + Sheet?. Returns ClockFilePreviewDto { sheets[], sheet, rowCount,
 * columns: [{ letter, header, samples[] }], rows: string[][] (first rows), suggested: ClockFileMapping (guessed from the headings) }. Who: HR.
 */
export const previewClockFile = (file: File, sheet?: string | null) => {
  const form = new FormData();
  form.append("File", file, file.name);
  if (sheet) form.append("Sheet", sheet);
  return fcall("post", at + "clock/import-file/preview", form);
};
/**
 * ClockFileMapping { sheet?, headerRow (1; 0 = no heading), firstDataRow (0 = the row after the heading), codeColumn, dateColumn + timeColumn or dateTimeColumn, directionColumn?, inValues?, outValues?,
 * deviceColumn?, siteColumn?, defaultDeviceCode? }; a column is a heading text or a letter. Persian or Gregorian dates both work. Send the file with a mapping or a saved profileId; saveProfileAs saves
 * the mapping under that name. Returns { imported, duplicates, unknown, unknownCodes[], rowsRead, rejected, errors[] }; unknown personnel numbers are reported, never created; importing the same file again stores nothing. Who: HR.
 */
export const importClockFile = (file: File, opts: { mapping?: any; profileId?: number | null; sheet?: string | null; saveProfileAs?: string | null }) => {
  const form = new FormData();
  form.append("File", file, file.name);
  if (opts.mapping) form.append("Mapping", JSON.stringify(opts.mapping));
  if (opts.profileId) form.append("ProfileId", String(opts.profileId));
  if (opts.sheet) form.append("Sheet", opts.sheet);
  if (opts.saveProfileAs?.trim()) form.append("SaveProfileAs", opts.saveProfileAs.trim());
  return fcall("post", at + "clock/import-file", form);
};
/** The saved column mappings: [{ id, name, mapping }]. Who: HR, CEO. */
export const getClockImportProfiles = () => get(at + "clock/import-profiles");
/** SaveClockImportProfileDto { id?, name, mapping, delete? }. Who: HR. */
export const saveClockImportProfile = (dto: { id?: number | null; name: string; mapping: any; delete?: boolean }) => post(at + "clock/import-profiles", { id: dto.id || null, name: dto.name, mapping: dto.mapping, delete: !!dto.delete });

/**
 * Device push (POST attendance/push) is for the devices on the LAN: anonymous, headers X-Device-Code and X-Device-Key, HTTP 401 on a bad code or key.
 * It is NOT called from the browser, so there is intentionally no function for it.
 */

/**
 * AttendancePolicyDto { graceMinutes (15), maxOvertimeMinutesPerDay? (empty = none), overtimeNeedsPreApproval (false), roundingMinutes,
 * monthlyOvertimeCapMinutes (1320 = 22 hours; overtime above it is not counted), delayPenaltyFactor (3: a delay beyond the grace costs delay x factor of working time, 0..10) }.
 * Who: read by everyone.
 */
export const getAttendancePolicy = () => get(at + "policy");
/** Same DTO. Who: HR, PMO, CEO. */
export const saveAttendancePolicy = (dto: any) => post(at + "policy", dto);

/** ComputeDaysDto { fromDate, toDate, employeeId? }. Returns { days, employees, skippedLocked, employeesWithoutShift }. Who: HR, CEO (the service uses AttendanceReaders; PMO is refused). */
export const computeAttendance = (dto: { fromDate: string; toDate: string; employeeId?: number | null }) => post(at + "compute", { fromDate: dto.fromDate, toDate: dto.toDate, employeeId: dto.employeeId || null });
/**
 * params: DayQuery { employeeId, from, to, status 1..6 } + paging.
 * AttendanceDayDto { employeeId, employeeName, personnelCode, date, dateText, shiftId, shiftTitle, status, nonWorkingDay, firstIn, lastOut, workedMinutes, delayMinutes, earlyLeaveMinutes,
 * overtimeMinutes, nightMinutes, holidayMinutes, absenceDays, unpaidLeaveDays, paidLeaveDays, sickLeaveDays, missionDays, note }. Who: HR, CEO see all; others only their own.
 */
export const getAttendanceDays = (params?: OfficePageParams) => get(at + "days", params);
/**
 * CloseMonthDto { year, month (Persian), acceptIncompleteAsAbsent }. Returns { year, month, sheets, skippedLocked, incompleteDays, warnings[], rows: SheetRowDto[] }.
 * Refused while incomplete days exist unless acceptIncompleteAsAbsent=true; refused for a month whose payroll run is approved or later. Who: HR.
 */
export const closeAttendanceMonth = (year: number, month: number, acceptIncompleteAsAbsent = false) => post(at + "close-month", { year, month, acceptIncompleteAsAbsent });
/** MonthStatusDto { year, month, closed, closedAt, closedAtText, sheets, lockedByPayroll } (Persian year / month). Who: HR, CEO, PAYROLL, FINANCEMANAGER (OfficeRoles.monthReaders). */
export const getAttendanceMonth = (year: number, month: number) => get(at + `month/${year}/${month}`);
/** WorkedHoursQuery { employeeIds[], from, to } returns [{ employeeId, date, workedMinutes, workedHours }] for the production time-booking check. Who: HR, CEO, PMO, PROJECTMANAGER, PLANNING, CONSTRUCTION, FINANCEMANAGER. */
export const getWorkedHours = (employeeIds: number[], from: string, to: string) => post(at + "worked-hours", { employeeIds, from, to });

// ---------------------------------------------------------------- leave, mission, overtime
/** params: LeaveQuery { employeeId, status 0..5, kind 1..7, from, to, mineOnly, waitingForMe } + paging. LeaveDto: see getLeave. Who: own; the unit manager, HR and CEO also see theirs. */
export const getLeaves = (params?: OfficePageParams) => get(of + "leaves", params);
/**
 * LeaveDto { id, number, employeeId, employeeName, kind, fromDate, toDate, fromDateText, toDateText, fromTime, toTime, hours, direction, days, reason, attachmentDocumentId, status,
 * approverPositionId, approverPositionTitle, approvedByName, approvedOnBehalf, approvedAt, approverComment, recordedByName, recordedAt, rejectReason, allowedActions (save, submit, approve, record, reject, cancel), rowVersion }.
 */
export const getLeave = (id: number) => get(of + "leaves/" + id);
/**
 * SaveLeaveDto { id?, employeeId? (HR only, for somebody else), kind 1 daily / 2 hourly / 3 sick / 4 unpaid / 5 mission / 6 overtime / 7 forgotten clock, fromDate, toDate?, fromTime?, toTime?, hours?,
 * direction? (forgotten clock: 1 in / 2 out), reason, attachmentDocumentId?, submit, rowVersion }. Who: the employee for himself; HR for others. submit=true sends it for approval in the same call.
 */
export const saveLeave = (dto: any) => post(of + "leaves", dto);
/**
 * LeaveActionDto { id, action, comment?, actingPositionId?, rowVersion }. action: submit, approve, record, reject, cancel.
 * Who: submit / cancel - the employee or HR (after the start of an approved leave only HR cancels); approve - the manager position of the unit (or its deputy);
 * record - HR (never for himself; not for a forgotten-clock request); reject - the approver (HR for an approved one). Months with an approved payroll run are locked.
 */
export const leaveAction = (id: number, action: "submit" | "approve" | "record" | "reject" | "cancel", comment?: string | null, actingPositionId?: number | null, rowVersion?: string | null) =>
  post(of + "leaves/action", { id, action, comment: comment || null, actingPositionId: actingPositionId || null, rowVersion: rowVersion || null });
/**
 * Leave balance (query employeeId, year, month; all optional, defaults to the caller and today). LeaveBalanceDto { employeeId, year, carriedForward, entitled, used, lastMonth, entitlementRate,
 * projectedEntitlement, pendingDays, available }. Who: own; HR, CEO any.
 */
export const getLeaveBalance = (params?: { employeeId?: number | null; year?: number | null; month?: number | null }) => get(of + "leaves/balance", params);

// ---------------------------------------------------------------- general requests
/**
 * RequestTypeDto[] (plain array) { id, title, fields: [{ name, label, type text | number | date | select, required, options[] }], approvalChain[], approvalChainTitles[], targetNote, isActive }.
 * Who: everyone (inactive types only for HR, PMO, CEO). Field names are lowerCamel: the API camel-cases dictionary keys of "values".
 */
export const getRequestTypes = () => get(of + "request-types");
/** SaveRequestTypeDto { id?, title, fields[], approvalChain[] (positions in order; empty = the requester's unit manager), targetNote, isActive }. Who: HR, PMO, CEO. */
export const saveRequestType = (dto: any) => post(of + "request-types", dto);
/** params: RequestQuery { status 0..5, typeId, mineOnly, waitingForMe } + paging. Who: the requester, the approvers; HR, PMO, CEO see all. */
export const getRequests = (params?: OfficePageParams) => get(of + "requests", params);
/**
 * RequestDto { id, number, typeId, typeTitle, targetNote, requesterUserId, requesterName, values: { [fieldName]: text }, fields[], status, submittedAt(Text), completedAt(Text), note,
 * approvals: [{ step, positionId, positionTitle, status, actedByName, onBehalf, comment, actedAt(Text) }], allowedActions (save, submit, approve, reject, cancel, complete), rowVersion }.
 */
export const getRequest = (id: number) => get(of + "requests/" + id);
/** SaveRequestDto { id?, typeId, values: { [fieldName]: text }, submit, rowVersion }. Who: everyone (own requests). Dates in values are ISO. */
export const saveRequest = (dto: any) => post(of + "requests", dto);
/**
 * RequestActionDto { id, action, comment?, rowVersion }. action: submit, approve, reject, cancel, complete.
 * Who: submit / cancel - the requester; approve / reject - the position whose turn it is (or its deputy); complete - HR, PMO, CEO.
 */
export const requestAction = (id: number, action: "submit" | "approve" | "reject" | "cancel" | "complete", comment?: string | null, rowVersion?: string | null) =>
  post(of + "requests/action", { id, action, comment: comment || null, rowVersion: rowVersion || null });
