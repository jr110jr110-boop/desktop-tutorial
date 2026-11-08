<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import QPager from "@/components/quality/QPager.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import ProcedurePicker from "@/components/quality/ProcedurePicker.vue";
import DocAttach from "@/components/quality/DocAttach.vue";
import { call, rowsOf } from "@/api/ApiClient";
import { getEquipmentList, getPersons, getSpQualification, getSpQualifications, getSpTypes, saveSpQualification, spQualificationAction } from "@/api/QualityAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { spQualStatuses, spSurveyKinds, usePaged } from "@/composables/quality";
import { hasRole } from "@/composables/roles";

// Package C: the qualification of a special process. QC/QA/engineering/PMO write it (procedure revision in force, parameter limits inside the master list, furnace with its temperature survey, calibrated
// instruments, qualified operators); QA approves it and never its author (maker-checker). A run is accepted only inside an approved qualification. The 30 day expiry alert is sent by the system.
const filters = ref({ Status: null as number | null, TypeId: null as number | null, ExpiringInDays: null as number | null, Contains: "" });
const list = usePaged(getSpQualifications, filters);
const types = ref<any[]>([]);
const machines = ref<any[]>([]);
const instruments = ref<any[]>([]);
const people = ref<any[]>([]);
const form = ref<any>(null);
const busy = ref(false);
const dialog = ref<null | { id: number; action: string; title: string }>(null);
const canWrite = hasRole(["QA", "QC", "PMO", "ENGINEERING", "ENGINEER"]);
const typeOf = computed(() => types.value.find((t) => t.id === form.value?.typeId));

