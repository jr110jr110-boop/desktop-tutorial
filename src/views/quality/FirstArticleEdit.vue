<script setup lang="ts">
import { nextTick, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import FaiPrint from "@/components/quality/print/FaiPrint.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import { firstArticleAction, getEquipmentList, getFirstArticle, measureFirstArticle } from "@/api/QualityAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, num, ok } from "@/composables/inventory";
import { faiStatuses, printForm } from "@/composables/quality";

// Package B (FAI): ONE first article inspection. QC measures EVERY characteristic of the released list; another QC inspector accepts; a QA manager approves (three different persons).
// Until it is approved, the operations after the first article point of the order do not start or finish.
const route = useRoute();
const fai = ref<any>(null);
const equipment = ref<any[]>([]);
const values = ref<Record<number, { measured: any; equipmentId: number | null }>>({});
const busy = ref(false);
const note = ref("");
const dialog = ref<null | { action: string; title: string }>(null);
const can = (a: string) => (fai.value?.actions || []).includes(a);

function setFai(data: any) {
  fai.value = data;
  for (const l of data.lines) if (!values.value[l.characteristicId]) values.value[l.characteristicId] = { measured: l.measured ?? null, equipmentId: null };
}
async function load() {
  const [data, error] = await getFirstArticle(Number(route.params.id));
  if (error) return fail(error);
  setFai(data);
}
const preview = (l: any) => {
  const v = values.value[l.characteristicId]?.measured;
  if (v === null || v === "" || v === undefined || isNaN(Number(v))) return null;
  return Number(v) >= l.lower && Number(v) <= l.upper;
};
async function measure() {
  const rows = fai.value.lines.map((l: any) => ({ characteristicId: l.characteristicId, measured: Number(values.value[l.characteristicId].measured), equipmentId: values.value[l.characteristicId].equipmentId }));
  if (rows.some((r: any) => isNaN(r.measured))) return fail("برای همه مشخصه‌ها مقدار اندازه‌گیری‌شده را وارد کنید.");
  if (rows.some((r: any) => !r.equipmentId)) return fail("برای هر مقدار ابزار اندازه‌گیری را انتخاب کنید.");
  busy.value = true;
  const [data, error] = await measureFirstArticle(fai.value.id, rows, note.value);
  busy.value = false;
  if (error) return fail(error);
  ok(data.status === 4 ? "مشخصه خارج از تلرانس بود؛ نمونه اول رد و گزارش عدم انطباق ساخته شد." : "اندازه‌گیری ثبت شد.");
  setFai(data);
}
async function act(action: string, text?: string) {
  busy.value = true;
  const [data, error] = await firstArticleAction(fai.value.id, action, text ?? note.value);
  busy.value = false;
  dialog.value = null;
  if (error) return fail(error);
  ok("انجام شد.");
  setFai(data);
}
async function doPrint() {
  await nextTick();
  printForm();
}
onMounted(async () => {
  const [eq] = await getEquipmentList({ PageSize: 300 });
  equipment.value = rowsOf(eq);
  load();
});
</script>

