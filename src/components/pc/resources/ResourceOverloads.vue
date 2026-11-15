<script setup lang="ts">
// «بیش‌تخصیص‌ها»: the over-allocations of the resources (a run of working days on which the approved plans of all projects load a resource beyond its capacity). An over-allocation stays open until a change of the plan or the schedule
// resolves it, or the PMO accepts it on purpose with a reason. The planning agent proposes a move of one activity for each; the planner applies or rejects the proposal (the dates change through the schedule), a second person (PMO)
// confirms it, takes it back, or closes it when taking it back is impossible. Every button is gated by the SERVER: it comes disabled with the server's reason. The links of the gate inbox open this tab with the one
// over-allocation or proposal fetched by its id and shown first; the list below is paged (the open ones by default).
import { computed, onMounted, ref, watch } from "vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import type { PromptField } from "@/components/fn/PromptModal.vue";
import { acceptOverload, getOverload, getOverloads, getProposal, getResources, proposalAction } from "@/api/ResourceAPIs";
import type { LevelingProposal, Resource, ResourceOverload } from "@/api/ResourceAPIs";
import { fail, ok } from "@/composables/inventory";
import {
  CONSTRAINT_NOTE, faDate, faDateTime, loadNum, overloadAcceptGate, overloadStatusBadge, overloadStatuses, PENDING_SECOND, proposalApplyGate, proposalCloseGate, proposalConfirmGate, proposalKinds,
  proposalRejectGate, proposalStatuses, resourceLabel, resourceUnits,
} from "@/composables/resources";

const props = defineProps({ overloadId: { type: Number, default: null }, proposalId: { type: Number, default: null }, resourceId: { type: Number, default: null } });
const emit = defineEmits<{ (e: "load", resourceId: number): void }>();

const PAGE_SIZE = 10;
const rows = ref<ResourceOverload[]>([]);
const focus = ref<ResourceOverload | null>(null);
const resources = ref<Resource[]>([]);
const loaded = ref(false);
const busy = ref(false);
const status = ref<number | null>(1);
const resourceFilter = ref<number | null>(props.resourceId);
const page = ref(1);
const totalPages = ref(1);
const totalItems = ref(0);
const dialog = ref<null | { kind: "accept" | "reject" | "refuse" | "close"; overload?: ResourceOverload; proposal?: LevelingProposal }>(null);

// Requests are numbered: an answer that is not the newest request's is ignored.
let sequence = 0;
async function load() {
  const mine = ++sequence;
  const [data, error] = await getOverloads({ resourceId: resourceFilter.value, status: status.value, page: page.value, pageSize: PAGE_SIZE });
  if (mine !== sequence) return;
  loaded.value = true;
  if (error) return fail(error);
  rows.value = data.rows;
  totalPages.value = Math.max(1, data.totalPages);
  totalItems.value = data.totalItems;
  if (page.value > totalPages.value) { page.value = totalPages.value; load(); }
}

// the over-allocation (or the proposal) that a link of the gate inbox names, fetched by its id
async function loadFocus() {
  focus.value = null;
  let id = props.overloadId;
  if (!id && props.proposalId) {
    const [proposal, perr] = await getProposal(props.proposalId);
    if (perr) return fail(perr);
    id = proposal.overloadId;
  }
  if (!id) return;
  const [overload, error] = await getOverload(id);
  if (error) return fail(error);
  focus.value = overload;
}

async function loadResources() {
  const [data, error] = await getResources({ isActive: true });
  if (error) return fail(error);
  resources.value = data;
}

const shown = computed(() => (focus.value ? [focus.value, ...rows.value.filter((o) => o.id !== focus.value!.id)] : rows.value));
const isFocus = (o: ResourceOverload) => focus.value?.id === o.id;
const unitOf = (o: ResourceOverload) => resourceUnits[o.resourceUnit] || "";
const proposalBadge = (p: LevelingProposal) => (p.pendingSecondPerson ? "warn" : p.status === 2 ? "ok" : p.status === 3 ? "bad" : "info");

