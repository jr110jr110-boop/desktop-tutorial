<script setup lang="ts">
import { onMounted, ref } from "vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import {
  downloadSepidarExport, exportSepidarPeriod, getExportAccountMapping, getExportDetailMapping, getExportLayout, getExportPreview, getSepidarExports, getSepidarExportStatus,
  saveExportAccountMapping, saveExportDetailMapping, saveExportLayout,
} from "@/api/FnAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { money, yesNo } from "@/composables/fn";
import { exportStatuses, usePeriods } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Full voucher export for Sepidar (fallback path; the outbox of the F2 release stays the normal way): layout of the file, code mapping, preview, export of a period, list and download.
const tab = ref("export");
const tabs = [{ key: "export", title: "خروجی دوره" }, { key: "layout", title: "ستون‌های فایل" }, { key: "accounts", title: "نگاشت کد حساب" }, { key: "details", title: "نگاشت کد تفصیلی" }, { key: "list", title: "خروجی‌های گرفته‌شده" }];
const { periods, load: loadPeriods, label } = usePeriods();
const enabled = ref<boolean | null>(null);
const canWrite = hasRole(FnRoles.managers);
const busy = ref(false);

const periodId = ref<number | null>(null);
const preview = ref<any>(null);
const supersede = ref(false);
async function doPreview() {
  preview.value = null;
  if (!periodId.value) return;
  const [d, error] = await getExportPreview(periodId.value);
  if (error) return fail(error);
  preview.value = d;
}
async function doExport(v?: any) {
  supersede.value = false;
  busy.value = true;
  const [, error] = await exportSepidarPeriod(periodId.value!, v?.reason);
  busy.value = false;
  if (error) return fail(error);
  ok("خروجی ساخته شد؛ آن را از فهرست خروجی‌ها دریافت کنید.");
  doPreview();
  loadExports();
}
function startExport() {
  if (!periodId.value) return fail("دوره را انتخاب کنید.");
  if (preview.value?.alreadyExported) supersede.value = true;
  else doExport();
}

const layout = ref<any[]>([]);
async function loadLayout() {
  const [d, error] = await getExportLayout();
  if (error) return fail(error);
  layout.value = (d || []).map((c: any) => ({ ...c }));
}
async function saveLayout() {
  busy.value = true;
  const [d, error] = await saveExportLayout(layout.value.map((c, i) => ({ key: c.key, title: c.title, sortOrder: Number(c.sortOrder) || i + 1, isActive: c.isActive })));
  busy.value = false;
  if (error) return fail(error);
  ok("ستون‌های فایل ذخیره شد.");
  layout.value = (d || []).map((c: any) => ({ ...c }));
}
function move(i: number, d: number) {
  const j = i + d;
  if (j < 0 || j >= layout.value.length) return;
  const a = layout.value;
  [a[i], a[j]] = [a[j], a[i]];
  a.forEach((c, k) => (c.sortOrder = k + 1));
}

const onlyUnmapped = ref(false);
const accounts = ref<any[]>([]);
const details = ref<any[]>([]);
async function loadMappings() {
  const [a] = await getExportAccountMapping(onlyUnmapped.value);
  accounts.value = (a || []).map((x: any) => ({ ...x, edit: x.sepidarCode || "" }));
  const [d] = await getExportDetailMapping(onlyUnmapped.value);
  details.value = (d || []).map((x: any) => ({ ...x, edit: x.sepidarCode || "" }));
}
async function saveMap(kind: "account" | "detail", row: any) {
  if (!String(row.edit).trim()) return fail("کد سپیدار را وارد کنید.");
  busy.value = true;
  const [d, error] = await (kind === "account" ? saveExportAccountMapping : saveExportDetailMapping)(row.code, String(row.edit).trim());
  busy.value = false;
  if (error) return fail(error);
  ok("نگاشت ذخیره شد.");
  row.sepidarCode = d.sepidarCode;
  row.mapped = d.mapped;
}

const exportsList = ref<any[]>([]);
async function loadExports() {
  const [d, error] = await getSepidarExports(null);
  if (error) return fail(error);
  exportsList.value = d || [];
}
async function download(x: any) {
  const message = await downloadSepidarExport(x.id);
  if (message) fail(message);
  else ok("فایل ذخیره شد.");
}

