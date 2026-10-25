<script setup lang="ts">
// Service settings (SvSettings): SLA hours per priority, the settings keys (lead days, cost centre ...) and the manual run of the nightly jobs.
import { computed, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { typedNumber } from "@/components/service/svKit";
import { getSvSettings, getSvSlaRules, runSvJobs, saveSvSetting, saveSvSlaRule } from "@/api/ServiceAPIs";
import { fail, ok } from "@/composables/inventory";
import { hasRole, ServiceRoles } from "@/composables/roles";
import { dateText, priorities, serviceSettingTitles } from "@/composables/service";

const canSla = hasRole(ServiceRoles.slaWriters);
const canSettings = hasRole(ServiceRoles.settingSetters);
const canJobs = hasRole(ServiceRoles.jobRunners);
const busy = ref(false);

// ---------------------------------------------------------------- SLA
const sla = ref<Record<number, { id: number | null; responseHours: string; resolutionHours: string; isActive: boolean }>>({});
async function loadSla() {
  const [data, error] = await getSvSlaRules();
  if (error) return fail(error);
  const byPriority: Record<number, any> = {};
  for (const r of Array.isArray(data) ? data : []) byPriority[r.priority] = r;
  const next: typeof sla.value = {};
  for (const p of [1, 2, 3, 4]) {
    const r = byPriority[p];
    next[p] = { id: r?.id ?? null, responseHours: r ? String(r.responseHours) : "", resolutionHours: r?.resolutionHours ? String(r.resolutionHours) : "", isActive: r ? r.isActive : true };
  }
  sla.value = next;
}
const slaSaved = (p: number) => !!sla.value[p]?.id;
async function saveSla(p: number) {
  const r = sla.value[p];
  const response = typedNumber(r.responseHours);
  if (!response || response <= 0) return fail("ساعت پاسخ باید بیشتر از صفر باشد.");
  busy.value = true;
  const [, error] = await saveSvSlaRule({ priority: p, responseHours: response, resolutionHours: typedNumber(r.resolutionHours), isActive: r.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("ردیف SLA ذخیره شد.");
  loadSla();
}
const anySla = computed(() => Object.values(sla.value).some((r) => r.id));

// ---------------------------------------------------------------- settings
const settings = ref<any[]>([]);
const texts = ref<Record<string, string>>({});
async function loadSettings() {
  const [data, error] = await getSvSettings();
  if (error) return fail(error);
  settings.value = Array.isArray(data) ? data : [];
  texts.value = Object.fromEntries(settings.value.map((s) => [s.key, s.text ?? ""]));
}
async function saveSetting(key: string) {
  busy.value = true;
  const [, error] = await saveSvSetting({ key, text: texts.value[key] || null });
  busy.value = false;
  if (error) return fail(error);
  ok("تنظیم ذخیره شد.");
  loadSettings();
}

// ---------------------------------------------------------------- jobs
const asOf = ref<string | null>(null);
const jobs = ref<any>(null);
const jobsError = ref("");
async function runJobs() {
  busy.value = true;
  jobsError.value = "";
  const [data, error] = await runSvJobs(asOf.value);
  busy.value = false;
  if (error) {
    jobsError.value = error?.message || String(error);
    return fail(error);
  }
  jobs.value = data;
  ok("کارهای شبانه اجرا شد.");
}

onMounted(() => {
  loadSla();
  loadSettings();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <h1 class="inv-title mb-3">تنظیمات خدمات پس از فروش</h1>

      <h3 class="inv-title mb-2">جدول SLA</h3>
      <p class="inv-muted">
        ساعت‌های پاسخ و رفع برای هر اولویت را خودتان تعیین کنید؛ مقدار پیش‌فرضی وجود ندارد. اولویتی که ردیف ندارد، «بدون SLA» است: برای درخواست‌های آن موعدی محاسبه نمی‌شود و تخطی هم رخ نمی‌دهد.
      </p>
      <p v-if="!anySla" class="alert alert-warning py-1 small">هنوز هیچ ردیف SLA ثبت نشده است.</p>
      <div class="table-responsive mb-4">
        <table class="table table-sm">
          <thead><tr><th>اولویت</th><th>ساعت تا پاسخ</th><th>ساعت تا رفع (خالی = ندارد)</th><th>فعال</th><th></th></tr></thead>
          <tbody>
            <tr v-for="(title, key) in priorities" :key="key">
              <template v-if="sla[Number(key)]">
                <td>{{ title }} <span v-if="!slaSaved(Number(key))" class="inv-badge">بدون SLA</span></td>
                <td><input v-model="sla[Number(key)].responseHours" :disabled="!canSla" class="form-control form-control-sm fn-num" inputmode="decimal" style="max-width: 8rem" /></td>
                <td><input v-model="sla[Number(key)].resolutionHours" :disabled="!canSla" class="form-control form-control-sm fn-num" inputmode="decimal" style="max-width: 8rem" /></td>
                <td><input v-model="sla[Number(key)].isActive" :disabled="!canSla" class="form-check-input" type="checkbox" /></td>
                <td><button v-if="canSla" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="saveSla(Number(key))">ذخیره</button></td>
              </template>
            </tr>
          </tbody>
        </table>
      </div>

      <h3 class="inv-title mb-2">تنظیمات</h3>
      <p class="inv-muted">مقدار خالی یعنی مقدار پیش‌فرض (ستون «مقدار جاری») به کار می‌رود. این تنظیمات را فقط مدیرعامل تغییر می‌دهد.</p>
      <div class="table-responsive mb-4">
        <table class="table table-sm">
          <thead><tr><th>تنظیم</th><th>مقدار ذخیره‌شده</th><th>مقدار جاری</th><th></th></tr></thead>
          <tbody>
            <tr v-for="s in settings" :key="s.key">
              <td>{{ s.title || serviceSettingTitles[s.key] || s.key }} <span class="inv-ltr inv-muted">({{ s.key }})</span></td>
              <td><input v-model="texts[s.key]" :disabled="!canSettings" class="form-control form-control-sm inv-ltr" style="max-width: 14rem" /></td>
              <td class="inv-ltr">{{ s.effective ?? "-" }}</td>
              <td><button v-if="canSettings" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="saveSetting(s.key)">ذخیره</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="inv-muted">پیش‌فرض‌های API: پیش‌نویس بازدید LTSA ۳۰ روز قبل از موعد، پیش‌نویس نظرسنجی ۳۰ روز بعد از تحویل.</p>

      <template v-if="canJobs">
        <h3 class="inv-title mb-2">اجرای کارهای شبانه</h3>
        <p class="inv-muted">پیش‌نویس بازدیدهای LTSA، پایان‌دادن به قراردادهای تمام‌شده، هشدار تخطی SLA و پیش‌نویس نظرسنجی‌ها. مقدار خالی یعنی امروز؛ روز آینده به‌طور عادی رد می‌شود.</p>
        <div class="d-flex gap-2 align-items-end flex-wrap mb-2">
          <div><label class="form-label small mb-0">برای روز</label><FnDate v-model="asOf" /></div>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="runJobs">اجرای کارهای شبانه</button>
        </div>
        <p v-if="jobsError" class="alert alert-danger py-1 small">{{ jobsError }}</p>
        <div v-if="jobs" class="small">
          <div>روز اجرا: {{ dateText(jobs.asOf) }}</div>
          <div>بازدیدهای پیش‌نویس‌شده: {{ jobs.visitsDrafted }} | قراردادهای منقضی: {{ jobs.contractsExpired }} | هشدارهای تخطی SLA: {{ jobs.breachAlerts }} | نظرسنجی‌های پیش‌نویس‌شده: {{ jobs.surveysDrafted }}</div>
          <ul v-if="(jobs.notes || []).length" class="mb-0"><li v-for="(n, i) in jobs.notes" :key="i">{{ n }}</li></ul>
        </div>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
