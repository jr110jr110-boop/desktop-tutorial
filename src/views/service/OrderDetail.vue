<script setup lang="ts">
// One service order (/service/orders/:id): data, engineers and tasks, warranty / billable / goodwill decision, time sheet, parts documents, measurements,
// the signed customer report, findings, closing, the draft invoice and the A4 print. Labour money is payroll-protected: «محرمانه (حقوق)» when the API hides it.
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import FnDate from "@/components/fn/FnDate.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import DocAttach from "@/components/quality/DocAttach.vue";
import OrderPrint from "@/components/service/OrderPrint.vue";
import { isoDay, todayIso, typedNumber, useEmployees } from "@/components/service/svKit";
import { decideSvOrder, draftSvInvoice, getSvOrder, getSvUnit, removeSvMeasurement, removeSvTimeEntry, requestSvGoodwill, saveSvMeasurement, saveSvOrder, saveSvTimeEntry, svOrderAction } from "@/api/ServiceAPIs";
import { getDocuments } from "@/api/InventoryAPIs";
import { rowsOf } from "@/api/InventoryAPIs";
import { printSheet } from "@/composables/fn";
import { documentStatuses, documentTypes, fail, jalali, ok, rial } from "@/composables/inventory";
import { hasRole, ServiceRoles } from "@/composables/roles";
import { badgeOfDecision, badgeOfServiceOrder, hoursText, labourText, orderKinds, orderStatuses, periodText, qtyText, rialText, serviceActionTitles, timeKinds, warrantyDecisions } from "@/composables/service";

const route = useRoute();
const router = useRouter();
const id = computed(() => Number(route.params.id) || 0);
const canWrite = hasRole(ServiceRoles.writers);
const isCeo = hasRole(ServiceRoles.goodwillDeciders);
const { employees, loadEmployees } = useEmployees();

const order = ref<any>(null);
const unit = ref<any>(null);
const form = ref<any>(null);
const busy = ref(false);
const prompt = ref<null | "cancel" | "goodwill">(null);
const decision = ref({ causeIsMisuse: false, note: "" });
const time = ref<any>({ employeeId: null, workDate: todayIso(), hours: "", kind: 2, note: "" });
const measure = ref({ name: "", value: "", unit: "", note: "" });
const pickEngineer = ref<number | null>(null);
const invoiceRate = ref("");

const open = computed(() => !!order.value && order.value.status >= 1 && order.value.status <= 3);
const editable = computed(() => canWrite && open.value);
const hasTimeSheet = computed(() => open.value && order.value.status !== 1);
const goodwillLocked = computed(() => order.value?.decision === 3 && !isCeo);
const warrantyPossible = computed(() => !!unit.value?.inWarranty);
const touchedHint = "شما این مورد را ثبت یا ویرایش کرده‌اید؛ تأیید با کاربر دیگر است";
const nameOf = (eid: number) => employees.value.find((e) => e.id === eid)?.name || order.value?.engineers?.find((e: any) => e.employeeId === eid)?.name || "#" + eid;

function toForm(o: any) {
  form.value = {
    title: o.title || "", kind: o.kind, plannedStart: isoDay(o.plannedStart), plannedEnd: isoDay(o.plannedEnd), travelNote: o.travelNote || "", findings: o.findings || "", recommendations: o.recommendations || "",
    runningHoursReading: o.runningHoursReading === null || o.runningHoursReading === undefined ? "" : String(o.runningHoursReading),
    billableLabourRateRial: o.billableLabourRateRial ? String(o.billableLabourRateRial) : "", reportDocumentId: o.reportDocumentId || null,
    engineers: (o.engineers || []).map((e: any) => e.employeeId), tasks: (o.tasks || []).map((t: any) => ({ description: t.description, isDone: t.isDone })),
  };
}
/** Takes the fresh order from the server. A row action (time, measurement, decision, goodwill, invoice) keeps the user's unsaved form fields; only an explicit save / load rebuilds the whole form. */
async function take(o: any, keepForm = false) {
  order.value = o;
  if (!keepForm) toForm(o);
  decision.value = { causeIsMisuse: !!o.causeIsMisuse, note: o.decisionNote || "" };
  invoiceRate.value = o.billableLabourRateRial ? String(o.billableLabourRateRial) : "";
}
async function load() {
  const [data, error] = await getSvOrder(id.value);
  if (error) return fail(error);
  await take(data);
  loadDocuments();
  // the warranty is judged on the day the request came in (warrantyTestedOn)
  const [u] = await getSvUnit(data.unitId, isoDay(data.warrantyTestedOn));
  unit.value = u || null;
}

