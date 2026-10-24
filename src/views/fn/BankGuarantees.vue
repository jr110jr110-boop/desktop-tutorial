<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import ContractPick from "@/components/fn/ContractPick.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import { bankGuaranteeAction, getBankGuarantees, getGuaranteeExposure, saveBankGuarantee, saveGuaranteeLimit } from "@/api/FnAPIs";
import { getSuppliers } from "@/api/ProcurementAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { money, parseAmount } from "@/composables/fn";
import { guaranteeDirections, guaranteeEvents, guaranteeKinds, guaranteeStatuses } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Bank guarantees register (issued by our banks / received from suppliers and contractors): extend, reduce, release, claim, bank limits and exposure per bank.
const tab = ref("register");
const tabs = [{ key: "register", title: "دفتر ضمانت‌نامه‌ها" }, { key: "exposure", title: "تعهدات هر بانک و سقف" }];
const canWrite = hasRole(FnRoles.treasuryAndAccountants);
const canManage = hasRole(FnRoles.managers);
const busy = ref(false);
const direction = ref<number | null>(null);
const list = ref<any[]>([]);
const detail = ref<any>(null);
const form = ref<any>(null);
const act = ref<any>(null);
const suppliers = ref<any[]>([]);
const exposure = ref<any>(null);
const asOf = ref<string | null>(null);
const limitForm = ref<any>(null);

