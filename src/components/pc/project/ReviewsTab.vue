<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { getReviews, reviewAction, saveReview } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok } from "@/composables/inventory";
import { reviewDecisions, reviewKinds } from "@/composables/projectControl";
import { currentRole, hasRole, ProjectControlRoles } from "@/composables/roles";

// Contract review (ISO 9001 8.2.3, WI-IP-002): eight checks, the capability check by planning (special orders), the decision by the CEO.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const r = ref<any>(null);
const busy = ref(false);
const note = ref("");
const canEdit = hasRole(ProjectControlRoles.reviews);
const role = currentRole();

const checks: [string, string][] = [
  ["requirementsDefined", "الزامات مشتری تعریف شده است"], ["differencesResolved", "اختلاف با پیشنهاد رفع شده است"], ["capabilityChecked", "توانایی اجرا (تایید برنامه‌ریزی)"],
  ["deliveryFeasible", "تحویل در موعد ممکن است"], ["standardsItpAgreed", "استانداردها و ITP توافق شده است"], ["guaranteesReviewed", "ضمانت‌نامه‌ها بررسی شد"],
  ["penaltiesReviewed", "جرایم بررسی شد"], ["paymentTermsReviewed", "شرایط پرداخت بررسی شد"], ["currencyReviewed", "ارز و نرخ تبدیل بررسی شد"],
];

async function load() {
  const [data, error] = await getReviews({ ContractId: props.contractId, PageSize: 100 });
  if (error) return fail(error);
  rows.value = rowsOf(data);
}
const blank = () => ({ id: null, contractId: props.contractId, tenderCode: "", kind: 1, notes: "", decision: 0, requirementsDefined: false, differencesResolved: false, capabilityChecked: false, deliveryFeasible: false, standardsItpAgreed: false, guaranteesReviewed: false, penaltiesReviewed: false, paymentTermsReviewed: false, currencyReviewed: false });
const pending = () => r.value && r.value.decision === 0;

async function save() {
  busy.value = true;
  const [data, error] = await saveReview(r.value);
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  r.value = data;
  load();
}
async function act(action: string, decision = 0) {
  busy.value = true;
  const [data, error] = await reviewAction(r.value.id, action, decision, note.value);
  busy.value = false;
  if (error) return fail(error);
  ok("انجام شد.");
  r.value = data;
  load();
}
onMounted(load);
watch(() => props.contractId, () => { r.value = null; load(); });
</script>

<template>
  <div>
    <div class="d-flex justify-content-between mb-2">
      <b>بازنگری‌های قرارداد</b>
      <button v-if="canEdit" class="btn btn-sm btn-primary" @click="r = blank()">بازنگری جدید</button>
    </div>
    <table class="table table-sm table-hover">
      <thead><tr><th>نوع</th><th>کد مناقصه</th><th>تصمیم</th><th>تاریخ تصمیم</th><th>یادداشت</th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td colspan="5" class="text-center inv-muted">بازنگری ثبت نشده است.</td></tr>
        <tr v-for="x in rows" :key="x.id" role="button" @click="r = { ...x }">
          <td>{{ reviewKinds[x.kind] }}</td><td class="inv-ltr">{{ x.tenderCode }}</td>
          <td><span :class="x.decision === 1 ? 'ok' : x.decision === 0 ? 'warn' : 'bad'" class="inv-badge">{{ reviewDecisions[x.decision] }}</span></td>
          <td>{{ jalali(x.decidedAt) }}</td><td>{{ x.decisionNote || x.notes }}</td>
        </tr>
      </tbody>
    </table>
    <div v-if="r" class="border rounded p-3">
      <div class="row g-2">
        <div class="col-md-3"><label class="form-label small mb-0">نوع</label><select v-model.number="r.kind" :disabled="!pending() || !canEdit" class="form-select form-select-sm"><option v-for="(n, k) in reviewKinds" :key="k" :value="Number(k)">{{ n }}</option></select></div>
        <div class="col-md-3"><label class="form-label small mb-0">کد مناقصه</label><input v-model="r.tenderCode" :disabled="!pending() || !canEdit" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-12 d-flex flex-wrap gap-3">
          <label v-for="c in checks" :key="c[0]" class="form-check">
            <input v-model="r[c[0]]" :disabled="!pending() || !canEdit || c[0] === 'capabilityChecked'" class="form-check-input" type="checkbox" /> {{ c[1] }}
          </label>
        </div>
        <div class="col-12"><label class="form-label small mb-0">توضیحات</label><textarea v-model="r.notes" :disabled="!pending() || !canEdit" class="form-control form-control-sm" rows="2"></textarea></div>
      </div>
      <div class="d-flex flex-wrap gap-2 align-items-end mt-2">
        <button v-if="pending() && canEdit" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        <template v-if="r.id && pending()">
          <button v-if="role === 'PLANNING' || role === 'PMO'" :disabled="busy || r.capabilityChecked" class="btn btn-sm btn-outline-secondary" @click="act('capability-check')">تایید توانایی اجرا (برنامه‌ریزی)</button>
          <template v-if="role === 'CEO'">
            <input v-model="note" class="form-control form-control-sm" placeholder="دلیل / یادداشت تصمیم" style="max-width: 260px" />
            <button :disabled="busy" class="btn btn-sm btn-success" @click="act('decide', 1)">پذیرش سفارش</button>
            <button :disabled="busy" class="btn btn-sm btn-outline-warning" @click="act('decide', 3)">نیاز به اصلاح</button>
            <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('decide', 2)">رد</button>
          </template>
        </template>
        <button class="btn btn-sm btn-outline-secondary" @click="r = null">بستن</button>
      </div>
      <p class="inv-muted mt-2 mb-0">پذیرش فقط پس از تکمیل همه بررسی‌ها ممکن است؛ برای سفارش ویژه، تایید توانایی اجرا توسط برنامه‌ریزی هم لازم است. تصمیم با مدیرعامل است و پس از آن بازنگری تغییر نمی‌کند.</p>
    </div>
  </div>
</template>
