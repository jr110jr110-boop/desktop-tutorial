<script setup lang="ts">
import { nextTick } from "vue";
import { printForm } from "@/composables/quality";
import CertificatePrint from "@/components/quality/print/CertificatePrint.vue";
import { onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import QPager from "@/components/quality/QPager.vue";
import DocAttach from "@/components/quality/DocAttach.vue";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import { downloadCertificateTemplate, getCertificates, getCertificateSheet, getRequiredCertificate, importCertificateValues, saveCertificate, verifyCertificate } from "@/api/QualityAPIs";
import CertificateSheetPrint from "@/components/quality/print/CertificateSheetPrint.vue";
import { getLots } from "@/api/InventoryAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok, printPage } from "@/composables/inventory";
import { certificateResults, certificateTypes, certRowStatuses, certValueKinds, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Material certificates (EN 10204 2.2 / 3.1 / 3.2) per lot. The warehouse may attach them; only QC verifies.
const filters = ref({ Result: null as number | null, Contains: "" });
const list = usePaged(getCertificates, filters);
const canWrite = hasRole(QualityRoles.certificateWriters);
const canVerify = hasRole(QualityRoles.inspectors);
const form = ref<any>(null);
const item = ref<any>(null);
const lots = ref<any[]>([]);
const verifyNote = ref<Record<number, string>>({});
const busy = ref(false);
const printCert = ref<any>(null);
async function doPrint(c: any) {
  printCert.value = c;
  await nextTick();
  printForm();
}

// Package C: the structured certificate. The server judges the heat number, the chemistry and the mechanical values against the approved material specification; a 3.x certificate
// that is not judged «قبول» is not accepted. The type of certificate a coded item needs is derived from its specification and BOM line (shown beside the type).
const blank = () => ({
  id: null, lotId: null, type: 2, number: "", issuer: "", documentId: null, summary: "",
  certHeatNumber: "", grade: "", standard: "", inspectorOrganisation: "", inspectorName: "", inspectorStampNo: "", manufacturerSignatory: "",
  values: [] as any[],
});
const required = ref<any>(null);
const sheet = ref<any>(null);
const importFile = ref<File | null>(null);
const importResult = ref<any>(null);
const addValue = (kind: number) => form.value.values.push({ kind, name: "", unit: "", value: null });
async function loadRequired() {
  required.value = null;
  if (!item.value) return;
  const [data] = await getRequiredCertificate(item.value.id);
  required.value = data || null;
}
async function openSheet(c: any) {
  const [data, error] = await getCertificateSheet(c.id);
  if (error) return fail(error);
  sheet.value = data;
  await nextTick();
  printForm();
}
async function pickImport(event: Event) {
  const input = event.target as HTMLInputElement;
  importFile.value = (input.files && input.files[0]) || null;
  importResult.value = null;
}
async function doImport(preview: boolean) {
  if (!form.value?.id) return fail("ابتدا گواهی را ذخیره کنید؛ سپس مقادیر را از Excel وارد کنید.");
  if (!importFile.value) return fail("فایل Excel را انتخاب کنید.");
  busy.value = true;
  const [data, error] = await importCertificateValues(form.value.id, preview, importFile.value);
  busy.value = false;
  if (error) return fail(error);
  importResult.value = data;
  if (!preview) {
    ok("مقادیر گواهی وارد و توسط سرور قضاوت شد.");
    list.load();
  }
}
const evalClass = (s: number) => ({ ok: s === 1, bad: s === 2, warn: s === 0 });

async function loadLots() {
  lots.value = [];
  if (!item.value) return;
  const [data] = await getLots({ ItemId: item.value.id, PageSize: 200 });
  lots.value = rowsOf(data);
}

function edit(c: any) {
  form.value = {
    id: c.id, lotId: c.lotId, type: c.type, number: c.number, issuer: c.issuer, documentId: c.documentId, summary: c.summary,
    certHeatNumber: c.certHeatNumber || "", grade: c.grade || "", standard: c.standard || "", inspectorOrganisation: c.inspectorOrganisation || "", inspectorName: c.inspectorName || "",
    inspectorStampNo: c.inspectorStampNo || "", manufacturerSignatory: c.manufacturerSignatory || "",
    values: (c.values || []).map((v: any) => ({ kind: v.kind, name: v.name, unit: v.unit, value: v.value })),
  };
  importResult.value = null;
  item.value = { id: c.itemId, code: c.itemCode, title: c.itemTitle };
  loadRequired();
  lots.value = [{ id: c.lotId, lotNumber: c.lotNumber }];
}

async function save() {
  busy.value = true;
  const f = form.value;
  const [, error] = await saveCertificate({ ...f, values: f.values.filter((v: any) => v.name && v.value !== null && v.value !== "").map((v: any) => ({ ...v, value: Number(v.value) })) });
  busy.value = false;
  if (error) return fail(error);
  ok("گواهی ثبت شد.");
  form.value = null;
  list.load();
}

async function verify(c: any, accept: boolean) {
  busy.value = true;
  const [, error] = await verifyCertificate(c.id, accept, verifyNote.value[c.id]);
  busy.value = false;
  if (error) return fail(error);
  ok(accept ? "گواهی پذیرفته شد." : "گواهی رد شد.");
  list.load();
}

onMounted(list.load);
</script>

<template>
  <div class="inv-page ks-report">
    <CertificatePrint v-if="printCert" :cert="printCert" />
    <CertificateSheetPrint v-if="sheet" :sheet="sheet" />
    <KsReportHeader title="گواهی‌های مواد (EN 10204)" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">گواهی‌های مواد (EN 10204)</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <router-link :to="{ name: 'QualityTraceability' }" class="btn btn-sm btn-outline-secondary">ردیابی سریال / بچ</router-link>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="(form = blank()), (item = null), (lots = [])">گواهی جدید</button>
        </div>
      </div>

      <div v-if="form" class="border rounded p-2 mb-3 d-print-none">
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">کالا *</label><ItemPicker v-model="item" :active-only="false" @select="(loadLots(), loadRequired())" />
            <div v-if="required" class="small mt-1" :class="required.type ? '' : 'inv-muted'">نوع گواهی لازم این کالا: <b>{{ required.typeText || "تعیین نشده" }}</b><span v-if="required.sources?.length" class="inv-muted"> ({{ required.sources.join("، ") }})</span></div></div>
          <div class="col-md-2"><label class="form-label small mb-0">بچ / ذوب *</label><select v-model="form.lotId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="l in lots" :key="l.id" :value="l.id">{{ l.lotNumber }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">نوع گواهی</label><select v-model="form.type" class="form-select form-select-sm"><option v-for="(t, k) in certificateTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">شماره گواهی *</label><input v-model="form.number" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">صادرکننده</label><input v-model="form.issuer" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">فایل گواهی</label><DocAttach v-model="form.documentId" title="گواهی مواد" /></div>
          <div class="col-md-8"><label class="form-label small mb-0">خلاصه شیمیایی / مکانیکی</label><input v-model="form.summary" class="form-control form-control-sm" /></div>
          <template v-if="form.type !== 1">
            <div class="col-12"><hr class="my-1" /><b class="small">مشخصات ساختاریافته گواهی (قضاوت با سرور)</b></div>
            <div class="col-md-3"><label class="form-label small mb-0">شماره ذوب روی گواهی *</label><input v-model="form.certHeatNumber" class="form-control form-control-sm inv-ltr" /><div class="inv-muted small">باید با شماره ذوب بچ برابر باشد.</div></div>
            <div class="col-md-3"><label class="form-label small mb-0">گرید ماده *</label><input v-model="form.grade" class="form-control form-control-sm inv-ltr" placeholder="S355J2" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">استاندارد</label><input v-model="form.standard" class="form-control form-control-sm inv-ltr" placeholder="EN 10025-2" /></div>
            <div v-if="form.type === 2" class="col-md-3"><label class="form-label small mb-0">امضاکننده مجاز سازنده *</label><input v-model="form.manufacturerSignatory" class="form-control form-control-sm" /></div>
            <template v-if="form.type === 3">
              <div class="col-md-3"><label class="form-label small mb-0">سازمان بازرس *</label><input v-model="form.inspectorOrganisation" class="form-control form-control-sm" /></div>
              <div class="col-md-3"><label class="form-label small mb-0">نام بازرس</label><input v-model="form.inspectorName" class="form-control form-control-sm" /></div>
              <div class="col-md-3"><label class="form-label small mb-0">شماره مهر بازرس</label><input v-model="form.inspectorStampNo" class="form-control form-control-sm inv-ltr" /></div>
            </template>
            <div class="col-12">
              <table class="table table-sm mb-1">
                <thead><tr><th>نوع</th><th>پارامتر (C, Mn, Yield ...)</th><th>واحد</th><th>مقدار</th><th></th></tr></thead>
                <tbody>
                  <tr v-if="!form.values.length"><td colspan="5" class="inv-muted text-center">مقداری ثبت نشده است؛ مقدار الزامی مشخصات ماده بدون ثبت، پذیرش را مسدود می‌کند.</td></tr>
                  <tr v-for="(v, i) in form.values" :key="i">
                    <td><select v-model="v.kind" class="form-select form-select-sm"><option v-for="(t, k) in certValueKinds" :key="k" :value="Number(k)">{{ t }}</option></select></td>
                    <td><input v-model="v.name" class="form-control form-control-sm inv-ltr" /></td><td><input v-model="v.unit" class="form-control form-control-sm inv-ltr" style="max-width: 90px" /></td>
                    <td><input v-model="v.value" class="form-control form-control-sm inv-ltr" type="number" step="any" style="max-width: 140px" /></td>
                    <td><button class="btn btn-sm btn-outline-danger" @click="form.values.splice(i, 1)">حذف</button></td>
                  </tr>
                </tbody>
              </table>
              <div class="d-flex flex-wrap gap-2 align-items-center">
                <button class="btn btn-sm btn-outline-secondary" @click="addValue(1)">ردیف شیمیایی</button>
                <button class="btn btn-sm btn-outline-secondary" @click="addValue(2)">ردیف مکانیکی</button>
                <button class="btn btn-sm btn-link" @click="downloadCertificateTemplate()">دریافت الگوی Excel</button>
                <template v-if="form.id">
                  <input class="form-control form-control-sm" style="max-width: 240px" type="file" accept=".xlsx" @change="pickImport" />
                  <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="doImport(true)">پیش‌نمایش</button>
                  <button :disabled="busy" class="btn btn-sm btn-primary" @click="doImport(false)">وارد کردن مقادیر</button>
                </template>
                <span v-else class="inv-muted small">برای ورود از Excel، ابتدا گواهی را ذخیره کنید.</span>
              </div>
              <div v-if="importResult" class="small mt-2 border rounded p-2">
                <div>ردیف‌ها: {{ importResult.rowCount }}، معتبر: {{ importResult.validRows }}، دارای خطا: {{ importResult.errorRows }} <span v-if="!importResult.preview" class="ok inv-badge">نوشته شد</span><span v-else class="warn inv-badge">پیش‌نمایش (چیزی نوشته نشده)</span></div>
                <div v-for="(w, i) in importResult.warnings || []" :key="'w' + i" class="text-warning">{{ w }}</div>
                <div v-for="r in importResult.rows || []" :key="r.row" :class="r.ok ? '' : 'text-danger'">ردیف {{ r.row }}: <span class="inv-ltr">{{ r.name }}</span> {{ r.message }}</div>
              </div>
            </div>
          </template>
          <div class="col-md-12 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ثبت</button><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button></div>
        </div>
      </div>

      <div class="inv-filters mb-3 d-print-none">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره گواهی، بچ یا کالا" @keydown.enter="list.search()" />
        <select v-model="filters.Result" class="form-select form-select-sm" @change="list.search()"><option :value="null">همه</option><option v-for="(t, k) in certificateResults" :key="k" :value="Number(k)">{{ t }}</option></select>
        <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>کالا</th><th>بچ</th><th>ذوب</th><th>نوع</th><th>شماره</th><th>صادرکننده</th><th>ثبت‌کننده</th><th>وضعیت</th><th>قضاوت سرور</th><th class="d-print-none">تایید QC</th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="10" class="text-center inv-muted">گواهی‌ای نیست.</td></tr>
            <tr v-for="c in list.rows.value" :key="c.id">
              <td><span class="inv-ltr">{{ c.itemCode }}</span> {{ c.itemTitle }}</td><td class="inv-ltr">{{ c.lotNumber }}</td><td class="inv-ltr">{{ c.heatNumber }}</td>
              <td>{{ certificateTypes[c.type] }}</td><td class="inv-ltr">{{ c.number }} <button class="btn btn-sm btn-link p-0 d-print-none" @click="doPrint(c)">چاپ</button></td><td>{{ c.issuer }}</td><td>{{ c.creatorName }}</td>
              <td>
                <span :class="{ ok: c.result === 1, bad: c.result === 2, warn: c.result === 0 }" class="inv-badge">{{ certificateResults[c.result] }}</span>
                <div v-if="c.verifiedByName" class="small">{{ c.verifiedByName }} {{ jalali(c.verifiedAt) }}</div>
              </td>
              <td class="small">
                <span v-if="c.type !== 1" :class="evalClass(c.evaluation)" class="inv-badge">{{ certRowStatuses[c.evaluation] }}</span><span v-else class="inv-muted">-</span>
                <div v-if="c.evaluation === 2" class="text-danger">{{ c.evaluationText }}</div>
                <button v-if="c.type !== 1" class="btn btn-sm btn-link p-0 d-print-none" @click="openSheet(c)">برگه راستی‌آزمایی</button>
              </td>
              <td class="d-print-none">
                <div v-if="c.result === 0 && canVerify" class="d-flex gap-1">
                  <input v-model="verifyNote[c.id]" class="form-control form-control-sm" placeholder="یادداشت" style="min-width: 120px" />
                  <button :disabled="busy" class="btn btn-sm btn-success" @click="verify(c, true)">تایید</button>
                  <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="verify(c, false)">رد</button>
                </div>
                <button v-else-if="c.result === 0 && canWrite" class="btn btn-sm btn-outline-secondary" @click="edit(c)">ویرایش</button>
                <span v-else class="small">{{ c.verificationNote }}</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
