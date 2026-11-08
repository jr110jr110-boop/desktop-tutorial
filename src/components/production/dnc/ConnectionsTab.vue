<script setup lang="ts">
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import { getConnections, testConnection } from "@/api/DncAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { DncRoles, controllerFamilies, linkKinds } from "@/composables/dnc";
import { machineStatuses } from "@/composables/production";
import { hasRole } from "@/composables/roles";
import ConnectionEditor from "./ConnectionEditor.vue";

// The DNC link of every machine (one row per machine, also those without a link yet).
const filters = ref({ LinkKind: null as number | null, Contains: "" });
const list = usePagedList(getConnections, filters);
const editing = ref<any>(null);
const testing = ref<number | null>(null);
const canEdit = hasRole(DncRoles.linkEditors);
const canTest = hasRole(DncRoles.linkTesters);

async function test(r: any) {
  testing.value = r.machineId;
  const [d, error] = await testConnection(r.machineId);
  testing.value = null;
  if (error) return fail(error);
  d.ok ? ok(d.message) : fail(d.message);
}
function saved() {
  editing.value = null;
  list.load();
}
onMounted(list.load);
</script>

<template>
  <div>
    <div class="inv-toolbar">
      <div class="inv-filters">
        <div><label class="form-label small mb-0">نوع اتصال</label><select v-model="filters.LinkKind" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in linkKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div><label class="form-label small mb-0">ماشین</label><input v-model="filters.Contains" class="form-control form-control-sm" placeholder="نام ماشین" @keyup.enter="list.search" /></div>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>ماشین</th><th>مرکز کار</th><th>وضعیت</th><th>کنترلر</th><th>اتصال</th><th>نشانی / مسیر</th><th>محورها</th><th>دور / ابزار</th><th>گیت شروع</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="10" class="text-center inv-muted">ماشینی پیدا نشد.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.machineId">
            <td>{{ r.machineTitle }}<span v-if="!r.machineIsActive" class="inv-badge ms-1">غیرفعال</span></td>
            <td class="inv-ltr">{{ r.workCenterCode || "-" }}</td>
            <td>{{ r.machineStatus ? machineStatuses[r.machineStatus] : "-" }}</td>
            <td>{{ r.id ? controllerFamilies[r.controllerFamily] : "-" }}<div v-if="r.controllerModel" class="inv-muted inv-ltr">{{ r.controllerModel }}</div></td>
            <td><span :class="r.id ? 'info' : ''" class="inv-badge">{{ r.id ? linkKinds[r.linkKind] : "تعریف نشده" }}</span><div v-if="r.linkKind === 5 && r.lastSeenAtText" class="inv-muted">آخرین تماس: {{ r.lastSeenAtText }}</div></td>
            <td class="inv-ltr small">{{ r.host ? r.host + (r.port ? ":" + r.port : "") : r.remotePath || "-" }}</td>
            <td class="inv-ltr">{{ r.axes || "-" }}</td>
            <td class="inv-ltr">{{ r.maxSpindleRpm || "-" }} / {{ r.toolCapacity || "-" }}</td>
            <td>{{ r.id ? (r.requireConfirmedProgram ? "بله" : "خیر") : "-" }}</td>
            <td class="text-nowrap">
              <button v-if="canEdit" class="btn btn-sm btn-outline-primary" @click="editing = r">{{ r.id ? "ویرایش" : "تعریف اتصال" }}</button>
              <button v-if="canTest && r.id" :disabled="testing === r.machineId" class="btn btn-sm btn-outline-secondary ms-1" @click="test(r)">آزمون</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    <ConnectionEditor v-if="editing" :row="editing" @close="editing = null" @saved="saved" />
  </div>
</template>
