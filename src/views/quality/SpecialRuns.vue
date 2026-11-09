<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import QPager from "@/components/quality/QPager.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import DocAttach from "@/components/quality/DocAttach.vue";
import SpRunPrint from "@/components/quality/print/SpRunPrint.vue";
import { call, rowsOf } from "@/api/ApiClient";
import { getSpGate, getSpQualification, getSpQualifications, getSpRun, getSpRuns, saveSpRun, spRunAction } from "@/api/QualityAPIs";
import { fail, jalali, num, ok } from "@/composables/inventory";
import { printForm, spReviewStatuses, spRunResults, spRunStatuses, usePaged } from "@/composables/quality";
import { hasRole } from "@/composables/roles";

// Package C: the run records of special processes. The server judges every value against the limits of the qualification: a value outside is a failed run that opens an NCR on its own. A run is reviewed by a
// second person (QA/QC, never the recorder or the operator, never an AI seat). A flagged production step finishes only with passed and reviewed runs that cover its quantity.
const route = useRoute();
const filters = ref<any>({ Result: null, ReviewStatus: null, Status: null, OperationId: route.query.operationId ? Number(route.query.operationId) : null, Contains: "" });
const list = usePaged(getSpRuns, filters);
const canWrite = hasRole(["QC", "QA", "PMO", "ENGINEERING", "ENGINEER", "CONSTRUCTION", "ASSEMBLER", "PRODUCTIONMANAGER", "SHOPSUPERVISOR"]);
const form = ref<any>(null);
const quals = ref<any[]>([]);
const qual = ref<any>(null);
const providers = ref<any[]>([]);
const gate = ref<any>(null);
const busy = ref(false);
const printRun = ref<any>(null);
const dialog = ref<null | { id: number; action: string; title: string }>(null);

const blank = () => ({
  operationId: filters.value.OperationId as number | null, partOperationId: null as number | null, qualificationId: null as number | null, isSubcontract: false, supplierId: null as number | null,
  receiptDocumentId: null as number | null, lotId: null as number | null, serialNumbers: "", quantity: null as number | null, operatorPersonId: null as number | null, machineId: null as number | null,
  startedAt: "", endedAt: "", chartDocumentId: null as string | null, chartFileName: "", certificateDocumentId: null as string | null, certificateNumber: "", instrumentIds: [] as number[], values: [] as any[], note: "",
});
async function pickQualification() {
  qual.value = null;
  const f = form.value;
  if (!f.qualificationId) return;
  const [q, error] = await getSpQualification(f.qualificationId);
  if (error) return fail(error);
  qual.value = q;
  f.values = q.limits.map((l: any) => ({ name: l.name, unit: l.unit, min: l.min, max: l.max, value: null }));
  f.operatorPersonId = q.operators.length === 1 ? q.operators[0].personId : null;
  const m = q.equipments.filter((e: any) => e.machineId);
  f.machineId = m.length === 1 ? m[0].machineId : null;
  f.instrumentIds = q.equipments.filter((e: any) => e.equipmentId).map((e: any) => e.equipmentId);
}
const preview = (v: any) => {
  if (v.value === null || v.value === "" || isNaN(Number(v.value))) return null;
  return (v.min === null || v.min === undefined || Number(v.value) >= v.min) && (v.max === null || v.max === undefined || Number(v.value) <= v.max);
};
async function checkGate() {
  gate.value = null;
  const f = form.value || filters.value;
  const id = form.value ? form.value.operationId : filters.value.OperationId;
  if (!id) return;
  const [g] = await getSpGate(id);
  gate.value = g || null;
  void f;
}
async function save() {
  const f = form.value;
  if (!f.operationId && !f.partOperationId) return fail("شناسه گام تولید یا مرحله ردیابی را وارد کنید.");
  if (!f.quantity) return fail("تعداد قطعه‌های اجرا را وارد کنید.");
  if (f.values.some((v: any) => v.value === null || v.value === "")) return fail("برای همه پارامترها مقدار وارد کنید.");
  busy.value = true;
  const [data, error] = await saveSpRun({
    ...f, operationId: f.operationId || null, partOperationId: f.partOperationId || null, qualificationId: f.isSubcontract ? null : f.qualificationId, supplierId: f.isSubcontract ? f.supplierId : null,
    operatorPersonId: f.isSubcontract ? null : f.operatorPersonId, machineId: f.isSubcontract ? null : f.machineId, quantity: Number(f.quantity),
    values: f.values.map((v: any) => ({ name: v.name, value: Number(v.value) })), startedAt: f.startedAt, endedAt: f.endedAt,
  });
  busy.value = false;
  if (error) return fail(error);
  ok(data.result === 2 ? "اجرا مردود شد؛ گزارش عدم انطباق " + (data.ncrNumber || "") + " ساخته شد." : "اجرا ثبت شد؛ بازبینی نفر دوم لازم است.");
  form.value = null;
  list.load();
  checkGate();
}
async function act(id: number, action: string, note?: string) {
  busy.value = true;
  const [, error] = await spRunAction(id, action, note);
  busy.value = false;
  dialog.value = null;
  if (error) return fail(error);
  ok("انجام شد.");
  list.load();
  checkGate();
}
async function doPrint(id: number) {
  const [r, error] = await getSpRun(id);
  if (error) return fail(error);
  printRun.value = r;
  await nextTick();
  printForm();
}
const resultBadge = (r: number) => ({ ok: r === 1, bad: r === 2, warn: r === 0 });
const operators = computed(() => qual.value?.operators || []);
const machinesOfQual = computed(() => (qual.value?.equipments || []).filter((e: any) => e.machineId));
onMounted(async () => {
  list.load();
  checkGate();
  const [q] = await getSpQualifications({ Status: 2, PageSize: 200 });
  quals.value = rowsOf(q);
  const [p] = await call("get", "Provider");
  providers.value = Array.isArray(p) ? p : rowsOf(p);
});
</script>

