<script setup lang="ts">
import { ref } from "vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import BookingList from "@/components/production/floor/BookingList.vue";
import AttendanceDiff from "@/components/production/floor/AttendanceDiff.vue";
import { hasRole, ProductionRoles } from "@/composables/roles";

// Time bookings of the shop floor and the comparison of attendance with booked hours.
// HR may open only the attendance check (the bookings list refuses HR)
const canList = hasRole(ProductionRoles.masterReaders);
const tab = ref(canList ? "list" : "attendance");
const tabs = [
  { key: "list", title: "ثبت زمان‌ها", hidden: !canList },
  { key: "attendance", title: "اختلاف حضور و ثبت زمان", hidden: !hasRole(ProductionRoles.attendanceCheck) },
];
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">ثبت زمان کار</h1></div>
      <PcTabs v-model="tab" :tabs="tabs" />
      <BookingList v-if="tab === 'list'" />
      <AttendanceDiff v-else-if="tab === 'attendance'" />
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
