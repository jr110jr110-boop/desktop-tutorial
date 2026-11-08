<script setup lang="ts">
import { computed, ref } from "vue";
import { useRoute } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import EcoPrint from "@/components/engChange/print/EcoPrint.vue";
import { addChange, bulkDisposition, getOrder, newRevisionOf, orderAction, removeChange, saveDisposition, saveOrder, taskAction } from "@/api/EngChangeAPIs";
import { fail, jalali, num, ok, printPage } from "@/composables/inventory";
import { changeActions, changeKinds, dispositions, ecClasses, ecUrgencies, ecoStatuses, impactCategories, statusClass } from "@/composables/engChange";

// One engineering change order: content (revisions), effectivity, the computed impact, the disposition of every impacted item, approvals (ENGINEERING and QA, two different people, never the originator) and tasks.
const route = useRoute();
const busy = ref(false);
const order = ref<any>(null);
const form = ref<any>({ title: "", summary: "", effectiveFromOrderNumber: "", effectiveFromSerial: "", effectiveFromDate: "", effectivityNote: "" });
const change = ref<any>({ kind: 1, action: 1, targetId: null, note: "" });
const fresh = ref<any>({ kind: 1, sourceId: null, note: "" });
const category = ref<number | null>(null);
const bulk = ref<any>({ category: 3, disposition: 5, note: "", ownerUserId: null, dueDate: "" });

async function load() {
  const [data, error] = await getOrder(Number(route.params.id));
  if (error) return fail(error);
  apply(data);
}
function apply(data: any) {
  order.value = data;
  form.value = {
    id: data.id, title: data.title, summary: data.summary || "", effectiveFromOrderNumber: data.effectiveFromOrderNumber || "", effectiveFromSerial: data.effectiveFromSerial || "",
    effectiveFromDate: data.effectiveFromDate ? String(data.effectiveFromDate).substring(0, 10) : "", effectivityNote: data.effectivityNote || "",
  };
}
async function run(fn: () => Promise<any>, message: string) {
  busy.value = true;
  const [data, error] = await fn();
  busy.value = false;
  if (error) return fail(error);
  ok(message);
  if (data && data.id) apply(data);
  else load();
}
const save = () => run(() => saveOrder({ ...form.value, effectiveFromDate: form.value.effectiveFromDate || null }), "دستور ذخیره شد.");
const act = (action: string, ask: string | null, message: string) => {
  let note: string | null = null;
  if (ask) {
    note = window.prompt(ask);
    if (!note || !note.trim()) return;
  }
  return run(() => orderAction(order.value.id, action, note), message);
};
const addNamed = () => run(() => addChange(order.value.id, change.value.kind, Number(change.value.targetId), change.value.action, change.value.note), "بازنگری در دستور نام برده شد.");
const makeRevision = () => run(() => newRevisionOf(order.value.id, fresh.value.kind, Number(fresh.value.sourceId), fresh.value.note), "پیش‌نویس بازنگری جدید ساخته و در دستور نام برده شد.");
const dropChange = (c: any) => run(() => removeChange(order.value.id, c.id), "از دستور برداشته شد.");
const setDisposition = (d: any) => run(() => saveDisposition({ orderId: order.value.id, impactKey: d.impactKey, disposition: d.disposition || null, note: d.note, ownerUserId: d.ownerUserId || null, dueDate: d.dueDate ? String(d.dueDate).substring(0, 10) : null }), "تکلیف ثبت شد.");
const setBulk = () => run(() => bulkDisposition({ orderId: order.value.id, category: bulk.value.category, disposition: bulk.value.disposition, note: bulk.value.note, ownerUserId: bulk.value.ownerUserId || null, dueDate: bulk.value.dueDate || null }), "تکلیف برای همه موارد فهرست ثبت شد.");
const doTask = (t: any) => {
  const note = window.prompt("شرح انجام کار");
  if (note && note.trim()) run(() => taskAction(t.id, "done", note), "تکلیف انجام‌شده ثبت شد.");
};
const verifyTask = (t: any) => run(() => taskAction(t.id, "verify", null), "اجرای تکلیف تایید شد.");

