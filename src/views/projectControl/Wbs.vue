<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import WorkOrderOfContract from "@/components/pc/WorkOrderOfContract.vue";
import { approveWbsNode, autoWeights, deleteWbsNode, generateWbs, getActivities, getWbs, saveWbsNode } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok, printPage, rial } from "@/composables/inventory";
import { pcContractId, phases, unitOptions, unitText, wbsTypes } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// WBS tree: generated from the work orders, BOM and processes; weights of the children of a node add up to 100;
// a node is approved (with its dictionary text) by the project manager or the PMO deputy.
const nodes = ref<any[]>([]);
const acts = ref<any[]>([]);
const loading = ref(false);
const busy = ref(false);
const open = ref<Record<number, boolean>>({});
const edit = ref<any>(null);
const genResult = ref<any>(null);
const genOrders = ref<number | null>(null);

const canGenerate = hasRole(ProjectControlRoles.planning);
const canEdit = hasRole(ProjectControlRoles.planningOrPm);
const canApprove = hasRole(ProjectControlRoles.projectManagers);

async function load() {
  if (!pcContractId.value) return;
  loading.value = true;
  const [[n, e1], [a, e2]] = await Promise.all([getWbs({ ContractId: pcContractId.value }), getActivities({ ContractId: pcContractId.value })]);
  loading.value = false;
  if (e1) return fail(e1);
  if (e2) return fail(e2);
  nodes.value = rowsOf(n);
  acts.value = rowsOf(a);
  if (!Object.keys(open.value).length) nodes.value.filter((x) => !x.parentId).forEach((x) => (open.value[x.id] = true));
}

interface Row { kind: "node" | "act"; item: any; depth: number; hasChildren: boolean; siblingSum?: number }

const rows = computed<Row[]>(() => {
  const byParent = new Map<number | null, any[]>();
  const ids = new Set(nodes.value.map((n) => n.id));
  nodes.value.forEach((n) => {
    const p = n.parentId && ids.has(n.parentId) ? n.parentId : null;
    byParent.set(p, [...(byParent.get(p) || []), n]);
  });
  const actsBy = new Map<number, any[]>();
  acts.value.forEach((a) => {
    if (a.wbsNodeId && ids.has(a.wbsNodeId)) actsBy.set(a.wbsNodeId, [...(actsBy.get(a.wbsNodeId) || []), a]);
  });
  const out: Row[] = [];
  const sortNodes = (l: any[]) => [...l].sort((x, y) => (x.sort - y.sort) || String(x.code).localeCompare(String(y.code), "en", { numeric: true }));
  const walk = (parent: number | null, depth: number) => {
    const list = sortNodes(byParent.get(parent) || []);
    list.forEach((n) => {
      const childNodes = byParent.get(n.id) || [];
      const childActs = actsBy.get(n.id) || [];
      const sum = childNodes.reduce((s, c) => s + Number(c.weight || 0), 0) + childActs.reduce((s, c) => s + Number(c.weight || 0), 0);
      out.push({ kind: "node", item: n, depth, hasChildren: childNodes.length + childActs.length > 0, siblingSum: childNodes.length + childActs.length ? sum : undefined });
      if (open.value[n.id]) {
        walk(n.id, depth + 1);
        [...childActs].sort((x, y) => x.sort - y.sort).forEach((a) => out.push({ kind: "act", item: a, depth: depth + 1, hasChildren: false }));
      }
    });
  };
  walk(null, 0);
  return out;
});

const rootSum = computed(() => nodes.value.filter((n) => !n.parentId).reduce((s, n) => s + Number(n.weight || 0), 0));
const orphanActs = computed(() => acts.value.filter((a) => !a.wbsNodeId).length);

function blank(parent?: any) {
  return { id: null, contractId: pcContractId.value, parentId: parent?.id ?? null, code: "", title: "", nodeType: parent ? Math.min(7, (parent.nodeType || 1) + 1) : 1, phase: null, workOrderId: null, weight: 0, budgetAmount: 0, dictionary: "", responsibleUnit: "", sort: 0 };
}

