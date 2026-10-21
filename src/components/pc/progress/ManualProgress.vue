<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { decideManualProgress, getActivities, getManualProgress, proposeManualProgress } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok } from "@/composables/inventory";
import { manualMethods, manualStates, pct } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Manual progress (manual, contractor and quantity activities): a unit manager proposes a percent, planning approves or rejects it
// (a different person than the proposer). Activities measured from system events cannot be proposed here.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const acts = ref<any[]>([]);
const state = ref<number | null>(0);
const form = ref<any>({ activityId: null, percent: 0, comment: "" });
const busy = ref(false);
const note = ref<Record<number, string>>({});
const canPropose = hasRole(ProjectControlRoles.unitManagers);
const canDecide = hasRole(ProjectControlRoles.planning);

async function load() {
  const [[d, e], [a]] = await Promise.all([getManualProgress({ ContractId: props.contractId, State: state.value }), getActivities({ ContractId: props.contractId })]);
  if (e) return fail(e);
  rows.value = rowsOf(d);
  acts.value = rowsOf(a).filter((x: any) => manualMethods.includes(x.progressMethod) && x.status !== 2);
}
async function propose() {
  busy.value = true;
  const [, error] = await proposeManualProgress(form.value.activityId, form.value.percent, form.value.comment);
  busy.value = false;
  if (error) return fail(error);
  ok("پیشنهاد ثبت شد.");
  form.value = { activityId: null, percent: 0, comment: "" };
  load();
}
async function decide(r: any, approve: boolean) {
  busy.value = true;
  const [, error] = await decideManualProgress(r.id, approve, note.value[r.id]);
  busy.value = false;
  if (error) return fail(error);
  ok(approve ? "تایید شد." : "رد شد.");
  load();
}
const current = (id: number) => acts.value.find((a) => a.id === id)?.percentCache;
onMounted(load);
watch(() => props.contractId, load);
</script>

<template>
  <div>
    <div v-if="canPropose" class="border rounded p-3 mb-3 d-print-none">
      <b class="small">پیشنهاد پیشرفت جدید</b>
      <div class="row g-2 align-items-end mt-1">
        <div class="col-md-4"><label class="form-label small mb-0">فعالیت</label>
          <select v-model="form.activityId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="a in acts" :key="a.id" :value="a.id">{{ a.code }} {{ a.title }} ({{ pct(a.percentCache, 0) }})</option></select>
        </div>
        <div class="col-md-2"><label class="form-label small mb-0">درصد پیشرفت</label><input v-model.number="form.percent" class="form-control form-control-sm" type="number" min="0" max="100" step="0.5" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">توضیح</label><input v-model="form.comment" class="form-control form-control-sm" /></div>
        <div class="col-md-2"><button :disabled="busy || !form.activityId" class="btn btn-sm btn-primary" @click="propose">ثبت پیشنهاد</button></div>
      </div>
    </div>
    <div class="inv-filters mb-2 d-print-none">
      <select v-model="state" class="form-select form-select-sm" @change="load"><option :value="null">همه</option><option v-for="(n, k) in manualStates" :key="k" :value="Number(k)">{{ n }}</option></select>
    </div>
    <table class="table table-sm">
      <thead><tr><th>فعالیت</th><th>درصد پیشنهادی</th><th>پیشنهاددهنده</th><th>تاریخ</th><th>توضیح</th><th>وضعیت</th><th class="d-print-none"></th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td colspan="7" class="text-center inv-muted">پیشنهادی نیست.</td></tr>
        <tr v-for="r in rows" :key="r.id">
          <td><span class="inv-ltr">{{ r.activityCode }}</span> {{ r.activityTitle }}</td>
          <td>{{ pct(r.percent) }} <span v-if="current(r.activityId) !== undefined && r.state === 0" class="inv-muted">(فعلی {{ pct(current(r.activityId), 0) }})</span></td>
          <td>{{ r.proposedByName }}</td><td>{{ jalali(r.proposedAt) }}</td><td>{{ r.comment }}</td>
          <td><span :class="r.state === 1 ? 'ok' : r.state === 2 ? 'bad' : 'warn'" class="inv-badge">{{ manualStates[r.state] }}</span><div v-if="r.decidedAt" class="small inv-muted">{{ r.decidedByName }} — {{ r.decisionNote }}</div></td>
          <td class="d-print-none" style="min-width: 260px">
            <div v-if="r.state === 0 && canDecide" class="d-flex gap-1">
              <input v-model="note[r.id]" class="form-control form-control-sm" placeholder="یادداشت (برای رد لازم)" />
              <button :disabled="busy" class="btn btn-sm btn-success" @click="decide(r, true)">تایید</button>
              <button :disabled="busy || !note[r.id]" class="btn btn-sm btn-outline-danger" @click="decide(r, false)">رد</button>
            </div>
          </td>
        </tr>
      </tbody>
    </table>
    <p class="inv-muted mb-0">برای هر فعالیت همزمان فقط یک پیشنهاد در انتظار می‌تواند باشد. تایید توسط خود پیشنهاددهنده ممکن نیست (تفکیک وظایف). پیشرفت ۱۰۰ درصد تایید‌شده، فعالیت را پایان‌یافته می‌کند.</p>
  </div>
</template>
