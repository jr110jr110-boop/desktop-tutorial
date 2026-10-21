<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import { assetAction, getAsset, getAssetClasses, getAssets, getEmployeeDirectory, saveAsset } from "@/api/FnAPIs";
import { getCostCenters } from "@/api/FinanceAPIs";
import { assetHistoryKinds, assetSources, assetStatuses, money, parseAmount, usePagedList } from "@/composables/fn";
import { accountLabel, searchAccounts } from "@/composables/fnLookups";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { FnRoles, hasRole } from "@/composables/roles";

// Fixed asset register. A draft asset is approved by a second person; after that changes happen only through actions (commission, idle, transfer, improvement).
const filters = ref({ Status: null as number | null, ClassId: null as number | null, CostCenterId: null as number | null, Contains: "" });
const list = usePagedList(getAssets, filters);
const classes = ref<any[]>([]);
const costCenters = ref<any[]>([]);
const form = ref<any>(null);
const detail = ref<any>(null);
const act = ref<null | { asset: any; action: string; title: string; date: string | null; amount: string; note: string; account: any; costCenterId: number | null; custodian: any; location: string }>(null);
const busy = ref(false);
const canWrite = hasRole(FnRoles.accountants);

const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);
const searchEmployees = async (t: string) => rowsOf((await getEmployeeDirectory(t))[0]);
const employeeLabel = (e: any) => `${e.name}${e.jobTitle ? " - " + e.jobTitle : ""}`;
const badge = (s: number) => (s === 1 ? "ok" : s === 0 ? "warn" : s === 4 ? "bad" : "info");

function blank() {
  return { id: null, title: "", classId: null, serial: "", maker: "", location: "", custodian: null, custodianTitle: "", costCenterId: null, acquisitionDate: today(), inUseDate: null, cost: "", salvageValue: "", source: 1, sourceNumber: "", offsetAccount: null, openingAccumulated: "", openingDepreciatedThrough: null };
}

async function edit(row: any) {
  const [d, error] = await getAsset(row.id);
  if (error) return fail(error);
  form.value = {
    id: d.id, title: d.title, classId: d.classId, serial: d.serial || "", maker: d.maker || "", location: d.location || "",
    custodian: d.custodianEmployeeId ? { id: d.custodianEmployeeId, name: d.custodianTitle || "", jobTitle: "" } : null, custodianTitle: d.custodianEmployeeId ? "" : d.custodianTitle || "",
    costCenterId: d.costCenterId, acquisitionDate: d.acquisitionDate ? String(d.acquisitionDate).slice(0, 10) : null, inUseDate: d.inUseDate ? String(d.inUseDate).slice(0, 10) : null,
    cost: String(d.cost), salvageValue: d.salvageValue ? String(d.salvageValue) : "", source: d.source, sourceNumber: d.sourceNumber || "",
    offsetAccount: d.offsetAccountCode ? { code: d.offsetAccountCode, title: "" } : null, openingAccumulated: d.openingAccumulated ? String(d.openingAccumulated) : "", openingDepreciatedThrough: d.depreciatedThrough ? String(d.depreciatedThrough).slice(0, 10) : null,
  };
}

