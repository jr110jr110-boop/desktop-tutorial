<script setup lang="ts">
// Delay-cause report (R19): the late lines of the confirmed daily reports by cause, and the issues that explain them by responsible position.
import { onMounted, ref, watch } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import JDate from "@/components/pc/JDate.vue";
import DashChart from "@/components/dashboards/DashChart.vue";
import { getDelayCauses } from "@/api/DashboardsAPIs";
import { fail, jalali } from "@/composables/inventory";
import { pcContractId } from "@/composables/projectControl";

const from = ref<string | null>(null);
const to = ref<string | null>(null);
const cause = ref("");
const d = ref<any>(null);

async function load() {
  const [r, e] = await getDelayCauses({ contractId: pcContractId.value, from: from.value, to: to.value, causeCode: cause.value });
  if (e) return fail(e);
  d.value = r;
}
const chartOf = () => ({ key: "causes", title: "", kind: "bar", labels: d.value.byCause.map((c: any) => c.cause), series: [{ name: "ردیف‌های دیرکرد", values: d.value.byCause.map((c: any) => c.lateLines) }] });
onMounted(load);
watch(pcContractId, load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="گزارش علل تأخیر" subtitle="بر پایه علت و مسئول" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <div><h1 class="inv-title screen-only">گزارش علل تأخیر</h1><span class="inv-muted d-print-none">ردیف‌های دیرکردِ گزارش‌های روزانه‌ی تاییدشده در بازه، به تفکیک علت؛ موضوعات مرتبط به تفکیک سمت مسئول.</span></div>
        <div class="d-flex flex-wrap gap-2 d-print-none align-items-center">
          <ContractSelect allow-all @change="load" />
          <JDate v-model="from" /><JDate v-model="to" />
          <input v-model="cause" class="form-control form-control-sm" style="width: 110px" placeholder="کد علت" />
          <button class="btn btn-sm btn-outline-secondary" @click="load">نمایش</button>
        </div>
      </div>
      <template v-if="d">
        <p>بازه: {{ jalali(d.from) }} تا {{ jalali(d.to) }} — {{ d.totalLateLines }} ردیف دیرکرد</p>
        <DashChart v-if="d.byCause.length" :chart="chartOf()" :key="d.totalLateLines + cause" />
        <h2 class="h6 mt-3">به تفکیک علت</h2>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>کد</th><th>علت</th><th>ردیف دیرکرد</th><th>فعالیت</th><th>گزارش</th><th>موضوع باز</th><th>موضوع بسته</th></tr></thead>
            <tbody>
              <tr v-if="!d.byCause.length"><td colspan="7" class="text-center inv-muted">ردیف دیرکردی نیست.</td></tr>
              <tr v-for="c in d.byCause" :key="c.causeCode"><td class="inv-ltr">{{ c.causeCode }}</td><td>{{ c.cause }}</td><td>{{ c.lateLines }}</td><td>{{ c.activities }}</td><td>{{ c.reports }}</td><td>{{ c.openIssues }}</td><td>{{ c.closedIssues }}</td></tr>
            </tbody>
          </table>
        </div>
        <h2 class="h6 mt-3">به تفکیک مسئول</h2>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>علت</th><th>مسئول (سمت)</th><th>باز</th><th>بسته</th><th>دیرکرد رسیدگی</th></tr></thead>
            <tbody>
              <tr v-if="!d.byResponsible.length"><td colspan="5" class="text-center inv-muted">موضوعی ثبت نشده است.</td></tr>
              <tr v-for="(r, i) in d.byResponsible" :key="i"><td class="inv-ltr">{{ r.causeCode }}</td><td>{{ r.responsible }}</td><td>{{ r.openIssues }}</td><td>{{ r.closedIssues }}</td><td :class="r.overdueIssues ? 'text-danger' : ''">{{ r.overdueIssues }}</td></tr>
            </tbody>
          </table>
        </div>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
