<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import type { Budget, ResourcePlan } from "@/api/ResourceAPIs";
import { faNum } from "@/composables/ceo";
import { assignmentSources, CONFIDENTIAL, faDateTime, money, NO_APPROVED_RATE, planStatuses, qty, resourceKinds, resourceUnits } from "@/composables/resources";
import { distributionCommercial } from "@/composables/projectControl";

// «برنامه منابع پروژه»: the resource plan of a project, line by line, with the amounts of the budget view. A4, B Zar through PrintForm, Persian digits, Shamsi dates.
// An amount that could show one person's pay is «محرمانه» for a viewer who may not see it and is left out of the total, as on the screen.
const props = defineProps<{ plan: ResourcePlan; budget: Budget | null }>();
const lineOf = (assignmentId: number) => props.budget?.lines.find((l) => l.assignmentId === assignmentId) || null;
const rateText = (id: number) => {
  const l = lineOf(id);
  if (!props.budget) return "—";
  if (!l) return "—";
  if (l.rateHidden) return CONFIDENTIAL;
  if (l.noApprovedRate) return NO_APPROVED_RATE;
  return money(l.amount);
};
</script>

<template>
  <PrintForm
    class="q-form"
    title="برنامه منابع پروژه"
    :fields="[
      { label: 'پروژه', value: plan.contractCode, ltr: true },
      { label: 'نسخه', value: faNum(plan.version, 0) },
      { label: 'وضعیت', value: planStatuses[plan.status] },
      { label: 'تهیه‌کننده', value: plan.makerName || '—' },
      { label: 'تاییدکننده', value: plan.approvedByName || '—' },
      { label: 'تاریخ تایید', value: plan.approvedAt ? faDateTime(plan.approvedAt) : '—' },
    ]"
    :signatures="[
      { title: 'تهیه‌کننده', name: plan.makerName, date: plan.submittedAt ? faDateTime(plan.submittedAt) : '' },
      { title: 'تاییدکننده', name: plan.approvedByName, date: plan.approvedAt ? faDateTime(plan.approvedAt) : '' },
    ]"
    :distribution="distributionCommercial">
    <table>
      <thead><tr><th>ردیف</th><th>فعالیت</th><th>منبع</th><th>گروه</th><th>مقدار</th><th>منبع ثبت</th><th>مبلغ (ریال)</th></tr></thead>
      <tbody>
        <tr v-if="!plan.assignments.length"><td colspan="7">تخصیصی ثبت نشده است.</td></tr>
        <tr v-for="(a, i) in plan.assignments" :key="a.id">
          <td>{{ faNum(i + 1, 0) }}</td>
          <td><span class="inv-ltr">{{ a.activityCode }}</span> {{ a.activityTitle }}</td>
          <td><span class="inv-ltr">{{ a.resourceCode }}</span> {{ a.resourceTitle }}</td>
          <td>{{ resourceKinds[a.resourceKind] }}</td>
          <td>{{ qty(a.quantity) }} {{ resourceUnits[a.resourceUnit] }}</td>
          <td>{{ assignmentSources[a.source] }}</td>
          <td>{{ rateText(a.id) }}</td>
        </tr>
      </tbody>
      <tfoot v-if="budget">
        <tr><th colspan="6">جمع مبلغ (ردیف‌های بدون نرخ مصوب و {{ CONFIDENTIAL }} در جمع نیست)</th><th>{{ money(budget.total) }}</th></tr>
      </tfoot>
    </table>
    <p v-if="budget && (budget.noRateCount || budget.hiddenLineCount)" class="small">
      ردیف بدون نرخ مصوب: {{ faNum(budget.noRateCount, 0) }}؛ ردیف {{ CONFIDENTIAL }}: {{ faNum(budget.hiddenLineCount, 0) }}.
    </p>
  </PrintForm>
</template>
