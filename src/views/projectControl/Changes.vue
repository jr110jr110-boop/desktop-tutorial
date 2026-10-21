<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import JDate from "@/components/pc/JDate.vue";
import { changeAction, getActivities, getAuthorities, getChange, getChanges, saveAuthority, saveChange } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok, rial } from "@/composables/inventory";
import ChangePrint from "@/components/pc/print/ChangePrint.vue";
import { changeDecisions, changeOrigins, changeStatuses, changeTypes, contractLabel, pcContractId, unitTitle, usePcPrint, votes } from "@/composables/projectControl";
import { currentRole, hasRole, ProjectControlRoles } from "@/composables/roles";

// Change control: register (the change board is formed from the affected units), impact analysis (CPM before/after), PM recommendation, board votes, decision within the
// approval authority; an approved change makes a new approved baseline. The authorities are edited by the CEO.
const { job, run } = usePcPrint();
const tab = ref("changes");
const tabs = [{ key: "changes", title: "درخواست‌های تغییر" }, { key: "authority", title: "سطوح اختیار تصمیم‌گیری" }];
const rows = ref<any[]>([]);
const status = ref<number | null>(null);
const acts = ref<any[]>([]);
const form = ref<any>(null);
const d = ref<any>(null);
const sub = ref<any>(null);
const auth = ref<any[]>([]);
const editAuth = ref<any>(null);
const busy = ref(false);
const role = currentRole();
const canCreate = hasRole(ProjectControlRoles.unitManagers);
const canAnalyze = hasRole(ProjectControlRoles.planningOrPm);
const canRecommend = hasRole(ProjectControlRoles.projectManagers);
const canClose = hasRole(ProjectControlRoles.planning);
const isCeo = hasRole(ProjectControlRoles.ceo);

