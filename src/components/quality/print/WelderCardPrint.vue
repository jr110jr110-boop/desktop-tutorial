<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionQc, employerKinds, ndtLevels, qualificationKinds, qualificationSchemes, qualificationStates } from "@/composables/quality";

// Qualification card of a welder / NDT person (no company form code yet).
defineProps<{ person: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="کارت صلاحیت پرسنل"
    :fields="[
      { label: 'نام', value: person.name },
      { label: 'کد پرسنلی', value: person.personnelCode || '-', ltr: true },
      { label: 'شماره مهر / استمپ', value: person.stampId || '-', ltr: true },
      { label: 'کارفرمای فرد', value: employerKinds[person.employer] },
    ]"
    :signatures="[{ title: 'مسئول مدارک صلاحیت (کنترل کیفیت)' }]"
    :distribution="distributionQc">
    <table>
      <thead><tr><th>نوع</th><th>روش</th><th>سطح</th><th>طرح</th><th>دامنه</th><th>شماره گواهی</th><th>صدور</th><th>انقضا</th><th>وضعیت</th></tr></thead>
      <tbody>
        <tr v-if="!(person.qualifications || []).length"><td colspan="9">گواهی‌ای ثبت نشده.</td></tr>
        <tr v-for="q in person.qualifications || []" :key="q.id">
          <td>{{ qualificationKinds[q.kind] }}</td><td>{{ q.method }}</td><td>{{ ndtLevels[q.level] }}</td><td>{{ qualificationSchemes[q.scheme] }}</td><td>{{ q.scope }}</td>
          <td class="inv-ltr">{{ q.certificateNumber }}</td><td>{{ jalali(q.issuedDate) }}</td><td>{{ jalali(q.expiryDate) }}</td><td>{{ q.stateText || qualificationStates[q.state] }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
