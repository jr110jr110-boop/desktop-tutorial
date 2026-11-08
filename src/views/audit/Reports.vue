<script setup lang="ts">
import { onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getReports, getReport, generateReport, rowsOf } from "@/api/AuditAPIs";
import { fail, ok, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { AuditRoles, reportKinds } from "@/composables/audit";

// Audit reports: daily digest, weekly (PMO and QA), monthly (CEO). A stored report is immutable (its SHA-256 is shown); printing uses the A4 report layout.
const rows = ref<any[]>([]);
const kind = ref<number | null>(null);
const open = ref<any>(null);
const genKind = ref(2);
const canGenerate = hasRole(AuditRoles.runners);

async function load() {
  const [d, e] = await getReports(kind.value, 60);
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
async function show(id: number) {
  const [d, e] = await getReport(id);
  if (e) return fail(e);
  open.value = d;
}
async function generate() {
  const [d, e] = await generateReport(genKind.value, null);
  if (e) return fail(e);
  ok("گزارش آماده شد.");
  await load();
  open.value = d;
}
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader :title="open ? open.title : 'گزارش‌های ممیزی داخلی'" :subtitle="open ? open.periodFromText + ' تا ' + open.periodToText : ''" />
    <div class="inv-card">
      <div class="inv-toolbar d-print-none">
        <h1 class="inv-title">گزارش‌های ممیزی داخلی</h1>
        <div class="d-flex gap-2">
          <select v-model="kind" class="form-select form-select-sm" @change="load">
            <option :value="null">همه</option>
            <option v-for="(t, k) in reportKinds" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
          <template v-if="canGenerate">
            <select v-model.number="genKind" class="form-select form-select-sm"><option v-for="(t, k) in reportKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
            <button class="btn btn-sm btn-primary" @click="generate">تهیه‌ی گزارش</button>
          </template>
          <button v-if="open" class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <table v-if="!open || true" class="table table-sm table-hover d-print-none">
        <thead><tr><th>نوع</th><th>عنوان</th><th>دوره</th><th>زمان تهیه</th><th>گیرندگان</th><th>متن</th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="6">گزارشی ثبت نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id" style="cursor: pointer" @click="show(r.id)">
            <td>{{ r.kind }}</td><td>{{ r.title }}</td><td>{{ r.periodFromText }} تا {{ r.periodToText }}</td><td>{{ r.generatedAtText }}</td><td>{{ r.recipients }}</td><td class="small">{{ r.narrativeSource }}</td>
          </tr>
        </tbody>
      </table>
      <div v-if="open" class="mt-3">
        <p v-if="open.narrative" style="white-space: pre-line">{{ open.narrative }}</p>
        <div v-if="open.content" class="row text-center mb-3">
          <div class="col"><div class="fs-4">{{ open.content.open }}</div><small>باز</small></div>
          <div class="col"><div class="fs-4">{{ open.content.openMajor }} / {{ open.content.openMinor }} / {{ open.content.openObservation }}</div><small>عمده / جزئی / مشاهده</small></div>
          <div class="col"><div class="fs-4">{{ open.content.newInPeriod }}</div><small>تازه در دوره</small></div>
          <div class="col"><div class="fs-4">{{ open.content.closedInPeriod }}</div><small>بسته‌شده</small></div>
          <div class="col"><div class="fs-4">{{ open.content.overdue }}</div><small>گذشته از مهلت</small></div>
          <div class="col"><div class="fs-4">{{ open.content.recurrences }}</div><small>تکرار</small></div>
          <div class="col"><div class="fs-4">{{ open.content.avgClosureDays ?? "-" }}</div><small>میانگین روز تا بسته شدن</small></div>
        </div>
        <template v-if="open.content">
          <p class="small">سن یافته‌های باز: تا ۷ روز {{ open.content.age0To7 }}، ۸ تا ۳۰ روز {{ open.content.age8To30 }}، ۳۱ تا ۹۰ روز {{ open.content.age31To90 }}، بیش از ۹۰ روز {{ open.content.ageOver90 }}. روند نسبت به دوره‌ی قبل: {{ open.content.trend }}.</p>
          <div class="row">
            <div class="col-md-4"><h3 class="h6">بر حسب فرایند</h3><ul class="small"><li v-for="x in open.content.byProcess" :key="x.key">{{ x.title }}: {{ x.count }}</li></ul></div>
            <div class="col-md-4"><h3 class="h6">بر حسب وضعیت</h3><ul class="small"><li v-for="x in open.content.byStatus" :key="x.key">{{ x.title }}: {{ x.count }}</li></ul></div>
            <div class="col-md-4"><h3 class="h6">پرتکرارترین قواعد</h3><ul class="small"><li v-for="x in open.content.topRules" :key="x.key">{{ x.title }}: {{ x.count }}</li></ul></div>
          </div>
          <h3 class="h6">امتیاز انطباق بندهای استاندارد</h3>
          <table class="table table-sm">
            <thead><tr><th>استاندارد</th><th>بند</th><th>قواعد</th><th>منطبق</th><th>نامنطبق</th><th>نتیجه</th></tr></thead>
            <tbody><tr v-for="(s, i) in open.content.scores" :key="i"><td class="inv-ltr">{{ s.standard }}</td><td class="inv-ltr">{{ s.clause }}</td><td>{{ s.rules }}</td><td>{{ s.met }}</td><td>{{ s.not }}</td><td>{{ s.result }}</td></tr></tbody>
          </table>
          <h3 class="h6">یافته‌های عمده‌ی باز</h3>
          <ul class="small"><li v-for="m in open.content.majorOpen" :key="m.id">{{ m.number }} - {{ m.title }} (مهلت {{ m.dueDateText }})</li></ul>
        </template>
        <div class="small inv-muted inv-ltr">SHA-256: {{ open.sha256 }}</div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