async function load() {
  const [d, error] = await getBankGuarantees(direction.value);
  if (error) return fail(error);
  list.value = d || [];
}
async function loadExposure() {
  const [d, error] = await getGuaranteeExposure(asOf.value);
  if (error) return fail(error);
  exposure.value = d;
}
const blank = () => ({ direction: 1, kind: 1, bankName: "", guaranteeNo: "", partyName: "", providerId: null, contractId: null, amount: "", issueDate: today(), expiryDate: null, feeAmount: "", note: "" });
async function save() {
  const f = form.value;
  if (!String(f.bankName).trim() || !String(f.guaranteeNo).trim() || !String(f.partyName).trim() || !(parseAmount(f.amount) || 0) || !f.issueDate || !f.expiryDate) return fail("بانک، شماره، طرف حساب، مبلغ و تاریخ‌ها را وارد کنید.");
  if (f.expiryDate < f.issueDate) return fail("تاریخ انقضا پیش از تاریخ صدور است.");
  busy.value = true;
  const [d, error] = await saveBankGuarantee({
    direction: Number(f.direction), kind: Number(f.kind), bankName: f.bankName, bankAccountId: null, guaranteeNo: f.guaranteeNo, partyName: f.partyName, providerId: f.providerId || null, customerId: null, contractId: f.contractId || null,
    amount: parseAmount(f.amount), issueDate: f.issueDate, expiryDate: f.expiryDate, feeAmount: parseAmount(f.feeAmount) || 0, note: f.note || null,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("ضمانت‌نامه ثبت شد.");
  form.value = null;
  detail.value = d;
  load();
}
const actionTitles: Record<string, string> = { extend: "تمدید", reduce: "کاهش مبلغ", release: "آزادسازی", expire: "ثبت انقضا", claim: "ثبت مطالبه" };
async function doAct() {
  const a = act.value;
  if (a.action === "extend" && !a.newExpiry) return fail("تاریخ جدید انقضا را وارد کنید.");
  if (a.action === "reduce" && !(parseAmount(a.amount) || 0)) return fail("مبلغ کاهش را وارد کنید.");
  busy.value = true;
  const [d, error] = await bankGuaranteeAction({ id: detail.value.id, action: a.action, date: a.date || today(), newExpiry: a.action === "extend" ? a.newExpiry : null, amount: a.action === "reduce" ? parseAmount(a.amount) : 0, feeAmount: parseAmount(a.feeAmount) || 0, note: a.note || null });
  busy.value = false;
  if (error) return fail(error);
  ok(actionTitles[a.action] + " ثبت شد.");
  act.value = null;
  detail.value = d;
  load();
}
async function saveLimit() {
  const f = limitForm.value;
  if (!String(f.bankName).trim() || parseAmount(f.limit) === null) return fail("بانک و سقف را وارد کنید.");
  busy.value = true;
  const [, error] = await saveGuaranteeLimit({ bankName: f.bankName, limit: parseAmount(f.limit), note: f.note || null });
  busy.value = false;
  if (error) return fail(error);
  ok("سقف ضمانت‌نامه ذخیره شد.");
  limitForm.value = null;
  loadExposure();
}

onMounted(async () => {
  load();
  loadExposure();
  const [s] = await getSuppliers(); // the auditor is not a procurement reader: the supplier list stays empty for that role
  suppliers.value = Array.isArray(s) ? s : s?.data || [];
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">ضمانت‌نامه‌های بانکی</h1>
        <button v-if="canWrite && tab === 'register'" class="btn btn-sm btn-primary" @click="form = blank()">ضمانت‌نامه جدید</button>
        <button v-if="canManage && tab === 'exposure'" class="btn btn-sm btn-primary" @click="limitForm = { bankName: '', limit: '', note: '' }">سقف بانک</button>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'register'">
        <div class="inv-filters mb-3">
          <select v-model="direction" class="form-select form-select-sm" @change="load"><option :value="null">همه</option><option v-for="(t, k) in guaranteeDirections" :key="k" :value="Number(k)">{{ t }}</option></select>
        </div>
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>جهت</th><th>نوع</th><th>بانک</th><th>شماره ضمانت‌نامه</th><th>طرف حساب</th><th>مبلغ (ریال)</th><th>انقضا</th><th>روز مانده</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-if="!list.length"><td colspan="10" class="text-center inv-muted">ضمانت‌نامه‌ای ثبت نشده است.</td></tr>
            <tr v-for="g in list" :key="g.id" style="cursor: pointer" @click="detail = g">
              <td class="inv-ltr">{{ g.number }}</td><td>{{ guaranteeDirections[g.direction] }}</td><td>{{ guaranteeKinds[g.kind] }}</td><td>{{ g.bankName }}</td><td class="inv-ltr">{{ g.guaranteeNo }}</td><td>{{ g.partyName }}</td><td class="fn-num">{{ money(g.amount) }}</td><td>{{ jalali(g.expiryDate) }}</td>
              <td><span :class="g.status === 1 && g.daysToExpiry <= 30 ? 'warn' : ''" class="inv-badge">{{ g.status === 1 ? g.daysToExpiry : "-" }}</span></td><td>{{ guaranteeStatuses[g.status] }}</td>
            </tr>
          </tbody>
        </table>
      </template>

      <template v-else>
        <div class="inv-filters mb-3"><div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="asOf" /></div><button class="btn btn-sm btn-secondary" @click="loadExposure">نمایش</button></div>
        <template v-if="exposure">
          <p>جمع صادرشده: <b>{{ money(exposure.totalIssued) }}</b> - جمع دریافتی: <b>{{ money(exposure.totalReceived) }}</b> ریال</p>
          <table class="table table-sm">
            <thead><tr><th>بانک</th><th>تعداد فعال</th><th>تعهد (ریال)</th><th>سقف</th><th>مانده سقف</th></tr></thead>
            <tbody>
              <tr v-if="!exposure.banks.length"><td colspan="5" class="text-center inv-muted">داده‌ای نیست.</td></tr>
              <tr v-for="b in exposure.banks" :key="b.bankName"><td>{{ b.bankName }}</td><td>{{ b.activeCount }}</td><td class="fn-num">{{ money(b.exposure) }}</td><td class="fn-num">{{ money(b.limit) }}</td><td class="fn-num" :class="b.available !== null && b.available < 0 ? 'text-danger' : ''">{{ b.available === null || b.available === undefined ? "بدون سقف" : money(b.available) }}</td></tr>
            </tbody>
          </table>
          <h2 class="inv-subtitle">سررسید تا ۳۰ روز آینده</h2>
          <table class="table table-sm">
            <thead><tr><th>شماره</th><th>بانک</th><th>طرف حساب</th><th>مبلغ</th><th>انقضا</th></tr></thead>
            <tbody>
              <tr v-if="!exposure.expiringWithin30Days.length"><td colspan="5" class="text-center inv-muted">موردی نیست.</td></tr>
              <tr v-for="g in exposure.expiringWithin30Days" :key="g.id"><td class="inv-ltr">{{ g.number }}</td><td>{{ g.bankName }}</td><td>{{ g.partyName }}</td><td class="fn-num">{{ money(g.amount) }}</td><td>{{ jalali(g.expiryDate) }}</td></tr>
            </tbody>
          </table>
        </template>
      </template>
    </div>

    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2"><span class="inv-ltr">{{ detail.number }}</span> - {{ guaranteeKinds[detail.kind] }} <span class="inv-badge">{{ guaranteeStatuses[detail.status] }}</span></h2>
        <p>
          {{ guaranteeDirections[detail.direction] }} - {{ detail.bankName }} - شماره <span class="inv-ltr">{{ detail.guaranteeNo }}</span> - {{ detail.partyName }}<br />
          مبلغ {{ money(detail.amount) }} ریال - صدور {{ jalali(detail.issueDate) }} - انقضا {{ jalali(detail.expiryDate) }}<br />
          کارمزد {{ money(detail.feeAmount) }} (مستهلک‌شده {{ money(detail.feeAmortised) }}، باقی‌مانده {{ money(detail.feeRemaining) }})
          <span v-if="detail.closedDate"> - بسته‌شده در {{ jalali(detail.closedDate) }}</span> - {{ detail.note }}
        </p>
        <table class="table table-sm">
          <thead><tr><th>تاریخ</th><th>رویداد</th><th>انقضای قبلی</th><th>انقضای جدید</th><th>مبلغ</th><th>کارمزد</th><th>توضیح</th></tr></thead>
          <tbody><tr v-for="e in detail.events" :key="e.id"><td>{{ jalali(e.date) }}</td><td>{{ guaranteeEvents[e.kind] }}</td><td>{{ jalali(e.oldExpiry) }}</td><td>{{ jalali(e.newExpiry) }}</td><td class="fn-num">{{ money(e.amount) }}</td><td class="fn-num">{{ money(e.feeAmount) }}</td><td>{{ e.note }}</td></tr></tbody>
        </table>
        <div class="d-flex gap-2 justify-content-end flex-wrap">
          <button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button>
          <template v-if="canWrite && detail.status === 1">
            <button v-for="(t, k) in actionTitles" :key="k" class="btn btn-sm btn-outline-primary" @click="act = { action: k, date: today(), newExpiry: null, amount: '', feeAmount: '', note: '' }">{{ t }}</button>
          </template>
        </div>
      </div>
    </div>

    <div v-if="act" class="inv-modal-backdrop screen-only" @click.self="act = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ actionTitles[act.action] }}</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">تاریخ</label><FnDate v-model="act.date" /></div>
          <div v-if="act.action === 'extend'" class="col-md-6"><label class="form-label small mb-0">انقضای جدید *</label><FnDate v-model="act.newExpiry" /></div>
          <div v-if="act.action === 'reduce'" class="col-md-6"><label class="form-label small mb-0">مبلغ کاهش (ریال) *</label><input v-model="act.amount" class="form-control form-control-sm fn-num" /></div>
          <div v-if="act.action === 'extend'" class="col-md-6"><label class="form-label small mb-0">کارمزد تمدید (ریال)</label><input v-model="act.feeAmount" class="form-control form-control-sm fn-num" /></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="act.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="act = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="doAct">ثبت</button></div>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">ضمانت‌نامه جدید</h2>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">جهت</label><select v-model="form.direction" class="form-select form-select-sm"><option v-for="(t, k) in guaranteeDirections" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">نوع</label><select v-model="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in guaranteeKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">بانک *</label><input v-model="form.bankName" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">شماره ضمانت‌نامه *</label><input v-model="form.guaranteeNo" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">طرف حساب *</label><input v-model="form.partyName" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">تامین‌کننده</label><select v-model="form.providerId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">پروژه</label><ContractPick v-model="form.contractId" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">مبلغ (ریال) *</label><input v-model="form.amount" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">کارمزد (ریال)</label><input v-model="form.feeAmount" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">تاریخ صدور *</label><FnDate v-model="form.issueDate" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">تاریخ انقضا *</label><FnDate v-model="form.expiryDate" /></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="form.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ثبت</button></div>
      </div>
    </div>

    <div v-if="limitForm" class="inv-modal-backdrop screen-only" @click.self="limitForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">سقف ضمانت‌نامه بانک</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">بانک *</label><input v-model="limitForm.bankName" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">سقف (ریال) *</label><input v-model="limitForm.limit" class="form-control form-control-sm fn-num" /></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="limitForm.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="limitForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveLimit">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
