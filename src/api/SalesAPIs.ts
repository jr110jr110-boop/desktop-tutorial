import { call, clean } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// Sales (api/v1/sales, release S1 part A): CRM, inquiries, the bid / no-bid gate, estimates, proposals, price lists, quotations, sales orders and the dashboard.
// Every answer is { success, message, data }; a rule violation answers HTTP 200 with success=false (the helpers return it as the error). Lists are paged
// ({ data: [], currentPage, totalItems }; read them with rowsOf() or usePagedList()), except the single-record GETs, GET customers/unlinked, GET settings,
// GET statistics/win-loss, GET dashboard and POST price-lists/suggest, which answer one object or a plain array.
// Enums are numbers (see composables/sales.ts for the labels). Money is in rial (decimal, whole numbers on documents). Dates in the JSON are ISO (yyyy-mm-dd or ISO date-time);
// the "...Text" fields are Persian (Jalali) and are what people read. Credit limits are null for roles outside creditViewers; the estimate / proposal cost fields are only
// returned to commercialReaders. The controllers need only a signed-in user; the "Who" part of each comment is checked inside the services (SalesRoles in composables/roles.ts).
// Concurrency: send the rowVersion you read; a stale one is refused with a Persian message.
//
// Query parameter names are the PascalCase property names of the C# query classes (ASP.NET binds them case-insensitively).
const sl = "sales/";

type Method = "get" | "post" | "put" | "delete";
async function fcall(method: Method, url: string, body?: any, params?: any): Promise<[any, null] | [null, Error | any]> {
  const [data, error] = await call(method, url, body, params);
  return error ? [null, error] : [camelize(data), null];
}
const get = (url: string, params?: any) => fcall("get", url, undefined, clean(params));
const post = (url: string, body?: any) => fcall("post", url, body);

/** Paging + free-text filter of every list: SlPageQuery { PageNumber (1), PageSize (25, max 500), Contains }. usePagedList() adds PageNumber and PageSize. */
export type SalesPageParams = { PageNumber?: number; PageSize?: number; Contains?: string | null; [key: string]: any };

// ---------------------------------------------------------------- customers
/**
 * params: SlCustomerQuery { Status 1..3, Type 1..4, OwnerUserId } + paging.
 * SlCustomerDto { id, name, economicCode, nationalId, postalCode, address, phone, employerName, isActive, profileId, type?, industry?, country, city, status, ownerUserId, ownerName,
 * creditLimitRial (null for roles outside creditViewers), paymentTermDays, note, contactCount, siteCount, hasPostedInvoices (name and codes then change only by finance), rowVersion }.
 * Who: SalesRoles.readers.
 */
export const getSlCustomers = (params?: SalesPageParams) => get(sl + "customers", params);
/** One SlCustomerDto. Who: readers. */
export const getSlCustomer = (id: number) => get(sl + "customers/" + id);
/**
 * SlSaveCustomerDto { id?, name, economicCode, nationalId, postalCode, address, phone, employerName, type?, industry?, country, city, status (1 prospect default), ownerUserId?, paymentTermDays?, note, rowVersion }.
 * The credit limit is NOT part of this DTO (see setSlCredit). Returns SlCustomerDto.
 * Who: SalesRoles.masterWriters (SALES, PMO); changing the name / economic code / national id of an existing customer also needs identityChangers (FINANCEMANAGER, FINANCIAL).
 */
export const saveSlCustomer = (dto: any) => post(sl + "customers", dto);
/** SlSetCreditDto { customerId, creditLimitRial? } (rial; empty clears the limit). Returns SlCustomerDto. Who: SalesRoles.creditSetters (FINANCEMANAGER). */
export const setSlCredit = (dto: { customerId: number; creditLimitRial?: number | null }) => post(sl + "customers/credit", dto);

/** params: SlContactQuery { CustomerId } + paging. SlContactDto { id, customerId, customerName, fullName, jobTitle, phone, email, isPrimary, isActive }. Who: readers. */
export const getSlContacts = (params?: SalesPageParams) => get(sl + "contacts", params);
/** SlSaveContactDto { id?, customerId, fullName, jobTitle, phone, email, isPrimary, isActive (true) }. Returns SlContactDto. Who: masterWriters (SALES, PMO). */
export const saveSlContact = (dto: any) => post(sl + "contacts", dto);

