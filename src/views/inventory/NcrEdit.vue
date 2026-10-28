<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import AiNotesBox from "@/components/ai/AiNotesBox.vue";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import PrintForm from "@/components/print/PrintForm.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import ReinspectionPicker from "@/components/quality/ReinspectionPicker.vue";
import {
  approveNcr,
  closeNcr,
  executeNcr,
  getContracts,
  getDocuments,
  getLots,
  getNcr,
  getProviders,
  getSupplierReturnReport,
  getWarehouses,
  getWorkOrders,
  rowsOf,
  saveNcr,
} from "@/api/InventoryAPIs";
import { getEquipmentList } from "@/api/QualityAPIs";
import { qualityFormCodes, reinspectionKinds, rootCauseMethods, type ReinspectionValue } from "@/composables/quality";
import {
  fail,
  jalali,
  ncrCauses,
  ncrDispositions,
  ncrSeverities,
  ncrSources,
  ncrStatuses,
  num,
  ok,
  rial,
  today,
  warehouseKinds,
} from "@/composables/inventory";
import { hasRole, InventoryRoles, QualityRoles } from "@/composables/roles";

// Non-conformance report (FO-QC-013, procedure PR-QC-002): quality creates it, quality / engineering / manufacturing sign,
// engineering issues the concession, the warehouse executes the decision, quality closes it after the effectiveness check.
const route = useRoute();
const router = useRouter();
const ncr = ref<any>(null);
const item = ref<any>(null);
const lots = ref<any[]>([]);
const providers = ref<any[]>([]);
const warehouses = ref<any[]>([]);
const workOrders = ref<any[]>([]);
const contracts = ref<any[]>([]);
const busy = ref(false);
const targetWarehouseId = ref<number | null>(null);
const supplierReturn = ref<any>(null);
const equipment = ref<any[]>([]);
const printMode = ref<"ncr" | "return">("ncr");
// dialogs: cancel (a reason), execute (the target warehouse, or the proof of a decision carried out in the workshop) and close (the effectiveness check, the re-inspection)
const showCancel = ref(false);
const exec = ref<any>(null);
const closing = ref<any>(null);
const evidenceDocuments = ref<any[]>([]);
// the form as it was loaded: a signature or a close is made on the SAVED report, never on edits that are still on the screen
const snapshot = ref("");
// R1q D3: whoever saved, executed or created the report in this page is its maker; the API gives him neither a signature nor the close (it answers with its own message)
const touchedByMe = ref(false);
// one person gives one of the three signatures (the concession permit is separate)
const signedByMe = ref(false);

const isNew = computed(() => !ncr.value?.id);
const open = computed(() => ncr.value && ncr.value.status === 0);
const editableFollowUp = computed(() => ncr.value && ncr.value.status < 3);
const needsConcession = computed(() => ncr.value && [1, 3].includes(ncr.value.disposition));
const dirty = computed(() => !isNew.value && !!ncr.value && snapshot.value !== "" && JSON.stringify(ncr.value) !== snapshot.value);
const needsReinspection = computed(() => !!ncr.value && [2, 3].includes(ncr.value.disposition));
const reinspectionLinked = computed(() => !!ncr.value?.reinspectionRecordId);
// the stock document of the decision (R1q D4): scrap goes to a scrap warehouse; rework and repair go to a quarantine / QC-hold warehouse other than the one the goods are in, where
// an inspection waits for them; a concession goes to a usable warehouse (never quarantine, scrap or contractor)
const targetOptions = computed(() => {
  const disposition = ncr.value?.disposition;
  if (disposition === 4) return warehouses.value.filter((w) => w.kind === 6);
  if (disposition === 2 || disposition === 3) return warehouses.value.filter((w) => w.kind === 5 && w.id !== ncr.value?.warehouseId);
  return warehouses.value.filter((w) => ![5, 6, 7].includes(w.kind));
});
// who saves the report (POST quality/ncr: PMO and QC), who signs which signature, who closes or cancels (QC and QA), who executes
const canEdit = hasRole(InventoryRoles.qc);
const canSignQuality = hasRole(InventoryRoles.qualitySigners);
const canSignEngineering = hasRole(InventoryRoles.engineeringSigners);
const canSignManufacturing = hasRole(InventoryRoles.manufacturingSigners);
const canClose = hasRole(InventoryRoles.qcOrQa);
// the QA manager cancels any open report; a QC user only a report he did not raise or edit (the API repeats the check)
const canCancel = computed(() => canClose && (hasRole(QualityRoles.qaManager) || !touchedByMe.value));
const equipmentLabel = (id: number | null) => {
  const e = equipment.value.find((x) => x.id === id);
  return e ? `${e.code} ${e.name}` : "-";
};
const canExecute = hasRole(InventoryRoles.executeNcr);
const canCapa = hasRole(QualityRoles.capaStaff);
const unsavedMessage =
  "تغییرات ذخیره‌نشده دارید. امضا و بستن روی نسخه ذخیره‌شده گزارش ثبت می‌شود؛ ابتدا ذخیره کنید (کسی که گزارش را ذخیره یا ویرایش می‌کند خودش آن را امضا یا بسته نمی‌کند) یا صفحه را دوباره باز کنید.";
const documentLabel = (d: any) => `${d.number} — ${jalali(d.documentDate)}${d.warehouseTitle ? " — " + d.warehouseTitle : ""}${d.providerName ? " — " + d.providerName : ""}`;

function blank() {
  return {
    id: null,
    status: 0,
    reportDate: today(),
    source: 2,
    severity: 0,
    itemId: null,
    lotId: null,
    quantity: 1,
    serialText: "",
    warehouseId: null,
    providerId: null,
    workOrderId: null,
    contractId: null,
    ownedByContractor: false,
    requirement: "",
    description: "",
    causeCategory: 0,
    rootCause: "",
    disposition: 0,
    dispositionNote: "",
    committeeMinutesNumber: "",
    committeeMembers: "",
    concessionNumber: "",
    customerApprovalRequired: false,
    customerApprovalReference: "",
    targetWarehouseId: null,
    correctiveAction: "",
    correctiveActionOwner: "",
    correctiveActionDueDate: null,
    dueDate: null,
    rootCauseMethod: 0,
    costImpact: null,
    timeImpactDays: null,
    customerReturnRef: "",
    affectedEquipmentId: null,
  };
}

