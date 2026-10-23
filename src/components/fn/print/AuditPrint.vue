<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionAudit, jalaliInText } from "@/composables/fnF3";

// Audit report (گزارش ردپای حسابرسی): the filtered entries of the append-only audit trail.
defineProps<{ rows: any[]; fields: { label: string; value: any }[] }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="گزارش ردپای حسابرسی (سامانه مالی)"
    :fields="[...fields, { label: 'تعداد رکورد', value: rows.length }]"
    :signatures="[{ title: 'حسابرس' }, { title: 'مدیر مالی' }, { title: 'مدیرعامل' }]"
    :distribution="distributionAudit">
    <table>
      <thead><tr><th>ردیف</th><th>تاریخ و ساعت</th><th>کاربر</th><th>نوع رکورد</th><th>شناسه</th><th>عملیات</th><th>خلاصه</th><th>نشانی</th></tr></thead>
      <tbody>
        <tr v-for="(r, i) in rows" :key="r.id">
          <td>{{ i + 1 }}</td><td class="inv-ltr">{{ jalali(r.at, true) }}</td><td>{{ r.userName }}</td><td class="inv-ltr">{{ r.entityType }}</td><td>{{ r.entityId }}</td><td>{{ r.action }}</td><td>{{ jalaliInText(r.summary) }}</td><td class="inv-ltr">{{ r.ip }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