/** params: SlSiteQuery { CustomerId } + paging. SlSiteDto { id, customerId, customerName, title, address, country, coordinatesText }. Who: readers. */
export const getSlSites = (params?: SalesPageParams) => get(sl + "sites", params);
/** SlSaveSiteDto { id?, customerId, title, address, country, coordinatesText }. Returns SlSiteDto. Who: masterWriters. */
export const saveSlSite = (dto: any) => post(sl + "sites", dto);

/**
 * params: SlActivityQuery { CustomerId, InquiryId, ProposalId } + paging.
 * SlActivityDto { id, customerId, customerName, contactId, kind (1..5), activityDate, activityDateText, subject, notes, nextActionDate, nextActionDateText, inquiryId, proposalId,
 * serviceRequestId, ownerUserId, ownerName }. Who: readers.
 */
export const getSlActivities = (params?: SalesPageParams) => get(sl + "activities", params);
/**
 * SlSaveActivityDto { id?, customerId, contactId?, kind (1 call .. 5 letter), activityDate? (now), subject, notes, nextActionDate?, inquiryId?, proposalId?, serviceRequestId? }.
 * Who: SalesRoles.activityWriters (SALES, SERVICE); a SERVICE user edits only their own activities.
 */
export const saveSlActivity = (dto: any) => post(sl + "activities", dto);

// ---------------------------------------------------------------- legacy link (contracts and tenders to customers)
/**
 * Runs the one-time matcher: exact normalised match of the employer / company text of contracts and tenders to a customer. No body.
 * Returns SlLinkLegacyResultDto { linkedContracts, linkedTenders, alreadyLinked, unmatched: [{ sourceType (1 contract, 2 tender), sourceId, text }] }.
 * Who: SalesRoles.legacyLinkers (SALES, PMO, CEO).
 */
export const linkSlLegacy = () => post(sl + "customers/link-legacy");
/** SlManualLinkDto { sourceType (1 contract, 2 tender), sourceId, customerId }: a person links one unmatched contract / tender by hand. Returns SlLinkDto. Who: legacyLinkers. */
export const linkSlManual = (dto: { sourceType: number; sourceId: number; customerId: number }) => post(sl + "customers/link", dto);
/**
 * params: SlLinkQuery { CustomerId, SourceType 1..2 } + paging. SlLinkDto { id, sourceType, sourceId, sourceText, customerId, customerName, method (1 legacy match, 2 manual, 3 from proposal) }. Who: readers.
 */
export const getSlLinks = (params?: SalesPageParams) => get(sl + "customers/links", params);
/** PLAIN ARRAY (not paged) of SlLinkSourceDto { sourceType, sourceId, text }: the contracts and tenders that have no customer yet. Who: readers. */
export const getSlUnlinked = () => get(sl + "customers/unlinked");

// ---------------------------------------------------------------- settings, reason codes
/**
 * PLAIN ARRAY of SlSettingDto { key, title, value (decimal or null), note }. The only key today is ProposalApprovalThresholdRial: proposals above it (or below cost) need the CEO;
 * while it is empty the CEO approves every proposal. Who: readers.
 */
export const getSlSettings = () => get(sl + "settings");
/** SlSaveSettingDto { key, value? } (empty clears). Returns SlSettingDto. Who: SalesRoles.settingSetters (CEO). */
export const saveSlSetting = (dto: { key: string; value?: number | null }) => post(sl + "settings", dto);

/** params: SlReasonCodeQuery { Kind 1 won / 2 lost / 3 no-bid, IsActive } + paging. SlReasonCodeDto { id, code, title, kind, isActive }. Who: readers. */
export const getSlReasonCodes = (params?: SalesPageParams) => get(sl + "reason-codes", params);
/** SlSaveReasonCodeDto { id?, code, title, kind (1..3), isActive (true) }. Who: masterWriters (SALES, PMO). */
export const saveSlReasonCode = (dto: any) => post(sl + "reason-codes", dto);

