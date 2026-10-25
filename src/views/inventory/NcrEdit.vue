<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import AiNotesBox from "@/components/ai/AiNotesBox.vue";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import PrintForm from "@/components/print/PrintForm.vue";
import {
  approveNcr,
  closeNcr,
  executeNcr,
  getContracts,
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
import { qualityFormCodes, rootCauseMethods } from "@/composables/quality";
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
import { hasRole, InventoryRoles, QualityRoles, Roles } from "@/composables/roles";

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
const effectiveness = ref("");
const cancelReason = ref("");
const supplierReturn = ref<any>(null);
const equipment = ref<any[]>([]);
const printMode = ref<"ncr" | "return">("ncr");

const isNew = computed(() => !ncr.value?.id);
const open = computed(() => ncr.value && ncr.value.status === 0);
const editableFollowUp = computed(() => ncr.value && ncr.value.status < 3);
const needsConcession = computed(() => ncr.value && [1, 3].includes(ncr.value.disposition));
// the stock document of the decision: scrap goes to the scrap warehouse, other decisions to a normal one
const targetOptions = computed(() =>
  ncr.value?.disposition === 4 ? warehouses.value.filter((w) => w.kind === 6) : warehouses.value.filter((w) => ![5, 6, 7].includes(w.kind))
);
const canQc = hasRole(InventoryRoles.qc);
const equipmentLabel = (id: number | null) => {
  const e = equipment.value.find((x) => x.id === id);
  return e ? `${e.code} ${e.name}` : "-";
};
const canSign = (role: string) => hasRole([role]);
const canExecute = hasRole(InventoryRoles.executeNcr);
const canCapa = hasRole(QualityRoles.capaStaff);

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
  load(data);
  if (!silent) ok("ذخیره شد.");
  if (route.params.id === "new") router.replace({ name: "InventoryNcrEdit", params: { id: data.id } });
  return true;
}

/** kind: 4 quality, 5 engineering, 3 manufacturing; concession is signed by engineering. */
async function sign(kind: number, concession = false) {
  if (open.value && !(await save(true))) return;
  busy.value = true;
  const [data, error] = await approveNcr({ id: ncr.value.id, kind, concession });
  busy.value = false;
  if (error) return fail(error);
  load(data);
  ok(data.status === 1 ? "تصمیم تایید نهایی شد." : "امضا ثبت شد.");
}

async function execute() {
  busy.value = true;
  const [data, error] = await executeNcr({ id: ncr.value.id, targetWarehouseId: targetWarehouseId.value });
  busy.value = false;
  if (error) return fail(error);
  load(data);
  ok(
    ncr.value.disposition === 5
      ? "سند برگشت به تامین‌کننده ساخته شد؛ انبار اطلاعات خروج را کامل و تایید کند."
      : "تصمیم با سند انبار اجرا شد."
  );
}

async function close(cancel = false) {
  if (!cancel && editableFollowUp.value && !(await save(true))) return;
  busy.value = true;
  const [data, error] = await closeNcr({ id: ncr.value.id, effectivenessCheck: effectiveness.value, cancel, reason: cancelReason.value });
  busy.value = false;
  if (error) return fail(error);
  load(data);
  ok(cancel ? "گزارش ابطال شد." : "گزارش بسته شد.");
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
          <span class="inv-badge">{{ ncrStatuses[ncr.status] }}</span>
        </div>
        <div class="d-flex flex-wrap gap-2">
          <router-link :to="{ name: 'InventoryNcrs' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button v-if="!isNew" class="btn btn-sm btn-outline-secondary" @click="printNcr">چاپ FO-QC-013</button>
          <button v-if="!isNew && ncr.disposition === 5" class="btn btn-sm btn-outline-secondary" @click="printReturn">گزارش مرجوعی (بازرگانی)</button>
          <button v-if="editableFollowUp && canQc" :disabled="busy" class="btn btn-sm btn-primary" @click="save()">ذخیره</button>
        </div>
      </div>

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
        <template v-if="open">
          <button v-if="!ncr.qualityApprovedAt && canSign(Roles.QC)" :disabled="busy" class="btn btn-sm btn-outline-success" @click="sign(4)">امضای کنترل کیفیت</button>
          <button v-if="!ncr.engineeringApprovedAt && canSign(Roles.ENGINEERING)" :disabled="busy" class="btn btn-sm btn-outline-success" @click="sign(5)">امضای مهندسی</button>
          <button v-if="!ncr.manufacturingApprovedAt && canSign(Roles.CONSTRUCTION)" :disabled="busy" class="btn btn-sm btn-outline-success" @click="sign(3)">امضای تولید</button>
          <button
            v-if="needsConcession && !ncr.concessionApprovedAt && canSign(Roles.ENGINEERING)"
            :disabled="busy"
            class="btn btn-sm btn-outline-success"
            @click="sign(5, true)">
            صدور مجوز ارفاقی
          </button>
        </template>
      </div>
      <p class="inv-muted mt-2 mb-0">
        هر امضا باید از شخص دیگری باشد. تغییر نوع یا تصمیم، امضاها را پاک می‌کند. کالای متعلق به پیمانکار ضایعات شرکت نمی‌شود و مرجوع می‌گردد.
      </p>

      <div v-if="ncr.status === 1 && canExecute" class="d-flex flex-wrap gap-2 mt-3 align-items-end">
        <div v-if="ncr.disposition !== 5 && ncr.warehouseId">
          <label class="form-label small mb-0">{{ ncr.disposition === 4 ? "انبار ضایعات" : "انبار مقصد" }}</label>
          <select v-model="targetWarehouseId" class="form-select form-select-sm" style="min-width: 240px">
            <option :value="null">-</option>
            <option v-for="w in targetOptions" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }} ({{ warehouseKinds[w.kind] }})</option>
          </select>
        </div>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="execute">اجرای تصمیم{{ ncr.warehouseId ? " (سند انبار)" : "" }}</button>
      </div>
      <p v-if="ncr.resultDocumentId" class="small mt-2 mb-0">
        سند اجرای تصمیم:
        <router-link :to="{ name: 'InventoryDocumentEdit', params: { id: ncr.resultDocumentId } }" class="inv-ltr">{{ ncr.resultDocumentNumber }}</router-link>
      </p>
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
      <div v-if="ncr.status === 2 && canQc" class="d-flex gap-2 mt-3 align-items-end">
        <div class="flex-grow-1">
          <label class="form-label small mb-0">نتیجه بررسی اثربخشی *</label>
          <input v-model="effectiveness" class="form-control form-control-sm" />
        </div>
        <button :disabled="busy" class="btn btn-sm btn-success" @click="close()">بستن گزارش</button>
      </div>
      <div v-if="open && !ncr.inspectionId && canQc" class="d-flex gap-2 mt-3 align-items-end">
        <div class="flex-grow-1">
          <label class="form-label small mb-0">علت ابطال</label>
          <input v-model="cancelReason" class="form-control form-control-sm" />
        </div>
        <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="close(true)">ابطال گزارش</button>
      </div>
      <p v-if="ncr.closedAt" class="small mt-2 mb-0">بسته شد: {{ ncr.closedByName }} {{ jalali(ncr.closedAt, true) }} — {{ ncr.effectivenessCheck }}</p>
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

    <!-- notes of the AI seat QC-NCR-ASSIST for this report (it only suggests; the quality gate stays with the people) -->
    <AiNotesBox v-if="!isNew" :target-id="ncr.id" target-type="QualityNcr" />
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
