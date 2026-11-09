<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import UserSelect from "@/components/quality/UserSelect.vue";
import { designPlanAction, designStageAction, getDesignPlan, saveDesignPlan } from "@/api/DesignControlAPIs";
import { getProducts } from "@/api/ProductBankAPIs";
import { fail, jalali, ok, printPage } from "@/composables/inventory";
import { inputKinds, planStatusClass, planStatuses, revisionText, stageKinds, stageStatusClass, stageStatuses } from "@/composables/designControl";

// Package A1: ONE design plan (ISO 9001 8.3.2): stages with a responsible role and person and planned dates, interfaces between groups, the validation waiver.
// Prepared by engineering; approved by the engineering manager or QA, never the preparer; the inputs are declared complete by a second person.
const route = useRoute();
const router = useRouter();
const plan = ref<any>(null);
const products = ref<any[]>([]);
const form = ref<any>(null);
const busy = ref(false);
const dialog = ref<null | { action: string; title: string; required: boolean }>(null);
const completeDialog = ref(false);
const naKinds = ref<number[]>([]);
const completeNote = ref("");
const isNew = computed(() => route.params.id === "new");

const defaultStages = [
  { kind: 1, role: "ENGINEERING" }, { kind: 2, role: "ENGINEER" }, { kind: 3, role: "ENGINEERING" }, { kind: 4, role: "QA" }, { kind: 5, role: "QA" }, { kind: 6, role: "ENGINEERING" },
];
const dateOnly = (v: string | null | undefined) => (v ? String(v).substring(0, 10) : null);

function editForm(p: any) {
  form.value = {
    id: p?.id ?? null, productId: p?.productId ?? null, productRevision: p?.productRevision ?? 0, title: p?.title ?? "", scope: p?.scope ?? "",
    validationRequired: p ? p.validationRequired : true, validationWaiverReason: p?.validationWaiverReason ?? "", rowVersion: p?.rowVersion ?? null,
    stages: (p?.stages?.length ? p.stages : defaultStages.map((d) => ({ kind: d.kind, responsibleRole: d.role, responsibleUserId: null, plannedStart: null, plannedEnd: null, note: "" }))).map((s: any) => ({
      kind: s.kind, responsibleRole: s.responsibleRole ?? "", responsibleUserId: s.responsibleUserId ?? null, plannedStart: dateOnly(s.plannedStart), plannedEnd: dateOnly(s.plannedEnd), note: s.note ?? "",
    })),
    interfaces: (p?.interfaces || []).map((i: any) => ({ fromGroup: i.fromGroup, toGroup: i.toGroup, subject: i.subject, responsibleRole: i.responsibleRole ?? "", responsibleUserId: i.responsibleUserId ?? null })),
  };
}
async function load() {
  if (isNew.value) {
    plan.value = null;
    editForm(null);
    return;
  }
  const [data, error] = await getDesignPlan(Number(route.params.id));
  if (error) return fail(error);
  plan.value = data;
  form.value = data.canEdit ? (editForm(data), form.value) : null;
}
async function save() {
  busy.value = true;
  const [data, error] = await saveDesignPlan(form.value);
  busy.value = false;
  if (error) return fail(error);
  ok("برنامه طراحی ذخیره شد (پیش‌نویس).");
  if (isNew.value) router.replace({ name: "DesignPlan", params: { id: data.id } });
  else {
    plan.value = data;
    editForm(data);
  }
}
async function act(action: string, note?: string | null) {
  busy.value = true;
  const [data, error] = await designPlanAction({ id: plan.value.id, action, note: note || null });
  busy.value = false;
  dialog.value = null;
  if (error) return fail(error);
  ok("انجام شد.");
  if (action === "new-version") return router.replace({ name: "DesignPlan", params: { id: data.id } });
  plan.value = data;
  form.value = data.canEdit ? (editForm(data), form.value) : null;
}
async function declareComplete() {
  busy.value = true;
  const [data, error] = await designPlanAction({ id: plan.value.id, action: "inputs-complete", note: completeNote.value, notApplicableKinds: naKinds.value });
  busy.value = false;
  if (error) return fail(error);
  completeDialog.value = false;
  ok("کامل بودن ورودی‌های طراحی اعلام شد.");
  plan.value = data;
}
async function stage(s: any, action: string) {
  busy.value = true;
  const [data, error] = await designStageAction({ planId: plan.value.id, stageId: s.id, action });
  busy.value = false;
  if (error) return fail(error);
  plan.value = data;
}
const toggleNa = (k: number) => (naKinds.value = naKinds.value.includes(k) ? naKinds.value.filter((x) => x !== k) : [...naKinds.value, k]);
const addInterface = () => form.value.interfaces.push({ fromGroup: "", toGroup: "", subject: "", responsibleRole: "", responsibleUserId: null });