// ---------------------------------------------------------------- inquiries
/**
 * params: SlInquiryQuery { Status 1..8, CustomerId, ProductType 1..9 } + paging.
 * SlInquiryDto { id, number, receivedDate, receivedDateText, customerId, customerName, contactId, channel, customerReference, title, productType, productTypeText, quantity, requiredDeliveryDate(Text),
 * proposalDueDate(Text), attachmentIds (comma separated text), status, statusText, tenderId, note, cancelReason, rowVersion }. Who: readers.
 */
export const getSlInquiries = (params?: SalesPageParams) => get(sl + "inquiries", params);
/** One SlInquiryDto. Who: readers. */
export const getSlInquiry = (id: number) => get(sl + "inquiries/" + id);
/**
 * SlSaveInquiryDto { id?, receivedDate? (today), customerId, contactId?, channel, customerReference, title, productType (1..9, default 9), quantity (1), requiredDeliveryDate?, proposalDueDate?,
 * attachmentIds, note, rowVersion }. Returns SlInquiryDto. Who: SalesRoles.writers (SALES).
 */
export const saveSlInquiry = (dto: any) => post(sl + "inquiries", dto);
/** SlInquiryActionDto { id, action, reason?, rowVersion }. action: review (start the review), cancel (reason required). Returns SlInquiryDto. Who: writers. */
export const inquiryAction = (dto: { id: number; action: "review" | "cancel"; reason?: string | null; rowVersion?: string | null }) =>
  post(sl + "inquiries/action", { id: dto.id, action: dto.action, reason: dto.reason || null, rowVersion: dto.rowVersion || null });

// ---------------------------------------------------------------- bid / no-bid gate
/**
 * params: SlBidDecisionQuery { Status 1..4, InquiryId } + paging. SlBidDecisionDto { id, inquiryId, inquiryNumber, inquiryTitle, customerName, status (1 draft, 2 waiting decision, 3 bid, 4 no-bid),
 * technicalCapabilityOk?, technicalNote, capacityOverloadWeeks (read from the production load), capacityCheckedUntil(Text), capacityNote, commercialRiskReviewed, commercialRiskNote,
 * paymentTermsReviewed, paymentTermsNote, guaranteesReviewed, guaranteesNote, sanctionCheck (0 not checked, 1 clear, 2 concern), sanctionNote, expectedMarginPercent, submittedAt(Text),
 * decidedAt(Text), decidedByUserId, decidedByName, decisionReason, tenderId (the system drafts a Tender when the decision is bid), touchedByMe (the current user created or edited it: the backend refuses their approval, so the screen hides the button), rowVersion }. Who: readers.
 */
export const getSlBidDecisions = (params?: SalesPageParams) => get(sl + "bid-decisions", params);
/** One SlBidDecisionDto by the INQUIRY id (not the decision id). Who: readers. */
export const getSlBidDecision = (inquiryId: number) => get(sl + "bid-decisions/" + inquiryId);
/**
 * SlSaveBidDecisionDto { inquiryId, technicalCapabilityOk?, technicalNote, capacityNote, commercialRiskReviewed, commercialRiskNote, paymentTermsReviewed, paymentTermsNote, guaranteesReviewed,
 * guaranteesNote, sanctionCheck (0..2), sanctionNote, expectedMarginPercent?, rowVersion }: the checklist (draft only). Returns SlBidDecisionDto. Who: writers (SALES).
 */
export const saveSlBidDecision = (dto: any) => post(sl + "bid-decisions", dto);
/** SlBidActionDto { inquiryId, rowVersion }: sales submits the checklist to the CEO (status 1 -> 2). Decision / reason are not used. Returns SlBidDecisionDto. Who: writers (SALES). */
export const submitSlBidDecision = (dto: { inquiryId: number; rowVersion?: string | null }) =>
  post(sl + "bid-decisions/submit", { inquiryId: dto.inquiryId, decision: null, reason: null, rowVersion: dto.rowVersion || null });
