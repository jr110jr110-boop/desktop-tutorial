<script setup lang="ts">
import { nextTick, onMounted, ref } from "vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import CloseChecklistPrint from "@/components/fn/print/CloseChecklistPrint.vue";
import { getCloseChecklist, overrideChecklist } from "@/api/FnAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { printSheet, yesNo } from "@/composables/fn";
import { closeStatuses, usePeriods } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Period close checklist: the controls the finance manager looks at before locking a period. Red items can be passed with a written reason (finance manager).
const { periods, load: loadPeriods, label } = usePeriods();
const periodId = ref<number | null>(null);
const data = ref<any>(null);
const dialog = ref(false);
const printing = ref(false);
const busy = ref(false);
const canOverride = hasRole(FnRoles.managers);

async function load() {
  data.value = null;
  if (!periodId.value) return;
  const [d, error] = await getCloseChecklist(periodId.value);
  if (error) return fail(error);
  data.value = d;
}

async function override(v: any) {
  dialog.value = false;
  busy.value = true;
  const [d, error] = await overrideChecklist(periodId.value!, v.reason);
  busy.value = false;
  if (error) return fail(error);
  ok("عبور از کنترل با علت ثبت شد.");
  data.value = d;
}

async function print() {
  printing.value = true;
  await nextTick();
  printSheet();
}

onMounted(async () => {
  await loadPeriods();
  const open = periods.value.find((p) => p.status === 0) || periods.value[0];
  if (open) {
    periodId.value = open.id;
    load();
  }
});
</script>

<template>
  <div class="inv-page">
    <CloseChecklistPrint v-if="printing && data" :data="data" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">فهرست کنترل بستن دوره</h1>
        <div class="d-flex gap-2">
          <button :disabled="!data" class="btn btn-sm btn-outline-secondary" @click="print">چاپ</button>
          <button v-if="canOverride && data && data.redCount > 0 && !data.overridden" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="dialog = true">عبور از موارد قرمز با ثبت علت</button>
        </div>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="periodId" class="form-select form-select-sm" @change="load"><option :value="null">دوره...</option><option v-for="p in periods" :key="p.id" :value="p.id">{{ label(p) }}</option></select>
        <button class="btn btn-sm btn-secondary" @click="load">نمایش</button>
      </div>
      <template v-if="data">
        <p>
          <span :class="data.allGreen ? 'ok' : 'bad'" class="inv-badge">{{ data.allGreen ? "همه موارد سبز" : data.redCount + " مورد قرمز" }}</span>
          <span class="inv-muted me-2">الزام کنترل برای قفل دوره: {{ yesNo(data.enforced) }} - قفل دوره: {{ data.canLock ? "مجاز" : "مجاز نیست" }}</span>
        </p>
        <p v-if="data.overridden" class="alert alert-warning py-2">با علت از کنترل عبور شده است: {{ data.overrideReason }} ({{ jalali(data.overriddenAt, true) }})</p>
        <table class="table table-sm">
          <thead><tr><th>مورد کنترل</th><th>وضعیت</th><th>تعداد</th><th>توضیح</th></tr></thead>
          <tbody>
            <tr v-for="it in data.items" :key="it.key" :class="{ 'table-danger': it.status === 'Red' }">
              <td>{{ it.title }}</td>
              <td><span :class="it.status === 'Green' ? 'ok' : it.status === 'Red' ? 'bad' : ''" class="inv-badge">{{ closeStatuses[it.status] || it.status }}</span></td>
              <td>{{ it.count }}</td><td>{{ it.detail }}</td>
            </tr>
          </tbody>
        </table>
      </template>
      <p v-else class="inv-muted">دوره را انتخاب کنید.</p>
    </div>

    <PromptModal v-if="dialog" :fields="[{ key: 'reason', label: 'علت عبور از موارد قرمز', type: 'textarea', required: true }]" confirm-text="ثبت عبور" title="عبور از کنترل بستن دوره" @cancel="dialog = false" @confirm="override" />
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
