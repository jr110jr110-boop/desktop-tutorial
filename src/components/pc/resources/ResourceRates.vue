<script setup lang="ts">
// The standard rates of the resources. The finance manager proposes and submits a rate; a second finance person (the finance manager or the CEO) approves or rejects it with a reason.
// The maker sees the approve button disabled with the words of the server. A rate the API holds back is shown as «محرمانه».
import { computed, onMounted, ref, watch } from "vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import type { PromptField } from "@/components/fn/PromptModal.vue";
import { getRates, getResources, rateAction, saveRate } from "@/api/ResourceAPIs";
import type { Resource, ResourceRate } from "@/api/ResourceAPIs";
import { fail, ok, today } from "@/composables/inventory";
import { faDate, faDateTime, rateApproveGate, rateEditGate, rateRejectGate, rateStatusBadge, rateStatuses, rateText, rateSubmitGate, resourceLabel, ResourceTexts } from "@/composables/resources";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

const props = defineProps({ resourceId: { type: Number, default: null } });
const rows = ref<ResourceRate[]>([]);
const loaded = ref(false);
const busy = ref(false);
const status = ref<number | null>(null);
const resourceFilter = ref<number | null>(props.resourceId);
const resources = ref<Resource[]>([]);
const canPropose = hasRole(ProjectControlRoles.rateMakers);
const dialog = ref<null | { kind: "propose" | "edit" | "reject"; rate?: ResourceRate }>(null);

async function load() {
  const [data, error] = await getRates({ resourceId: resourceFilter.value, status: status.value });
  loaded.value = true;
  if (error) return fail(error);
  rows.value = data;
}
async function loadResources() {
  const [data, error] = await getResources({ isActive: true });
  if (error) return fail(error);
  resources.value = data;
}
const candidates = computed(() => resources.value.filter((r) => r.canCarryRate));
const resourceOptions = computed(() => candidates.value.map((r) => ({ value: r.id, text: resourceLabel(r) })));

const dialogHint = computed(() => (dialog.value?.rate ? `منبع: ${dialog.value.rate.resourceCode} - ${dialog.value.rate.resourceTitle}` : undefined));
const fields = computed<PromptField[]>(() => {
  const d = dialog.value;
  if (!d) return [];
  if (d.kind === "reject") return [{ key: "note", label: "علت رد نرخ", type: "textarea", required: true, maxlength: 1000 }];
  const proposing = d.kind === "propose";
  return [
    ...(proposing ? [{ key: "resourceId", label: "منبع", type: "select", required: true, options: resourceOptions.value, value: resourceFilter.value && candidates.value.some((r) => r.id === resourceFilter.value) ? resourceFilter.value : null } as PromptField] : []),
    { key: "rate", label: "نرخ استاندارد (ریال برای هر واحد منبع)", type: "number", required: true, value: d.rate?.rate ?? "" },
    { key: "effectiveFrom", label: "شروع اعتبار", type: "date", required: true, value: d.rate ? d.rate.effectiveFrom.slice(0, 10) : today() },
  ];
});

