<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { getActivities, getProgress, getWbs } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali } from "@/composables/inventory";
import { activityStatuses, pct, phases, unitText } from "@/composables/projectControl";

// Physical progress (weighted) of the project: by phase and work order (from the API) and, computed here from the same weights, by WBS node and by activity.
const props = defineProps({ contractId: { type: Number, required: true } });
const p = ref<any>(null);
const nodes = ref<any[]>([]);
const acts = ref<any[]>([]);
const view = ref("node");
const text = ref("");

async function load() {
  p.value = null;
  const [[d, e1], [n], [a]] = await Promise.all([getProgress(props.contractId), getWbs({ ContractId: props.contractId }), getActivities({ ContractId: props.contractId })]);
  if (e1) return fail(e1);
  p.value = d;
  nodes.value = rowsOf(n);
  acts.value = rowsOf(a);
}

/** Percent of a node = weighted average of its children (nodes and activities), weights are the sibling weights. */
const nodeRows = computed(() => {
  const ids = new Set(nodes.value.map((n) => n.id));
  const kids = new Map<number | null, any[]>();
  nodes.value.forEach((n) => {
    const k = n.parentId && ids.has(n.parentId) ? n.parentId : null;
    kids.set(k, [...(kids.get(k) || []), n]);
  });
  const actsBy = new Map<number, any[]>();
  acts.value.forEach((a) => a.wbsNodeId && ids.has(a.wbsNodeId) && actsBy.set(a.wbsNodeId, [...(actsBy.get(a.wbsNodeId) || []), a]));
  const memo = new Map<number, number>();
  const calc = (n: any): number => {
    if (memo.has(n.id)) return memo.get(n.id)!;
    const items: { w: number; v: number }[] = [];
    (kids.get(n.id) || []).forEach((c) => items.push({ w: Number(c.weight) || 0, v: calc(c) }));
    (actsBy.get(n.id) || []).forEach((a) => items.push({ w: Number(a.weight) || 0, v: Number(a.percentCache) || 0 }));
    const tw = items.reduce((s, i) => s + i.w, 0);
    const v = tw > 0 ? items.reduce((s, i) => s + i.w * i.v, 0) / tw : items.length ? items.reduce((s, i) => s + i.v, 0) / items.length : 0;
    memo.set(n.id, v);
    return v;
  };
  const out: any[] = [];
  const walk = (parent: number | null, depth: number) => {
    [...(kids.get(parent) || [])].sort((a, b) => String(a.code).localeCompare(String(b.code), "en", { numeric: true })).forEach((n) => {
      out.push({ n, depth, percent: calc(n) });
      walk(n.id, depth + 1);
    });
  };
  walk(null, 0);
  return out;
});
const actRows = computed(() => {
  const t = text.value.trim().toLowerCase();
  return acts.value.filter((a) => (t ? `${a.code} ${a.title}`.toLowerCase().includes(t) : true)).sort((a, b) => a.sort - b.sort);
});

onMounted(load);
watch(() => props.contractId, load);
</script>

