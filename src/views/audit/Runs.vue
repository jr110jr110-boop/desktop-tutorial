<script setup lang="ts">
import { onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getRuns, getRun, runNow, rowsOf } from "@/api/AuditAPIs";
import { fail, ok, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { AuditRoles } from "@/composables/audit";

// History of the audit runs (the seat runs daily; an on-demand run is allowed for the auditor, QA and PMO).
const rows = ref<any[]>([]);
const open = ref<any>(null);
const busy = ref(false);
const canRun = hasRole(AuditRoles.runners);

async function load() {
  const [d, e] = await getRuns(60);
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
async function show(id: number) {
  const [d, e] = await getRun(id);
  if (e) return fail(e);
  open.value = d;
}
async function start() {
  busy.value = true;
  try {
    const [d, e] = await runNow(null);
    if (e) return fail(e);
    ok("اجرا انجام شد: " + (d?.newFindings ?? 0) + " یافته‌ی تازه.");
    await load();
  } finally { busy.value = false; }
}
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="اجراهای ممیز داخلی" subtitle="تاریخچه" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">اجراهای ممیز داخلی</h1>
        <div class="d-flex gap-2 d-print-none">
          <button v-if="canRun" class="btn btn-sm btn-primary" :disabled="busy" @click="start">اجرای فوری</button>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>نوع</th><th>آغاز</th><th>پایان</th><th>وضعیت</th><th>قواعد</th><th>خطا</th><th>انحراف</th><th>یافته‌ی تازه</th><th>تکرار</th><th>بازبینی</th><th>بازبینی ردشده</th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="11">اجرایی ثبت نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id" style="cursor: pointer" @click="show(r.id)">
            <td>{{ r.kind }}</td><td>{{ r.startedAtText }}</td><td>{{ r.finishedAtText }}</td><td>{{ r.status }}</td><td>{{ r.rulesRun }}</td><td>{{ r.rulesFailed }}</td>
            <td>{{ r.hits }}</td><td>{{ r.newFindings }}</td><td>{{ r.recurrences }}</td><td>{{ r.rechecked }}</td><td>{{ r.recheckFailed }}</td>
          </tr>
        </tbody>
      </table>
      <div v-if="open" class="mt-3">
        <h2 class="h6">جزئیات اجرا {{ open.startedAtText }}</h2>
        <p v-if="open.message" class="small">{{ open.message }}</p>
        <table class="table table-sm">
          <thead><tr><th>قاعده</th><th>وضعیت</th><th>انحراف</th><th>تازه</th><th>زمان (ms)</th></tr></thead>
          <tbody><tr v-for="x in open.rules" :key="x.code"><td class="inv-ltr">{{ x.code }}</td><td>{{ x.status }}</td><td>{{ x.hits }}</td><td>{{ x.new }}</td><td>{{ x.ms }}</td></tr></tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