onMounted(async () => {
  const [s] = await getSepidarExportStatus();
  enabled.value = s === true;
  loadPeriods();
  loadLayout();
  loadMappings();
  loadExports();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">خروجی کامل اسناد برای سپیدار</h1></div>
      <p v-if="enabled === false" class="alert alert-warning py-2">این مسیر خروجی در تنظیمات غیرفعال است؛ مسیر عادی «صندوق خروجی سپیدار» است.</p>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'export'">
        <div class="inv-filters mb-3">
          <select v-model="periodId" class="form-select form-select-sm" @change="doPreview"><option :value="null">دوره...</option><option v-for="p in periods" :key="p.id" :value="p.id">{{ label(p) }}</option></select>
          <button class="btn btn-sm btn-secondary" @click="doPreview">پیش‌نمایش</button>
          <button v-if="canWrite" :disabled="busy || !preview || !preview.enabled" class="btn btn-sm btn-primary" @click="startExport">ساخت فایل خروجی</button>
        </div>
        <div v-if="preview">
          <p>
            <span :class="preview.errors.length || preview.unmappedAccounts.length || preview.unmappedDetails.length ? 'bad' : 'ok'" class="inv-badge">{{ preview.errors.length || preview.unmappedAccounts.length || preview.unmappedDetails.length ? "نیازمند رفع مشکل" : "آماده خروجی" }}</span>
            اسناد: <b>{{ preview.vouchers }}</b> - ردیف‌ها: <b>{{ preview.rows }}</b> - جمع بدهکار: {{ money(preview.totalDebit) }} - جمع بستانکار: {{ money(preview.totalCredit) }} - قبلاً خروجی گرفته شده: {{ yesNo(preview.alreadyExported) }}
          </p>
          <div v-if="preview.unmappedAccounts.length" class="alert alert-danger py-2">حساب‌های بدون کد سپیدار: <span class="inv-ltr">{{ preview.unmappedAccounts.join("، ") }}</span></div>
          <div v-if="preview.unmappedDetails.length" class="alert alert-danger py-2">تفصیلی‌های بدون کد سپیدار: <span class="inv-ltr">{{ preview.unmappedDetails.join("، ") }}</span></div>
          <ul v-if="preview.errors.length" class="text-danger"><li v-for="(e, i) in preview.errors" :key="i">{{ e }}</li></ul>
        </div>
      </template>

      <template v-else-if="tab === 'layout'">
        <p class="inv-muted">ستون‌های فایل CSV و ترتیب آن‌ها. ستون غیرفعال در فایل نمی‌آید.</p>
        <table class="table table-sm">
          <thead><tr><th>ترتیب</th><th>کلید</th><th>عنوان ستون</th><th>فعال</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-for="(c, i) in layout" :key="c.key">
              <td>{{ i + 1 }}</td><td class="inv-ltr">{{ c.key }}</td>
              <td><input v-model="c.title" :disabled="!canWrite" class="form-control form-control-sm" /></td>
              <td><input v-model="c.isActive" :disabled="!canWrite" type="checkbox" /></td>
              <td v-if="canWrite" class="text-nowrap"><button class="btn btn-sm btn-outline-secondary me-1" @click="move(i, -1)">بالا</button><button class="btn btn-sm btn-outline-secondary" @click="move(i, 1)">پایین</button></td>
            </tr>
          </tbody>
        </table>
        <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-primary" @click="saveLayout">ذخیره ستون‌ها</button>
      </template>

      <template v-else-if="tab === 'accounts' || tab === 'details'">
        <div class="d-flex gap-3 align-items-center mb-2">
          <label class="form-check small"><input v-model="onlyUnmapped" class="form-check-input" type="checkbox" @change="loadMappings" /> فقط بدون نگاشت</label>
        </div>
        <div class="table-responsive" style="max-height: 520px; overflow: auto">
          <table class="table table-sm">
            <thead><tr><th>کد PMIS</th><th>عنوان</th><th>کد سپیدار</th><th>وضعیت</th><th v-if="canWrite"></th></tr></thead>
            <tbody>
              <tr v-for="r in tab === 'accounts' ? accounts : details" :key="r.code">
                <td class="inv-ltr">{{ r.code }}</td><td>{{ r.title }}</td>
                <td><input v-model="r.edit" :disabled="!canWrite" class="form-control form-control-sm inv-ltr" /></td>
                <td><span :class="r.mapped ? 'ok' : 'warn'" class="inv-badge">{{ r.mapped ? "نگاشت‌شده" : "بدون نگاشت" }}</span></td>
                <td v-if="canWrite"><button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="saveMap(tab === 'accounts' ? 'account' : 'detail', r)">ذخیره</button></td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>

      <template v-else>
        <table class="table table-sm">
          <thead><tr><th>شماره</th><th>دوره</th><th>وضعیت</th><th>اسناد</th><th>ردیف</th><th>جمع بدهکار</th><th>جمع بستانکار</th><th>تهیه‌کننده</th><th>زمان</th><th>علت جایگزینی</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!exportsList.length"><td colspan="11" class="text-center inv-muted">خروجی‌ای گرفته نشده است.</td></tr>
            <tr v-for="x in exportsList" :key="x.id">
              <td class="inv-ltr">{{ x.number }}</td><td>{{ x.periodTitle }}</td><td><span :class="x.status === 1 ? 'ok' : ''" class="inv-badge">{{ exportStatuses[x.status] }}</span></td>
              <td>{{ x.vouchers }}</td><td>{{ x.rows }}</td><td class="fn-num">{{ money(x.totalDebit) }}</td><td class="fn-num">{{ money(x.totalCredit) }}</td><td>{{ x.exportedByName }}</td><td>{{ jalali(x.exportedAt, true) }}</td><td>{{ x.supersedeReason }}</td>
              <td><button class="btn btn-sm btn-outline-secondary" @click="download(x)">دریافت فایل</button></td>
            </tr>
          </tbody>
        </table>
      </template>
    </div>

    <PromptModal v-if="supersede" :fields="[{ key: 'reason', label: 'علت جایگزینی خروجی قبلی', type: 'textarea', required: true }]" confirm-text="ساخت خروجی جدید" hint="برای این دوره قبلاً خروجی گرفته شده است؛ خروجی قبلی جایگزین می‌شود." title="خروجی دوباره" @cancel="supersede = false" @confirm="doExport" />
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
