<script setup lang="ts">
import { onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import { getAcceptances, proposeAcceptance, stepAcceptance, rowsOf } from "@/api/AuditAPIs";
import { fail, ok, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { acceptanceStatuses } from "@/composables/audit";

// Accepted deviations: a time-boxed acceptance of one rule and object. QA proposes, PMO agrees, the CEO accepts (three distinct persons, never an AI seat).
// While valid the deviation is shown as accepted in the reports (no finding, no alert); after the expiry date it becomes a finding again.
const rows = ref<any[]>([]);
const form = ref<any>({ ruleCode: "", objectKey: "", objectNumber: "", reason: "", expiresOn: "" });
const note = ref("");
const canPropose = hasRole(["QA"]);
const canPmo = hasRole(["PMO"]);
const canCeo = hasRole(["CEO"]);

async function load() {
  const [d, e] = await getAcceptances();
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
async function propose() {
  const [, e] = await proposeAcceptance({ ...form.value, objectNumber: form.value.objectNumber || null, expiresOn: form.value.expiresOn || null });
  if (e) return fail(e);
  ok("پیشنهاد پذیرش ثبت شد.");
  form.value = { ruleCode: "", objectKey: "", objectNumber: "", reason: "", expiresOn: "" };
  await load();
}
async function step(id: number, s: string, refuse: boolean) {
  const [, e] = await stepAcceptance(id, s, refuse, note.value);
  if (e) return fail(e);
  ok(refuse ? "رد شد." : "ثبت شد.");
  note.value = "";
  await load();
}
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="انحراف‌های پذیرفته‌شده‌ی موقت" subtitle="ممیزی داخلی" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">انحراف‌های پذیرفته‌شده‌ی موقت</h1>
        <button class="btn btn-sm btn-outline-secondary d-print-none" @click="printPage">چاپ</button>
      </div>
      <div v-if="canPropose" class="row g-2 mb-3 d-print-none">
        <div class="col-md-2"><input v-model="form.ruleCode" class="form-control form-control-sm inv-ltr" maxlength="30" placeholder="کد قاعده" /></div>
        <div class="col-md-3"><input v-model="form.objectKey" class="form-control form-control-sm inv-ltr" maxlength="200" placeholder="کلید شیء" /></div>
        <div class="col-md-2"><input v-model="form.objectNumber" class="form-control form-control-sm" maxlength="80" placeholder="شماره‌ی شیء" /></div>
        <div class="col-md-2"><KsDatePicker v-model="form.expiresOn" element-tag="aus-expires" placeholder="تا تاریخ" simple-placeholder /></div>
        <div class="col-md-3"><input v-model="form.reason" class="form-control form-control-sm" maxlength="1000" placeholder="دلیل پذیرش (دست‌کم ۱۵ نویسه)" /></div>
        <div class="col-12"><button class="btn btn-sm btn-primary" @click="propose">پیشنهاد پذیرش</button></div>
      </div>
      <input v-if="canPmo || canCeo" v-model="note" class="form-control form-control-sm mb-2 d-print-none" maxlength="1000" placeholder="یادداشت (برای رد، دلیل لازم است)" />
      <table class="table table-sm table-hover">
        <thead><tr><th>قاعده</th><th>شیء</th><th>دلیل</th><th>تا تاریخ</th><th>وضعیت</th><th>اعتبار</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="7">موردی ثبت نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id">
            <td class="inv-ltr small">{{ r.ruleCode }}<br />{{ r.ruleTitle }}</td><td class="small">{{ r.objectNumber || r.objectKey }}</td><td class="small">{{ r.reason }}<span v-if="r.refusalReason"> (رد: {{ r.refusalReason }})</span></td>
            <td>{{ r.expiresOnText }}</td><td>{{ acceptanceStatuses[r.statusValue] }}</td><td>{{ r.valid ? "معتبر" : "-" }}</td>
            <td class="d-print-none">
              <template v-if="r.statusValue === 0 && canPmo"><button class="btn btn-sm btn-success me-1" @click="step(r.id, 'pmo', false)">تایید PMO</button><button class="btn btn-sm btn-outline-danger" @click="step(r.id, 'pmo', true)">رد</button></template>
              <template v-if="r.statusValue === 1 && canCeo"><button class="btn btn-sm btn-success me-1" @click="step(r.id, 'ceo', false)">پذیرش مدیرعامل</button><button class="btn btn-sm btn-outline-danger" @click="step(r.id, 'ceo', true)">رد</button></template>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
