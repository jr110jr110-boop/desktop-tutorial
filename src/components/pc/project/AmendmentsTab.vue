<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { amendmentAction, getAmendments, saveAmendment } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import JDate from "@/components/pc/JDate.vue";
import { fail, jalali, ok, rial, today } from "@/composables/inventory";
import { amendmentOrigins, amendmentTypes } from "@/composables/projectControl";
import { currentRole, hasRole, ProjectControlRoles } from "@/composables/roles";

// Contract amendments (WI-IP-002): drafted by commercial, approved by the CEO, then the customer's approval letter; three copies are tracked.
// The list endpoint does not carry the copies, so they are shown after an action on the amendment.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const a = ref<any>(null);
const busy = ref(false);
const reference = ref("");
const date = ref<string | null>(today());
const canEdit = hasRole(ProjectControlRoles.commercial);
const role = currentRole();

async function load() {
  const [data, error] = await getAmendments({ ContractId: props.contractId, PageSize: 100 });
  if (error) return fail(error);
  rows.value = rowsOf(data);
}
const blank = () => ({ id: null, contractId: props.contractId, type: 3, origin: 1, description: "", priceDelta: null, daysDelta: null, copies: [] });
async function save() {
  busy.value = true;
  const [data, error] = await saveAmendment({ ...a.value, contractId: props.contractId });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  a.value = data;
  load();
}
async function act(action: string, extra: any = {}) {
  busy.value = true;
  const [data, error] = await amendmentAction({ id: a.value.id, action, ...extra });
  busy.value = false;
  if (error) return fail(error);
  ok("انجام شد.");
  a.value = data;
  load();
}
onMounted(load);
watch(() => props.contractId, () => { a.value = null; load(); });
</script>

<template>
  <div>
    <div class="d-flex justify-content-between mb-2">
      <b>الحاقیه‌های قرارداد</b>
      <button v-if="canEdit" class="btn btn-sm btn-primary" @click="a = blank()">الحاقیه جدید</button>
    </div>
    <table class="table table-sm table-hover">
      <thead><tr><th>شماره</th><th>نوع</th><th>منشأ</th><th>تغییر قیمت</th><th>تغییر زمان (روز)</th><th>تایید مدیرعامل</th><th>تایید مشتری</th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td colspan="7" class="text-center inv-muted">الحاقیه‌ای نیست.</td></tr>
        <tr v-for="x in rows" :key="x.id" role="button" @click="a = { ...x }">
          <td class="inv-ltr">{{ x.number }}</td><td>{{ amendmentTypes[x.type] }}</td><td>{{ amendmentOrigins[x.origin] }}</td><td>{{ rial(x.priceDelta) }}</td><td>{{ x.daysDelta }}</td>
          <td>{{ jalali(x.ceoApprovedAt) }}</td><td>{{ x.customerApprovalReference }} {{ jalali(x.customerApprovalDate) }}</td>
        </tr>
      </tbody>
    </table>
    <div v-if="a" class="border rounded p-3">
      <div class="row g-2">
        <div class="col-md-2"><label class="form-label small mb-0">نوع</label><select v-model.number="a.type" :disabled="!!a.ceoApprovedAt || !canEdit" class="form-select form-select-sm"><option v-for="(n, k) in amendmentTypes" :key="k" :value="Number(k)">{{ n }}</option></select></div>
        <div class="col-md-2"><label class="form-label small mb-0">منشأ</label><select v-model.number="a.origin" :disabled="!!a.ceoApprovedAt || !canEdit" class="form-select form-select-sm"><option v-for="(n, k) in amendmentOrigins" :key="k" :value="Number(k)">{{ n }}</option></select></div>
        <div class="col-md-3"><label class="form-label small mb-0">تغییر قیمت</label><input v-model.number="a.priceDelta" :disabled="!!a.ceoApprovedAt || !canEdit" class="form-control form-control-sm" type="number" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">تغییر زمان (روز)</label><input v-model.number="a.daysDelta" :disabled="!!a.ceoApprovedAt || !canEdit" class="form-control form-control-sm" type="number" /></div>
        <div class="col-12"><label class="form-label small mb-0">شرح *</label><textarea v-model="a.description" :disabled="!!a.ceoApprovedAt || !canEdit" class="form-control form-control-sm" rows="2"></textarea></div>
      </div>
      <div class="d-flex flex-wrap gap-2 align-items-end mt-2">
        <button v-if="canEdit && !a.ceoApprovedAt" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        <button v-if="a.id && !a.ceoApprovedAt && role === 'CEO'" :disabled="busy" class="btn btn-sm btn-success" @click="act('ceo-approve')">تایید مدیرعامل</button>
        <template v-if="a.id && a.ceoApprovedAt && !a.customerApprovalReference && canEdit">
          <input v-model="reference" class="form-control form-control-sm" placeholder="شماره نامه تایید مشتری" style="max-width: 220px" />
          <div style="width: 150px"><JDate v-model="date" /></div>
          <button :disabled="busy || !reference" class="btn btn-sm btn-success" @click="act('customer-approve', { reference, date })">ثبت تایید مشتری</button>
        </template>
        <button class="btn btn-sm btn-outline-secondary" @click="a = null">بستن</button>
      </div>
      <div v-if="a.copies && a.copies.length" class="mt-3">
        <b class="small">نسخه‌ها (۳ نسخه)</b>
        <div v-for="c in a.copies" :key="c.id" class="d-flex gap-3 align-items-center small mt-1">
          <span style="min-width: 110px">{{ c.recipient }}</span>
          <span v-if="c.sentAt" class="inv-badge ok">ارسال شد {{ jalali(c.sentAt) }}</span>
          <button v-else-if="canEdit && a.ceoApprovedAt" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="act('copy-sent', { copyId: c.id, date })">ارسال شد</button>
        </div>
      </div>
      <p class="inv-muted mt-2 mb-0">الحاقیه تاییدشده توسط مدیرعامل دیگر ویرایش نمی‌شود.</p>
    </div>
  </div>
</template>
