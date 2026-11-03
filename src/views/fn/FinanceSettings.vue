<script setup lang="ts">
import { onMounted, ref } from "vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import { getApprovalThresholds, getAttachmentRules, getFinanceSettings, saveApprovalThreshold, saveAttachmentRule, saveFinanceSetting } from "@/api/FnAPIs";
import { fail, ok } from "@/composables/inventory";
import { money, parseAmount, voucherTypes, yesNo } from "@/composables/fn";
import { FnRoles, hasRole } from "@/composables/roles";

// Control settings of the ledger: switches (finance manager), the vouchers that must carry an attachment, and the amounts from which the CEO approves a voucher.
const tab = ref("settings");
const tabs = [{ key: "settings", title: "تنظیمات کنترل" }, { key: "rules", title: "قاعده پیوست اجباری" }, { key: "thresholds", title: "حد تایید مدیرعامل" }];
const canWrite = hasRole(FnRoles.managers);
const busy = ref(false);

const settings = ref<any[]>([]);
async function loadSettings() {
  const [d, error] = await getFinanceSettings();
  if (error) return fail(error);
  settings.value = d || [];
}
const isFlag = (s: any) => (s.value === "0" || s.value === "1") && s.key !== "ParallelRun.RequiredMonths";
/** the typed value of a setting that is not an on / off switch (CloseBy: CEO or FINANCEMANAGER; go-live month 1405/08; days; months) */
const typed = ref<Record<string, string>>({});
const valueOf = (s: any) => (typed.value[s.key] !== undefined ? typed.value[s.key] : s.value ?? "");
const keyHint: Record<string, string> = {
  "PeriodClose.CloseBy": "چه نقشی مرحله‌ی دوم (بستن قطعی) دوره‌ی قفل‌شده را انجام می‌دهد؛ هرگز همان کسی که دوره را قفل کرده است.",
  "CloseChecklist.Enforce": "۱ = قفل دوره فقط با فهرست کنترل سبز یا عبور تاییدشده‌ی دو مدیر مالی ممکن است.",
  "Payroll:GoLiveMonth": "ماه شمسی شروع حقوق و مرخصی (مثلاً ۱۴۰۵/۰۸)؛ خالی = ماه نخستین بستن تاییدشده‌ی حضور و غیاب.",
  "ParallelRun.RequiredMonths": "تعداد ماه اجرای موازی (۱ تا ۱۲).",
};
async function saveSetting(s: any, value: string) {
  busy.value = true;
  const [, error] = await saveFinanceSetting(s.key, value);
  busy.value = false;
  if (error) return fail(error);
  ok("تنظیم ذخیره شد.");
  loadSettings();
}