async function save() {
  const f = form.value;
  const cost = parseAmount(f.cost);
  if (!String(f.title).trim()) return fail("عنوان دارایی را وارد کنید.");
  if (!f.classId) return fail("رده دارایی را انتخاب کنید.");
  if (!cost || cost <= 0) return fail("بهای تمام‌شده را وارد کنید.");
  busy.value = true;
  const [, error] = await saveAsset({
    id: f.id, title: f.title, classId: f.classId, serial: f.serial || null, maker: f.maker || null, location: f.location || null,
    custodianEmployeeId: f.custodian?.id || null, custodianTitle: f.custodian ? f.custodian.name : f.custodianTitle || null, costCenterId: f.costCenterId, acquisitionDate: f.acquisitionDate, inUseDate: f.inUseDate,
    cost, salvageValue: f.salvageValue === "" ? null : parseAmount(f.salvageValue), source: Number(f.source), sourceNumber: f.sourceNumber || null,
    offsetAccountCode: f.source === 4 ? null : f.offsetAccount?.code || null, openingAccumulated: f.source === 4 ? parseAmount(f.openingAccumulated) || 0 : null, openingDepreciatedThrough: f.source === 4 ? f.openingDepreciatedThrough : null,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("دارایی ذخیره شد.");
  form.value = null;
  list.load();
}

const actionTitles: Record<string, string> = { approve: "تایید دارایی", commission: "بهره‌برداری", "idle-from": "شروع بلااستفاده", "idle-to": "پایان بلااستفاده", transfer: "انتقال دارایی", improve: "بهسازی (افزایش بها)" };
function ask(asset: any, action: string) {
  act.value = { asset, action, title: actionTitles[action], date: today(), amount: "", note: "", account: asset.offsetAccountCode ? { code: asset.offsetAccountCode, title: "" } : null, costCenterId: null, custodian: null, location: "" };
}
async function runAction() {
  const a = act.value!;
  if (["approve", "improve"].includes(a.action) && a.asset.source !== 4 && !a.account?.code) return fail("حساب طرف بستانکار را انتخاب کنید.");
  if (a.action === "improve" && !(parseAmount(a.amount) > 0)) return fail("مبلغ بهسازی را وارد کنید.");
  if (a.action === "transfer" && !a.costCenterId && !a.custodian && !a.location.trim()) return fail("مقصد انتقال را مشخص کنید.");
  busy.value = true;
  const [, error] = await assetAction({
    id: a.asset.id, action: a.action, date: a.date, amount: a.amount ? parseAmount(a.amount) : null, note: a.note || null, offsetAccountCode: a.account?.code || null,
    costCenterId: a.costCenterId, custodianEmployeeId: a.custodian?.id || null, custodianTitle: a.custodian ? a.custodian.name : null, location: a.location || null,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("انجام شد.");
  act.value = null;
  list.load();
}

async function show(row: any) {
  const [d, error] = await getAsset(row.id);
  if (error) return fail(error);
  detail.value = d;
}

onMounted(async () => {
  classes.value = rowsOf((await getAssetClasses())[0]);
  costCenters.value = (await getCostCenters())[0] || [];
  list.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">دفتر دارایی‌های ثابت</h1>
        <div class="d-flex gap-2">
          <router-link :to="{ name: 'FnAssetReports' }" class="btn btn-sm btn-outline-secondary">گزارش‌ها و تحویل دارایی</router-link>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = blank()">دارایی جدید</button>
        </div>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in assetStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.ClassId" class="form-select form-select-sm"><option :value="null">همه رده‌ها</option><option v-for="c in classes" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option></select>
        <select v-model="filters.CostCenterId" class="form-select form-select-sm"><option :value="null">همه مراکز هزینه</option><option v-for="c in costCenters" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option></select>
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="کد، عنوان یا سریال" @keydown.enter="list.search" />
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>کد</th><th>عنوان</th><th>رده</th><th>مرکز هزینه</th><th>امین</th><th>بها (ریال)</th><th>استهلاک انباشته</th><th>ارزش دفتری</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="10" class="text-center inv-muted">دارایی‌ای نیست.</td></tr>
            <tr v-for="a in list.rows.value" :key="a.id">
              <td class="inv-ltr">{{ a.code }}</td><td>{{ a.title }}</td><td>{{ a.classTitle }}</td><td>{{ a.costCenterCode }}</td><td>{{ a.custodianTitle }}</td>
              <td class="fn-num">{{ money(a.cost) }}</td><td class="fn-num">{{ money(a.accumulatedDepreciation) }}</td><td class="fn-num">{{ money(a.bookValue) }}</td>
              <td><span :class="badge(a.status)" class="inv-badge">{{ assetStatuses[a.status] }}</span></td>
              <td class="text-nowrap">
                <button class="btn btn-sm btn-link p-0 me-2" @click="show(a)">سابقه</button>
                <template v-if="canWrite">
                  <template v-if="a.status === 0"><button class="btn btn-sm btn-outline-secondary me-1" @click="edit(a)">ویرایش</button><button class="btn btn-sm btn-success" @click="ask(a, 'approve')">تایید</button></template>
                  <template v-if="a.status === 3"><button class="btn btn-sm btn-outline-primary me-1" @click="ask(a, 'commission')">بهره‌برداری</button><button class="btn btn-sm btn-outline-secondary me-1" @click="ask(a, 'improve')">بهسازی</button></template>
                  <template v-if="a.status === 1"><button class="btn btn-sm btn-outline-secondary me-1" @click="ask(a, 'idle-from')">بلااستفاده</button><button class="btn btn-sm btn-outline-secondary me-1" @click="ask(a, 'transfer')">انتقال</button><button class="btn btn-sm btn-outline-secondary" @click="ask(a, 'improve')">بهسازی</button></template>
                  <template v-if="a.status === 2"><button class="btn btn-sm btn-outline-primary me-1" @click="ask(a, 'idle-to')">بازگشت به کار</button><button class="btn btn-sm btn-outline-secondary" @click="ask(a, 'transfer')">انتقال</button></template>
                </template>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ detail.code }} - {{ detail.title }}</h2>
        <p class="small">منبع: {{ assetSources[detail.source] }} {{ detail.sourceNumber }} | تاریخ تحصیل: {{ detail.acquisitionDate ? jalali(detail.acquisitionDate) : "" }} | بهره‌برداری: {{ detail.inUseDate ? jalali(detail.inUseDate) : "" }} | استهلاک تا: {{ detail.depreciatedThrough ? jalali(detail.depreciatedThrough) : "" }}</p>
        <p class="small">ثبت‌کننده: {{ detail.creatorName }} | تاییدکننده: {{ detail.approvedByName }}</p>
        <table class="table table-sm">
          <thead><tr><th>تاریخ</th><th>رویداد</th><th>مبلغ</th><th>توضیح</th><th>کاربر</th></tr></thead>
          <tbody><tr v-for="(h, i) in detail.history" :key="i"><td>{{ jalali(h.date) }}</td><td>{{ assetHistoryKinds[h.kind] }}</td><td class="fn-num">{{ h.amount ? money(h.amount) : "" }}</td><td>{{ h.note }}</td><td>{{ h.userName }}</td></tr></tbody>
        </table>
        <div class="text-end"><button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button></div>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش پیش‌نویس دارایی" : "دارایی جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-5"><label class="form-label small mb-0">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">رده *</label><select v-model="form.classId" class="form-select form-select-sm"><option :value="null">انتخاب...</option><option v-for="c in classes.filter((x) => x.isActive)" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">منبع</label><select v-model="form.source" class="form-select form-select-sm"><option :value="1">{{ assetSources[1] }}</option><option :value="3">{{ assetSources[3] }}</option><option :value="4">{{ assetSources[4] }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">سریال</label><input v-model="form.serial" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">سازنده</label><input v-model="form.maker" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">محل استقرار</label><input v-model="form.location" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">شماره سند منبع</label><input v-model="form.sourceNumber" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">امین دارایی</label><LookupBox v-model="form.custodian" :fetch="searchEmployees" :label="employeeLabel" placeholder="نام کارمند" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">مرکز هزینه</label><select v-model="form.costCenterId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="c in costCenters" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">تاریخ تحصیل</label><FnDate v-model="form.acquisitionDate" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">تاریخ بهره‌برداری</label><FnDate v-model="form.inUseDate" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">بهای تمام‌شده (ریال) *</label><input v-model="form.cost" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">ارزش اسقاط (ریال)</label><input v-model="form.salvageValue" class="form-control form-control-sm fn-num" /></div>
          <template v-if="form.source === 4">
            <div class="col-md-3"><label class="form-label small mb-0">استهلاک انباشته دفاتر قبلی</label><input v-model="form.openingAccumulated" class="form-control form-control-sm fn-num" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">استهلاک تا تاریخ (ماه آخر)</label><FnDate v-model="form.openingDepreciatedThrough" /></div>
          </template>
          <div v-else class="col-md-6"><label class="form-label small mb-0">حساب طرف بستانکار (محل فعلی بها)</label><LookupBox v-model="form.offsetAccount" :fetch="searchAccounts" :label="accountLabel" :fallback-text="form.offsetAccount?.code" placeholder="کد یا نام حساب" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره پیش‌نویس</button></div>
      </div>
    </div>

    <div v-if="act" class="inv-modal-backdrop screen-only" @click.self="act = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ act.title }} - {{ act.asset.code }}</h2>
        <div class="row g-2">
          <div v-if="act.action !== 'approve'" class="col-6"><label class="form-label small mb-0">تاریخ</label><FnDate v-model="act.date" /></div>
          <div v-if="act.action === 'improve'" class="col-6"><label class="form-label small mb-0">مبلغ بهسازی (ریال) *</label><input v-model="act.amount" class="form-control form-control-sm fn-num" /></div>
          <div v-if="(act.action === 'approve' && act.asset.source !== 4) || act.action === 'improve'" class="col-12"><label class="form-label small mb-0">حساب طرف بستانکار *</label><LookupBox v-model="act.account" :fetch="searchAccounts" :label="accountLabel" :fallback-text="act.account?.code" placeholder="کد یا نام حساب" /></div>
          <template v-if="act.action === 'transfer'">
            <div class="col-12"><label class="form-label small mb-0">مرکز هزینه مقصد</label><select v-model="act.costCenterId" class="form-select form-select-sm"><option :value="null">بدون تغییر</option><option v-for="c in costCenters" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option></select></div>
            <div class="col-12"><label class="form-label small mb-0">امین جدید</label><LookupBox v-model="act.custodian" :fetch="searchEmployees" :label="employeeLabel" placeholder="نام کارمند" /></div>
            <div class="col-12"><label class="form-label small mb-0">محل جدید</label><input v-model="act.location" class="form-control form-control-sm" /></div>
          </template>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="act.note" class="form-control form-control-sm" /></div>
        </div>
        <p v-if="act.action === 'approve'" class="inv-muted small mt-2 mb-0">تایید باید توسط شخصی غیر از ثبت‌کننده انجام شود؛ سند تحصیل دارایی صادر می‌شود.</p>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="act = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="runAction">انجام</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
