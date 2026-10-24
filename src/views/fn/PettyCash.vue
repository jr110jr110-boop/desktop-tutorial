<script setup lang="ts">
import { onMounted, ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import FnDate from "@/components/fn/FnDate.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import QPager from "@/components/quality/QPager.vue";
import UserSelect from "@/components/quality/UserSelect.vue";
import { getAccounts, getPettyCandidates, getPettyCashes, getSettlements, savePettyCash, saveSettlement, settlementAction } from "@/api/FnAPIs";
import { getCostCenters } from "@/api/FinanceAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { badgeOfDoc, docStatuses, money, parseAmount, usePagedList, yesNo } from "@/composables/fn";
import { FnRoles, hasRole } from "@/composables/roles";

// Petty cash holders (ceiling, balance) and their settlements: expense lines (expense account, VAT, purchase receipt) -> approved by finance (not the holder) -> posted.
const tab = ref("settlements");
const tabs = [{ key: "settlements", title: "تسویه تنخواه" }, { key: "petty", title: "تنخواه‌گردان‌ها" }];
const pettyFilters = ref({});
const setFilters = ref({ PettyCashId: null as number | null, Status: null as number | null });
const petties = usePagedList(getPettyCashes, pettyFilters, 200);
const settlements = usePagedList(getSettlements, setFilters);
const pettyForm = ref<any>(null);
const form = ref<any>(null);
const candidates = ref<any[]>([]);
const costCenters = ref<any[]>([]);
const busy = ref(false);
const canManage = hasRole(FnRoles.treasury);
const canApprove = hasRole(FnRoles.accountants);
const canWrite = hasRole(FnRoles.treasuryAndAccountants);

const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);
const searchExpense = async (t: string) => rowsOf((await getAccounts({ Level: 3, Type: 5, Contains: t, PageSize: 15 }))[0]);
const accountLabel = (a: any) => `${a.code} - ${a.title}`;