/**
 * SlBidActionDto { inquiryId, decision, reason?, rowVersion }. decision: "bid" or "nobid" (no-bid needs a reason; "bid" drafts the Tender). Returns SlBidDecisionDto.
 * Who: SalesRoles.bidDeciders (CEO only; SALES cannot decide in place of the CEO).
 */
export const decideSlBid = (dto: { inquiryId: number; decision: "bid" | "nobid"; reason?: string | null; rowVersion?: string | null }) =>
  post(sl + "bid-decisions/decide", { inquiryId: dto.inquiryId, decision: dto.decision, reason: dto.reason || null, rowVersion: dto.rowVersion || null });

// ---------------------------------------------------------------- estimates
/**
 * params: SlEstimateQuery { InquiryId, Status 1..3 } + paging. SlEstimateDto (same fields in the list) { id, number, inquiryId, inquiryNumber, revision, status (1 draft, 2 final, 3 superseded), note,
 * overheadPercent, contingencyPercent, targetMarginPercent, guaranteeAmount, guaranteeMonths, guaranteeRatePercentPerMonth, financingAmount, financingMonths, financingRatePercentPerMonth,
 * materialCost, labourCost, engineeringCost, subcontractCost, packingTransportCost, directCost, guaranteeCost, financingCost, riskCost, contingencyCost, costBeforeOverhead, overheadCost, totalCost,
 * price (null while an input is missing), missingCostLines, missingInputs (text), finalizedAt(Text),
 * equipments: [{ equipmentId, equipmentName, count }],
 * lines: [{ id, rowNumber, kind (1..5), description, equipmentId, equipmentBomId, itemId, itemCode, itemName, workCenterId, workCenterName, unit, quantity, unitCost (null = missing; send null on save to keep a system-sourced cost, a number makes it Manual), costSource (0 manual, 1 average cost, 2 last purchase, 3 last purchase order, 4 work-centre rate), amount, note }],
 * risks: [{ id, title, probabilityPercent, impactAmount, expectedCost }], rowVersion }. Every percentage is an input (nothing is seeded). Who: SalesRoles.commercialReaders.
 */
export const getSlEstimates = (params?: SalesPageParams) => get(sl + "estimates", params);
/** One SlEstimateDto with lines, equipments and risks. Who: commercialReaders. */
export const getSlEstimate = (id: number) => get(sl + "estimates/" + id);
/**
 * SlSaveEstimateDto { id?, inquiryId, note, overheadPercent?, contingencyPercent?, targetMarginPercent?, guaranteeAmount?, guaranteeMonths?, guaranteeRatePercentPerMonth?, financingAmount?,
 * financingMonths?, financingRatePercentPerMonth?, lines: [{ kind (1..5), description, equipmentId?, equipmentBomId?, itemId?, workCenterId?, unit, quantity, unitCost? (empty: filled from the cost sources;
 * still empty = missing), note }], risks: [{ title, probabilityPercent, impactAmount }], rowVersion }. Saves a DRAFT estimate; lines and risks replace the previous ones. Returns SlEstimateDto.
 * Who: writers (SALES).
 */
export const saveSlEstimate = (dto: any) => post(sl + "estimates", dto);
/**
 * SlEstimateFromEquipmentDto { inquiryId, equipments: [{ equipmentId, count (1) }], overheadPercent?, contingencyPercent?, targetMarginPercent? }: the system drafts the lines from equipment templates
 * (BOM x cost sources, routing standards x work-centre rates). Returns SlEstimateDto (draft). Who: writers (SALES).
 */
export const slEstimateFromEquipment = (dto: { inquiryId: number; equipments: { equipmentId: number; count: number }[]; overheadPercent?: number | null; contingencyPercent?: number | null; targetMarginPercent?: number | null }) =>
  post(sl + "estimates/from-equipment", dto);
/** SlEstimateActionDto { id, action, rowVersion }. action: finalize (draft -> final), revise (a new draft revision; the old one becomes superseded). Returns SlEstimateDto. Who: writers (SALES). */
export const estimateAction = (dto: { id: number; action: "finalize" | "revise"; rowVersion?: string | null }) =>
  post(sl + "estimates/action", { id: dto.id, action: dto.action, rowVersion: dto.rowVersion || null });

