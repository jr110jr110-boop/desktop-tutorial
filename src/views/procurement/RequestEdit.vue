<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { isSeatDraft, SEAT_DRAFT_TEXT } from "@/composables/ai";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import PrintForm from "@/components/print/PrintForm.vue";
import { getContracts, getWorkOrders, rowsOf } from "@/api/InventoryAPIs";
import { getInquiries, getPurchaseRequest, purchaseRequestAction, savePurchaseRequest } from "@/api/ProcurementAPIs";
import { fail, jalali, num, ok, printPage, rial, today } from "@/composables/inventory";
import { inquiryStatuses, purchaseKinds, requestSources, requestStatuses } from "@/composables/procurement";
import { hasRole, ProcurementRoles } from "@/composables/roles";

// Purchase request: draft -> submitted -> planning approval (+ CEO approval from the medium purchase limit) -> inquiry.
const route = useRoute();
const router = useRouter();
const req = ref<any>(null);
const inquiries = ref<any[]>([]);
const workOrders = ref<any[]>([]);
const contracts = ref<any[]>([]);
const busy = ref(false);
const dialog = ref<null | { action: string; title: string; label: string; value: string; required: boolean }>(null);

const isNew = computed(() => !req.value?.id);
const isDraft = computed(() => req.value && req.value.status === 0);
const canEdit = computed(() => isDraft.value && hasRole(ProcurementRoles.requesters));
const canPlanning = hasRole(ProcurementRoles.planning);
const canManagement = hasRole(ProcurementRoles.management);
const canCommercial = hasRole(ProcurementRoles.commercial);
const estimated = computed(() => (req.value?.lines || []).reduce((s: number, l: any) => s + (Number(l.quantity) || 0) * (Number(l.estimatedUnitPrice) || 0), 0));

function newLine() {
  return { key: Math.random().toString(36).slice(2), item: null as any, description: "", unit: "", quantity: 1, neededBy: "", estimatedUnitPrice: null as number | null, technicalRequirements: "" };
}

function blank() {
  return {
    id: null, status: 0, source: 3, kind: 1, requestDate: today(), workOrderId: null, contractId: null, costCenter: "",
    materialRequestId: route.query.materialRequest ? Number(route.query.materialRequest) : null, description: "", lines: [newLine()],
  };
}

function load(dto: any) {
  req.value = {
    ...dto,
    requestDate: (dto.requestDate || "").slice(0, 10),
    lines: dto.lines.map((l: any) => ({
      ...newLine(),
      item: l.itemId ? { id: l.itemId, code: l.itemCode, title: l.itemTitle, unit: l.unit } : null,
      description: l.description || "", unit: l.unit || "", quantity: l.quantity, orderedQuantity: l.orderedQuantity,
      neededBy: l.neededBy ? l.neededBy.slice(0, 10) : "", estimatedUnitPrice: l.estimatedUnitPrice, technicalRequirements: l.technicalRequirements || "",
    })),
  };
}

function onItem(line: any) {
  if (line.item) {
    line.unit = line.item.unit;
    if (!line.description) line.description = line.item.title;
  }
}

async function save(submit = false): Promise<boolean> {
  const r = req.value;
  const lines = r.lines.filter((l: any) => l.item || l.description);
  if (!lines.length) {
    fail("حداقل یک ردیف لازم است.");
    return false;
  }
  busy.value = true;
  const [data, error] = await savePurchaseRequest({
    id: r.id, source: r.source, kind: r.kind, requestDate: r.requestDate, workOrderId: r.workOrderId, contractId: r.contractId,
    costCenter: r.costCenter, materialRequestId: r.materialRequestId, description: r.description, submit,
    lines: lines.map((l: any) => ({
      itemId: l.item?.id ?? null, description: l.description, unit: l.unit || l.item?.unit, quantity: Number(l.quantity) || 0,
      neededBy: l.neededBy || null, estimatedUnitPrice: l.estimatedUnitPrice === "" ? null : l.estimatedUnitPrice,
      technicalRequirements: l.technicalRequirements,
    })),
  });
  busy.value = false;
  if (error) {
    fail(error);
    return false;
  }
  load(data);
  ok(submit ? "درخواست ارسال شد." : "ذخیره شد.");
  if (route.params.id === "new") router.replace({ name: "ProcurementRequestEdit", params: { id: data.id } });
  return true;
}

