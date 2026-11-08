<script setup lang="ts">
import { onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import { getAudits, getAudit, createAudit, auditAction, saveCheck, createManualFinding, rowsOf } from "@/api/AuditAPIs";
import { fail, ok, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { AuditRoles, auditStatuses, checkResults, severities } from "@/composables/audit";

// Process audits by human auditors (lead auditor and auditee are different persons). The checklist comes from the rule catalogue; a deviation found goes into the same findings flow.
const rows = ref<any[]>([]);
const open = ref<any>(null);
const form = ref<any>({ processCode: "", title: "", plannedDate: "", leadAuditorUserId: null, auditeeUserId: null, auditeeUnit: "" });
const summary = ref("");
const nf = ref<any>({ title: "", description: "", standard: "", clause: "", severity: 2, ownerUserId: null, checkId: null });
const canPlan = hasRole(["QA", "PMO"]);

async function load() {
  const [d, e] = await getAudits(60);
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
async function show(id: number) {
  const [d, e] = await getAudit(id);
  if (e) return fail(e);
  open.value = d;
}
async function create() {
  const [d, e] = await createAudit({ ...form.value, plannedDate: form.value.plannedDate || null });
  if (e) return fail(e);
  ok("ممیزی برنامه‌ریزی شد.");
  await load();
  open.value = d;
}
async function act(action: string) {
  const [d, e] = await auditAction(open.value.id, action, summary.value);
  if (e) return fail(e);
  open.value = d;
  ok("ثبت شد.");
  await load();
}
async function check(c: any, result: number) {
  const [, e] = await saveCheck(c.id, result, c.note);
  if (e) return fail(e);
  await show(open.value.id);
}
async function finding() {
  const [, e] = await createManualFinding({ ...nf.value, auditId: open.value.id });
  if (e) return fail(e);
  ok("یافته ثبت شد.");
  nf.value.title = ""; nf.value.description = "";
  await show(open.value.id);
}
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="ممیزی فرایند" subtitle="ممیزی انسانی با پشتیبانی ممیز هوشمند" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">ممیزی فرایند</h1>
        <button class="btn btn-sm btn-outline-secondary d-print-none" @click="printPage">چاپ</button>
      </div>
      <div v-if="canPlan" class="row g-2 mb-3 d-print-none">
        <div class="col-md-2"><input v-model="form.processCode" class="form-control form-control-sm inv-ltr" placeholder="کد فرایند (مثل QUALITY)" /></div>
        <div class="col-md-3"><input v-model="form.title" class="form-control form-control-sm" placeholder="عنوان" /></div>
        <div class="col-md-2"><KsDatePicker v-model="form.plannedDate" element-tag="aus-planned" placeholder="تاریخ برنامه" simple-placeholder /></div>
        <div class="col-md-1"><input v-model.number="form.leadAuditorUserId" class="form-control form-control-sm inv-ltr" placeholder="شناسه‌ی ممیز" /></div>
        <div class="col-md-1"><input v-model.number="form.auditeeUserId" class="form-control form-control-sm inv-ltr" placeholder="شناسه‌ی ممیزی‌شونده" /></div>
        <div class="col-md-2"><input v-model="form.auditeeUnit" class="form-control form-control-sm" placeholder="واحد" /></div>
        <div class="col-md-1"><button class="btn btn-sm btn-primary w-100" @click="create">برنامه‌ریزی</button></div>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>عنوان</th><th>فرایند</th><th>تاریخ</th><th>ممیز</th><th>ممیزی‌شونده</th><th>وضعیت</th><th>بررسی‌ها</th><th>نامنطبق</th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="9">ممیزی‌ای ثبت نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id" style="cursor: pointer" @click="show(r.id)">
            <td class="inv-ltr">{{ r.number }}</td><td>{{ r.title }}</td><td>{{ r.processTitle }}</td><td>{{ r.plannedDateText }}</td><td>{{ r.leadAuditorName }}</td><td>{{ r.auditeeName || r.auditeeUnit }}</td><td>{{ auditStatuses[r.statusValue] }}</td><td>{{ r.checks }}</td><td>{{ r.nonConform }}</td>
          </tr>
        </tbody>
      </table>
      <div v-if="open" class="mt-3">
        <h2 class="h6">{{ open.number }} - {{ open.title }}</h2>
        <table class="table table-sm">
          <thead><tr><th>قاعده</th><th>بند</th><th>پرسش</th><th>یافته‌ی باز</th><th>نتیجه</th><th>توضیح</th></tr></thead>
          <tbody>
            <tr v-for="c in open.items" :key="c.id">
              <td class="inv-ltr">{{ c.ruleCode }}</td><td class="inv-ltr small">{{ c.standard }} {{ c.clause }}</td><td>{{ c.question }}</td><td>{{ c.openFindings }}</td>
              <td>
                <span v-if="!open.allowed?.includes('check')">{{ checkResults[c.result] }}</span>
                <select v-else :value="c.result" class="form-select form-select-sm" @change="check(c, Number(($event.target as HTMLSelectElement).value))"><option v-for="(t, k) in checkResults" :key="k" :value="Number(k)">{{ t }}</option></select>
              </td>
              <td><input v-model="c.note" class="form-control form-control-sm" :disabled="!open.allowed?.includes('check')" /></td>
            </tr>
          </tbody>
        </table>
        <div v-if="open.allowed?.includes('finding')" class="border rounded p-2 mb-2 d-print-none">
          <h3 class="h6">ثبت یافته‌ی ممیز</h3>
          <div class="row g-2">
            <div class="col-md-3"><input v-model="nf.title" class="form-control form-control-sm" placeholder="عنوان" /></div>
            <div class="col-md-4"><input v-model="nf.description" class="form-control form-control-sm" placeholder="شرح" /></div>
            <div class="col-md-1"><input v-model="nf.standard" class="form-control form-control-sm inv-ltr" placeholder="استاندارد" /></div>
            <div class="col-md-1"><input v-model="nf.clause" class="form-control form-control-sm inv-ltr" placeholder="بند" /></div>
            <div class="col-md-1"><select v-model.number="nf.severity" class="form-select form-select-sm"><option v-for="(t, k) in severities" :key="k" :value="Number(k)">{{ t }}</option></select></div>
            <div class="col-md-1"><input v-model.number="nf.ownerUserId" class="form-control form-control-sm inv-ltr" placeholder="شناسه‌ی مسئول" /></div>
            <div class="col-md-1"><button class="btn btn-sm btn-primary w-100" @click="finding">ثبت</button></div>
          </div>
        </div>
        <div class="d-print-none d-flex gap-2 align-items-center">
          <input v-if="open.allowed?.includes('complete')" v-model="summary" class="form-control form-control-sm" placeholder="خلاصه‌ی نتیجه‌ی ممیزی" />
          <button v-if="open.allowed?.includes('start')" class="btn btn-sm btn-primary" @click="act('start')">شروع</button>
          <button v-if="open.allowed?.includes('complete')" class="btn btn-sm btn-success" @click="act('complete')">اتمام</button>
          <button v-if="open.allowed?.includes('cancel')" class="btn btn-sm btn-outline-danger" @click="act('cancel')">لغو</button>
        </div>
        <p v-if="open.summary" class="mt-2">{{ open.summary }}</p>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
