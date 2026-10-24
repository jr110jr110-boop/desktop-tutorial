<script setup lang="ts">
// Leave balance (days, two decimals): own for everybody, any employee for HR and CEO.
import { onMounted, ref } from "vue";
import { getLeaveBalance } from "@/api/OfficeAPIs";
import { fail } from "@/composables/inventory";
import EmployeePick from "./EmployeePick.vue";
import { days2 } from "./attCommon";

const props = defineProps<{ canPick: boolean }>();
const employee = ref<any>(null);
const balance = ref<any>(null);
const loading = ref(false);

async function load() {
  loading.value = true;
  const [d, error] = await getLeaveBalance({ employeeId: props.canPick ? employee.value?.id || null : null });
  loading.value = false;
  if (error) {
    balance.value = null;
    return fail(error);
  }
  balance.value = d;
}
const tiles: [string, string, string][] = [
  ["carriedForward", "مانده از سال قبل", ""], ["entitled", "استحقاق ثبت‌شده", ""], ["used", "استفاده‌شده", ""], ["entitlementRate", "نرخ استحقاق ماهانه", ""],
  ["projectedEntitlement", "استحقاق ماه‌های ثبت‌نشده", ""], ["pendingDays", "مرخصی تاییدشده ثبت‌نشده در حقوق", "warn"], ["available", "مانده قابل استفاده", "ok"],
];
onMounted(load);
</script>

<template>
  <div class="border rounded p-3 mb-3">
    <div class="d-flex align-items-end gap-2 flex-wrap mb-2">
      <h2 class="inv-title mb-0 me-2">مانده مرخصی (روز)</h2>
      <div v-if="canPick" style="min-width: 220px"><label class="form-label small mb-0">کارمند</label><EmployeePick v-model="employee" placeholder="خودم" /></div>
      <button v-if="canPick" :disabled="loading" class="btn btn-sm btn-secondary" @click="load">نمایش</button>
    </div>
    <div v-if="balance" class="fn-tiles">
      <div v-for="t in tiles" :key="t[0]" :class="t[2]" class="fn-tile"><div class="t">{{ t[1] }}</div><div class="v">{{ days2(balance[t[0]]) }}</div></div>
    </div>
    <p v-if="balance" class="inv-muted small mt-2 mb-0">مانده قابل استفاده = مانده سال قبل + استحقاق - استفاده‌شده + استحقاق ماه‌های ثبت‌نشده - مرخصی تاییدشده ماه‌های ثبت‌نشده (سال {{ balance.year }}).</p>
  </div>
</template>
