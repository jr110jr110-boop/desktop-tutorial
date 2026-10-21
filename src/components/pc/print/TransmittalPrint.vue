<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { contractLabel, distributionDcc, transmittalDirections, transmittalPurposes } from "@/composables/projectControl";

// Transmittal of documents to (or from) the client; the client signs the receipt. No company form code yet.
defineProps<{ t: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="ترنسمیتال مدارک"
    :fields="[
      { label: 'شماره', value: t.number, ltr: true },
      { label: 'جهت', value: transmittalDirections[t.direction] },
      { label: 'پروژه', value: contractLabel(t.contractId) },
      { label: 'تاریخ', value: jalali(t.transmittalDate) },
      { label: 'مهلت پاسخ کارفرما', value: jalali(t.responseDue) },
      { label: 'مرجع (نامه)', value: t.reference },
    ]"
    :notes="t.note ? [{ label: 'یادداشت', value: t.note }] : []"
    :signatures="[
      { title: t.direction === 1 ? 'کنترل مدارک پروژه (ارسال‌کننده)' : 'کنترل مدارک پروژه (دریافت‌کننده)' },
      { title: t.direction === 1 ? 'نماینده کارفرما (دریافت‌کننده)' : 'نماینده کارفرما (ارسال‌کننده)' },
    ]"
    :distribution="distributionDcc">
    <table>
      <thead><tr><th>ردیف</th><th>شماره سند</th><th>عنوان</th><th>ویرایش</th><th>منظور</th><th>کد پاسخ</th><th>تاریخ پاسخ</th></tr></thead>
      <tbody>
        <tr v-for="(l, i) in t.lines" :key="l.id">
          <td>{{ i + 1 }}</td><td class="inv-ltr">{{ l.documentNumber }}</td><td>{{ l.title }}</td><td class="inv-ltr">{{ l.revision }}</td><td>{{ transmittalPurposes[l.purpose] }}</td><td class="inv-ltr">{{ l.responseCode }}</td><td>{{ jalali(l.responseDate) }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
