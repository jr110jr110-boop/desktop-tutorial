<script setup lang="ts">
import { nextTick } from "vue";
import { printForm } from "@/composables/quality";
import ItpPrint from "@/components/quality/print/ItpPrint.vue";
import { computed, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import { downloadFile } from "@/api/InventoryAPIs";
import { addItpRelease, getItp, itpAction, itpActivityAction, saveItp, saveItpActivity, setItpLimits } from "@/api/QualityAPIs";
import DocAttach from "@/components/quality/DocAttach.vue";
import LetterPicker from "@/components/quality/LetterPicker.vue";
import { fail, jalali, ok, today } from "@/composables/inventory";
import {
  activityPoints,
  activityStatuses,
  dateInput,
  itpStatuses,
  loadSettingValues,
  pointHints,
  points,
  releaseActions,
  releaseParties,
  reportTypes,
  settingValue,
  testTypes,
  useBackdateWindow,
} from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// One ITP: activities with V / TPI / C points, notices, releases, acceptance limits and the plan actions.
const route = useRoute();
const itp = ref<any>(null);
const busy = ref(false);
async function doPrint() {
  await nextTick();
  printForm();
}
const selectedId = ref<number | null>(null);
const edit = ref<any>(null);
const approve = ref({ reference: "", date: today() });
const reviseReason = ref("");
// activity actions: the notice (an optional outgoing letter), the planned date, the proof that the work was done, and "not applicable" (a reason; the client's letter for a client / third-party point)
const notifyLetterId = ref<number | null>(null);
const notifyNote = ref("");
const planDate = ref(today());
const doneNote = ref("");
const doneEvidence = ref<string | null>(null);
const na = ref<any>(null);
// R1q H6: whoever saved, revised or submitted the plan (in this page) is its maker: the API does not let him record the client's approval; the refusal is kept on the screen
const touchedByMe = ref(false);
const approveError = ref("");
const minNoticeDays = settingValue("MinNoticeDays", 1);
// the date of the client's approval is today or a day inside the back-date window
const { min: approvalMin, max: approvalMax, warning: approvalWarning } = useBackdateWindow(false);
const release = ref({ party: 3, action: 1, personName: "", organisation: "", releaseDate: today(), reference: "" });
const limitEdit = ref<Record<number, { min: any; max: any; source: string }>>({});

const canPlan = hasRole(QualityRoles.planners);
const canQc = hasRole(QualityRoles.inspectors);
const canManage = hasRole(QualityRoles.managers);
const canLimits = hasRole(QualityRoles.limits);
const approved = computed(() => itp.value && (itp.value.status === 2 || itp.value.status === 3));
// a witness or hold point of the client or the third-party inspector is dropped only with the client's own letter (ItpNoticeRules.ThirdPartyControlled)
const thirdPartyControlled = (a: any) => [1, 2].includes(a.pointTpi) || [1, 2].includes(a.pointClient);
const closed = computed(() => itp.value?.status === 4);
const selected = computed(() => (itp.value?.activities || []).find((a: any) => a.id === selectedId.value) || null);
const terminal = (a: any) => [2, 3, 4].includes(a.status);

function load(dto: any) {
  itp.value = dto;
  limitEdit.value = {};
}

async function run(promise: Promise<any>, message = "انجام شد.") {
  busy.value = true;
  const [data, error] = await promise;
  busy.value = false;
  if (error) {
    fail(error);
    return false;
  }
  load(data);
  ok(message);
  return true;
}

async function saveNotice() {
  const days = Number(itp.value.noticeDays);
  if (!Number.isInteger(days) || days < minNoticeDays.value || days > 365) return fail(`مهلت اعلام نقطه شاهد باید بین ${minNoticeDays.value} تا 365 روز باشد.`);
  if (await run(saveItp({ id: itp.value.id, noticeDays: days, note: itp.value.note }), "ذخیره شد.")) touchedByMe.value = true;
}

async function planAction(action: string, extra: any = {}) {
  const done = await run(itpAction(itp.value.id, action, extra));
  // submit and revise make the user a maker of the plan (the approval of the client is recorded by somebody else)
  if (done && (action === "submit" || action === "revise")) touchedByMe.value = true;
  return done;
}

async function clientApprove() {
  if (!approve.value.reference.trim()) return fail("شماره نامه یا مرجع تایید کارفرما را وارد کنید.");
  approveError.value = "";
  busy.value = true;
  const [data, error] = await itpAction(itp.value.id, "client-approve", { reference: approve.value.reference.trim(), date: approve.value.date });
  busy.value = false;
  if (error) {
    // the API's own text (separation of duties, date window...) stays on the screen next to the button
    approveError.value = error?.message || String(error);
    return fail(error);
  }
  load(data);
  ok("تایید کارفرما ثبت شد.");
}

function revise() {
  if (!reviseReason.value.trim()) return fail("دلیل بازنگری را بنویسید.");
  return planAction("revise", { reason: reviseReason.value }).then(() => (reviseReason.value = ""));
}

function newActivity() {
  const next = Math.max(0, ...(itp.value.activities || []).map((a: any) => a.seq)) + 1;
  edit.value = {
    id: null, itpId: itp.value.id, seq: next, itemNo: "", stage: "", activity: "", testType: 4, acceptanceReference: "", pointVendor: 0, pointTpi: 0, pointClient: 0,
    documentProduced: "", requiredReportType: null, remark: "", gatesRelease: true, plannedDate: null, reason: "", parameters: [],
  };
}

function editActivity(a: any) {
  edit.value = {
    ...JSON.parse(JSON.stringify(a)),
    itpId: itp.value.id,
    plannedDate: dateInput(a.plannedDate),
    reason: "",
    parameters: (a.parameters || []).map((p: any) => ({ name: p.name, unit: p.unit, min: p.min, max: p.max, source: p.source, requiresLimits: p.requiresLimits })),
  };
}

async function saveActivity() {
  const a = edit.value;
  if (!a.activity?.trim()) return fail("شرح فعالیت را بنویسید.");
  if (approved.value && !a.reason?.trim()) return fail("برنامه تاییدشده است؛ دلیل تغییر را بنویسید (بازنگری جدید ساخته می‌شود).");
  if (await run(saveItpActivity({
        ...a,
        seq: Number(a.seq) || 0,
        requiredReportType: a.requiredReportType || null,
        plannedDate: a.plannedDate || null,
        parameters: a.parameters.map((p: any) => ({ ...p, min: p.min === "" ? null : p.min, max: p.max === "" ? null : p.max })),
      }))) {
    edit.value = null;
    touchedByMe.value = true;
  }
}

/** Witness / hold notice: the date is the day of the entry, or the day the quoted outgoing letter was sent; a date is never typed (the API refuses a past one). */
async function notifyActivity() {
  const done = await run(itpActivityAction(selected.value.id, "notify", { letterId: notifyLetterId.value, note: notifyNote.value.trim() || null }), "اعلام ثبت شد.");
  if (done) {
    notifyLetterId.value = null;
    notifyNote.value = "";
  }
}

async function planActivity() {
  if (!planDate.value) return fail("تاریخ برنامه را وارد کنید.");
  await run(itpActivityAction(selected.value.id, "plan", { date: planDate.value }), "تاریخ برنامه ثبت شد.");
}

/** Done: an activity without a required test report needs proof, an accepted inspection record on it or an uploaded document (the note describes it). */
async function doneActivity() {
  const done = await run(
    itpActivityAction(selected.value.id, "done", { note: doneNote.value.trim() || null, evidenceDocumentId: selected.value.requiredReportType ? null : doneEvidence.value || null }),
    "فعالیت انجام‌شده ثبت شد."
  );
  if (done) {
    doneNote.value = "";
    doneEvidence.value = null;
  }
}

function openNotApplicable() {
  na.value = { note: "", letterId: null as number | null, reference: "", thirdParty: thirdPartyControlled(selected.value) };
}

async function submitNotApplicable() {
  const n = na.value;
  if (!n.note.trim()) return fail("دلیل غیرقابل‌اجرا بودن فعالیت را بنویسید.");
  if (n.thirdParty && !n.letterId) return fail("نقطه توقف یا شاهد کارفرما/بازرس ثالث را فقط با نامه موافقت کارفرما می‌توان غیرقابل‌اجرا کرد؛ نامه وارده کارفرما را انتخاب کنید.");
  const done = await run(
    itpActivityAction(selected.value.id, "not-applicable", { note: n.note.trim(), letterId: n.thirdParty ? n.letterId : null, reference: n.thirdParty ? n.reference.trim() || null : null }),
    n.thirdParty ? "درخواست ثبت شد؛ فعالیت تا تایید برنامه بازنگری‌شده توسط کارفرما باز می‌ماند." : "فعالیت غیرقابل‌اجرا شد."
  );
  if (done) {
    na.value = null;
    // a client / third-party point makes a revision of the plan: the user is its maker
    if (n.thirdParty) touchedByMe.value = true;
  }
}

async function addRelease() {
  if (!release.value.personName.trim()) return fail("نام شخص رهاکننده را وارد کنید.");
  if (await run(addItpRelease({ ...release.value, activityId: selected.value.id }), "رهاسازی ثبت شد.")) release.value.reference = "";
}

async function saveLimits(p: any) {
  const e = limitEdit.value[p.id];
  const min = e.min === "" || e.min === null ? null : Number(e.min);
  const max = e.max === "" || e.max === null ? null : Number(e.max);
  if (await run(setItpLimits({ parameterId: p.id, min, max, source: e.source || null }), "حدود ثبت شد.")) touchedByMe.value = true;
}

function startLimits(p: any) {
  limitEdit.value[p.id] = { min: p.min, max: p.max, source: p.source || "" };
}

function addParameter() {
  edit.value.parameters.push({ name: "", unit: "", min: null, max: null, source: "", requiresLimits: false });
}

onMounted(async () => {
  const [data, error] = await getItp(Number(route.params.id));
  if (error) return fail(error);
  load(data);
  loadSettingValues();
});
</script>

<template>
  <div v-if="itp" class="inv-page">
    <ItpPrint :itp="itp" />
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">برنامه بازرسی و آزمون <span class="inv-ltr ms-2">{{ itp.number }}</span></h1>
          <span class="inv-badge" :class="{ ok: approved, warn: itp.status === 1 }">{{ itpStatuses[itp.status] }}</span>
          <span class="inv-muted ms-2">بازنگری {{ itp.revision }} — دستور ساخت <span class="inv-ltr">{{ itp.workOrderCode }}</span> — قرارداد <span class="inv-ltr">{{ itp.contractCode }}</span> — قالب <span class="inv-ltr">{{ itp.templateCode }}</span></span>
          <span v-if="itp.autoDrafted" class="inv-badge info ms-2">پیش‌نویس سیستم؛ بررسی و تایید با انسان</span>
        </div>
        <div class="d-flex flex-wrap gap-2">
          <router-link :to="{ name: 'QualityItps' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button class="btn btn-sm btn-outline-secondary" @click="doPrint">چاپ ITP</button>
          <router-link :to="{ name: 'QualityReleaseNotes', query: { workOrderId: itp.workOrderId } }" class="btn btn-sm btn-outline-secondary">برگه‌های ترخیص</router-link>
        </div>
      </div>

      <div class="row g-2 mb-2">
        <div class="col-md-2">
          <label class="form-label small mb-0">روزهای اطلاع نقطه شاهد</label>
          <input v-model.number="itp.noticeDays" :disabled="!canPlan || closed || approved" :min="minNoticeDays" class="form-control form-control-sm" max="365" type="number" />
          <span v-if="approved" class="inv-muted small">پس از تایید کارفرما قفل است؛ با بازنگری برنامه تغییر می‌کند.</span>
          <span v-else class="inv-muted small">حداقل {{ minNoticeDays }} روز (تنظیمات کیفیت)</span>
        </div>
        <div class="col-md-6">
          <label class="form-label small mb-0">توضیح</label>
          <input v-model="itp.note" :disabled="!canPlan || closed" class="form-control form-control-sm" />
        </div>
        <div class="col-md-2 d-flex align-items-end"><button v-if="canPlan && !closed" :disabled="busy" class="btn btn-sm btn-primary" @click="saveNotice">ذخیره</button></div>
        <div class="col-md-12 small" v-if="itp.clientApprovalReference">تایید کارفرما: <b class="inv-ltr">{{ itp.clientApprovalReference }}</b> — {{ jalali(itp.clientApprovalDate) }}</div>
      </div>

      <div class="d-flex flex-wrap gap-2 align-items-end">
        <button v-if="itp.status === 0 && canPlan" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="planAction('submit')">ارسال برای تایید کارفرما</button>
        <template v-if="itp.status === 1 && canManage">
          <template v-if="!touchedByMe">
            <input v-model="approve.reference" class="form-control form-control-sm" maxlength="300" placeholder="شماره نامه تایید کارفرما *" style="max-width: 220px" />
            <input v-model="approve.date" :max="approvalMax" :min="approvalMin" class="form-control form-control-sm" style="max-width: 160px" type="date" />
            <button :disabled="busy" class="btn btn-sm btn-success" @click="clientApprove">ثبت تایید کارفرما</button>
          </template>
          <span v-else class="small text-warning-emphasis">شما این برنامه را تهیه، ویرایش، بازنگری یا ارسال کرده‌اید؛ تایید کارفرما را شخص دیگری (از مدیران QA / QC) ثبت می‌کند.</span>
          <span v-if="!touchedByMe && approvalWarning(approve.date)" class="small text-danger">{{ approvalWarning(approve.date) }}</span>
        </template>
        <button v-if="itp.status === 2 && canManage" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="planAction('use')">در حال اجرا</button>
        <button v-if="approved && canManage" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="planAction('close')">بستن برنامه</button>
        <template v-if="approved && canPlan">
          <input v-model="reviseReason" class="form-control form-control-sm" placeholder="دلیل بازنگری" style="max-width: 240px" />
          <button :disabled="busy" class="btn btn-sm btn-outline-warning" @click="revise">بازنگری جدید</button>
        </template>
      </div>
      <p v-if="approveError" class="small text-danger mt-2 mb-0">{{ approveError }}</p>
      <p class="inv-muted mt-2 mb-0">
        پس از تایید کارفرما هر تغییر برنامه، بازنگری جدید می‌سازد و تاریخچه نگه داشته می‌شود. نقطه کنترل پیش از تایید کارفرما انجام‌شده ثبت نمی‌شود.
        تایید کارفرما را کسی ثبت می‌کند که برنامه را تهیه، ویرایش، بازنگری یا ارسال نکرده است و تاریخ آن امروز یا در بازه مجاز ثبت با تاخیر است.
      </p>
    </div>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h2 class="inv-title">فعالیت‌ها</h2>
        <button v-if="canPlan && !closed" class="btn btn-sm btn-primary" @click="newActivity">فعالیت جدید</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>ردیف</th><th>مرحله</th><th>فعالیت</th><th>نوع</th><th>مرجع پذیرش</th><th>نقطه‌ها</th><th>گزارش لازم</th><th>وضعیت</th><th>برنامه</th><th>مهلت شاهد</th><th>مانع‌ها</th></tr></thead>
          <tbody>
            <tr v-for="a in itp.activities" :key="a.id" :class="{ 'table-active': a.id === selectedId }" role="button" @click="selectedId = a.id">
              <td class="inv-ltr">{{ a.itemNo || a.seq }}</td>
              <td>{{ a.stage }}</td>
              <td>{{ a.activity }}</td>
              <td>{{ testTypes[a.testType] }}</td>
              <td class="inv-ltr">{{ a.acceptanceReference }}</td>
              <td class="inv-ltr">{{ activityPoints(a) }}</td>
              <td>{{ a.requiredReportType ? reportTypes[a.requiredReportType] : "—" }}</td>
              <td>
                <span :class="{ ok: a.status === 2, warn: a.status === 1, info: a.status === 3 }" class="inv-badge">{{ activityStatuses[a.status] }}</span>
                <span v-if="a.notApplicablePending" class="inv-badge warn ms-1" title="نامه موافقت کارفرما ثبت شده؛ فعالیت تا تایید برنامه بازنگری‌شده باز است">درخواست غیرقابل‌اجرا</span>
                <span v-if="a.holdPending" class="inv-badge bad ms-1">H</span>
              </td>
              <td>{{ jalali(a.plannedDate) }}</td>
              <td>{{ jalali(a.witnessDeadline) }}</td>
              <td class="small">{{ (a.blockers || []).join(" | ") }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <div v-if="selected" class="inv-card screen-only">
      <div class="inv-toolbar">
        <h2 class="inv-title">{{ selected.itemNo || selected.seq }} — {{ selected.activity }}</h2>
        <button v-if="canPlan && !closed && !terminal(selected)" class="btn btn-sm btn-outline-secondary" @click="editActivity(selected)">ویرایش فعالیت</button>
      </div>
      <p class="small mb-2">
        مدرک تولیدشده: {{ selected.documentProduced || "—" }} — توضیح: {{ selected.remark || "—" }} —
        {{ selected.gatesRelease ? "شرط ترخیص است" : "شرط ترخیص نیست" }}
      </p>
      <ul v-if="selected.notificationSentAt || selected.notApplicableReference || selected.notApplicableLetterNumber || selected.notApplicablePending || selected.evidenceDocumentId || selected.evidenceNote || selected.doneAt" class="small mb-2">
        <li v-if="selected.notificationSentAt">
          اعلام به کارفرما / TPI: {{ jalali(selected.notificationSentAt) }}
          <template v-if="selected.notificationLetterNumber">— نامه صادره <b class="inv-ltr">{{ selected.notificationLetterNumber }}</b></template>
          <template v-else>(ثبت در سامانه)</template>
          <template v-if="selected.witnessDeadline">— مهلت شاهد: {{ jalali(selected.witnessDeadline) }}</template>
        </li>
        <li v-if="selected.notApplicablePending" class="text-warning-emphasis">
          درخواست غیرقابل‌اجرا شدن با نامه کارفرما <b class="inv-ltr">{{ selected.notApplicableLetterNumber || selected.notApplicableReference }}</b> ثبت شده؛ فعالیت تا تایید برنامه بازنگری‌شده توسط کارفرما باز می‌ماند.
        </li>
        <li v-else-if="selected.notApplicableReference || selected.notApplicableLetterNumber">
          غیرقابل‌اجرا با نامه کارفرما <b class="inv-ltr">{{ selected.notApplicableLetterNumber }}</b> <span class="inv-ltr">{{ selected.notApplicableReference }}</span>
        </li>
        <li v-if="selected.evidenceDocumentId || selected.evidenceNote">
          مدرک انجام کار:
          <button v-if="selected.evidenceDocumentId" class="btn btn-sm btn-link p-0" type="button" @click="downloadFile(selected.evidenceDocumentId, 'مدرک ' + (selected.itemNo || selected.seq))">دیدن فایل</button>
          <span v-if="selected.evidenceNote"> — {{ selected.evidenceNote }}</span>
        </li>
        <li v-if="selected.doneAt">زمان پایان: {{ jalali(selected.doneAt, true) }}</li>
      </ul>

      <h3 class="inv-title mb-1 mt-3">پارامترها و حدود پذیرش</h3>
      <table class="table table-sm">
        <thead><tr><th>پارامتر</th><th>واحد</th><th>حداقل</th><th>حداکثر</th><th>منبع</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!selected.parameters.length"><td colspan="6" class="inv-muted text-center">پارامتری تعریف نشده.</td></tr>
          <tr v-for="p in selected.parameters" :key="p.id">
            <td>{{ p.name }} <span v-if="p.requiresLimits" class="inv-badge warn">حد را مهندسی تعیین کند</span></td>
            <td class="inv-ltr">{{ p.unit }}</td>
            <template v-if="limitEdit[p.id]">
              <td><input v-model="limitEdit[p.id].min" class="form-control form-control-sm inv-ltr" type="number" step="any" /></td>
              <td><input v-model="limitEdit[p.id].max" class="form-control form-control-sm inv-ltr" type="number" step="any" /></td>
              <td><input v-model="limitEdit[p.id].source" class="form-control form-control-sm" placeholder="مرجع (بند استاندارد، نقشه)" /></td>
              <td class="text-nowrap"><button :disabled="busy" class="btn btn-sm btn-success" @click="saveLimits(p)">ثبت</button> <button class="btn btn-sm btn-outline-secondary" @click="delete limitEdit[p.id]">انصراف</button></td>
            </template>
            <template v-else>
              <td class="inv-ltr">{{ p.min ?? "" }}</td>
              <td class="inv-ltr">{{ p.max ?? "" }}</td>
              <td>{{ p.source }}</td>
              <td><button v-if="canLimits && !terminal(selected)" class="btn btn-sm btn-outline-primary" @click="startLimits(p)">تعیین حدود (مهندسی)</button></td>
            </template>
          </tr>
        </tbody>
      </table>

      <h3 class="inv-title mb-1 mt-3">رهاسازی نقطه‌های کنترل</h3>
      <table class="table table-sm">
        <thead><tr><th>طرف</th><th>اقدام</th><th>شخص</th><th>سازمان</th><th>تاریخ</th><th>مرجع</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!selected.releases.length"><td colspan="7" class="inv-muted text-center">رهاسازی ثبت نشده.</td></tr>
          <tr v-for="r in selected.releases" :key="r.id">
            <td>{{ releaseParties[r.party] }}</td><td>{{ releaseActions[r.action] }}</td><td>{{ r.personName }}</td><td>{{ r.organisation }}</td>
            <td>{{ jalali(r.releaseDate) }}</td><td class="inv-ltr">{{ r.reference }}</td><td><span v-if="r.waivedByNotice" class="inv-badge info">معاف به دلیل انقضای مهلت اطلاع</span></td>
          </tr>
        </tbody>
      </table>
      <div v-if="canQc && !terminal(selected) && !closed" class="row g-2 align-items-end mb-3">
        <div class="col-md-2"><label class="form-label small mb-0">طرف</label><select v-model="release.party" class="form-select form-select-sm"><option v-for="(t, k) in releaseParties" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-2"><label class="form-label small mb-0">اقدام</label><select v-model="release.action" class="form-select form-select-sm"><option v-for="(t, k) in releaseActions" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-2"><label class="form-label small mb-0">نام شخص *</label><input v-model="release.personName" class="form-control form-control-sm" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">سازمان</label><input v-model="release.organisation" class="form-control form-control-sm" /></div>
        <div class="col-md-1"><label class="form-label small mb-0">تاریخ</label><input v-model="release.releaseDate" class="form-control form-control-sm" type="date" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">مرجع (نامه)</label><input v-model="release.reference" class="form-control form-control-sm" /></div>
        <div class="col-md-1"><button :disabled="busy" class="btn btn-sm btn-primary w-100" @click="addRelease">ثبت</button></div>
      </div>

      <div v-if="!terminal(selected) && !closed" class="border-top pt-3">
        <div v-if="canQc" class="mb-3">
          <h3 class="inv-title mb-1" style="font-size: 1rem">اعلام نقطه شاهد / توقف به کارفرما یا TPI</h3>
          <LetterPicker v-model="notifyLetterId" :contract-id="itp.contractId" mode="outgoing" />
          <div class="d-flex flex-wrap gap-2 align-items-center mt-2">
            <input v-model="notifyNote" class="form-control form-control-sm" maxlength="500" placeholder="توضیح (اختیاری)" style="max-width: 320px" />
            <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="notifyActivity">ثبت اعلام</button>
          </div>
          <p class="inv-muted small mb-0 mt-1">تاریخ اعلام نوشته نمی‌شود: همان روز ثبت در سامانه است، یا روز ارسال نامه صادره‌ای که اعلام را به کارفرما رسانده. مهلت شاهد از این تاریخ حساب می‌شود.</p>
        </div>

        <div class="row g-3 align-items-end">
          <div v-if="canPlan" class="col-md-4">
            <label class="form-label small mb-0">تاریخ برنامه</label>
            <div class="d-flex gap-2">
              <input v-model="planDate" class="form-control form-control-sm" type="date" />
              <button :disabled="busy" class="btn btn-sm btn-outline-secondary text-nowrap" @click="planActivity">تعیین تاریخ برنامه</button>
            </div>
          </div>
          <div v-if="canQc" class="col-md-8">
            <label class="form-label small mb-0">
              {{ selected.requiredReportType ? "توضیح" : "شرح مدرک انجام کار / توضیح" }}
            </label>
            <div class="d-flex flex-wrap gap-2 align-items-center">
              <input v-model="doneNote" class="form-control form-control-sm" maxlength="500" style="max-width: 320px" />
              <DocAttach v-if="!selected.requiredReportType" v-model="doneEvidence" title="مدرک انجام فعالیت" />
              <button :disabled="busy" class="btn btn-sm btn-success" @click="doneActivity">انجام شد</button>
            </div>
            <p v-if="!selected.requiredReportType" class="inv-muted small mb-0 mt-1">
              این فعالیت گزارش آزمون ندارد؛ با یک بازرسی قبول‌شده روی همین فعالیت، یا مدرک انجام کار (گواهی، صورت‌جلسه یا سند بازبینی) بسته می‌شود. فایل مدرک تازه، بارگذاری‌شده توسط QC / QA / مهندسی و برای یک فعالیت است.
            </p>
            <p v-else class="inv-muted small mb-0 mt-1">گزارش آزمون لازم این فعالیت با تایید آن، فعالیت را خودکار انجام‌شده می‌کند.</p>
          </div>
          <div v-if="canManage" class="col-12">
            <button :disabled="busy || selected.notApplicablePending" class="btn btn-sm btn-outline-danger" @click="openNotApplicable">غیرقابل‌اجرا (دلیل لازم)…</button>
            <span v-if="selected.notApplicablePending" class="inv-muted small ms-2">درخواست قبلی منتظر تایید کارفرماست.</span>
          </div>
        </div>
      </div>
      <p v-if="selected.statusNote" class="small mt-2 mb-0">یادداشت وضعیت: {{ selected.statusNote }}</p>
    </div>

    <div v-if="itp.revisions.length" class="inv-card screen-only">
      <h2 class="inv-title mb-2">تاریخچه بازنگری‌ها</h2>
      <table class="table table-sm">
        <thead><tr><th>بازنگری</th><th>دلیل</th><th>مرجع تایید کارفرما</th><th>تاریخ تایید</th><th>تغییر دهنده</th><th>زمان</th></tr></thead>
        <tbody>
          <tr v-for="r in itp.revisions" :key="r.id">
            <td>{{ r.revision }}</td><td>{{ r.reason }}</td><td class="inv-ltr">{{ r.clientApprovalReference }}</td><td>{{ jalali(r.clientApprovalDate) }}</td><td>{{ r.changedByName }}</td><td>{{ jalali(r.changedAt, true) }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="na" class="inv-modal-backdrop screen-only" @click.self="na = null">
      <div class="inv-modal" role="dialog" aria-modal="true" style="max-width: 720px">
        <h2 class="inv-title mb-2">غیرقابل‌اجرا کردن فعالیت {{ selected?.itemNo || selected?.seq }}</h2>
        <p v-if="na.thirdParty" class="small">
          این فعالیت نقطه توقف یا شاهد کارفرما / بازرس ثالث دارد؛ حذف آن فقط با نامه موافقت کارفرما ممکن است. نامه ثبت می‌شود، بازنگری جدید ساخته می‌شود و فعالیت تا تایید برنامه بازنگری‌شده توسط کارفرما باز می‌ماند
          (شخصی که بازنگری را ساخته تایید کارفرما را ثبت نمی‌کند).
        </p>
        <p v-else class="small">فعالیت با ثبت دلیل غیرقابل‌اجرا می‌شود و دیگر لازم نیست انجام شود.</p>
        <label class="form-label small mb-0">دلیل *</label>
        <textarea v-model="na.note" class="form-control form-control-sm" maxlength="500" rows="2"></textarea>
        <template v-if="na.thirdParty">
          <label class="form-label small mb-0 mt-2">نامه وارده کارفرما *</label>
          <LetterPicker v-model="na.letterId" :contract-id="itp.contractId" mode="incoming" />
          <label class="form-label small mb-0 mt-2">مرجع نامه (اختیاری؛ حداکثر ۳۰۰ نویسه)</label>
          <input v-model="na.reference" class="form-control form-control-sm inv-ltr" maxlength="300" placeholder="شماره وارده نامه کارفرما اگر خالی بماند" />
        </template>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" type="button" @click="na = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-danger" type="button" @click="submitNotApplicable">ثبت</button>
        </div>
      </div>
    </div>

    <div v-if="edit" class="inv-modal-backdrop screen-only">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-3">{{ edit.id ? "ویرایش فعالیت" : "فعالیت جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-1"><label class="form-label small mb-0">ردیف</label><input v-model.number="edit.seq" class="form-control form-control-sm" type="number" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">شماره بند</label><input v-model="edit.itemNo" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">مرحله</label><input v-model="edit.stage" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">فعالیت *</label><input v-model="edit.activity" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">نوع آزمون</label><select v-model="edit.testType" class="form-select form-select-sm"><option v-for="(t, k) in testTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">مرجع پذیرش</label><input v-model="edit.acceptanceReference" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">نقطه شرکت (V)</label><select v-model="edit.pointVendor" class="form-select form-select-sm"><option v-for="(t, k) in pointHints" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">نقطه TPI</label><select v-model="edit.pointTpi" class="form-select form-select-sm"><option v-for="(t, k) in pointHints" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">نقطه کارفرما (C)</label><select v-model="edit.pointClient" class="form-select form-select-sm"><option v-for="(t, k) in pointHints" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">گزارش آزمون لازم</label><select v-model="edit.requiredReportType" class="form-select form-select-sm"><option :value="null">—</option><option v-for="(t, k) in reportTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">مدرک تولیدشده</label><input v-model="edit.documentProduced" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">تاریخ برنامه</label><input v-model="edit.plannedDate" class="form-control form-control-sm" type="date" /></div>
          <div class="col-md-3 d-flex align-items-end"><label class="form-check small"><input v-model="edit.gatesRelease" class="form-check-input" type="checkbox" /> شرط ترخیص</label></div>
          <div class="col-md-12"><label class="form-label small mb-0">توضیح</label><input v-model="edit.remark" class="form-control form-control-sm" /></div>
          <div v-if="approved" class="col-md-12"><label class="form-label small mb-0">دلیل تغییر برنامه تاییدشده * (بازنگری جدید ساخته می‌شود)</label><input v-model="edit.reason" class="form-control form-control-sm" /></div>
        </div>
        <h3 class="inv-title mt-3 mb-1">پارامترها <button class="btn btn-sm btn-outline-primary ms-2" @click="addParameter">افزودن</button></h3>
        <table class="table table-sm">
          <thead><tr><th>نام</th><th>واحد</th><th>حداقل</th><th>حداکثر</th><th>منبع</th><th>حد را مهندسی بدهد</th><th></th></tr></thead>
          <tbody>
            <tr v-for="(p, i) in edit.parameters" :key="i">
              <td><input v-model="p.name" class="form-control form-control-sm" /></td>
              <td><input v-model="p.unit" class="form-control form-control-sm inv-ltr" /></td>
              <td><input v-model.number="p.min" class="form-control form-control-sm inv-ltr" type="number" step="any" /></td>
              <td><input v-model.number="p.max" class="form-control form-control-sm inv-ltr" type="number" step="any" /></td>
              <td><input v-model="p.source" class="form-control form-control-sm" /></td>
              <td class="text-center"><input v-model="p.requiresLimits" type="checkbox" /></td>
              <td><button class="btn btn-sm btn-outline-danger" @click="edit.parameters.splice(i, 1)">حذف</button></td>
            </tr>
          </tbody>
        </table>
        <div class="d-flex gap-2 justify-content-end">
          <button class="btn btn-sm btn-outline-secondary" @click="edit = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="saveActivity">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
