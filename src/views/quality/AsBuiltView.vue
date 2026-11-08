<script setup lang="ts">
import { nextTick, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import AsBuiltPrint from "@/components/quality/print/AsBuiltPrint.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import { getAsBuilt, getAsBuiltBySerial, getAsBuiltList, reissueAsBuilt, decideAsBuiltReissue } from "@/api/QualityAPIs";
import { fail, jalali, num, ok } from "@/composables/inventory";
import { asBuiltStatuses, printForm } from "@/composables/quality";
import { hasRole } from "@/composables/roles";

// Package B (H10): «پیکربندی ساخته‌شده» per serial. The record is a snapshot made at the release (JSON + SHA-256): it is never edited; a correction is a new version with a reason that supersedes the old one.
const route = useRoute();
const serial = ref(String(route.query.serial || ""));
const rec = ref<any>(null);
const rows = ref<any[]>([]);
const busy = ref(false);
const reissue = ref(false);
const canReissue = hasRole(["QA"]);
const canDecide = hasRole(["PMO", "CEO"]);
const deciding = ref<"approve" | "reject" | null>(null);

async function find() {
  rec.value = null;
  if (!serial.value.trim()) return;
  const [data, error] = await getAsBuiltBySerial(serial.value.trim());
  if (error) return fail(error);
  rec.value = data;
}
async function open(id: number) {
  const [data, error] = await getAsBuilt(id);
  if (error) return fail(error);
  rec.value = data;
}
async function loadList() {
  const [data] = await getAsBuiltList({ PageSize: 50, WorkOrderId: route.query.workOrderId || undefined });
  rows.value = Array.isArray(data) ? data : data?.data || [];
}
async function doReissue(reason: string) {
  busy.value = true;
  const [data, error] = await reissueAsBuilt(rec.value.id, reason);
  busy.value = false;
  reissue.value = false;
  if (error) return fail(error);
  ok("درخواست صدور مجدد ثبت شد؛ پس از تایید مدیر پروژه یا مدیرعامل نسخه جدید جاری می‌شود.");
  rec.value = data;
  loadList();
}
async function doDecide(note: string) {
  busy.value = true;
  const [data, error] = await decideAsBuiltReissue(rec.value.id, deciding.value === "approve", note);
  busy.value = false;
  if (error) return fail(error);
  deciding.value = null;
  ok(data.status === 0 ? "نسخه جدید جاری شد." : "درخواست صدور مجدد رد شد.");
  rec.value = data;
  loadList();
}
async function doPrint() {
  await nextTick();
  printForm();
}
onMounted(async () => {
  await loadList();
  if (route.query.id) await open(Number(route.query.id));
  else if (serial.value) await find();
});
</script>

<template>
  <div class="inv-page">
    <AsBuiltPrint v-if="rec" :rec="rec" />
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">پیکربندی ساخته‌شده</h1></div>
      <div class="inv-filters mb-2">
        <input v-model="serial" class="form-control form-control-sm inv-ltr" placeholder="شماره سریال" @keydown.enter="find" />
        <button class="btn btn-sm btn-secondary" @click="find">نمایش</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>محصول</th><th>سریال</th><th>دستور ساخت</th><th>برگه ترخیص</th><th>نسخه</th><th>تاریخ</th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="6" class="text-center inv-muted">پیکربندی ساخته‌شده‌ای صادر نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id" role="button" @click="open(r.id)">
            <td class="inv-ltr">{{ r.itemCode }}</td><td class="inv-ltr">{{ r.serialNumber || "(بچ)" }}</td><td class="inv-ltr">{{ r.workOrderCode }}</td><td class="inv-ltr">{{ r.releaseNoteNumber }}</td><td>{{ r.version }}</td><td>{{ jalali(r.createDate) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-if="rec" class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h2 class="inv-title"><span class="inv-ltr">{{ rec.itemCode }}</span> <span class="inv-ltr">{{ rec.serialNumber }}</span> — نسخه {{ rec.version }}</h2>
          <span :class="rec.hashOk ? 'ok' : 'bad'" class="inv-badge">{{ rec.hashOk ? "یکپارچگی تایید شد (SHA-256)" : "اثر انگشت با محتوا نمی‌خواند" }}</span>
          <span v-if="rec.status !== 0" class="inv-badge warn ms-1">{{ asBuiltStatuses[rec.status] }}</span>
        </div>
        <div class="d-flex gap-2">
          <button class="btn btn-sm btn-outline-secondary" @click="doPrint">چاپ</button>
          <button v-if="canDecide && rec.status === 2" class="btn btn-sm btn-success" @click="deciding = 'approve'">تایید صدور مجدد</button>
          <button v-if="canDecide && rec.status === 2" class="btn btn-sm btn-outline-danger" @click="deciding = 'reject'">رد صدور مجدد</button>
          <button v-if="canReissue && rec.status === 0" class="btn btn-sm btn-outline-primary" @click="reissue = true">صدور نسخه جدید</button>
        </div>
      </div>
      <p class="small inv-ltr mb-2">{{ rec.sha256 }}</p>
      <div v-if="rec.snapshot">
        <h3 class="h6">قطعات، بچ‌ها و سریال‌های نصب‌شده</h3>
        <table class="table table-sm">
          <thead><tr><th>شماره قطعه</th><th>بچ</th><th>سریال</th><th>شماره ذوب</th><th>گواهی</th><th>مقدار</th></tr></thead>
          <tbody>
            <tr v-if="!rec.snapshot.installed.length"><td colspan="6" class="inv-muted text-center">ثبت نشده.</td></tr>
            <tr v-for="(i, k) in rec.snapshot.installed" :key="k"><td class="inv-ltr">{{ i.partNumber }}</td><td class="inv-ltr">{{ i.lotNumber }}</td><td class="inv-ltr">{{ i.serialNumber }}</td><td class="inv-ltr">{{ i.heatNumber }}</td><td class="inv-ltr">{{ i.certificateNumber }}</td><td>{{ num(i.quantity) }}</td></tr>
          </tbody>
        </table>
        <h3 class="h6">نقشه‌ها و مسیر ساخت</h3>
        <ul class="small">
          <li v-for="(d, k) in rec.snapshot.drawings" :key="'d' + k"><span class="inv-ltr">{{ d.documentNumber }} / {{ d.revisionCode }}</span> (فهرست مشخصه نسخه {{ d.listVersion || "-" }})</li>
          <li v-for="(r, k) in rec.snapshot.routings" :key="'r' + k">مسیر ساخت <span class="inv-ltr">{{ r.partNumber }}</span> بازنگری {{ r.revision }}</li>
          <li>BOM: <span class="inv-ltr">{{ rec.snapshot.bomCode }}</span> بازنگری {{ rec.snapshot.bomRevision }}</li>
        </ul>
        <h3 class="h6">انحراف‌ها، عدم انطباق‌ها و نمونه اول</h3>
        <ul class="small">
          <li v-for="(d, k) in rec.snapshot.deviations" :key="'v' + k"><span class="inv-ltr">{{ d.number }}</span> {{ d.kind }} {{ d.partNumber }} — {{ d.reason }}</li>
          <li v-for="(n, k) in rec.snapshot.ncrs" :key="'n' + k"><span class="inv-ltr">{{ n.number }}</span> {{ n.status }} {{ n.disposition }}</li>
          <li v-for="(f, k) in rec.snapshot.firstArticles" :key="'f' + k"><span class="inv-ltr">{{ f.number }}</span> {{ f.status }}</li>
          <li v-if="!rec.snapshot.deviations.length && !rec.snapshot.ncrs.length && !rec.snapshot.firstArticles.length" class="inv-muted">موردی نیست.</li>
        </ul>
      </div>
      <h3 class="h6 mt-3">نسخه‌ها</h3>
      <table class="table table-sm">
        <thead><tr><th>نسخه</th><th>وضعیت</th><th>دلیل</th><th>تاریخ</th><th>SHA-256</th></tr></thead>
        <tbody><tr v-for="v in rec.versions" :key="v.id" role="button" @click="open(v.id)"><td>{{ v.version }}</td><td>{{ asBuiltStatuses[v.status] }}</td><td class="small">{{ v.reason }}</td><td>{{ jalali(v.createDate) }}</td><td class="inv-ltr small">{{ v.sha256?.substring(0, 16) }}…</td></tr></tbody>
      </table>
    </div>
    <ReasonDialog v-if="deciding" :title="deciding === 'approve' ? 'تایید صدور مجدد' : 'رد صدور مجدد'" label="توضیح (در رد الزامی)" confirm-class="btn-primary" :busy="busy" @confirm="doDecide" @close="deciding = null" />
    <ReasonDialog v-if="reissue" title="صدور نسخه جدید پیکربندی ساخته‌شده" label="دلیل" confirm-class="btn-primary" :busy="busy" @confirm="doReissue" @close="reissue = false" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
