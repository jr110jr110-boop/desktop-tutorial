<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali, rial } from "@/composables/inventory";
import { baselineKinds, baselineStatuses, contractLabel, distributionPlanning, unitTitle } from "@/composables/projectControl";

// Approval sheet of a baseline: created by planning, signed by the unit managers (each by his own role), approved by the PMO deputy.
defineProps<{ b: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="برگه تایید خط مبنای پروژه"
    :fields="[
      { label: 'پروژه', value: contractLabel(b.contractId) },
      { label: 'شماره خط مبنا', value: b.number },
      { label: 'نوع', value: baselineKinds[b.kind] },
      { label: 'وضعیت', value: baselineStatuses[b.status] },
      { label: 'شروع پروژه', value: jalali(b.projectStart) },
      { label: 'پایان پروژه', value: jalali(b.projectFinish) },
      { label: 'بودجه تا تکمیل (BAC)', value: rial(b.bac) },
      { label: 'ذخیره مدیریتی', value: rial(b.managementReserve) },
      { label: 'تعداد فعالیت', value: b.activityCount },
    ]"
    :notes="b.reason ? [{ label: 'علت صدور', value: b.reason }] : []"
    :signatures="[
      { title: 'مدیر برنامه‌ریزی و کنترل پروژه (تنظیم‌کننده)' },
      ...b.signatures.map((s: any) => ({ title: unitTitle(s.unitRole), name: s.userName, date: jalali(s.signedAt) })),
      { title: 'معاون PMO (تایید نهایی)', date: jalali(b.approvedAt) },
    ]"
    :distribution="distributionPlanning">
    <table>
      <thead><tr><th>ردیف</th><th>امضاکننده</th><th>نام</th><th>تاریخ امضا</th></tr></thead>
      <tbody>
        <tr v-for="(s, i) in b.signatures" :key="i"><td>{{ i + 1 }}</td><td>{{ unitTitle(s.unitRole) }}</td><td>{{ s.userName || "امضا نشده" }}</td><td>{{ jalali(s.signedAt) }}</td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
