<script setup lang="ts">
import { nextTick, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import ChequeRegisterPrint from "@/components/fn/print/ChequeRegisterPrint.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import QPager from "@/components/quality/QPager.vue";
import { getBankAccounts, getChequeBooks, getIssuedCheques, getReceivedCheques, issuedChequeAction, receivedChequeAction, saveChequeBook } from "@/api/FnAPIs";
import { getSuppliers } from "@/api/ProcurementAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { issuedChequeStatuses, money, parseAmount, printSheet, receivedChequeStatuses, usePagedList } from "@/composables/fn";
import { FnRoles, hasRole } from "@/composables/roles";

// Received cheques (in box -> deposited -> cleared / bounced / returned / endorsed), cheque books and issued cheques.
const tab = ref("received");
const tabs = [{ key: "received", title: "چک‌های دریافتی" }, { key: "issued", title: "چک‌های صادرشده" }, { key: "books", title: "دسته‌چک‌ها" }];
const rFilters = ref({ Kind: null as number | null, CustomerId: null, From: null as string | null, To: null as string | null, Contains: "" });
const iFilters = ref({ Status: null as number | null, BankAccountId: null as number | null });
const bFilters = ref({ BankAccountId: null as number | null });
const received = usePagedList(getReceivedCheques, rFilters);
const issued = usePagedList(getIssuedCheques, iFilters);
const books = usePagedList(getChequeBooks, bFilters);
const banks = ref<any[]>([]);
const suppliers = ref<any[]>([]);
const history = ref<any>(null);
const dialog = ref<null | { target: any; action: string; kind: "received" | "issued"; title: string; fields: any[] }>(null);
const bookForm = ref<any>(null);
const printKind = ref<"" | "received" | "issued">("");
const printRows = ref<any[]>([]);
const busy = ref(false);
const canWrite = hasRole(FnRoles.treasury);

function ask(c: any, action: string, kind: "received" | "issued") {
  const bankOptions = banks.value.map((b) => ({ value: b.id, text: b.title }));
  const dateField = { key: "date", label: "تاریخ عملیات", type: "date", value: today() };
  const noteField = (required: boolean) => ({ key: "note", label: required ? "علت *" : "توضیح", type: "textarea", required });
  const map: Record<string, { title: string; fields: any[] }> = {
    "received:deposit": { title: "واگذاری چک به بانک", fields: [{ key: "bankAccountId", label: "حساب بانکی وصول", type: "select", options: bankOptions, required: true }, dateField, noteField(false)] },
    "received:clear": { title: "وصول چک", fields: [dateField, noteField(false)] },
    "received:bounce": { title: "برگشت چک", fields: [dateField, noteField(true)] },
    "received:return": { title: "عودت چک به صادرکننده", fields: [dateField, noteField(false)] },
    "received:endorse": { title: "خرج چک", fields: [{ key: "providerId", label: "تامین‌کننده گیرنده چک", type: "select", options: suppliers.value.map((s) => ({ value: s.providerId, text: s.providerName })), required: true }, dateField, noteField(false)] },
    "issued:clear": { title: "پاس‌شدن چک صادرشده", fields: [dateField] },
    "issued:cancel": { title: "ابطال چک صادرشده", fields: [noteField(true)] },
    "issued:void": { title: "خراب‌شدن برگ سفید", fields: [noteField(true)] },
  };
  const m = map[`${kind}:${action}`];
  dialog.value = { target: c, action, kind, title: `${m.title} - ${kind === "received" ? c.serial : c.serial}`, fields: m.fields };
}

async function confirm(v: any) {
  const d = dialog.value!;
  dialog.value = null;
  busy.value = true;
  const dto = { id: d.target.id, action: d.action, note: v.note, date: v.date, bankAccountId: v.bankAccountId, providerId: v.providerId };
  const [, error] = d.kind === "received" ? await receivedChequeAction(dto) : await issuedChequeAction(dto);
  busy.value = false;
  if (error) return fail(error);
  ok("انجام شد.");
  (d.kind === "received" ? received : issued).load();
}

async function saveBook() {
  const f = bookForm.value;
  const from = parseAmount(f.fromSerial), to = parseAmount(f.toSerial);
  if (!f.bankAccountId || !from || !to) return fail("حساب بانکی و بازه سریال را وارد کنید.");
  busy.value = true;
  const [, error] = await saveChequeBook({ bankAccountId: f.bankAccountId, series: f.series || "", fromSerial: from, toSerial: to });
  busy.value = false;
  if (error) return fail(error);
  ok("دسته‌چک ثبت شد.");
  bookForm.value = null;
  books.load();
}

async function printRegister(kind: "received" | "issued") {
  const fetcher = kind === "received" ? getReceivedCheques : getIssuedCheques;
  const [data, error] = await fetcher({ ...(kind === "received" ? rFilters.value : iFilters.value), PageSize: 200, PageNumber: 1 });
  if (error) return fail(error);
  printRows.value = Array.isArray(data) ? data : data?.data || [];
  printKind.value = kind;
  await nextTick();
  printSheet();
}

const badgeOfReceived = (s: number) => (s === 3 ? "ok" : s === 4 ? "bad" : s === 1 ? "info" : "warn");

onMounted(async () => {
  const [b] = await getBankAccounts({ PageSize: 200 });
  banks.value = (b?.data || []).filter((x: any) => x.isActive);
  const [s] = await getSuppliers();
  suppliers.value = Array.isArray(s) ? s : s?.data || [];
  received.load();
  issued.load();
  books.load();
});
</script>

<template>
  <div class="inv-page">
    <ChequeRegisterPrint v-if="printKind" :kind="printKind" :rows="printRows" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">چک‌ها</h1>
        <div class="d-flex gap-2">
          <button v-if="tab !== 'books'" class="btn btn-sm btn-outline-secondary" @click="printRegister(tab === 'received' ? 'received' : 'issued')">چاپ دفتر ثبت چک</button>
          <button v-if="tab === 'books' && canWrite" class="btn btn-sm btn-primary" @click="bookForm = { bankAccountId: null, series: '', fromSerial: '', toSerial: '' }">دسته‌چک جدید</button>
        </div>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'received'">
        <div class="inv-filters mb-3">
          <select v-model="rFilters.Kind" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in receivedChequeStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
          <div><label class="form-label small mb-0">سررسید از</label><FnDate v-model="rFilters.From" /></div>
          <div><label class="form-label small mb-0">سررسید تا</label><FnDate v-model="rFilters.To" /></div>
          <input v-model="rFilters.Contains" class="form-control form-control-sm" placeholder="صیادی، سریال یا صاحب حساب" @keydown.enter="received.search" />
          <button class="btn btn-sm btn-secondary" @click="received.search">جستجو</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>صیادی</th><th>سریال</th><th>بانک</th><th>صاحب حساب</th><th>مشتری</th><th>سررسید</th><th>مبلغ (ریال)</th><th>وضعیت</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!received.rows.value.length"><td colspan="9" class="text-center inv-muted">چکی نیست.</td></tr>
              <tr v-for="c in received.rows.value" :key="c.id">
                <td class="inv-ltr">{{ c.sayadId }}</td><td class="inv-ltr">{{ c.serial }}</td><td>{{ c.bankName }} {{ c.branch }}</td><td>{{ c.accountHolder }}</td><td>{{ c.customerName }}</td>
                <td>{{ jalali(c.dueDate) }}</td><td class="fn-num">{{ money(c.amount) }}</td>
                <td><span :class="badgeOfReceived(c.status)" class="inv-badge">{{ receivedChequeStatuses[c.status] }}</span></td>
                <td class="text-nowrap">
                  <button class="btn btn-sm btn-link p-0 me-2" @click="history = c">سابقه</button>
                  <template v-if="canWrite">
                    <button v-if="c.status === 1" class="btn btn-sm btn-outline-primary me-1" @click="ask(c, 'deposit', 'received')">واگذاری به بانک</button>
                    <button v-if="c.status === 1" class="btn btn-sm btn-outline-secondary me-1" @click="ask(c, 'endorse', 'received')">خرج</button>
                    <button v-if="c.status === 2" class="btn btn-sm btn-success me-1" @click="ask(c, 'clear', 'received')">وصول</button>
                    <button v-if="c.status === 2" class="btn btn-sm btn-outline-danger me-1" @click="ask(c, 'bounce', 'received')">برگشت</button>
                    <button v-if="c.status === 1 || c.status === 4" class="btn btn-sm btn-outline-secondary" @click="ask(c, 'return', 'received')">عودت</button>
                  </template>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="received.page.value" :total="received.total.value" @go="received.go" />
      </template>

      <template v-else-if="tab === 'issued'">
        <div class="inv-filters mb-3">
          <select v-model="iFilters.BankAccountId" class="form-select form-select-sm"><option :value="null">همه حساب‌ها</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select>
          <select v-model="iFilters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in issuedChequeStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
          <button class="btn btn-sm btn-secondary" @click="issued.search">جستجو</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>حساب بانکی</th><th>سری</th><th>سریال</th><th>صیادی</th><th>در وجه</th><th>سررسید</th><th>مبلغ (ریال)</th><th>وضعیت</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!issued.rows.value.length"><td colspan="9" class="text-center inv-muted">برگی نیست.</td></tr>
              <tr v-for="c in issued.rows.value" :key="c.id">
                <td>{{ c.bankAccountTitle }}</td><td class="inv-ltr">{{ c.series }}</td><td class="inv-ltr">{{ c.serial }}</td><td class="inv-ltr">{{ c.sayadId }}</td><td>{{ c.payeeName }}</td>
                <td>{{ c.dueDate ? jalali(c.dueDate) : "" }}</td><td class="fn-num">{{ c.amount ? money(c.amount) : "" }}</td>
                <td><span :class="c.status === 2 ? 'ok' : c.status === 1 ? 'info' : c.status >= 3 ? 'bad' : ''" class="inv-badge">{{ issuedChequeStatuses[c.status] }}</span><div v-if="c.voidReason" class="inv-muted">{{ c.voidReason }}</div></td>
                <td v-if="canWrite" class="text-nowrap">
                  <button v-if="c.status === 1" class="btn btn-sm btn-success me-1" @click="ask(c, 'clear', 'issued')">پاس شد</button>
                  <button v-if="c.status === 1" class="btn btn-sm btn-outline-danger me-1" @click="ask(c, 'cancel', 'issued')">ابطال</button>
                  <button v-if="c.status === 0" class="btn btn-sm btn-outline-secondary" @click="ask(c, 'void', 'issued')">خراب</button>
                </td>
                <td v-else></td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="issued.page.value" :total="issued.total.value" @go="issued.go" />
      </template>

      <template v-else>
        <div class="inv-filters mb-3">
          <select v-model="bFilters.BankAccountId" class="form-select form-select-sm" @change="books.search"><option :value="null">همه حساب‌ها</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select>
        </div>
        <table class="table table-sm table-hover">
          <thead><tr><th>حساب بانکی</th><th>سری</th><th>از سریال</th><th>تا سریال</th><th>تعداد برگ</th><th>برگ سفید</th><th>فعال</th></tr></thead>
          <tbody>
            <tr v-if="!books.rows.value.length"><td colspan="7" class="text-center inv-muted">دسته‌چکی نیست.</td></tr>
            <tr v-for="b in books.rows.value" :key="b.id">
              <td>{{ b.bankAccountTitle }}</td><td class="inv-ltr">{{ b.series }}</td><td class="fn-num">{{ b.fromSerial }}</td><td class="fn-num">{{ b.toSerial }}</td><td class="fn-num">{{ b.leaves }}</td><td class="fn-num">{{ b.blank }}</td><td>{{ b.isActive ? "بله" : "خیر" }}</td>
            </tr>
          </tbody>
        </table>
        <QPager :page="books.page.value" :total="books.total.value" @go="books.go" />
      </template>
    </div>

    <PromptModal v-if="dialog" :fields="dialog.fields" :title="dialog.title" @cancel="dialog = null" @confirm="confirm" />

    <div v-if="history" class="inv-modal-backdrop screen-only" @click.self="history = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">سابقه چک <span class="inv-ltr">{{ history.serial }}</span></h2>
        <table class="table table-sm">
          <thead><tr><th>تاریخ</th><th>از</th><th>به</th><th>توضیح</th><th>سند</th></tr></thead>
          <tbody>
            <tr v-for="(h, i) in history.history" :key="i">
              <td>{{ jalali(h.date) }}</td><td>{{ h.fromStatus ? receivedChequeStatuses[h.fromStatus] : "-" }}</td><td>{{ receivedChequeStatuses[h.toStatus] }}</td><td>{{ h.note }}</td>
              <td><router-link v-if="h.voucherId" :to="{ name: 'FnVoucherEdit', params: { id: h.voucherId } }">{{ h.voucherNumber ?? "پیش‌نویس" }}</router-link></td>
            </tr>
          </tbody>
        </table>
        <div class="text-end"><button class="btn btn-sm btn-outline-secondary" @click="history = null">بستن</button></div>
      </div>
    </div>

    <div v-if="bookForm" class="inv-modal-backdrop screen-only" @click.self="bookForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">دسته‌چک جدید</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">حساب بانکی *</label><select v-model="bookForm.bankAccountId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select></div>
          <div class="col-4"><label class="form-label small mb-0">سری</label><input v-model="bookForm.series" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-4"><label class="form-label small mb-0">از سریال *</label><input v-model="bookForm.fromSerial" class="form-control form-control-sm fn-num" /></div>
          <div class="col-4"><label class="form-label small mb-0">تا سریال *</label><input v-model="bookForm.toSerial" class="form-control form-control-sm fn-num" /></div>
        </div>
        <p class="inv-muted mt-2 mb-0">برای هر شماره بازه یک برگ سفید ساخته می‌شود (حداکثر ۵۰۰ برگ).</p>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="bookForm = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="saveBook">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
