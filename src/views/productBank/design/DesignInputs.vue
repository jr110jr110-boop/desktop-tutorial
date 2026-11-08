<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import { copyDesignInputs, designInputAction, downloadDesignInputTemplate, exportDesignInputs, getDesignInputs, importDesignInputs, saveDesignInput } from "@/api/DesignControlAPIs";
import { getProducts } from "@/api/ProductBankAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { DesignRoles, inputKinds, inputStatusClass, inputStatuses } from "@/composables/designControl";

// Package A1: the register of design inputs of a product revision (ISO 9001 8.3.3). Every input has a source and an acceptance criterion (a standard its edition, a previous design its reference,
// a customer specification its document and revision). An input a review refers to is replaced, not edited. A conflict stops the design until a second person resolves it. Excel in and out.
const route = useRoute();
const rows = ref<any[]>([]);
const products = ref<any[]>([]);
const productId = ref<number | null>(route.query.productId ? Number(route.query.productId) : null);
const revision = ref<number>(route.query.revision ? Number(route.query.revision) : 0);
const showAll = ref(false);
const text = ref("");
const form = ref<any>(null);
const busy = ref(false);
const dialog = ref<null | { id: number; action: string; title: string }>(null);
const conflictDialog = ref<null | { id: number; withInputId: number | null; note: string }>(null);
const copyFrom = ref<number | null>(null);
const preview = ref<any>(null);
const fileInput = ref<HTMLInputElement | null>(null);
const pending = ref<File | null>(null);
const canWrite = hasRole(DesignRoles.inputWriters);
const canResolve = hasRole(DesignRoles.resolvers);
const live = computed(() => rows.value.filter((r) => r.status === 0 || r.status === 1));

