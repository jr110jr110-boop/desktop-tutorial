<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import type { LoadRow } from "@/api/ResourceAPIs";
import { faNum } from "@/composables/ceo";
import { distributionCommercial } from "@/composables/projectControl";
import { faDate, loadNum, PRODUCTION_LAYER, resourceUnits } from "@/composables/resources";

// «بار منابع»: the load of the resources against their capacity over a period, in the buckets of the screen. No amount appears in this report (the load is in the unit of the resource).
const props = defineProps<{ rows: LoadRow[]; from: string; to: string; bucket: "day" | "week" }>();
const first = (r: LoadRow) => r.buckets.filter((b) => b.over > 0);
const production = (r: LoadRow) => r.buckets.reduce((s, b) => s + b.productionLoad, 0);
const hasProduction = () => props.rows.some((r) => production(r) > 0);
</script>

<template>
  <PrintForm
    class="q-form"
    title="بار منابع"
    :fields="[
      { label: 'از تاریخ', value: faDate(from) },
      { label: 'تا تاریخ', value: faDate(to) },
      { label: 'بازه', value: bucket === 'week' ? 'هفتگی' : 'روزانه' },
      { label: 'تعداد منبع', value: faNum(rows.length, 0) },
    ]"
    :signatures="[{ title: 'تهیه‌کننده' }, { title: 'تاییدکننده' }]"
    :distribution="distributionCommercial">
    <table>
      <thead>
        <tr><th>منبع</th><th>ظرفیت</th><th>اوج بار</th><th v-if="hasProduction()">جمع {{ PRODUCTION_LAYER }}</th><th>جمع بیش‌تخصیص</th><th>بازه‌های بیش‌تخصیص</th></tr>
      </thead>
      <tbody>
        <tr v-if="!rows.length"><td colspan="6">در این بازه بار ثبت نشده است.</td></tr>
        <tr v-for="r in rows" :key="r.resourceId">
          <td><span class="inv-ltr">{{ r.code }}</span> {{ r.title }}</td>
          <td>{{ loadNum(r.capacity) }} {{ resourceUnits[r.unit] }}</td>
          <td>{{ loadNum(r.peakLoad) }}</td>
          <td v-if="hasProduction()">{{ loadNum(production(r)) }}</td>
          <td>{{ r.totalOver > 0 ? loadNum(r.totalOver) : "ندارد" }}</td>
          <td>
            <template v-if="first(r).length">
              <div v-for="b in first(r).slice(0, 8)" :key="b.start">{{ bucket === "week" ? faDate(b.start) + " تا " + faDate(b.end) : faDate(b.start) }}: {{ loadNum(b.load + b.productionLoad) }} از {{ loadNum(b.capacity) }}</div>
              <div v-if="first(r).length > 8">و {{ faNum(first(r).length - 8, 0) }} بازه‌ی دیگر</div>
            </template>
            <template v-else>—</template>
          </td>
        </tr>
      </tbody>
    </table>
    <p class="small">بار هر منبع از طرح‌های منابع تاییدشده‌ی پروژه‌ها حساب شده است{{ hasProduction() ? "؛ «" + PRODUCTION_LAYER + "» دستورهای تولید مرکز کار وصل‌شده است و در بیش‌تخصیص می‌آید" : "" }}.</p>
  </PrintForm>
</template>
