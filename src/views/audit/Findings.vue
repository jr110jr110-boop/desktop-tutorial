<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getFindings, getStatus, rowsOf } from "@/api/AuditAPIs";
import { fail, printPage } from "@/composables/inventory";
import { findingStatuses, findingStatusClass, severities, severityClass } from "@/composables/audit";

// Findings of the internal auditor: every deviation is tracked one by one (no finding is dropped or closed by severity).
const router = useRouter();
const rows = ref<any[]>([]);
const status = ref<any>(null);
const f = ref<any>({ Status: "", Severity: "", Text: "", OpenOnly: true, Overdue: false, Mine: false });

async function load() {
  const [d, e] = await getFindings({ Status: f.value.Status, Severity: f.value.Severity, Text: f.value.Text, OpenOnly: f.value.OpenOnly || undefined, Overdue: f.value.Overdue || undefined, Mine: f.value.Mine || undefined, Limit: 500 });
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
onMounted(async () => {
  const [s] = await getStatus();
  status.value = s;
  await load();
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="یافته‌های ممیزی داخلی" subtitle="هر انحراف به‌صورت جداگانه پیگیری می‌شود" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">یافته‌های ممیزی داخلی</h1>
        <div class="d-flex gap-2 d-print-none">
          <router-link :to="{ name: 'AuditReports' }" class="btn btn-sm btn-outline-secondary">گزارش‌ها</router-link>
          <router-link :to="{ name: 'AuditRules' }" class="btn btn-sm btn-outline-secondary">فهرست قواعد</router-link>
          <router-link :to="{ name: 'AuditRuns' }" class="btn btn-sm btn-outline-secondary">اجراها</router-link>
          <router-link :to="{ name: 'AuditPrograms' }" class="btn btn-sm btn-outline-secondary">برنامه‌ی ممیزی</router-link>
          <router-link :to="{ name: 'AuditProcessAudits' }" class="btn btn-sm btn-outline-secondary">ممیزی فرایند</router-link>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <div v-if="status" class="row text-center mb-3">
        <div class="col"><div class="fs-4">{{ status.open }}</div><small>باز</small></div>
        <div class="col"><div class="fs-4 text-danger">{{ status.openMajor }}</div><small>عمده</small></div>
        <div class="col"><div class="fs-4">{{ status.openMinor }}</div><small>جزئی</small></div>
        <div class="col"><div class="fs-4">{{ status.openObservation }}</div><small>مشاهده</small></div>
        <div class="col"><div class="fs-4 text-danger">{{ status.overdue }}</div><small>گذشته از مهلت</small></div>
        <div class="col"><div class="fs-4">{{ status.waitingQa }} / {{ status.waitingPmo }} / {{ status.waitingCeo }}</div><small>منتظر QA / PMO / مدیرعامل</small></div>
        <div class="col"><div class="fs-4">{{ status.myTurn }}</div><small>نوبت من</small></div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <select v-model="f.Status" class="form-select form-select-sm" @change="load">
          <option value="">همه وضعیت‌ها</option>
          <option v-for="(t, k) in findingStatuses" :key="k" :value="String(k)">{{ t }}</option>
        </select>
        <select v-model="f.Severity" class="form-select form-select-sm" @change="load">
          <option value="">همه شدت‌ها</option>
          <option v-for="(t, k) in severities" :key="k" :value="String(k)">{{ t }}</option>
        </select>
        <input v-model="f.Text" class="form-control form-control-sm" placeholder="شماره، عنوان یا کد قاعده" @keyup.enter="load" />
        <label class="small"><input v-model="f.OpenOnly" type="checkbox" @change="load" /> فقط باز</label>
        <label class="small"><input v-model="f.Overdue" type="checkbox" @change="load" /> گذشته از مهلت</label>
        <label class="small"><input v-model="f.Mine" type="checkbox" @change="load" /> مربوط به من</label>
        <button class="btn btn-sm btn-outline-secondary" @click="load">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>عنوان</th><th>بند</th><th>فرایند</th><th>شدت</th><th>وضعیت</th><th>مسئول</th><th>مهلت</th><th>سن (روز)</th><th>تکرار</th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="10">یافته‌ای وجود ندارد.</td></tr>
          <tr v-for="r in rows" :key="r.id" style="cursor: pointer" @click="router.push({ name: 'AuditFinding', params: { id: r.id } })">
            <td class="inv-ltr">{{ r.number }}</td>
            <td>{{ r.title }}<div v-if="r.objectNumber" class="small inv-muted inv-ltr">{{ r.objectNumber }}</div></td>
            <td class="inv-ltr small">{{ r.standard }} {{ r.clause }}</td>
            <td>{{ r.processTitle }}</td>
            <td><span class="inv-badge" :class="severityClass[r.severityValue]">{{ severities[r.severityValue] }}</span></td>
            <td><span class="inv-badge" :class="findingStatusClass[r.statusValue]">{{ findingStatuses[r.statusValue] }}</span></td>
            <td>{{ r.ownerName || r.ownerRole }}</td>
            <td :class="r.overdue ? 'text-danger' : ''">{{ r.dueDateText }}</td>
            <td>{{ r.ageDays }}</td>
            <td>{{ r.recurrenceCount || "" }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