// ---------------------------------------------------------------- proposals
/**
 * params: SlProposalQuery { Status 1..7, InquiryId, CustomerId } + paging.
 * SlProposalDto { id, number, revision, inquiryId, inquiryNumber, inquiryTitle, customerId, customerName, productType, estimateId, technicalDocumentId (guid), commercialDocumentId (guid), validityDays, price (rial, before VAT),
 * deliveryWeeks, paymentTermsText, guaranteesText, deviationsText, vatPercent, advancePercent, retentionPercent, latePenaltyPercentPerWeek, penaltyCapPercent, clientReviewDays, status, statusText,
 * estimateTotalCost, belowCost, needsCeo, approvedAt(Text), approvedByUserId, approvedByName, approvalNote, submittedAt(Text), customerReference, resultAt(Text), resultReasonId, resultReason, competitor,
 * competitorPrice, lessonsLearned, contractId (set when Won), contractReviewId (the contract review drafted in project control when Won), touchedByMe (the current user created or edited it: the backend refuses their approval, so the screen hides the button), rowVersion }. Who: SalesRoles.commercialReaders.
 */
export const getSlProposals = (params?: SalesPageParams) => get(sl + "proposals", params);
/** One SlProposalDto. Who: commercialReaders. */
export const getSlProposal = (id: number) => get(sl + "proposals/" + id);
/**
 * SlSaveProposalDto { id?, inquiryId, estimateId (a FINAL estimate), technicalDocumentId?, commercialDocumentId?, validityDays?, price? (rial before VAT; empty = the estimate price), deliveryWeeks?,
 * paymentTermsText, guaranteesText, deviationsText, vatPercent?, advancePercent?, retentionPercent?, latePenaltyPercentPerWeek?, penaltyCapPercent?, clientReviewDays?, customerReference, rowVersion }.
 * Returns SlProposalDto. Who: writers (SALES).
 */
export const saveSlProposal = (dto: any) => post(sl + "proposals", dto);
/**
 * SlProposalActionDto { id, action, note?, rowVersion }. action:
 *  approve (draft only; SALES or CEO, someone who did not write it; the CEO alone when the price is below cost or above the threshold or no threshold is set; a below-cost approval needs a note),
 *  submit (approved -> sent to the customer; writers), revise (a new draft revision of an approved / submitted proposal; writers), cancel (note = reason, required; writers).
 * Returns SlProposalDto (for revise: the NEW revision). Who: proposalApprovers for approve, writers for the rest.
 */
export const proposalAction = (dto: { id: number; action: "approve" | "submit" | "revise" | "cancel"; note?: string | null; rowVersion?: string | null }) =>
  post(sl + "proposals/action", { id: dto.id, action: dto.action, note: dto.note || null, rowVersion: dto.rowVersion || null });
/**
 * SlProposalResultDto { id, result, reasonId?, competitor?, competitorPrice?, lessonsLearned?, rowVersion }. result: "won" or "lost"; only for a submitted proposal; lost needs reasonId (a Lost reason code),
 * the reason of a win is a Won reason code. Won makes the contract (terms, customer link, equipment rows, a draft contract review and project initiation) and fills contractId. Returns SlProposalDto.
 * Who: SalesRoles.proposalApprovers (SALES, CEO); won also needs contractMakers (SALES, CEO, the same two roles).
 */
export const slProposalResult = (dto: { id: number; result: "won" | "lost"; reasonId?: number | null; competitor?: string | null; competitorPrice?: number | null; lessonsLearned?: string | null; rowVersion?: string | null }) =>
  post(sl + "proposals/result", { id: dto.id, result: dto.result, reasonId: dto.reasonId ?? null, competitor: dto.competitor || null, competitorPrice: dto.competitorPrice ?? null, lessonsLearned: dto.lessonsLearned || null, rowVersion: dto.rowVersion || null });
/**
 * params: SlWinLossQuery { From, To } (ISO dates; NO paging). Answers one SlWinLossDto { byProduct, byCustomer, byReason: [{ key, name, won, lost, hitRatePercent, wonValue, lostValue }], won, lost, hitRatePercent }.
 * Who: commercialReaders.
 */