async function confirm(v: Record<string, any>) {
  const d = dialog.value;
  if (!d) return;
  busy.value = true;
  let error: any = null;
  if (d.kind === "reject" && d.rate) [, error] = await rateAction(d.rate.id, "reject", String(v.note || "").trim());
  else if (d.kind === "propose") [, error] = await saveRate({ id: null, resourceId: Number(v.resourceId), rate: Number(v.rate), effectiveFrom: String(v.effectiveFrom), rowVersion: null });
  else if (d.rate) [, error] = await saveRate({ id: d.rate.id, resourceId: d.rate.resourceId, rate: Number(v.rate), effectiveFrom: String(v.effectiveFrom), rowVersion: d.rate.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  ok(d.kind === "reject" ? "نرخ رد شد." : "نرخ ذخیره شد (پیش‌نویس)؛ برای تایید، آن را ارسال کنید.");
  dialog.value = null;
  load();
}
async function act(r: ResourceRate, action: "submit" | "approve") {
  busy.value = true;
  const [, error] = await rateAction(r.id, action);
  busy.value = false;
  if (error) return fail(error);
  ok(action === "submit" ? "نرخ برای تایید نفر دوم مالی ارسال شد." : "نرخ تایید شد.");
  load();
}
const filterName = computed(() => {
  const r = rows.value.find((x) => x.resourceId === resourceFilter.value);
  return r ? resourceLabel({ code: r.resourceCode, title: r.resourceTitle }) : "#" + resourceFilter.value;
});

onMounted(() => {
  load();
  if (canPropose) loadResources();
});
watch([status, resourceFilter], load);
watch(() => props.resourceId, (v) => { resourceFilter.value = v; });
</script>

<template>
  <div>
    <div class="inv-filters d-print-none">
      <select v-model.number="status" class="form-select form-select-sm" style="max-width: 170px">
        <option :value="null">همه وضعیت‌ها</option>
        <option v-for="(n, k) in rateStatuses" :key="k" :value="Number(k)">{{ n }}</option>
      </select>
      <span v-if="resourceFilter" class="inv-badge info">منبع: {{ filterName }} <a href="#" class="ms-1" @click.prevent="resourceFilter = null">همه منابع</a></span>
      <button v-if="canPropose" class="btn btn-sm btn-primary ms-auto" @click="dialog = { kind: 'propose' }">پیشنهاد نرخ</button>
    </div>

    <div class="table-responsive">
      <table class="table table-sm table-hover align-middle">
        <thead><tr><th>منبع</th><th>نرخ (ریال)</th><th>شروع اعتبار</th><th>وضعیت</th><th>پیشنهاددهنده</th><th>تایید / رد</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="loaded && !rows.length"><td colspan="7" class="text-center inv-muted">نرخی ثبت نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id">
            <td><span class="inv-ltr">{{ r.resourceCode }}</span> {{ r.resourceTitle }}</td>
            <td><span :class="r.rateHidden ? 'inv-muted' : ''">{{ rateText(r.rate, r.rateHidden) }}</span></td>
            <td>{{ faDate(r.effectiveFrom) }}</td>
            <td><span class="inv-badge" :class="rateStatusBadge(r.status)">{{ rateStatuses[r.status] }}</span></td>
            <td>{{ r.makerName || "—" }}<div v-if="r.submittedAt" class="small inv-muted">ارسال: {{ faDateTime(r.submittedAt) }}</div></td>
            <td>
              <template v-if="r.status === 2">{{ r.approvedByName || "—" }}<div class="small inv-muted">{{ faDateTime(r.approvedAt) }}</div></template>
              <template v-else-if="r.status === 3">{{ r.rejectReason }}<div class="small inv-muted">{{ faDateTime(r.rejectedAt) }}</div></template>
              <span v-else class="inv-muted">—</span>
            </td>
            <td class="d-print-none">
              <div class="d-flex flex-wrap gap-1">
                <template v-if="rateEditGate(r).show">
                  <button :disabled="busy || !!rateEditGate(r).reason" :title="rateEditGate(r).reason || ''" class="btn btn-sm btn-outline-secondary" @click="dialog = { kind: 'edit', rate: r }">ویرایش</button>
                </template>
                <template v-if="rateSubmitGate(r).show">
                  <button :disabled="busy || !!rateSubmitGate(r).reason" :title="rateSubmitGate(r).reason || ''" class="btn btn-sm btn-outline-primary" @click="act(r, 'submit')">ارسال برای تایید</button>
                </template>
                <template v-if="rateApproveGate(r).show">
                  <button :disabled="busy || !!rateApproveGate(r).reason" :title="rateApproveGate(r).reason || ''" class="btn btn-sm btn-success" @click="act(r, 'approve')">تایید</button>
                  <button :disabled="busy || !!rateRejectGate(r).reason" :title="rateRejectGate(r).reason || ''" class="btn btn-sm btn-outline-danger" @click="dialog = { kind: 'reject', rate: r }">رد</button>
                </template>
              </div>
              <div v-if="rateApproveGate(r).reason" class="small text-danger mt-1">{{ rateApproveGate(r).reason }}</div>
              <div v-else-if="rateEditGate(r).reason" class="small text-danger mt-1">{{ rateEditGate(r).reason }}</div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <p class="inv-muted small mb-0">نرخ را مدیر مالی پیشنهاد می‌دهد و نفر دوم مالی (مدیر مالی یا مدیرعامل) تایید می‌کند؛ پیشنهاددهنده نرخ خودش را تایید نمی‌کند. روی منبع وصل‌شده به کارمند نرخ ثبت نمی‌شود: {{ ResourceTexts.ratePrivacy }}</p>

    <PromptModal
      v-if="dialog"
      :key="dialog.kind + (dialog.rate?.id || '')"
      :title="dialog.kind === 'reject' ? 'رد نرخ' : dialog.kind === 'edit' ? 'ویرایش نرخ پیش‌نویس' : 'پیشنهاد نرخ استاندارد'"
      :fields="fields"
      :hint="dialogHint"
      :confirm-text="dialog.kind === 'reject' ? 'رد نرخ' : 'ذخیره'"
      @confirm="confirm"
      @cancel="dialog = null" />
  </div>
</template>
