<script setup lang="ts">
// Breakdown reports from the shop floor terminal: list, and open (or create) the maintenance order of a report.
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import { getMaintenanceOrders, getMaintenanceRequests } from "@/api/ProductionAPIs";
import { camelize, usePagedList } from "@/composables/fn";
import { fail } from "@/composables/inventory";
import { badgeOfMaintenance, maintenanceRequestStatuses, persianOf } from "@/composables/production";
import { hasRole, ProductionRoles } from "@/composables/roles";

const props = defineProps({ machines: { type: Array as () => any[], default: () => [] }, highlightId: { type: Number, default: null } });
const emit = defineEmits(["create"]);
const router = useRouter();
const filters = ref({ MachineId: null as number | null, Status: null as number | null });
const list = usePagedList(getMaintenanceRequests, filters);
const orderOfRequest = ref<Record<number, number>>({});
const canWrite = hasRole(ProductionRoles.maintenanceWriters);
const rows = computed(() => list.rows.value);

async function loadOrderMap() {
  const [data, error] = await getMaintenanceOrders({ Kind: 2, PageNumber: 1, PageSize: 500 });
  if (error) return fail(error);
  const d = camelize(data);
  const map: Record<number, number> = {};
  for (const o of Array.isArray(d) ? d : d?.data || []) if (o.requestId && o.status !== 3) map[o.requestId] = o.id;
  orderOfRequest.value = map;
}

async function reload() {
  await list.load();
  await loadOrderMap();
}
function openOrder(r: any) {
  const id = orderOfRequest.value[r.id];
  if (id) router.push("/production/maintenance-orders/" + id);
  else emit("create", r);
}

defineExpose({ reload });
onMounted(reload);
</script>

<template>
  <div>
    <div class="inv-filters mb-3">
      <select v-model="filters.MachineId" class="form-select form-select-sm">
        <option :value="null">همه ماشین‌ها</option>
        <option v-for="m in props.machines" :key="m.machineId" :value="m.machineId">{{ m.title }}</option>
      </select>
      <select v-model="filters.Status" class="form-select form-select-sm">
        <option :value="null">همه وضعیت‌ها</option>
        <option v-for="(t, k) in maintenanceRequestStatuses" :key="k" :value="Number(k)">{{ t }}</option>
      </select>
      <button class="btn btn-sm btn-secondary" @click="list.search().then(loadOrderMap)">جستجو</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>ماشین</th><th>زمان گزارش</th><th>شرح خرابی</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="6" class="text-center inv-muted">گزارش خرابی نیست.</td></tr>
          <tr v-for="r in rows" :key="r.id" :class="{ 'table-warning': r.id === props.highlightId }">
            <td class="inv-ltr">{{ r.number }}</td>
            <td>{{ r.machineTitle }}</td>
            <td>{{ persianOf(r.reportedAtText, r.reportedAt, true) }}</td>
            <td>{{ r.description }}</td>
            <td><span :class="badgeOfMaintenance(r.status)" class="inv-badge">{{ maintenanceRequestStatuses[r.status] }}</span></td>
            <td class="text-nowrap">
              <button v-if="orderOfRequest[r.id]" class="btn btn-sm btn-outline-primary" @click="openOrder(r)">باز کردن دستور</button>
              <button v-else-if="canWrite && r.status <= 1" class="btn btn-sm btn-primary" @click="openOrder(r)">ساخت دستور تعمیر</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
  </div>
</template>
