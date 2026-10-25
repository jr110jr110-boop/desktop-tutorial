<script setup lang="ts">
import { nextTick, ref } from "vue";
import JsBarcode from "jsbarcode";
import QRCode from "qrcode";
import PrintForm from "@/components/print/PrintForm.vue";
import { printSheet } from "@/composables/fn";
import { dispatchStages, persianOf, qtyText } from "@/composables/production";
import type { FormField } from "@/components/print/PrintForm.vue";

// A4 job cards (RTL, B Zar) of the listed operations, one card per page. The job number is printed as a QR code and a Code 128 barcode
// that the terminal scan box reads. Rendered only when printing (the print form hides the rest of the page, see PrintForm).
const props = defineProps({ rows: { type: Array as () => any[], default: () => [] }, workCenterTitle: { type: String, default: "" } });
const shown = ref<any[]>([]);
const qr = ref<Record<string, string>>({});
const bars = ref<Record<string, string>>({});

function fields(r: any): FormField[] {
  return [
    { label: "شماره دستور", value: r.orderNumber, ltr: true },
    { label: "شماره برگه کار", value: r.jobCard, ltr: true },
    { label: "مرحله", value: `${r.sequence} - ${r.title}` },
    { label: "مرکز کار", value: props.workCenterTitle },
    { label: "کالا", value: [r.itemCode, r.itemTitle].filter(Boolean).join(" - ") },
    { label: "تعداد", value: `${qtyText(r.quantity)} ${r.unit || ""}` },
    { label: "اولویت", value: r.priority ?? "" },
    { label: "نوبت", value: dispatchStages[r.stage] || r.stage },
    { label: "شروع برنامه", value: persianOf(r.plannedStartText, r.plannedStart), ltr: true },
    { label: "پایان برنامه", value: persianOf(r.plannedFinishText, r.plannedFinish), ltr: true },
    { label: "زمان راه‌اندازی (ساعت)", value: qtyText(r.setupHoursPlanned) },
    { label: "زمان تولید (ساعت)", value: qtyText(r.runHoursPlanned) },
    { label: "تاریخ نیاز", value: r.needDateText, ltr: true },
  ];
}

function barcodeOf(text: string): string {
  const canvas = document.createElement("canvas");
  try {
    JsBarcode(canvas, text, { format: "CODE128", displayValue: false, height: 50, margin: 0, width: 2 });
    return canvas.toDataURL("image/png");
  } catch {
    return "";
  }
}

/** Builds the codes of every card, then opens the print dialog. */
async function print() {
  shown.value = props.rows.slice();
  const q: Record<string, string> = {};
  const b: Record<string, string> = {};
  for (const r of shown.value) {
    const text = String(r.jobCard || r.orderNumber || "");
    q[r.operationId] = await QRCode.toDataURL(text, { margin: 0, width: 220, errorCorrectionLevel: "M" });
    b[r.operationId] = barcodeOf(text);
  }
  qr.value = q;
  bars.value = b;
  await nextTick();
  printSheet();
}
defineExpose({ print });
</script>

<template>
  <PrintForm v-for="r in shown" :key="r.operationId" :fields="fields(r)" :signatures="[{ title: 'اپراتور' }, { title: 'سرپرست کارگاه' }, { title: 'کنترل کیفیت' }]" class="job-card" distribution="توزیع نسخ: ۱- کارگاه ۲- برنامه‌ریزی تولید" title="کارت کار">
    <div class="jc-codes">
      <img v-if="qr[r.operationId]" :src="qr[r.operationId]" alt="" class="jc-qr" />
      <div class="jc-bar">
        <img v-if="bars[r.operationId]" :src="bars[r.operationId]" alt="" />
        <div class="inv-ltr jc-text">{{ r.jobCard }}</div>
      </div>
    </div>
    <table class="jc-log">
      <thead><tr><th>تاریخ</th><th>شروع</th><th>پایان</th><th>سالم</th><th>ضایعات</th><th>امضای اپراتور</th></tr></thead>
      <tbody><tr v-for="n in 8" :key="n"><td></td><td></td><td></td><td></td><td></td><td></td></tr></tbody>
    </table>
  </PrintForm>
</template>

<style lang="scss">
.job-card { font-family: "BZar", "B Zar", Tahoma, sans-serif; direction: rtl; }
.job-card + .job-card { break-before: page; page-break-before: always; }
.job-card .jc-codes { display: flex; justify-content: space-between; align-items: center; margin: 0.6rem 0 1rem; }
.job-card .jc-qr { width: 110px; height: 110px; }
.job-card .jc-bar { text-align: center; }
.job-card .jc-bar img { max-width: 360px; height: 60px; }
.job-card .jc-text { font-size: 1.1rem; letter-spacing: 0.1em; }
.job-card .jc-log { width: 100%; border-collapse: collapse; }
.job-card .jc-log th, .job-card .jc-log td { border: 1px solid #444; padding: 0.35rem; text-align: center; height: 1.9rem; }
</style>
