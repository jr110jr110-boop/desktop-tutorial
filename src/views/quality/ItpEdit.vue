<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import { addItpRelease, getItp, itpAction, itpActivityAction, saveItp, saveItpActivity, setItpLimits } from "@/api/QualityAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import {
  activityPoints,
  activityStatuses,
  dateInput,
  itpStatuses,
  pointHints,
  points,
  releaseActions,
  releaseParties,
  reportTypes,
  testTypes,
} from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// One ITP: activities with V / TPI / C points, notices, releases, acceptance limits and the plan actions.
const route = useRoute();
const itp = ref<any>(null);
const busy = ref(false);
const selectedId = ref<number | null>(null);
const edit = ref<any>(null);
const approve = ref({ reference: "", date: today() });
const reviseReason = ref("");
const actionNote = ref("");
const actionDate = ref(today());
const release = ref({ party: 3, action: 1, personName: "", organisation: "", releaseDate: today(), reference: "" });
const limitEdit = ref<Record<number, { min: any; max: any; source: string }>>({});

const canPlan = hasRole(QualityRoles.planners);
const canQc = hasRole(QualityRoles.inspectors);
const canManage = hasRole(QualityRoles.managers);
const canLimits = hasRole(QualityRoles.limits);
const approved = computed(() => itp.value && (itp.value.status === 2 || itp.value.status === 3));
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

const saveNotice = () => run(saveItp({ id: itp.value.id, noticeDays: Number(itp.value.noticeDays) || 0, note: itp.value.note }), "ذخیره شد.");
const planAction = (action: string, extra: any = {}) => run(itpAction(itp.value.id, action, extra));
function clientApprove() {
  if (!approve.value.reference.trim()) return fail("شماره نامه یا مرجع تایید کارفرما را وارد کنید.");
  return planAction("client-approve", { reference: approve.value.reference, date: approve.value.date });
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
      }))) edit.value = null;
}

const activityAction = (action: string, note?: string) =>
  run(itpActivityAction(selected.value.id, action, { date: actionDate.value, note: note ?? (actionNote.value || null) })).then((done) => done && (actionNote.value = ""));

async function addRelease() {
  if (!release.value.personName.trim()) return fail("نام شخص رهاکننده را وارد کنید.");
  if (await run(addItpRelease({ ...release.value, activityId: selected.value.id }), "رهاسازی ثبت شد.")) release.value.reference = "";
}

async function saveLimits(p: any) {
  const e = limitEdit.value[p.id];
  const min = e.min === "" || e.min === null ? null : Number(e.min);
  const max = e.max === "" || e.max === null ? null : Number(e.max);
  await run(setItpLimits({ parameterId: p.id, min, max, source: e.source || null }), "حدود ثبت شد.");
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
});
</script>

<template>
  <div v-if="itp" class="inv-page">
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
          <router-link :to="{ name: 'QualityReleaseNotes', query: { workOrderId: itp.workOrderId } }" class="btn btn-sm btn-outline-secondary">برگه‌های ترخیص</router-link>
        </div>
      </div>

      <div class="row g-2 mb-2">
        <div class="col-md-2">
          <label class="form-label small mb-0">روزهای اطلاع نقطه شاهد</label>
          <input v-model.number="itp.noticeDays" :disabled="!canPlan || closed" class="form-control form-control-sm" type="number" />
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
          <input v-model="approve.reference" class="form-control form-control-sm" placeholder="شماره نامه تایید کارفرما *" style="max-width: 220px" />
          <input v-model="approve.date" class="form-control form-control-sm" style="max-width: 160px" type="date" />
          <button :disabled="busy" class="btn btn-sm btn-success" @click="clientApprove">ثبت تایید کارفرما</button>
        </template>
        <button v-if="itp.status === 2 && canManage" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="planAction('use')">در حال اجرا</button>
        <button v-if="approved && canManage" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="planAction('close')">بستن برنامه</button>
        <template v-if="approved && canPlan">
          <input v-model="reviseReason" class="form-control form-control-sm" placeholder="دلیل بازنگری" style="max-width: 240px" />
          <button :disabled="busy" class="btn btn-sm btn-outline-warning" @click="revise">بازنگری جدید</button>
        </template>
      </div>
      <p class="inv-muted mt-2 mb-0">پس از تایید کارفرما هر تغییر برنامه، بازنگری جدید می‌سازد و تاریخچه نگه داشته می‌شود. نقطه کنترل پیش از تایید کارفرما انجام‌شده ثبت نمی‌شود.</p>
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
              <td><span :class="{ ok: a.status === 2, warn: a.status === 1, info: a.status === 3 }" class="inv-badge">{{ activityStatuses[a.status] }}</span> <span v-if="a.holdPending" class="inv-badge bad">H</span></td>
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

      <div v-if="!terminal(selected) && !closed" class="d-flex flex-wrap gap-2 align-items-end border-top pt-3">
        <input v-model="actionDate" class="form-control form-control-sm" style="max-width: 160px" type="date" />
        <input v-model="actionNote" class="form-control form-control-sm" placeholder="توضیح / دلیل" style="max-width: 280px" />
        <button v-if="canQc" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="activityAction('notify')">ثبت اعلام به کارفرما / TPI</button>
        <button v-if="canPlan" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="activityAction('plan')">تعیین تاریخ برنامه</button>
        <button v-if="canQc" :disabled="busy" class="btn btn-sm btn-success" @click="activityAction('done')">انجام شد</button>
        <button v-if="canManage" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="activityAction('not-applicable')">غیرقابل‌اجرا (دلیل لازم)</button>
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