async function load() {
  if (!pcContractId.value) return;
  const [[c, e], [a]] = await Promise.all([getChanges({ ContractId: pcContractId.value, Status: status.value }), getActivities({ ContractId: pcContractId.value })]);
  if (e) return fail(e);
  rows.value = rowsOf(c);
  acts.value = rowsOf(a);
}
async function loadAuth() {
  const [a, e] = await getAuthorities();
  if (e) return fail(e);
  auth.value = Array.isArray(a) ? a : [];
}
const actText = (id: number) => { const a = acts.value.find((x) => x.id === id); return a ? `${a.code} ${a.title}` : "#" + id; };
const blank = () => ({ contractId: pcContractId.value, origin: 2, type: 1, managerAgreement: "", description: "", reason: "", requestedPriceDelta: 0, requestedDaysDelta: 0, scenario: [] as any[] });
async function save() {
  busy.value = true;
  const [c, e] = await saveChange(form.value);
  busy.value = false;
  if (e) return fail(e);
  ok("ذخیره شد: " + c.number);
  form.value = null;
  load();
}
async function open(id: number) {
  const [c, e] = await getChange(id);
  if (e) return fail(e);
  d.value = c;
  sub.value = { note: "", impactScope: c.impactScope || "", impactResources: c.impactResources || "", impactQuality: c.impactQuality || "", impactRisk: c.impactRisk || "", impactProcurement: c.impactProcurement || "", impactClientContract: c.impactClientContract || "", decision: 1, vote: 1 };
}
async function act(action: string, extra: any = {}) {
  busy.value = true;
  const [c, e] = await changeAction({ id: d.value.id, action, ...extra });
  busy.value = false;
  if (e) return fail(e);
  ok("انجام شد.");
  d.value = c;
  load();
}
const myOpenVote = computed(() => d.value?.board?.some((m: any) => m.vote === 0 && m.role === role));
async function saveAuth() {
  busy.value = true;
  const [, e] = await saveAuthority(editAuth.value);
  busy.value = false;
  if (e) return fail(e);
  ok("ذخیره شد.");
  editAuth.value = null;
  loadAuth();
}
const statusBadge = (s: number) => (s === 6 ? "bad" : s === 4 || s === 5 ? "ok" : s === 0 ? "info" : "warn");
onMounted(() => { load(); loadAuth(); });
watch(pcContractId, load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="کنترل تغییرات پروژه" :subtitle="contractLabel(pcContractId)" />
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title screen-only">کنترل تغییرات</h1><div class="d-print-none"><ContractSelect /></div></div>
      <PcTabs v-model="tab" :tabs="tabs" />
      <template v-if="tab === 'changes'">
        <NeedContract v-if="!pcContractId" />
        <template v-else>
          <div class="inv-filters mb-2 d-print-none">
            <select v-model="status" class="form-select form-select-sm" @change="load"><option :value="null">همه وضعیت‌ها</option><option v-for="(n, k) in changeStatuses" :key="k" :value="Number(k)">{{ n }}</option></select>
            <button v-if="canCreate" class="btn btn-sm btn-primary" @click="form = blank()">درخواست تغییر جدید</button>
          </div>
          <table class="table table-sm table-hover">
            <thead><tr><th>شماره</th><th>منشأ</th><th>نوع</th><th>شرح</th><th>مهلت تحلیل</th><th>اثر زمانی (روز)</th><th>اثر هزینه</th><th>وضعیت</th></tr></thead>
            <tbody>
              <tr v-if="!rows.length"><td colspan="8" class="text-center inv-muted">درخواستی نیست.</td></tr>
              <tr v-for="r in rows" :key="r.id" role="button" @click="open(r.id)">
                <td class="inv-ltr">{{ r.number }}</td><td>{{ changeOrigins[r.origin] }}</td><td>{{ changeTypes[r.type] }}</td><td>{{ (r.description || "").slice(0, 70) }}</td>
                <td>{{ jalali(r.dueDate) }}<span v-if="r.escalated" class="inv-badge bad ms-1">ارجاع به مدیرعامل</span></td>
                <td>{{ r.impactDeltaDays ?? "—" }}<span v-if="r.impactOnCriticalPath" class="inv-badge bad ms-1">بحرانی</span></td><td>{{ r.impactDeltaCost != null ? rial(r.impactDeltaCost) : "—" }}</td>
                <td><span :class="statusBadge(r.status)" class="inv-badge">{{ changeStatuses[r.status] }}</span></td>
              </tr>
            </tbody>
          </table>
        </template>
      </template>
      <div v-else>
        <table class="table table-sm">
          <thead><tr><th>نقش</th><th>حق تصمیم</th><th>سقف مبلغ (ریال)</th><th>سقف تاخیر (روز)</th><th>توضیح</th><th class="d-print-none"></th></tr></thead>
          <tbody>
            <tr v-for="a in auth" :key="a.id"><td>{{ unitTitle(a.role) }}</td><td>{{ a.canDecide ? "بله" : "فقط پیشنهاد" }}</td><td>{{ a.maxAmount != null ? rial(a.maxAmount) : "بدون سقف" }}</td><td>{{ a.maxDelayDays ?? "بدون سقف" }}</td><td>{{ a.note }}</td><td class="d-print-none"><button v-if="isCeo" class="btn btn-sm btn-outline-secondary" @click="editAuth = { ...a }">ویرایش</button></td></tr>
          </tbody>
        </table>
        <p class="inv-muted">تصمیم درباره تغییر در سقف اختیار صاحب نقش است؛ بالاتر از آن تصمیم با مدیرعامل است. سقف‌ها را فقط مدیرعامل تغییر می‌دهد.</p>
      </div>
    </div>

    <ChangePrint v-if="job?.kind === 'change'" :c="job.data" :activities="acts" />
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal" style="max-width: 860px; width: 96%">
        <h3 class="h6">{{ form.id ? "ویرایش " + form.number : "درخواست تغییر جدید" }}</h3>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">منشأ</label><select v-model.number="form.origin" class="form-select form-select-sm"><option v-for="(n, k) in changeOrigins" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">نوع تغییر</label><select v-model.number="form.type" class="form-select form-select-sm"><option v-for="(n, k) in changeTypes" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div v-if="form.origin === 2" class="col-md-4"><label class="form-label small mb-0">موافقت مدیر درخواست‌کننده</label><input v-model="form.managerAgreement" class="form-control form-control-sm" placeholder="نام مدیر یا مرجع موافقت" /></div>
          <div class="col-12"><label class="form-label small mb-0">شرح تغییر</label><textarea v-model="form.description" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-12"><label class="form-label small mb-0">دلیل تغییر</label><textarea v-model="form.reason" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-md-6"><label class="form-label small mb-0">تغییر مبلغ درخواستی (ریال)</label><input v-model.number="form.requestedPriceDelta" class="form-control form-control-sm" type="number" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">تغییر زمان درخواستی (روز)</label><input v-model.number="form.requestedDaysDelta" class="form-control form-control-sm" type="number" /></div>
        </div>
        <b class="small d-block mt-3">سناریوی تغییر (فعالیت‌های متاثر)</b>
        <table class="table table-sm mb-1">
          <thead><tr><th>فعالیت</th><th style="width: 130px">تغییر مدت (روز)</th><th style="width: 170px">تغییر بودجه (ریال)</th><th></th></tr></thead>
          <tbody>
            <tr v-for="(s, i) in form.scenario" :key="i">
              <td><select v-model.number="s.activityId" class="form-select form-select-sm"><option :value="0">-</option><option v-for="a in acts" :key="a.id" :value="a.id">{{ a.code }} {{ a.title }}</option></select></td>
              <td><input v-model.number="s.durationDelta" class="form-control form-control-sm" type="number" /></td><td><input v-model.number="s.budgetDelta" class="form-control form-control-sm" type="number" /></td>
              <td><button class="btn btn-sm btn-outline-danger" @click="form.scenario.splice(i, 1)">حذف</button></td>
            </tr>
          </tbody>
        </table>
        <button class="btn btn-sm btn-outline-primary mb-3" @click="form.scenario.push({ activityId: 0, durationDelta: 0, budgetDelta: 0 })">افزودن فعالیت</button>
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="form = null">انصراف</button><button :disabled="busy || !form.description || !form.reason || form.scenario.some((s: any) => !s.activityId)" class="btn btn-sm btn-primary" @click="save">ذخیره پیش‌نویس</button></div>
      </div>
    </div>

    <div v-if="d" class="inv-modal-backdrop" @click.self="d = null">
      <div class="inv-modal" style="max-width: 980px; width: 96%; max-height: 92vh; overflow: auto">
        <div class="d-flex justify-content-between"><h3 class="h6">درخواست تغییر <span class="inv-ltr">{{ d.number }}</span> — <span :class="statusBadge(d.status)" class="inv-badge">{{ changeStatuses[d.status] }}</span></h3>
          <div class="d-flex gap-2"><button class="btn btn-sm btn-outline-secondary" @click="run('change', d)">چاپ فرم</button><button class="btn btn-sm btn-secondary" @click="d = null">بستن</button></div></div>
        <div class="row g-2 small mb-2">
          <div class="col-md-4">منشأ: {{ changeOrigins[d.origin] }}</div><div class="col-md-4">نوع: {{ changeTypes[d.type] }}</div><div class="col-md-4">مهلت تحلیل: {{ jalali(d.dueDate) }}</div>
          <div class="col-12">شرح: {{ d.description }}</div><div class="col-12">دلیل: {{ d.reason }}</div>
          <div v-if="d.managerAgreement" class="col-12">موافقت مدیر: {{ d.managerAgreement }}</div>
          <div class="col-md-6">مبلغ درخواستی: {{ rial(d.requestedPriceDelta) }} ریال</div><div class="col-md-6">زمان درخواستی: {{ d.requestedDaysDelta }} روز</div>
        </div>
        <b class="small">سناریو</b>
        <table class="table table-sm"><thead><tr><th>فعالیت</th><th>تغییر مدت (روز)</th><th>تغییر بودجه</th></tr></thead>
          <tbody><tr v-if="!d.scenario.length"><td colspan="3" class="inv-muted">سناریویی ثبت نشده است.</td></tr><tr v-for="s in d.scenario" :key="s.activityId"><td>{{ actText(s.activityId) }}</td><td>{{ s.durationDelta }}</td><td>{{ rial(s.budgetDelta) }}</td></tr></tbody></table>

        <template v-if="d.analysedAt">
          <b class="small">نتیجه تحلیل اثر ({{ jalali(d.analysedAt) }})</b>
          <div class="row g-2 small my-1">
            <div class="col-md-3">تغییر پایان پروژه: <b>{{ d.impactDeltaDays }}</b> روز</div><div class="col-md-3">تغییر هزینه: <b>{{ rial(d.impactDeltaCost) }}</b></div>
            <div class="col-md-3">مسیر بحرانی: <b>{{ d.impactOnCriticalPath ? "متاثر" : "بدون اثر" }}</b></div><div class="col-md-3">سطرهای سفارش / نقاط ITP متاثر: <b>{{ d.affectedPoLines }} / {{ d.affectedItpPoints }}</b></div>
          </div>
          <table v-if="d.impactActivities.length" class="table table-sm"><thead><tr><th>فعالیت</th><th>پایان قبلی</th><th>پایان جدید</th><th></th></tr></thead>
            <tbody><tr v-for="a in d.impactActivities" :key="a.activityId"><td class="inv-ltr">{{ a.code }}</td><td>{{ jalali(a.oldFinish) }}</td><td>{{ jalali(a.newFinish) }}</td><td><span v-if="a.critical" class="inv-badge bad">بحرانی</span></td></tr></tbody></table>
          <div class="row g-2 small mb-2">
            <div v-for="[k, t] in [['impactScope', 'محدوده'], ['impactResources', 'منابع'], ['impactQuality', 'کیفیت'], ['impactRisk', 'ریسک'], ['impactProcurement', 'تامین'], ['impactClientContract', 'قرارداد با کارفرما']]" :key="k" class="col-md-4"><b>{{ t }}:</b> {{ d[k] || "—" }}</div>
          </div>
        </template>

        <template v-if="d.board.length">
          <b class="small">هیئت بررسی تغییر</b>
          <table class="table table-sm"><thead><tr><th>سمت</th><th>رای</th><th>رای‌دهنده</th><th>تاریخ</th><th>توضیح</th></tr></thead>
            <tbody><tr v-for="(m, i) in d.board" :key="i"><td>{{ unitTitle(m.role) }}</td><td><span :class="m.vote === 1 ? 'ok' : m.vote === 2 ? 'bad' : 'info'" class="inv-badge">{{ votes[m.vote] }}</span></td><td>{{ m.userName }}</td><td>{{ jalali(m.votedAt) }}</td><td>{{ m.comment }}</td></tr></tbody></table>
        </template>
        <div v-if="d.recommendation" class="small mb-2"><b>پیشنهاد مدیر پروژه:</b> {{ d.recommendation }}</div>
        <div v-if="d.decision" class="alert alert-info py-2 small">تصمیم: <b>{{ changeDecisions[d.decision] }}</b> — {{ unitTitle(d.decidedByRole) }} — {{ jalali(d.decidedAt) }} — {{ d.decisionNote }}<span v-if="d.baselineId"> — خط مبنای جدید صادر شد.</span></div>
        <div v-if="d.escalated" class="alert alert-warning py-2 small">مبلغ یا تاخیر از سقف اختیار فراتر است؛ تصمیم با مدیرعامل است.</div>

        <div class="border-top pt-2 d-print-none">
          <button v-if="d.status === 0" :disabled="busy" class="btn btn-sm btn-primary me-2" @click="act('register')">ثبت رسمی و تشکیل هیئت</button>
          <button v-if="d.status === 0 && canCreate" class="btn btn-sm btn-outline-secondary" @click="form = { ...d, scenario: d.scenario.map((s: any) => ({ ...s })) }; d = null">ویرایش پیش‌نویس</button>
          <template v-if="(d.status === 1 || d.status === 2) && canAnalyze">
            <b class="small d-block mt-2">تحلیل اثر (توضیحات اختیاری؛ اثر زمانی و هزینه‌ای را سیستم محاسبه می‌کند)</b>
            <div class="row g-2 my-1">
              <div v-for="[k, t] in [['impactScope', 'اثر بر محدوده'], ['impactResources', 'اثر بر منابع'], ['impactQuality', 'اثر بر کیفیت'], ['impactRisk', 'اثر بر ریسک'], ['impactProcurement', 'اثر بر تامین'], ['impactClientContract', 'اثر بر قرارداد با کارفرما']]" :key="k" class="col-md-4"><input v-model="sub[k]" class="form-control form-control-sm" :placeholder="t" /></div>
            </div>
            <button :disabled="busy" class="btn btn-sm btn-primary" @click="act('analyze', { impactScope: sub.impactScope, impactResources: sub.impactResources, impactQuality: sub.impactQuality, impactRisk: sub.impactRisk, impactProcurement: sub.impactProcurement, impactClientContract: sub.impactClientContract })">اجرای تحلیل اثر</button>
          </template>
          <template v-if="d.status === 2">
            <div v-if="canRecommend" class="d-flex gap-2 mt-3"><input v-model="sub.note" class="form-control form-control-sm" placeholder="پیشنهاد مدیر پروژه" /><button :disabled="busy || !sub.note" class="btn btn-sm btn-outline-primary text-nowrap" @click="act('recommend', { note: sub.note })">ثبت پیشنهاد</button></div>
            <div v-if="myOpenVote" class="d-flex gap-2 mt-3"><select v-model.number="sub.vote" class="form-select form-select-sm" style="width: 130px"><option :value="1">موافق</option><option :value="2">مخالف</option></select><input v-model="sub.voteNote" class="form-control form-control-sm" placeholder="توضیح رای" /><button :disabled="busy" class="btn btn-sm btn-outline-primary text-nowrap" @click="act('vote', { vote: sub.vote, boardRole: role, note: sub.voteNote })">ثبت رای</button></div>
            <div class="d-flex gap-2 mt-3"><select v-model.number="sub.decision" class="form-select form-select-sm" style="width: 190px"><option v-for="k in [1, 2, 3, 4]" :key="k" :value="k">{{ changeDecisions[k] }}</option></select><input v-model="sub.decNote" class="form-control form-control-sm" placeholder="دلیل تصمیم (لازم)" /><button :disabled="busy || !sub.decNote" class="btn btn-sm btn-success text-nowrap" @click="act('decide', { decision: sub.decision, note: sub.decNote })">ثبت تصمیم</button></div>
            <p class="small inv-muted mt-1 mb-0">تایید فقط پس از رای همه اعضای هیئت و ثبت پیشنهاد مدیر پروژه ممکن است؛ با رای مخالف تنها مدیرعامل می‌تواند تایید کند. تایید، خط مبنای جدید می‌سازد.</p>
          </template>
          <button v-if="d.status === 4 && canClose" :disabled="busy" class="btn btn-sm btn-outline-success" @click="act('close')">بستن تغییر</button>
        </div>
      </div>
    </div>
    <div v-if="editAuth" class="inv-modal-backdrop" @click.self="editAuth = null">
      <div class="inv-modal">
        <h3 class="h6">اختیار {{ unitTitle(editAuth.role) }}</h3>
        <label class="form-check mb-2"><input v-model="editAuth.canDecide" class="form-check-input" type="checkbox" /> حق تصمیم‌گیری</label>
        <label class="form-label small mb-0">سقف مبلغ (ریال، خالی = بدون سقف)</label><input v-model.number="editAuth.maxAmount" class="form-control form-control-sm mb-2" type="number" min="0" />
        <label class="form-label small mb-0">سقف تاخیر (روز، خالی = بدون سقف)</label><input v-model.number="editAuth.maxDelayDays" class="form-control form-control-sm mb-2" type="number" min="0" />
        <label class="form-label small mb-0">توضیح</label><input v-model="editAuth.note" class="form-control form-control-sm mb-3" />
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="editAuth = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveAuth">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