const blank = () => ({
  id: null, typeId: null as number | null, title: "", procedureDeliverableId: null as number | null, procedureRevisionId: null as number | null, validFrom: today(), validTo: "", note: "", supersedesId: null as number | null,
  limits: [] as any[], equipments: [] as any[], operatorPersonIds: [] as number[], rowVersion: null as string | null,
});
function pickType() {
  const t = typeOf.value;
  if (!t) return;
  form.value.limits = t.parameters.map((p: any) => ({ name: p.name, unit: p.unit, min: p.min, max: p.max, required: p.required }));
  if (!form.value.procedureDeliverableId && t.procedureDeliverableId) form.value.procedureDeliverableId = t.procedureDeliverableId;
}
async function edit(id: number, renew = false) {
  const [q, error] = await getSpQualification(id);
  if (error) return fail(error);
  form.value = {
    id: renew ? null : q.id, typeId: q.typeId, title: q.title, procedureDeliverableId: q.procedureDeliverableId, procedureRevisionId: renew ? null : q.procedureRevisionId, validFrom: renew ? today() : q.validFrom?.slice(0, 10), validTo: renew ? "" : q.validTo?.slice(0, 10),
    note: q.note || "", supersedesId: renew ? q.id : q.supersedesId, rowVersion: renew ? null : q.rowVersion,
    limits: q.limits.map((l: any) => ({ name: l.name, unit: l.unit, min: l.min, max: l.max, required: l.required })),
    equipments: q.equipments.map((e: any) => ({ kind: e.kind, machineId: e.machineId, equipmentId: e.equipmentId, surveyKind: e.surveyKind, surveyDate: e.surveyDate?.slice(0, 10) ?? "", surveyDueDate: e.surveyDueDate?.slice(0, 10) ?? "", surveyDocumentId: e.surveyDocumentId })),
    operatorPersonIds: q.operators.map((o: any) => o.personId),
  };
}
const addEquipment = (kind: number) => form.value.equipments.push({ kind, machineId: null, equipmentId: null, surveyKind: 0, surveyDate: "", surveyDueDate: "", surveyDocumentId: null });
async function save() {
  const f = form.value;
  const nn = (v: any) => (v === "" || v === undefined ? null : v);
  if (!f.typeId) return fail("نوع فرایند را انتخاب کنید.");
  if (!f.procedureDeliverableId || !f.procedureRevisionId) return fail("روش اجرایی و ویرایش منتشرشده آن را انتخاب کنید.");
  busy.value = true;
  const [, error] = await saveSpQualification({
    ...f, validFrom: f.validFrom, validTo: f.validTo,
    limits: f.limits.map((l: any) => ({ ...l, min: nn(l.min), max: nn(l.max) })),
    equipments: f.equipments.map((e: any) => ({ ...e, machineId: e.kind === 1 ? e.machineId : null, equipmentId: e.kind === 2 ? e.equipmentId : null, surveyDate: nn(e.surveyDate), surveyDueDate: nn(e.surveyDueDate) })),
  });
  busy.value = false;
  if (error) return fail(error);
  ok("صلاحیت فرایند ذخیره شد.");
  form.value = null;
  list.load();
}
async function act(id: number, action: string, note?: string) {
  busy.value = true;
  const [, error] = await spQualificationAction(id, action, note);
  busy.value = false;
  dialog.value = null;
  if (error) return fail(error);
  ok("انجام شد.");
  list.load();
}
const badge = (s: number) => ({ ok: s === 2, warn: s === 0 || s === 1, bad: s === 3 || s === 4, info: s === 5 });
onMounted(async () => {
  list.load();
  const [t] = await getSpTypes();
  types.value = (Array.isArray(t) ? t : []).filter((x: any) => !x.existingControl);
  const [m] = await call("get", "Machine");
  machines.value = Array.isArray(m) ? m : rowsOf(m);
  const [i] = await getEquipmentList({ PageSize: 300 });
  instruments.value = rowsOf(i);
  const [p] = await getPersons({ PageSize: 300 });
  people.value = rowsOf(p);
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">صلاحیت فرایندهای ویژه</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = blank()">صلاحیت جدید</button>
      </div>

      <div v-if="form" class="border rounded p-2 mb-3">
        <div class="row g-2">
          <div class="col-md-3"><label class="form-label small mb-0">نوع فرایند *</label>
            <select v-model="form.typeId" :disabled="!!form.id" class="form-select form-select-sm" @change="pickType"><option :value="null">-</option><option v-for="t in types" :key="t.id" :value="t.id">{{ t.title }}</option></select></div>
          <div class="col-md-5"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">اعتبار از *</label><KsDatePicker v-model="form.validFrom" element-tag="spq-valid-from" placeholder="تاریخ" simple-placeholder /></div>
          <div class="col-md-2"><label class="form-label small mb-0">اعتبار تا * (حداکثر سه سال)</label><KsDatePicker v-model="form.validTo" element-tag="spq-valid-to" placeholder="تاریخ" simple-placeholder /></div>
          <div class="col-md-6"><label class="form-label small mb-0">روش اجرایی و ویرایش معتبر *</label><ProcedurePicker v-model:deliverable-id="form.procedureDeliverableId" v-model:revision-id="form.procedureRevisionId" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">توضیح</label><input v-model="form.note" class="form-control form-control-sm" /></div>
          <div class="col-12">
            <b class="small">حد پارامترها (باید داخل حد فهرست فرایند باشد، نه بازتر)</b>
            <table class="table table-sm mb-1">
              <thead><tr><th>پارامتر</th><th>واحد</th><th>حداقل</th><th>حداکثر</th><th>الزامی</th></tr></thead>
              <tbody>
                <tr v-if="!form.limits.length"><td colspan="5" class="inv-muted text-center">نوع فرایند را انتخاب کنید.</td></tr>
                <tr v-for="(l, i) in form.limits" :key="i">
                  <td>{{ l.name }}</td><td class="inv-ltr">{{ l.unit }}</td>
                  <td><input v-model="l.min" class="form-control form-control-sm inv-ltr" type="number" step="any" style="max-width: 120px" /></td><td><input v-model="l.max" class="form-control form-control-sm inv-ltr" type="number" step="any" style="max-width: 120px" /></td>
                  <td><input v-model="l.required" class="form-check-input" type="checkbox" /></td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="col-12">
            <b class="small">تجهیزات (کوره/خط و ابزارهای اندازه‌گیری)</b>
            <div v-for="(e, i) in form.equipments" :key="i" class="row g-2 mb-1 align-items-end">
              <div v-if="e.kind === 1" class="col-md-3"><label class="form-label small mb-0">کوره / خط</label><select v-model="e.machineId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="m in machines" :key="m.id" :value="m.id">{{ m.title }}</option></select></div>
              <template v-if="e.kind === 1 && typeOf?.requiresSurvey">
                <div class="col-md-2"><label class="form-label small mb-0">نوع بررسی دما</label><select v-model="e.surveyKind" class="form-select form-select-sm"><option v-for="(t, k) in spSurveyKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
                <div class="col-md-2"><label class="form-label small mb-0">تاریخ بررسی</label><KsDatePicker v-model="e.surveyDate" :element-tag="'spq-survey-' + i" placeholder="تاریخ" simple-placeholder /></div>
                <div class="col-md-2"><label class="form-label small mb-0">سررسید بعدی</label><KsDatePicker v-model="e.surveyDueDate" :element-tag="'spq-due-' + i" placeholder="تاریخ" simple-placeholder /></div>
                <div class="col-md-2"><DocAttach v-model="e.surveyDocumentId" title="گزارش بررسی دما" /></div>
              </template>
              <div v-if="e.kind === 2" class="col-md-5"><label class="form-label small mb-0">ابزار اندازه‌گیری</label><select v-model="e.equipmentId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="m in instruments" :key="m.id" :value="m.id">{{ m.code }} {{ m.name }}</option></select></div>
              <div class="col-md-1"><button class="btn btn-sm btn-outline-danger" @click="form.equipments.splice(i, 1)">حذف</button></div>
            </div>
            <div class="d-flex gap-2"><button class="btn btn-sm btn-outline-secondary" @click="addEquipment(1)">کوره / خط</button><button class="btn btn-sm btn-outline-secondary" @click="addEquipment(2)">ابزار اندازه‌گیری</button></div>
          </div>
          <div class="col-12"><label class="form-label small mb-0">اپراتورهای واجد صلاحیت (گواهی «اپراتور فرایند ویژه» معتبر لازم است)</label>
            <div class="d-flex flex-wrap gap-2"><label v-for="p in people" :key="p.id" class="form-check small border rounded px-2 py-1"><input v-model="form.operatorPersonIds" :value="p.id" class="form-check-input ms-1" type="checkbox" /> {{ p.name }}</label></div></div>
          <div class="col-12 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره پیش‌نویس</button><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button></div>
        </div>
      </div>

      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره، عنوان یا روش اجرایی" @keydown.enter="list.search()" />
        <select v-model="filters.TypeId" class="form-select form-select-sm" @change="list.search()"><option :value="null">همه فرایندها</option><option v-for="t in types" :key="t.id" :value="t.id">{{ t.title }}</option></select>
        <select v-model="filters.Status" class="form-select form-select-sm" @change="list.search()"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in spQualStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.ExpiringInDays" class="form-select form-select-sm" @change="list.search()"><option :value="null">همه سررسیدها</option><option :value="30">منقضی در ۳۰ روز</option><option :value="90">منقضی در ۹۰ روز</option></select>
        <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
      </div>
      <table class="table table-sm">
        <thead><tr><th>شماره</th><th>فرایند</th><th>روش اجرایی</th><th>اعتبار</th><th>وضعیت</th><th>تهیه‌کننده / تاییدکننده</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">صلاحیتی نیست.</td></tr>
          <tr v-for="q in list.rows.value" :key="q.id">
            <td class="inv-ltr">{{ q.number }}</td><td>{{ q.typeTitle }}<div class="small inv-muted">{{ q.title }}</div></td>
            <td class="inv-ltr">{{ q.procedureNumber }} / {{ q.procedureRevisionCode }}<div v-if="!q.procedureInForce" class="text-danger small">ویرایش دیگر معتبر نیست</div></td>
            <td class="small">{{ jalali(q.validFrom) }} تا {{ jalali(q.validTo) }}<div :class="q.daysLeft <= 30 ? 'text-danger' : 'inv-muted'">{{ q.daysLeft }} روز مانده</div></td>
            <td><span :class="badge(q.status)" class="inv-badge">{{ spQualStatuses[q.status] }}</span><div v-if="q.rejectReason" class="small text-danger">{{ q.rejectReason }}</div><div v-if="q.voidReason" class="small text-danger">{{ q.voidReason }}</div></td>
            <td class="small">{{ q.creatorName }}<br />{{ q.approvedByName }}</td>
            <td class="text-nowrap">
              <button v-if="(q.actions || []).includes('edit')" class="btn btn-sm btn-outline-secondary me-1" @click="edit(q.id)">ویرایش</button>
              <button v-if="(q.actions || []).includes('submit')" :disabled="busy" class="btn btn-sm btn-outline-primary me-1" @click="act(q.id, 'submit')">ارسال</button>
              <button v-if="(q.actions || []).includes('approve')" :disabled="busy" class="btn btn-sm btn-success me-1" @click="act(q.id, 'approve')">تایید QA</button>
              <button v-if="(q.actions || []).includes('reject')" :disabled="busy" class="btn btn-sm btn-outline-warning me-1" @click="dialog = { id: q.id, action: 'reject', title: 'رد صلاحیت فرایند' }">رد</button>
              <button v-if="q.status === 2 && canWrite" class="btn btn-sm btn-outline-secondary me-1" @click="edit(q.id, true)">تمدید</button>
              <button v-if="(q.actions || []).includes('void')" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="dialog = { id: q.id, action: 'void', title: 'ابطال صلاحیت فرایند' }">ابطال</button>
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
