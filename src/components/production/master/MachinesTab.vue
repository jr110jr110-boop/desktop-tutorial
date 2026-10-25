<script setup lang="ts">
import { onMounted, ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import { getProductionMachines, saveMachineInfo } from "@/api/ProductionAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { badgeOfMachine, machineStatuses } from "@/composables/production";
import { ProductionRoles, hasRole } from "@/composables/roles";
import { assetLabel, blankToNull, loadWorkCenters, searchAssets } from "./masterKit";

// Machines: the machine record itself is kept by the equipment pages; here the production side-data (work centre, asset, maker, model, serial, capabilities, status) is edited.
const filters = ref({ WorkCenterId: null as number | null, Status: null as number | null, Contains: "" });
const list = usePagedList(getProductionMachines, filters);
const centers = ref<any[]>([]);
const form = ref<any>(null);
const asset = ref<any>(null);
const busy = ref(false);
const canEdit = hasRole(ProductionRoles.machineEditors);

function edit(m: any) {
  form.value = { machineId: m.machineId, title: m.title, workCenterId: m.workCenterId, assetId: m.assetId, maker: m.maker || "", model: m.model || "", serialNumber: m.serialNumber || "", capabilities: m.capabilities || "", status: m.status || 1 };
  asset.value = null;
}
function pickAsset(a: any) {
  asset.value = a;
  form.value.assetId = a ? a.id : null;
}

async function save() {
  const f = form.value;
  busy.value = true;
  const [, error] = await saveMachineInfo({ machineId: f.machineId, workCenterId: f.workCenterId, assetId: f.assetId, maker: blankToNull(f.maker), model: blankToNull(f.model), serialNumber: blankToNull(f.serialNumber), capabilities: blankToNull(f.capabilities), status: f.status });
  busy.value = false;
  if (error) return fail(error);
  ok("مشخصات ماشین ذخیره شد.");
  form.value = null;
  list.load();
}

onMounted(async () => {
  centers.value = await loadWorkCenters(false);
  list.load();
});
defineExpose({ reload: list.load });
</script>

<template>
  <div>
    <div class="inv-toolbar">
      <div class="inv-filters">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="عنوان، سازنده، مدل یا سریال" @keyup.enter="list.search" />
        <select v-model="filters.WorkCenterId" class="form-select form-select-sm"><option :value="null">همه مراکز کار</option><option v-for="c in centers" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option></select>
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in machineStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>ماشین</th><th>مرکز کار</th><th>سازنده</th><th>مدل</th><th>شماره سریال</th><th>توانایی‌ها</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="8" class="text-center inv-muted">ماشینی پیدا نشد.</td></tr>
          <tr v-for="m in list.rows.value" :key="m.machineId">
            <td>{{ m.title }} <span v-if="!m.isActive" class="inv-badge">غیرفعال</span></td><td class="inv-ltr">{{ m.workCenterCode || "-" }}</td><td>{{ m.maker || "-" }}</td><td>{{ m.model || "-" }}</td>
            <td class="inv-ltr">{{ m.serialNumber || "-" }}</td><td>{{ m.capabilities || "-" }}</td>
            <td><span :class="badgeOfMachine(m.status)" class="inv-badge">{{ machineStatuses[m.status] }}</span></td>
            <td><button v-if="canEdit" class="btn btn-sm btn-outline-primary" @click="edit(m)">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">مشخصات تولیدی ماشین - {{ form.title }}</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">مرکز کار</label><select v-model="form.workCenterId" class="form-select form-select-sm"><option :value="null">بدون مرکز کار</option><option v-for="c in centers" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">وضعیت</label><select v-model="form.status" class="form-select form-select-sm"><option v-for="(t, k) in machineStatuses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-12"><label class="form-label small mb-0">دارایی ثابت</label>
            <LookupBox :model-value="asset" :fetch="searchAssets" :label="assetLabel" :fallback-text="form.assetId ? 'دارایی ثبت‌شده (برای تغییر جستجو کنید)' : ''" placeholder="کد یا عنوان دارایی" @pick="pickAsset" />
          </div>
          <div class="col-md-4"><label class="form-label small mb-0">سازنده</label><input v-model="form.maker" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">مدل</label><input v-model="form.model" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">شماره سریال</label><input v-model="form.serialNumber" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-12"><label class="form-label small mb-0">توانایی‌ها (فرایندها، ابعاد قابل ساخت، ...)</label><textarea v-model="form.capabilities" class="form-control form-control-sm" rows="2"></textarea></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>
