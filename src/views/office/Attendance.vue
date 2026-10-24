<script setup lang="ts">
// Attendance (حضور و غیاب): daily results, clock events (manual and file import for HR) and the computation (HR, CEO).
// HR and CEO see everybody; everyone else sees their own rows (the server filters, the page only hides the employee filter).
import { computed, ref } from "vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import AttendanceCompute from "@/components/office/att/AttendanceCompute.vue";
import AttendanceDays from "@/components/office/att/AttendanceDays.vue";
import ClockEvents from "@/components/office/att/ClockEvents.vue";
import { hasRole, OfficeRoles } from "@/composables/roles";

const canAll = hasRole(OfficeRoles.attendanceReaders);
const isHr = hasRole(OfficeRoles.hr);
const tab = ref("days");
const tabs = computed(() => [{ key: "days", title: "نتیجه روزانه" }, { key: "clock", title: "ترددها" }, { key: "compute", title: "محاسبه", hidden: !canAll }]);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">حضور و غیاب</h1>
        <router-link v-if="canAll || isHr" :to="{ name: 'OfMonthClose' }" class="btn btn-sm btn-outline-secondary">بستن ماه</router-link>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />
      <AttendanceDays v-if="tab === 'days'" :can-all="canAll" />
      <ClockEvents v-else-if="tab === 'clock'" :can-all="canAll" :is-hr="isHr" />
      <AttendanceCompute v-else-if="tab === 'compute' && canAll" />
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