const rules = ref<any[]>([]);
const rForm = ref<any>(null);
async function loadRules() {
  const [d, error] = await getAttachmentRules();
  if (error) return fail(error);
  rules.value = d || [];
}
async function saveRule() {
  const f = rForm.value;
  if (!String(f.accountCode).trim() && !f.voucherType) return fail("کد حساب یا نوع سند را مشخص کنید.");
  busy.value = true;
  const [, error] = await saveAttachmentRule({ id: f.id, accountCode: String(f.accountCode).trim() || null, voucherType: f.voucherType ? Number(f.voucherType) : null, note: f.note || null, isActive: f.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("قاعده ذخیره شد.");
  rForm.value = null;
  loadRules();
}

const thresholds = ref<any[]>([]);
const tForm = ref<any>(null);
async function loadThresholds() {
  const [d, error] = await getApprovalThresholds();
  if (error) return fail(error);
  thresholds.value = d || [];
}
async function saveThreshold() {
  const f = tForm.value;
  const amount = parseAmount(f.minAmount);
  if (!amount || amount <= 0) return fail("حداقل مبلغ را وارد کنید.");
  busy.value = true;
  const [, error] = await saveApprovalThreshold({ id: f.id, minAmount: amount, note: f.note || null, isActive: f.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("حد تایید ذخیره شد.");
  tForm.value = null;
  loadThresholds();
}

onMounted(() => {
  loadSettings();
  loadRules();
  loadThresholds();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">تنظیمات کنترل دفاتر</h1></div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'settings'">
        <p class="inv-muted">فقط مدیر مالی می‌تواند تنظیمات را تغییر دهد. مقدار «۱» یعنی فعال و «۰» یعنی غیرفعال.</p>
        <table class="table table-sm">
          <thead><tr><th>تنظیم</th><th>مقدار</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!settings.length"><td colspan="3" class="text-center inv-muted">تنظیمی نیست.</td></tr>
            <tr v-for="s in settings" :key="s.key">
              <td>{{ s.title }} <span class="inv-muted inv-ltr">{{ s.key }}</span><div v-if="keyHint[s.key]" class="inv-muted small">{{ keyHint[s.key] }}</div></td>
              <td><span :class="isFlag(s) ? (s.value === '1' ? 'ok' : '') : ''" class="inv-badge">{{ isFlag(s) ? yesNo(s.value === "1") : s.key === "PeriodClose.CloseBy" ? (s.value === "CEO" ? "مدیرعامل" : "مدیر مالی") : s.value || "-" }}</span></td>
              <td v-if="canWrite" class="text-nowrap">
                <button v-if="isFlag(s)" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="saveSetting(s, s.value === '1' ? '0' : '1')">{{ s.value === "1" ? "غیرفعال کن" : "فعال کن" }}</button>
                <div v-else class="d-flex gap-1">
                  <select v-if="s.key === 'PeriodClose.CloseBy'" :value="valueOf(s)" class="form-select form-select-sm" style="width: 11rem" @change="typed[s.key] = ($event.target as HTMLSelectElement).value"><option value="CEO">مدیرعامل</option><option value="FINANCEMANAGER">مدیر مالی</option></select>
                  <input v-else :value="valueOf(s)" class="form-control form-control-sm inv-ltr" style="width: 9rem" @input="typed[s.key] = ($event.target as HTMLInputElement).value" />
                  <button :disabled="busy || typed[s.key] === undefined || typed[s.key] === s.value" class="btn btn-sm btn-outline-primary" @click="saveSetting(s, typed[s.key]); delete typed[s.key]">ذخیره</button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </template>

      <template v-else-if="tab === 'rules'">
        <div class="d-flex justify-content-between mb-2">
          <span class="inv-muted">سندی که یکی از این قاعده‌ها را داشته باشد بدون پیوست کنترل و قطعی نمی‌شود.</span>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="rForm = { id: null, accountCode: '', voucherType: '', note: '', isActive: true }">قاعده جدید</button>
        </div>
        <table class="table table-sm">
          <thead><tr><th>کد حساب</th><th>نوع سند</th><th>توضیح</th><th>فعال</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!rules.length"><td colspan="5" class="text-center inv-muted">قاعده‌ای نیست.</td></tr>
            <tr v-for="r in rules" :key="r.id">
              <td class="inv-ltr">{{ r.accountCode || "-" }}</td><td>{{ r.voucherType ? voucherTypes[r.voucherType] : "-" }}</td><td>{{ r.note }}</td><td>{{ yesNo(r.isActive) }}</td>
              <td v-if="canWrite"><button class="btn btn-sm btn-outline-secondary" @click="rForm = { id: r.id, accountCode: r.accountCode || '', voucherType: r.voucherType || '', note: r.note || '', isActive: r.isActive }">ویرایش</button></td>
            </tr>
          </tbody>
        </table>
      </template>

      <template v-else>
        <div class="d-flex justify-content-between mb-2">
          <span class="inv-muted">سندی با مبلغ برابر یا بیشتر از کمترین حد فعال، پیش از قطعی‌شدن تایید مدیرعامل می‌خواهد.</span>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="tForm = { id: null, minAmount: '', note: '', isActive: true }">حد جدید</button>
        </div>
        <table class="table table-sm">
          <thead><tr><th>از مبلغ (ریال)</th><th>توضیح</th><th>فعال</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!thresholds.length"><td colspan="4" class="text-center inv-muted">حدی تعریف نشده است.</td></tr>
            <tr v-for="t in thresholds" :key="t.id">
              <td class="fn-num">{{ money(t.minAmount) }}</td><td>{{ t.note }}</td><td>{{ yesNo(t.isActive) }}</td>
              <td v-if="canWrite"><button class="btn btn-sm btn-outline-secondary" @click="tForm = { id: t.id, minAmount: String(t.minAmount), note: t.note || '', isActive: t.isActive }">ویرایش</button></td>
            </tr>
          </tbody>
        </table>
      </template>
    </div>

    <div v-if="rForm" class="inv-modal-backdrop screen-only" @click.self="rForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">قاعده پیوست اجباری</h2>
        <div class="mb-2"><label class="form-label small mb-0">کد حساب (اختیاری)</label><input v-model="rForm.accountCode" class="form-control form-control-sm inv-ltr" /></div>
        <div class="mb-2"><label class="form-label small mb-0">نوع سند (اختیاری)</label><select v-model="rForm.voucherType" class="form-select form-select-sm"><option value="">-</option><option v-for="(t, k) in voucherTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="mb-2"><label class="form-label small mb-0">توضیح</label><input v-model="rForm.note" class="form-control form-control-sm" /></div>
        <label class="form-check small"><input v-model="rForm.isActive" class="form-check-input" type="checkbox" /> فعال</label>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="rForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveRule">ذخیره</button></div>
      </div>
    </div>

    <div v-if="tForm" class="inv-modal-backdrop screen-only" @click.self="tForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">حد تایید مدیرعامل</h2>
        <div class="mb-2"><label class="form-label small mb-0">حداقل مبلغ سند (ریال) *</label><input v-model="tForm.minAmount" class="form-control form-control-sm fn-num" /></div>
        <div class="mb-2"><label class="form-label small mb-0">توضیح</label><input v-model="tForm.note" class="form-control form-control-sm" /></div>
        <label class="form-check small"><input v-model="tForm.isActive" class="form-check-input" type="checkbox" /> فعال</label>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="tForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveThreshold">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