const dialogFields = computed<PromptField[]>(() => {
  const d = dialog.value;
  if (!d) return [];
  const label = d.kind === "accept" ? "علت پذیرش بیش‌تخصیص" : d.kind === "reject" ? "علت رد پیشنهاد" : d.kind === "refuse" ? "علت عدم‌تایید جابه‌جایی" : "علت بستن بدون برگرداندن";
  return [{ key: "reason", label, type: "textarea", required: true, maxlength: 1000 }];
});
const dialogTitle = computed(() => {
  const k = dialog.value?.kind;
  return k === "accept" ? "پذیرش بیش‌تخصیص" : k === "reject" ? "رد پیشنهاد تسطیح" : k === "refuse" ? "عدم‌تایید جابه‌جایی برنامه" : "بستن بدون برگرداندن";
});
const dialogHint = computed(() => {
  const k = dialog.value?.kind;
  if (k === "accept") return "بیش‌تخصیص به‌عمد باز می‌ماند و جدا شمرده می‌شود؛ علت در سابقه می‌ماند.";
  if (k === "refuse") return "تاریخ فعالیت از راه سرویس برنامه به حالت قبل برمی‌گردد.";
  if (k === "close") return "فقط وقتی قید فعالیت پس از اعمال دوباره عوض شده و برگرداندن خودکار ممکن نیست؛ تاریخ‌ها همان‌طور که هست می‌ماند و علت در سابقه ثبت می‌شود.";
  return undefined;
});

async function refresh() { await Promise.all([load(), props.overloadId || props.proposalId ? loadFocus() : Promise.resolve()]); }

async function confirmDialog(v: Record<string, any>) {
  const d = dialog.value;
  if (!d) return;
  const reason = String(v.reason || "").trim();
  busy.value = true;
  let error: any = null;
  if (d.kind === "accept" && d.overload) [, error] = await acceptOverload(d.overload, reason);
  else if (d.proposal) [, error] = await proposalAction(d.proposal, d.kind === "accept" ? "reject" : d.kind, reason);
  busy.value = false;
  if (error) return fail(error);
  ok(d.kind === "accept" ? "بیش‌تخصیص پذیرفته شد." : d.kind === "reject" ? "پیشنهاد رد شد." : d.kind === "refuse" ? "جابه‌جایی برگردانده شد." : "پیشنهاد بدون برگرداندن بسته شد.");
  dialog.value = null;
  refresh();
}
async function act(p: LevelingProposal, action: "apply" | "confirm") {
  busy.value = true;
  const [, error] = await proposalAction(p, action);
  busy.value = false;
  if (error) { fail(error); return refresh(); }
  ok(action === "apply" ? "پیشنهاد اعمال شد؛ منتظر تایید نفر دوم (PMO) است." : "جابه‌جایی تایید شد.");
  refresh();
}

onMounted(() => {
  loadResources();
  load();
  if (props.overloadId || props.proposalId) loadFocus();
});
watch([status, resourceFilter], () => { page.value = 1; load(); });
watch(page, load);
watch(() => [props.overloadId, props.proposalId], loadFocus);
watch(() => props.resourceId, (v) => { resourceFilter.value = v; });
</script>

