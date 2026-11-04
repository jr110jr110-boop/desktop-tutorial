<script setup lang="ts">
import { onMounted, ref } from "vue";
import { confirmTermCondition, getContractPrint, getPaymentTerms, requestTermPayment, savePaymentTerms } from "@/api/Pb6APIs";
import { fail, jalali, ok, printPage, rial } from "@/composables/inventory";

// PB6: the payment terms of a supplier contract (template 30/30/30/10) that drive the payment requests, and the contract print FO-IP-040-04 (A4, RTL, B Zar).
const props = defineProps<{ orderId: number }>();
const terms = ref<any[]>([]);
const print = ref<any>(null);
const invoiceId = ref<number | null>(null);
const kinds: Record<number, string> = { 1: "پیش‌پرداخت", 2: "پس از تحویل و تایید QC", 3: "چک پس از فاکتور", 4: "حسن انجام کار" };
const states: Record<number, string> = { 0: "در انتظار", 1: "درخواست پرداخت ثبت شد", 2: "پرداخت شد" };

async function load() {
  const [d, e] = await getPaymentTerms(props.orderId);
  if (e) return fail(e);
  terms.value = d || [];
}
async function useTemplate() {
  const [d, e] = await savePaymentTerms({ purchaseOrderId: props.orderId, useTemplate: true, terms: [] });
  if (e) return fail(e);
  terms.value = d || [];
  ok("الگوی ۳۰/۳۰/۳۰/۱۰ اعمال شد.");
}
async function confirm(t: any) {
  const [d, e] = await confirmTermCondition(t.id);
  if (e) return fail(e);
  terms.value = d || terms.value;
  ok("تایید شد.");
}
async function pay(t: any) {
  const [, e] = await requestTermPayment(t.id, invoiceId.value);
  if (e) return fail(e);
  ok("درخواست پرداخت ثبت شد.");
  load();
}
async function showPrint() {
  const [d, e] = await getContractPrint(props.orderId);
  if (e) return fail(e);
  print.value = d;
}
onMounted(load);
</script>

<template>
  <div class="inv-card screen-only mt-3">
    <div class="inv-toolbar">
      <h2 class="inv-title">شرایط پرداخت قرارداد و چاپ</h2>
      <div class="d-flex gap-2">
        <button class="btn btn-sm btn-outline-secondary" @click="useTemplate">اعمال الگوی ۳۰/۳۰/۳۰/۱۰</button>
        <button class="btn btn-sm btn-outline-primary" @click="showPrint">پیش‌نمایش قرارداد (FO-IP-040-04)</button>
      </div>
    </div>
    <table class="table table-sm">
      <thead><tr><th>#</th><th>مرحله</th><th>درصد</th><th>مبلغ (ریال)</th><th>سررسید</th><th>شرط</th><th>وضعیت</th><th></th></tr></thead>
      <tbody>
        <tr v-if="!terms.length"><td colspan="8" class="text-center inv-muted">شرایط پرداختی ثبت نشده است.</td></tr>
        <tr v-for="t in terms" :key="t.id">
          <td>{{ t.rowNumber }}</td><td>{{ kinds[t.kind] }}</td><td>{{ t.percent }}</td><td class="num">{{ rial(t.amount) }}</td><td>{{ jalali(t.dueDate) }}</td>
          <td class="small">{{ t.condition }} <span v-if="t.conditionConfirmedByName" class="inv-badge ok">تایید: {{ t.conditionConfirmedByName }}</span></td>
          <td>{{ states[t.status] }} <span v-if="t.paymentRequestNumber" class="inv-ltr">{{ t.paymentRequestNumber }}</span></td>
          <td class="d-flex gap-1">
            <button v-if="t.status === 0 && !t.conditionConfirmedAt && (t.kind === 2 || t.kind === 4)" class="btn btn-sm btn-outline-success" @click="confirm(t)">تایید شرط</button>
            <button v-if="t.status === 0 && t.eligible" class="btn btn-sm btn-primary" @click="pay(t)">درخواست پرداخت</button>
            <span v-else-if="t.status === 0" class="small inv-muted">{{ t.eligibleNote }}</span>
          </td>
        </tr>
      </tbody>
    </table>
    <input v-model.number="invoiceId" type="number" class="form-control form-control-sm w-auto inv-ltr" placeholder="شناسه فاکتور تاییدشده (برای چک پس از فاکتور)" />
  </div>

  <div v-if="print" class="contract-sheet" dir="rtl">
    <h2 class="text-center">{{ print.templateTitle }} <small class="inv-ltr">{{ print.templateCode }}</small></h2>
    <p>شماره سفارش: <span class="inv-ltr">{{ print.orderNumber }}</span> — شماره قرارداد: <span class="inv-ltr">{{ print.contractNumber }}</span> — تاریخ: {{ jalali(print.contractDate) }}</p>
    <p>خریدار: {{ print.buyerName }} — تامین‌کننده: {{ print.supplierName }} (شناسه ملی {{ print.supplierNationalId }}، کد اقتصادی {{ print.supplierEconomicCode }})</p>
    <p>موضوع: {{ print.subject }} — مبلغ کل: {{ rial(print.totalAmount) }} ریال ({{ print.totalInWords }}) — مالیات بر ارزش افزوده {{ print.vatPercent }}٪</p>
    <table class="table table-sm table-bordered"><thead><tr><th>ردیف</th><th>شرح</th><th>واحد</th><th>مقدار</th><th>فی</th><th>مبلغ</th></tr></thead><tbody>
      <tr v-for="l in print.lines" :key="l.row"><td>{{ l.row }}</td><td>{{ l.description }}</td><td>{{ l.unit }}</td><td>{{ l.quantity }}</td><td class="num">{{ rial(l.unitPrice) }}</td><td class="num">{{ rial(l.amount) }}</td></tr>
    </tbody></table>
    <table class="table table-sm table-bordered"><thead><tr><th>ردیف</th><th>مرحله پرداخت</th><th>درصد</th><th>مبلغ</th><th>شرط</th></tr></thead><tbody>
      <tr v-for="t in print.paymentTerms" :key="t.row"><td>{{ t.row }}</td><td>{{ t.title }}</td><td>{{ t.percent }}</td><td class="num">{{ rial(t.amount) }}</td><td>{{ t.condition }}</td></tr>
    </tbody></table>
    <div v-for="c in print.clauses" :key="c.number" class="mb-1"><strong>{{ c.number }}. {{ c.title }}:</strong> {{ c.text }}</div>
    <div class="row mt-4"><div class="col">امضای خریدار</div><div class="col">امضای تامین‌کننده</div></div>
    <button class="btn btn-sm btn-outline-secondary d-print-none mt-2" @click="printPage">چاپ</button>
  </div>
</template>

<style>
.contract-sheet { font-family: "B Zar", "BZar", Tahoma, sans-serif; background: #fff; padding: 12mm; margin-top: 12px; }
@media print { @page { size: A4; margin: 12mm; } .screen-only { display: none; } }
</style>
