<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import { decideSubmission, getChain, getNotUpdated, getOverdueUpdates, getSubmission, getSubmissions, saveChain, saveSubmission } from "@/api/ProjectRegistersAPIs";
import { getActivities } from "@/api/ProjectControlAPIs";
import { getTasks } from "@/api/TasksAPIs";
import { uploadFile } from "@/api/InventoryAPIs";
import { downloadDocument } from "@/api/OfficeAPIs";
import { rowsOf } from "@/api/ApiClient";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import JDate from "@/components/pc/JDate.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import PositionSelect from "@/components/common/PositionSelect.vue";
import { fail, ok, printPage, rial } from "@/composables/inventory";
import { costKinds, manualMethods, pcContractId } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";
import { chainRoles, stepStates, submissionStateClass, submissionStates } from "@/composables/tasks";

// Progress approval cartable (appendix 1): a resource submits progress of an assigned activity or task; the chain of the project
// (project manager -> ... -> PMO) approves level by level. Submitted rows are locked for the submitter; the approver may edit
// the values before approving; a rejection returns the report with a reason. The approved values reach the plan through the
// normal manual-progress path (no bypass of the baseline or the plan lock).
const tab = ref("waiting");
const tabs = [
  { key: "waiting", title: "در انتظار تایید من" }, { key: "mine", title: "ارسال‌های من" }, { key: "all", title: "همه (مدیریت)" },
  { key: "notupdated", title: "به‌روز نشده" }, { key: "overdue", title: "تاخیر در تایید" }, { key: "chain", title: "زنجیره تایید" },
];
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const sel = ref<any>(null);
const form = reactive<any>({});
const showForm = ref(false);
const acts = ref<any[]>([]);
const tasks = ref<any[]>([]);
const files = ref<File[]>([]);
const busy = ref(false);
const dec = reactive<any>({ note: "", percent: null, actualCost: null, actualStart: null, actualFinish: null });
const chain = ref<any>(null);
const report = ref<any[]>([]);
const canChain = hasRole(ProjectControlRoles.planningOrPm);
const days = ref(7);