function load(dto: any) {
  ncr.value = {
    ...dto,
    reportDate: (dto.reportDate || "").slice(0, 10),
    serialText: (dto.serialNumbers || []).join("\n"),
    correctiveActionDueDate: dto.correctiveActionDueDate ? dto.correctiveActionDueDate.slice(0, 10) : null,
    dueDate: dto.dueDate ? dto.dueDate.slice(0, 10) : null,
  };
  item.value = { id: dto.itemId, code: dto.itemCode, title: dto.itemTitle, unit: dto.unit };
  targetWarehouseId.value = dto.targetWarehouseId;
  snapshot.value = JSON.stringify(ncr.value);
}

async function loadLots() {
  lots.value = [];
  if (!item.value) return;
  const [data] = await getLots({ ItemId: item.value.id, PageSize: 200 });
  lots.value = rowsOf(data);
}

async function save(silent = false): Promise<boolean> {
  const n = ncr.value;
  if (!item.value) {
    fail("کالا را انتخاب کنید.");
    return false;
  }
  busy.value = true;
  const [data, error] = await saveNcr({
    ...n,
    itemId: item.value.id,
    quantity: Number(n.quantity) || 0,
    serialNumbers: (n.serialText || "")
      .split(/[\n,،]+/)
      .map((s: string) => s.trim())
      .filter(Boolean),
    correctiveActionDueDate: n.correctiveActionDueDate || null,
    // quality module additions (spec 5.5); an empty due date lets the API apply its default (minor 7 days, major 14 days)
    dueDate: n.dueDate || null,
    rootCauseMethod: Number(n.rootCauseMethod) || 0,
    costImpact: n.costImpact === "" || n.costImpact === null ? null : Number(n.costImpact),
    timeImpactDays: n.timeImpactDays === "" || n.timeImpactDays === null ? null : Number(n.timeImpactDays),
    customerReturnRef: n.customerReturnRef || null,
    affectedEquipmentId: n.affectedEquipmentId || null,
  });
  busy.value = false;
  if (error) {
    fail(error);
    return false;
  }
  touchedByMe.value = true;
  load(data);
  if (!silent) ok("ذخیره شد.");
  if (route.params.id === "new") router.replace({ name: "InventoryNcrEdit", params: { id: data.id } });
  return true;
}

/**
 * kind: 4 quality (QC or the QA manager), 5 engineering, 3 manufacturing; the concession permit is signed by engineering after the QC signature.
 * The signature is made on the saved report: the report is NOT saved here, because saving would make the signer an editor and the API refuses a signature from its maker.
 */
async function sign(kind: number, concession = false) {
  if (dirty.value) return fail(unsavedMessage);
  busy.value = true;
  const [data, error] = await approveNcr({ id: ncr.value.id, kind, concession });
  busy.value = false;
  if (error) return fail(error);
  load(data);
  if (!concession) signedByMe.value = true;
  ok(data.status === 1 ? "تصمیم تایید نهایی شد." : "امضا ثبت شد.");
}

/** The execute dialog: the target warehouse of a stock decision, or the proof that a decision about a product found in the workshop was carried out (R1q D5). */
async function openExecute() {
  const d = ncr.value;
  exec.value = {
    targetWarehouseId: targetOptions.value.some((w) => w.id === targetWarehouseId.value) ? targetWarehouseId.value : null,
    reworkOperationId: null,
    reinspection: { kind: "QcRecord", recordId: null } as ReinspectionValue,
    scrapDocumentId: null,
    scrappedSerialUnitId: null,
    returnDocumentId: null,
  };
  evidenceDocuments.value = [];
  if (!d.warehouseId && (d.disposition === 4 || d.disposition === 5)) {
    // a confirmed scrap receipt (type 13) or a confirmed return to the supplier (type 5)
    const [data] = await getDocuments({ Type: d.disposition === 4 ? 13 : 5, Status: 1, PageSize: 100 });
    evidenceDocuments.value = rowsOf(data);
  }
}

function numberOrNull(value: any): number | null {
  const n = Number(value);
  return value !== null && value !== "" && Number.isFinite(n) && n > 0 ? Math.trunc(n) : null;
}

/** ExecuteNcrDto: only the fields of the decision at hand are sent. */
function executeBody() {
  const d = ncr.value;
  const e = exec.value;
  const body: any = { id: d.id };
  if (d.warehouseId) {
    if (d.disposition !== 5) body.targetWarehouseId = e.targetWarehouseId;
    return body;
  }
  if (d.disposition === 2 || d.disposition === 3) {
    const operationId = numberOrNull(e.reworkOperationId);
    if (operationId) body.reworkOperationId = operationId;
    if (e.reinspection.recordId) {
      body.reinspectionKind = e.reinspection.kind;
      body.reinspectionRecordId = e.reinspection.recordId;
    }
  } else if (d.disposition === 4) {
    body.scrapDocumentId = numberOrNull(e.scrapDocumentId);
    body.scrappedSerialUnitId = numberOrNull(e.scrappedSerialUnitId);
  } else if (d.disposition === 5) {
    body.returnDocumentId = numberOrNull(e.returnDocumentId);
  }
  return body;
}

async function execute() {
  busy.value = true;
  const [data, error] = await executeNcr(executeBody());
  busy.value = false;
  if (error) return fail(error);
  exec.value = null;
  // review H7: the person who carried the decision out is a maker of the outcome; he cannot close the report
  touchedByMe.value = true;
  load(data);
  ok(
    !ncr.value.warehouseId
      ? "اجرای تصمیم با مدرک کارگاه ثبت شد."
      : ncr.value.disposition === 5
      ? "سند برگشت به تامین‌کننده ساخته شد؛ انبار اطلاعات خروج را کامل و تایید کند."
      : [2, 3].includes(ncr.value.disposition)
      ? "کالا به انبار قرنطینه منتقل شد؛ پس از بازرسی مجدد قبول‌شده گزارش بسته می‌شود."
      : "تصمیم با سند انبار اجرا شد."
  );
}

