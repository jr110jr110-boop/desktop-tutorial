<script setup lang="ts">
import { nextTick, onMounted, ref } from "vue";
import JsBarcode from "jsbarcode";
import QRCode from "qrcode";
import Logo from "@/assets/img/logo.png";
import { printSheet } from "@/composables/fn";
import { hoursText, inspectionRequirements, persianOf, qtyText } from "@/composables/production";

// Job card (برگه کار) of one operation: RTL, B Zar (the .ks-report print font), A5 or A4. The QR and the Code 128 barcode carry the job number (order number / sequence),
// which the shop-floor terminal scan accepts. The page size is set by a temporary @page rule while printing.
const props = defineProps<{ order: any; operation: any }>();
const emit = defineEmits(["close"]);
const size = ref<"A5" | "A4">("A4"); // company rule: printouts are A4 by default
const qr = ref("");
const barcode = ref<SVGElement | null>(null);
const code = props.operation.jobCard || `${props.order.number}/${props.operation.sequence}`;

onMounted(async () => {
  qr.value = await QRCode.toDataURL(code, { margin: 0, width: 240, errorCorrectionLevel: "M" });
  await nextTick();
  try {
    if (barcode.value) JsBarcode(barcode.value, code, { format: "CODE128", displayValue: false, height: 40, margin: 0, width: 1.6 });
  } catch {
    /* text the barcode cannot hold: the QR code is still printed */
  }
});

function print() {
  const style = document.createElement("style");
  style.textContent = `@page { size: ${size.value} portrait; margin: ${size.value === "A5" ? "8mm" : "12mm"}; }`;
  document.head.appendChild(style);
  const clear = () => style.remove();
  window.addEventListener("afterprint", clear, { once: true });
  setTimeout(clear, 60000);
  printSheet();
}
</script>

<template>
  <div class="inv-modal-backdrop screen-only" @click.self="emit('close')">
    <div class="inv-modal" style="max-width: 460px">
      <h2 class="inv-title mb-2">چاپ برگه کار - مرحله {{ operation.sequence }}</h2>
      <p class="mb-1">شماره برگه: <b class="inv-ltr">{{ code }}</b></p>
      <div class="d-flex gap-3 mb-2">
        <label class="form-check"><input v-model="size" class="form-check-input" type="radio" value="A4" /> <span class="form-check-label">A4</span></label>
        <label class="form-check"><input v-model="size" class="form-check-input" type="radio" value="A5" /> <span class="form-check-label">A5</span></label>
      </div>
      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">بستن</button><button class="btn btn-sm btn-primary" @click="print">چاپ</button></div>
    </div>
  </div>

  <div :class="['jc', 'jc-' + size]" class="print-form print-only ks-report">
    <div class="jc-head">
      <img :src="Logo" alt="" class="jc-logo" />
      <div class="jc-title">برگه کار</div>
      <img v-if="qr" :src="qr" alt="" class="jc-qr" />
    </div>
    <div class="jc-code"><svg ref="barcode" class="jc-barcode"></svg><div class="inv-ltr jc-codetext">{{ code }}</div></div>
    <table class="jc-table">
      <tbody>
        <tr><th>شماره دستور تولید</th><td class="inv-ltr">{{ order.number }}</td><th>دستور کار</th><td class="inv-ltr">{{ order.workOrderCode }}</td></tr>
        <tr><th>کالا</th><td colspan="3"><span class="inv-ltr">{{ order.itemCode }}</span> {{ order.itemTitle }}</td></tr>
        <tr><th>تعداد دستور</th><td>{{ qtyText(order.quantity) }} {{ order.unit }}</td><th>اولویت</th><td>{{ order.priority ?? "-" }}</td></tr>
        <tr><th>مرحله</th><td colspan="3">{{ operation.sequence }} - {{ operation.title }}<span v-if="operation.isRework"> (دوباره‌کاری)</span></td></tr>
        <tr><th>مرکز کار</th><td colspan="3"><span class="inv-ltr">{{ operation.workCenterCode }}</span> {{ operation.workCenterTitle }}</td></tr>
        <tr><th>زمان برنامه (ساعت)</th><td>راه‌اندازی {{ hoursText(operation.setupHoursPlanned) }} - اجرا {{ hoursText(operation.runHoursPlanned) }}</td><th>الزام بازرسی</th><td>{{ inspectionRequirements[operation.inspectionRequirement] }}</td></tr>
        <tr><th>شروع برنامه</th><td>{{ persianOf(operation.plannedStartText, operation.plannedStart) || "-" }}</td><th>پایان برنامه</th><td>{{ persianOf(operation.plannedFinishText, operation.plannedFinish) || "-" }}</td></tr>
        <tr><th>تاریخ نیاز</th><td colspan="3">{{ persianOf(order.needDateText, order.needDate) || "-" }}</td></tr>
        <tr v-if="operation.note"><th>توضیح</th><td colspan="3">{{ operation.note }}</td></tr>
      </tbody>
    </table>
    <table class="jc-table jc-entry">
      <thead><tr><th>تاریخ</th><th>از ساعت</th><th>تا ساعت</th><th>سالم</th><th>ضایعات</th><th>دوباره‌کاری</th></tr></thead>
      <tbody><tr v-for="n in size === 'A5' ? 3 : 6" :key="n"><td></td><td></td><td></td><td></td><td></td><td></td></tr></tbody>
    </table>
    <div class="jc-sign"><div>اپراتور</div><div>سرپرست کارگاه</div><div v-if="operation.inspectionRequirement >= 2">کنترل کیفیت</div></div>
  </div>
</template>

<style scoped>
.jc { direction: rtl; font-size: 11pt; }
.jc-head { display: flex; align-items: center; justify-content: space-between; border-bottom: 2px solid #000; padding-bottom: 4mm; margin-bottom: 3mm; }
.jc-logo { height: 14mm; }
.jc-title { font-size: 18pt; font-weight: bold; }
.jc-qr { width: 22mm; height: 22mm; }
.jc-code { text-align: center; margin-bottom: 3mm; }
.jc-barcode { height: 12mm; width: 70%; }
.jc-codetext { font-size: 13pt; font-weight: bold; letter-spacing: 1px; }
.jc-table { width: 100%; border-collapse: collapse; margin-bottom: 4mm; }
.jc-table th, .jc-table td { border: 1px solid #000; padding: 1.5mm 2mm; text-align: right; vertical-align: top; }
.jc-table th { background: #eee; width: 22%; white-space: nowrap; }
.jc-entry th { width: auto; text-align: center; }
.jc-entry td { height: 9mm; }
.jc-sign { display: flex; gap: 4mm; margin-top: 8mm; }
.jc-sign div { flex: 1; border-top: 1px solid #000; padding-top: 1.5mm; text-align: center; }
.jc-A4 { font-size: 12pt; }
</style>