async function load() {
  if (!productId.value) return;
  const [data, error] = await getDesignInputs({ ProductId: productId.value, ProductRevision: revision.value, IncludeInactive: showAll.value || null, Contains: text.value });
  if (error) return fail(error);
  rows.value = rowsOf(data);
}
function blank() {
  return { id: null, productId: productId.value, productRevision: revision.value, kind: 1, statement: "", source: "", acceptanceCriterion: "", standardEdition: "", previousDesignRef: "", refText: "", refRevisionText: "", failureConsequence: "", rowVersion: null, usedByReview: false };
}
const edit = (r: any) => (form.value = { ...r });
async function save() {
  busy.value = true;
  const [, error] = await saveDesignInput(form.value);
  busy.value = false;
  if (error) return fail(error);
  ok(form.value.usedByReview ? "ورودی جایگزین شد؛ نسخه قبلی در تاریخچه می‌ماند." : "ورودی ذخیره شد.");
  form.value = null;
  load();
}
async function act(note: string) {
  const d = dialog.value!;
  busy.value = true;
  const [, error] = await designInputAction({ id: d.id, action: d.action, note });
  busy.value = false;
  if (error) return fail(error);
  dialog.value = null;
  ok("انجام شد.");
  load();
}
async function markConflict() {
  const c = conflictDialog.value!;
  busy.value = true;
  const [, error] = await designInputAction({ id: c.id, action: "conflict", note: c.note, withInputId: c.withInputId });
  busy.value = false;
  if (error) return fail(error);
  conflictDialog.value = null;
  ok("تعارض ثبت شد؛ تا رفع تعارض توسط نفر دوم پیشرفت طراحی متوقف است.");
  load();
}
async function copy() {
  if (copyFrom.value === null || !productId.value) return;
  busy.value = true;
  const [data, error] = await copyDesignInputs(productId.value, copyFrom.value, revision.value);
  busy.value = false;
  if (error) return fail(error);
  ok(`${rowsOf(data).length} ورودی از بازنگری ${copyFrom.value} کپی شد.`);
  load();
}
async function template() {
  const error = await downloadDesignInputTemplate();
  if (error) fail(error);
}
async function exportFile() {
  if (!productId.value) return;
  const error = await exportDesignInputs(productId.value, revision.value);
  if (error) fail(error);
}
async function pickFile(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0];
  if (!file || !productId.value) return;
  pending.value = file;
  busy.value = true;
  const [data, error] = await importDesignInputs(productId.value, revision.value, true, file);
  busy.value = false;
  if (fileInput.value) fileInput.value.value = "";
  if (error) {
    pending.value = null;
    return fail(error);
  }
  preview.value = data;
}
async function commitImport() {
  if (!pending.value || !productId.value) return;
  busy.value = true;
  const [data, error] = await importDesignInputs(productId.value, revision.value, false, pending.value);
  busy.value = false;
  if (error) return fail(error);
  if (data.created === 0) {
    preview.value = data;
    return fail((data.warnings || []).join(" ") || "هیچ ورودی ساخته نشد.");
  }
  ok(`${data.created} ورودی از فایل ساخته شد.`);
  preview.value = null;
  pending.value = null;
  load();
}
onMounted(async () => {
  const [p] = await getProducts();
  products.value = p || [];
  load();
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="ورودی‌های طراحی" :subtitle="'بازنگری ' + String(revision).padStart(2, '0')" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">ورودی‌های طراحی (ISO 9001 بند 8.3.3)</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <button class="btn btn-sm btn-outline-secondary" @click="template">قالب Excel</button>
          <button :disabled="!productId" class="btn btn-sm btn-outline-secondary" @click="exportFile">خروجی Excel</button>
          <button v-if="canWrite" :disabled="!productId" class="btn btn-sm btn-outline-primary" @click="fileInput?.click()">ورود از Excel</button>
          <input ref="fileInput" type="file" accept=".xlsx" class="d-none" @change="pickFile" />
          <button v-if="canWrite" :disabled="!productId" class="btn btn-sm btn-primary" @click="form = blank()">+ ورودی جدید</button>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <select v-model="productId" class="form-select form-select-sm" @change="load">
          <option :value="null">محصول را انتخاب کنید</option>
          <option v-for="p in products" :key="p.id" :value="p.id">{{ p.code }} — {{ p.title }}</option>
        </select>
        <input v-model.number="revision" type="number" min="0" max="99" class="form-control form-control-sm" style="max-width: 110px" title="بازنگری محصول" @change="load" />
        <input v-model="text" class="form-control form-control-sm" placeholder="کد، الزام یا منبع" @keyup.enter="load" />
        <label class="small text-nowrap"><input v-model="showAll" type="checkbox" @change="load" /> جایگزین‌شده و باطل هم</label>
        <button class="btn btn-sm btn-outline-secondary" @click="load">نمایش</button>
      </div>
      <div v-if="canWrite && productId" class="d-flex gap-2 align-items-center mb-3 d-print-none">
        <span class="small">کپی ورودی‌ها از بازنگری:</span>
        <input v-model.number="copyFrom" type="number" min="0" max="99" class="form-control form-control-sm" style="max-width: 100px" />
        <button :disabled="busy || copyFrom === null" class="btn btn-sm btn-outline-secondary" @click="copy">کپی (به‌عنوان ورودی جدید)</button>
      </div>

      <!-- the preview of the Excel file: nothing is created while one row is wrong -->
      <div v-if="preview" class="border rounded p-3 mb-3 d-print-none">
        <p class="mb-1">پیش‌نمایش فایل: {{ preview.rowCount }} ردیف؛ {{ preview.validRows }} درست، {{ preview.errorRows }} دارای خطا.</p>
        <p v-if="preview.errorRows" class="text-danger small">با وجود حتی یک ردیف دارای خطا هیچ ورودی ساخته نمی‌شود؛ فایل را اصلاح کنید.</p>
        <table class="table table-sm">
          <thead><tr><th>ردیف</th><th>الزام</th><th>نتیجه</th></tr></thead>
          <tbody><tr v-for="r in preview.rows" :key="r.row" :class="{ 'table-danger': !r.ok }"><td>{{ r.row }}</td><td>{{ r.statement }}</td><td>{{ r.ok ? "درست" : r.message }}</td></tr></tbody>
        </table>
        <button :disabled="busy || preview.errorRows > 0" class="btn btn-sm btn-primary" @click="commitImport">ساخت ورودی‌ها</button>
        <button class="btn btn-sm btn-outline-secondary ms-2" @click="preview = null; pending = null">انصراف</button>
      </div>

      <p v-if="!productId" class="text-center inv-muted">محصول را انتخاب کنید.</p>
      <table v-else class="table table-sm table-hover">
        <thead><tr><th>کد</th><th>نوع</th><th>الزام</th><th>منبع</th><th>معیار پذیرش</th><th>مرجع</th><th>وضعیت</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="8" class="text-center inv-muted">ورودی ثبت نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id" :class="{ 'table-danger': r.status === 1, 'inv-muted': r.status > 1 }">
            <td class="inv-ltr">{{ r.code }}</td><td>{{ inputKinds[r.kind] }}</td><td>{{ r.statement }}</td><td>{{ r.source }}</td><td>{{ r.acceptanceCriterion }}</td>
            <td class="small">{{ r.standardEdition || r.previousDesignRef || [r.refText, r.refRevisionText].filter(Boolean).join(" / ") }}</td>
            <td>
              <span class="inv-badge" :class="inputStatusClass[r.status]">{{ inputStatuses[r.status] }}</span>
              <div v-if="r.status === 1" class="small">{{ r.conflictNote }}<span v-if="r.conflictWithCode"> (با {{ r.conflictWithCode }})</span> · ثبت: {{ r.conflictMarkedByName }}</div>
              <div v-if="r.conflictResolvedAt" class="small inv-muted">رفع تعارض: {{ r.conflictResolvedByName }} {{ jalali(r.conflictResolvedAt) }}</div>
              <div v-if="r.voidReason" class="small">{{ r.voidReason }}</div>
            </td>
            <td class="d-print-none text-nowrap">
              <template v-if="canWrite && (r.status === 0 || r.status === 1)">
                <button v-if="r.status === 0" class="btn btn-sm btn-outline-primary py-0" @click="edit(r)">{{ r.usedByReview ? "جایگزینی" : "ویرایش" }}</button>
                <button v-if="r.status === 0" class="btn btn-sm btn-outline-danger py-0 ms-1" @click="conflictDialog = { id: r.id, withInputId: null, note: '' }">تعارض</button>
                <button v-if="r.status === 1 && canResolve" class="btn btn-sm btn-outline-success py-0" @click="dialog = { id: r.id, action: 'resolve', title: 'رفع تعارض (نفر دوم)' }">رفع تعارض</button>
                <button v-if="r.status === 0" class="btn btn-sm btn-outline-secondary py-0 ms-1" @click="dialog = { id: r.id, action: 'void', title: 'ابطال ورودی' }">ابطال</button>
              </template>
            </td>
          </tr>
        </tbody>
      </table>
      <p v-if="productId" class="small inv-muted">{{ live.length }} ورودی معتبر.</p>
    </div>

    <div v-if="form" class="inv-modal-backdrop d-print-none" @click.self="form = null">
      <div class="inv-modal" style="max-width: 760px" role="dialog" aria-modal="true">
        <h2 class="inv-title mb-2">{{ form.id ? (form.usedByReview ? "جایگزینی ورودی (بازبینی به آن ارجاع دارد)" : "ویرایش ورودی") : "ورودی طراحی جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-5">
            <label class="form-label small mb-0">نوع *</label>
            <select v-model.number="form.kind" class="form-select form-select-sm" :disabled="!!form.id"><option v-for="(t, k) in inputKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
          </div>
          <div class="col-12"><label class="form-label small mb-0">متن الزام *</label><textarea v-model="form.statement" class="form-control form-control-sm" rows="2" maxlength="1000"></textarea></div>
          <div class="col-md-6"><label class="form-label small mb-0">منبع * (قرارداد، استاندارد، نیاز مشتری ...)</label><input v-model="form.source" class="form-control form-control-sm" maxlength="500" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">معیار پذیرش *</label><input v-model="form.acceptanceCriterion" class="form-control form-control-sm" maxlength="1000" /></div>
          <div v-if="form.kind === 4" class="col-md-6"><label class="form-label small mb-0">شماره و ویرایش استاندارد *</label><input v-model="form.standardEdition" class="form-control form-control-sm inv-ltr" maxlength="200" /></div>
          <div v-if="form.kind === 5" class="col-md-6"><label class="form-label small mb-0">طراحی مرجع *</label><input v-model="form.previousDesignRef" class="form-control form-control-sm" maxlength="300" /></div>
          <template v-if="form.kind === 6">
            <div class="col-md-6"><label class="form-label small mb-0">سند مشتری / قرارداد *</label><input v-model="form.refText" class="form-control form-control-sm" maxlength="500" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">بازنگری سند *</label><input v-model="form.refRevisionText" class="form-control form-control-sm inv-ltr" maxlength="100" /></div>
          </template>
          <div class="col-12"><label class="form-label small mb-0">پیامد خرابی (اختیاری)</label><input v-model="form.failureConsequence" class="form-control form-control-sm" maxlength="1000" /></div>
        </div>
        <div class="d-flex gap-2 mt-3">
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
        </div>
      </div>
    </div>

    <div v-if="conflictDialog" class="inv-modal-backdrop d-print-none" @click.self="conflictDialog = null">
      <div class="inv-modal" style="max-width: 560px" role="dialog" aria-modal="true">
        <h2 class="inv-title mb-2">ثبت تعارض بین ورودی‌ها</h2>
        <label class="form-label small mb-0">در تعارض با ورودی (اختیاری)</label>
        <select v-model="conflictDialog.withInputId" class="form-select form-select-sm mb-2">
          <option :value="null">-</option>
          <option v-for="r in live.filter((x) => x.id !== conflictDialog!.id)" :key="r.id" :value="r.id">{{ r.code }} — {{ r.statement.substring(0, 60) }}</option>
        </select>
        <label class="form-label small mb-0">شرح تعارض *</label>
        <textarea v-model="conflictDialog.note" class="form-control form-control-sm" rows="3" maxlength="1000"></textarea>
        <div class="d-flex gap-2 mt-3">
          <button :disabled="busy || !conflictDialog.note.trim()" class="btn btn-sm btn-danger" @click="markConflict">ثبت تعارض</button>
          <button class="btn btn-sm btn-outline-secondary" @click="conflictDialog = null">انصراف</button>
        </div>
      </div>
    </div>

    <ReasonDialog v-if="dialog" :title="dialog.title" :busy="busy" @confirm="act" @close="dialog = null" />
  </div>
</template>

<style lang="scss" src="../../inventory/inventory.scss"></style>
