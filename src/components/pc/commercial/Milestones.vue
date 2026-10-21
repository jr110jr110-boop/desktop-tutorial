<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { deleteMilestone, evaluateMilestones, getActivities, getMilestones, milestoneAction, saveMilestone } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import JDate from "@/components/pc/JDate.vue";
import WorkOrderOfContract from "@/components/pc/WorkOrderOfContract.vue";
import { fail, jalali, ok, rial } from "@/composables/inventory";
import { milestoneKinds, milestoneStatuses, milestoneTriggers, pct, sumOf } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Payment milestones of the contract: the system sets «due» when the trigger happens (date, progress, milestone, release note, delivery, handover, client letter);
// commercial/financial then record the statement, the invoice and the payment.
const emit = defineEmits(["payment-request"]);
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const acts = ref<any[]>([]);
const form = ref<any>(null);
const act = ref<{ m: any; action: string; value: string; id: number | null } | null>(null);
const busy = ref(false);
const canEdit = hasRole(ProjectControlRoles.commercial);
const canAct = hasRole(ProjectControlRoles.commercialFinance);

async function load() {
  const [[d, e], [a]] = await Promise.all([getMilestones({ ContractId: props.contractId }), getActivities({ ContractId: props.contractId })]);
  if (e) return fail(e);
  rows.value = rowsOf(d).sort((x: any, y: any) => x.seq - y.seq);
  acts.value = rowsOf(a).filter((x: any) => x.isMilestone || true);
}
const total = computed(() => sumOf(rows.value, "amount"));
const paid = computed(() => sumOf(rows.value.filter((r) => r.status === 4), "amount"));
const blank = () => ({ contractId: props.contractId, title: "", kind: 2, trigger: 1, triggerPercent: null, workOrderId: null, activityId: null, percentOfValue: null, amount: 0, plannedDate: null });
async function save() {
  busy.value = true;
  const [, error] = await saveMilestone(form.value);
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  form.value = null;
  load();
}
async function remove(r: any) {
  if (!confirm("مرحله حذف شود؟")) return;
  const [, error] = await deleteMilestone(r.id);
  if (error) return fail(error);
  load();
}
async function evaluate() {
  busy.value = true;
  const [d, error] = await evaluateMilestones(props.contractId);
  busy.value = false;
  if (error) return fail(error);
  ok(d.becameDue ? d.becameDue + " مرحله سررسید شد." : "مرحله جدیدی سررسید نشده است.");
  load();
}
function ask(m: any, action: string) {
  act.value = { m, action, value: "", id: null };
}
async function doAct() {
  const a = act.value!;
  busy.value = true;
  const [, error] = await milestoneAction({ id: a.m.id, action: a.action, reference: a.value || null, provisionalStatementId: a.id });
  busy.value = false;
  if (error) return fail(error);
  ok("ثبت شد.");
  act.value = null;
  load();
}
const actTitle: Record<string, string> = { "client-letter": "ثبت نامه کارفرما", statement: "صدور صورت‌وضعیت", invoiced: "ثبت فاکتور", paid: "ثبت پرداخت" };
onMounted(load);
watch(() => props.contractId, load);
</script>

