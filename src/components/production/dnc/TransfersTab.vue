<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import { getTransfers, runPendingTransfers } from "@/api/DncAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { DncRoles, transferDirections, transferStatuses } from "@/composables/dnc";
import { hasRole } from "@/composables/roles";
import TransfersTable from "./TransfersTable.vue";

// Every transfer, newest first. The queue runs by itself every few seconds on the server; a supervisor can run it now.
const route = useRoute();
const filters = ref({ MachineId: route.query.machineId ? Number(route.query.machineId) : (null as number | null), Status: null as number | null, Direction: null as number | null });
const list = usePagedList(getTransfers, filters);
const canRun = hasRole(DncRoles.supervisors);
const busy = ref(false);

async function runNow() {
  busy.value = true;
  const [d, error] = await runPendingTransfers();
  busy.value = false;
  if (error) return fail(error);
  ok(`پردازش شد: ${d.processed}، تحویل: ${d.delivered}، دریافت: ${d.received}، خطا: ${d.failed}` + (d.messages?.length ? " — " + d.messages.join(" ") : ""));
  list.load();
}
onMounted(list.load);
</script>

<template>
  <div>
    <div class="inv-toolbar">
      <div class="inv-filters">
        <div><label class="form-label small mb-0">وضعیت</label><select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in transferStatuses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div><label class="form-label small mb-0">جهت</label><select v-model="filters.Direction" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in transferDirections" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <button v-if="canRun" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="runNow">اجرای صف انتقال</button>
    </div>
    <TransfersTable :rows="list.rows.value" show-program @changed="list.load" />
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
  </div>
</template>
