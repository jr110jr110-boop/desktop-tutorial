<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import type { PerfRow, ResourcePerformance } from "@/api/ResourceAPIs";
import { faNum } from "@/composables/ceo";
import { contractLabel, distributionCommercial, resourceClasses } from "@/composables/projectControl";
import { CONFIDENTIAL, faDate, money, NO_APPROVED_RATE, qty } from "@/composables/resources";

// «بودجه و عملکرد منابع» on paper: the same figures as the screen, per class and per resource; amounts that could show one person's pay are «محرمانه» (never in the totals).
defineProps<{ data: ResourcePerformance; contractId: number | null }>();
const cell = (v: number | null, r: PerfRow) => (v !== null ? money(v) : r.rateHidden ? CONFIDENTIAL : r.noApprovedRate ? NO_APPROVED_RATE : "—");
</script>

<template>
  <PrintForm
    class="q-form"
    title="بودجه و عملکرد منابع"
    :fields="[
      { label: 'پروژه', value: contractLabel(contractId) },
      { label: 'تاریخ داده', value: faDate(data.asOf) },
      { label: 'جمع خط مبنا (ریال)', value: money(data.baselineTotal) },
      { label: 'جمع طرح جاری (ریال)', value: money(data.planTotal) },
    ]"
    :signatures="[{ title: 'تهیه‌کننده' }, { title: 'تاییدکننده' }]"
    :distribution="distributionCommercial">
    <table>
      <thead><tr><th>گروه</th><th>خط مبنا</th><th>برنامه تا تاریخ</th><th>طرح جاری</th><th>واقعی (مقدار)</th><th>واقعی (ریال)</th><th>انحراف (ریال)</th></tr></thead>
      <tbody>
        <tr v-for="c in data.byClass" :key="c.resourceClass">
          <td>{{ resourceClasses[c.resourceClass] }}</td><td>{{ cell(c.baselineAmount, c) }}</td><td>{{ cell(c.plannedToDateAmount, c) }}</td><td>{{ cell(c.planAmount, c) }}</td>
          <td>{{ c.actualQuantity === null ? "—" : qty(c.actualQuantity) }}</td><td>{{ cell(c.actualAmount, c) }}</td><td>{{ cell(c.varianceAmount, c) }}</td>
        </tr>
      </tbody>
    </table>
    <table>
      <thead><tr><th>منبع</th><th>خط مبنا (مقدار)</th><th>طرح جاری (مقدار)</th><th>واقعی (مقدار)</th><th>خط مبنا (ریال)</th><th>طرح جاری (ریال)</th><th>واقعی (ریال)</th></tr></thead>
      <tbody>
        <tr v-for="r in data.rows" :key="r.resourceId || r.resourceCode || ''">
          <td><span class="inv-ltr">{{ r.resourceCode }}</span> {{ r.resourceTitle }}</td><td>{{ qty(r.baselineQuantity) }}</td><td>{{ qty(r.planQuantity) }}</td><td>{{ r.actualQuantity === null ? "—" : qty(r.actualQuantity) }}</td>
          <td>{{ cell(r.baselineAmount, r) }}</td><td>{{ cell(r.planAmount, r) }}</td><td>{{ cell(r.actualAmount, r) }}</td>
        </tr>
      </tbody>
    </table>
    <p class="small">ردیف {{ CONFIDENTIAL }}: {{ faNum(data.hiddenLineCount, 0) }}؛ ردیف بدون نرخ مصوب: {{ faNum(data.noRateCount, 0) }}؛ این ردیف‌ها در جمع‌ها نیست. نیروی انسانی فقط جمع ساعت گروه است، نه به تفکیک نفر و نه با مبلغ.</p>
  </PrintForm>
</template>