/** Close dialog: the effectiveness check and, for rework and repair, the accepted re-inspection (when the system or the execute step has not linked it yet). */
function openClose() {
  if (dirty.value) return fail(unsavedMessage);
  closing.value = { effectiveness: "", reinspection: { kind: "QcInspection", recordId: null } as ReinspectionValue };
}

async function confirmClose() {
  const c = closing.value;
  const body: any = { id: ncr.value.id, effectivenessCheck: c.effectiveness, cancel: false };
  if (needsReinspection.value && !reinspectionLinked.value && c.reinspection.recordId) {
    body.reinspectionKind = c.reinspection.kind;
    body.reinspectionRecordId = c.reinspection.recordId;
  }
  busy.value = true;
  const [data, error] = await closeNcr(body);
  busy.value = false;
  if (error) return fail(error);
  closing.value = null;
  load(data);
  ok("گزارش بسته شد.");
}

/** Cancels an open report with a reason that stays on the record (QA, or QC for a report he did not raise or edit). */
async function cancelNcr(reason: string) {
  if (dirty.value) {
    showCancel.value = false;
    return fail(unsavedMessage);
  }
  busy.value = true;
  const [data, error] = await closeNcr({ id: ncr.value.id, cancel: true, reason });
  busy.value = false;
  if (error) return fail(error);
  showCancel.value = false;
  load(data);
  ok("گزارش ابطال شد.");
}

async function printNcr() {
  printMode.value = "ncr";
  await nextTick();
  window.print();
}

async function printReturn() {
  const [data, error] = await getSupplierReturnReport(ncr.value.id);
  if (error) return fail(error);
  supplierReturn.value = data;
  printMode.value = "return";
  await nextTick();
  window.print();
}

onMounted(async () => {
  const [w] = await getWarehouses();
  warehouses.value = (w || []).filter((x: any) => x.isActive);
  getProviders().then(([list]) => (providers.value = rowsOf(list)));
  getWorkOrders().then(([list]) => (workOrders.value = rowsOf(list)));
  getContracts().then(([list]) => (contracts.value = rowsOf(list)));
  getEquipmentList({ PageSize: 300 }).then(([list]) => (equipment.value = rowsOf(list)));
  if (route.params.id === "new") {
    ncr.value = blank();
  } else {
    const [data, error] = await getNcr(Number(route.params.id));
    if (error) return fail(error);
    load(data);
    loadLots();
  }
});
</script>

