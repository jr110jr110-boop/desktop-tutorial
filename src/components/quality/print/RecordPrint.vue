<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali, num } from "@/composables/inventory";
import { distributionQc, qcResults, qualityFormCodes, recordTypes } from "@/composables/quality";

// kind dimensional: FO-QC-14 (DCF); order: FO-QC-018 inspection order; inspection: the generic record (no company code).
const props = defineProps<{ rec: any; kind: "dimensional" | "order" | "inspection" }>();
const title = () => (props.kind === "dimensional" ? "فرم کنترل ابعادی (DCF)" : props.kind === "order" ? "فرم صدور حکم بازرسی" : "سابقه بازرسی کنترل کیفیت");
const code = () => (props.kind === "dimensional" ? qualityFormCodes.dimensional : props.kind === "order" ? qualityFormCodes.inspectionOrder : "");
</script>

<template>
  <PrintForm
    class="q-form"
    :title="title()"
    :form-code="code()"
    :fields="[
      { label: 'شماره', value: rec.number, ltr: true },
      { label: 'تاریخ', value: jalali(rec.recordDate) },
      { label: 'نوع', value: recordTypes[rec.type] },
      { label: 'پروژه / دستور ساخت', value: rec.workOrderCode || '-' },
      { label: 'کد قطعه', value: rec.itemCode || '-', ltr: true },
      { label: 'نام قطعه', value: rec.itemTitle || '-' },
      { label: 'سریال', value: rec.serialNumber || '-', ltr: true },
      { label: 'مقدار', value: rec.quantity ? num(rec.quantity) : '-' },
      { label: 'مرحله / عملیات', value: rec.operation || '-' },
      { label: 'کد نقشه / ویرایش', value: (rec.drawingNumber || '-') + ' / ' + (rec.drawingRevision || '-'), ltr: true },
      { label: 'بازرس', value: rec.inspectorName },
      ...(kind === 'order' ? [] : [{ label: 'نتیجه', value: qcResults[rec.result] }]),
    ]"
    :notes="[{ label: 'توضیحات', value: rec.note || '-' }]"
    :signatures="
      kind === 'order'
        ? [{ title: 'صادرکننده حکم (واحد PMO / کنترل کیفیت)' }, { title: 'بازرس کنترل کیفیت', name: rec.inspectorName }]
        : [{ title: 'بازرس کنترل کیفیت', name: rec.inspectorName, date: jalali(rec.recordDate) }, { title: rec.selfInspection ? 'سرپرست ساخت (خودبازرسی)' : 'مسئول ساخت و مونتاژ (اطلاع از نتیجه)' }]
    "
    :distribution="kind === 'dimensional' ? 'توزیع نسخ: ۱- ساخت ۲- طراحی ۳- کنترل کیفیت (نگهداری: دائمی)' : distributionQc">
    <table v-if="kind !== 'order'">
      <thead><tr><th>مشخصه</th><th>اسمی</th><th>+ تلرانس</th><th>- تلرانس</th><th>مقدار اندازه‌گیری</th><th>واحد</th><th>ابزار</th><th>نتیجه</th></tr></thead>
      <tbody>
        <tr v-if="!rec.measurements.length"><td colspan="8">اندازه‌گیری ثبت نشده.</td></tr>
        <tr v-for="m in rec.measurements" :key="m.id">
          <td>{{ m.characteristic }}</td><td>{{ m.nominal }}</td><td>{{ m.tolPlus }}</td><td>{{ m.tolMinus }}</td><td>{{ m.measured }}</td><td>{{ m.unit }}</td><td class="inv-ltr">{{ m.equipmentCode }}</td><td>{{ m.pass ? "قبول" : "مردود" }}</td>
        </tr>
      </tbody>
    </table>
    <table v-else>
      <tbody>
        <tr><td style="width: 25%"><b>تامین‌کننده / محل بازرسی</b></td><td>&nbsp;</td></tr>
        <tr><td><b>موضوع بازرسی</b></td><td>{{ rec.operation || rec.itemTitle || "" }}</td></tr>
        <tr><td><b>تاریخ انجام بازرسی</b></td><td>&nbsp;</td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