async function load() {
  rows.value = []; report.value = [];
  if (!pcContractId.value) return;
  if (["waiting", "mine", "all"].includes(tab.value)) {
    const [d, e] = await getSubmissions({ Cartable: tab.value, ContractId: pcContractId.value, PageNumber: page.value, PageSize: 25 });
    if (e) return fail(e);
    rows.value = rowsOf(d); total.value = d?.totalItems ?? rows.value.length;
  } else if (tab.value === "notupdated") {
    const [d, e] = await getNotUpdated({ ContractId: pcContractId.value, Days: days.value });
    if (e) return fail(e);
    report.value = Array.isArray(d) ? d : [];
  } else if (tab.value === "overdue") {
    const [d, e] = await getOverdueUpdates();
    if (e) return fail(e);
    report.value = Array.isArray(d) ? d : [];
  } else {
    const [d, e] = await getChain(pcContractId.value);
    if (e) return fail(e);
    chain.value = d;
  }
}
async function openRow(r: any) {
  const [d, e] = await getSubmission(r.id);
  if (e) return fail(e);
  sel.value = d;
  Object.assign(dec, { note: "", percent: d.percent, actualCost: d.actualCost, actualStart: d.actualStart?.slice(0, 10) ?? null, actualFinish: d.actualFinish?.slice(0, 10) ?? null });
}
async function newForm(from?: any) {
  const [a] = await getActivities({ ContractId: pcContractId.value });
  acts.value = rowsOf(a).filter((x: any) => x.status !== 2);
  const [t] = await getTasks({ Cartable: "assigned-to-me", ContractId: pcContractId.value, PageSize: 200 });
  tasks.value = rowsOf(t).filter((x: any) => x.activityId == null);
  files.value = [];
  Object.assign(form, { id: from?.id ?? null, contractId: pcContractId.value, target: from?.target ?? 1, activityId: from?.activityId ?? null, taskId: from?.taskId ?? null, percent: from?.percent ?? 0, actualStart: from?.actualStart?.slice(0, 10) ?? null, actualFinish: from?.actualFinish?.slice(0, 10) ?? null, actualCost: from?.actualCost ?? 0, costKind: from?.costKind ?? 0, costNote: from?.costNote ?? "", comment: from?.comment ?? "", attachmentDocumentIds: from?.attachmentDocumentIds ?? [] });
  showForm.value = true;
}
async function save(submit: boolean) {
  busy.value = true;
  const ids = [...form.attachmentDocumentIds];
  for (const f of files.value) {
    const [id, err] = await uploadFile(f, "پیوست گزارش پیشرفت");
    if (err || !id) { busy.value = false; return fail(err); }
    ids.push(id);
  }
  const [d, e] = await saveSubmission({ ...form, attachmentDocumentIds: ids, submit });
  busy.value = false;
  if (e) return fail(e);
  ok(submit ? "گزارش ارسال شد." : "پیش‌نویس ذخیره شد.");
  showForm.value = false;
  sel.value = d;
  load();
}
async function decide(approve: boolean) {
  if (!approve && !dec.note.trim()) return fail("برای بازگرداندن، دلیل را بنویسید.");
  busy.value = true;
  const [d, e] = await decideSubmission({ id: sel.value.id, approve, note: dec.note, percent: dec.percent, actualCost: dec.actualCost, actualStart: dec.actualStart, actualFinish: dec.actualFinish, costNote: sel.value.costNote });
  busy.value = false;
  if (e) return fail(e);
  ok(approve ? "تایید شد." : "برگشت داده شد.");
  sel.value = d;
  load();
}
const addLevel = () => chain.value.levels.push({ level: chain.value.levels.length + 1, title: "", role: "PMO", positionId: null });
async function storeChain() {
  chain.value.levels.forEach((l: any, i: number) => (l.level = i + 1));
  const [d, e] = await saveChain({ contractId: pcContractId.value, levels: chain.value.levels });
  if (e) return fail(e);
  chain.value = d;
  ok("زنجیره تایید ذخیره شد.");
}
const unitLabel = computed(() => (form.target === 1 ? "فعالیت" : "وظیفه"));
const openFile = async (id: string) => { const e = await downloadDocument(id, "پیوست پیشرفت"); if (e) fail(e); };
onMounted(load);
watch([tab, pcContractId], () => { page.value = 1; sel.value = null; load(); });
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="کارتابل تایید پیشرفت" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">کارتابل تایید پیشرفت</h1>
        <div class="d-flex flex-wrap gap-2 d-print-none"><ContractSelect /><button v-if="pcContractId" class="btn btn-sm btn-success" @click="newForm()">+ ارسال پیشرفت</button><button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button></div>
      </div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <PcTabs v-model="tab" :tabs="tabs" />

        <div v-if="['waiting', 'mine', 'all'].includes(tab)" class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>شماره</th><th>مورد</th><th>درصد قبلی</th><th>درصد جدید</th><th>هزینه</th><th>مرحله</th><th>وضعیت</th><th>ارسال‌کننده</th><th>انتظار (روز)</th></tr></thead>
            <tbody>
              <tr v-if="!rows.length"><td colspan="9" class="text-center inv-muted">موردی نیست.</td></tr>
              <tr v-for="r in rows" :key="r.id" style="cursor: pointer" @click="openRow(r)">
                <td class="inv-ltr">{{ r.number }}</td><td>{{ r.targetTitle }}</td><td>{{ r.previousPercent }}٪</td><td>{{ r.percent }}٪</td><td>{{ rial(r.actualCost) }}</td>
                <td>{{ r.currentLevelTitle }}</td><td><span :class="submissionStateClass[r.state]" class="inv-badge">{{ submissionStates[r.state] }}</span></td><td>{{ r.submittedByName }} {{ r.submittedAtText }}</td><td>{{ r.waitingDays }}</td>
              </tr>
            </tbody>
          </table>
          <div class="inv-pager d-print-none"><button :disabled="page <= 1" class="btn btn-sm btn-light" @click="page--; load()">قبلی</button><span>صفحه {{ page }} - {{ total }} مورد</span><button :disabled="page * 25 >= total" class="btn btn-sm btn-light" @click="page++; load()">بعدی</button></div>
        </div>

        <div v-else-if="tab === 'notupdated'">
          <div class="d-flex gap-2 align-items-end mb-2 d-print-none"><div style="width: 160px"><label class="form-label small mb-0">بدون به‌روزرسانی بیش از (روز)</label><input v-model.number="days" class="form-control form-control-sm" type="number" min="1" /></div><button class="btn btn-sm btn-secondary" @click="load">نمایش</button></div>
          <table class="table table-sm"><thead><tr><th>نوع</th><th>کد</th><th>عنوان</th><th>مسئول</th><th>درصد</th><th>آخرین به‌روزرسانی</th><th>روز</th></tr></thead>
            <tbody><tr v-if="!report.length"><td colspan="7" class="text-center inv-muted">همه به‌روز هستند.</td></tr>
              <tr v-for="r in report" :key="r.target + '-' + r.targetId"><td>{{ r.target === 1 ? "فعالیت" : "وظیفه" }}</td><td class="inv-ltr">{{ r.code }}</td><td>{{ r.title }}</td><td>{{ r.assigneeNames }}</td><td>{{ r.percent }}٪</td><td>{{ r.lastUpdateText || "هرگز" }}</td><td>{{ r.daysSince }}</td></tr></tbody></table>
        </div>
        <div v-else-if="tab === 'overdue'">
          <table class="table table-sm"><thead><tr><th>شماره</th><th>پروژه</th><th>مورد</th><th>منتظر</th><th>مرحله</th><th>روز انتظار</th></tr></thead>
            <tbody><tr v-if="!report.length"><td colspan="6" class="text-center inv-muted">تاخیری در تایید نیست.</td></tr>
              <tr v-for="r in report" :key="r.submissionId"><td class="inv-ltr">{{ r.number }}</td><td>{{ r.contractTitle }}</td><td>{{ r.targetTitle }}</td><td>{{ r.waitingAt }}</td><td>{{ r.levelTitle }}</td><td>{{ r.waitingDays }}</td></tr></tbody></table>
        </div>

        <div v-else-if="chain">
          <p class="inv-muted">زنجیره تایید پروژه؛ هر مرحله یک نقش یا یک سمت مشخص است و آخرین مرحله باید دفتر مدیریت پروژه یا برنامه‌ریزی باشد. تا وقتی گزارشی در جریان تایید است زنجیره قفل می‌ماند. {{ chain.isDefault ? "(پیش‌فرض سیستم)" : "" }}</p>
          <p v-if="chain.locked" class="text-danger">{{ chain.inApprovalCount }} گزارش در جریان تایید است؛ ویرایش زنجیره ممکن نیست.</p>
          <table class="table table-sm">
            <thead><tr><th style="width: 60px">مرحله</th><th>عنوان</th><th>نقش</th><th>یا سمت</th><th></th></tr></thead>
            <tbody>
              <tr v-for="(l, i) in chain.levels" :key="i">
                <td>{{ i + 1 }}</td><td><input v-model="l.title" :disabled="chain.locked || !canChain" class="form-control form-control-sm" /></td>
                <td><select v-model="l.role" :disabled="chain.locked || !canChain || !!l.positionId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="r in chainRoles" :key="r[0]" :value="r[0]">{{ r[1] }}</option></select></td>
                <td><PositionSelect v-model="l.positionId" :disabled="chain.locked || !canChain" @update:model-value="(v: any) => { if (v) l.role = null; }" /></td>
                <td><button v-if="!chain.locked && canChain" class="btn btn-sm btn-outline-danger" @click="chain.levels.splice(i, 1)">حذف</button></td>
              </tr>
            </tbody>
          </table>
          <div v-if="!chain.locked && canChain" class="d-flex gap-2"><button class="btn btn-sm btn-outline-primary" @click="addLevel">+ مرحله</button><button class="btn btn-sm btn-primary" @click="storeChain">ذخیره زنجیره</button></div>
        </div>

        <!-- one submission -->
        <div v-if="sel" class="inv-modal-backdrop" @click.self="sel = null">
          <div class="inv-modal wide">
            <div class="d-flex justify-content-between"><h5>{{ sel.number }} - {{ sel.targetTitle }} <span :class="submissionStateClass[sel.state]" class="inv-badge">{{ submissionStates[sel.state] }}</span></h5><button class="btn btn-sm btn-light" @click="sel = null">بستن</button></div>
            <table class="table table-sm">
              <tbody>
                <tr><th style="width: 150px">درصد</th><td>{{ sel.previousPercent }}٪ ← <b>{{ sel.percent }}٪</b></td><th style="width: 130px">هزینه واقعی</th><td>{{ rial(sel.actualCost) }} ریال <span v-if="sel.costNote" class="inv-muted">({{ sel.costNote }})</span></td></tr>
                <tr><th>شروع واقعی</th><td>{{ sel.actualStartText }}</td><th>پایان واقعی</th><td>{{ sel.actualFinishText }}</td></tr>
                <tr><th>ارسال‌کننده</th><td>{{ sel.submittedByName }} {{ sel.submittedAtText }}</td><th>مرحله فعلی</th><td>{{ sel.currentLevelTitle }} (دور {{ sel.round }})</td></tr>
                <tr v-if="sel.comment"><th>توضیح</th><td colspan="3">{{ sel.comment }}</td></tr>
                <tr v-if="sel.resultNote"><th>نتیجه</th><td colspan="3">{{ sel.resultNote }}</td></tr>
                <tr v-if="sel.attachmentDocumentIds.length"><th>پیوست</th><td colspan="3"><button v-for="(a, i) in sel.attachmentDocumentIds" :key="a" class="btn btn-sm btn-link" @click="openFile(a)">پیوست {{ i + 1 }}</button></td></tr>
              </tbody>
            </table>
            <h6>مراحل تایید</h6>
            <table class="table table-sm"><thead><tr><th>دور</th><th>مرحله</th><th>وضعیت</th><th>تصمیم‌گیرنده</th><th>تاریخ</th><th>توضیح</th></tr></thead>
              <tbody><tr v-for="(s, i) in sel.steps" :key="i"><td>{{ s.round }}</td><td>{{ s.title }} <span class="inv-muted">({{ s.positionTitle || s.role }})</span></td><td>{{ stepStates[s.state] }}</td><td>{{ s.decidedByName }}</td><td>{{ s.decidedAtText }}</td><td>{{ s.note }}</td></tr></tbody></table>
            <div v-if="sel.canDecide" class="border rounded p-2 mb-2">
              <h6>تصمیم شما (می‌توانید مقادیر را پیش از تایید اصلاح کنید؛ اصلاح در تاریخچه ثبت می‌شود)</h6>
              <div class="row g-2">
                <div class="col-md-2"><label class="form-label small mb-0">درصد</label><input v-model.number="dec.percent" class="form-control form-control-sm" type="number" min="0" max="100" /></div>
                <div class="col-md-3"><label class="form-label small mb-0">هزینه واقعی</label><input v-model.number="dec.actualCost" class="form-control form-control-sm" type="number" min="0" /></div>
                <div class="col-md-3"><label class="form-label small mb-0">شروع واقعی</label><JDate v-model="dec.actualStart" /></div>
                <div class="col-md-3"><label class="form-label small mb-0">پایان واقعی</label><JDate v-model="dec.actualFinish" /></div>
                <div class="col-12"><input v-model="dec.note" class="form-control form-control-sm" placeholder="توضیح (برای بازگرداندن الزامی است)" /></div>
              </div>
              <div class="d-flex gap-2 mt-2"><button :disabled="busy" class="btn btn-sm btn-success" @click="decide(true)">تایید</button><button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="decide(false)">بازگرداندن با دلیل</button></div>
            </div>
            <button v-if="sel.canEdit" class="btn btn-sm btn-secondary mb-2" @click="newForm(sel); sel = null">ویرایش و ارسال مجدد</button>
            <h6>تاریخچه</h6>
            <div class="inv-timeline"><div v-for="h in sel.history" :key="h.id" class="step"><div class="small inv-muted">{{ h.atText }} - {{ h.userName }} - {{ h.actionText }}</div><div v-if="h.note">{{ h.note }}</div></div></div>
          </div>
        </div>

        <!-- submit form -->
        <div v-if="showForm" class="inv-modal-backdrop" @click.self="showForm = false">
          <div class="inv-modal">
            <h5>ارسال پیشرفت</h5>
            <div class="row g-2">
              <div class="col-md-4"><label class="form-label small mb-0">مورد</label><select v-model.number="form.target" class="form-select form-select-sm" :disabled="!!form.id"><option :value="1">فعالیت</option><option :value="2">وظیفه</option></select></div>
              <div class="col-md-8">
                <label class="form-label small mb-0">{{ unitLabel }}</label>
                <select v-if="form.target === 1" v-model.number="form.activityId" :disabled="!!form.id" class="form-select form-select-sm"><option :value="null">-</option><option v-for="a in acts.filter((x) => manualMethods.includes(x.progressMethod))" :key="a.id" :value="a.id">{{ a.code }} - {{ a.title }}</option></select>
                <select v-else v-model.number="form.taskId" :disabled="!!form.id" class="form-select form-select-sm"><option :value="null">-</option><option v-for="t in tasks" :key="t.id" :value="t.id">{{ t.number }} - {{ t.title }}</option></select>
              </div>
              <div class="col-md-3"><label class="form-label small mb-0">درصد پیشرفت</label><input v-model.number="form.percent" class="form-control form-control-sm" type="number" min="0" max="100" /></div>
              <div class="col-md-3"><label class="form-label small mb-0">شروع واقعی</label><JDate v-model="form.actualStart" /></div>
              <div class="col-md-3"><label class="form-label small mb-0">پایان واقعی</label><JDate v-model="form.actualFinish" /></div>
              <div class="col-md-3"><label class="form-label small mb-0">هزینه واقعی (ریال)</label><input v-model.number="form.actualCost" class="form-control form-control-sm" type="number" min="0" /></div>
              <div v-if="form.actualCost > 0" class="col-md-4"><label class="form-label small mb-0">نوع هزینه</label><select v-model.number="form.costKind" class="form-select form-select-sm"><option :value="0">-</option><option v-for="(t, k) in costKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
              <div v-if="form.actualCost > 0" class="col-md-8"><label class="form-label small mb-0">شرح هزینه (ثبت هزینه دستی با کنترل دومین نفر مالی)</label><input v-model="form.costNote" class="form-control form-control-sm" /></div>
              <div class="col-12"><label class="form-label small mb-0">توضیح</label><textarea v-model="form.comment" class="form-control form-control-sm" rows="2"></textarea></div>
              <div class="col-12"><label class="form-label small mb-0">پیوست‌ها (حداکثر ۲۰ فایل)</label><input class="form-control form-control-sm" type="file" multiple @change="files = Array.from(($event.target as HTMLInputElement).files || [])" /></div>
            </div>
            <div class="d-flex gap-2 mt-3"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save(true)">ارسال برای تایید</button><button :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="save(false)">ذخیره پیش‌نویس</button><button class="btn btn-sm btn-light" @click="showForm = false">انصراف</button></div>
          </div>
        </div>
      </template>
    </div>
  </div>
</template>
<style lang="scss" src="../inventory/inventory.scss"></style>
