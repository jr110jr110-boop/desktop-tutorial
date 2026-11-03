<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import WorkOrderOfContract from "@/components/pc/WorkOrderOfContract.vue";
import { approveFinalBook, generateFinalBook, getFinalBooks } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok } from "@/composables/inventory";
import { contractLabel, pcContractId } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Data book: the company variant holds everything, the client variant only what the contract requires. The issue becomes a controlled document (code + revision).
const rows = ref<any[]>([]);
const workOrderId = ref<number | null>(null);
const f = ref({ workOrderId: null as number | null, variant: "company" as "company" | "client", allowIncomplete: false });
const busy = ref(false);
const can = hasRole(ProjectControlRoles.finalBooks);
const isPmo = hasRole(ProjectControlRoles.pmo);

async function load() {
  if (!pcContractId.value) return;
  const [d, e] = await getFinalBooks({ ContractId: pcContractId.value, WorkOrderId: workOrderId.value });
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
const revisionStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "در بازبینی", 2: "تأییدشده", 3: "جاری", 4: "منسوخ", 5: "زمان‌بندی‌شده" };
// M1: the preparer does not approve the revision (the API refuses it); PMO or QC does, then it becomes the revision in force
async function approve(r: any) {
  const [, error] = await approveFinalBook(r.reportIssueId);
  if (error) return fail(error);
  ok("نسخه تأیید شد و جاری شد.");
  load();
}
async function generate() {
  busy.value = true;
  const [b, e] = await generateFinalBook(f.value.workOrderId!, f.value.variant, f.value.allowIncomplete);
  busy.value = false;
  if (e) return fail(e);
  ok(`کتاب نهایی ${b.documentCode || b.number} ویرایش ${b.revision || ""} صادر شد.`);
  load();
}
onMounted(load);
watch(pcContractId, () => { workOrderId.value = null; f.value.workOrderId = null; load(); });
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="کتاب نهایی (دیتابوک)" :subtitle="contractLabel(pcContractId)" />
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title screen-only">کتاب نهایی (دیتابوک)</h1><div class="d-print-none"><ContractSelect /></div></div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <div v-if="can" class="border rounded p-3 mb-3 d-print-none">
          <b class="small">صدور کتاب نهایی</b>
          <div class="row g-2 align-items-end mt-1">
            <div class="col-md-3"><label class="form-label small mb-0">دستور ساخت</label><WorkOrderOfContract v-model="f.workOrderId" :contract-id="pcContractId" placeholder="انتخاب کنید" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">نسخه</label><select v-model="f.variant" class="form-select form-select-sm"><option value="company">شرکت (کامل)</option><option value="client">کارفرما</option></select></div>
            <div class="col-md-3"><label v-if="isPmo" class="form-check"><input v-model="f.allowIncomplete" class="form-check-input" type="checkbox" /> صدور موقت با موارد ناقص (فقط PMO)</label></div>
            <div class="col-md-3"><button :disabled="busy || !f.workOrderId" class="btn btn-sm btn-primary" @click="generate">صدور کتاب نهایی</button></div>
          </div>
          <p class="small inv-muted mb-0 mt-2">اگر موردی الزامی ناقص باشد صدور رد می‌شود؛ صدور ناقص فقط با تایید معاونت PMO مجاز است و تعداد موارد ناقص در کتاب ثبت می‌شود.</p>
        </div>
        <div class="inv-filters mb-2 d-print-none"><WorkOrderOfContract v-model="workOrderId" :contract-id="pcContractId" @change="load" /></div>
        <table class="table table-sm">
          <thead><tr><th>شماره صدور</th><th>نسخه</th><th>دستور ساخت</th><th>کد سند کنترل‌شده</th><th>ویرایش</th><th>اقلام</th><th>موارد ناقص</th><th>تاریخ</th><th>هش</th><th>وضعیت ویرایش</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="11" class="text-center inv-muted">کتاب نهایی صادر نشده است.</td></tr>
            <tr v-for="r in rows" :key="r.reportIssueId">
              <td class="inv-ltr">{{ r.number }}</td><td>{{ r.variant === "client" ? "کارفرما" : "شرکت" }}</td><td class="inv-ltr">{{ r.workOrderCode }}</td><td class="inv-ltr">{{ r.documentCode || "—" }}</td><td class="inv-ltr">{{ r.revision }}</td>
              <td>{{ r.entryCount }}</td><td><span v-if="r.missingCount" class="inv-badge bad">{{ r.missingCount }}</span><span v-else class="inv-badge ok">کامل</span></td><td>{{ jalali(r.generatedAt) }}</td><td class="inv-ltr small">{{ (r.hash || "").slice(0, 12) }}</td>
              <td><span v-if="r.revisionStatus !== null && r.revisionStatus !== undefined" :class="r.revisionStatus === 3 ? 'ok' : r.revisionStatus === 1 ? 'warn' : ''" class="inv-badge">{{ revisionStatuses[r.revisionStatus] }}</span></td>
              <td><button v-if="can && r.revisionStatus === 1" class="btn btn-sm btn-outline-success" title="نسخه‌ی جاری تا تأیید نفر دوم معتبر می‌ماند" @click="approve(r)">تأیید نسخه</button></td>
            </tr>
          </tbody>
        </table>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
