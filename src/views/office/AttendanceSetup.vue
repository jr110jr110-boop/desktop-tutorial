<script setup lang="ts">
// Attendance setup (HR, PMO, CEO): shifts, shift assignments, clock devices and the policy.
import { ref } from "vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import AssignmentsTab from "@/components/office/att/AssignmentsTab.vue";
import DevicesTab from "@/components/office/att/DevicesTab.vue";
import PolicyTab from "@/components/office/att/PolicyTab.vue";
import ShiftsTab from "@/components/office/att/ShiftsTab.vue";

const tab = ref("shifts");
const tabs = [{ key: "shifts", title: "شیفت‌ها" }, { key: "assignments", title: "تخصیص شیفت" }, { key: "devices", title: "دستگاه‌های تردد" }, { key: "policy", title: "سیاست حضور و غیاب" }];
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">تنظیمات حضور و غیاب</h1></div>
      <PcTabs v-model="tab" :tabs="tabs" />
      <ShiftsTab v-if="tab === 'shifts'" />
      <AssignmentsTab v-else-if="tab === 'assignments'" />
      <DevicesTab v-else-if="tab === 'devices'" />
      <PolicyTab v-else-if="tab === 'policy'" />
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