export const getSlWinLoss = (params?: { From?: string | null; To?: string | null }) => get(sl + "statistics/win-loss", params);

// ---------------------------------------------------------------- price lists
/**
 * params: SlPriceListQuery { Status 1 draft / 2 approved / 3 retired } + paging.
 * SlPriceListDto { id, number, title, validFrom(Text), validTo(Text), status, note, approvedByUserId, approvedAt(Text), lines: [{ id, itemId, itemCode, itemName, unitPrice, minQuantity }], touchedByMe (the current user created or edited it: the backend refuses their approval, so the screen hides the button), rowVersion }. Who: readers.
 */
export const getSlPriceLists = (params?: SalesPageParams) => get(sl + "price-lists", params);
/** One SlPriceListDto with lines. Who: readers. */
export const getSlPriceList = (id: number) => get(sl + "price-lists/" + id);
/** SlSavePriceListDto { id?, title, validFrom? (today), validTo?, note, lines: [{ itemId, unitPrice (rial), minQuantity }], rowVersion }. Draft only. Returns SlPriceListDto. Who: writers (SALES). */
export const saveSlPriceList = (dto: any) => post(sl + "price-lists", dto);
/** SlPriceListActionDto { id, action, rowVersion }. action: approve (someone who did not edit it), retire. Returns SlPriceListDto. Who: SalesRoles.priceListApprovers (SALES, CEO, FINANCEMANAGER). */
export const priceListAction = (dto: { id: number; action: "approve" | "retire"; rowVersion?: string | null }) =>
  post(sl + "price-lists/action", { id: dto.id, action: dto.action, rowVersion: dto.rowVersion || null });
/**
 * SlPriceSuggestDto { itemIds[], markupPercent? } (cost-plus; no markup is assumed). Answers a PLAIN ARRAY of SlPriceSuggestionDto { itemId, itemCode, itemName, unitCost (null = unknown), costSource (0..4),
 * suggestedPrice, note }. Who: commercialReaders.
 */
export const suggestSlPrices = (dto: { itemIds: number[]; markupPercent?: number | null }) => post(sl + "price-lists/suggest", dto);

// ---------------------------------------------------------------- quotations (light, spare parts and services)
/**
 * params: SlQuotationQuery { Status 1..6, CustomerId } + paging. SlQuotationDto { id, number, quotationDate(Text), customerId, customerName, contactId, validUntil(Text), status, priceListId, note, netAmount (rial),
 * salesOrderId, lines: SlDocLineDto[], rowVersion }. SlDocLineDto { id, rowNumber, itemId, itemCode, itemName, unit, description, quantity, unitPrice, discountPercent, amount, reservedQuantity,
 * deliveredQuantity, invoicedQuantity }. Who: readers.
 */
export const getSlQuotations = (params?: SalesPageParams) => get(sl + "quotations", params);
/** One SlQuotationDto with lines. Who: readers. */
export const getSlQuotation = (id: number) => get(sl + "quotations/" + id);
/**
 * SlSaveQuotationDto { id?, quotationDate?, customerId, contactId?, validUntil?, priceListId?, note, lines: [{ itemId, description, quantity, unitPrice? (empty = the approved price list), discountPercent }], rowVersion }.
 * Returns SlQuotationDto. Who: writers (SALES).
 */
export const saveSlQuotation = (dto: any) => post(sl + "quotations", dto);
/**
 * SlQuotationActionDto { id, action, rowVersion }. action: send, accept, reject, cancel, and expire (see mismatch 3 in SALES_SERVICE_FOUNDATION.md: the service accepts "expire" for a sent quotation past its validity,
 * the DTO comment omits it). Returns SlQuotationDto. Who: writers (SALES).
 */
export const quotationAction = (dto: { id: number; action: "send" | "accept" | "reject" | "expire" | "cancel"; rowVersion?: string | null }) =>
  post(sl + "quotations/action", { id: dto.id, action: dto.action, rowVersion: dto.rowVersion || null });