async function act(action: string, note?: string, reference?: string) {
  busy.value = true;
  const [data, error] = await purchaseRequestAction(req.value.id, action, note, reference);
  busy.value = false;
  if (error) return fail(error);
  load(data);
  ok("ثبت شد.");
}

function ask(action: string) {
  if (action === "approve-management" && req.value.needsBoard)
    dialog.value = { action, title: "تایید مدیرعامل — خرید عمده", label: "شماره مصوبه هیئت‌مدیره *", value: "", required: true };
  else if (action === "reject") dialog.value = { action, title: "رد درخواست", label: "دلیل رد *", value: "", required: true };
  else act(action);
}

async function confirmDialog() {
  const d = dialog.value!;
  if (d.required && !d.value.trim()) return fail(d.label.replace(" *", "") + " را بنویسید.");
  dialog.value = null;
  if (d.action === "reject") await act("reject", d.value);
  else await act(d.action, undefined, d.value);
}

async function loadInquiries() {
  const [data] = await getInquiries(req.value.id);
  inquiries.value = data || [];
}

const signatures = computed(() => {
  const r = req.value;
  const list: any[] = [
    { title: "درخواست‌کننده", name: r.creatorName, date: jalali(r.createDate) },
    { title: "مدیر برنامه‌ریزی و کنترل پروژه", name: r.planningApprovedByName, date: jalali(r.planningApprovedAt) },
  ];
  if (r.needsManagement) list.push({ title: "مدیرعامل", name: r.managementApprovedByName, date: jalali(r.managementApprovedAt) });
  return list;
});

onMounted(async () => {
  getWorkOrders().then(([l]) => (workOrders.value = rowsOf(l)));
  getContracts().then(([l]) => (contracts.value = rowsOf(l)));
  if (route.params.id === "new") req.value = blank();
  else {
    const [data, error] = await getPurchaseRequest(Number(route.params.id));
    if (error) return fail(error);
    load(data);
    loadInquiries();
  }
});
</script>