function payload() {
  const f = form.value;
  return {
    id: order.value.id, requestId: order.value.requestId, unitId: order.value.unitId, kind: f.kind, title: f.title, plannedStart: f.plannedStart, plannedEnd: f.plannedEnd, travelNote: f.travelNote,
    engineerEmployeeIds: f.engineers, tasks: f.tasks.filter((t: any) => String(t.description).trim()), findings: f.findings, recommendations: f.recommendations,
    runningHoursReading: typedNumber(f.runningHoursReading), billableLabourRateRial: typedNumber(f.billableLabourRateRial), reportDocumentId: f.reportDocumentId, rowVersion: order.value.rowVersion,
  };
}
/** Saves the form; answers the fresh order, or null after the error was shown. */
async function persist(): Promise<any | null> {
  const [data, error] = await saveSvOrder(payload());
  if (error) {
    fail(error);
    return null;
  }
  return data;
}
async function save() {
  busy.value = true;
  const data = await persist();
  busy.value = false;
  if (!data) return;
  await take(data);
  ok("دستور خدمات ذخیره شد.");
}
async function reportAttached(guid: string | null) {
  form.value.reportDocumentId = guid;
  if (guid && editable.value) await save();
}

/** Warehouse documents linked to this order (GET inventory/documents?ServiceOrderId=). Roles without warehouse access just see no list. */
const documents = ref<any[]>([]);
async function loadDocuments() {
  const [data] = await getDocuments({ ServiceOrderId: id.value, PageSize: 100 });
  documents.value = data ? rowsOf(data) : [];
}

/** A row action that answers the fresh order. keepForm (default) leaves unsaved form fields alone; the order object, its rows and rowVersion always come from the server. */
async function run(call: () => Promise<any>, message: string, keepForm = true) {
  busy.value = true;
  const [data, error] = await call();
  busy.value = false;
  if (error) {
    fail(error);
    if (keepForm) {
      // refresh rows and rowVersion only, so a stale version does not stick, but do not wipe the edits
      const [fresh] = await getSvOrder(id.value);
      if (fresh) await take(fresh, true);
    } else await load();
    return false;
  }
  await take(data, keepForm);
  ok(message);
  return true;
}
async function act(action: "confirm" | "start" | "cancel" | "close", reason?: string) {
  busy.value = true;
  let rowVersion = order.value.rowVersion;
  if (editable.value && action !== "cancel") {
    const saved = await persist();
    if (!saved) {
      busy.value = false;
      return;
    }
    rowVersion = saved.rowVersion;
  }
  busy.value = false;
  if (await run(() => svOrderAction({ id: order.value.id, action, reason: reason || null, rowVersion }), serviceActionTitles[action] + " انجام شد.", false)) await load();
}
const decide = (d: number) => run(() => decideSvOrder({ orderId: order.value.id, decision: d, causeIsMisuse: decision.value.causeIsMisuse, note: decision.value.note, rowVersion: order.value.rowVersion }), "تصمیم ثبت شد.");
const askGoodwill = (reason: string) => run(() => requestSvGoodwill({ orderId: order.value.id, reason }), "درخواست حسن نیت برای مدیرعامل ارسال شد.");