onMounted(async () => {
  const [p] = await getProducts();
  products.value = p || [];
  load();
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader :title="plan ? 'برنامه طراحی ' + plan.number : 'برنامه طراحی جدید'" :subtitle="plan ? plan.productCode + ' - بازنگری ' + revisionText(plan.productRevision) : ''" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">{{ plan ? "برنامه طراحی " + plan.number : "برنامه طراحی جدید" }}</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <router-link v-if="plan" :to="{ name: 'DesignInputs', query: { productId: plan.productId, revision: plan.productRevision } }" class="btn btn-sm btn-outline-secondary">ورودی‌های طراحی</router-link>
          <router-link v-if="plan" :to="{ name: 'DesignDossier', params: { productId: plan.productId } }" class="btn btn-sm btn-outline-secondary">پرونده طراحی محصول</router-link>
        </div>
      </div>

      <div v-if="plan" class="mb-3">
        <p class="small mb-1">
          <span class="inv-badge" :class="planStatusClass[plan.status]">{{ planStatuses[plan.status] }}</span>
          محصول <b class="inv-ltr">{{ plan.productCode }}</b> {{ plan.productTitle }} · بازنگری <span class="inv-ltr">{{ revisionText(plan.productRevision) }}</span> · نسخه برنامه {{ plan.version }}
          · صحه‌گذاری: {{ plan.validationRequired ? "لازم" : "معاف" }}
        </p>
        <p class="small mb-1">تهیه‌کننده: {{ plan.preparedByName }} · ارسال: {{ jalali(plan.submittedAt) }} · تایید: {{ plan.approvedByName || "-" }} {{ jalali(plan.approvedAt) }}</p>
        <p v-if="plan.returnNote" class="alert alert-warning py-1 small">برگردانده شد: {{ plan.returnNote }}</p>
        <p v-if="plan.voidReason" class="alert alert-secondary py-1 small">باطل شد: {{ plan.voidReason }}</p>
        <p v-if="!plan.validationRequired" class="small">دلیل معافیت از صحه‌گذاری: {{ plan.validationWaiverReason }}</p>
        <div class="d-flex flex-wrap gap-2 d-print-none">
          <button v-if="plan.canSubmit" :disabled="busy" class="btn btn-sm btn-primary" @click="act('submit')">ارسال برای تایید</button>
          <button v-if="plan.canApprove" :disabled="busy" class="btn btn-sm btn-success" @click="act('approve')">تایید برنامه</button>
          <button v-if="plan.canApprove" :disabled="busy" class="btn btn-sm btn-outline-warning" @click="dialog = { action: 'return', title: 'برگرداندن برنامه', required: true }">برگرداندن</button>
          <button v-if="plan.canDeclareInputsComplete" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="completeDialog = true">اعلام کامل بودن ورودی‌ها</button>
          <button v-if="plan.canNewVersion" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="act('new-version')">نسخه جدید</button>
          <button v-if="plan.status === 0 || plan.status === 1" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="dialog = { action: 'void', title: 'ابطال برنامه', required: true }">ابطال</button>
        </div>
      </div>

      <!-- the editable draft -->
      <div v-if="form" class="border rounded p-3 mb-3 d-print-none">
        <div class="row g-2 mb-2">
          <div v-if="isNew" class="col-md-4">
            <label class="form-label small mb-0">محصول *</label>
            <select v-model="form.productId" class="form-select form-select-sm">
              <option :value="null">انتخاب کنید</option>
              <option v-for="p in products" :key="p.id" :value="p.id">{{ p.code }} — {{ p.title }}</option>
            </select>
          </div>
          <div v-if="isNew" class="col-md-2"><label class="form-label small mb-0">بازنگری محصول *</label><input v-model.number="form.productRevision" type="number" min="0" max="99" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" maxlength="300" /></div>
          <div class="col-12"><label class="form-label small mb-0">دامنه برنامه</label><textarea v-model="form.scope" class="form-control form-control-sm" rows="2" maxlength="2000"></textarea></div>
          <div class="col-md-3 d-flex align-items-end"><label class="small"><input v-model="form.validationRequired" type="checkbox" /> صحه‌گذاری طراحی (Validation) لازم است</label></div>
          <div v-if="!form.validationRequired" class="col-md-9"><label class="form-label small mb-0">دلیل معافیت * (QA تایید می‌کند)</label><input v-model="form.validationWaiverReason" class="form-control form-control-sm" maxlength="1000" /></div>
        </div>
        <table class="table table-sm">
          <thead><tr><th>مرحله</th><th>نقش مسئول</th><th>فرد مسئول</th><th>شروع برنامه‌ای</th><th>پایان برنامه‌ای</th></tr></thead>
          <tbody>
            <tr v-for="s in form.stages" :key="s.kind">
              <td>{{ stageKinds[s.kind] }}</td>
              <td><input v-model="s.responsibleRole" class="form-control form-control-sm inv-ltr" maxlength="40" /></td>
              <td><UserSelect v-model="s.responsibleUserId" /></td>
              <td><KsDatePicker v-model="s.plannedStart" :element-tag="'ps-' + s.kind" placeholder="تاریخ" simple-placeholder /></td>
              <td><KsDatePicker v-model="s.plannedEnd" :element-tag="'pe-' + s.kind" placeholder="تاریخ" simple-placeholder /></td>
            </tr>
          </tbody>
        </table>
        <h2 class="h6">ارتباط بین گروه‌ها (Interfaces)</h2>
        <table class="table table-sm">
          <thead><tr><th>از گروه</th><th>به گروه</th><th>موضوع</th><th>نقش</th><th>فرد مسئول</th><th></th></tr></thead>
          <tbody>
            <tr v-for="(i, idx) in form.interfaces" :key="idx">
              <td><input v-model="i.fromGroup" class="form-control form-control-sm" maxlength="100" /></td>
              <td><input v-model="i.toGroup" class="form-control form-control-sm" maxlength="100" /></td>
              <td><input v-model="i.subject" class="form-control form-control-sm" maxlength="500" /></td>
              <td><input v-model="i.responsibleRole" class="form-control form-control-sm inv-ltr" maxlength="40" /></td>
              <td><UserSelect v-model="i.responsibleUserId" /></td>
              <td><button class="btn btn-sm btn-outline-danger py-0" @click="form.interfaces.splice(idx, 1)">حذف ردیف</button></td>
            </tr>
          </tbody>
        </table>
        <div class="d-flex gap-2">
          <button class="btn btn-sm btn-outline-secondary" @click="addInterface">+ ارتباط</button>
          <button :disabled="busy || !form.productId" class="btn btn-sm btn-primary" @click="save">ذخیره پیش‌نویس</button>
        </div>
      </div>

      <!-- read-only stages with the stage actions -->
      <div v-if="plan">
        <h2 class="h6">مراحل طراحی</h2>
        <table class="table table-sm table-hover">
          <thead><tr><th>#</th><th>مرحله</th><th>نقش</th><th>مسئول</th><th>شروع برنامه‌ای</th><th>پایان برنامه‌ای</th><th>وضعیت</th><th>پایان واقعی</th><th class="d-print-none"></th></tr></thead>
          <tbody>
            <tr v-for="s in plan.stages" :key="s.id" :class="{ 'table-warning': s.late }">
              <td>{{ s.seq }}</td><td>{{ stageKinds[s.kind] }}</td><td class="inv-ltr">{{ s.responsibleRole }}</td><td>{{ s.responsibleName }}</td>
              <td>{{ jalali(s.plannedStart) }}</td><td>{{ jalali(s.plannedEnd) }}</td>
              <td><span class="inv-badge" :class="stageStatusClass[s.status]">{{ stageStatuses[s.status] }}</span><span v-if="s.late" class="inv-badge bad ms-1">دیرکرد</span></td>
              <td>{{ jalali(s.actualEnd) }}</td>
              <td class="d-print-none text-nowrap">
                <template v-if="plan.status === 2 && s.status < 2">
                  <button v-if="s.status === 0" :disabled="busy" class="btn btn-sm btn-outline-primary py-0" @click="stage(s, 'start')">شروع</button>
                  <button :disabled="busy" class="btn btn-sm btn-outline-success py-0 ms-1" @click="stage(s, 'complete')">پایان</button>
                  <button v-if="s.kind === 5 && !plan.validationRequired" :disabled="busy" class="btn btn-sm btn-outline-secondary py-0 ms-1" @click="stage(s, 'not-required')">لازم نیست</button>
                </template>
              </td>
            </tr>
          </tbody>
        </table>

        <h2 class="h6">ارتباط بین گروه‌ها</h2>
        <table class="table table-sm">
          <thead><tr><th>از گروه</th><th>به گروه</th><th>موضوع</th><th>مسئول</th></tr></thead>
          <tbody>
            <tr v-if="!plan.interfaces.length"><td colspan="4" class="text-center inv-muted">ارتباطی ثبت نشده است.</td></tr>
            <tr v-for="i in plan.interfaces" :key="i.id"><td>{{ i.fromGroup }}</td><td>{{ i.toGroup }}</td><td>{{ i.subject }}</td><td>{{ i.responsibleName || i.responsibleRole }}</td></tr>
          </tbody>
        </table>

        <h2 class="h6">ورودی‌های طراحی</h2>
        <p class="small">
          <span v-for="k in plan.kinds" :key="k.kind" class="me-3">{{ inputKinds[k.kind] }}: <b>{{ k.notApplicable ? "لازم نیست" : k.total }}</b><span v-if="k.conflicts" class="text-danger"> ({{ k.conflicts }} تعارض)</span></span>
        </p>
        <p v-if="plan.missingKinds.length" class="alert alert-warning py-1 small">ورودی ثبت نشده یا «لازم نیست» اعلام نشده: {{ plan.missingKinds.join("، ") }}</p>
        <p v-if="plan.conflictCount" class="alert alert-danger py-1 small">{{ plan.conflictCount }} ورودی در تعارض است؛ تا رفع تعارض (نفر دوم) پیشرفت طراحی متوقف است.</p>
        <p class="small">اعلام کامل بودن: <span v-if="plan.inputsCompleteAt">{{ plan.inputsCompleteByName }} در {{ jalali(plan.inputsCompleteAt, true) }} - {{ plan.inputsCompleteNote }}</span><span v-else class="text-danger">هنوز اعلام نشده است</span></p>

        <h2 class="h6">نسخه‌های برنامه</h2>
        <ul class="small">
          <li v-for="v in plan.versions" :key="v.id"><router-link :to="{ name: 'DesignPlan', params: { id: v.id } }">{{ v.number }}</router-link> — نسخه {{ v.version }} — {{ planStatuses[v.status] }}</li>
        </ul>
      </div>
    </div>

    <ReasonDialog v-if="dialog" :title="dialog.title" :required="dialog.required" :busy="busy" @confirm="(t: string) => act(dialog!.action, t)" @close="dialog = null" />

    <div v-if="completeDialog" class="inv-modal-backdrop d-print-none" @click.self="completeDialog = false">
      <div class="inv-modal" style="max-width: 640px" role="dialog" aria-modal="true">
        <h2 class="inv-title mb-2">اعلام کامل بودن ورودی‌های طراحی</h2>
        <p class="inv-muted small">نفر دوم (غیر از تهیه‌کننده برنامه) تایید می‌کند که منابع قرارداد، استانداردها، الزامات قانونی و تجربه طراحی‌های قبلی بررسی شده‌اند. انواعی که برای این محصول لازم نیستند را با دلیل علامت بزنید.</p>
        <div class="mb-2"><label v-for="(t, k) in inputKinds" :key="k" class="d-block small"><input type="checkbox" :checked="naKinds.includes(Number(k))" @change="toggleNa(Number(k))" /> {{ t }} لازم نیست</label></div>
        <label class="form-label small mb-0">شرح بررسی (چه منابعی بررسی شد؟) *</label>
        <textarea v-model="completeNote" class="form-control form-control-sm" rows="3" maxlength="1000"></textarea>
        <div class="d-flex gap-2 mt-3">
          <button :disabled="busy || !completeNote.trim()" class="btn btn-sm btn-primary" @click="declareComplete">اعلام</button>
          <button class="btn btn-sm btn-outline-secondary" @click="completeDialog = false">انصراف</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../../inventory/inventory.scss"></style>