<template>
  <div v-if="req" class="inv-page">
    <PrintForm
      v-if="!isNew"
      title="درخواست خرید"
      :fields="[
        { label: 'شماره', value: req.number, ltr: true },
        { label: 'تاریخ', value: jalali(req.requestDate) },
        { label: 'نوع خرید', value: purchaseKinds[req.kind] },
        { label: 'منبع', value: requestSources[req.source] },
        { label: 'دستور ساخت', value: req.workOrderCode || '-' },
        { label: 'پروژه / قرارداد', value: req.contractCode || '-' },
        { label: 'مرکز هزینه', value: req.costCenter || '-', ltr: true },
        { label: 'برآورد کل (ریال)', value: rial(req.estimatedAmount) },
        { label: 'تعداد استعلام لازم', value: req.requiredQuotes },
        { label: 'مصوبه هیئت‌مدیره', value: req.boardResolution || '-' },
      ]"
      :notes="req.description ? [{ label: 'توضیح', value: req.description }] : []"
      :signatures="signatures"
      distribution="توزیع نسخ: ۱- بازرگانی ۲- برنامه‌ریزی و کنترل پروژه ۳- مالی">
      <table>
        <thead>
          <tr><th>ردیف</th><th>کد کالا</th><th>شرح</th><th>واحد</th><th>مقدار</th><th>نیاز تا</th><th>فی برآوردی</th><th>مبلغ برآوردی</th><th>مشخصات فنی</th></tr>
        </thead>
        <tbody>
          <tr v-for="(l, i) in req.lines" :key="l.key">
            <td>{{ i + 1 }}</td>
            <td class="inv-ltr">{{ l.item?.code }}</td>
            <td>{{ l.description }}</td>
            <td>{{ l.unit }}</td>
            <td class="num">{{ num(l.quantity) }}</td>
            <td>{{ jalali(l.neededBy) }}</td>
            <td class="num">{{ rial(l.estimatedUnitPrice) }}</td>
            <td class="num">{{ l.estimatedUnitPrice ? rial(l.quantity * l.estimatedUnitPrice) : "" }}</td>
            <td>{{ l.technicalRequirements }}</td>
          </tr>
        </tbody>
      </table>
    </PrintForm>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">درخواست خرید <span v-if="req.number" class="inv-ltr ms-2">{{ req.number }}</span>
            <span v-if="isSeatDraft(req)" class="inv-badge info ms-2" title="این پیش‌نویس را دستیار هوشمند ساخته است؛ بررسی، ویرایش و ارسال با برنامه‌ریزی است.">{{ SEAT_DRAFT_TEXT }}</span></h1>
          <span class="inv-badge" :class="{ ok: req.status === 2 || req.status === 3, warn: req.status <= 1, bad: req.status === 9 }">{{ requestStatuses[req.status] }}</span>
        </div>
        <div class="d-flex flex-wrap gap-2">
          <router-link :to="{ name: 'ProcurementRequests' }" class="btn btn-sm btn-outline-secondary">فهرست</router-link>
          <button v-if="!isNew" class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <template v-if="canEdit">
            <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="save(false)">ذخیره پیش‌نویس</button>
            <button :disabled="busy" class="btn btn-sm btn-success" @click="save(true)">ذخیره و ارسال</button>
          </template>
          <button v-if="req.status === 1 && !req.planningApprovedAt && canPlanning" :disabled="busy" class="btn btn-sm btn-outline-success" @click="ask('approve-planning')">تایید برنامه‌ریزی</button>
          <button v-if="req.status === 1 && req.needsManagement && !req.managementApprovedAt && canManagement" :disabled="busy" class="btn btn-sm btn-outline-success" @click="ask('approve-management')">تایید مدیرعامل</button>
          <button v-if="req.status === 1 && (canPlanning || canManagement)" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="ask('reject')">رد</button>
          <button v-if="(req.status === 2 || req.status === 3) && canPlanning" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="ask('close')">بستن</button>
        </div>
      </div>

      <div v-if="!isNew" class="d-flex flex-wrap gap-2 mb-2">
        <span class="inv-badge">برآورد: {{ rial(req.estimatedAmount) }} ریال</span>
        <span class="inv-badge">استعلام لازم: {{ req.requiredQuotes }}</span>
        <span class="inv-badge" :class="{ warn: req.needsManagement }">{{ req.needsManagement ? "نیازمند تایید مدیرعامل" : "تایید مدیرعامل لازم نیست" }}</span>
        <span v-if="req.needsBoard" class="inv-badge warn">خرید عمده: مصوبه هیئت‌مدیره {{ req.boardResolution || "(در تایید مدیرعامل ثبت می‌شود)" }}</span>
        <span class="inv-badge" :class="{ ok: req.planningApprovedAt }">برنامه‌ریزی: {{ req.planningApprovedByName || "—" }}</span>
        <span v-if="req.needsManagement" class="inv-badge" :class="{ ok: req.managementApprovedAt }">مدیرعامل: {{ req.managementApprovedByName || "—" }}</span>
      </div>
      <div v-if="req.status === 9" class="alert alert-danger small py-2">رد شد: {{ req.rejectReason }}</div>
      <p v-if="req.status === 1" class="inv-muted small">تایید کننده باید غیر از ثبت‌کننده باشد؛ تایید مدیرعامل باید از شخصی غیر از تاییدکننده برنامه‌ریزی باشد.</p>

      <div class="row g-2">
        <div class="col-md-2">
          <label class="form-label small mb-0">تاریخ</label>
          <input v-model="req.requestDate" :disabled="!canEdit" class="form-control form-control-sm" type="date" />
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">نوع خرید</label>
          <select v-model="req.kind" :disabled="!canEdit" class="form-select form-select-sm">
            <option v-for="(t, k) in purchaseKinds" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">منبع</label>
          <select v-model="req.source" :disabled="!canEdit" class="form-select form-select-sm">
            <option v-for="(t, k) in requestSources" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">دستور ساخت</label>
          <select v-model="req.workOrderId" :disabled="!canEdit" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="w in workOrders" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option>
          </select>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">قرارداد / پروژه</label>
          <select v-model="req.contractId" :disabled="!canEdit" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="c in contracts" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option>
          </select>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">مرکز هزینه</label>
          <input v-model="req.costCenter" :disabled="!canEdit" class="form-control form-control-sm inv-ltr" />
        </div>
        <div class="col-md-9">
          <label class="form-label small mb-0">توضیح</label>
          <input v-model="req.description" :disabled="!canEdit" class="form-control form-control-sm" />
        </div>
      </div>
    </div>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h2 class="inv-title">ردیف‌ها <span class="inv-muted">برآورد: {{ rial(estimated) }} ریال</span></h2>
        <button v-if="canEdit" class="btn btn-sm btn-outline-primary" @click="req.lines.push(newLine())">+ ردیف</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-bordered">
          <thead>
            <tr><th>#</th><th style="min-width: 240px">کالا (برای خدمات خالی بماند)</th><th style="min-width: 200px">شرح</th><th style="width: 6rem">واحد</th><th style="width: 7rem">مقدار</th><th style="width: 9rem">نیاز تا</th><th style="width: 9rem">فی برآوردی (ریال)</th><th style="min-width: 180px">مشخصات فنی</th><th v-if="req.id">سفارش‌شده</th><th v-if="canEdit"></th></tr>
          </thead>
          <tbody>
            <tr v-for="(l, i) in req.lines" :key="l.key">
              <td>{{ i + 1 }}</td>
              <td>
                <ItemPicker v-if="canEdit" v-model="l.item" @select="onItem(l)" />
                <span v-else><span class="inv-ltr">{{ l.item?.code }}</span> {{ l.item?.title }}</span>
              </td>
              <td><input v-model="l.description" :disabled="!canEdit" class="form-control form-control-sm" /></td>
              <td><input v-model="l.unit" :disabled="!canEdit" class="form-control form-control-sm" /></td>
              <td><input v-model.number="l.quantity" :disabled="!canEdit" class="form-control form-control-sm" step="any" type="number" /></td>
              <td><input v-model="l.neededBy" :disabled="!canEdit" class="form-control form-control-sm" type="date" /></td>
              <td><input v-model.number="l.estimatedUnitPrice" :disabled="!canEdit" class="form-control form-control-sm" type="number" /></td>
              <td><input v-model="l.technicalRequirements" :disabled="!canEdit" class="form-control form-control-sm" /></td>
              <td v-if="req.id" class="num">{{ num(l.orderedQuantity) }}</td>
              <td v-if="canEdit"><button class="btn btn-sm btn-link text-danger p-0" @click="req.lines.splice(i, 1)">×</button></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <div v-if="!isNew && (req.status === 2 || req.status === 3 || inquiries.length)" class="inv-card screen-only">
      <div class="inv-toolbar">
        <h2 class="inv-title">استعلام‌ها</h2>
        <router-link v-if="req.status === 2 && canCommercial" :to="{ name: 'ProcurementInquiryEdit', params: { id: 'new' }, query: { request: req.id } }" class="btn btn-sm btn-primary">استعلام جدید</router-link>
      </div>
      <p v-if="!inquiries.length" class="inv-muted mb-0">استعلامی ثبت نشده است.</p>
      <table v-else class="table table-sm table-hover mb-0">
        <thead><tr><th>شماره</th><th>تاریخ</th><th>وضعیت</th><th>سفارش خرید</th></tr></thead>
        <tbody>
          <tr v-for="q in inquiries" :key="q.id" role="button" @click="router.push({ name: 'ProcurementInquiryEdit', params: { id: q.id } })">
            <td class="inv-ltr">{{ q.number }}</td>
            <td>{{ jalali(q.inquiryDate) }}</td>
            <td>{{ inquiryStatuses[q.status] }}</td>
            <td class="inv-ltr">{{ q.purchaseOrderNumber }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="dialog" class="inv-modal-backdrop" @click.self="dialog = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ dialog.title }}</h2>
        <label class="form-label small mb-0">{{ dialog.label }}</label>
        <input v-model="dialog.value" class="form-control form-control-sm" />
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="dialog = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="confirmDialog">ثبت</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