async function save() {
  busy.value = true;
  const [, error] = await saveWbsNode({ ...edit.value, phase: edit.value.phase || null, workOrderId: edit.value.workOrderId || null, responsibleUnit: edit.value.responsibleUnit || null });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  edit.value = null;
  load();
}
async function remove(n: any) {
  if (!confirm(`گره ${n.code} حذف شود؟`)) return;
  const [, error] = await deleteWbsNode(n.id);
  if (error) return fail(error);
  ok("حذف شد.");
  load();
}
async function approve(n: any) {
  const [, error] = await approveWbsNode(n.id);
  if (error) return fail(error);
  ok("تایید شد.");
  load();
}
async function generate() {
  if (!confirm("WBS از دستورهای ساخت، BOM و فرآیندها ساخته شود؟")) return;
  busy.value = true;
  const [data, error] = await generateWbs(pcContractId.value!, genOrders.value ? [genOrders.value] : undefined);
  busy.value = false;
  if (error) return fail(error);
  genResult.value = data;
  ok("WBS ساخته شد.");
  load();
}
async function weights() {
  if (!confirm("وزن همه گره‌ها و فعالیت‌ها دوباره محاسبه شود (بر اساس بودجه، سپس مدت)؟ وزن‌های دستی از بین می‌رود.")) return;
  busy.value = true;
  const [data, error] = await autoWeights(pcContractId.value!);
  busy.value = false;
  if (error) return fail(error);
  ok(`وزن ${data} مورد محاسبه شد.`);
  load();
}

