<script setup lang="ts">
import { ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { getTraceability } from "@/api/QualityAPIs";
import { fail, jalali, num, printPage } from "@/composables/inventory";
import { certificateResults, certificateTypes, reportResults, reportStatuses, reportTypes, vtResults } from "@/composables/quality";

// Traceability query: by serial or by lot / heat number -> lots, certificates, receipts, NCRs, test reports, weld logs and stage history.
const route = useRoute();
const router = useRouter();
const serial = ref(String(route.query.serial || ""));
const lotNumber = ref(String(route.query.lot || ""));
const data = ref<any>(null);
const busy = ref(false);

async function search() {
  if (!serial.value.trim() && !lotNumber.value.trim()) return fail("سریال یا شماره بچ / ذوب را وارد کنید.");
  busy.value = true;
  const [result, error] = await getTraceability({ Serial: serial.value.trim(), LotNumber: lotNumber.value.trim() });
  busy.value = false;
  if (error) return fail(error);
  data.value = result;
}

if (serial.value || lotNumber.value) search();
</script>

<template>
  <div class="inv-page ks-report">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">ردیابی سریال و بچ</h1>
        <div class="d-flex gap-2 d-print-none">
          <button v-if="data" class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <router-link :to="{ name: 'QualityCertificates' }" class="btn btn-sm btn-outline-secondary">گواهی‌های مواد</router-link>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <input v-model="serial" class="form-control form-control-sm inv-ltr" placeholder="شماره سریال" @keydown.enter="search" />
        <input v-model="lotNumber" class="form-control form-control-sm inv-ltr" placeholder="شماره بچ / ذوب" @keydown.enter="search" />
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="search">جستجو</button>
      </div>

      <template v-if="data">
        <h2 class="inv-title mb-2">بچ‌ها و گواهی‌ها <span v-if="data.serial" class="inv-ltr ms-2">{{ data.serial }}</span></h2>
        <p v-if="!data.lots.length" class="inv-muted">بچی پیدا نشد.</p>
        <div v-for="l in data.lots" :key="l.lotId" class="border rounded p-2 mb-2">
          <div><b class="inv-ltr">{{ l.lotNumber }}</b> — ذوب <span class="inv-ltr">{{ l.heatNumber }}</span> — <span class="inv-ltr">{{ l.itemCode }}</span> {{ l.itemTitle }} <span v-if="l.relation" class="inv-badge info">{{ l.relation }}</span></div>
          <div class="small">گواهی ثبت‌شده روی بچ: {{ l.certificateType }} <span class="inv-ltr">{{ l.certificateNumber }}</span></div>
          <table v-if="l.certificates.length" class="table table-sm mt-1 mb-1">
            <thead><tr><th>نوع</th><th>شماره</th><th>صادرکننده</th><th>وضعیت</th><th>تایید</th></tr></thead>
            <tbody><tr v-for="c in l.certificates" :key="c.id"><td>{{ certificateTypes[c.type] }}</td><td class="inv-ltr">{{ c.number }}</td><td>{{ c.issuer }}</td><td>{{ certificateResults[c.result] }}</td><td>{{ c.verifiedByName }} {{ jalali(c.verifiedAt) }}</td></tr></tbody>
          </table>
          <table v-if="l.receipts.length" class="table table-sm mb-0">
            <thead><tr><th>سند رسید</th><th>تاریخ</th><th>تامین‌کننده</th><th>مقدار</th></tr></thead>
            <tbody><tr v-for="(r, i) in l.receipts" :key="i"><td class="inv-ltr">{{ r.documentNumber }}</td><td>{{ jalali(r.documentDate) }}</td><td>{{ r.provider }}</td><td>{{ num(r.quantity) }}</td></tr></tbody>
          </table>
        </div>

        <div class="row g-3">
          <div class="col-lg-6">
            <h2 class="inv-title mb-2">گزارش‌های عدم انطباق</h2>
            <table class="table table-sm">
              <thead><tr><th>شماره</th><th>وضعیت</th><th>شدت</th><th>شرح</th></tr></thead>
              <tbody>
                <tr v-if="!data.ncrs.length"><td colspan="4" class="inv-muted text-center">موردی نیست.</td></tr>
                <tr v-for="n in data.ncrs" :key="n.id" role="button" @click="router.push({ name: 'InventoryNcrEdit', params: { id: n.id } })"><td class="inv-ltr">{{ n.number }}</td><td>{{ n.status }}</td><td>{{ n.severity }}</td><td>{{ n.description }}</td></tr>
              </tbody>
            </table>
          </div>
          <div class="col-lg-6">
            <h2 class="inv-title mb-2">گزارش‌های آزمون</h2>
            <table class="table table-sm">
              <thead><tr><th>شماره</th><th>نوع</th><th>تاریخ</th><th>وضعیت</th><th>نتیجه</th></tr></thead>
              <tbody>
                <tr v-if="!data.testReports.length"><td colspan="5" class="inv-muted text-center">موردی نیست.</td></tr>
                <tr v-for="r in data.testReports" :key="r.id" role="button" @click="router.push({ name: 'QualityReportEdit', params: { id: r.id } })">
                  <td class="inv-ltr">{{ r.number }}</td><td>{{ reportTypes[r.type] }}</td><td>{{ jalali(r.reportDate) }}</td><td>{{ reportStatuses[r.status] }}</td><td>{{ reportResults[r.result] }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="col-lg-12">
            <h2 class="inv-title mb-2">جوشکاری‌ها</h2>
            <table class="table table-sm">
              <thead><tr><th>تاریخ</th><th>اتصال</th><th>WPS</th><th>جوشکار</th><th>مهر</th><th>فرایند</th><th>بچ فیلر</th><th>VT</th><th>گزارش NDT</th></tr></thead>
              <tbody>
                <tr v-if="!data.weldLogs.length"><td colspan="9" class="inv-muted text-center">موردی نیست.</td></tr>
                <tr v-for="w in data.weldLogs" :key="w.id">
                  <td>{{ jalali(w.weldDate) }}</td><td class="inv-ltr">{{ w.jointNo }}</td><td class="inv-ltr">{{ w.wpsNumber }}</td><td>{{ w.welderName }}</td><td class="inv-ltr">{{ w.welderStamp }}</td>
                  <td class="inv-ltr">{{ w.process }}</td><td class="inv-ltr">{{ w.fillerBatch }}</td><td>{{ vtResults[w.vt] }}</td><td class="inv-ltr">{{ (w.reportNumbers || []).join("، ") }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="col-lg-12">
            <h2 class="inv-title mb-2">تاریخچه مراحل ساخت</h2>
            <div class="inv-timeline">
              <div v-for="(s, i) in data.stageHistory" :key="i" class="step done">
                <b>{{ s.label }}</b> <span class="inv-muted">{{ jalali(s.when, true) }}</span>
                <div class="small">{{ s.detail }}</div>
              </div>
              <p v-if="!data.stageHistory.length" class="inv-muted">تاریخچه‌ای نیست.</p>
            </div>
          </div>
        </div>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
