<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { rowsOf } from "@/api/ApiClient";
import { generateWeekly, getWeeklyReports, releaseWeekly, saveWeekly } from "@/api/ProjectRegistersAPIs";
import JDate from "@/components/pc/JDate.vue";
import { fail, ok, printPage, today } from "@/composables/inventory";
import { hasRole, ProjectControlRoles } from "@/composables/roles";
import { weeklyStates } from "@/composables/tasks";

// Weekly report: the figures come from progress, issues, risks, tasks and daily reports; the manager edits the summary and the PMO releases it.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const sel = ref<any>(null);
const date = ref<string | null>(today());
const isPmo = hasRole(ProjectControlRoles.pmo);

async function load() {
  const [d, e] = await getWeeklyReports({ ContractId: props.contractId, PageSize: 60 });
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
async function generate() {
  const [d, e] = await generateWeekly({ contractId: props.contractId, date: date.value });
  if (e) return fail(e);
  ok("گزارش هفتگی ساخته شد.");
  await load();
  sel.value = d;
}
async function save() {
  const [d, e] = await saveWeekly({ id: sel.value.id, summary: sel.value.summary, nextWeekPlan: sel.value.nextWeekPlan });
  if (e) return fail(e);
  ok("ذخیره شد.");
  sel.value = d;
  load();
}
async function release() {
  const [d, e] = await releaseWeekly(sel.value.id);
  if (e) return fail(e);
  ok("گزارش منتشر شد.");
  sel.value = d;
  load();
}
onMounted(load);
watch(() => props.contractId, () => { sel.value = null; load(); });
</script>

<template>
  <div>
    <div class="d-flex gap-2 align-items-end mb-2 d-print-none"><div style="width: 170px"><label class="form-label small mb-0">هفته شامل تاریخ</label><JDate v-model="date" /></div><button class="btn btn-sm btn-success" @click="generate">ساخت / به‌روزرسانی گزارش هفته</button></div>
    <div class="row g-3">
      <div class="col-md-4 d-print-none">
        <div class="list-group"><a v-for="r in rows" :key="r.id" :class="{ active: sel?.id === r.id }" class="list-group-item list-group-item-action" href="#" @click.prevent="sel = r">{{ r.number }} - {{ r.weekText }} <span class="inv-badge ms-1">{{ weeklyStates[r.state] }}</span></a><div v-if="!rows.length" class="list-group-item inv-muted">گزارشی نیست.</div></div>
      </div>
      <div class="col-md-8">
        <div v-if="sel">
          <h5>گزارش هفتگی {{ sel.number }} - {{ sel.weekText }} <span class="inv-badge">{{ weeklyStates[sel.state] }}</span></h5>
          <table class="table table-sm">
            <tbody>
              <tr><th>پیشرفت واقعی / برنامه</th><td>{{ sel.auto.progressPercent }}٪ / {{ sel.auto.plannedPercent ?? "-" }}٪</td><th>SPI / CPI</th><td>{{ sel.auto.spi ?? "-" }} / {{ sel.auto.cpi ?? "-" }}</td></tr>
              <tr><th>فعالیت شروع / پایان / دیرکرد</th><td>{{ sel.auto.activitiesStarted }} / {{ sel.auto.activitiesFinished }} / {{ sel.auto.lateActivities }}</td><th>گزارش روزانه / HSE</th><td>{{ sel.auto.dailyReports }} / {{ sel.auto.hseEvents }}</td></tr>
              <tr><th>مسائل جدید / باز / بسته</th><td>{{ sel.auto.newIssues }} / {{ sel.auto.openIssues }} / {{ sel.auto.closedIssues }}</td><th>ریسک باز / زیاد</th><td>{{ sel.auto.openRisks }} / {{ sel.auto.highRisks }}</td></tr>
              <tr><th>وظایف بسته / معوق</th><td>{{ sel.auto.tasksClosed }} / {{ sel.auto.tasksOverdue }}</td><td colspan="2"></td></tr>
            </tbody>
          </table>
          <ul v-if="sel.auto.highlights?.length"><li v-for="(h, i) in sel.auto.highlights" :key="i">{{ h }}</li></ul>
          <label class="form-label small mb-0">خلاصه مدیریتی</label><textarea v-model="sel.summary" :disabled="sel.state === 1" class="form-control form-control-sm mb-2" rows="4"></textarea>
          <label class="form-label small mb-0">برنامه هفته بعد</label><textarea v-model="sel.nextWeekPlan" :disabled="sel.state === 1" class="form-control form-control-sm" rows="3"></textarea>
          <div class="d-flex gap-2 mt-2 d-print-none"><button v-if="sel.state === 0" class="btn btn-sm btn-primary" @click="save">ذخیره</button><button v-if="sel.state === 0 && isPmo" class="btn btn-sm btn-success" @click="release">انتشار (PMO)</button><button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button></div>
          <p v-if="sel.releasedByName" class="small inv-muted mt-1">منتشر شده توسط {{ sel.releasedByName }} {{ sel.releasedAtText }}. انتشار را باید شخصی غیر از آخرین ویرایش‌کننده انجام دهد.</p>
        </div>
      </div>
    </div>
  </div>
</template>