const editable = computed(() => !!order.value?.canEdit);
const shown = computed(() => (order.value?.dispositions || []).filter((d: any) => !d.stale && (category.value === null || d.category === category.value)));
const counts = computed(() => (order.value?.impact?.counts || []) as any[]);
load();
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader :title="order ? 'دستور تغییر مهندسی ' + order.number : 'دستور تغییر مهندسی'" />
    <div v-if="order" class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">دستور تغییر {{ order.number }}
          <span class="inv-badge ms-2" :class="statusClass[order.status]">{{ ecoStatuses[order.status] }}</span></h1>
        <div class="d-flex gap-2 d-print-none">
          <router-link :to="{ name: 'EngChangeOrders' }" class="btn btn-sm btn-outline-secondary">فهرست دستورها</router-link>
          <router-link :to="{ name: 'EngChangeRequestEdit', params: { id: order.requestId } }" class="btn btn-sm btn-outline-secondary">درخواست {{ order.requestNumber }}</router-link>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <p class="small inv-muted mb-2">{{ ecClasses[order.classification] }} — فوریت {{ ecUrgencies[order.urgency] }} — دلیل: {{ order.requestReason }}</p>

      <h2 class="h6">نقطه اجرا (Effectivity)</h2>
      <div class="row g-2 mb-2">
        <div class="col-md-3"><label class="form-label small mb-0">از شماره دستور تولید</label><input v-model="form.effectiveFromOrderNumber" class="form-control form-control-sm inv-ltr" :disabled="!editable" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">از شماره سریال</label><input v-model="form.effectiveFromSerial" class="form-control form-control-sm inv-ltr" :disabled="!editable" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">از تاریخ</label>
          <KsDatePicker v-if="editable" v-model="form.effectiveFromDate" element-tag="eco-eff-date" placeholder="تاریخ" simple-placeholder />
          <input v-else class="form-control form-control-sm" :value="jalali(form.effectiveFromDate)" disabled /></div>
        <div class="col-md-3"><label class="form-label small mb-0">توضیح</label><input v-model="form.effectivityNote" class="form-control form-control-sm" :disabled="!editable" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" :disabled="!editable" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">خلاصه</label><input v-model="form.summary" class="form-control form-control-sm" :disabled="!editable" /></div>
      </div>
      <button v-if="editable" class="btn btn-sm btn-primary mb-3" :disabled="busy" @click="save">ذخیره</button>

      <h2 class="h6">بازنگری‌های این دستور</h2>
      <table class="table table-sm">
        <thead><tr><th>نوع</th><th>عمل</th><th>مورد</th><th>از</th><th>به</th><th>وضعیت فعلی</th><th v-if="editable"></th></tr></thead>
        <tbody>
          <tr v-if="!order.changes.length"><td colspan="7" class="text-center inv-muted">هنوز بازنگری نام برده نشده است.</td></tr>
          <tr v-for="c in order.changes" :key="c.id">
            <td>{{ changeKinds[c.kind] }}</td><td>{{ changeActions[c.action] }}</td><td>{{ c.reference }}</td><td>{{ c.oldRevision ?? "-" }}</td><td>{{ c.newRevision ?? "-" }}</td><td>{{ c.state }}</td>
            <td v-if="editable"><button class="btn btn-sm btn-outline-danger py-0" @click="dropChange(c)">برداشتن</button></td>
          </tr>
        </tbody>
      </table>
      <div v-if="editable" class="row g-2 mb-3 align-items-end">
        <div class="col-md-2"><label class="form-label small mb-0">نوع</label><select v-model="change.kind" class="form-select form-select-sm"><option v-for="(t, k) in changeKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-2"><label class="form-label small mb-0">عمل</label><select v-model="change.action" class="form-select form-select-sm"><option v-for="(t, k) in changeActions" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-2"><label class="form-label small mb-0">شناسه بازنگری</label><input v-model="change.targetId" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">توضیح</label><input v-model="change.note" class="form-control form-control-sm" /></div>
        <div class="col-md-2"><button class="btn btn-sm btn-outline-primary w-100" :disabled="busy || !change.targetId" @click="addNamed">نام بردن بازنگری</button></div>
        <div class="col-md-2"><select v-model="fresh.kind" class="form-select form-select-sm"><option :value="1">BOM</option><option :value="2">چارت OPC</option></select></div>
        <div class="col-md-2"><input v-model="fresh.sourceId" class="form-control form-control-sm inv-ltr" placeholder="شناسه نسخه جاری" /></div>
        <div class="col-md-4"><input v-model="fresh.note" class="form-control form-control-sm" placeholder="دلیل (پیش‌فرض: دلیل درخواست)" /></div>
        <div class="col-md-2"><button class="btn btn-sm btn-outline-primary w-100" :disabled="busy || !fresh.sourceId" @click="makeRevision">ساخت پیش‌نویس بازنگری جدید</button></div>
      </div>

      <h2 class="h6">تحلیل اثر (محاسبه‌شده توسط سیستم، نه تایپی)</h2>
      <div class="d-flex flex-wrap gap-2 align-items-center mb-2">
        <button v-if="editable" class="btn btn-sm btn-outline-primary" :disabled="busy" @click="act('refresh-impact', null, 'تحلیل اثر تازه شد.')">محاسبه / تازه‌سازی تحلیل اثر</button>
        <span v-if="order.impact" class="small inv-muted">آخرین محاسبه: {{ jalali(order.impact.computedAt, true) }} — SHA-256: <span class="inv-ltr">{{ order.impact.sha256?.substring(0, 16) }}…</span>
          <span v-if="!order.impact.hashOk" class="text-danger"> (اثر انگشت نمی‌خواند!)</span></span>
        <span v-else class="small text-danger">هنوز محاسبه نشده است.</span>
      </div>
      <div v-if="counts.length" class="d-flex flex-wrap gap-2 mb-2">
        <button class="btn btn-sm" :class="category === null ? 'btn-secondary' : 'btn-outline-secondary'" @click="category = null">همه</button>
        <button v-for="c in counts" :key="c.category" class="btn btn-sm" :class="category === c.category ? 'btn-secondary' : 'btn-outline-secondary'" @click="category = c.category">
          {{ impactCategories[c.category] }}: {{ c.count }} <span v-if="c.count">({{ c.decided }} تکلیف)</span><span v-if="c.truncated" class="text-danger"> بریده شد</span>
        </button>
      </div>
      <div v-if="editable && category !== null" class="row g-2 mb-2 align-items-end">
        <div class="col-md-3"><label class="form-label small mb-0">تکلیف یکجا برای «{{ impactCategories[category] }}»</label>
          <select v-model="bulk.disposition" class="form-select form-select-sm"><option v-for="(t, k) in dispositions" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-3"><input v-model="bulk.note" class="form-control form-control-sm" placeholder="توضیح" /></div>
        <div class="col-md-2"><input v-model.number="bulk.ownerUserId" class="form-control form-control-sm inv-ltr" placeholder="شناسه مسئول" /></div>
        <div class="col-md-2"><KsDatePicker v-model="bulk.dueDate" element-tag="eco-bulk-due" placeholder="مهلت" simple-placeholder /></div>
        <div class="col-md-2"><button class="btn btn-sm btn-outline-primary w-100" :disabled="busy" @click="bulk.category = category; setBulk()">ثبت برای همه</button></div>
      </div>
      <table class="table table-sm">
        <thead><tr><th>فهرست</th><th>مورد</th><th>تعداد</th><th>تکلیف</th><th>توضیح</th><th>مسئول (شناسه)</th><th>مهلت</th><th v-if="editable"></th></tr></thead>
        <tbody>
          <tr v-if="!shown.length"><td colspan="8" class="text-center inv-muted">مورد اثر‌پذیری نیست.</td></tr>
          <tr v-for="d in shown" :key="d.id">
            <td>{{ d.categoryTitle }}</td><td>{{ d.text }}</td><td>{{ num(d.quantity) }}</td>
            <td style="min-width: 170px">
              <select v-if="editable" v-model="d.disposition" class="form-select form-select-sm"><option :value="null">— تکلیف —</option><option v-for="a in d.allowed" :key="a" :value="a">{{ dispositions[a] }}</option></select>
              <span v-else>{{ d.dispositionTitle || "—" }}</span>
            </td>
            <td><input v-if="editable" v-model="d.note" class="form-control form-control-sm" /><span v-else>{{ d.note }}</span></td>
            <td style="width: 110px"><input v-if="editable" v-model.number="d.ownerUserId" class="form-control form-control-sm inv-ltr" /><span v-else>{{ d.ownerName }}</span></td>
            <td style="width: 150px"><KsDatePicker v-if="editable" v-model="d.dueDate" :element-tag="'eco-due-' + d.id" placeholder="مهلت" simple-placeholder /><span v-else>{{ d.dueDate ? jalali(d.dueDate) : "" }}</span></td>
            <td v-if="editable"><button class="btn btn-sm btn-outline-primary py-0" :disabled="busy" @click="setDisposition(d)">ثبت</button></td>
          </tr>
        </tbody>
      </table>

      <div v-if="order.blockers.length" class="alert alert-warning py-2">
        <strong>تا اینجا مانع ارسال/تایید:</strong>
        <ul class="mb-0"><li v-for="(b, i) in order.blockers" :key="i">{{ b }}</li></ul>
      </div>

      <h2 class="h6">تایید</h2>
      <p class="small mb-2">
        تایید مهندسی: <strong>{{ order.engineeringApprovedByName || "—" }}</strong> {{ order.engineeringApprovedAt ? jalali(order.engineeringApprovedAt, true) : "" }}
        &nbsp;|&nbsp; تایید تضمین کیفیت (QA): <strong>{{ order.qaApprovedByName || "—" }}</strong> {{ order.qaApprovedAt ? jalali(order.qaApprovedAt, true) : "" }}
        <span class="inv-muted"> — دو نفر متفاوت، نه سازنده و نه ویرایشگر؛ مدیریت پروژه (PMO)، مدیرعامل و مدیران پروژه‌های متاثر فقط مطلع می‌شوند.</span>
      </p>
      <div class="d-flex flex-wrap gap-2 mb-3">
        <button v-if="order.canSubmit" class="btn btn-sm btn-success" :disabled="busy" @click="act('submit', null, 'برای تایید فرستاده شد.')">ارسال برای تایید</button>
        <button v-if="order.canApproveEngineering" class="btn btn-sm btn-success" :disabled="busy" @click="act('approve-engineering', null, 'تایید مهندسی ثبت شد.')">تایید مهندسی</button>
        <button v-if="order.canApproveQa" class="btn btn-sm btn-success" :disabled="busy" @click="act('approve-qa', null, 'تایید QA ثبت شد.')">تایید QA</button>
        <button v-if="order.canReject" class="btn btn-sm btn-outline-secondary" :disabled="busy" @click="act('return', 'دلیل بازگرداندن به پیش‌نویس', 'به پیش‌نویس برگشت.')">بازگرداندن</button>
        <button v-if="order.canReject" class="btn btn-sm btn-danger" :disabled="busy" @click="act('reject', 'دلیل رد', 'دستور رد شد.')">رد</button>
        <button v-if="order.canCancel" class="btn btn-sm btn-outline-danger" :disabled="busy" @click="act('cancel', 'دلیل لغو', 'دستور لغو شد.')">لغو</button>
        <button v-if="order.canClose" class="btn btn-sm btn-primary" :disabled="busy" @click="act('close', null, 'دستور بسته شد.')">بستن دستور</button>
      </div>

      <h2 class="h6">تکلیف‌های اجرایی</h2>
      <table class="table table-sm">
        <thead><tr><th>تکلیف</th><th>تکلیف (disposition)</th><th>مسئول</th><th>مهلت</th><th>وضعیت</th><th>انجام</th><th>تایید QA</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!order.tasks.length"><td colspan="8" class="text-center inv-muted">تکلیف اجرایی پس از تایید نهایی ساخته می‌شود.</td></tr>
          <tr v-for="t in order.tasks" :key="t.id">
            <td>{{ t.title }}</td><td>{{ t.dispositionTitle }}</td><td>{{ t.ownerName }}</td>
            <td :class="{ 'text-danger': t.overdue }">{{ jalali(t.dueDate) }}</td><td>{{ t.statusText }}</td>
            <td>{{ t.doneByName }} <span class="small inv-muted">{{ t.doneNote }}</span></td><td>{{ t.verifiedByName }}</td>
            <td class="d-print-none">
              <button v-if="t.canDone" class="btn btn-sm btn-outline-primary py-0" @click="doTask(t)">انجام شد</button>
              <button v-if="t.canVerify" class="btn btn-sm btn-outline-success py-0" @click="verifyTask(t)">تایید QA</button>
            </td>
          </tr>
        </tbody>
      </table>
      <p v-if="order.rejectReason" class="text-danger small">دلیل رد: {{ order.rejectReason }}</p>
      <p v-if="order.cancelReason" class="text-danger small">دلیل لغو: {{ order.cancelReason }}</p>
    </div>
    <EcoPrint v-if="order" :order="order" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
