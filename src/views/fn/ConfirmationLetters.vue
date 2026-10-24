<script setup lang="ts">
import { nextTick, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import ConfirmationPrint from "@/components/fn/print/ConfirmationPrint.vue";
import { getConfirmations, getCustomers, issueConfirmation, respondConfirmation } from "@/api/FnAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { money, parseAmount, printSheet, yesNo } from "@/composables/fn";
import { confirmationStatuses } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Customer balance confirmation letters: issue (the ledger balance and statement are frozen), print for the customer, then record the answer (confirmed / disputed).
const canWrite = hasRole(FnRoles.accountants);
const busy = ref(false);
const printing = ref(false);
const customers = ref<any[]>([]);
const customerId = ref<number | null>(null);
const list = ref<any[]>([]);
const detail = ref<any>(null);
const issue = ref<any>(null);
const respond = ref<null | "confirm" | "dispute">(null);

async function load() {
  const [d, error] = await getConfirmations(customerId.value);
  if (error) return fail(error);
  list.value = d || [];
}
async function doIssue() {
  if (!issue.value.customerId || !issue.value.asOf) return fail("مشتری و تاریخ را وارد کنید.");
  busy.value = true;
  const [d, error] = await issueConfirmation(issue.value.customerId, issue.value.asOf);
  busy.value = false;
  if (error) return fail(error);
  ok("نامه صادر شد.");
  issue.value = null;
  detail.value = d;
  load();
}
async function doRespond(v: any) {
  const action = respond.value!;
  respond.value = null;
  busy.value = true;
  const [d, error] = await respondConfirmation({ id: detail.value.id, action, amount: action === "dispute" ? parseAmount(v.amount) : null, date: v.date || today(), note: v.note || null });
  busy.value = false;
  if (error) return fail(error);
  ok("پاسخ مشتری ثبت شد.");
  detail.value = d;
  load();
}
async function print() {
  printing.value = true;
  await nextTick();
  printSheet();
}

onMounted(async () => {
  load();
  const [c] = await getCustomers({ PageSize: 300 });
  customers.value = Array.isArray(c) ? c : c?.data || [];
});
</script>

<template>
  <div class="inv-page">
    <ConfirmationPrint v-if="printing && detail" :letter="detail" />
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">نامه‌های تایید مانده مشتریان</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="issue = { customerId: customerId, asOf: today() }">صدور نامه</button>
      </div>
      <div class="inv-filters mb-3"><select v-model="customerId" class="form-select form-select-sm" @change="load"><option :value="null">همه مشتریان</option><option v-for="c in customers" :key="c.id" :value="c.id">{{ c.name }}</option></select></div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>مشتری</th><th>تاریخ مانده</th><th>مانده دفتر</th><th>مانده صورت‌حساب</th><th>همخوان</th><th>اسناد باز</th><th>وضعیت</th><th>مبلغ تاییدشده</th></tr></thead>
        <tbody>
          <tr v-if="!list.length"><td colspan="9" class="text-center inv-muted">نامه‌ای صادر نشده است.</td></tr>
          <tr v-for="c in list" :key="c.id" style="cursor: pointer" @click="detail = c">
            <td class="inv-ltr">{{ c.number }}</td><td>{{ c.customerName }}</td><td>{{ jalali(c.asOf) }}</td><td class="fn-num">{{ money(c.ledgerBalance) }}</td><td class="fn-num">{{ money(c.statementBalance) }}</td><td>{{ yesNo(c.agrees) }}</td><td>{{ c.openDocuments }}</td><td>{{ confirmationStatuses[c.status] }}</td><td class="fn-num">{{ c.confirmedAmount === null || c.confirmedAmount === undefined ? "" : money(c.confirmedAmount) }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2"><span class="inv-ltr">{{ detail.number }}</span> - {{ detail.customerName }} <span class="inv-badge">{{ confirmationStatuses[detail.status] }}</span></h2>
        <p>مانده طبق دفاتر در {{ jalali(detail.asOf) }}: <b>{{ money(detail.ledgerBalance) }}</b> ریال - صورت‌حساب: {{ money(detail.statementBalance) }} - اسناد باز: {{ detail.openDocuments }}
          <span v-if="detail.responseDate"><br />پاسخ در {{ jalali(detail.responseDate) }}: {{ detail.confirmedAmount === null || detail.confirmedAmount === undefined ? "" : money(detail.confirmedAmount) + " ریال - " }}{{ detail.responseNote }}</span></p>
        <div class="table-responsive" style="max-height: 280px; overflow: auto">
          <table class="table table-sm">
            <thead><tr><th>تاریخ</th><th>نوع</th><th>شماره</th><th>شرح</th><th>بدهکار</th><th>بستانکار</th><th>مانده</th></tr></thead>
            <tbody><tr v-for="(r, i) in detail.rows" :key="i"><td>{{ jalali(r.date) }}</td><td>{{ r.kind }}</td><td class="inv-ltr">{{ r.number }}</td><td>{{ r.description }}</td><td class="fn-num">{{ money(r.debit) }}</td><td class="fn-num">{{ money(r.credit) }}</td><td class="fn-num">{{ money(r.balance) }}</td></tr></tbody>
          </table>
        </div>
        <div class="d-flex gap-2 justify-content-end flex-wrap mt-2">
          <button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button>
          <button class="btn btn-sm btn-outline-secondary" @click="print">چاپ نامه</button>
          <template v-if="canWrite && detail.status === 1">
            <button :disabled="busy" class="btn btn-sm btn-success" @click="respond = 'confirm'">ثبت تایید مشتری</button>
            <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="respond = 'dispute'">ثبت مغایرت</button>
          </template>
        </div>
      </div>
    </div>

    <div v-if="issue" class="inv-modal-backdrop screen-only" @click.self="issue = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">صدور نامه تایید مانده</h2>
        <div class="row g-2">
          <div class="col-md-7"><label class="form-label small mb-0">مشتری *</label><select v-model="issue.customerId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="c in customers" :key="c.id" :value="c.id">{{ c.name }}</option></select></div>
          <div class="col-md-5"><label class="form-label small mb-0">مانده تا تاریخ *</label><FnDate v-model="issue.asOf" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="issue = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="doIssue">صدور</button></div>
      </div>
    </div>

    <PromptModal
      v-if="respond"
      :title="respond === 'confirm' ? 'ثبت تایید مشتری' : 'ثبت مغایرت اعلامی مشتری'"
      :fields="[...(respond === 'dispute' ? [{ key: 'amount', label: 'مانده طبق دفاتر مشتری (ریال)', type: 'number' as const }] : []), { key: 'date', label: 'تاریخ پاسخ', type: 'date' as const, value: today() }, { key: 'note', label: 'توضیح', type: 'textarea' as const, required: respond === 'dispute' }]"
      @confirm="doRespond"
      @cancel="respond = null" />
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
