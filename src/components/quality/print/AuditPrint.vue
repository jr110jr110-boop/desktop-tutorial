<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { auditStatuses, distributionQa, findingTypes, qualityFormCodes } from "@/composables/quality";

// FO-QA-018: internal audit report.
defineProps<{ audit: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="گزارش ممیزی داخلی"
    :form-code="qualityFormCodes.auditReport"
    :fields="[
      { label: 'شماره', value: audit.number, ltr: true },
      { label: 'واحد ممیزی‌شونده', value: audit.auditedUnit },
      { label: 'معیار ممیزی', value: audit.criteria, ltr: true },
      { label: 'تاریخ برنامه', value: jalali(audit.plannedDate) },
      { label: 'ممیز ارشد', value: audit.leadAuditorName },
      { label: 'اعضای تیم', value: (audit.teamNames || []).join('، ') || '-' },
      { label: 'وضعیت', value: auditStatuses[audit.status] },
      { label: 'تاریخ انجام', value: jalali(audit.doneAt) || '-' },
    ]"
    :signatures="[
      { title: 'ممیز ارشد', name: audit.leadAuditorName },
      { title: 'مدیر واحد ممیزی‌شونده', hint: audit.auditedUnit },
      { title: 'نماینده مدیریت (تضمین کیفیت)', date: jalali(audit.closedAt) },
    ]"
    :distribution="distributionQa">
    <table>
      <thead><tr><th>ردیف</th><th>بند</th><th>نوع</th><th>شواهد</th><th>مهلت</th><th>CAPA</th></tr></thead>
      <tbody>
        <tr v-if="!audit.findings.length"><td colspan="6">یافته‌ای ثبت نشده.</td></tr>
        <tr v-for="(f, i) in audit.findings" :key="f.id"><td>{{ i + 1 }}</td><td class="inv-ltr">{{ f.clause }}</td><td>{{ findingTypes[f.type] }}</td><td>{{ f.evidence }}</td><td>{{ jalali(f.dueDate) }}</td><td class="inv-ltr">{{ f.capaNumber }}</td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
