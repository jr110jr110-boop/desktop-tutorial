<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import JDate from "@/components/pc/JDate.vue";
import WorkOrderOfContract from "@/components/pc/WorkOrderOfContract.vue";
import { deliverableAction, draftMdr, getDeliverables, saveDeliverable } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok } from "@/composables/inventory";
import MdrPrint from "@/components/pc/print/MdrPrint.vue";
import { contractLabel, issueStates, pcContractId, pct, usePcPrint } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// MDR: the master document register of a work order (all deliverables); MDL: the part that is submitted to the client (ClientSubmittal).
const props = defineProps({ mdl: Boolean });
const rows = ref<any[]>([]);
const workOrderId = ref<number | null>(null);
const woCode = ref("");
const state = ref<number | null>(null);
const text = ref("");
const form = ref<any>(null);
const rev = ref<any>(null);
const busy = ref(false);
const { job, run } = usePcPrint();
const canDraft = hasRole(ProjectControlRoles.mdrDraft);
const canEdit = hasRole(ProjectControlRoles.dcc);
const canApprove = hasRole(ProjectControlRoles.internalApprove);

async function load() {
  if (!pcContractId.value) return;
  const [d, e] = await getDeliverables({ ContractId: pcContractId.value, WorkOrderId: workOrderId.value, ClientOnly: props.mdl || null, IssueState: state.value });
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
const list = computed(() => {
  const t = text.value.trim().toLowerCase();
  return rows.value.filter((r) => (t ? `${r.documentNumber} ${r.title} ${r.discipline}`.toLowerCase().includes(t) : true));
});
const approved = computed(() => rows.value.filter((r) => r.issueState === 3).length);
async function draft() {
  busy.value = true;
  const [d, e] = await draftMdr(pcContractId.value!, workOrderId.value!);
  busy.value = false;
  if (e) return fail(e);
  ok(d.message || d.linesDrafted + " ردیف ساخته شد.");
  load();
}
async function save() {
  busy.value = true;
  const [, e] = await saveDeliverable({ ...form.value, contractId: pcContractId.value });
  busy.value = false;
  if (e) return fail(e);
  ok("ذخیره شد.");
  form.value = null;
  load();
}
async function act(r: any, action: string, extra: any = {}) {
  busy.value = true;
  const [, e] = await deliverableAction({ id: r.id, action, ...extra });
  busy.value = false;
  if (e) return fail(e);
  ok("انجام شد.");
  rev.value = null;
  load();
}
const badge = (s: number) => (s === 3 ? "ok" : s === 0 ? "info" : "warn");
const blank = () => ({ workOrderId: workOrderId.value, documentNumber: "", title: "", clientSubmittal: props.mdl, standardSource: "", discipline: "", documentType: "", required: true, plannedDate: null });
const doPrint = () => run("mdr", list.value);
onMounted(load);
watch([pcContractId, () => props.mdl], () => { workOrderId.value = null; load(); });
defineExpose({ rows });
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader :title="mdl ? 'فهرست مدارک کارفرما (MDL)' : 'فهرست مدارک اصلی (MDR)'" :subtitle="contractLabel(pcContractId)" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title screen-only">{{ mdl ? "فهرست مدارک ارسالی به کارفرما (MDL)" : "فهرست اصلی مدارک مهندسی (MDR)" }}</h1>
          <span class="inv-muted d-print-none">{{ mdl ? "فقط مدارکی که برای کارفرما ارسال می‌شوند؛ ارسال با ترنسمیتال و تایید با کد پاسخ کارفرما." : "همه مدارک؛ MDL بخشی از آن است که برای کارفرما ارسال می‌شود." }}</span>
        </div>
        <div class="d-flex flex-wrap gap-2 d-print-none"><ContractSelect @change="() => { workOrderId = null; }" /><button class="btn btn-sm btn-outline-secondary" @click="doPrint">چاپ</button></div>
      </div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <div class="inv-filters mb-2 d-print-none">
          <WorkOrderOfContract v-model="workOrderId" :contract-id="pcContractId" @change="load" @select="(w: any) => (woCode = w?.code || '')" />
          <select v-model="state" class="form-select form-select-sm" @change="load"><option :value="null">همه وضعیت‌ها</option><option v-for="(n, k) in issueStates" :key="k" :value="Number(k)">{{ n }}</option></select>
          <input v-model="text" class="form-control form-control-sm" placeholder="شماره یا عنوان" />
          <button v-if="canDraft && !mdl" :disabled="busy || !workOrderId" class="btn btn-sm btn-outline-primary" title="دستور ساخت را انتخاب کنید" @click="draft">ساخت پیش‌نویس از قالب</button>
          <button v-if="canEdit" class="btn btn-sm btn-primary" @click="form = blank()">سند جدید</button>
        </div>
        <p class="mb-2">{{ rows.length }} سند — {{ approved }} تایید شده ({{ pct(rows.length ? (approved * 100) / rows.length : 0, 0) }})</p>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>شماره سند</th><th>عنوان</th><th>رشته</th><th>نوع</th><th>الزامی</th><th>تاریخ برنامه</th><th>وضعیت</th><th>ویرایش</th><th>ترنسمیتال</th><th v-if="mdl">پاسخ کارفرما</th><th class="d-print-none"></th></tr></thead>
            <tbody>
              <tr v-if="!list.length"><td :colspan="11" class="text-center inv-muted">سندی نیست.</td></tr>
              <tr v-for="r in list" :key="r.id">
                <td class="inv-ltr">{{ r.documentNumber }}</td><td>{{ r.title }}<span v-if="!mdl && r.clientSubmittal" class="inv-badge info ms-1">MDL</span></td><td>{{ r.discipline }}</td><td>{{ r.documentType }}</td><td>{{ r.required ? "بله" : "خیر" }}</td><td>{{ jalali(r.plannedDate) }}</td>
                <td><span :class="badge(r.issueState)" class="inv-badge">{{ issueStates[r.issueState] }}</span></td>
                <td class="inv-ltr">{{ r.revision }}</td>
                <td class="small"><span class="inv-ltr">{{ r.lastTransmittalNumber }}</span> {{ jalali(r.lastTransmittalDate) }}</td>
                <td v-if="mdl"><b class="inv-ltr">{{ r.clientResponseCode }}</b> {{ jalali(r.clientResponseDate) }}</td>
                <td class="d-print-none text-nowrap">
                  <template v-if="canEdit">
                    <button v-if="!r.clientSubmittal && r.issueState === 0" class="btn btn-sm btn-outline-primary me-1" @click="rev = { r, action: 'first-issue', revision: r.revision || 'A' }">ارسال اول</button>
                    <button v-if="!r.clientSubmittal && r.issueState === 1" class="btn btn-sm btn-outline-primary me-1" @click="rev = { r, action: 'second-issue', revision: r.revision }">ارسال دوم</button>
                    <button class="btn btn-sm btn-outline-secondary me-1" @click="rev = { r, action: 'link-document', revision: r.revision, controlledDocumentId: r.controlledDocumentId }">پیوند سند</button>
                    <button v-if="r.issueState === 0" class="btn btn-sm btn-outline-secondary me-1" @click="form = { ...r }">ویرایش</button>
                  </template>
                  <button v-if="canApprove && !r.clientSubmittal && r.issueState > 0 && r.issueState < 3" class="btn btn-sm btn-outline-success" @click="act(r, 'internal-approve')">تایید داخلی</button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>
    </div>

    <MdrPrint v-if="job?.kind === 'mdr'" :rows="job.data" :contract-id="pcContractId" :mdl="mdl" :work-order-code="woCode" />
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h3 class="h6">{{ form.id ? "ویرایش سند" : "سند جدید" }}</h3>
        <div class="row g-2">
          <div class="col-6"><label class="form-label small mb-0">شماره سند</label><input v-model="form.documentNumber" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-6"><label class="form-label small mb-0">دستور ساخت</label><WorkOrderOfContract v-model="form.workOrderId" :contract-id="pcContractId" placeholder="بدون دستور ساخت" /></div>
          <div class="col-12"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-6"><label class="form-label small mb-0">رشته</label><input v-model="form.discipline" class="form-control form-control-sm" /></div>
          <div class="col-6"><label class="form-label small mb-0">نوع سند</label><input v-model="form.documentType" class="form-control form-control-sm" /></div>
          <div class="col-6"><label class="form-label small mb-0">منبع استاندارد</label><input v-model="form.standardSource" class="form-control form-control-sm" /></div>
          <div class="col-6"><label class="form-label small mb-0">تاریخ برنامه</label><JDate v-model="form.plannedDate" /></div>
          <div class="col-6"><label class="form-check"><input v-model="form.clientSubmittal" :disabled="form.issueState > 0" class="form-check-input" type="checkbox" /> ارسال به کارفرما (MDL)</label></div>
          <div class="col-6"><label class="form-check"><input v-model="form.required" class="form-check-input" type="checkbox" /> الزامی</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-secondary" @click="form = null">انصراف</button><button :disabled="busy || !form.documentNumber || !form.title" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
    <div v-if="rev" class="inv-modal-backdrop" @click.self="rev = null">
      <div class="inv-modal">
        <h3 class="h6">{{ rev.action === 'link-document' ? "پیوند به سند کنترل‌شده" : rev.action === 'first-issue' ? "ارسال اول" : "ارسال دوم" }} — <span class="inv-ltr">{{ rev.r.documentNumber }}</span></h3>
        <label class="form-label small mb-0">ویرایش (Revision)</label><input v-model="rev.revision" class="form-control form-control-sm inv-ltr mb-2" />
        <template v-if="rev.action === 'link-document'"><label class="form-label small mb-0">شناسه سند کنترل‌شده (DCC)</label><input v-model.number="rev.controlledDocumentId" class="form-control form-control-sm" type="number" /></template>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-secondary" @click="rev = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="act(rev.r, rev.action, { revision: rev.revision, controlledDocumentId: rev.controlledDocumentId || null })">ثبت</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
