<script setup lang="ts">
// Printable labels: QR code + Code 128 barcode of the same text, for label printers or A4 sheets.
import { nextTick, onMounted, ref } from "vue";
import QRCode from "qrcode";
import JsBarcode from "jsbarcode";

interface Label {
  code: string;
  title: string;
  lines: string[];
}

const labels = ref<Label[]>([]);
const size = ref<"small" | "large">("small");
const copies = ref(1);
const qr = ref<Record<number, string>>({});
const barcodes = ref<any[]>([]);

async function render() {
  const list: Label[] = [];
  labels.value.forEach((l) => {
    for (let i = 0; i < copies.value; i++) list.push(l);
  });
  shown.value = list;
  const images: Record<number, string> = {};
  for (let i = 0; i < list.length; i++) {
    images[i] = await QRCode.toDataURL(list[i].code, { margin: 0, width: 240, errorCorrectionLevel: "M" });
  }
  qr.value = images;
  await nextTick();
  barcodes.value.forEach((el: SVGElement, i: number) => {
    if (!el || !list[i]) return;
    try {
      JsBarcode(el, list[i].code, { format: "CODE128", displayValue: false, height: 40, margin: 0, width: 1.4 });
    } catch {
      /* text the barcode cannot hold: the QR code is still printed */
    }
  });
}

const shown = ref<Label[]>([]);

function print() {
  window.print();
}

onMounted(() => {
  try {
    labels.value = JSON.parse(localStorage.getItem("inventoryLabels") || "[]");
  } catch {
    labels.value = [];
  }
  render();
});
</script>

<template>
  <div class="labels-page ks-report">
    <div class="toolbar d-print-none">
      <b>چاپ برچسب ({{ labels.length }} برچسب)</b>
      <label>اندازه:
        <select v-model="size">
          <option value="small">کوچک 50×30 میلی‌متر</option>
          <option value="large">بزرگ 100×50 میلی‌متر</option>
        </select>
      </label>
      <label>تعداد نسخه: <input v-model.number="copies" max="20" min="1" style="width: 4rem" type="number" @change="render" /></label>
      <button class="btn btn-sm btn-primary" @click="print">چاپ</button>
      <span v-if="!labels.length" class="text-danger">برچسبی انتخاب نشده است؛ این صفحه را از دکمه «چاپ برچسب» باز کنید.</span>
    </div>
    <div class="sheet">
      <div v-for="(l, i) in shown" :key="i" :class="size" class="label">
        <img v-if="qr[i]" :src="qr[i]" alt="" class="qr" />
        <div class="text">
          <div class="title">{{ l.title }}</div>
          <div v-for="(line, j) in l.lines" :key="j" class="line">{{ line }}</div>
          <svg :ref="(el) => (barcodes[i] = el)" class="barcode"></svg>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.labels-page {
  direction: rtl;
  font-family: inherit;
  padding: 1rem;
  background: #fff;
}
.toolbar {
  display: flex;
  gap: 1rem;
  align-items: center;
  flex-wrap: wrap;
  margin-bottom: 1rem;
}
.sheet {
  display: flex;
  flex-wrap: wrap;
  gap: 2mm;
}
.label {
  border: 1px dashed #bbb;
  display: flex;
  gap: 2mm;
  padding: 2mm;
  overflow: hidden;
  page-break-inside: avoid;
  break-inside: avoid;
  box-sizing: border-box;
}
.label.small {
  width: 50mm;
  height: 30mm;
}
.label.large {
  width: 100mm;
  height: 50mm;
}
.label.small .qr {
  width: 22mm;
  height: 22mm;
}
.label.large .qr {
  width: 40mm;
  height: 40mm;
}
.text {
  flex: 1;
  min-width: 0;
  font-size: 7pt;
  line-height: 1.25;
}
.label.large .text {
  font-size: 10pt;
}
.title {
  font-weight: 700;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.line {
  direction: ltr;
  text-align: right;
  font-family: monospace;
  white-space: nowrap;
  overflow: hidden;
}
.barcode {
  width: 100%;
  height: 6mm;
  margin-top: 1mm;
}
.label.large .barcode {
  height: 12mm;
}
@media print {
  .labels-page {
    padding: 0;
  }
  .label {
    border: none;
  }
}
</style>

<style>
/* print only the labels, not the menu and header around the page */
@media print {
  body * {
    visibility: hidden;
  }
  .labels-page,
  .labels-page * {
    visibility: visible;
  }
  .labels-page {
    position: absolute;
    top: 0;
    right: 0;
  }
}
</style>
