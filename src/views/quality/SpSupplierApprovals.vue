<script setup lang="ts">
import { onMounted, ref } from "vue";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import QPager from "@/components/quality/QPager.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import DocAttach from "@/components/quality/DocAttach.vue";
import { call, rowsOf } from "@/api/ApiClient";
import { getSpSupplierApprovals, getSpTypes, saveSpSupplierApproval, spSupplierAction } from "@/api/QualityAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { spApprovalStatuses, usePaged } from "@/composables/quality";
import { hasRole } from "@/composables/roles";

// Package C: the scope of a supplier for a special process (heat treatment, coating, plating ...). Commercial/QA/QC register the audit or accreditation evidence; QA approves, never the registrant. A subcontracted
// special step needs this approval, and the receipt of the work (QC accept) needs the supplier's chart and certificate as well.
const filters = ref({ Status: null as number | null, TypeId: null as number | null });
const list = usePaged(getSpSupplierApprovals, filters);
const types = ref<any[]>([]);
const providers = ref<any[]>([]);
const form = ref<any>(null);
const busy = ref(false);
const dialog = ref<null | { id: number; action: string; title: string }>(null);
const canWrite = hasRole(["QA", "QC", "PMO", "COMMERCIAL"]);
const blank = () => ({ id: null, providerId: null as number | null, typeId: null as number | null, validFrom: today(), validTo: "", accreditationNumber: "", evidenceDocumentId: null as string | null, auditDate: "", note: "" });
async function save() {
  const f = form.value;
  busy.value = true;
  const [, error] = await saveSpSupplierApproval({ ...f, auditDate: f.auditDate || null });
  busy.value = false;
  if (error) return fail(error);
  ok("تایید تامین‌کننده ثبت شد؛ تایید QA لازم است.");
  form.value = null;
  list.load();
}
async function act(id: number, action: string, note?: string) {
  busy.value = true;
  const [, error] = await spSupplierAction(id, action, note);
  busy.value = false;
  dialog.value = null;
  if (error) return fail(error);
  ok("انجام شد.");
  list.load();
}
onMounted(async () => {
  list.load();
  const [t] = await getSpTypes();
  types.value = (Array.isArray(t) ? t : []).filter((x: any) => !x.existingControl);
  const [p] = await call("get", "Provider");
  providers.value = Array.isArray(p) ? p : rowsOf(p);
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">تایید پیمانکار برای فرایند ویژه</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = blank()">تایید جدید</button>
      </div>
      <div v-if="form" class="border rounded p-2 mb-3">
        <div class="row g-2">
          <div class="col-md-3"><label class="form-label small mb-0">پیمانکار *</label><select v-model="form.providerId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="p in providers" :key="p.id" :value="p.id">{{ p.name }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">فرایند *</label><select v-model="form.typeId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="t in types" :key="t.id" :value="t.id">{{ t.title }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">از *</label><KsDatePicker v-model="form.validFrom" element-tag="spa-valid-from" placeholder="تاریخ" simple-placeholder /></div>
          <div class="col-md-2"><label class="form-label small mb-0">تا * (حداکثر سه سال)</label><KsDatePicker v-model="form.validTo" element-tag="spa-valid-to" placeholder="تاریخ" simple-placeholder /></div>
          <div class="col-md-2"><label class="form-label small mb-0">تاریخ ممیزی</label><KsDatePicker v-model="form.auditDate" element-tag="spa-audit" placeholder="تاریخ" simple-placeholder /></div>
          <div class="col-md-3"><label class="form-label small mb-0">شماره اعتبارنامه</label><input v-model="form.accreditationNumber" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">مدرک ممیزی / اعتبارنامه (فایل یا شماره)</label><DocAttach v-model="form.evidenceDocumentId" title="مدرک تایید پیمانکار" /></div>
          <div class="col-md-5"><label class="form-label small mb-0">توضیح</label><input v-model="form.note" class="form-control form-control-sm" /></div>
          <div class="col-12 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ثبت</button><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button></div>
        </div>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="filters.TypeId" class="form-select form-select-sm" @change="list.search()"><option :value="null">همه فرایندها</option><option v-for="t in types" :key="t.id" :value="t.id">{{ t.title }}</option></select>
        <select v-model="filters.Status" class="form-select form-select-sm" @change="list.search()"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in spApprovalStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
      </div>
      <table class="table table-sm">
        <thead><tr><th>پیمانکار</th><th>فرایند</th><th>اعتبار</th><th>مدرک</th><th>وضعیت</th><th>ثبت / تایید</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">تاییدی نیست.</td></tr>
          <tr v-for="a in list.rows.value" :key="a.id">
            <td>{{ a.providerName }}</td><td>{{ a.typeTitle }}</td><td class="small">{{ jalali(a.validFrom) }} تا {{ jalali(a.validTo) }}<div :class="a.daysLeft <= 30 ? 'text-danger' : 'inv-muted'">{{ a.daysLeft }} روز مانده</div></td>
            <td class="inv-ltr small">{{ a.accreditationNumber }}</td>
            <td><span :class="{ ok: a.status === 1, warn: a.status === 0, bad: a.status === 2 || a.status === 3 }" class="inv-badge">{{ spApprovalStatuses[a.status] }}</span><div v-if="a.rejectReason" class="small text-danger">{{ a.rejectReason }}</div><div v-if="a.voidReason" class="small text-danger">{{ a.voidReason }}</div></td>
            <td class="small">{{ a.creatorName }}<br />{{ a.approvedByName }}</td>
            <td class="text-nowrap">
              <button v-if="(a.actions || []).includes('approve')" :disabled="busy" class="btn btn-sm btn-success me-1" @click="act(a.id, 'approve')">تایید QA</button>
              <button v-if="(a.actions || []).includes('reject')" :disabled="busy" class="btn btn-sm btn-outline-warning me-1" @click="dialog = { id: a.id, action: 'reject', title: 'رد تایید پیمانکار' }">رد</button>
              <button v-if="(a.actions || []).includes('void')" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="dialog = { id: a.id, action: 'void', title: 'ابطال تایید پیمانکار' }">ابطال</button>
            </td>
          </tr>
        </tbody>
      </table>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
    <ReasonDialog v-if="dialog" :title="dialog.title" :busy="busy" @confirm="(t: string) => act(dialog!.id, dialog!.action, t)" @close="dialog = null" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
