<script setup lang="ts">
import FnDate from "@/components/fn/FnDate.vue";
import { onMounted, ref } from "vue";
import Pb6Table from "@/components/common/Pb6Table.vue";
import { getPettyRequests, getPettySummaries, getPettySummary, pettyRequestAction, pettySummaryAction, previewPettySummary, savePettyRequest, savePettySummary } from "@/api/Pb6APIs";
import { fail, jalali, ok, printPage, rial, today } from "@/composables/inventory";

// Petty cash flow: top-up request -> accounting control -> finance manager -> treasury pays; the summary FO-AC-005-07 -> accounting -> finance manager -> CEO (final).
const tab = ref<"requests" | "summaries">("requests");
const rows = ref<any[]>([]);
const current = ref<any>(null);
const note = ref("");
const bankAccountId = ref<number | null>(null);
const requestStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال شد", 2: "کنترل حسابداری", 3: "تایید مدیر مالی", 4: "شارژ شد", 9: "رد شد" };
const summaryStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال شد", 2: "کنترل حسابداری", 3: "تایید مدیر مالی", 4: "تایید نهایی مدیرعامل", 5: "برگشت خورد", 9: "رد شد" };

const req = ref<any>({ pettyCashId: null, amount: 0, reason: "", submit: true });
const sum = ref<any>({ pettyCashId: null, periodFrom: today(), periodTo: today(), notes: "", autoFill: true, lines: [] });

