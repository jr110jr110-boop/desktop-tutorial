<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { addGuaranteeEvent, getGuarantees, parseGuarantees, saveGuarantee } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import JDate from "@/components/pc/JDate.vue";
import { fail, jalali, ok, rial, today } from "@/composables/inventory";
import { guaranteeEventKinds, guaranteeIssuers, guaranteeReleases, guaranteeSources, guaranteeStatuses, guaranteeTypes } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Guarantees of the contract: the legacy free-text amount is read once («parse»); then type, issuer, validity, release condition and events (issue, extension, cancellation) are kept here.
const emit = defineEmits(["print"]);
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const status = ref<number | null>(null);
const onlyUnparsed = ref(false);
const form = ref<any>(null);
const ev = ref<any>(null);
const busy = ref(false);
const canEdit = hasRole(ProjectControlRoles.commercialFinance);

async function load() {
  const [d, e] = await getGuarantees({ ContractId: props.contractId, Status: status.value, OnlyUnparsed: onlyUnparsed.value || null });
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
async function parse() {
  busy.value = true;
  const [d, e] = await parseGuarantees();
  busy.value = false;
  if (e) return fail(e);
  ok(`ایجاد: ${d.created}، خوانده‌شده: ${d.parsed}، ناموفق: ${d.failed}`);
  load();
}
async function save() {
  busy.value = true;
  const [, e] = await saveGuarantee(form.value);
  busy.value = false;
  if (e) return fail(e);
  ok("ذخیره شد.");
  form.value = null;
  load();
}
async function saveEvent() {
  busy.value = true;
  const [, e] = await addGuaranteeEvent(ev.value);
  busy.value = false;
  if (e) return fail(e);
  ok("رویداد ثبت شد.");
  ev.value = null;
  load();
}
const level = (d: any) => (d == null ? "" : d < 0 ? "bad" : d <= 14 ? "bad" : d <= 30 ? "warn" : "");
const failed = computed(() => rows.value.filter((r) => r.parseFailed).length);
defineExpose({ rows });
onMounted(load);
watch(() => props.contractId, load);
</script>

<template>
  <div>
    <div class="inv-filters mb-2 d-print-none">
      <select v-model="status" class="form-select form-select-sm" @change="load"><option :value="null">همه وضعیت‌ها</option><option v-for="(n, k) in guaranteeStatuses" :key="k" :value="Number(k)">{{ n }}</option></select>
      <label class="form-check mb-1"><input v-model="onlyUnparsed" class="form-check-input" type="checkbox" @change="load" /> فقط مبلغ ناخوانا</label>
      <button v-if="canEdit" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="parse">خواندن ضمانت‌نامه‌های قدیمی</button>
      <button class="btn btn-sm btn-outline-secondary" @click="emit('print', rows)">چاپ فهرست ضمانت‌نامه‌ها</button>
    </div>
    <div v-if="failed" class="alert alert-warning py-2">مبلغ {{ failed }} ضمانت‌نامه از متن قدیمی خوانده نشد؛ مبلغ را دستی وارد کنید.</div>
    <table class="table table-sm">
      <thead><tr><th>ضمانت‌نامه</th><th>نوع</th><th>صادرکننده</th><th>مبلغ</th><th>اعتبار تا</th><th>آزادسازی</th><th>وضعیت</th><th class="d-print-none"></th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td colspan="8" class="text-center inv-muted">ضمانت‌نامه‌ای نیست.</td></tr>
        <template v-for="r in rows" :key="r.id">
          <tr>
            <td>{{ r.title }} <span class="inv-ltr">{{ r.no }}</span><div class="small inv-muted">{{ r.bank }} — {{ guaranteeSources[r.sourceKind] }}</div></td>
            <td>{{ guaranteeTypes[r.guaranteeType] }}</td><td>{{ guaranteeIssuers[r.issuerKind] }}</td>
            <td>{{ r.amountDecimal != null ? rial(r.amountDecimal) : "—" }}<div v-if="r.parseFailed" class="small text-danger">{{ r.parseNote }}</div></td>
            <td>{{ jalali(r.expiryDate) }} <span v-if="r.daysToExpiry != null" :class="level(r.daysToExpiry)" class="inv-badge">{{ r.daysToExpiry < 0 ? "منقضی" : r.daysToExpiry + " روز" }}</span></td>
            <td>{{ guaranteeReleases[r.releaseTrigger] }}</td>
            <td>{{ guaranteeStatuses[r.status] }}</td>
            <td class="d-print-none text-nowrap" v-if="canEdit">
              <button class="btn btn-sm btn-outline-secondary me-1" @click="form = { ...r }">ویرایش</button>
              <button class="btn btn-sm btn-outline-primary" @click="ev = { guaranteeId: r.id, kind: 2, eventDate: today(), newExpiryDate: null, fee: null, note: '' }">رویداد</button>
            </td>
          </tr>
          <tr v-if="r.events?.length"><td colspan="8" class="small inv-muted py-0">رویدادها: <span v-for="x in r.events" :key="x.id" class="me-3">{{ guaranteeEventKinds[x.kind] }} {{ jalali(x.eventDate) }}<span v-if="x.newExpiryDate"> (تا {{ jalali(x.newExpiryDate) }})</span><span v-if="x.fee"> کارمزد {{ rial(x.fee) }}</span></span></td></tr>
        </template>
      </tbody>
    </table>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h3 class="h6">اطلاعات ضمانت‌نامه — {{ form.title }}</h3>
        <div class="row g-2">
          <div class="col-6"><label class="form-label small mb-0">نوع</label><select v-model.number="form.guaranteeType" class="form-select form-select-sm"><option v-for="(n, k) in guaranteeTypes" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">صادرکننده</label><select v-model.number="form.issuerKind" class="form-select form-select-sm"><option v-for="(n, k) in guaranteeIssuers" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">مبلغ (ریال)</label><input v-model.number="form.amountDecimal" class="form-control form-control-sm" type="number" min="0" /></div>
          <div class="col-6"><label class="form-label small mb-0">وضعیت</label><select v-model.number="form.status" class="form-select form-select-sm"><option v-for="(n, k) in guaranteeStatuses" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">شروع اعتبار</label><JDate v-model="form.validFrom" /></div>
          <div class="col-6"><label class="form-label small mb-0">پایان اعتبار</label><JDate v-model="form.expiryDate" /></div>
          <div class="col-12"><label class="form-label small mb-0">شرط آزادسازی</label><select v-model.number="form.releaseTrigger" class="form-select form-select-sm"><option v-for="(n, k) in guaranteeReleases" :key="k" :value="Number(k)">{{ n }}</option></select></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
    <div v-if="ev" class="inv-modal-backdrop" @click.self="ev = null">
      <div class="inv-modal">
        <h3 class="h6">رویداد ضمانت‌نامه</h3>
        <div class="row g-2">
          <div class="col-6"><label class="form-label small mb-0">نوع</label><select v-model.number="ev.kind" class="form-select form-select-sm"><option v-for="(n, k) in guaranteeEventKinds" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">تاریخ</label><JDate v-model="ev.eventDate" /></div>
          <div v-if="ev.kind === 2" class="col-6"><label class="form-label small mb-0">تاریخ جدید انقضا</label><JDate v-model="ev.newExpiryDate" /></div>
          <div class="col-6"><label class="form-label small mb-0">کارمزد (ریال)</label><input v-model.number="ev.fee" class="form-control form-control-sm" type="number" min="0" /></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="ev.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-secondary" @click="ev = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveEvent">ثبت</button></div>
      </div>
    </div>
  </div>
</template>
