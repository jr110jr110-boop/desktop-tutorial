<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionQc, vtResults } from "@/composables/quality";

// Weld log of a work order / serial (no company form code yet).
defineProps<{ rows: any[]; label: string }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="گزارش جوشکاری (Weld Log)"
    :fields="[{ label: 'دستور ساخت / سریال', value: label }]"
    :signatures="[{ title: 'بازرس جوش (کنترل کیفیت)' }, { title: 'سرپرست جوشکاری' }]"
    :distribution="distributionQc">
    <table>
      <thead><tr><th>تاریخ</th><th>شماره درز</th><th>سریال</th><th>WPS</th><th>جوشکار</th><th>مهر</th><th>فرایند</th><th>بچ مصرفی</th><th>پیشگرم</th><th>بین‌پاس</th><th>VT</th><th>گزارش‌های NDT</th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td colspan="12">موردی نیست.</td></tr>
        <tr v-for="r in rows" :key="r.id">
          <td>{{ jalali(r.weldDate) }}</td><td class="inv-ltr">{{ r.jointNo }}</td><td class="inv-ltr">{{ r.serialNumber }}</td><td class="inv-ltr">{{ r.wpsNumber }}</td><td>{{ r.welderName }}</td><td class="inv-ltr">{{ r.welderStamp }}</td>
          <td>{{ r.process }}</td><td class="inv-ltr">{{ r.fillerBatch }}</td><td>{{ r.preheatC ?? "" }}</td><td>{{ r.interpassC ?? "" }}</td><td>{{ vtResults[r.vt] }}</td><td class="inv-ltr">{{ (r.reportNumbers || []).join("، ") }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
