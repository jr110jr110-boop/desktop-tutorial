<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { confirmDailyReport, getDailyReport, saveDailyReport } from "@/api/ProjectControlAPIs";
import JDate from "@/components/pc/JDate.vue";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { unitOptions } from "@/composables/projectControl";
import { currentRole, hasRole, ProjectControlRoles } from "@/composables/roles";

// Daily report of a unit (R-PR-4): the system pre-fills the activities that should be running; the unit manager adds the delay reason,
// the preventive and the corrective action for every late activity and confirms. A confirmed report is not changed.
const props = defineProps({ contractId: { type: Number, required: true } });
const unit = ref<string>("ALL");
const date = ref<string | null>(today());
const rep = ref<any>(null);
const busy = ref(false);
const canWrite = hasRole(ProjectControlRoles.unitManagers);
const role = currentRole();

async function load() {
  rep.value = null;
  const [data, error] = await getDailyReport({ ContractId: props.contractId, Unit: unit.value, Date: date.value });
  if (error) return fail(error);
  rep.value = data;
}
async function save() {
  busy.value = true;
  const [data, error] = await saveDailyReport({ ...rep.value, contractId: props.contractId, unit: unit.value, reportDate: date.value });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  rep.value = data;
}
async function confirm() {
  if (!rep.value.id) {
    await save();
    if (!rep.value?.id) return;
  }
  busy.value = true;
  const [data, error] = await confirmDailyReport(rep.value.id);
  busy.value = false;
  if (error) return fail(error);
  ok("گزارش تایید شد.");
  rep.value = data;
}
const mayConfirm = () => (unit.value === "ALL" ? canWrite : role === unit.value || role === "PMO" || role === "PLANNING");
onMounted(() => { if (role && unitOptions.some((u) => u[0] === role)) unit.value = role; load(); });
watch(() => props.contractId, load);
</script>

<template>
  <div>
    <div class="d-flex flex-wrap gap-2 align-items-end mb-3 d-print-none">
      <div style="min-width: 180px"><label class="form-label small mb-0">واحد</label><select v-model="unit" class="form-select form-select-sm"><option value="ALL">همه واحدها</option><option v-for="u in unitOptions" :key="u[0]" :value="u[0]">{{ u[1] }}</option></select></div>
      <div style="width: 170px"><label class="form-label small mb-0">تاریخ گزارش</label><JDate v-model="date" /></div>
      <button class="btn btn-sm btn-secondary" @click="load">نمایش</button>
    </div>
    <div v-if="rep">
      <p class="mb-2">
        گزارش روزانه {{ unit === "ALL" ? "همه واحدها" : unitOptions.find((u) => u[0] === unit)?.[1] }} — {{ jalali(rep.reportDate) }}
        <span :class="rep.state === 1 ? 'ok' : 'warn'" class="inv-badge">{{ rep.state === 1 ? "تایید شد " + jalali(rep.confirmedAt) : rep.id ? "ذخیره‌شده (تاییدنشده)" : "پیش‌نویس سیستم" }}</span>
      </p>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>فعالیت</th><th>رویداد / وضعیت</th><th style="width: 90px">درصد</th><th>تاخیر</th><th>کد دلیل</th><th>دلیل تاخیر</th><th>اقدام پیشگیرانه</th><th>اقدام اصلاحی</th></tr></thead>
          <tbody>
            <tr v-if="!rep.lines.length"><td colspan="8" class="text-center inv-muted">فعالیتی برای این روز و واحد نیست.</td></tr>
            <tr v-for="l in rep.lines" :key="l.activityId" :class="{ 'table-warning': l.isLate }">
              <td><span class="inv-ltr">{{ l.activityCode }}</span> {{ l.activityTitle }}</td>
              <td><input v-model="l.eventText" :disabled="rep.state === 1 || !canWrite" class="form-control form-control-sm" /></td>
              <td><input v-model.number="l.percent" :disabled="rep.state === 1 || !canWrite" class="form-control form-control-sm" type="number" min="0" max="100" /></td>
              <td><input v-model="l.isLate" :disabled="rep.state === 1 || !canWrite" class="form-check-input" type="checkbox" /></td>
              <td><input v-model="l.delayReasonCode" :disabled="rep.state === 1 || !canWrite || !l.isLate" class="form-control form-control-sm inv-ltr" style="width: 80px" /></td>
              <td><input v-model="l.delayReason" :disabled="rep.state === 1 || !canWrite || !l.isLate" class="form-control form-control-sm" /></td>
              <td><input v-model="l.preventiveAction" :disabled="rep.state === 1 || !canWrite" class="form-control form-control-sm" /></td>
              <td><input v-model="l.correctiveAction" :disabled="rep.state === 1 || !canWrite" class="form-control form-control-sm" /></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-if="rep.state !== 1 && canWrite" class="d-flex gap-2 d-print-none">
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        <button v-if="mayConfirm()" :disabled="busy || !rep.lines.length" class="btn btn-sm btn-success" @click="confirm">تایید گزارش</button>
      </div>
      <p class="inv-muted mt-2 mb-0">درصد این گزارش فقط یادداشت است و پیشرفت فعالیت را تغییر نمی‌دهد (پیشرفت از «درخواست‌های پیشرفت» یا رویدادهای سیستم می‌آید). برای فعالیت دارای تاخیر، کد و شرح دلیل و اقدام اصلاحی لازم است؛ تایید را مدیر همان واحد انجام می‌دهد.</p>
    </div>
  </div>
</template>