async function load() {
  current.value = null;
  const [d, e] = tab.value === "requests" ? await getPettyRequests() : await getPettySummaries();
  if (e) return fail(e);
  rows.value = (d || []).map((r: any) => ({ ...r, statusText: (tab.value === "requests" ? requestStatuses : summaryStatuses)[r.status] }));
}
function switchTab(t: any) {
  tab.value = t;
  load();
}
async function open(r: any) {
  if (tab.value === "requests") {
    current.value = r;
    return;
  }
  const [d, e] = await getPettySummary(r.id);
  if (e) return fail(e);
  current.value = d;
}
async function createRequest() {
  const [, e] = await savePettyRequest(req.value);
  if (e) return fail(e);
  ok("درخواست شارژ ثبت شد.");
  load();
}
async function createSummary(preview: boolean) {
  const [d, e] = preview ? await previewPettySummary(sum.value) : await savePettySummary(sum.value);
  if (e) return fail(e);
  if (!preview) {
    ok("خلاصه تنخواه ذخیره شد.");
    await load();
  }
  current.value = d;
}
async function act(action: string, extra: any = {}) {
  const fn = tab.value === "requests" ? pettyRequestAction : pettySummaryAction;
  const [d, e] = await fn(current.value.id, action, { note: note.value || null, bankAccountId: bankAccountId.value, ...extra });
  if (e) return fail(e);
  ok("انجام شد.");
  await load();
  current.value = d;
}
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">گردش تنخواه</h1></div>
      <div class="btn-group mb-3 d-print-none">
        <button class="btn btn-sm" :class="tab === 'requests' ? 'btn-primary' : 'btn-outline-primary'" @click="switchTab('requests')">درخواست شارژ</button>
        <button class="btn btn-sm" :class="tab === 'summaries' ? 'btn-primary' : 'btn-outline-primary'" @click="switchTab('summaries')">خلاصه تنخواه (FO-AC-005-07)</button>
      </div>

      <Pb6Table v-if="tab === 'requests'" :columns="[{ key: 'number', title: 'شماره' }, { key: 'holderName', title: 'تنخواه‌دار' }, { key: 'amount', title: 'مبلغ (ریال)', kind: 'money' }, { key: 'balance', title: 'مانده', kind: 'money' }, { key: 'statusText', title: 'وضعیت' }]" :rows="rows" @select="open" />
      <Pb6Table v-else :columns="[{ key: 'number', title: 'شماره' }, { key: 'holderName', title: 'تنخواه‌دار' }, { key: 'periodFrom', title: 'از', kind: 'date' }, { key: 'periodTo', title: 'تا', kind: 'date' }, { key: 'totalAmount', title: 'جمع (ریال)', kind: 'money' }, { key: 'balance', title: 'مانده', kind: 'money' }, { key: 'statusText', title: 'وضعیت' }]" :rows="rows" @select="open" />

      <div v-if="tab === 'requests' && !current" class="inv-card mt-3 d-print-none">
        <h2 class="h6">درخواست شارژ جدید</h2>
        <div class="inv-filters">
          <input v-model.number="req.pettyCashId" type="number" class="form-control form-control-sm inv-ltr" placeholder="شناسه تنخواه" />
          <input v-model.number="req.amount" type="number" class="form-control form-control-sm inv-ltr" placeholder="مبلغ (ریال)" />
          <input v-model="req.reason" class="form-control form-control-sm" placeholder="علت" />
          <button class="btn btn-sm btn-primary" @click="createRequest">ثبت و ارسال</button>
        </div>
      </div>
      <div v-if="tab === 'summaries' && !current" class="inv-card mt-3 d-print-none">
        <h2 class="h6">خلاصه تنخواه جدید (پر شدن خودکار از ردیف‌های تسویه)</h2>
        <div class="inv-filters">
          <input v-model.number="sum.pettyCashId" type="number" class="form-control form-control-sm inv-ltr" placeholder="شناسه تنخواه" />
          <FnDate v-model="sum.periodFrom" />
          <FnDate v-model="sum.periodTo" />
          <button class="btn btn-sm btn-outline-secondary" @click="createSummary(true)">پیش‌نمایش</button>
          <button class="btn btn-sm btn-primary" @click="createSummary(false)">ذخیره</button>
        </div>
      </div>

      <div v-if="current" class="inv-card mt-3">
        <div class="d-flex justify-content-between d-print-none"><h2 class="h6">{{ current.number || "پیش‌نمایش" }}</h2><button class="btn btn-sm btn-outline-secondary" @click="current = null">بستن</button></div>
        <template v-if="tab === 'requests'">
          <p>مبلغ {{ rial(current.amount) }} ریال — سقف {{ rial(current.ceiling) }} — مانده {{ rial(current.balance) }} — {{ requestStatuses[current.status] }}</p>
          <p v-if="current.rejectReason" class="text-danger">{{ current.rejectReason }}</p>
          <div class="d-print-none d-flex gap-2 flex-wrap">
            <input v-model="note" class="form-control form-control-sm w-auto" placeholder="علت (برای رد)" />
            <input v-model.number="bankAccountId" type="number" class="form-control form-control-sm w-auto inv-ltr" placeholder="شناسه حساب بانکی (پرداخت)" />
            <button v-if="current.status === 0" class="btn btn-sm btn-primary" @click="act('submit')">ارسال</button>
            <button v-if="current.status === 1" class="btn btn-sm btn-success" @click="act('control')">کنترل حسابداری</button>
            <button v-if="current.status === 2" class="btn btn-sm btn-success" @click="act('approve')">تایید مدیر مالی</button>
            <button v-if="current.status === 3" class="btn btn-sm btn-primary" @click="act('create-payment')">ثبت سند پرداخت</button>
            <button v-if="current.status === 3" class="btn btn-sm btn-success" @click="act('confirm-topup')">تایید شارژ</button>
            <button v-if="current.status >= 1 && current.status <= 3" class="btn btn-sm btn-danger" @click="act('reject')">رد</button>
          </div>
        </template>
        <template v-else>
          <div class="pb6-sheet" dir="rtl">
            <p>دوره {{ jalali(current.periodFrom) }} تا {{ jalali(current.periodTo) }} — مانده قبلی {{ rial(current.previousBalance) }} — شارژ {{ rial(current.charge) }} — جمع هزینه {{ rial(current.totalAmount) }} ({{ current.totalInWords }}) — مانده {{ rial(current.balance) }}</p>
            <ul v-if="current.problems?.length" class="text-warning small"><li v-for="p in current.problems" :key="p">{{ p }}</li></ul>
            <table class="table table-sm table-bordered"><thead><tr><th>#</th><th>تاریخ</th><th>شماره فاکتور</th><th>شماره درخواست خرید / رسید</th><th>شرح</th><th>مبلغ</th><th>مدارک</th><th>کمبود</th><th></th></tr></thead><tbody>
              <tr v-for="l in current.lines" :key="l.id ?? l.rowNumber"><td>{{ l.rowNumber }}</td><td>{{ jalali(l.date) }}</td><td>{{ l.invoiceNumber || "*" }}</td><td>{{ l.prApprovalNumber || "*" }} / {{ l.warehouseReceiptNumber || "*" }}</td><td>{{ l.description }}</td><td class="num">{{ rial(l.amount) }}</td><td>{{ l.attachmentCount }}</td><td class="text-danger small">{{ (l.missing || []).join("، ") }}</td>
                <td class="d-print-none"><button v-if="l.misc && !l.misc.signed && current.status < 4" class="btn btn-sm btn-outline-secondary" @click="act('sign-misc', { lineId: l.id })">امضای فرم متفرقه</button></td></tr>
            </tbody></table>
            <p class="small">تنخواه‌دار: {{ current.holderName }} | کنترل: {{ current.controlledByName }} | مدیر مالی: {{ current.approvedByName }} | مدیرعامل: {{ current.finalByName }}</p>
          </div>
          <div class="d-print-none d-flex gap-2 flex-wrap" v-if="current.id">
            <input v-model="note" class="form-control form-control-sm w-auto" placeholder="علت (برای برگشت/رد)" />
            <button v-if="current.status === 0 || current.status === 5" class="btn btn-sm btn-primary" @click="act('submit')">امضا و ارسال</button>
            <button v-if="current.status === 1" class="btn btn-sm btn-success" @click="act('control')">کنترل حسابداری</button>
            <button v-if="current.status === 2" class="btn btn-sm btn-success" @click="act('approve')">تایید مدیر مالی</button>
            <button v-if="current.status === 3" class="btn btn-sm btn-success" @click="act('final')">تایید نهایی (مدیرعامل)</button>
            <button v-if="current.status >= 1 && current.status <= 3" class="btn btn-sm btn-warning" @click="act('return')">برگشت</button>
            <button v-if="current.status >= 1 && current.status <= 3" class="btn btn-sm btn-danger" @click="act('reject')">رد</button>
            <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          </div>
        </template>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style>
.pb6-sheet { font-family: "B Zar", "BZar", Tahoma, sans-serif; }
@media print { @page { size: A4; margin: 12mm; } }
</style>