<template>
  <div v-if="ncr" class="inv-page">
    <!-- FO-QC-013 -->
    <PrintForm
      v-if="printMode === 'ncr' && !isNew"
      title="گزارش عدم انطباق (NCR)"
      :form-code="qualityFormCodes.ncr"
      :fields="[
        { label: 'شماره', value: ncr.number, ltr: true },
        { label: 'تاریخ', value: jalali(ncr.reportDate) },
        { label: 'منبع', value: ncrSources[ncr.source] },
        { label: 'نوع', value: ncrSeverities[ncr.severity] },
        { label: 'وضعیت', value: ncrStatuses[ncr.status] },
        { label: 'محل نگهداری', value: ncr.warehouseTitle || 'کارگاه' },
        { label: 'تامین‌کننده / پیمانکار', value: ncr.providerName || '-' },
        { label: 'رسید', value: ncr.receiptNumber || '-', ltr: true },
        { label: 'دستور ساخت', value: ncr.workOrderCode || '-' },
        { label: 'قرارداد', value: ncr.contractCode || '-' },
        { label: 'مهلت تصمیم', value: jalali(ncr.dueDate) || '-' },
        { label: 'روش تحلیل علت', value: rootCauseMethods[ncr.rootCauseMethod] },
        { label: 'اثر هزینه (ریال)', value: ncr.costImpact === null || ncr.costImpact === undefined ? '-' : rial(ncr.costImpact) },
        { label: 'اثر زمان (روز)', value: ncr.timeImpactDays ?? '-' },
        { label: 'مرجع برگشت مشتری', value: ncr.customerReturnRef || '-', ltr: true },
        { label: 'CAPA مرتبط', value: ncr.capaNumber || '-', ltr: true },
        { label: 'تجهیز اندازه‌گیری مرتبط', value: equipmentLabel(ncr.affectedEquipmentId), ltr: true },
      ]"
      :signatures="[
        { title: 'کنترل کیفیت', name: ncr.qualityApprovedByName, date: jalali(ncr.qualityApprovedAt) },
        { title: 'مهندسی', name: ncr.engineeringApprovedByName, date: jalali(ncr.engineeringApprovedAt) },
        { title: 'تولید', name: ncr.manufacturingApprovedByName, date: jalali(ncr.manufacturingApprovedAt) },
        { title: 'معاونت طراحی و ساخت (مجوز ارفاقی)', name: ncr.concessionApprovedByName, date: jalali(ncr.concessionApprovedAt) },
      ]"
      distribution="توزیع نسخ: ۱- کنترل کیفیت ۲- مهندسی ۳- تولید ۴- انبار ۵- برنامه‌ریزی و کنترل پروژه ۶- تضمین کیفیت">
      <table>
        <thead>
          <tr>
            <th>کد کالا</th>
            <th>نام کالا</th>
            <th>مقدار</th>
            <th>بچ / ذوب</th>
            <th>سریال‌ها</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td class="inv-ltr">{{ ncr.itemCode }}</td>
            <td>{{ ncr.itemTitle }}</td>
            <td class="num">{{ num(ncr.quantity) }} {{ ncr.unit }}</td>
            <td class="inv-ltr">{{ ncr.lotNumber }} {{ ncr.heatNumber }}</td>
            <td class="inv-ltr">{{ (ncr.serialNumbers || []).join("، ") || "-" }}</td>
          </tr>
        </tbody>
      </table>
      <table class="mt-1">
        <tbody>
          <tr>
            <td style="width: 22%"><b>الزام (نقشه / استاندارد)</b></td>
            <td>{{ ncr.requirement || "-" }}</td>
          </tr>
          <tr>
            <td><b>شرح عدم انطباق</b></td>
            <td>{{ ncr.description || "-" }}</td>
          </tr>
          <tr>
            <td><b>علت ریشه‌ای</b></td>
            <td>{{ ncrCauses[ncr.causeCategory] }} — {{ ncr.rootCause || "-" }}</td>
          </tr>
          <tr>
            <td><b>تصمیم</b></td>
            <td>{{ ncrDispositions[ncr.disposition] }} {{ ncr.dispositionNote ? "— " + ncr.dispositionNote : "" }}</td>
          </tr>
          <tr v-if="ncr.severity === 2">
            <td><b>کمیته فنی</b></td>
            <td>صورت‌جلسه {{ ncr.committeeMinutesNumber }} — {{ ncr.committeeMembers }}</td>
          </tr>
          <tr v-if="needsConcession">
            <td><b>مجوز ارفاقی</b></td>
            <td>
              {{ ncr.concessionNumber || "-" }}
              <span v-if="ncr.customerApprovalRequired"> · تایید مشتری: {{ ncr.customerApprovalReference || "ندارد" }}</span>
            </td>
          </tr>
          <tr>
            <td><b>سند اجرای تصمیم</b></td>
            <td class="inv-ltr">{{ ncr.resultDocumentNumber || "-" }}</td>
          </tr>
          <tr>
            <td><b>اقدام اصلاحی</b></td>
            <td>
              {{ ncr.correctiveAction || "-" }}
              <span v-if="ncr.correctiveActionOwner">— مسئول: {{ ncr.correctiveActionOwner }}</span>
              <span v-if="ncr.correctiveActionDueDate">— مهلت: {{ jalali(ncr.correctiveActionDueDate) }}</span>
            </td>
          </tr>
          <tr>
            <td><b>بررسی اثربخشی</b></td>
            <td>{{ ncr.effectivenessCheck || "-" }}</td>
          </tr>
        </tbody>
      </table>
    </PrintForm>

    <!-- supplier return report for the commercial unit (no company form code yet) -->
    <PrintForm
      v-if="printMode === 'return' && supplierReturn"
      title="گزارش کالای مرجوعی به تامین‌کننده"
      :fields="[
        { label: 'شماره NCR', value: supplierReturn.ncrNumber, ltr: true },
        { label: 'تامین‌کننده', value: supplierReturn.providerName || '-' },
        { label: 'رسید', value: (supplierReturn.receiptNumber || '-') + ' ' + jalali(supplierReturn.receiptDate), ltr: true },
        { label: 'شماره فاکتور / مرجع رسید', value: supplierReturn.invoiceNumber || '-' },
        { label: 'پروژه / دستور ساخت', value: (supplierReturn.contractCode || '-') + ' ' + (supplierReturn.workOrderCode || '') },
        { label: 'وضعیت پرداخت', value: supplierReturn.paymentState || '-' },
        { label: 'درخواست خرید', value: (supplierReturn.buyOrderTitle || '-') + (supplierReturn.financeNumber ? ' — سند مالی ' + supplierReturn.financeNumber : '') },
        { label: 'سند برگشت انبار', value: supplierReturn.returnDocumentNumber || 'هنوز صادر نشده', ltr: true },
      ]"
      :notes="[{ label: 'علت مرجوعی', value: supplierReturn.description || '-' }]"
      :signatures="[{ title: 'کنترل کیفیت' }, { title: 'انباردار' }, { title: 'بازرگانی' }]">
      <table>
        <thead>
          <tr>
            <th>کد کالا</th>
            <th>نام کالا</th>
            <th>مقدار مرجوعی</th>
            <th>فی (ریال)</th>
            <th>مبلغ (ریال)</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td class="inv-ltr">{{ supplierReturn.itemCode }}</td>
            <td>{{ supplierReturn.itemTitle }}</td>
            <td class="num">{{ num(supplierReturn.quantity) }} {{ supplierReturn.unit }}</td>
            <td class="num">{{ rial(supplierReturn.unitPrice) }}</td>
            <td class="num">{{ rial(supplierReturn.amount) }}</td>
          </tr>
        </tbody>
      </table>
    </PrintForm>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">
            گزارش عدم انطباق
            <span v-if="ncr.number" class="inv-ltr ms-2">{{ ncr.number }}</span>
          </h1>
          <span class="inv-badge" :class="{ bad: ncr.status === 4, ok: ncr.status === 3 }">{{ ncrStatuses[ncr.status] }}</span>
          <span v-if="ncr.creatorName" class="inv-muted ms-2">ثبت‌کننده: {{ ncr.creatorName }}</span>
        </div>
        <div class="d-flex flex-wrap gap-2">
          <router-link :to="{ name: 'InventoryNcrs' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button v-if="!isNew" class="btn btn-sm btn-outline-secondary" @click="printNcr">چاپ FO-QC-013</button>
          <button v-if="!isNew && ncr.disposition === 5" class="btn btn-sm btn-outline-secondary" @click="printReturn">گزارش مرجوعی (بازرگانی)</button>
          <button v-if="editableFollowUp && canEdit" :disabled="busy" class="btn btn-sm btn-primary" @click="save()">ذخیره</button>
        </div>
      </div>

      <div v-if="ncr.status === 4" class="alert alert-danger py-2 small">
        این گزارش ابطال شد<span v-if="ncr.cancelledByName"> توسط {{ ncr.cancelledByName }}</span><span v-if="ncr.cancelledAt"> در {{ jalali(ncr.cancelledAt, true) }}</span>.
        <b>دلیل ابطال:</b> {{ ncr.cancelReason || "-" }}
      </div>
      <div v-if="dirty" class="alert alert-warning py-2 small">تغییرات ذخیره‌نشده دارید؛ امضا، اجرا، بستن و ابطال روی نسخه ذخیره‌شده انجام می‌شود.</div>

      <div class="row g-2">
        <div class="col-md-2">
          <label class="form-label small mb-0">تاریخ</label>
          <input v-model="ncr.reportDate" :disabled="!open" class="form-control form-control-sm" type="date" />
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">منبع</label>
          <select v-model="ncr.source" :disabled="!open" class="form-select form-select-sm">
            <option v-for="(t, k) in ncrSources" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">کالا *</label>
          <ItemPicker v-if="isNew" v-model="item" @select="loadLots" />
          <div v-else class="small pt-1">
            <span class="inv-ltr">{{ ncr.itemCode }}</span> — {{ ncr.itemTitle }}
          </div>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">مقدار *</label>
          <input v-model.number="ncr.quantity" :disabled="!isNew" class="form-control form-control-sm" type="number" />
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">بچ</label>
          <select v-model="ncr.lotId" :disabled="!isNew" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="l in lots" :key="l.id" :value="l.id">{{ l.lotNumber }}</option>
          </select>
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">سریال‌ها (هر کدام در یک خط)</label>
          <textarea v-model="ncr.serialText" :disabled="!isNew" class="form-control form-control-sm inv-ltr" rows="2"></textarea>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">محل نگهداری کالای نامنطبق</label>
          <select v-model="ncr.warehouseId" :disabled="!isNew" class="form-select form-select-sm">
            <option :value="null">کارگاه (خارج از انبار)</option>
            <option v-for="w in warehouses" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option>
          </select>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">تامین‌کننده / پیمانکار</label>
          <select v-model="ncr.providerId" :disabled="!open" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="p in providers" :key="p.id" :value="p.id">{{ p.name }}</option>
          </select>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">دستور ساخت</label>
          <select v-model="ncr.workOrderId" :disabled="!open" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="w in workOrders" :key="w.id" :value="w.id">{{ w.code }}</option>
          </select>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">قرارداد</label>
          <select v-model="ncr.contractId" :disabled="!open" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="c in contracts" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option>
          </select>
        </div>
        <div class="col-md-3 d-flex align-items-end">
          <label class="form-check small">
            <input v-model="ncr.ownedByContractor" :disabled="!open" class="form-check-input" type="checkbox" />
            متعلق به پیمانکار / مشتری
          </label>
        </div>
        <div class="col-md-6">
          <label class="form-label small mb-0">الزام (نقشه، استاندارد، بند قرارداد)</label>
          <input v-model="ncr.requirement" :disabled="!open" class="form-control form-control-sm" />
        </div>
        <div class="col-md-6">
          <label class="form-label small mb-0">شرح عدم انطباق</label>
          <textarea v-model="ncr.description" :disabled="!open" class="form-control form-control-sm" rows="2"></textarea>
        </div>
      </div>
    </div>

    <div v-if="!isNew" class="inv-card screen-only">
      <h2 class="inv-title mb-2">تصمیم (کمیته / مدیران)</h2>
      <div class="row g-2">
        <div class="col-md-3">
          <label class="form-label small mb-0">نوع عدم انطباق</label>
          <select v-model="ncr.severity" :disabled="!open" class="form-select form-select-sm">
            <option v-for="(t, k) in ncrSeverities" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">تصمیم</label>
          <select v-model="ncr.disposition" :disabled="!open" class="form-select form-select-sm">
            <option v-for="(t, k) in ncrDispositions" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
        </div>
        <div class="col-md-5">
          <label class="form-label small mb-0">توضیح تصمیم</label>
          <input v-model="ncr.dispositionNote" :disabled="!open" class="form-control form-control-sm" />
        </div>
        <template v-if="ncr.severity === 2">
          <div class="col-md-3">
            <label class="form-label small mb-0">شماره صورت‌جلسه کمیته فنی *</label>
            <input v-model="ncr.committeeMinutesNumber" :disabled="!open" class="form-control form-control-sm" />
          </div>
          <div class="col-md-9">
            <label class="form-label small mb-0">اعضای کمیته *</label>
            <input v-model="ncr.committeeMembers" :disabled="!open" class="form-control form-control-sm" />
          </div>
        </template>
        <template v-if="needsConcession">
          <div class="col-md-3">
            <label class="form-label small mb-0">شماره مجوز ارفاقی *</label>
            <input v-model="ncr.concessionNumber" :disabled="!open" class="form-control form-control-sm" />
          </div>
          <div class="col-md-4 d-flex align-items-end">
            <label class="form-check small">
              <input v-model="ncr.customerApprovalRequired" :disabled="!open" class="form-check-input" type="checkbox" />
              قرارداد تایید انحراف توسط مشتری را لازم دارد (مثل API)
            </label>
          </div>
          <div v-if="ncr.customerApprovalRequired" class="col-md-5">
            <label class="form-label small mb-0">شماره نامه / تایید مشتری *</label>
            <input v-model="ncr.customerApprovalReference" :disabled="!open" class="form-control form-control-sm" />
          </div>
        </template>
      </div>

      <div class="d-flex flex-wrap gap-2 mt-3 align-items-center">
        <span class="inv-badge" :class="{ ok: ncr.qualityApprovedAt }">کنترل کیفیت: {{ ncr.qualityApprovedByName || "—" }}</span>
        <span class="inv-badge" :class="{ ok: ncr.engineeringApprovedAt }">مهندسی: {{ ncr.engineeringApprovedByName || "—" }}</span>
        <span class="inv-badge" :class="{ ok: ncr.manufacturingApprovedAt }">تولید: {{ ncr.manufacturingApprovedByName || "—" }}</span>
        <span v-if="needsConcession" class="inv-badge" :class="{ ok: ncr.concessionApprovedAt }">مجوز ارفاقی: {{ ncr.concessionApprovedByName || "—" }}</span>
        <template v-if="open && !touchedByMe">
          <button v-if="!ncr.qualityApprovedAt && canSignQuality && !signedByMe" :disabled="busy" class="btn btn-sm btn-outline-success" @click="sign(4)">امضای کنترل کیفیت</button>
          <button v-if="!ncr.engineeringApprovedAt && canSignEngineering && !signedByMe" :disabled="busy" class="btn btn-sm btn-outline-success" @click="sign(5)">امضای مهندسی</button>
          <button v-if="!ncr.manufacturingApprovedAt && canSignManufacturing && !signedByMe" :disabled="busy" class="btn btn-sm btn-outline-success" @click="sign(3)">امضای تولید</button>
          <button
            v-if="needsConcession && !ncr.concessionApprovedAt && ncr.qualityApprovedAt && canSignEngineering"
            :disabled="busy"
            class="btn btn-sm btn-outline-success"
            @click="sign(5, true)">
            صدور مجوز ارفاقی
          </button>
        </template>
      </div>
      <p class="inv-muted mt-2 mb-0">
        امضای کنترل کیفیت را QC یا مدیر QA می‌دهد. هر امضا باید از شخص دیگری باشد و ثبت‌کننده یا ویرایشگر گزارش هیچ امضایی (و مجوز ارفاقی) نمی‌دهد؛ مجوز ارفاقی پس از امضای کنترل کیفیت صادر می‌شود.
        تغییر نوع یا تصمیم، امضاها را پاک می‌کند. کالای متعلق به پیمانکار ضایعات شرکت نمی‌شود و مرجوع می‌گردد.
      </p>
      <p v-if="open && touchedByMe" class="small text-warning mb-0">شما این گزارش را ذخیره یا ثبت کرده‌اید؛ امضای آن با کاربر دیگری است.</p>

      <div v-if="ncr.status === 1 && canExecute" class="d-flex flex-wrap gap-2 mt-3 align-items-end">
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="openExecute">اجرای تصمیم{{ ncr.warehouseId ? " (سند انبار)" : " (مدرک کارگاه)" }}</button>
        <span class="inv-muted small">کسی که تصمیم را اجرا می‌کند، آن را نمی‌بندد.</span>
      </div>
      <p v-if="ncr.resultDocumentId" class="small mt-2 mb-0">
        سند اجرای تصمیم:
        <router-link :to="{ name: 'InventoryDocumentEdit', params: { id: ncr.resultDocumentId } }" class="inv-ltr">{{ ncr.resultDocumentNumber }}</router-link>
      </p>
      <div v-if="ncr.executedAt || ncr.reinspectionRecordId || ncr.reworkOperationId || ncr.scrapDocumentId || ncr.scrappedSerialUnitId" class="mt-2 small">
        <b>مدرک اجرای تصمیم</b>
        <ul class="mb-0">
          <li v-if="ncr.executedAt">اجرا شد: {{ jalali(ncr.executedAt, true) }}</li>
          <li v-if="ncr.reworkOperationId">
            عملیات دوباره‌کاری پایان‌یافته:
            <router-link :to="{ name: 'MfOperation', params: { id: ncr.reworkOperationId } }" class="inv-ltr">#{{ ncr.reworkOperationId }}</router-link>
          </li>
          <li v-if="ncr.reinspectionRecordId">
            بازرسی مجدد قبول‌شده ({{ reinspectionKinds[ncr.reinspectionKind] || ncr.reinspectionKind }}):
            <router-link v-if="ncr.reinspectionKind === 'QcRecord'" :to="{ name: 'QualityRecordEdit', params: { id: ncr.reinspectionRecordId } }" class="inv-ltr">#{{ ncr.reinspectionRecordId }}</router-link>
            <router-link v-else-if="ncr.reinspectionKind === 'TestReport'" :to="{ name: 'QualityReportEdit', params: { id: ncr.reinspectionRecordId } }" class="inv-ltr">#{{ ncr.reinspectionRecordId }}</router-link>
            <span v-else class="inv-ltr">#{{ ncr.reinspectionRecordId }}</span>
          </li>
          <li v-if="ncr.scrapDocumentId">
            رسید ضایعات:
            <router-link :to="{ name: 'InventoryDocumentEdit', params: { id: ncr.scrapDocumentId } }" class="inv-ltr">#{{ ncr.scrapDocumentId }}</router-link>
          </li>
          <li v-if="ncr.scrappedSerialUnitId">سریال ضایعات‌شده: <span class="inv-ltr">#{{ ncr.scrappedSerialUnitId }}</span></li>
        </ul>
      </div>
    </div>

    <div v-if="!isNew" class="inv-card screen-only">
      <h2 class="inv-title mb-2">علت ریشه‌ای و اقدام اصلاحی</h2>
      <div class="row g-2">
        <div class="col-md-3">
          <label class="form-label small mb-0">دسته علت</label>
          <select v-model="ncr.causeCategory" :disabled="!editableFollowUp" class="form-select form-select-sm">
            <option v-for="(t, k) in ncrCauses" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
        </div>
        <div class="col-md-9">
          <label class="form-label small mb-0">شرح علت</label>
          <input v-model="ncr.rootCause" :disabled="!editableFollowUp" class="form-control form-control-sm" />
        </div>
        <div class="col-md-6">
          <label class="form-label small mb-0">اقدام اصلاحی</label>
          <textarea v-model="ncr.correctiveAction" :disabled="!editableFollowUp" class="form-control form-control-sm" rows="2"></textarea>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">مسئول</label>
          <input v-model="ncr.correctiveActionOwner" :disabled="!editableFollowUp" class="form-control form-control-sm" />
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">مهلت</label>
          <input v-model="ncr.correctiveActionDueDate" :disabled="!editableFollowUp" class="form-control form-control-sm" type="date" />
        </div>
      </div>
      <div v-if="ncr.status === 2 && canClose" class="d-flex flex-wrap gap-2 mt-3 align-items-center">
        <button :disabled="busy || touchedByMe" class="btn btn-sm btn-success" @click="openClose">بستن گزارش…</button>
        <span v-if="touchedByMe" class="small text-warning">شما این گزارش را ثبت، ذخیره یا اجرا کرده‌اید؛ بستن آن با QC یا QA دیگری است.</span>
        <span v-else class="inv-muted small">بستن با نتیجه بررسی اثربخشی{{ needsReinspection ? " و بازرسی مجدد قبول‌شده" : "" }} است و با شخصی است که گزارش را ثبت، ویرایش یا اجرا نکرده است.</span>
      </div>
      <div v-if="open && !ncr.inspectionId && canCancel" class="d-flex flex-wrap gap-2 mt-3 align-items-center">
        <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="showCancel = true">ابطال گزارش…</button>
        <span class="inv-muted small">ابطال با ثبت دلیل است؛ مدیر QA هر گزارش باز را ابطال می‌کند و QC فقط گزارشی را که خودش ثبت یا ویرایش نکرده.</span>
      </div>
      <p v-if="ncr.closedAt && ncr.status === 3" class="small mt-2 mb-0">بسته شد: {{ ncr.closedByName }} {{ jalali(ncr.closedAt, true) }} — {{ ncr.effectivenessCheck }}</p>
    </div>

    <div v-if="!isNew" class="inv-card screen-only">
      <h2 class="inv-title mb-2">مهلت، پیامدها و CAPA</h2>
      <div class="row g-2">
        <div class="col-md-3">
          <label class="form-label small mb-0">مهلت تصمیم <span v-if="ncr.overdue" class="inv-badge bad">از مهلت گذشته</span></label>
          <input v-model="ncr.dueDate" :disabled="!open" class="form-control form-control-sm" type="date" />
          <span class="inv-muted">پیش‌فرض: جزئی ۷ روز، عمده ۱۴ روز (تنظیمات کیفیت)</span>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">روش تحلیل علت ریشه‌ای</label>
          <select v-model="ncr.rootCauseMethod" :disabled="!editableFollowUp" class="form-select form-select-sm">
            <option v-for="(t, k) in rootCauseMethods" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">اثر هزینه (ریال)</label>
          <input v-model="ncr.costImpact" :disabled="!editableFollowUp" class="form-control form-control-sm" min="0" type="number" />
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">اثر زمان (روز)</label>
          <input v-model="ncr.timeImpactDays" :disabled="!editableFollowUp" class="form-control form-control-sm" min="0" type="number" />
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">مرجع برگشت از مشتری</label>
          <input v-model="ncr.customerReturnRef" :disabled="!editableFollowUp" class="form-control form-control-sm inv-ltr" />
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">تجهیز اندازه‌گیری دخیل</label>
          <select v-model="ncr.affectedEquipmentId" :disabled="!editableFollowUp" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="e in equipment" :key="e.id" :value="e.id">{{ e.code }} - {{ e.name }}</option>
          </select>
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">CAPA مرتبط</label>
          <div class="small pt-1">
            <router-link v-if="ncr.capaId" :to="'/quality/capa/' + ncr.capaId" class="inv-ltr">{{ ncr.capaNumber }}</router-link>
            <router-link
              v-else-if="canCapa"
              :to="{ path: '/quality/capa/new', query: { source: 1, sourceId: ncr.id, problem: 'گزارش عدم انطباق ' + ncr.number + ': ' + (ncr.description || '') } }">
              ثبت CAPA برای این NCR
            </router-link>
            <span v-else class="inv-muted">ندارد</span>
          </div>
        </div>
      </div>
      <p v-if="ncr.sourceRecordNumber" class="small mt-2 mb-0">ایجاد خودکار از سند کیفیت: <span class="inv-ltr">{{ ncr.sourceRecordNumber }}</span></p>
      <p v-if="ncr.severity === 2" class="inv-muted mt-2 mb-0">عدم انطباق عمده کمیته فنی با حداقل سه عضو (کیفیت، مهندسی، تولید) می‌خواهد و CAPA آن را سیستم پیش‌نویس می‌کند.</p>
    </div>

    <!-- cancel: a reason that stays on the report -->
    <ReasonDialog
      v-if="showCancel"
      title="ابطال گزارش عدم انطباق"
      label="دلیل ابطال"
      confirm-text="ابطال گزارش"
      hint="گزارش باز ابطال می‌شود و دلیل، ابطال‌کننده و زمان در سابقه گزارش می‌ماند. گزارش حاصل از بازرسی ورودی ابطال نمی‌شود، و گزارشی که از بازرسی یا آزمون مردودشده ساخته شده تا ثبت بازرسی مجدد قبول‌شده ابطال نمی‌شود."
      :busy="busy"
      @confirm="cancelNcr"
      @close="showCancel = false" />

    <!-- execute: the target warehouse of a stock decision, or the proof that a decision about a product found in the workshop was carried out -->
    <div v-if="exec" class="inv-modal-backdrop" @click.self="exec = null">
      <div class="inv-modal" style="max-width: 760px" role="dialog" aria-modal="true">
        <h2 class="inv-title mb-2">اجرای تصمیم: {{ ncrDispositions[ncr.disposition] }}</h2>

        <template v-if="ncr.warehouseId">
          <p v-if="ncr.disposition === 5" class="small">سند برگشت به تامین‌کننده ساخته می‌شود؛ انبار اطلاعات خروج (راننده، خودرو، تحویل‌گیرنده) را کامل و سند را تایید می‌کند.</p>
          <template v-else>
            <label class="form-label small mb-0">
              {{ ncr.disposition === 4 ? "انبار ضایعات" : ncr.disposition === 2 || ncr.disposition === 3 ? "انبار قرنطینه / نگهداری QC" : "انبار مقصد" }} *
            </label>
            <select v-model="exec.targetWarehouseId" class="form-select form-select-sm">
              <option :value="null">-</option>
              <option v-for="w in targetOptions" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }} ({{ warehouseKinds[w.kind] }})</option>
            </select>
            <p v-if="ncr.disposition === 2 || ncr.disposition === 3" class="inv-muted small mt-1 mb-0">
              کالای دوباره‌کاری یا تعمیر هیچ‌گاه خودبه‌خود به موجودی قابل‌مصرف برنمی‌گردد: به انبار قرنطینه (غیر از انباری که کالا در آن است) منتقل می‌شود،
              سامانه بازرسی مجدد می‌سازد و گزارش فقط با بازرسی مجدد قبول‌شده بسته می‌شود.
            </p>
            <p v-if="!targetOptions.length" class="small text-danger mt-1 mb-0">
              {{ ncr.disposition === 2 || ncr.disposition === 3 ? "انبار قرنطینه دیگری تعریف نشده است؛ در بخش انبارها یک انبار از نوع «قرنطینه / بازرسی» تعریف کنید." : "انبار مناسبی پیدا نشد." }}
            </p>
          </template>
        </template>

        <template v-else>
          <p class="inv-muted small">کالای این گزارش در کارگاه است (خارج از انبار)؛ تصمیم با مدرکی که اجرای آن را ثابت می‌کند ثبت می‌شود.</p>

          <p v-if="ncr.disposition === 1" class="small mb-0">استفاده با مجوز ارفاقی مدرک دیگری نمی‌خواهد؛ مجوز هنگام تایید گزارش بررسی شده است.</p>

          <template v-else-if="ncr.disposition === 2 || ncr.disposition === 3">
            <div class="row g-2 align-items-end">
              <div class="col-md-5">
                <label class="form-label small mb-0">شناسه عملیات دوباره‌کاری پایان‌یافته</label>
                <input v-model="exec.reworkOperationId" class="form-control form-control-sm inv-ltr" min="1" type="number" />
                <span class="inv-muted small">عملیات دوباره‌کاری دستور تولید با بازرسی QC قبول‌شده. اگر تولید آن را به این گزارش وصل کرده، سامانه خودش پیدا می‌کند.</span>
              </div>
              <div class="col-md-7 small inv-muted">یا کاری که بیرون از دستور تولید انجام شده (مثلاً تعمیر در محل) را با بازرسی یا آزمون مجدد قبول‌شده ثابت کنید:</div>
            </div>
            <ReinspectionPicker v-model="exec.reinspection" :kinds="['QcRecord', 'TestReport']" :work-order-id="ncr.workOrderId" class="mt-2" />
          </template>

          <template v-else-if="ncr.disposition === 4">
            <div class="row g-2 align-items-end">
              <div class="col-md-6">
                <label class="form-label small mb-0">رسید ضایعات تاییدشده</label>
                <select v-model="exec.scrapDocumentId" class="form-select form-select-sm">
                  <option :value="null">-</option>
                  <option v-for="d in evidenceDocuments" :key="d.id" :value="d.id">{{ documentLabel(d) }}</option>
                </select>
              </div>
              <div class="col-md-3">
                <label class="form-label small mb-0">یا شناسه سند</label>
                <input v-model="exec.scrapDocumentId" class="form-control form-control-sm inv-ltr" min="1" type="number" />
              </div>
              <div class="col-md-3">
                <label class="form-label small mb-0">شناسه سریال ضایعات‌شده</label>
                <input v-model="exec.scrappedSerialUnitId" class="form-control form-control-sm inv-ltr" min="1" type="number" />
              </div>
            </div>
            <p class="inv-muted small mt-1 mb-0">
              ضایعات‌کردن کالای کارگاه با رسید ضایعات تاییدشده (با مقدار کافی و پس از تاریخ گزارش) ثبت می‌شود؛ برای کالای سریالی می‌توان وضعیت «ضایعات» همه سریال‌های گزارش را مدرک گرفت
              (شناسه سریال اختیاری است).
            </p>
          </template>

          <template v-else-if="ncr.disposition === 5">
            <div class="row g-2 align-items-end">
              <div class="col-md-8">
                <label class="form-label small mb-0">سند برگشت به تامین‌کننده (تاییدشده) *</label>
                <select v-model="exec.returnDocumentId" class="form-select form-select-sm">
                  <option :value="null">-</option>
                  <option v-for="d in evidenceDocuments" :key="d.id" :value="d.id">{{ documentLabel(d) }}</option>
                </select>
              </div>
              <div class="col-md-4">
                <label class="form-label small mb-0">یا شناسه سند</label>
                <input v-model="exec.returnDocumentId" class="form-control form-control-sm inv-ltr" min="1" type="number" />
              </div>
            </div>
            <p class="inv-muted small mt-1 mb-0">سند باید برای همین تامین‌کننده و کالا و تا مقدار گزارش باشد و برای گزارش دیگری ثبت نشده باشد.</p>
          </template>
        </template>

        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" type="button" @click="exec = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" type="button" @click="execute">اجرای تصمیم</button>
        </div>
      </div>
    </div>

    <!-- close: the effectiveness check and, for rework and repair, the accepted re-inspection -->
    <div v-if="closing" class="inv-modal-backdrop" @click.self="closing = null">
      <div class="inv-modal" style="max-width: 760px" role="dialog" aria-modal="true">
        <h2 class="inv-title mb-2">بستن گزارش {{ ncr.number }}</h2>
        <label class="form-label small mb-0">نتیجه بررسی اثربخشی اقدام اصلاحی *</label>
        <textarea v-model="closing.effectiveness" class="form-control form-control-sm" rows="3"></textarea>
        <template v-if="needsReinspection">
          <h3 class="inv-title mt-3 mb-1" style="font-size: 1rem">بازرسی مجدد</h3>
          <p v-if="reinspectionLinked" class="small mb-0">
            بازرسی مجدد قبول‌شده به گزارش وصل است ({{ reinspectionKinds[ncr.reinspectionKind] || ncr.reinspectionKind }} <span class="inv-ltr">#{{ ncr.reinspectionRecordId }}</span>)؛ هنگام بستن دوباره بررسی می‌شود.
          </p>
          <template v-else>
            <p class="inv-muted small">
              دوباره‌کاری و تعمیر فقط با بازرسی مجدد قبول‌شده بسته می‌شود. بازرسی ورودی مجدد را سامانه هنگام رسیدن کالا به انبار قرنطینه می‌سازد؛
              برای کار کارگاهی، سابقه بازرسی یا گزارش آزمون قبول‌شده‌ای را که پس از ثبت این گزارش و توسط شخص دیگری انجام شده انتخاب کنید.
            </p>
            <ReinspectionPicker v-model="closing.reinspection" :ncr-id="ncr.id" :work-order-id="ncr.workOrderId" />
          </template>
        </template>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" type="button" @click="closing = null">انصراف</button>
          <button :disabled="busy || !closing.effectiveness.trim()" class="btn btn-sm btn-success" type="button" @click="confirmClose">بستن گزارش</button>
        </div>
      </div>
    </div>

    <!-- notes of the AI seat QC-NCR-ASSIST for this report (it only suggests; the quality gate stays with the people) -->
    <AiNotesBox v-if="!isNew" :target-id="ncr.id" target-type="QualityNcr" />
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
