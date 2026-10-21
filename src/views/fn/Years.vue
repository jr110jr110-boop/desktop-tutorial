<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import { closeFiscalYear, ensureFiscalYear, getFiscalYears, periodAction } from "@/api/FnAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { periodStatuses, yearStatuses } from "@/composables/fn";
import { FnRoles, hasRole } from "@/composables/roles";

// Fiscal years and their periods (Persian months): create a year, lock / reopen / close a period, close the year.
const years = ref<any[]>([]);
const openId = ref<number | null>(null);
const newDate = ref<string | null>(today());
const dialog = ref<null | { kind: "reopen" | "close-year"; id: number; title: string }>(null);
const busy = ref(false);
const canManage = hasRole(FnRoles.managers);
const canEnsure = hasRole(FnRoles.systemJobs);

async function load() {
  const [data, error] = await getFiscalYears();
  if (error) return fail(error);
  years.value = data || [];
  if (openId.value === null && years.value.length) openId.value = years.value[0].id;
}

function replaceYear(y: any) {
  const i = years.value.findIndex((x) => x.id === y.id);
  if (i >= 0) years.value[i] = y;
  else years.value.unshift(y);
}

async function ensure() {
  if (!newDate.value) return fail("تاریخی در سال مالی مورد نظر را وارد کنید.");
  busy.value = true;
  const [data, error] = await ensureFiscalYear(newDate.value);
  busy.value = false;
  if (error) return fail(error);
  ok("سال مالی آماده است.");
  replaceYear(data);
  openId.value = data.id;
}

async function act(p: any, action: string, reason?: string) {
  busy.value = true;
  const [data, error] = await periodAction(p.id, action, reason);
  busy.value = false;
  if (error) return fail(error);
  ok("انجام شد.");
  replaceYear(data);
}

function onConfirm(v: any) {
  const d = dialog.value!;
  dialog.value = null;
  if (d.kind === "reopen") {
    const p = { id: d.id };
    act(p, "reopen", v.reason);
  } else closeYear(d.id, v);
}

async function closeYear(id: number, v: any) {
  busy.value = true;
  const override = v.override === "1";
  const [data, error] = await closeFiscalYear(id, override, v.reason);
  busy.value = false;
  if (error) return fail(error);
  ok("سال مالی بسته شد؛ سند اختتامیه و سند افتتاحیه سال بعد صادر شد.");
  replaceYear(data);
  load();
}

onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">سال و دوره‌های مالی</h1>
        <div v-if="canEnsure" class="d-flex gap-2 align-items-end">
          <div><label class="form-label small mb-0">یک تاریخ از سال مالی</label><FnDate v-model="newDate" /></div>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="ensure">ایجاد / آماده‌سازی سال مالی</button>
        </div>
      </div>
      <p class="inv-muted">دوره‌ها ماه‌های شمسی هستند. دوره «قفل» فقط برای مدیر مالی باز است؛ دوره «بسته» قطعی است و سندی در آن ثبت نمی‌شود.</p>
      <p v-if="!years.length" class="inv-muted">سال مالی تعریف نشده است.</p>

      <div v-for="y in years" :key="y.id" class="border rounded mb-2">
        <div class="d-flex justify-content-between align-items-center p-2 bg-light" style="cursor: pointer" @click="openId = openId === y.id ? null : y.id">
          <div>
            <b>{{ y.title }}</b>
            <span class="inv-muted me-2">از {{ jalali(y.startDate) }} تا {{ jalali(y.endDate) }}</span>
            <span :class="y.status === 0 ? 'ok' : y.status === 1 ? 'warn' : ''" class="inv-badge">{{ yearStatuses[y.status] }}</span>
            <span v-if="y.isImported" class="inv-badge info me-1">واردشده از سپیدار</span>
          </div>
          <button v-if="canManage && y.status === 0" class="btn btn-sm btn-outline-danger" @click.stop="dialog = { kind: 'close-year', id: y.id, title: 'بستن سال مالی ' + y.title }">بستن سال مالی</button>
        </div>
        <div v-if="openId === y.id" class="p-2">
          <table class="table table-sm table-hover mb-0">
            <thead><tr><th>#</th><th>دوره</th><th>از</th><th>تا</th><th>وضعیت</th><th>تعداد اسناد</th><th>اسناد قطعی‌نشده</th><th v-if="canManage"></th></tr></thead>
            <tbody>
              <tr v-for="p in y.periods" :key="p.id">
                <td>{{ p.number }}</td>
                <td>{{ p.title }}</td>
                <td>{{ jalali(p.startDate) }}</td>
                <td>{{ jalali(p.endDate) }}</td>
                <td><span :class="p.status === 0 ? 'ok' : p.status === 1 ? 'warn' : 'bad'" class="inv-badge">{{ periodStatuses[p.status] }}</span></td>
                <td class="fn-num">{{ p.vouchers }}</td>
                <td class="fn-num">{{ p.nonFinalVouchers }}</td>
                <td v-if="canManage" class="text-nowrap">
                  <button v-if="p.status === 0" :disabled="busy" class="btn btn-sm btn-outline-secondary me-1" @click="act(p, 'lock')">قفل</button>
                  <button v-if="p.status === 1" :disabled="busy" class="btn btn-sm btn-outline-secondary me-1" @click="dialog = { kind: 'reopen', id: p.id, title: 'بازگشایی ' + p.title }">بازگشایی</button>
                  <button v-if="p.status !== 2" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act(p, 'close')">بستن قطعی</button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <PromptModal
      v-if="dialog && dialog.kind === 'reopen'"
      :fields="[{ key: 'reason', label: 'علت بازگشایی دوره', type: 'textarea', required: true }]"
      :title="dialog.title"
      @cancel="dialog = null"
      @confirm="onConfirm" />
    <PromptModal
      v-if="dialog && dialog.kind === 'close-year'"
      :fields="[
        { key: 'override', label: 'ادامه با وجود مغایرت‌گیری دفتر و زیرحساب‌ها', type: 'select', options: [{ value: '0', text: 'خیر؛ مغایرت باید صفر باشد' }, { value: '1', text: 'بله، با ثبت علت' }], value: '0' },
        { key: 'reason', label: 'علت (فقط هنگام ادامه با مغایرت)', type: 'textarea' },
      ]"
      :title="dialog.title"
      confirm-text="بستن سال"
      hint="همه دوره‌ها باید قطعی شده باشند. سند بستن حساب‌های موقت و اختتامیه صادر و سال بعد با سند افتتاحیه آغاز می‌شود."
      @cancel="dialog = null"
      @confirm="onConfirm" />
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