<template>
  <div class="inv-page">
    <SpRunPrint v-if="printRun" :run="printRun" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">اجرای فرایندهای ویژه</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="(form = blank()), checkGate()">ثبت اجرا</button>
      </div>
      <div v-if="gate && gate.applies" class="alert py-1 small" :class="gate.ready ? 'alert-success' : 'alert-warning'">
        گام انتخاب‌شده («{{ gate.typeTitle }}»): {{ gate.ready ? "گیت پایان باز است؛ اجرای قبول و بازبینی‌شده تعداد را می‌پوشاند." : gate.problem }}
      </div>

      <div v-if="form" class="border rounded p-2 mb-3">
        <div class="row g-2">
          <div class="col-md-2"><label class="form-label small mb-0">شناسه گام تولید *</label><input v-model.number="form.operationId" class="form-control form-control-sm inv-ltr" type="number" @change="checkGate" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">یا مرحله ردیابی (قدیمی)</label><input v-model.number="form.partOperationId" class="form-control form-control-sm inv-ltr" type="number" /></div>
          <div class="col-md-2 d-flex align-items-end"><label class="form-check"><input v-model="form.isSubcontract" class="form-check-input" type="checkbox" /> <span class="form-check-label">اجرای پیمانکار</span></label></div>
          <div v-if="!form.isSubcontract" class="col-md-6"><label class="form-label small mb-0">صلاحیت فرایند (تاییدشده) *</label>
            <select v-model="form.qualificationId" class="form-select form-select-sm" @change="pickQualification"><option :value="null">-</option><option v-for="q in quals" :key="q.id" :value="q.id">{{ q.number }} - {{ q.typeTitle }} ({{ q.title }})</option></select></div>
          <template v-else>
            <div class="col-md-3"><label class="form-label small mb-0">پیمانکار *</label><select v-model="form.supplierId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="p in providers" :key="p.id" :value="p.id">{{ p.name }}</option></select></div>
            <div class="col-md-3"><label class="form-label small mb-0">سند رسید برگشت از پیمانکار (شناسه)</label><input v-model.number="form.receiptDocumentId" class="form-control form-control-sm inv-ltr" type="number" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">شماره گواهی پیمانکار *</label><input v-model="form.certificateNumber" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">فایل گواهی پیمانکار *</label><DocAttach v-model="form.certificateDocumentId" title="گواهی فرایند پیمانکار" /></div>
          </template>
          <div class="col-md-2"><label class="form-label small mb-0">تعداد قطعه *</label><input v-model="form.quantity" class="form-control form-control-sm inv-ltr" type="number" step="any" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">شناسه بچ</label><input v-model.number="form.lotId" class="form-control form-control-sm inv-ltr" type="number" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">سریال‌ها</label><input v-model="form.serialNumbers" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">آغاز *</label><KsDatePicker v-model="form.startedAt" element-tag="spr-started" placeholder="تاریخ و ساعت" simple-placeholder type="datetime" format="YYYY-MM-DDTHH:mm" display-format="jYYYY/jMM/jDD HH:mm" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">پایان *</label><KsDatePicker v-model="form.endedAt" element-tag="spr-ended" placeholder="تاریخ و ساعت" simple-placeholder type="datetime" format="YYYY-MM-DDTHH:mm" display-format="jYYYY/jMM/jDD HH:mm" /></div>
          <template v-if="!form.isSubcontract && qual">
            <div class="col-md-3"><label class="form-label small mb-0">اپراتور *</label><select v-model="form.operatorPersonId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="o in operators" :key="o.personId" :value="o.personId">{{ o.personName }}</option></select></div>
            <div v-if="machinesOfQual.length" class="col-md-3"><label class="form-label small mb-0">کوره / خط *</label><select v-model="form.machineId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="e in machinesOfQual" :key="e.machineId" :value="e.machineId">{{ e.machineTitle }}</option></select></div>
            <div class="col-md-6"><label class="form-label small mb-0">ابزارهای اندازه‌گیری</label>
              <div class="d-flex flex-wrap gap-2"><label v-for="e in qual.equipments.filter((x: any) => x.equipmentId)" :key="e.equipmentId" class="form-check small"><input v-model="form.instrumentIds" :value="e.equipmentId" class="form-check-input" type="checkbox" /> <span class="inv-ltr">{{ e.equipmentCode }}</span> {{ e.equipmentTitle }}</label></div></div>
          </template>
          <div class="col-12">
            <table class="table table-sm mb-1">
              <thead><tr><th>پارامتر</th><th>واحد</th><th>حداقل</th><th>حداکثر</th><th>مقدار *</th><th>پیش‌نمایش (حکم نهایی با سرور)</th></tr></thead>
              <tbody>
                <tr v-if="!form.values.length"><td colspan="6" class="inv-muted text-center">با انتخاب صلاحیت، پارامترها نمایش داده می‌شوند.</td></tr>
                <tr v-for="(v, i) in form.values" :key="i">
                  <td>{{ v.name }}</td><td class="inv-ltr">{{ v.unit }}</td><td class="inv-ltr">{{ v.min ?? "-" }}</td><td class="inv-ltr">{{ v.max ?? "-" }}</td>
                  <td><input v-model="v.value" class="form-control form-control-sm inv-ltr" type="number" step="any" style="max-width: 140px" /></td>
                  <td><span v-if="preview(v) !== null" :class="preview(v) ? 'ok' : 'bad'" class="inv-badge">{{ preview(v) ? "داخل حد" : "خارج از حد" }}</span></td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="col-md-4"><label class="form-label small mb-0">نمودار / سابقه فرایند *</label><DocAttach v-model="form.chartDocumentId" title="نمودار فرایند" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">نام فایل نمودار</label><input v-model="form.chartFileName" class="form-control form-control-sm" /></div>
          <div class="col-md-5"><label class="form-label small mb-0">توضیح</label><input v-model="form.note" class="form-control form-control-sm" /></div>
          <div class="col-12 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ثبت اجرا</button><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button></div>
        </div>
      </div>

      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره، دستور تولید یا کالا" @keydown.enter="list.search()" />
        <input v-model.number="filters.OperationId" class="form-control form-control-sm inv-ltr" type="number" placeholder="شناسه گام" @keydown.enter="list.search()" />
        <select v-model="filters.Result" class="form-select form-select-sm" @change="list.search()"><option :value="null">همه نتایج</option><option v-for="(t, k) in spRunResults" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.ReviewStatus" class="form-select form-select-sm" @change="list.search()"><option :value="null">همه بازبینی‌ها</option><option v-for="(t, k) in spReviewStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
      </div>
      <table class="table table-sm">
        <thead><tr><th>شماره</th><th>فرایند / گام</th><th>قطعه</th><th>تعداد</th><th>نتیجه</th><th>بازبینی</th><th>ثبت‌کننده</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="8" class="text-center inv-muted">اجرایی نیست.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id" :class="{ 'text-decoration-line-through': r.status === 1 }">
            <td class="inv-ltr">{{ r.number }}</td><td>{{ r.typeTitle }}<div class="small inv-ltr">{{ r.operationText || r.orderNumber }}</div><div v-if="r.isSubcontract" class="small inv-muted">پیمانکار: {{ r.supplierName }}</div></td>
            <td class="small"><span class="inv-ltr">{{ r.itemCode }}</span> {{ r.itemTitle }}</td><td>{{ num(r.quantity) }}</td>
            <td><span :class="resultBadge(r.result)" class="inv-badge">{{ spRunResults[r.result] }}</span><div v-if="r.ncrNumber" class="small inv-ltr">{{ r.ncrNumber }}</div></td>
            <td class="small">{{ r.status === 1 ? spRunStatuses[1] : spReviewStatuses[r.reviewStatus] }}<div v-if="r.reviewedByName">{{ r.reviewedByName }} {{ jalali(r.reviewedAt) }}</div><div v-if="r.voidReason" class="text-danger">{{ r.voidReason }}</div></td>
            <td class="small">{{ r.creatorName }}</td>
            <td class="text-nowrap">
              <button v-if="(r.actions || []).includes('review')" :disabled="busy" class="btn btn-sm btn-success me-1" @click="act(r.id, 'review')">بازبینی و تایید</button>
              <button v-if="(r.actions || []).includes('reject-review')" :disabled="busy" class="btn btn-sm btn-outline-warning me-1" @click="dialog = { id: r.id, action: 'reject-review', title: 'رد بازبینی اجرا' }">رد</button>
              <button v-if="(r.actions || []).includes('void')" :disabled="busy" class="btn btn-sm btn-outline-danger me-1" @click="dialog = { id: r.id, action: 'void', title: 'ابطال اجرا' }">ابطال</button>
              <button class="btn btn-sm btn-outline-secondary" @click="doPrint(r.id)">چاپ</button>
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
