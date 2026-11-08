<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { useRouter } from "vue-router";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import { getPrograms, saveProgram } from "@/api/DncAPIs";
import { getOperationStandards, getProductionMachines } from "@/api/ProductionAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize, usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { DncRoles, controllerFamilies, programStatuses } from "@/composables/dnc";
import { hasRole } from "@/composables/roles";
import { itemLabel, loadWorkCenters, searchItems } from "@/components/production/master/masterKit";
import ProgramDetail from "./ProgramDetail.vue";

// The CNC program library. /production/dnc/programs/:id opens a program (the inbox link of a revision waiting for approval).
const props = defineProps<{ openId: number | null }>();
const router = useRouter();
const filters = ref({ Status: 1 as number | null, Pending: null as boolean | null, Contains: "" });
const list = usePagedList(getPrograms, filters);
const canWrite = hasRole(DncRoles.writers);
const centres = ref<any[]>([]);
const machines = ref<any[]>([]);
const steps = ref<any[]>([]);
const form = ref<any>(null);
const busy = ref(false);

function newForm() {
  form.value = { item: null, code: "", title: "", operationTemplateId: null, workCenterId: null, machineId: null, controllerFamily: 0, note: "" };
  steps.value = [];
}
async function pickItem(i: any) {
  form.value.item = i;
  form.value.operationTemplateId = null;
  steps.value = [];
  if (!i) return;
  const [d] = await getOperationStandards({ ItemId: i.id, PageSize: 200 });
  steps.value = rowsOf(camelize(d));
}
watch(() => form.value?.operationTemplateId, (id) => {
  const s = steps.value.find((x) => x.operationTemplateId === id);
  if (s?.workCenterId && form.value) form.value.workCenterId = s.workCenterId;
});

async function create() {
  const f = form.value;
  if (!f.code.trim() || !f.title.trim()) return fail("کد و عنوان برنامه را وارد کنید.");
  busy.value = true;
  const [d, error] = await saveProgram({
    code: f.code.trim(), title: f.title.trim(), itemId: f.item?.id ?? null, operationTemplateId: f.operationTemplateId, workCenterId: f.workCenterId, machineId: f.machineId,
    controllerFamily: f.controllerFamily, status: 1, note: f.note.trim() || null,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("برنامه ثبت شد؛ اکنون نسخه اول را بارگذاری کنید.");
  form.value = null;
  router.push({ name: "MfDncProgram", params: { id: d.id } });
}

function open(r: any) {
  router.push({ name: "MfDncProgram", params: { id: r.id } });
}
function back() {
  router.push({ name: "MfDnc" });
  list.load();
}

onMounted(async () => {
  list.load();
  centres.value = await loadWorkCenters();
  const [m] = await getProductionMachines({ PageSize: 500 });
  machines.value = rowsOf(camelize(m));
});
</script>

<template>
  <div>
    <ProgramDetail v-if="openId" :id="openId" @back="back" />
    <template v-else>
      <div class="inv-toolbar">
        <div class="inv-filters">
          <div><label class="form-label small mb-0">وضعیت</label><select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in programStatuses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="form-check mt-3"><input id="dnc-pending" v-model="filters.Pending" :false-value="null" :true-value="true" class="form-check-input" type="checkbox" /><label class="form-check-label small" for="dnc-pending">فقط منتظر تأیید</label></div>
          <div><label class="form-label small mb-0">کد یا عنوان</label><input v-model="filters.Contains" class="form-control form-control-sm" @keyup.enter="list.search" /></div>
          <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
        </div>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="newForm">برنامه تازه</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>کد</th><th>عنوان</th><th>قطعه / مرحله</th><th>مرکز کار / ماشین</th><th>کنترلر</th><th>نسخه جاری</th><th>منتظر تأیید</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="9" class="text-center inv-muted">برنامه‌ای پیدا نشد.</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id">
              <td class="inv-ltr">{{ r.code }}</td><td>{{ r.title }}</td>
              <td>{{ r.itemCode ? r.itemCode + " - " + (r.itemTitle || "") : "-" }}<div v-if="r.operationTitle" class="inv-muted">{{ r.operationTitle }}</div></td>
              <td class="inv-ltr">{{ r.workCenterCode || "-" }}<div v-if="r.machineTitle" class="inv-muted">{{ r.machineTitle }}</div></td>
              <td>{{ controllerFamilies[r.controllerFamily] }}</td>
              <td>{{ r.currentRevision ?? "-" }}<div v-if="r.currentApprovedAtText" class="inv-muted">{{ r.currentApprovedAtText }}</div></td>
              <td><span v-if="r.pendingRevisions" class="inv-badge info">{{ r.pendingRevisions }}</span><span v-else>-</span></td>
              <td><span :class="r.status === 1 ? 'ok' : ''" class="inv-badge">{{ programStatuses[r.status] }}</span></td>
              <td><button class="btn btn-sm btn-outline-primary" @click="open(r)">مشاهده</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </template>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal" style="max-width: 640px">
        <h3 class="inv-title mb-3">برنامه CNC تازه</h3>
        <div class="row g-2 small">
          <div class="col-md-4"><label class="form-label mb-0">کد برنامه</label><input v-model="form.code" class="form-control form-control-sm inv-ltr" placeholder="P-100" /></div>
          <div class="col-md-8"><label class="form-label mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-12"><label class="form-label mb-0">قطعه</label><LookupBox :model-value="form.item" :fetch="searchItems" :label="itemLabel" placeholder="کد یا نام قطعه" @pick="pickItem" /></div>
          <div class="col-md-12"><label class="form-label mb-0">مرحله مسیر ساخت</label><select v-model="form.operationTemplateId" :disabled="!steps.length" class="form-select form-select-sm"><option :value="null">{{ steps.length ? "انتخاب کنید" : "ابتدا قطعه را انتخاب کنید" }}</option><option v-for="s in steps" :key="s.operationTemplateId" :value="s.operationTemplateId">{{ s.sequence }} - {{ s.templateTitle }}{{ s.workCenterCode ? " (" + s.workCenterCode + ")" : "" }}</option></select></div>
          <div class="col-md-6"><label class="form-label mb-0">مرکز کار</label><select v-model="form.workCenterId" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="c in centres" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option></select></div>
          <div class="col-md-6"><label class="form-label mb-0">فقط برای ماشین</label><select v-model="form.machineId" class="form-select form-select-sm"><option :value="null">هر ماشین هم‌خانواده مرکز کار</option><option v-for="m in machines.filter((x) => !form.workCenterId || x.workCenterId === form.workCenterId)" :key="m.machineId" :value="m.machineId">{{ m.title }}</option></select></div>
          <div class="col-md-6"><label class="form-label mb-0">خانواده کنترلر</label><select v-model.number="form.controllerFamily" class="form-select form-select-sm"><option v-for="(t, k) in controllerFamilies" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-12"><label class="form-label mb-0">توضیح</label><input v-model="form.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="create">ثبت</button></div>
      </div>
    </div>
  </div>
</template>
