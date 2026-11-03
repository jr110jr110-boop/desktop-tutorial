<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import QPager from "@/components/quality/QPager.vue";
import { createReconciliation, getBankAccounts, getReconciliations, getStatements, reconciliationAction, saveReconciliationItems } from "@/api/FnAPIs";
import { money, reconciliationStatuses, usePagedList } from "@/composables/fn";
import { fail, jalali, ok } from "@/composables/inventory";
import { parseAmount } from "@/composables/fn";
import { FnRoles, hasRole } from "@/composables/roles";

// Bank reconciliation: book balance and bank balance, deposits in transit and outstanding payments. The manager approves it when the difference is zero.
const filters = ref({ BankAccountId: null as number | null });
const list = usePagedList(getReconciliations, filters);
const banks = ref<any[]>([]);
const form = ref<null | { bankAccountId: number | null; statementId: number | null; asOfDate: string | null; statements: any[] }>(null);
const confirmApprove = ref<any>(null);
const busy = ref(false);
const canWrite = hasRole(FnRoles.treasuryAndAccountants);
const canApprove = hasRole(FnRoles.managers);

async function openForm() {
  form.value = { bankAccountId: null, statementId: null, asOfDate: null, statements: [] };
}
async function loadStatements() {
  const f = form.value!;
  f.statementId = null;
  const [d] = await getStatements({ BankAccountId: f.bankAccountId, PageSize: 100 });
  f.statements = Array.isArray(d) ? d : d?.data || [];
}
async function create() {
  const f = form.value!;
  if (!f.statementId) return fail("صورت‌حساب را انتخاب کنید.");
  busy.value = true;
  const [, error] = await createReconciliation(f.statementId, f.asOfDate);
  busy.value = false;
  if (error) return fail(error);
  ok("مغایرت‌گیری ساخته شد.");
  form.value = null;
  list.search();
}
// the explained reconciling items of a draft reconciliation (R1t D7)
const itemKinds: Record<number, string> = { 1: "کارمزد یا سود بانکی ثبت‌نشده", 2: "واریز یا پرداخت در راه", 3: "خطای بانک", 4: "خطا در دفاتر شرکت", 5: "مانده قبل از شروع کار سیستم", 9: "سایر (علت را بنویسید)" };
const items = ref<null | { rec: any; rows: any[] }>(null);
const openItems = (r: any) => (items.value = { rec: r, rows: (r.items || []).map((i: any) => ({ kind: i.kind, amount: String(i.amount), note: i.note || "", correctingVoucherId: i.correctingVoucherId ? String(i.correctingVoucherId) : "" })) });
const itemsSum = () => (items.value?.rows || []).reduce((s, i) => s + (parseAmount(i.amount) || 0), 0);
async function saveItems() {
  const d = items.value!;
  const rows = d.rows.map((i) => ({ kind: Number(i.kind), amount: parseAmount(i.amount) || 0, note: String(i.note).trim(), correctingVoucherId: i.correctingVoucherId ? Number(i.correctingVoucherId) : null }));
  busy.value = true;
  const [, error] = await saveReconciliationItems(d.rec.id, rows);
  busy.value = false;
  if (error) return fail(error);
  ok("اقلام مغایرت ذخیره شد. تایید با مدیر مالی دیگری (غیر از تهیه‌کننده و ثبت‌کننده اقلام) است.");
  items.value = null;
  list.load();
}
/** approval needs the difference to be fully explained (items add up to it) and every line of the statement matched; the API checks all of it again */
const explained = (r: any) => (r.difference === 0 && !(r.items || []).length) || r.explainedAmount === r.difference;

async function act(r: any, action: string, v?: any) {
  busy.value = true;
  const [, error] = await reconciliationAction({ id: r.id, action, note: v?.note });
  busy.value = false;
  if (error) return fail(error);
  ok(action === "approve" ? "مغایرت‌گیری تایید شد." : "مغایرت‌گیری به‌روز شد.");
  list.load();
}