async function savePetty() {
  const f = pettyForm.value;
  if (!f.holderUserId) return fail("دارنده تنخواه را انتخاب کنید.");
  busy.value = true;
  const [, error] = await savePettyCash({ id: f.id, holderUserId: f.holderUserId, holderTitle: f.holderTitle, ceiling: parseAmount(f.ceiling) || 0, isActive: !!f.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  pettyForm.value = null;
  petties.load();
}

function newSettlement() {
  form.value = { id: null, pettyCashId: setFilters.value.PettyCashId || petties.rows.value[0]?.id || null, date: today(), lines: [blankLine()], status: 0 };
  loadCandidates();
}
const blankLine = () => ({ description: "", account: null as any, amount: "", vatAmount: "", supplierName: "", costCenterId: null as number | null, inventoryDocumentId: null as number | null });

async function loadCandidates() {
  candidates.value = [];
  if (!form.value?.pettyCashId) return;
  const [data] = await getPettyCandidates(form.value.pettyCashId, { PageSize: 100 });
  candidates.value = rowsOf(data);
}

function addCandidate(c: any) {
  if (form.value.lines.some((l: any) => l.inventoryDocumentId === c.inventoryDocumentId)) return;
  const empty = form.value.lines.length === 1 && !form.value.lines[0].account && !form.value.lines[0].amount;
  const line = { ...blankLine(), description: `رسید خرید ${c.number}`, amount: String(c.amount), inventoryDocumentId: c.inventoryDocumentId };
  if (empty) form.value.lines = [line];
  else form.value.lines.push(line);
}

function open(s: any) {
  form.value = {
    id: s.id, number: s.number, pettyCashId: s.pettyCashId, date: String(s.date).slice(0, 10), status: s.status, holderName: s.holderName, creatorName: s.creatorName, approvedByName: s.approvedByName,
    lines: s.lines.map((l: any) => ({
      description: l.description || "", account: { id: l.accountId, code: l.accountCode, title: "" }, amount: String(l.amount), vatAmount: l.vatAmount ? String(l.vatAmount) : "", supplierName: l.supplierName || "",
      costCenterId: l.costCenterId, inventoryDocumentId: l.inventoryDocumentId,
    })),
  };
  candidates.value = [];
}

const total = () => (form.value ? form.value.lines.reduce((s: number, l: any) => s + (parseAmount(l.amount) || 0) + (parseAmount(l.vatAmount) || 0), 0) : 0);

async function saveForm() {
  const f = form.value;
  if (!f.pettyCashId) return fail("تنخواه را انتخاب کنید.");
  const lines: any[] = [];
  for (let i = 0; i < f.lines.length; i++) {
    const l = f.lines[i];
    if (!l.account && !parseAmount(l.amount)) continue;
    if (!l.account) return fail(`ردیف ${i + 1}: حساب هزینه را انتخاب کنید.`);
    if (!(parseAmount(l.amount) || 0)) return fail(`ردیف ${i + 1}: مبلغ را وارد کنید.`);
    lines.push({
      description: l.description || null, accountId: l.account.id, amount: parseAmount(l.amount), vatAmount: parseAmount(l.vatAmount) || 0,
      supplierName: l.supplierName || null, costCenterId: l.costCenterId || null, inventoryDocumentId: l.inventoryDocumentId || null,
    });
  }
  if (!lines.length) return fail("تسویه حداقل یک ردیف هزینه لازم دارد.");
  busy.value = true;
  const [data, error] = await saveSettlement({ id: f.id, pettyCashId: f.pettyCashId, date: f.date, lines });
  busy.value = false;
  if (error) return fail(error);
  ok("تسویه ذخیره شد.");
  settlements.load();
  open(data);
}

async function act(action: string) {
  busy.value = true;
  const [data, error] = await settlementAction({ id: form.value.id, action });
  busy.value = false;
  if (error) return fail(error);
  ok(action === "approve" ? "تسویه تایید و ثبت شد." : "تسویه باطل شد.");
  settlements.load();
  petties.load();
  open(data);
}

onMounted(async () => {
  const [cc] = await getCostCenters();
  costCenters.value = cc || [];
  petties.load();
  settlements.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">تنخواه و تسویه</h1>
        <button v-if="tab === 'petty' && canManage" class="btn btn-sm btn-primary" @click="pettyForm = { id: null, holderUserId: null, holderTitle: '', ceiling: '', isActive: true }">تنخواه‌گردان جدید</button>
        <button v-if="tab === 'settlements' && canWrite" class="btn btn-sm btn-primary" @click="newSettlement">تسویه جدید</button>
      </div>
      <p class="inv-muted">شارژ تنخواه از صفحه «پرداخت‌ها» (هدف: شارژ تنخواه) انجام می‌شود.</p>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'petty'">
        <table class="table table-sm table-hover">
          <thead><tr><th>دارنده</th><th>سمت</th><th>سقف (ریال)</th><th>مانده (ریال)</th><th>فعال</th><th v-if="canManage"></th></tr></thead>
          <tbody>
            <tr v-if="!petties.rows.value.length"><td colspan="6" class="text-center inv-muted">تنخواه‌گردانی نیست.</td></tr>
            <tr v-for="p in petties.rows.value" :key="p.id" :class="{ 'opacity-50': !p.isActive }">
              <td>{{ p.holderName }}</td><td>{{ p.holderTitle }}</td><td class="fn-num">{{ money(p.ceiling) }}</td><td class="fn-num">{{ money(p.balance) }}</td><td>{{ yesNo(p.isActive) }}</td>
              <td v-if="canManage"><button class="btn btn-sm btn-link p-0" @click="pettyForm = { ...p }">ویرایش</button></td>
            </tr>
          </tbody>
        </table>
      </template>

      <template v-else>
        <div class="inv-filters mb-3">
          <select v-model="setFilters.PettyCashId" class="form-select form-select-sm"><option :value="null">همه تنخواه‌ها</option><option v-for="p in petties.rows.value" :key="p.id" :value="p.id">{{ p.holderName }}</option></select>
          <select v-model="setFilters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in docStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
          <button class="btn btn-sm btn-secondary" @click="settlements.search">جستجو</button>
        </div>
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>تاریخ</th><th>دارنده تنخواه</th><th>مبلغ (ریال)</th><th>مالیات</th><th>وضعیت</th><th>ثبت‌کننده</th></tr></thead>
          <tbody>
            <tr v-if="!settlements.rows.value.length"><td colspan="7" class="text-center inv-muted">تسویه‌ای نیست.</td></tr>
            <tr v-for="s in settlements.rows.value" :key="s.id" style="cursor: pointer" @click="open(s)">
              <td class="inv-ltr">{{ s.number }}</td><td>{{ jalali(s.date) }}</td><td>{{ s.holderName }}</td><td class="fn-num">{{ money(s.totalAmount) }}</td><td class="fn-num">{{ money(s.vatAmount) }}</td>
              <td><span :class="badgeOfDoc(s.status)" class="inv-badge">{{ docStatuses[s.status] }}</span></td><td>{{ s.creatorName }}</td>
            </tr>
          </tbody>
        </table>
        <QPager :page="settlements.page.value" :total="settlements.total.value" @go="settlements.go" />
      </template>
    </div>

    <div v-if="pettyForm" class="inv-modal-backdrop screen-only" @click.self="pettyForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">تنخواه‌گردان</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">دارنده تنخواه *</label><UserSelect v-model="pettyForm.holderUserId" /></div>
          <div class="col-6"><label class="form-label small mb-0">سمت</label><input v-model="pettyForm.holderTitle" class="form-control form-control-sm" /></div>
          <div class="col-6"><label class="form-label small mb-0">سقف تنخواه (ریال)</label><input v-model="pettyForm.ceiling" class="form-control form-control-sm fn-num" /></div>
          <div class="col-12"><label class="form-check small"><input v-model="pettyForm.isActive" class="form-check-input" type="checkbox" /> فعال</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="pettyForm = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="savePetty">ذخیره</button>
        </div>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">
          تسویه تنخواه <span v-if="form.number" class="inv-ltr">{{ form.number }}</span>
          <span v-if="form.id" :class="badgeOfDoc(form.status)" class="inv-badge">{{ docStatuses[form.status] }}</span>
        </h2>
        <div class="row g-2 mb-2">
          <div class="col-md-6"><label class="form-label small mb-0">تنخواه</label>
            <select v-model="form.pettyCashId" :disabled="!!form.id" class="form-select form-select-sm" @change="loadCandidates"><option v-for="p in petties.rows.value" :key="p.id" :value="p.id">{{ p.holderName }} (مانده {{ money(p.balance) }})</option></select>
          </div>
          <div class="col-md-3"><label class="form-label small mb-0">تاریخ</label><FnDate v-model="form.date" :disabled="form.status !== 0" /></div>
        </div>

        <div v-if="form.status === 0 && candidates.length" class="mb-2">
          <div class="inv-muted">رسیدهای خرید تنخواهی تسویه‌نشده (برای افزودن بزنید):</div>
          <button v-for="c in candidates" :key="c.inventoryDocumentId" class="btn btn-sm btn-outline-secondary me-1 mb-1" @click="addCandidate(c)">{{ c.number }} - {{ money(c.amount) }}</button>
        </div>

        <div class="table-responsive">
          <table class="table table-sm fn-lines">
            <thead><tr><th>شرح</th><th>رسید خرید انبار</th><th style="min-width: 220px">حساب هزینه *</th><th>تامین‌کننده</th><th>مرکز هزینه</th><th>مبلغ *</th><th>مالیات</th><th v-if="form.status === 0"></th></tr></thead>
            <tbody>
              <tr v-for="(l, i) in form.lines" :key="i">
                <td><input v-model="l.description" :disabled="form.status !== 0" class="form-control form-control-sm" /></td>
                <td><span v-if="l.inventoryDocumentId" class="inv-badge ok">رسید {{ l.inventoryDocumentId }}</span><span v-else class="inv-muted">-</span></td>
                <td>
                  <LookupBox v-if="form.status === 0" v-model="l.account" :fetch="searchExpense" :label="accountLabel" placeholder="حساب هزینه" />
                  <span v-else class="inv-ltr">{{ l.account?.code }}</span>
                </td>
                <td><input v-model="l.supplierName" :disabled="form.status !== 0" class="form-control form-control-sm" /></td>
                <td><select v-model="l.costCenterId" :disabled="form.status !== 0" class="form-select form-select-sm"><option :value="null">-</option><option v-for="c in costCenters" :key="c.id" :value="c.id">{{ c.code }}</option></select></td>
                <td><input v-model="l.amount" :disabled="form.status !== 0" class="form-control form-control-sm fn-num" /></td>
                <td><input v-model="l.vatAmount" :disabled="form.status !== 0" class="form-control form-control-sm fn-num" /></td>
                <td v-if="form.status === 0"><button class="btn btn-sm btn-link text-danger p-0" @click="form.lines.splice(i, 1)">حذف</button></td>
              </tr>
              <tr class="fn-total"><td colspan="4">جمع کل (با مالیات)</td><td class="fn-num" colspan="2">{{ money(total()) }}</td><td v-if="form.status === 0"></td></tr>
            </tbody>
          </table>
        </div>
        <button v-if="form.status === 0" class="btn btn-sm btn-outline-secondary mb-2" @click="form.lines.push(blankLine())">+ ردیف</button>
        <p v-if="form.id" class="inv-muted">ثبت‌کننده: {{ form.creatorName }} <span v-if="form.approvedByName">| تاییدکننده: {{ form.approvedByName }}</span></p>
        <div class="d-flex gap-2 justify-content-end flex-wrap">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">بستن</button>
          <template v-if="form.status === 0">
            <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-primary" @click="saveForm">ذخیره</button>
            <button v-if="form.id && canApprove" :disabled="busy" class="btn btn-sm btn-success" @click="act('approve')">تایید و ثبت (غیر از دارنده تنخواه)</button>
            <button v-if="form.id" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('cancel')">ابطال</button>
          </template>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
