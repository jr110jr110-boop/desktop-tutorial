<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { computePenalties, decidePenalty, getPenalties } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok, rial } from "@/composables/inventory";
import { pct, penaltyStatuses, sumOf } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Delay penalties are computed from the contract terms (rate per week, grace days, cap); only the CEO can waive or negotiate one.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const d = ref<any>(null);
const busy = ref(false);
const canCompute = hasRole(ProjectControlRoles.penalties);
const isCeo = hasRole(ProjectControlRoles.ceo);

async function load() {
  const [r, e] = await getPenalties({ ContractId: props.contractId });
  if (e) return fail(e);
  rows.value = rowsOf(r);
}
async function compute() {
  busy.value = true;
  const [, e] = await computePenalties(props.contractId);
  busy.value = false;
  if (e) return fail(e);
  ok("جریمه‌ها محاسبه شد.");
  load();
}
async function decide() {
  busy.value = true;
  const [, e] = await decidePenalty(d.value.id, d.value.status, d.value.reason, d.value.status === 2 ? d.value.amount : null);
  busy.value = false;
  if (e) return fail(e);
  ok("تصمیم ثبت شد.");
  d.value = null;
  load();
}
onMounted(load);
watch(() => props.contractId, load);
</script>

<template>
  <div>
    <div class="mb-2 d-print-none"><button v-if="canCompute" :disabled="busy" class="btn btn-sm btn-primary" @click="compute">محاسبه جریمه تاخیر</button></div>
    <table class="table table-sm">
      <thead><tr><th>موضوع</th><th>سررسید</th><th>تحویل واقعی</th><th>روز تاخیر</th><th>مهلت (روز)</th><th>نرخ هفتگی</th><th>سقف</th><th>مبنا</th><th>جریمه</th><th>وضعیت</th><th class="d-print-none"></th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td colspan="11" class="text-center inv-muted">جریمه‌ای محاسبه نشده است.</td></tr>
        <tr v-for="r in rows" :key="r.id">
          <td>{{ r.subject }}</td><td>{{ jalali(r.dueDate) }}</td><td>{{ jalali(r.actualDate) }}</td><td>{{ r.lateDays }}</td><td>{{ r.graceDays }}</td><td>{{ pct(r.ratePercentPerWeek) }}</td><td>{{ pct(r.capPercent) }}</td><td>{{ rial(r.baseAmount) }}</td><td>{{ rial(r.amount) }}</td>
          <td>{{ penaltyStatuses[r.status] }}<div v-if="r.decisionReason" class="small inv-muted">{{ r.decisionReason }}</div></td>
          <td class="d-print-none"><button v-if="isCeo && r.status === 0" class="btn btn-sm btn-outline-primary" @click="d = { id: r.id, status: 1, reason: '', amount: r.amount }">تصمیم مدیرعامل</button></td>
        </tr>
      </tbody>
      <tfoot v-if="rows.length"><tr><th colspan="8">جمع جریمه‌های محاسبه‌شده</th><th colspan="3">{{ rial(sumOf(rows.filter((x) => x.status === 0), "amount")) }}</th></tr></tfoot>
    </table>
    <div v-if="d" class="inv-modal-backdrop" @click.self="d = null">
      <div class="inv-modal">
        <h3 class="h6">بخشودگی یا مصالحه جریمه</h3>
        <select v-model.number="d.status" class="form-select form-select-sm mb-2"><option :value="1">بخشودگی کامل</option><option :value="2">مصالحه با مبلغ جدید</option></select>
        <input v-if="d.status === 2" v-model.number="d.amount" class="form-control form-control-sm mb-2" type="number" min="0" placeholder="مبلغ مصالحه (ریال)" />
        <textarea v-model="d.reason" class="form-control form-control-sm mb-3" rows="2" placeholder="دلیل (لازم)"></textarea>
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="d = null">انصراف</button><button :disabled="busy || !d.reason" class="btn btn-sm btn-primary" @click="decide">ثبت تصمیم</button></div>
      </div>
    </div>
  </div>
</template>
