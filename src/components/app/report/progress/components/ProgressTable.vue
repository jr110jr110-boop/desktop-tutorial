<script setup lang="ts">
import type { PropType } from "vue";
import type { ProgressReport } from "@/types/ProgressReport";
import { convertToJalaliDate } from "@/composables/ConvertToJalaliDate";

defineProps({
  reportProgress: {
    type: Object as PropType<ProgressReport>,
    required: true,
  },
});
</script>

<template>
  <div class="table-responsive" dir="rtl">
    <table class="table table-borderless IRANYekan">
      <thead>
        <tr class="border-0">
          <th scope="col"></th>
          <th scope="col">تاریخ شروع</th>
          <th scope="col">تاریخ پایان</th>
          <th scope="col">درصد پیشرفت %</th>
          <th scope="col">هزینه (تومان)</th>
        </tr>
      </thead>
      <tbody>
        <tr>
          <td>برنامه</td>
          <td>{{ convertToJalaliDate(reportProgress?.plannedStartDate) }}</td>
          <td>{{ convertToJalaliDate(reportProgress?.plannedEndDate) }}</td>
          <td>{{ reportProgress?.workOrderLevelPlannedProgress }}</td>
          <td>{{ reportProgress?.plannedPrice?.toLocaleString("ar-SA") }}</td>
        </tr>
        <tr>
          <td>واقعی</td>
          <td>{{ convertToJalaliDate(reportProgress?.actualStartDate) }}</td>
          <td>{{ convertToJalaliDate(reportProgress?.actualEndDate) }}</td>
          <td>{{ reportProgress?.workOrderLevelActualProgress }}</td>
          <td>{{ reportProgress?.actualPrice?.toLocaleString("ar-SA") }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped lang="scss">
.table {
  td,
  th {
    border: solid 1px rgba(var(--black), 0.1);
  }

  td {
    font-size: 14px;
  }

  th {
    font-size: 12px;
  }
}
</style>