async function addTime() {
  const hours = typedNumber(time.value.hours);
  if (!time.value.employeeId) return fail("مهندس را انتخاب کنید.");
  if (!hours || hours <= 0) return fail("ساعت کار را وارد کنید.");
  const ok1 = await run(() => saveSvTimeEntry({ orderId: order.value.id, employeeId: time.value.employeeId, workDate: time.value.workDate, hours, kind: time.value.kind, note: time.value.note || null }), "ساعت ثبت شد.");
  if (ok1) time.value = { ...time.value, hours: "", note: "" };
}
const dropTime = (rowId: number) => run(() => removeSvTimeEntry({ orderId: order.value.id, rowId }), "ردیف حذف شد.");
async function addMeasure() {
  const value = typedNumber(measure.value.value);
  if (!measure.value.name.trim() || !measure.value.unit.trim() || value === null) return fail("نام، مقدار و واحد اندازه‌گیری را وارد کنید.");
  const ok1 = await run(() => saveSvMeasurement({ orderId: order.value.id, name: measure.value.name, value, unit: measure.value.unit, note: measure.value.note || null }), "اندازه‌گیری ثبت شد.");
  if (ok1) measure.value = { name: "", value: "", unit: "", note: "" };
}
const dropMeasure = (rowId: number) => run(() => removeSvMeasurement({ orderId: order.value.id, rowId }), "ردیف حذف شد.");
const makeInvoice = () => run(() => draftSvInvoice({ id: order.value.id, billableLabourRateRial: typedNumber(invoiceRate.value) }), "پیش‌نویس فاکتور بررسی شد.");

function addEngineer() {
  if (pickEngineer.value && !form.value.engineers.includes(pickEngineer.value)) form.value.engineers.push(pickEngineer.value);
  pickEngineer.value = null;
}

watch(id, load);
onMounted(() => {
  loadEmployees();
  load();
});
</script>

