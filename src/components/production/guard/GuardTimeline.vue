<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { getActivity, getGuardMachines } from "@/api/GuardAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail } from "@/composables/inventory";

// The time line of one machine, newest first: every change of mode, program state, override, program and tool the control reported, and every decision of the guard
// (program read back, tools checked, start refused, override used, interlock on/off, unapproved program, measurement read). The record a supervisor or an auditor reads.
const props = defineProps<{ machineId: number | null }>();
const emit = defineEmits<{ (e: "update:machineId", id: number | null): void }>();
const machineId = ref<number | null>(props.machineId);
watch(() => props.machineId, (id) => (machineId.value = id));
watch(machineId, (id) => emit("update:machineId", id));
const machines = ref<any[]>([]);
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const busy = ref(false);
const size = 60;

onMounted(async () => {
  const [d] = await getGuardMachines();
  machines.value = (Array.isArray(d) ? d : []).filter((m: any) => m.guarded || m.machineId === machineId.value);
  if (!machineId.value && machines.value.length) machineId.value = machines.value[0].machineId;
  load(true);
});
watch(machineId, () => load(true));

async function load(reset: boolean) {
  if (!machineId.value) return;
  if (reset) page.value = 1;
  busy.value = true;
  const [d, error] = await getActivity({ machineId: machineId.value, pageNumber: page.value, pageSize: size });
  busy.value = false;
  if (error) return fail(error);
  const list = rowsOf(d);
  rows.value = reset ? list : [...rows.value, ...list];
  total.value = d?.totalItems ?? rows.value.length;
}
function more() {
  page.value++;
  load(false);
}
</script>

<template>
  <div class="gd-panel">
    <div class="d-flex gap-2 align-items-end flex-wrap mb-3">
      <div><label class="form-label small mb-0">ماشین</label><select v-model.number="machineId" class="form-select form-select-sm"><option v-for="m in machines" :key="m.machineId" :value="m.machineId">{{ m.machineTitle }}</option></select></div>
      <button :disabled="busy" class="gd-btn" @click="load(true)">تازه کردن</button>
      <span class="gd-muted ms-auto">{{ total.toLocaleString("fa-IR") }} رویداد</span>
    </div>
    <div v-if="!machineId" class="gd-empty">ماشینی با اتصال داده تعریف نشده است.</div>
    <div v-else-if="!rows.length && !busy" class="gd-empty">هنوز رویدادی برای این ماشین ثبت نشده است.</div>
    <ol v-else class="tl">
      <li v-for="a in rows" :key="a.id" :class="a.tone" class="tl-row">
        <span :class="a.tone" class="gd-dot" aria-hidden="true"></span>
        <time class="tl-time">{{ a.atText }}</time>
        <span class="tl-kind">{{ a.kindText }}</span>
        <span class="tl-text">{{ a.text }}<span v-if="a.operationId" class="gd-muted"> · عملیات {{ a.operationId }}</span></span>
      </li>
    </ol>
    <div v-if="rows.length < total" class="text-center mt-2"><button :disabled="busy" class="gd-btn" @click="more">رویدادهای قدیمی‌تر</button></div>
  </div>
</template>

<style scoped>
.tl { list-style: none; margin: 0; padding: 0; position: relative; }
.tl::before { content: ""; position: absolute; inset-block: 6px; inset-inline-start: 4px; width: 1px; background: var(--rule); }
.tl-row { display: grid; grid-template-columns: 10px 128px 150px 1fr; gap: 10px; align-items: baseline; padding: 6px 0; border-bottom: 1px dashed var(--rule); font-size: 0.88rem; position: relative; }
.tl-row .gd-dot { position: relative; top: 1px; }
.tl-time { color: var(--muted); white-space: nowrap; }
.tl-kind { font-weight: 700; }
.tl-row.bad .tl-kind { color: var(--red); }
.tl-row.warn .tl-kind { color: var(--amber); }
.tl-row.ok .tl-kind { color: var(--green); }
@media (max-width: 700px) {
  .tl-row { grid-template-columns: 10px 1fr; }
  .tl-time, .tl-kind, .tl-text { grid-column: 2; }
}
</style>
