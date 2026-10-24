<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import DelegationForm from "@/components/office/wf/DelegationForm.vue";
import DelegationTable from "@/components/office/wf/DelegationTable.vue";
import OrgPositions from "@/components/office/wf/OrgPositions.vue";
import OrgTreeNode from "@/components/office/wf/OrgTreeNode.vue";
import OrgUnits from "@/components/office/wf/OrgUnits.vue";
import { getMyOffice, getOrgTree } from "@/api/OfficeAPIs";
import { fail } from "@/composables/inventory";
import { OfficeRoles, hasRole } from "@/composables/roles";

// Organisation: chart, units, positions and delegations (/office/delegations opens the last tab). Everyone reads; HR, PMO, CEO write.
const route = useRoute();
const tab = ref<"tree" | "units" | "positions" | "delegations">(route.path.endsWith("/delegations") ? "delegations" : "tree");
const canWrite = hasRole(OfficeRoles.orgAdmins);
const tree = ref<any[]>([]);
const myPositionIds = ref<number[]>([]);
const form = ref(false);
const table = ref<any>(null);

async function loadTree() {
  const [d, error] = await getOrgTree();
  if (error) return fail(error);
  tree.value = Array.isArray(d) ? d : d ? [d] : [];
}
onMounted(async () => {
  loadTree();
  const [me] = await getMyOffice();
  myPositionIds.value = (me?.positions || []).filter((p: any) => !p.onBehalf).map((p: any) => p.positionId);
});
function saved() {
  form.value = false;
  table.value?.reload();
}
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">ساختار سازمانی و نیابت</h1>
        <button v-if="tab === 'delegations'" class="btn btn-sm btn-primary" @click="form = true">ثبت نیابت</button>
      </div>
      <ul class="nav nav-tabs mb-3">
        <li v-for="t in [['tree', 'چارت سازمانی'], ['units', 'واحدها'], ['positions', 'سمت‌ها'], ['delegations', 'نیابت‌ها']]" :key="t[0]" class="nav-item">
          <a :class="{ active: tab === t[0] }" class="nav-link" href="#" @click.prevent="tab = t[0] as any">{{ t[1] }}</a>
        </li>
      </ul>

      <div v-if="tab === 'tree'">
        <p v-if="!tree.length" class="inv-muted">چارتی ثبت نشده است.</p>
        <OrgTreeNode v-for="n in tree" :key="n.id" :node="n" />
      </div>
      <OrgUnits v-else-if="tab === 'units'" :can-write="canWrite" @changed="loadTree" />
      <OrgPositions v-else-if="tab === 'positions'" :can-write="canWrite" @changed="loadTree" />
      <DelegationTable v-else ref="table" />
    </div>
    <DelegationForm v-if="form" :only="canWrite ? null : myPositionIds" @close="form = false" @saved="saved" />
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