<template>
  <div class="inv-page">
    <div v-if="order && form" class="inv-card screen-only">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title mb-0">دستور خدمات <span class="inv-ltr">{{ order.number }}</span></h1>
          <span :class="badgeOfServiceOrder(order.status)" class="inv-badge">{{ orderStatuses[order.status] }}</span>
          <span class="inv-badge info ms-1">{{ orderKinds[order.kind] }}</span>
          <span :class="badgeOfDecision(order.decision)" class="inv-badge ms-1">{{ warrantyDecisions[order.decision] }}</span>
          <span v-if="order.ltsaNumber" class="inv-badge info ms-1">بازدید LTSA <span class="inv-ltr">{{ order.ltsaNumber }}</span></span>
        </div>
        <div class="d-flex gap-2">
          <button class="btn btn-sm btn-outline-secondary" @click="printSheet()">چاپ گزارش خدمات</button>
          <button class="btn btn-sm btn-outline-secondary" @click="router.push('/service/orders')">بازگشت به فهرست</button>
        </div>
      </div>

      <div class="small mb-3">
        <span class="me-3"><b>مشتری:</b> {{ order.customerName || "-" }}</span>
        <span class="me-3"><b>واحد:</b> <a href="#" @click.prevent="router.push('/service/units/' + order.unitId)"><span class="inv-ltr">{{ order.unitTag }}</span></a></span>
        <span v-if="order.requestId" class="me-3"><b>درخواست:</b> <a href="#" @click.prevent="router.push('/service/requests/' + order.requestId)"><span class="inv-ltr">{{ order.requestNumber }}</span></a></span>
        <span class="me-3"><b>مرکز هزینه:</b> <span class="inv-ltr">{{ order.costCenterCode }}</span></span>
        <span v-if="order.startedAtText" class="me-3"><b>شروع کار:</b> {{ order.startedAtText }}</span>
        <span v-if="order.closedAtText" class="me-3"><b>بسته شد:</b> {{ order.closedAtText }}</span>
      </div>

      <!-- actions by state -->
      <div v-if="canWrite && open" class="d-flex gap-2 flex-wrap mb-3">
        <button v-if="order.status === 1" :disabled="busy" class="btn btn-sm btn-primary" @click="act('confirm')">تایید دستور (برنامه‌ریزی)</button>
        <button v-if="order.status === 2" :disabled="busy" class="btn btn-sm btn-primary" @click="act('start')">شروع کار</button>
        <button v-if="order.status === 3" :disabled="busy" class="btn btn-sm btn-success" @click="act('close')">بستن دستور</button>
        <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="prompt = 'cancel'">لغو دستور</button>
      </div>
      <p v-if="order.status === 3 && canWrite" class="inv-muted">بستن نیازمند گزارش امضاشده توسط مشتری و تصمیم گارانتی است (بازدید LTSA مستثناست)؛ تغییرات فرم پیش از اقدام ذخیره می‌شود.</p>

      <!-- main form -->
      <div class="row g-2 mb-3">
        <div class="col-md-8"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" :disabled="!editable" class="form-control form-control-sm" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">نوع</label>
          <select v-model="form.kind" :disabled="!editable" class="form-select form-select-sm"><option v-for="(t, k) in orderKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-3"><label class="form-label small mb-0">شروع برنامه</label><FnDate v-model="form.plannedStart" :disabled="!editable" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">پایان برنامه</label><FnDate v-model="form.plannedEnd" :disabled="!editable" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">یادداشت سفر</label><input v-model="form.travelNote" :disabled="!editable" class="form-control form-control-sm" /></div>

        <div class="col-12">
          <label class="form-label small mb-0">مهندسان</label>
          <div class="d-flex gap-2 flex-wrap align-items-center">
            <template v-if="editable">
              <select v-model="pickEngineer" class="form-select form-select-sm" style="max-width: 18rem"><option :value="null">انتخاب...</option><option v-for="e in employees" :key="e.id" :value="e.id">{{ e.name }}<template v-if="e.jobTitle"> - {{ e.jobTitle }}</template></option></select>
              <button class="btn btn-sm btn-outline-secondary" @click="addEngineer">افزودن</button>
            </template>
            <span v-for="eid in form.engineers" :key="eid" class="inv-badge info">{{ nameOf(eid) }} <a v-if="editable" href="#" @click.prevent="form.engineers.splice(form.engineers.indexOf(eid), 1)">×</a></span>
            <span v-if="!form.engineers.length" class="inv-muted">مهندسی انتخاب نشده است (برای شروع کار حداقل یکی لازم است).</span>
          </div>
        </div>

        <div class="col-12">
          <label class="form-label small mb-0">وظایف</label>
          <div v-for="(t, i) in form.tasks" :key="i" class="d-flex gap-2 mb-1 align-items-center">
            <input v-model="t.isDone" :disabled="!editable" type="checkbox" class="form-check-input mt-0" />
            <input v-model="t.description" :disabled="!editable" class="form-control form-control-sm" />
            <button v-if="editable" class="btn btn-sm btn-outline-danger" @click="form.tasks.splice(i, 1)">حذف</button>
          </div>
          <button v-if="editable" class="btn btn-sm btn-outline-secondary" @click="form.tasks.push({ description: '', isDone: false })">افزودن وظیفه</button>
        </div>

        <div class="col-md-6"><label class="form-label small mb-0">یافته‌ها</label><textarea v-model="form.findings" :disabled="!editable" class="form-control form-control-sm" rows="3"></textarea></div>
        <div class="col-md-6"><label class="form-label small mb-0">توصیه‌ها</label><textarea v-model="form.recommendations" :disabled="!editable" class="form-control form-control-sm" rows="3"></textarea></div>
        <div class="col-md-3"><label class="form-label small mb-0">کارکرد واحد (ساعت) در این بازدید</label><input v-model="form.runningHoursReading" :disabled="!editable" class="form-control form-control-sm fn-num" inputmode="decimal" /></div>
        <div class="col-md-3">
          <label class="form-label small mb-0">نرخ ساعتی صورتحساب (ریال)</label>
          <input v-model="form.billableLabourRateRial" :disabled="!editable" class="form-control form-control-sm fn-num" inputmode="numeric" />
          <div v-if="typedNumber(form.billableLabourRateRial)" class="inv-muted">{{ rial(typedNumber(form.billableLabourRateRial)) }} ریال</div>
        </div>
        <div class="col-md-6">
          <label class="form-label small mb-0">گزارش خدمات امضاشده توسط مشتری (برای بستن الزامی)</label>
          <DocAttach :model-value="form.reportDocumentId" :disabled="!editable" title="گزارش خدمات امضاشده" @update:model-value="reportAttached" />
        </div>
      </div>
      <div v-if="editable" class="mb-4"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره تغییرات</button></div>

      <!-- warranty decision -->
      <h3 class="inv-title mb-2">تصمیم گارانتی</h3>
      <div class="border rounded p-2 mb-4">
        <div class="small mb-2">
          <span :class="badgeOfDecision(order.decision)" class="inv-badge">{{ warrantyDecisions[order.decision] }}</span>
          <span v-if="order.decidedAtText" class="ms-2">تاریخ تصمیم: {{ order.decidedAtText }}</span>
          <span v-if="order.decision === 1 || order.decision === 2" class="ms-2">سوءاستفاده مشتری: {{ order.causeIsMisuse ? "بله" : "خیر" }}</span>
          <span v-if="order.decisionNote" class="ms-2">یادداشت: {{ order.decisionNote }}</span>
          <div>وضعیت واحد در روز بررسی ({{ order.warrantyTestedOnText || "-" }}): <b>{{ unit ? (unit.inWarranty ? "در گارانتی" : "خارج از گارانتی") : "-" }}</b></div>
          <div v-if="order.goodwillRequested" class="mt-1">درخواست حسن نیت: {{ order.goodwillReason }} <span v-if="order.decision !== 3" class="inv-badge warn">منتظر تصمیم مدیرعامل</span></div>
        </div>
        <template v-if="open">
          <p v-if="goodwillLocked" class="inv-muted mb-0">تصمیم حسن نیت گرفته شده و فقط مدیرعامل آن را تغییر می‌دهد.</p>
          <template v-else-if="canWrite || isCeo">
            <div class="d-flex gap-3 flex-wrap align-items-center mb-2">
              <label class="form-check mb-0"><input v-model="decision.causeIsMisuse" class="form-check-input" type="checkbox" /> <span class="form-check-label">علت، سوءاستفاده یا استفاده نادرست مشتری است</span></label>
              <input v-model="decision.note" class="form-control form-control-sm" placeholder="یادداشت تصمیم" style="max-width: 22rem" />
            </div>
            <div class="d-flex gap-2 flex-wrap">
              <template v-if="canWrite">
                <template v-if="!order.touchedByMe">
                  <button :disabled="busy || !warrantyPossible || decision.causeIsMisuse" class="btn btn-sm btn-outline-success" :title="warrantyPossible ? '' : 'واحد در روز بررسی در گارانتی نبوده است'" @click="decide(1)">گارانتی</button>
                  <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="decide(2)">قابل‌فاکتور</button>
                </template>
                <span v-else class="inv-muted">{{ touchedHint }}</span>
                <button v-if="!order.goodwillRequested" :disabled="busy" class="btn btn-sm btn-outline-warning" @click="prompt = 'goodwill'">درخواست حسن نیت از مدیرعامل</button>
              </template>
              <button v-if="isCeo && order.goodwillRequested" :disabled="busy" class="btn btn-sm btn-warning" @click="decide(3)">تصمیم حسن نیت (مدیرعامل)</button>
            </div>
            <p class="inv-muted mt-2 mb-0">نویسندهٔ دستور نمی‌تواند خودش تصمیم گارانتی یا قابل‌فاکتور بگیرد؛ نفر دیگری از خدمات تصمیم می‌گیرد. گارانتی فقط برای واحد در گارانتی و علتی غیر از سوءاستفاده ممکن است.</p>
          </template>
        </template>
      </div>

      <!-- time sheet -->
      <h3 class="inv-title mb-2">ساعت کار</h3>
      <div class="small mb-1">سفر: {{ hoursText(order.travelHours) }} | کار: {{ hoursText(order.workHours) }} | انتظار: {{ hoursText(order.waitingHours) }} | جمع: {{ hoursText(order.totalHours) }}</div>
      <div class="table-responsive mb-2">
        <table class="table table-sm">
          <thead><tr><th>مهندس</th><th>تاریخ</th><th>نوع</th><th>ساعت</th><th>توضیح</th><th v-if="hasTimeSheet && canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!(order.timeEntries || []).length"><td colspan="6" class="text-center inv-muted">ساعتی ثبت نشده است.</td></tr>
            <tr v-for="t in order.timeEntries" :key="t.id">
              <td>{{ t.employeeName }}</td><td>{{ t.workDateText }}</td><td>{{ timeKinds[t.kind] }}</td><td>{{ hoursText(t.hours) }}</td><td>{{ t.note }}</td>
              <td v-if="hasTimeSheet && canWrite"><button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="dropTime(t.id)">حذف</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-if="hasTimeSheet && canWrite" class="d-flex gap-2 align-items-end flex-wrap mb-4">
        <div><label class="form-label small mb-0">مهندس</label>
          <select v-model="time.employeeId" class="form-select form-select-sm"><option :value="null">انتخاب...</option><option v-for="e in order.engineers" :key="e.employeeId" :value="e.employeeId">{{ e.name }}</option></select></div>
        <div><label class="form-label small mb-0">تاریخ</label><FnDate v-model="time.workDate" /></div>
        <div><label class="form-label small mb-0">نوع</label><select v-model="time.kind" class="form-select form-select-sm"><option v-for="(t, k) in timeKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div><label class="form-label small mb-0">ساعت (مثلا ۱٫۵)</label><input v-model="time.hours" class="form-control form-control-sm fn-num" inputmode="decimal" style="max-width: 7rem" /></div>
        <div class="flex-grow-1"><label class="form-label small mb-0">توضیح</label><input v-model="time.note" class="form-control form-control-sm" /></div>
        <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="addTime">ثبت ساعت</button>
      </div>
      <p v-else-if="order.status === 1" class="inv-muted">ثبت ساعت پس از تایید دستور ممکن است.</p>

      <!-- parts -->
      <h3 class="inv-title mb-2">قطعات مصرفی</h3>
      <div class="table-responsive mb-1">
        <table class="table table-sm">
          <thead><tr><th>سند</th><th>نوع</th><th>وضعیت</th><th>قطعات</th></tr></thead>
          <tbody>
            <tr v-if="!(order.parts || []).length"><td colspan="4" class="text-center inv-muted">سندی برای این دستور نیست.</td></tr>
            <tr v-for="p in order.parts" :key="p.documentId">
              <td><a href="#" @click.prevent="router.push('/inventory/documents/' + p.documentId)"><span class="inv-ltr">{{ p.number || "پیش‌نویس " + p.documentId }}</span></a></td>
              <td>{{ documentTypes[p.type] }}</td><td>{{ documentStatuses[p.status] }}</td>
              <td><div v-for="(l, i) in p.lines" :key="i"><span class="inv-ltr">{{ l.itemCode }}</span> {{ l.itemName }} × {{ qtyText(l.quantity) }}</div></td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-if="canWrite" class="small mb-2">
        سند جدید انبار برای این دستور (به دستور وصل می‌شود):
        <router-link :to="{ name: 'InventoryDocumentEdit', params: { id: 'new' }, query: { type: '4', serviceOrder: String(order.id) } }">حواله مصرف قطعات (درخواست مواد تأییدشده را در صفحهٔ سند انتخاب کنید)</router-link>
        <span class="mx-1">|</span>
        <router-link :to="{ name: 'InventoryDocumentEdit', params: { id: 'new' }, query: { type: '11', serviceOrder: String(order.id) } }">تحویل به مشتری (قطعات باقی‌مانده در محل)</router-link>
      </p>
      <div class="table-responsive mb-4">
        <table class="table table-sm">
          <thead><tr><th>اسناد انبار این دستور</th><th>نوع</th><th>وضعیت</th><th>تاریخ</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!documents.length"><td colspan="5" class="text-center inv-muted">سند انباری برای این دستور ثبت نشده است.</td></tr>
            <tr v-for="d in documents" :key="d.id">
              <td class="inv-ltr">{{ d.number || "(پیش‌نویس)" }}</td><td>{{ documentTypes[d.type] }}</td><td>{{ documentStatuses[d.status] }}</td><td>{{ jalali(d.documentDate) }}</td>
              <td><router-link :to="{ name: 'InventoryDocumentEdit', params: { id: d.id } }">باز کردن</router-link></td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- measurements -->
      <h3 class="inv-title mb-2">اندازه‌گیری‌ها</h3>
      <div class="table-responsive mb-2">
        <table class="table table-sm">
          <thead><tr><th>نام</th><th>مقدار</th><th>واحد</th><th>توضیح</th><th v-if="open && canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!(order.measurements || []).length"><td colspan="5" class="text-center inv-muted">اندازه‌گیری‌ای ثبت نشده است.</td></tr>
            <tr v-for="m in order.measurements" :key="m.id">
              <td>{{ m.name }}</td><td>{{ qtyText(m.value) }}</td><td>{{ m.unit }}</td><td>{{ m.note }}</td>
              <td v-if="open && canWrite"><button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="dropMeasure(m.id)">حذف</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-if="open && canWrite" class="d-flex gap-2 align-items-end flex-wrap mb-4">
        <div><label class="form-label small mb-0">نام (مثلا ارتعاش)</label><input v-model="measure.name" class="form-control form-control-sm" /></div>
        <div><label class="form-label small mb-0">مقدار</label><input v-model="measure.value" class="form-control form-control-sm fn-num" inputmode="decimal" style="max-width: 8rem" /></div>
        <div><label class="form-label small mb-0">واحد</label><input v-model="measure.unit" class="form-control form-control-sm" style="max-width: 7rem" /></div>
        <div class="flex-grow-1"><label class="form-label small mb-0">توضیح</label><input v-model="measure.note" class="form-control form-control-sm" /></div>
        <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="addMeasure">افزودن</button>
      </div>

      <!-- cost and invoice -->
      <h3 class="inv-title mb-2">هزینه و فاکتور</h3>
      <div class="row g-2 small mb-2">
        <div class="col-md-3"><b>ساعت دستمزد:</b> {{ hoursText(order.labourHours) }}</div>
        <div class="col-md-3"><b>دستمزد (ریال):</b> {{ labourText(order.labourAmount, order.labourHidden) }}</div>
        <div class="col-md-3"><b>قطعات (ریال):</b> {{ rialText(order.partsAmount) }}</div>
        <div class="col-md-3"><b>جمع هزینه (ریال):</b> {{ labourText(order.totalCost, order.labourHidden) }}</div>
        <div class="col-12">
          <span v-if="order.costPosted" class="inv-badge ok">هزینه به حسابداری رفته است</span>
          <span v-if="order.labourAggregatePending" class="inv-badge warn ms-1">دستمزد در ثبت ماهانهٔ تجمیعی می‌آید<template v-if="order.labourAggregatePeriod"> ({{ periodText(order.labourAggregatePeriod) }})</template></span>
        </div>
      </div>
      <div v-if="order.decision === 2" class="border rounded p-2">
        <div v-if="order.invoiceId" class="small">پیش‌نویس فاکتور فروش: <span class="inv-ltr">{{ order.invoiceNumber || order.invoiceId }}</span></div>
        <template v-else-if="order.status === 4 && canWrite">
          <div class="d-flex gap-2 align-items-end flex-wrap">
            <div>
              <label class="form-label small mb-0">نرخ ساعتی صورتحساب (ریال)</label>
              <input v-model="invoiceRate" class="form-control form-control-sm fn-num" inputmode="numeric" />
              <div v-if="typedNumber(invoiceRate)" class="inv-muted">{{ rial(typedNumber(invoiceRate)) }} ریال</div>
            </div>
            <button :disabled="busy" class="btn btn-sm btn-primary" @click="makeInvoice">ساخت پیش‌نویس فاکتور</button>
          </div>
        </template>
        <p v-else class="inv-muted mb-0">پیش‌نویس فاکتور پس از بستن دستور قابل‌فاکتور ساخته می‌شود.</p>
        <p v-if="order.draftNote" class="small text-danger mt-2 mb-0">{{ order.draftNote }}</p>
      </div>
    </div>

    <OrderPrint v-if="order" :o="order" />

    <PromptModal v-if="prompt === 'cancel'" title="لغو دستور خدمات" confirm-text="لغو دستور" :fields="[{ key: 'reason', label: 'دلیل لغو', type: 'textarea', required: true }]"
      @cancel="prompt = null" @confirm="(v: any) => { prompt = null; act('cancel', v.reason); }" />
    <PromptModal v-if="prompt === 'goodwill'" title="درخواست حسن نیت" confirm-text="ارسال برای مدیرعامل" hint="تصمیم حسن نیت فقط با مدیرعامل است."
      :fields="[{ key: 'reason', label: 'دلیل درخواست', type: 'textarea', required: true }]" @cancel="prompt = null" @confirm="(v: any) => { prompt = null; askGoodwill(v.reason); }" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
