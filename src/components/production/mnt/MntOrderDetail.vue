<script setup lang="ts">
// One maintenance order: details, downtime, cause / action, spare parts, labour bookings and the actions.
import { computed, onMounted, ref, watch } from "vue";
import { useRouter } from "vue-router";
import FnDate from "@/components/fn/FnDate.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import DateTimeField from "./DateTimeField.vue";
import { addMaintenanceLabour, getMaintenanceOrder, maintenanceOrderAction, saveMaintenanceOrder } from "@/api/ProductionAPIs";
import { parseAmount } from "@/composables/fn";
import { fail, ok, today } from "@/composables/inventory";
import { badgeOfMaintenance, hoursText, maintenanceKinds, maintenanceOrderStatuses, persianOf, productionActionTitles, qtyText } from "@/composables/production";
import { hasRole, ProductionRoles } from "@/composables/roles";

const props = defineProps({ id: { type: Number, required: true } });
const router = useRouter();
const order = ref<any>(null);
const form = ref<any>(null);
const busy = ref(false);
const prompt = ref<null | "cancel" | "complete">(null);
const labour = ref({ workDate: today() as string | null, hours: "", note: "" });
const canWrite = hasRole(ProductionRoles.maintenanceWriters);
const isOpen = computed(() => !!order.value && (order.value.status === 0 || order.value.status === 1));
const editable = computed(() => canWrite && isOpen.value);
const partsLocked = computed(() => !!order.value?.materialRequestId);
const isBreakdown = computed(() => order.value?.kind === 2);

function toForm(o: any) {
  form.value = {
    task: o.task || "", dueDate: o.dueDate ? String(o.dueDate).slice(0, 10) : null, estimatedHours: String(o.estimatedHours ?? ""), checklist: o.checklist || "", costCenter: o.costCenter || "",
    downtimeStart: o.downtimeStart || null, downtimeEnd: o.downtimeEnd || null, cause: o.cause || "", actionTaken: o.actionTaken || "",
    parts: (o.parts || []).map((p: any) => ({ itemId: p.itemId, item: { id: p.itemId, code: p.itemCode, title: p.itemTitle }, quantity: String(p.quantity), note: p.note || "" })),
  };
}
function take(o: any) {
  order.value = o;
  toForm(o);
}

async function load() {
  const [data, error] = await getMaintenanceOrder(props.id);
  if (error) return fail(error);
  take(data);
}

function payload() {
  const f = form.value;
  const dto: any = {
    id: order.value.id, task: f.task, dueDate: f.dueDate || null, estimatedHours: parseAmount(f.estimatedHours) ?? 0, checklist: f.checklist, costCenter: f.costCenter,
    downtimeStart: f.downtimeStart || null, downtimeEnd: f.downtimeEnd || null, cause: f.cause, actionTaken: f.actionTaken, rowVersion: order.value.rowVersion,
  };
  if (!partsLocked.value) dto.parts = f.parts.filter((p: any) => p.itemId).map((p: any) => ({ itemId: p.itemId, quantity: parseAmount(p.quantity) ?? 0, note: p.note }));
  return dto;
}

/** Saves the form; answers the fresh order (or null after the error was shown). */
async function persist(): Promise<any | null> {
  const [data, error] = await saveMaintenanceOrder(payload());
  if (error) {
    fail(error);
    return null;
  }
  return data;
}

async function save() {
  busy.value = true;
  const data = await persist();
  busy.value = false;
  if (!data) return;
  take(data);
  ok("دستور نگهداری ذخیره شد.");
}

/** Runs an action; unsaved form changes are saved first so the cause / action text the service checks is the one on screen. */
async function act(action: "start" | "complete" | "cancel" | "request-parts" | "draft-issue", extra: { reason?: string } = {}) {
  busy.value = true;
  let rowVersion = order.value.rowVersion;
  if (editable.value && action !== "cancel") {
    const saved = await persist();
    if (!saved) {
      busy.value = false;
      return;
    }
    rowVersion = saved.rowVersion;
  }
  const [data, error] = await maintenanceOrderAction({ id: order.value.id, action, reason: extra.reason, rowVersion });
  busy.value = false;
  if (error) {
    await load();
    return fail(error);
  }
  take(data);
  ok(productionActionTitles[action] + " انجام شد.");
}