onMounted(async () => {
  const [b] = await getBankAccounts({ PageSize: 200 });
  banks.value = (b?.data || []).filter((x: any) => x.isActive);
  list.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">مغایرت‌گیری بانکی</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="openForm">مغایرت‌گیری جدید</button>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="filters.BankAccountId" class="form-select form-select-sm" @change="list.search"><option :value="null">همه حساب‌ها</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select>
      </div>
      <p class="inv-muted small">اختلاف = مانده بانک + واریز در راه - پرداخت معوق - مانده دفتر. چک‌های صادرشده معوق فقط اطلاعاتی است و در این فرمول نمی‌آید (صدور چک بدهکاری اسناد پرداختنی را بالا می‌برد، نه حساب بانک را).</p>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>حساب</th><th>تا تاریخ</th><th>مانده دفتر</th><th>مانده بانک</th><th>واریز در راه</th><th>پرداخت معوق</th><th>چک معوق (اطلاعاتی)</th><th>اختلاف</th><th>ردیف تطبیق‌نشده</th><th>تهیه‌کننده</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="12" class="text-center inv-muted">مغایرت‌گیری نیست.</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id">
              <td>{{ r.bankAccountTitle }}</td><td>{{ jalali(r.asOfDate) }}</td><td class="fn-num">{{ money(r.bookBalance) }}</td><td class="fn-num">{{ money(r.bankBalance) }}</td>
              <td class="fn-num">{{ money(r.depositsInTransit) }}</td><td class="fn-num">{{ money(r.outstandingPayments) }}</td><td class="fn-num">{{ money(r.outstandingCheques) }}</td>
              <td class="fn-num"><span :class="r.difference && !explained(r) ? 'bad' : 'ok'" :title="`${money(r.bankBalance)} + ${money(r.depositsInTransit)} - ${money(r.outstandingPayments)} - ${money(r.bookBalance)}`" class="inv-badge">{{ money(r.difference) }}</span></td>
              <td>{{ r.unmatchedLines }}</td><td>{{ r.preparedByName }}</td>
              <td><span :class="r.status === 1 ? 'ok' : 'warn'" class="inv-badge">{{ reconciliationStatuses[r.status] }}</span><span v-if="r.approvedByName" class="inv-muted me-1">{{ r.approvedByName }}</span></td>
              <td class="text-nowrap">
                <template v-if="r.status === 0 && canWrite">
                  <button class="btn btn-sm btn-outline-secondary me-1" :disabled="busy" @click="act(r, 'refresh')">به‌روزرسانی</button>
                  <button class="btn btn-sm btn-outline-primary me-1" :disabled="busy" @click="openItems(r)">اقلام مغایرت<template v-if="r.items?.length"> ({{ r.items.length }})</template></button>
                  <button v-if="canApprove" class="btn btn-sm btn-success" :disabled="busy || !explained(r) || r.unmatchedLines > 0" @click="confirmApprove = r">تایید</button>
                </template>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
      <p class="inv-muted small mb-0">تایید فقط با اختلاف صفر، یا اختلافی که جمع اقلام مغایرت (با علت) دقیقاً برابر آن باشد، و بدون ردیف تطبیق‌نشده ممکن است. برای اقلام «کارمزد یا سود ثبت‌نشده» و «خطا در دفاتر شرکت» باید سند اصلاحیِ قطعی وصل شود. تهیه‌کننده و ثبت‌کننده‌ی اقلام نمی‌توانند خودشان تایید کنند.</p>
    </div>

    <PromptModal v-if="confirmApprove" :fields="[{ key: 'note', label: 'توضیح', type: 'textarea' }]" title="تایید مغایرت‌گیری" @cancel="confirmApprove = null" @confirm="(v: any) => { const r = confirmApprove; confirmApprove = null; act(r, 'approve', v); }" />

    <div v-if="items" class="inv-modal-backdrop screen-only" @click.self="items = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">اقلام مغایرت {{ items.rec.bankAccountTitle }}</h2>
        <p class="inv-muted small">اختلاف محاسبه‌شده: <b class="fn-num">{{ money(items.rec.difference) }}</b> — جمع اقلام: <b class="fn-num">{{ money(itemsSum()) }}</b>. مبلغ مانند اختلاف علامت دارد (مثبت: بانک بیشتر از دفتر). سند اصلاحی برای نوع ۱ و ۴ الزامی است (شماره‌ی سند).</p>
        <table class="table table-sm">
          <thead><tr><th>نوع</th><th>مبلغ (ریال)</th><th>علت *</th><th>سند اصلاحی</th><th></th></tr></thead>
          <tbody>
            <tr v-for="(i, n) in items.rows" :key="n">
              <td><select v-model="i.kind" class="form-select form-select-sm"><option v-for="(t, k) in itemKinds" :key="k" :value="Number(k)">{{ t }}</option></select></td>
              <td><input v-model="i.amount" class="form-control form-control-sm fn-num" autocomplete="off" /></td>
              <td><input v-model="i.note" class="form-control form-control-sm" maxlength="500" /></td>
              <td><input v-model="i.correctingVoucherId" :disabled="i.kind !== 1 && i.kind !== 4" class="form-control form-control-sm fn-num" placeholder="شناسه‌ی سند" /></td>
              <td><button class="btn btn-sm btn-link text-danger" @click="items.rows.splice(n, 1)">حذف</button></td>
            </tr>
          </tbody>
        </table>
        <button class="btn btn-sm btn-outline-secondary" @click="items.rows.push({ kind: 9, amount: '', note: '', correctingVoucherId: '' })">قلم جدید</button>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="items = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveItems">ذخیره اقلام</button></div>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">مغایرت‌گیری جدید</h2>
        <label class="form-label small mb-0">حساب بانکی</label>
        <select v-model="form.bankAccountId" class="form-select form-select-sm mb-2" @change="loadStatements"><option :value="null">انتخاب...</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select>
        <label class="form-label small mb-0">صورت‌حساب *</label>
        <select v-model="form.statementId" class="form-select form-select-sm mb-2"><option :value="null">انتخاب...</option><option v-for="s in form.statements" :key="s.id" :value="s.id">{{ jalali(s.fromDate) }} تا {{ jalali(s.toDate) }}</option></select>
        <label class="form-label small mb-0">تا تاریخ (پیش‌فرض پایان صورت‌حساب)</label>
        <FnDate v-model="form.asOfDate" />
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="create">ایجاد</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