/** SlQuotationToOrderDto { quotationId, customerOrderNumber?, requestedDeliveryDate? }: turns an accepted quotation into a sales order. Returns SlOrderDto. Who: writers (SALES). */
export const slQuotationToOrder = (dto: { quotationId: number; customerOrderNumber?: string | null; requestedDeliveryDate?: string | null }) => post(sl + "quotations/to-order", dto);

// ---------------------------------------------------------------- sales orders
/**
 * params: SlOrderQuery { Status 1..7, CustomerId, CreditPending } + paging.
 * SlOrderDto { id, number, orderDate(Text), customerId, customerName, contactId, quotationId, customerOrderNumber, requestedDeliveryDate(Text), status, statusText, netAmount, creditApprovalRequired, creditReason,
 * creditExcess, creditApprovedByUserId, creditApprovedAt(Text), creditApprovedNetAmount, creditApprovalNote, confirmedAt(Text), cancelReason, lines: SlDocLineDto[],
 * deliveries: [{ id, documentId, documentNumber, invoiceId, invoiceNumber, draftNote, reversed }], touchedByMe (the current user created or edited it: the backend refuses their credit approval, so the screen hides the button), rowVersion }. Who: readers.
 */
export const getSlOrders = (params?: SalesPageParams) => get(sl + "orders", params);
/** One SlOrderDto with lines and deliveries. Who: readers. */
export const getSlOrder = (id: number) => get(sl + "orders/" + id);
/** SlSaveOrderDto { id?, orderDate?, customerId, contactId?, customerOrderNumber, requestedDeliveryDate?, lines: [{ itemId, description, quantity, unitPrice?, discountPercent }], rowVersion }. Draft only. Returns SlOrderDto. Who: writers (SALES). */
export const saveSlOrder = (dto: any) => post(sl + "orders", dto);
/**
 * SlOrderActionDto { id, action, reason?, rowVersion }. action: confirm (reserves stock), cancel (reason required, draft or confirmed, no live delivery), close (delivered / invoiced, or partly delivered with a reason).
 * IMPORTANT: a confirm that takes the customer over the credit limit answers success=false WITH the reason, but the server has already marked the order creditApprovalRequired and sent the
 * finance manager a gate: show the message and reload the order. Returns SlOrderDto. Who: writers (SALES).
 */
export const slOrderAction = (dto: { id: number; action: "confirm" | "cancel" | "close"; reason?: string | null; rowVersion?: string | null }) =>
  post(sl + "orders/action", { id: dto.id, action: dto.action, reason: dto.reason || null, rowVersion: dto.rowVersion || null });
/** SlCreditApproveDto { id, note?, rowVersion }: approve an order that exceeds the credit limit. Returns SlOrderDto. Who: SalesRoles.creditApprovers (FINANCEMANAGER). */
export const creditApproveSlOrder = (dto: { id: number; note?: string | null; rowVersion?: string | null }) =>
  post(sl + "orders/credit-approve", { id: dto.id, note: dto.note || null, rowVersion: dto.rowVersion || null });
/** SlDraftInvoiceDto { orderId }: drafts the sales invoice of the delivered, not yet invoiced quantities (finance confirms it). Returns SlOrderDto. Who: writers (SALES). */
export const draftSlInvoice = (dto: { orderId: number }) => post(sl + "orders/draft-invoice", dto);

// ---------------------------------------------------------------- dashboard
/**
 * params: AsOf (ISO date, optional). SlDashboardDto { asOf, asOfText, openInquiries, pipelineValue, pipelineByStage: [{ name, count, value }], pipelineByProduct: [{ name, count, value }], won, lost,
 * hitRatePercent (null until a result exists), proposalsDueThisWeek: [{ inquiryId, inquiryNumber, title, customerName, dueDate, dueDateText, status }], proposalsDueThisWeekCount,
 * quotationsExpiringCount, quotationsExpiring: [{ quotationId, number, customerName, validUntil, validUntilText, netAmount }], bidDecisionsWaiting, proposalsWaitingApproval, ordersWaitingCredit, openOrders }.
 * Who: readers.
 */
export const getSlDashboard = (asOf?: string | null) => get(sl + "dashboard", { AsOf: asOf });
