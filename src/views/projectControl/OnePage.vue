<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { getOnePage } from "@/api/ProjectRegistersAPIs";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import { fail, printPage, rial } from "@/composables/inventory";
import { pcContractId } from "@/composables/projectControl";
import { priorities, riskClass, riskLevels } from "@/composables/tasks";

// The one-page project dashboard: charter summary, progress, milestones, deliverables, issues, risks, tasks and the S-curve.
const d = ref<any>(null);
async function load() {
  d.value = null;
  if (!pcContractId.value) return;
  const [data, e] = await getOnePage(pcContractId.value);
  if (e) return fail(e);
  d.value = data;
}
const W = 560, H = 200;
const pts = (key: string) => {
  const s = (d.value?.sCurve || []).filter((p: any) => p[key] != null);
  const n = d.value?.sCurve?.length || 1;
  return s.map((p: any) => `${(d.value.sCurve.indexOf(p) / Math.max(1, n - 1)) * W},${H - (Number(p[key]) / 100) * H}`).join(" ");
};
const healthClass = computed(() => (d.value?.health === "red" ? "bad" : d.value?.health === "amber" ? "warn" : "ok"));
const healthText = computed(() => ({ red: "نیازمند اقدام", amber: "هشدار", green: "سالم", none: "بدون داده" } as any)[d.value?.health] || d.value?.health || "");
onMounted(load);
watch(pcContractId, load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="خلاصه یک‌صفحه‌ای پروژه" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">خلاصه یک‌صفحه‌ای پروژه</h1>
        <div class="d-flex flex-wrap gap-2 d-print-none"><ContractSelect /><button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button></div>
      </div>
      <NeedContract v-if="!pcContractId" />
      <div v-else-if="d">
        <h5>{{ d.contractTitle }} <span class="inv-ltr small">{{ d.contractNumber }}</span> <span :class="healthClass" class="inv-badge">{{ healthText }}</span></h5>
        <div class="row g-2 mb-3">
          <div class="col-md-6"><div class="border rounded p-2 h-100"><div class="fw-bold">منشور پروژه</div><div>{{ d.charterTitle }}</div><div class="small">هدف: {{ d.charterObjective }}</div><div class="small">دامنه: {{ d.charterScope }}</div><div class="small inv-muted">مدیر پروژه: {{ d.projectManagerName }} | {{ d.plannedStartText }} تا {{ d.plannedFinishText }}<span v-if="d.budget"> | بودجه {{ rial(d.budget) }} ریال</span></div></div></div>
          <div class="col-md-6"><div class="border rounded p-2 h-100">
            <div class="row text-center">
              <div class="col"><div class="fs-3">{{ d.progressPercent }}٪</div><div class="small">پیشرفت واقعی</div></div>
              <div class="col"><div class="fs-3">{{ d.plannedPercent ?? "-" }}٪</div><div class="small">برنامه</div></div>
              <div class="col"><div class="fs-3">{{ d.spi ?? "-" }}</div><div class="small">SPI</div></div>
              <div class="col"><div class="fs-3">{{ d.cpi ?? "-" }}</div><div class="small">CPI</div></div>
              <div class="col"><div class="fs-3" :class="{ 'text-danger': d.lateActivities }">{{ d.lateActivities }}</div><div class="small">فعالیت دیرکرد</div></div>
            </div>
          </div></div>
        </div>

        <div class="row g-2 mb-3">
          <div class="col-md-7"><div class="border rounded p-2">
            <div class="fw-bold mb-1">منحنی S (برنامه / واقعی)</div>
            <svg :viewBox="`-5 -5 ${W + 10} ${H + 10}`" style="width: 100%; direction: ltr" role="img" aria-label="منحنی S">
              <rect :width="W" :height="H" fill="none" stroke="#dee2e6" />
              <polyline :points="pts('plannedPercent')" fill="none" stroke="#6c757d" stroke-width="2" stroke-dasharray="5 3" />
              <polyline :points="pts('actualPercent')" fill="none" stroke="#0d6efd" stroke-width="2.5" />
            </svg>
            <div class="small"><span style="color: #6c757d">- - برنامه</span> &nbsp; <span style="color: #0d6efd">— واقعی</span> <span v-if="!d.sCurve.length" class="inv-muted">(جریان نقدی برنامه ثبت نشده)</span></div>
          </div></div>
          <div class="col-md-5"><div class="border rounded p-2 h-100">
            <div class="fw-bold mb-1">نقاط عطف</div>
            <table class="table table-sm mb-0"><tbody>
              <tr v-if="!d.milestones.length"><td class="inv-muted">نقطه عطفی تعریف نشده.</td></tr>
              <tr v-for="m in d.milestones" :key="m.activityId" :class="{ 'text-danger': m.late }"><td class="inv-ltr">{{ m.code }}</td><td>{{ m.title }}</td><td>{{ m.plannedDateText }}</td><td>{{ m.done ? "انجام شد" : m.percent + "٪" }}</td></tr>
            </tbody></table>
          </div></div>
        </div>

        <div class="row g-2 mb-3 text-center">
          <div class="col-6 col-md-3"><div class="border rounded p-2"><div class="fs-4">{{ d.deliverablesAccepted }}/{{ d.deliverablesTotal }}</div><div class="small">تحویلی پذیرفته‌شده<span v-if="d.deliverablesOverdue" class="text-danger"> ({{ d.deliverablesOverdue }} معوق)</span></div></div></div>
          <div class="col-6 col-md-3"><div class="border rounded p-2"><div class="fs-4">{{ d.issuesOpen }}</div><div class="small">مساله باز <span v-if="d.issuesOverdue" class="text-danger">({{ d.issuesOverdue }} معوق)</span> <span v-if="d.issuesHigh">• {{ d.issuesHigh }} اولویت بالا</span></div></div></div>
          <div class="col-6 col-md-3"><div class="border rounded p-2"><div class="fs-4">{{ d.risksOpen }}</div><div class="small">ریسک باز • زیاد {{ d.risksHigh }} • بحرانی {{ d.risksCritical }}</div></div></div>
          <div class="col-6 col-md-3"><div class="border rounded p-2"><div class="fs-4">{{ d.tasksOpen }}</div><div class="small">وظیفه باز <span v-if="d.tasksOverdue" class="text-danger">({{ d.tasksOverdue }} معوق)</span> • {{ d.progressWaiting }} گزارش پیشرفت در انتظار تایید</div></div></div>
        </div>

        <div class="row g-2">
          <div class="col-md-6"><div class="fw-bold mb-1">ریسک‌های برتر</div>
            <table class="table table-sm"><tbody><tr v-if="!d.topRisks.length"><td class="inv-muted">-</td></tr><tr v-for="r in d.topRisks" :key="r.id"><td class="inv-ltr">{{ r.number }}</td><td>{{ r.title }}</td><td>{{ r.score }}</td><td><span :class="riskClass(r.level)" class="inv-badge">{{ riskLevels[r.level] }}</span></td></tr></tbody></table></div>
          <div class="col-md-6"><div class="fw-bold mb-1">مسائل برتر</div>
            <table class="table table-sm"><tbody><tr v-if="!d.topIssues.length"><td class="inv-muted">-</td></tr><tr v-for="i in d.topIssues" :key="i.id" :class="{ 'text-danger': i.overdue }"><td class="inv-ltr">{{ i.number }}</td><td>{{ i.title }}</td><td>{{ priorities[i.priority] }}</td><td>{{ i.dueDateText }}</td></tr></tbody></table></div>
        </div>
      </div>
    </div>
  </div>
</template>
<style lang="scss" src="../inventory/inventory.scss"></style>