<template>
  <div>
    <div class="d-flex flex-wrap gap-2 mb-2 d-print-none">
      <button v-if="canEdit" class="btn btn-sm btn-primary" @click="form = blank()">مرحله جدید</button>
      <button v-if="canAct" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="evaluate">بررسی سررسید مراحل</button>
    </div>
    <table class="table table-sm">
      <thead><tr><th>#</th><th>عنوان</th><th>نوع</th><th>محرک</th><th>مبلغ (ریال)</th><th>تاریخ برنامه</th><th>وضعیت</th><th>مراجع</th><th class="d-print-none"></th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td colspan="9" class="text-center inv-muted">مرحله پرداختی تعریف نشده است.</td></tr>
        <tr v-for="r in rows" :key="r.id">
          <td>{{ r.seq }}</td><td>{{ r.title }}</td><td>{{ milestoneKinds[r.kind] }}</td>
          <td>{{ milestoneTriggers[r.trigger] }}<span v-if="r.triggerPercent"> ({{ pct(r.triggerPercent, 0) }})</span></td>
          <td>{{ rial(r.amount) }}<span v-if="r.percentOfValue" class="inv-muted"> ({{ pct(r.percentOfValue) }})</span></td>
          <td>{{ jalali(r.plannedDate) }}</td>
          <td><span :class="r.status === 4 ? 'ok' : r.status === 1 ? 'bad' : r.status === 0 ? 'info' : 'warn'" class="inv-badge">{{ milestoneStatuses[r.status] }}</span><div v-if="r.dueAt" class="small inv-muted">سررسید: {{ jalali(r.dueAt) }}</div></td>
          <td class="small"><div v-if="r.clientLetterRef">نامه کارفرما: {{ r.clientLetterRef }}</div><div v-if="r.provisionalStatementId">صورت‌وضعیت: {{ r.provisionalStatementId }}</div><div v-if="r.financeRef">فاکتور: {{ r.financeRef }}</div><div v-if="r.paidAt">پرداخت: {{ jalali(r.paidAt) }}</div></td>
          <td class="d-print-none text-nowrap">
            <template v-if="canAct">
              <button v-if="r.trigger === 7 && r.status === 0" class="btn btn-sm btn-outline-primary me-1" @click="ask(r, 'client-letter')">نامه کارفرما</button>
              <button v-if="r.status === 1" class="btn btn-sm btn-outline-primary me-1" @click="ask(r, 'statement')">صورت‌وضعیت</button>
              <button v-if="r.status === 2" class="btn btn-sm btn-outline-primary me-1" @click="ask(r, 'invoiced')">فاکتور</button>
              <button v-if="r.status === 3" class="btn btn-sm btn-outline-success me-1" @click="ask(r, 'paid')">پرداخت شد</button>
            </template>
            <button v-if="r.status >= 1" class="btn btn-sm btn-outline-secondary me-1" @click="emit('payment-request', r)">چاپ درخواست پرداخت</button>
            <template v-if="canEdit && r.status === 0"><button class="btn btn-sm btn-outline-secondary me-1" @click="form = { ...r }">ویرایش</button><button class="btn btn-sm btn-outline-danger" @click="remove(r)">حذف</button></template>
          </td>
        </tr>
      </tbody>
      <tfoot v-if="rows.length"><tr><th colspan="4">جمع</th><th>{{ rial(total) }}</th><th colspan="4">پرداخت‌شده: {{ rial(paid) }}</th></tr></tfoot>
    </table>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h3 class="h6">{{ form.id ? "ویرایش" : "تعریف" }} مرحله پرداخت</h3>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-6"><label class="form-label small mb-0">نوع</label><select v-model.number="form.kind" class="form-select form-select-sm"><option v-for="(n, k) in milestoneKinds" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">محرک سررسید</label><select v-model.number="form.trigger" class="form-select form-select-sm"><option v-for="(n, k) in milestoneTriggers" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div v-if="form.trigger === 1" class="col-6"><label class="form-label small mb-0">تاریخ</label><JDate v-model="form.plannedDate" /></div>
          <div v-if="form.trigger === 2" class="col-6"><label class="form-label small mb-0">درصد پیشرفت محرک</label><input v-model.number="form.triggerPercent" class="form-control form-control-sm" type="number" min="1" max="100" /></div>
          <div v-if="[4, 5, 6].includes(form.trigger)" class="col-6"><label class="form-label small mb-0">دستور ساخت</label><WorkOrderOfContract v-model="form.workOrderId" :contract-id="contractId" /></div>
          <div v-if="form.trigger === 3" class="col-12"><label class="form-label small mb-0">فعالیت نقطه عطف</label><select v-model="form.activityId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="a in acts" :key="a.id" :value="a.id">{{ a.code }} {{ a.title }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">درصد از مبلغ قرارداد</label><input v-model.number="form.percentOfValue" class="form-control form-control-sm" type="number" min="0" max="100" step="0.5" /></div>
          <div class="col-6"><label class="form-label small mb-0">مبلغ (ریال)</label><input v-model.number="form.amount" :disabled="!!form.percentOfValue" class="form-control form-control-sm" type="number" min="0" /></div>
        </div>
        <p class="small inv-muted mt-2 mb-2">اگر درصد وارد شود مبلغ از مبلغ قرارداد در شرایط قرارداد محاسبه می‌شود.</p>
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="form = null">انصراف</button><button :disabled="busy || !form.title" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
    <div v-if="act" class="inv-modal-backdrop" @click.self="act = null">
      <div class="inv-modal">
        <h3 class="h6">{{ actTitle[act.action] }} — {{ act.m.title }}</h3>
        <template v-if="act.action === 'statement'"><label class="form-label small mb-0">شناسه صورت‌وضعیت موقت (اختیاری)</label><input v-model.number="act.id" class="form-control form-control-sm mb-3" type="number" /></template>
        <template v-else-if="act.action !== 'paid'"><label class="form-label small mb-0">{{ act.action === 'client-letter' ? "شماره نامه کارفرما" : "شماره فاکتور / مرجع مالی" }}</label><input v-model="act.value" class="form-control form-control-sm mb-3" /></template>
        <p v-else class="small">پرداخت با تاریخ امروز ثبت می‌شود.</p>
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="act = null">انصراف</button><button :disabled="busy || (act.action === 'client-letter' && !act.value)" class="btn btn-sm btn-primary" @click="doAct">ثبت</button></div>
      </div>
    </div>
  </div>
</template>
