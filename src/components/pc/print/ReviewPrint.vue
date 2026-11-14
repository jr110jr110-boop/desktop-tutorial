<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { contractLabel, distributionCommercial, reviewDecisions, reviewKinds, reviewStages } from "@/composables/projectControl";

// Contract review (ISO 9001 8.2.3, WI-IP-002): the eight checks with their answers, the decision and the two signatures of the acceptance. It has no company form code yet.
defineProps<{ r: any; checks: [string, string][] }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="فرم بازنگری قرارداد"
    :fields="[
      { label: 'پروژه / قرارداد', value: contractLabel(r.contractId) },
      { label: 'کد مناقصه', value: r.tenderCode, ltr: true },
      { label: 'نوع', value: reviewKinds[r.kind] },
      { label: 'مرحله', value: reviewStages[r.stage] },
      ...(r.amendmentId ? [{ label: 'الحاقیه شماره', value: r.amendmentId }] : []),
      ...(r.variationOrderId ? [{ label: 'دستور تغییر شماره', value: r.variationOrderId }] : []),
      { label: 'تصمیم', value: reviewDecisions[r.decision] },
      { label: 'تاریخ تصمیم', value: jalali(r.decidedAt) },
    ]"
    :notes="[
      { label: 'توضیحات', value: r.notes || '-' },
      ...(r.decisionNote ? [{ label: 'دلیل تصمیم', value: r.decisionNote }] : []),
    ]"
    :signatures="[
      { title: 'معاون PMO', name: r.pmoAcceptedByName, date: jalali(r.pmoAcceptedAt) },
      { title: 'بازرگانی', name: r.commercialAcceptedByName, date: jalali(r.commercialAcceptedAt) },
      ...(r.decision === 2 || r.decision === 3 ? [{ title: 'مدیرعامل (تصمیم)', date: jalali(r.decidedAt) }] : []),
    ]"
    :distribution="distributionCommercial">
    <table>
      <thead><tr><th style="width: 10%">ردیف</th><th>مورد بررسی</th><th style="width: 18%">نتیجه</th></tr></thead>
      <tbody>
        <tr v-for="(c, i) in checks" :key="c[0]"><td>{{ i + 1 }}</td><td>{{ c[1] }}</td><td>{{ r[c[0]] ? "انجام شد" : "انجام نشده" }}</td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
