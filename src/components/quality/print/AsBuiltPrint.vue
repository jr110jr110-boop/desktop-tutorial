<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali, num } from "@/composables/inventory";
import { distributionQc } from "@/composables/quality";

// «پیکربندی ساخته‌شده» (as-built record) of ONE released unit: product, BOM and routing revisions, drawings, installed serials / lots / heat numbers, deviations, NCRs and first articles. Printed in the final data book.
defineProps<{ rec: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="پیکربندی ساخته‌شده (As-Built)"
    :fields="[
      { label: 'محصول', value: rec.itemCode, ltr: true },
      { label: 'نام', value: rec.itemTitle },
      { label: 'سریال', value: rec.serialNumber || '-', ltr: true },
      { label: 'نسخه', value: rec.version },
      { label: 'دستور ساخت', value: rec.workOrderCode, ltr: true },
      { label: 'برگه ترخیص', value: rec.releaseNoteNumber, ltr: true },
      { label: 'BOM', value: rec.bomText || '-', ltr: true },
      { label: 'تاریخ صدور', value: jalali(rec.createDate) },
    ]"
    :notes="[{ label: 'SHA-256', value: rec.sha256 }, ...(rec.reason && rec.version > 1 ? [{ label: 'دلیل نسخه جدید', value: rec.reason }] : [])]"
    :signatures="[{ title: 'کنترل کیفیت', name: rec.issuedByName }, { title: 'تضمین کیفیت' }]"
    :distribution="distributionQc">
    <template v-if="rec.snapshot">
      <table>
        <thead><tr><th colspan="4">قطعات، بچ‌ها و سریال‌های نصب‌شده</th></tr><tr><th>شماره قطعه</th><th>بچ / سریال</th><th>شماره ذوب / گواهی</th><th>مقدار</th></tr></thead>
        <tbody>
          <tr v-if="!rec.snapshot.installed.length"><td colspan="4">ثبت نشده.</td></tr>
          <tr v-for="(i, k) in rec.snapshot.installed" :key="k"><td class="inv-ltr">{{ i.partNumber }}</td><td class="inv-ltr">{{ i.serialNumber || i.lotNumber }}</td><td class="inv-ltr">{{ [i.heatNumber, i.certificateNumber].filter((x: any) => x).join(" / ") }}</td><td>{{ num(i.quantity) }}</td></tr>
        </tbody>
      </table>
      <table>
        <thead><tr><th colspan="3">نقشه‌ها و مسیر ساخت</th></tr><tr><th>قطعه</th><th>نقشه / بازنگری</th><th>نسخه فهرست مشخصه / بازنگری مسیر</th></tr></thead>
        <tbody>
          <tr v-for="(d, k) in rec.snapshot.drawings" :key="'d' + k"><td class="inv-ltr">{{ d.partNumber }}</td><td class="inv-ltr">{{ d.documentNumber }} / {{ d.revisionCode }}</td><td>{{ d.listVersion || "-" }}</td></tr>
          <tr v-for="(r, k) in rec.snapshot.routings" :key="'r' + k"><td class="inv-ltr">{{ r.partNumber }}</td><td>مسیر ساخت</td><td>بازنگری {{ r.revision }}</td></tr>
        </tbody>
      </table>
      <table v-if="rec.snapshot.deviations.length || rec.snapshot.ncrs.length || rec.snapshot.firstArticles.length">
        <thead><tr><th colspan="4">انحراف‌ها، عدم انطباق‌ها و نمونه اول</th></tr></thead>
        <tbody>
          <tr v-for="(d, k) in rec.snapshot.deviations" :key="'v' + k"><td class="inv-ltr">{{ d.number }}</td><td>{{ d.kind }}</td><td class="inv-ltr">{{ d.partNumber }}</td><td>{{ d.reason }}</td></tr>
          <tr v-for="(n, k) in rec.snapshot.ncrs" :key="'n' + k"><td class="inv-ltr">{{ n.number }}</td><td>{{ n.status }}</td><td>{{ n.disposition }}</td><td>{{ n.concessionApproved ? "مجوز ارفاقی معتبر" : "" }}</td></tr>
          <tr v-for="(f, k) in rec.snapshot.firstArticles" :key="'f' + k"><td class="inv-ltr">{{ f.number }}</td><td>{{ f.status }}</td><td class="inv-ltr">{{ f.partNumber }}</td><td>{{ f.fingerprintText }}</td></tr>
        </tbody>
      </table>
    </template>
  </PrintForm>
</template>
