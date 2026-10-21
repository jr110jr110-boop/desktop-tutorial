<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionQa, documentTypes } from "@/composables/quality";

// Transmittal / distribution sheet of one revision of a controlled document (no company form code yet).
defineProps<{ doc: any; rev: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="فرم ارسال و توزیع مدرک"
    :fields="[
      { label: 'کد مدرک', value: doc.code, ltr: true },
      { label: 'عنوان', value: doc.title },
      { label: 'نوع', value: documentTypes[doc.type] },
      { label: 'بازنگری', value: rev.revision, ltr: true },
      { label: 'تاریخ اجرا', value: jalali(rev.effectiveDate) },
      { label: 'شرح تغییرات', value: rev.changeSummary },
    ]"
    :signatures="[
      { title: 'تهیه‌کننده', name: rev.preparedByName },
      { title: 'بررسی‌کننده', name: rev.reviewedByName, date: jalali(rev.reviewedAt) },
      { title: 'تاییدکننده', name: rev.approvedByName, date: jalali(rev.approvedAt) },
    ]"
    :distribution="distributionQa">
    <table>
      <thead><tr><th>ردیف</th><th>گیرنده</th><th>واحد</th><th>تاریخ ارسال</th><th>تاریخ دریافت</th><th>امضا</th></tr></thead>
      <tbody>
        <tr v-if="!rev.distributions.length"><td colspan="6">توزیعی ثبت نشده.</td></tr>
        <tr v-for="(d, i) in rev.distributions" :key="d.id">
          <td>{{ i + 1 }}</td><td>{{ d.recipientName }}</td><td>{{ d.recipientUnit }}</td><td>{{ jalali(d.sentAt) }}</td><td>{{ jalali(d.acknowledgedAt) }}</td><td>&nbsp;</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
