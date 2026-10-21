<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { contractLabel, distributionDcc, issueStates } from "@/composables/projectControl";

// MDR (all documents of the register) or MDL (the client submittal list, with the client response code). No company form code yet.
const props = defineProps<{ rows: any[]; contractId: number | null; mdl?: boolean; workOrderCode?: string }>();
const signatures = props.mdl ? [{ title: "کنترل مدارک پروژه" }, { title: "مدیر پروژه" }] : [{ title: "کنترل مدارک پروژه" }, { title: "مدیر مهندسی" }];
</script>

<template>
  <PrintForm
    class="q-form"
    :title="mdl ? 'فهرست مدارک ارسالی به کارفرما (MDL)' : 'فهرست اصلی مدارک مهندسی (MDR)'"
    :fields="[
      { label: 'پروژه', value: contractLabel(contractId) },
      { label: 'دستور ساخت', value: workOrderCode || 'همه', ltr: true },
      { label: 'تعداد اسناد', value: rows.length },
      { label: 'تاییدشده', value: rows.filter((r) => r.issueState === 3).length },
    ]"
    :signatures="signatures"
    :distribution="distributionDcc">
    <table>
      <thead><tr><th>ردیف</th><th>شماره سند</th><th>عنوان</th><th>رشته</th><th>ویرایش</th><th>وضعیت</th><th>ترنسمیتال</th><th v-if="mdl">پاسخ کارفرما</th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td :colspan="mdl ? 8 : 7">سندی ثبت نشده.</td></tr>
        <tr v-for="(r, i) in rows" :key="r.id">
          <td>{{ i + 1 }}</td><td class="inv-ltr">{{ r.documentNumber }}</td><td>{{ r.title }}</td><td>{{ r.discipline }}</td><td class="inv-ltr">{{ r.revision }}</td><td>{{ issueStates[r.issueState] }}</td>
          <td><span class="inv-ltr">{{ r.lastTransmittalNumber }}</span> {{ jalali(r.lastTransmittalDate) }}</td>
          <td v-if="mdl"><span class="inv-ltr">{{ r.clientResponseCode }}</span> {{ jalali(r.clientResponseDate) }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