<template>
  <div v-if="p">
    <div class="row g-2 mb-3">
      <div class="col-md-3"><div class="border rounded p-2"><div class="inv-muted">پیشرفت فیزیکی واقعی</div><b class="fs-5">{{ pct(p.physicalPercent) }}</b></div></div>
      <div class="col-md-3"><div class="border rounded p-2"><div class="inv-muted">پیشرفت برنامه‌ای تا {{ jalali(p.asOf) }}</div><b class="fs-5">{{ pct(p.plannedPercent) }}</b></div></div>
      <div class="col-md-2"><div class="border rounded p-2"><div class="inv-muted">فعالیت‌ها</div><b>{{ p.doneActivities }} / {{ p.activities }}</b> انجام شد</div></div>
      <div class="col-md-2"><div class="border rounded p-2"><div class="inv-muted">فعالیت‌های تاخیردار</div><b :class="{ 'text-danger': p.lateActivities }">{{ p.lateActivities }}</b></div></div>
      <div class="col-md-2"><div class="border rounded p-2"><div class="inv-muted">اختلاف با برنامه</div><b :class="{ 'text-danger': p.physicalPercent < p.plannedPercent }">{{ pct(p.physicalPercent - p.plannedPercent) }}</b></div></div>
    </div>
    <div class="row g-3">
      <div class="col-lg-6">
        <h2 class="inv-title mb-2">به تفکیک فاز</h2>
        <table class="table table-sm">
          <thead><tr><th>فاز</th><th>واقعی</th><th>برنامه</th><th>فعالیت</th></tr></thead>
          <tbody>
            <tr v-for="(r, i) in p.byPhase" :key="i">
              <td>{{ phases[r.phase] || "بدون فاز" }}</td>
              <td style="min-width: 150px"><div class="pbar"><div :style="{ width: Math.min(100, r.percent) + '%' }"></div><i :style="{ right: Math.min(100, r.plannedPercent) + '%' }"></i></div>{{ pct(r.percent) }}</td>
              <td>{{ pct(r.plannedPercent) }}</td><td>{{ r.activities }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="col-lg-6">
        <h2 class="inv-title mb-2">به تفکیک دستور ساخت</h2>
        <table class="table table-sm">
          <thead><tr><th>دستور ساخت</th><th>پیشرفت</th><th>فعالیت</th></tr></thead>
          <tbody>
            <tr v-for="(r, i) in p.byWorkOrder" :key="i"><td class="inv-ltr">{{ r.workOrderCode || "—" }}</td><td>{{ pct(r.percent) }}</td><td>{{ r.activities }}</td></tr>
          </tbody>
        </table>
      </div>
    </div>

    <ul class="nav nav-pills mb-2 mt-2 d-print-none">
      <li class="nav-item"><a :class="{ active: view === 'node' }" class="nav-link py-1" href="#" @click.prevent="view = 'node'">به تفکیک گره WBS</a></li>
      <li class="nav-item"><a :class="{ active: view === 'act' }" class="nav-link py-1" href="#" @click.prevent="view = 'act'">به تفکیک فعالیت</a></li>
    </ul>
    <table v-if="view === 'node'" class="table table-sm">
      <thead><tr><th>گره</th><th>عنوان</th><th>وزن</th><th>پیشرفت (میانگین وزنی زیرمجموعه)</th></tr></thead>
      <tbody>
        <tr v-if="!nodeRows.length"><td colspan="4" class="text-center inv-muted">WBS ساخته نشده است.</td></tr>
        <tr v-for="r in nodeRows" :key="r.n.id">
          <td class="inv-ltr" :style="{ paddingRight: 8 + r.depth * 16 + 'px' }">{{ r.n.code }}</td><td>{{ r.n.title }}</td><td>{{ Number(r.n.weight).toFixed(1) }}</td>
          <td style="min-width: 180px"><div class="pbar"><div :style="{ width: Math.min(100, r.percent) + '%' }"></div></div>{{ pct(r.percent) }}</td>
        </tr>
      </tbody>
    </table>
    <div v-else>
      <input v-model="text" class="form-control form-control-sm mb-2 d-print-none" style="max-width: 260px" placeholder="کد یا عنوان" />
      <table class="table table-sm">
        <thead><tr><th>کد</th><th>عنوان</th><th>واحد</th><th>وزن</th><th>پیشرفت</th><th>وضعیت</th></tr></thead>
        <tbody>
          <tr v-for="a in actRows" :key="a.id">
            <td class="inv-ltr">{{ a.code }}</td><td>{{ a.title }} <span v-if="a.isLate" class="inv-badge warn">تاخیر</span></td><td>{{ unitText(a.responsibleUnit) }}</td><td>{{ Number(a.weight).toFixed(1) }}</td>
            <td style="min-width: 150px"><div class="pbar"><div :style="{ width: Math.min(100, a.percentCache) + '%' }"></div></div>{{ pct(a.percentCache) }}</td><td>{{ activityStatuses[a.status] }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <p class="inv-muted mb-0">درصد گره‌ها در همین صفحه از وزن فعالیت‌ها محاسبه شده است؛ پیشرفت کل پروژه را API محاسبه می‌کند.</p>
  </div>
</template>

<style scoped>
.pbar { position: relative; height: 6px; background: #e9ecef; border-radius: 3px; margin-bottom: 2px; }
.pbar > div { height: 100%; background: #198754; border-radius: 3px; }
.pbar > i { position: absolute; top: -2px; width: 2px; height: 10px; background: #0d6efd; }
</style>