onMounted(load);
watch(pcContractId, () => { open.value = {}; genResult.value = null; load(); });
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="ساختار شکست کار (WBS)" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">ساختار شکست کار (WBS)</h1>
        <div class="d-flex flex-wrap gap-2 d-print-none">
          <ContractSelect />
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <div class="d-flex flex-wrap gap-2 align-items-end mb-3 d-print-none">
          <template v-if="canGenerate">
            <div style="min-width: 200px"><WorkOrderOfContract v-model="genOrders" :contract-id="pcContractId" placeholder="همه دستورهای ساخت" /></div>
            <button :disabled="busy" class="btn btn-sm btn-primary" @click="generate">ساخت WBS از دستور ساخت، BOM و فرآیند</button>
          </template>
          <button v-if="canEdit" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="weights">محاسبه خودکار وزن‌ها</button>
          <button v-if="canEdit" class="btn btn-sm btn-outline-success" @click="edit = blank()">گره ریشه جدید</button>
          <button class="btn btn-sm btn-outline-secondary" @click="nodes.forEach((n) => (open[n.id] = true))">باز کردن همه</button>
          <button class="btn btn-sm btn-outline-secondary" @click="open = {}">بستن همه</button>
        </div>
        <div v-if="genResult" class="alert alert-info py-2 d-print-none">
          {{ genResult.nodesCreated }} گره، {{ genResult.activitiesCreated }} فعالیت، {{ genResult.dependenciesCreated }} وابستگی و {{ genResult.mdrLinesDrafted }} ردیف MDR ساخته شد
          <span v-if="genResult.workOrdersSkipped"> ({{ genResult.workOrdersSkipped }} دستور ساخت رد شد)</span>.
          <div v-for="(t, i) in genResult.notes" :key="i" class="small">{{ t }}</div>
        </div>
        <p v-if="orphanActs" class="inv-muted">{{ orphanActs }} فعالیت به هیچ گره WBS وصل نیست.</p>

        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>کد</th><th>عنوان</th><th>نوع</th><th>فاز</th><th>واحد</th><th>وزن (٪)</th><th>بودجه</th><th>تایید</th><th class="d-print-none"></th></tr></thead>
            <tbody>
              <tr v-if="!rows.length"><td colspan="9" class="text-center inv-muted">{{ loading ? "در حال بارگذاری..." : "گره‌ای نیست؛ WBS را بسازید." }}</td></tr>
              <tr v-for="r in rows" :key="r.kind + r.item.id" :class="{ 'text-muted': r.kind === 'act' }">
                <td class="inv-ltr" :style="{ paddingRight: 8 + r.depth * 18 + 'px' }">
                  <a v-if="r.kind === 'node' && r.hasChildren" href="#" class="text-decoration-none" @click.prevent="open[r.item.id] = !open[r.item.id]">{{ open[r.item.id] ? "▾" : "◂" }}</a>
                  <span v-else>&nbsp;&nbsp;</span>
                  {{ r.item.code }}
                </td>
                <td>{{ r.item.title }}<span v-if="r.kind === 'act'" class="inv-muted"> (فعالیت)</span></td>
                <td>{{ r.kind === "node" ? wbsTypes[r.item.nodeType] : "" }}</td>
                <td>{{ phases[r.item.phase] }}</td>
                <td>{{ unitText(r.item.responsibleUnit) }}</td>
                <td>
                  {{ Number(r.item.weight).toFixed(2) }}
                </td>
                <td class="text-nowrap">
                  <template v-if="r.kind === 'node'">{{ rial(r.item.budgetAmount) }}</template>
                  <template v-else>{{ rial(r.item.budgetAtCompletion) }}</template>
                  <span v-if="r.kind === 'node' && r.siblingSum !== undefined" :class="Math.abs(r.siblingSum - 100) < 0.05 ? 'ok' : 'warn'" class="inv-badge ms-1" title="مجموع وزن زیرمجموعه‌ها">زیرمجموعه: {{ r.siblingSum.toFixed(1) }}</span>
                </td>
                <td>
                  <template v-if="r.kind === 'node'">
                    <span v-if="r.item.approvedAt" class="inv-badge ok">{{ jalali(r.item.approvedAt) }}</span>
                    <span v-else-if="r.item.isGenerated" class="inv-badge info">پیش‌نویس سیستم</span>
                  </template>
                </td>
                <td class="d-print-none text-nowrap">
                  <template v-if="r.kind === 'node'">
                    <button v-if="canEdit" class="btn btn-sm btn-outline-secondary" @click="edit = { ...r.item }">ویرایش</button>
                    <button v-if="canEdit" class="btn btn-sm btn-outline-secondary ms-1" @click="edit = blank(r.item)">زیرگره</button>
                    <button v-if="canApprove && !r.item.approvedAt" class="btn btn-sm btn-outline-success ms-1" @click="approve(r.item)">تایید</button>
                    <button v-if="canEdit" class="btn btn-sm btn-outline-danger ms-1" @click="remove(r.item)">حذف</button>
                  </template>
                </td>
              </tr>
            </tbody>
            <tfoot v-if="nodes.length"><tr><td colspan="5">مجموع وزن گره‌های ریشه</td><td colspan="4"><span :class="Math.abs(rootSum - 100) < 0.05 ? 'ok' : 'warn'" class="inv-badge">{{ rootSum.toFixed(2) }}</span></td></tr></tfoot>
          </table>
        </div>
        <p class="inv-muted mb-0">مجموع وزن زیرمجموعه‌های هر گره (گره‌ها و فعالیت‌ها) باید ۱۰۰ باشد؛ محاسبه خودکار بر اساس بودجه، و اگر بودجه نبود بر اساس مدت انجام می‌شود. تایید گره با نوشتن فرهنگ لغت WBS گره و توسط مدیر پروژه یا معاون PMO انجام می‌شود.</p>
      </template>
    </div>

    <div v-if="edit" class="inv-modal-backdrop d-print-none" @click.self="edit = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">{{ edit.id ? "ویرایش گره WBS" : "گره WBS جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">کد *</label><input v-model="edit.code" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-8"><label class="form-label small mb-0">عنوان *</label><input v-model="edit.title" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نوع</label><select v-model.number="edit.nodeType" class="form-select form-select-sm"><option v-for="(n, k) in wbsTypes" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">فاز</label><select v-model="edit.phase" class="form-select form-select-sm"><option :value="null">-</option><option v-for="(n, k) in phases" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">واحد مسئول</label><select v-model="edit.responsibleUnit" class="form-select form-select-sm"><option value="">-</option><option v-for="u in unitOptions" :key="u[0]" :value="u[0]">{{ u[1] }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">گره والد</label>
            <select v-model="edit.parentId" class="form-select form-select-sm"><option :value="null">— ریشه —</option><option v-for="n in nodes.filter((x) => x.id !== edit.id)" :key="n.id" :value="n.id">{{ n.code }} {{ n.title }}</option></select>
          </div>
          <div class="col-md-6"><label class="form-label small mb-0">دستور ساخت</label><WorkOrderOfContract v-model="edit.workOrderId" :contract-id="pcContractId" placeholder="-" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">وزن در والد (٪)</label><input v-model.number="edit.weight" class="form-control form-control-sm" type="number" min="0" max="100" step="0.01" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">بودجه</label><input v-model.number="edit.budgetAmount" class="form-control form-control-sm" type="number" min="0" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">ترتیب</label><input v-model.number="edit.sort" class="form-control form-control-sm" type="number" /></div>
          <div class="col-12"><label class="form-label small mb-0">فرهنگ لغت WBS (برای تایید لازم است)</label><textarea v-model="edit.dictionary" class="form-control form-control-sm" rows="3"></textarea></div>
        </div>
        <div class="d-flex gap-2 mt-3">
          <button :disabled="busy || !edit.code || !edit.title" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
          <button class="btn btn-sm btn-outline-secondary" @click="edit = null">انصراف</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
