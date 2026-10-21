<script setup lang="ts">
// Calibration labels (small labels glued on the instrument): code, name, calibration date, due date and status.
// Only the calibration officer handles them (WI-QC-001); the page just prints what the register says.
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import { getEquipment } from "@/api/QualityAPIs";
import { fail, jalali } from "@/composables/inventory";
import { equipmentStatuses } from "@/composables/quality";

const route = useRoute();
const print = () => window.print();
const items = ref<any[]>([]);
const copies = ref(1);
const size = ref<"small" | "large">("small");

const shown = () => {
  const list: any[] = [];
  items.value.forEach((e) => {
    for (let i = 0; i < copies.value; i++) list.push(e);
  });
  return list;
};

onMounted(async () => {
  const ids = String(route.query.ids || "").split(",").map(Number).filter(Boolean);
  for (const id of ids) {
    const [data, error] = await getEquipment(id);
    if (error) fail(error);
    else items.value.push(data);
  }
});
</script>

<template>
  <div class="cal-labels ks-report">
    <div class="toolbar d-print-none">
      <b>چاپ برچسب کالیبراسیون ({{ items.length }} تجهیز)</b>
      <label>اندازه:
        <select v-model="size"><option value="small">کوچک 50×30 میلی‌متر</option><option value="large">بزرگ 100×50 میلی‌متر</option></select>
      </label>
      <label>تعداد نسخه: <input v-model.number="copies" max="20" min="1" style="width: 4rem" type="number" /></label>
      <button class="btn btn-sm btn-primary" @click="print">چاپ</button>
      <span v-if="!items.length" class="text-danger">تجهیزی انتخاب نشده است؛ این صفحه را از دفتر تجهیزات باز کنید.</span>
    </div>
    <div class="sheet">
      <div v-for="(e, i) in shown()" :key="i" :class="size" class="label">
        <div class="head"><span>برچسب کالیبراسیون</span><span :class="{ nc: e.status === 2 || e.status === 3 }" class="status">{{ equipmentStatuses[e.status] }}</span></div>
        <div class="row-line"><b>کد:</b> <span class="ltr">{{ e.code }}</span></div>
        <div class="row-line"><b>نام:</b> {{ e.name }}</div>
        <div class="row-line"><b>تاریخ کالیبراسیون:</b> <span class="ltr">{{ jalali(e.lastCalibrated) || "—" }}</span></div>
        <div class="row-line"><b>سررسید:</b> <span class="ltr">{{ jalali(e.nextDue) || "—" }}</span></div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.cal-labels { direction: rtl; font-family: inherit; padding: 1rem; background: #fff; }
.toolbar { display: flex; gap: 1rem; align-items: center; flex-wrap: wrap; margin-bottom: 1rem; }
.sheet { display: flex; flex-wrap: wrap; gap: 2mm; }
.label { border: 1px solid #222; display: flex; flex-direction: column; gap: 0.6mm; padding: 1.5mm; overflow: hidden; page-break-inside: avoid; break-inside: avoid; box-sizing: border-box; font-size: 7.5pt; line-height: 1.25; }
.label.small { width: 50mm; height: 30mm; }
.label.large { width: 100mm; height: 50mm; font-size: 12pt; padding: 3mm; gap: 1.5mm; }
.head { display: flex; justify-content: space-between; font-weight: 700; border-bottom: 1px solid #222; margin-bottom: 0.5mm; }
.status { border: 1px solid #222; padding: 0 1.5mm; border-radius: 2mm; }
.status.nc { background: #222; color: #fff; }
.ltr { direction: ltr; unicode-bidi: isolate; font-family: monospace; }
</style>