async function addLabour() {
  const hours = parseAmount(labour.value.hours);
  if (!hours || hours <= 0) return fail("ساعت کار را وارد کنید.");
  busy.value = true;
  const [data, error] = await addMaintenanceLabour({ orderId: order.value.id, workDate: labour.value.workDate, hours, note: labour.value.note });
  busy.value = false;
  if (error) return fail(error);
  take(data);
  labour.value = { workDate: today(), hours: "", note: "" };
  ok("ساعت کار ثبت شد.");
}

function addPart() {
  form.value.parts.push({ itemId: null, item: null, quantity: "1", note: "" });
}
function pickPart(p: any, item: any) {
  p.item = item;
  p.itemId = item?.id ?? null;
}

watch(() => props.id, load);
onMounted(load);
</script>

<template>
  <div v-if="order && form">
    <div class="inv-toolbar mb-2">
      <div>
        <h2 class="inv-title mb-0">دستور نگهداری <span class="inv-ltr">{{ order.number }}</span></h2>
        <span class="inv-muted">{{ order.machineTitle }}</span>
        <span :class="badgeOfMaintenance(order.status)" class="inv-badge ms-2">{{ maintenanceOrderStatuses[order.status] }}</span>
        <span class="inv-badge info ms-1">{{ maintenanceKinds[order.kind] }}</span>
      </div>
      <button class="btn btn-sm btn-outline-secondary" @click="router.push('/production/maintenance')">بازگشت به فهرست</button>
    </div>

    <div v-if="canWrite && isOpen" class="d-flex gap-2 flex-wrap mb-3">
      <button v-if="order.status === 0" :disabled="busy" class="btn btn-sm btn-primary" @click="act('start')">شروع کار</button>
      <button :disabled="busy" class="btn btn-sm btn-success" @click="act('complete')">تکمیل و بستن</button>
      <button v-if="!order.materialRequestId" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="act('request-parts')">درخواست قطعات یدکی</button>
      <button v-else-if="!order.issueDocumentId" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="act('draft-issue')">پیش‌نویس حواله قطعات</button>
      <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="prompt = 'cancel'">لغو دستور</button>
    </div>
    <p v-if="isBreakdown && isOpen" class="inv-muted small">برای بستن دستور خرابی، علت خرابی و اقدام انجام‌شده الزامی است؛ با دکمه «تکمیل و بستن» ابتدا تغییرات ذخیره می‌شود.</p>

    <div class="row g-2 mb-3">
      <div class="col-md-8"><label class="form-label small mb-0">شرح کار</label><input v-model="form.task" :disabled="!editable" class="form-control form-control-sm" /></div>
      <div class="col-md-4"><label class="form-label small mb-0">مرکز هزینه</label><input v-model="form.costCenter" :disabled="!editable" class="form-control form-control-sm inv-ltr" /></div>
      <div class="col-md-3"><label class="form-label small mb-0">سررسید</label><FnDate v-model="form.dueDate" :disabled="!editable" /></div>
      <div class="col-md-3"><label class="form-label small mb-0">ساعت برآوردی</label><input v-model="form.estimatedHours" :disabled="!editable" class="form-control form-control-sm fn-num" inputmode="decimal" /></div>
      <div class="col-md-3"><label class="form-label small mb-0">شروع توقف ماشین</label><DateTimeField v-model="form.downtimeStart" :disabled="!editable" /></div>
      <div class="col-md-3"><label class="form-label small mb-0">پایان توقف ماشین</label><DateTimeField v-model="form.downtimeEnd" :disabled="!editable" /></div>
      <div class="col-md-6"><label class="form-label small mb-0">فهرست بازبینی</label><textarea v-model="form.checklist" :disabled="!editable" class="form-control form-control-sm" rows="4"></textarea></div>
      <div class="col-md-6">
        <label class="form-label small mb-0">علت خرابی<span v-if="isBreakdown"> *</span></label><textarea v-model="form.cause" :disabled="!editable" class="form-control form-control-sm" rows="2"></textarea>
        <label class="form-label small mb-0 mt-1">اقدام انجام‌شده *</label><textarea v-model="form.actionTaken" :disabled="!editable" class="form-control form-control-sm" rows="2"></textarea>
      </div>
    </div>

    <div class="small mb-3">
      <span v-if="order.startedAtText" class="me-3">شروع: {{ order.startedAtText }}</span>
      <span v-if="order.doneAtText" class="me-3">پایان: {{ order.doneAtText }}</span>
      <span v-if="order.downtimeHours !== null && order.downtimeHours !== undefined" class="me-3">مدت توقف: {{ hoursText(order.downtimeHours) }} ساعت</span>
      <span class="me-3">ساعت کار ثبت‌شده: {{ hoursText(order.labourHours) }}</span>
      <span v-if="order.cancelReason" class="text-danger">دلیل لغو: {{ order.cancelReason }}</span>
    </div>

    <h3 class="inv-title mb-2">قطعات یدکی</h3>
    <div class="table-responsive mb-2">
      <table class="table table-sm">
        <thead><tr><th>قطعه</th><th style="width: 8rem">مقدار</th><th>توضیح</th><th v-if="editable && !partsLocked"></th></tr></thead>
        <tbody>
          <tr v-if="!form.parts.length"><td colspan="4" class="text-center inv-muted">قطعه‌ای ثبت نشده است.</td></tr>
          <tr v-for="(p, i) in form.parts" :key="i">
            <td><ItemPicker v-if="editable && !partsLocked" :model-value="p.item" @select="pickPart(p, $event)" /><span v-else><span class="inv-ltr">{{ p.item?.code }}</span> {{ p.item?.title }}</span></td>
            <td><input v-if="editable && !partsLocked" v-model="p.quantity" class="form-control form-control-sm fn-num" inputmode="decimal" /><span v-else>{{ qtyText(Number(p.quantity)) }}</span></td>
            <td><input v-if="editable && !partsLocked" v-model="p.note" class="form-control form-control-sm" /><span v-else>{{ p.note }}</span></td>
            <td v-if="editable && !partsLocked"><button class="btn btn-sm btn-outline-danger" @click="form.parts.splice(i, 1)">حذف</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <div class="mb-3 small">
      <button v-if="editable && !partsLocked" class="btn btn-sm btn-outline-secondary me-2" @click="addPart">افزودن قطعه</button>
      <span v-if="partsLocked" class="inv-muted">فهرست قطعات پس از درخواست کالا قفل شده است.</span>
      <a v-if="order.materialRequestId" class="me-3" href="#" @click.prevent="router.push('/inventory/material-requests/' + order.materialRequestId)">درخواست کالا <span class="inv-ltr">{{ order.materialRequestNumber }}</span></a>
      <a v-if="order.issueDocumentId" href="#" @click.prevent="router.push('/inventory/documents/' + order.issueDocumentId)">
        {{ order.issueDocumentNumber ? "حواله " + order.issueDocumentNumber : "پیش‌نویس حواله (بدون شماره) - شناسه سند " + order.issueDocumentId }}
      </a>
      <span v-else-if="order.materialRequestId" class="inv-muted">حواله پس از تایید نهایی درخواست کالا پیش‌نویس می‌شود.</span>
    </div>

    <div v-if="editable" class="mb-4"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره تغییرات</button></div>

    <h3 class="inv-title mb-2">ساعت کار نیروی نگهداری</h3>
    <div class="table-responsive mb-2">
      <table class="table table-sm">
        <thead><tr><th>تاریخ</th><th>ساعت</th><th>توضیح</th></tr></thead>
        <tbody>
          <tr v-if="!(order.labour || []).length"><td colspan="3" class="text-center inv-muted">ساعت کاری ثبت نشده است.</td></tr>
          <tr v-for="l in (order.labour || [])" :key="l.id"><td>{{ persianOf(l.workDateText, l.workDate) }}</td><td>{{ hoursText(l.hours) }}</td><td>{{ l.note }}</td></tr>
        </tbody>
      </table>
    </div>
    <div v-if="editable" class="d-flex gap-2 align-items-end flex-wrap">
      <div><label class="form-label small mb-0">تاریخ</label><FnDate v-model="labour.workDate" /></div>
      <div><label class="form-label small mb-0">ساعت (مثلا ۱٫۵)</label><input v-model="labour.hours" class="form-control form-control-sm fn-num" inputmode="decimal" style="max-width: 7rem" /></div>
      <div class="flex-grow-1"><label class="form-label small mb-0">توضیح</label><input v-model="labour.note" class="form-control form-control-sm" /></div>
      <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="addLabour">ثبت ساعت کار من</button>
    </div>

    <PromptModal v-if="prompt === 'cancel'" :fields="[{ key: 'reason', label: 'دلیل لغو', type: 'textarea', required: true }]" confirm-text="لغو دستور" title="لغو دستور نگهداری"
      @cancel="prompt = null" @confirm="(v: any) => { prompt = null; act('cancel', { reason: v.reason }); }" />
  </div>
</template>