<template>
  <div>
    <div class="inv-filters d-print-none">
      <select v-model="status" class="form-select form-select-sm" style="max-width: 170px" aria-label="وضعیت">
        <option :value="null">همه وضعیت‌ها</option>
        <option v-for="(n, k) in overloadStatuses" :key="k" :value="Number(k)">{{ n }}</option>
      </select>
      <select v-model.number="resourceFilter" class="form-select form-select-sm" style="max-width: 240px" aria-label="منبع">
        <option :value="null">همه منابع</option>
        <option v-for="r in resources" :key="r.id" :value="r.id">{{ resourceLabel(r) }}</option>
      </select>
    </div>

    <p class="small inv-muted">بیش‌تخصیص تا رفع (با تغییر طرح یا برنامه) یا پذیرش آگاهانه‌ی PMO باز می‌ماند. ایجنت فقط پیشنهاد می‌دهد و هیچ تغییری در برنامه نمی‌دهد.</p>

    <div v-if="loaded && !shown.length" class="inv-muted text-center">بیش‌تخصیصی با این فیلتر نیست.</div>
    <div v-for="o in shown" :key="o.id" class="inv-card mb-3" :class="{ 'ov-focus': isFocus(o) }" :aria-current="isFocus(o) ? 'true' : undefined">
      <div class="d-flex flex-wrap gap-2 align-items-center">
        <strong><span class="inv-ltr">{{ o.resourceCode }}</span> {{ o.resourceTitle }}</strong>
        <span class="inv-badge" :class="overloadStatusBadge(o.status)">{{ overloadStatuses[o.status] }}</span>
        <span>از {{ faDate(o.fromDate) }} تا {{ faDate(o.toDate) }}</span>
        <span class="small">اوج بار {{ loadNum(o.peakLoad) }} در برابر ظرفیت {{ loadNum(o.peakCapacity) }} {{ unitOf(o) }}</span>
        <button class="btn btn-sm btn-outline-secondary ms-auto d-print-none" @click="emit('load', o.resourceId)">هیستوگرام</button>
      </div>
      <div v-if="o.status === 3" class="small mt-1">پذیرفته‌شده توسط {{ o.acceptedByName || "—" }} ({{ faDateTime(o.acceptedAt) }}): {{ o.acceptReason }}</div>
      <div v-if="o.status === 2" class="small inv-muted mt-1">رفع‌شده در {{ faDateTime(o.resolvedAt) }}</div>
      <div v-if="o.status === 4" class="small inv-muted mt-1">روزهای این بیش‌تخصیص گذشت بی‌آن‌که اصلاح یا پذیرفته شود ({{ faDateTime(o.resolvedAt) }}).</div>

      <div v-if="o.status === 1 && overloadAcceptGate(o).show" class="mt-2 d-print-none">
        <button :disabled="busy || !!overloadAcceptGate(o).reason" :title="overloadAcceptGate(o).reason || ''" class="btn btn-sm btn-outline-warning" @click="dialog = { kind: 'accept', overload: o }">پذیرش بیش‌تخصیص (PMO)</button>
        <span v-if="overloadAcceptGate(o).reason" class="small inv-muted ms-2">{{ overloadAcceptGate(o).reason }}</span>
      </div>

      <div v-if="o.proposals.length" class="mt-2">
        <div class="small fw-bold">پیشنهادهای تسطیح</div>
        <div v-for="p in o.proposals" :key="p.id" class="border rounded p-2 mt-1" :class="{ 'ov-focus': props.proposalId === p.id }">
          <div class="d-flex flex-wrap gap-2 align-items-center">
            <span class="inv-ltr">{{ p.contractCode }} / {{ p.activityCode }}</span> {{ p.activityTitle }}
            <span class="inv-badge" :class="proposalBadge(p)">{{ p.pendingSecondPerson ? PENDING_SECOND : proposalStatuses[p.status] }}</span>
            <span class="inv-badge info">{{ proposalKinds[p.kind] }}</span>
            <span v-if="p.preparedByAgent" class="small inv-muted">تهیه‌شده توسط ایجنت</span>
          </div>
          <div class="small mt-1">
            از {{ faDate(p.currentStart) }} به {{ faDate(p.proposedStart) }} (پایان {{ faDate(p.proposedFinish) }}) — {{ loadNum(p.shiftDays) }} روز کاری
            <span v-if="p.criticalImpactDays > 0" class="text-danger">؛ پایان پروژه {{ loadNum(p.criticalImpactDays) }} روز کاری دیرتر می‌شود</span>
            <span v-else>؛ بدون اثر روی مسیر بحرانی</span>
          </div>
          <div class="small inv-muted">{{ p.note }}</div>
          <div v-if="p.status === 1" class="small inv-muted">{{ CONSTRAINT_NOTE }}</div>
          <div v-if="p.applierName" class="small">اعمال: {{ p.applierName }} ({{ faDateTime(p.appliedAt) }})<template v-if="p.confirmedByName"> — تایید: {{ p.confirmedByName }} ({{ faDateTime(p.confirmedAt) }})</template></div>
          <div v-if="p.status === 3 || p.status === 5" class="small text-danger">{{ p.status === 5 ? "بسته شد بدون برگرداندن (تاریخ‌ها همان‌طور که هست ماند)" : p.reverted ? "نفر دوم نپذیرفت و جابه‌جایی برگردانده شد" : "رد شد" }}<template v-if="p.rejectedByName"> توسط {{ p.rejectedByName }}</template>: {{ p.rejectReason }}</div>
          <div class="d-flex flex-wrap gap-1 mt-1 d-print-none">
            <button v-if="proposalApplyGate(p).show" :disabled="busy || !!proposalApplyGate(p).reason" :title="proposalApplyGate(p).reason || ''" class="btn btn-sm btn-primary" @click="act(p, 'apply')">اعمال</button>
            <button v-if="proposalRejectGate(p).show" :disabled="busy || !!proposalRejectGate(p).reason" class="btn btn-sm btn-outline-danger" @click="dialog = { kind: 'reject', proposal: p }">رد</button>
            <button v-if="proposalConfirmGate(p).show" :disabled="busy || !!proposalConfirmGate(p).reason" :title="proposalConfirmGate(p).reason || ''" class="btn btn-sm btn-success" @click="act(p, 'confirm')">تایید نفر دوم</button>
            <button v-if="proposalConfirmGate(p).show" :disabled="busy || !!proposalConfirmGate(p).reason" :title="proposalConfirmGate(p).reason || ''" class="btn btn-sm btn-outline-danger" @click="dialog = { kind: 'refuse', proposal: p }">عدم‌تایید و بازگرداندن</button>
            <button v-if="proposalCloseGate(p).show" :disabled="busy || !!proposalCloseGate(p).reason" :title="proposalCloseGate(p).reason || ''" class="btn btn-sm btn-outline-secondary" @click="dialog = { kind: 'close', proposal: p }">بستن بدون برگرداندن</button>
          </div>
          <div v-for="(g, gi) in [proposalApplyGate(p), proposalConfirmGate(p), proposalCloseGate(p)]" :key="gi">
            <div v-if="g.show && g.reason" class="small inv-muted">{{ g.reason }}</div>
          </div>
        </div>
      </div>
      <div v-else-if="o.status === 1" class="small inv-muted mt-2">هنوز پیشنهادی نیست؛ ایجنت برنامه‌ریزی آن را می‌سازد.</div>
    </div>

    <nav v-if="totalPages > 1" class="d-flex gap-2 align-items-center justify-content-center d-print-none" aria-label="صفحه‌بندی بیش‌تخصیص‌ها">
      <button class="btn btn-sm btn-outline-secondary" :disabled="page <= 1" @click="page--">قبلی</button>
      <span class="small">صفحه {{ loadNum(page) }} از {{ loadNum(totalPages) }} ({{ loadNum(totalItems) }} مورد)</span>
      <button class="btn btn-sm btn-outline-secondary" :disabled="page >= totalPages" @click="page++">بعدی</button>
    </nav>

    <PromptModal v-if="dialog" :key="dialog.kind + (dialog.proposal?.id || dialog.overload?.id || '')" :title="dialogTitle" :fields="dialogFields" :hint="dialogHint" confirm-text="ثبت" @confirm="confirmDialog" @cancel="dialog = null" />
  </div>
</template>

<style scoped>
.ov-focus { outline: 2px solid #f0ad4e; outline-offset: 2px; }
</style>