<template>
  <div v-if="fai" class="inv-page">
    <FaiPrint :fai="fai" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">بازرسی نمونه اول <span class="inv-ltr ms-2">{{ fai.number }}</span></h1>
          <span :class="{ ok: fai.status === 3, warn: fai.status <= 2, bad: fai.status >= 4 }" class="inv-badge">{{ faiStatuses[fai.status] }}</span>
        </div>
        <div class="d-flex flex-wrap gap-2">
          <router-link :to="{ name: 'QualityFirstArticles' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button class="btn btn-sm btn-outline-secondary" @click="doPrint">چاپ گزارش FAI</button>
          <button v-if="can('qc-accept')" :disabled="busy" class="btn btn-sm btn-success" @click="act('qc-accept')">پذیرش QC</button>
          <button v-if="can('qa-approve')" :disabled="busy" class="btn btn-sm btn-success" @click="act('qa-approve')">تایید QA</button>
          <button v-if="can('reject')" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="dialog = { action: 'reject', title: 'رد نمونه اول' }">رد</button>
          <button v-if="can('void')" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="dialog = { action: 'void', title: 'ابطال نمونه اول' }">ابطال</button>
        </div>
      </div>
      <div class="row g-2 small">
        <div class="col-md-4">قطعه: <b class="inv-ltr">{{ fai.itemCode }}</b> {{ fai.itemTitle }}</div>
        <div class="col-md-4">دستور تولید: <b class="inv-ltr">{{ fai.productionOrderNumber || "-" }}</b></div>
        <div class="col-md-4">سابقه بازرسی: <b class="inv-ltr">{{ fai.qcRecordNumber || "-" }}</b></div>
        <div class="col-md-12">پیکربندی: <span class="inv-ltr">{{ fai.fingerprintText }}</span></div>
        <div class="col-md-12">علت: {{ fai.trigger }}</div>
        <div v-if="fai.measuredByName" class="col-md-4">اندازه‌گیری: {{ fai.measuredByName }} — {{ jalali(fai.measuredAt) }}</div>
        <div v-if="fai.qcAcceptedByName" class="col-md-4">پذیرش QC: {{ fai.qcAcceptedByName }} — {{ jalali(fai.qcAcceptedAt) }}</div>
        <div v-if="fai.qaApprovedByName" class="col-md-4">تایید QA: {{ fai.qaApprovedByName }} — {{ jalali(fai.qaApprovedAt) }}</div>
        <div v-if="fai.rejectReason" class="col-md-12 text-danger">رد: {{ fai.rejectReason }}</div>
        <div v-if="fai.voidReason" class="col-md-12 text-danger">ابطال: {{ fai.voidReason }}</div>
      </div>
    </div>
    <div class="inv-card">
      <div class="inv-toolbar"><h2 class="inv-title">مشخصه‌ها</h2><button v-if="can('measure')" :disabled="busy" class="btn btn-sm btn-primary" @click="measure">ثبت اندازه‌گیری همه مشخصه‌ها</button></div>
      <div class="table-responsive">
        <table class="table table-sm align-middle">
          <thead><tr><th>شماره</th><th>مشخصه</th><th>رده</th><th>اسمی</th><th>حدود</th><th>واحد</th><th>کلاس ابزار</th><th>اندازه‌گیری‌شده</th><th>ابزار</th><th>نتیجه</th></tr></thead>
          <tbody>
            <tr v-if="!fai.lines.length"><td colspan="10" class="text-center inv-muted">مشخصه‌ای نیست.</td></tr>
            <tr v-for="l in fai.lines" :key="l.characteristicId">
              <td class="inv-ltr">{{ l.no }}</td><td>{{ l.name }}</td><td>{{ l.classText }}</td><td class="inv-ltr">{{ num(l.nominal) }}</td><td class="inv-ltr small">{{ num(l.lower) }} … {{ num(l.upper) }}</td><td class="inv-ltr">{{ l.unit }}</td><td class="small">{{ l.instrumentText }}</td>
              <template v-if="can('measure')">
                <td><input v-model="values[l.characteristicId].measured" class="form-control form-control-sm inv-ltr" type="number" step="any" style="width: 110px" /></td>
                <td><select v-model="values[l.characteristicId].equipmentId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="e in equipment" :key="e.id" :value="e.id">{{ e.code }} - {{ e.name }}</option></select></td>
                <td><span v-if="preview(l) !== null" :class="preview(l) ? 'ok' : 'bad'" class="inv-badge">{{ preview(l) ? "قبول" : "رد" }}</span></td>
              </template>
              <template v-else>
                <td class="inv-ltr">{{ l.measured === null || l.measured === undefined ? "" : num(l.measured) }}</td><td class="inv-ltr">{{ l.equipmentCode }}</td>
                <td><span v-if="l.pass !== null && l.pass !== undefined" :class="l.pass ? 'ok' : 'bad'" class="inv-badge">{{ l.pass ? "قبول" : "رد" }}</span></td>
              </template>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="mt-2"><label class="form-label small mb-0">توضیح</label><input v-model="note" class="form-control form-control-sm" /></div>
      <p class="inv-muted small mt-2 mb-0">نتیجه هر مشخصه را سرور از حدود فهرست منتشرشده محاسبه می‌کند. اگر مشخصه‌ای خارج از تلرانس باشد نمونه اول رد می‌شود و گزارش عدم انطباق ساخته می‌شود. اندازه‌گیرنده، پذیرنده QC و تاییدکننده QA سه نفر متفاوت‌اند و صندلی هوش مصنوعی نمی‌پذیرد یا تایید نمی‌کند.</p>
    </div>
    <ReasonDialog v-if="dialog" :title="dialog.title" :busy="busy" @confirm="(t: string) => act(dialog!.action, t)" @close="dialog = null" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
