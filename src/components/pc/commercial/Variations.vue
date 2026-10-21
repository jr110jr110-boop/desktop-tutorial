<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { getChanges, getVariations, saveVariation, variationAction } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import JDate from "@/components/pc/JDate.vue";
import { fail, jalali, ok, rial, today } from "@/composables/inventory";
import { variationStatuses } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Variation orders (دستور تغییر): price and time change that the client has to approve in writing.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const changes = ref<any[]>([]);
const form = ref<any>(null);
const ap = ref<any>(null);
const busy = ref(false);
const can = hasRole(ProjectControlRoles.variations);

async function load() {
  const [[r, e], [c]] = await Promise.all([getVariations({ ContractId: props.contractId }), getChanges({ ContractId: props.contractId })]);
  if (e) return fail(e);
  rows.value = rowsOf(r);
  changes.value = rowsOf(c);
}
async function save() {
  busy.value = true;
  const [, e] = await saveVariation(form.value);
  busy.value = false;
  if (e) return fail(e);
  ok("ذخیره شد.");
  form.value = null;
  load();
}
async function act(r: any, action: string, extra: any = {}) {
  busy.value = true;
  const [, e] = await variationAction({ id: r.id, action, ...extra });
  busy.value = false;
  if (e) return fail(e);
  ok("انجام شد.");
  ap.value = null;
  load();
}
onMounted(load);
watch(() => props.contractId, load);
</script>

<template>
  <div>
    <div class="mb-2 d-print-none"><button v-if="can" class="btn btn-sm btn-primary" @click="form = { contractId, description: '', priceDelta: 0, daysDelta: 0, changeRequestId: null }">دستور تغییر جدید</button></div>
    <table class="table table-sm">
      <thead><tr><th>شماره</th><th>شرح</th><th>تغییر مبلغ (ریال)</th><th>تغییر زمان (روز)</th><th>وضعیت</th><th>تایید کارفرما</th><th class="d-print-none"></th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td colspan="7" class="text-center inv-muted">دستور تغییری ثبت نشده است.</td></tr>
        <tr v-for="r in rows" :key="r.id">
          <td class="inv-ltr">{{ r.number }}</td><td>{{ r.description }}</td><td>{{ rial(r.priceDelta) }}</td><td>{{ r.daysDelta }}</td>
          <td><span :class="r.status === 2 ? 'ok' : r.status === 3 ? 'bad' : r.status === 1 ? 'warn' : 'info'" class="inv-badge">{{ variationStatuses[r.status] }}</span></td>
          <td class="small"><span v-if="r.clientApprovalReference">{{ r.clientApprovalReference }} — {{ jalali(r.clientApprovalDate) }}</span></td>
          <td class="d-print-none text-nowrap">
            <template v-if="can">
              <template v-if="r.status === 0"><button class="btn btn-sm btn-outline-secondary me-1" @click="form = { ...r }">ویرایش</button><button class="btn btn-sm btn-outline-primary" @click="act(r, 'submit')">ارسال برای کارفرما</button></template>
              <template v-if="r.status === 1"><button class="btn btn-sm btn-outline-success me-1" @click="ap = { r, reference: '', date: today() }">تایید کارفرما</button><button class="btn btn-sm btn-outline-danger" @click="act(r, 'reject')">رد</button></template>
            </template>
          </td>
        </tr>
      </tbody>
    </table>
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h3 class="h6">دستور تغییر</h3>
        <label class="form-label small mb-0">شرح</label><textarea v-model="form.description" class="form-control form-control-sm mb-2" rows="2"></textarea>
        <div class="row g-2 mb-2">
          <div class="col-6"><label class="form-label small mb-0">تغییر مبلغ (ریال، منفی = کاهش)</label><input v-model.number="form.priceDelta" class="form-control form-control-sm" type="number" /></div>
          <div class="col-6"><label class="form-label small mb-0">تغییر زمان (روز)</label><input v-model.number="form.daysDelta" class="form-control form-control-sm" type="number" /></div>
        </div>
        <label class="form-label small mb-0">درخواست تغییر مرتبط</label>
        <select v-model="form.changeRequestId" class="form-select form-select-sm mb-3"><option :value="null">-</option><option v-for="c in changes" :key="c.id" :value="c.id">{{ c.number }} {{ c.title }}</option></select>
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="form = null">انصراف</button><button :disabled="busy || !form.description" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
    <div v-if="ap" class="inv-modal-backdrop" @click.self="ap = null">
      <div class="inv-modal">
        <h3 class="h6">تایید کارفرما — {{ ap.r.number }}</h3>
        <label class="form-label small mb-0">شماره نامه تایید</label><input v-model="ap.reference" class="form-control form-control-sm mb-2" />
        <label class="form-label small mb-0">تاریخ</label><JDate v-model="ap.date" class="mb-2" />
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="ap = null">انصراف</button><button :disabled="busy || !ap.reference" class="btn btn-sm btn-primary" @click="act(ap.r, 'approve', { reference: ap.reference, date: ap.date })">ثبت تایید</button></div>
      </div>
    </div>
  </div>
</template>
