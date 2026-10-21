<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { approvePlan, charterAction, getCharter, getPlan, saveCharter, savePlan } from "@/api/ProjectControlAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { currentRole, hasRole, ProjectControlRoles } from "@/composables/roles";

// Project charter (13 sections, WI-PL-007) and project execution plan PEP (WI-PL-006). Gates: the project manager accepts the authority,
// the CEO approves the charter, the PMO deputy approves the PEP. An approved document can be changed only through change control.
const props = defineProps({ contractId: { type: Number, required: true }, kind: { type: String as () => "charter" | "plan", required: true } });
const emit = defineEmits(["changed"]);

const charterFields = [
  ["overview", "نمای کلی پروژه"], ["scopeDescription", "شرح محدوده *"], ["requirements", "الزامات"], ["deliverables", "تحویل‌دادنی‌ها"], ["boundaries", "مرزهای پروژه"],
  ["timeBaselineEstimate", "برآورد زمان"], ["costEstimate", "برآورد هزینه"], ["alternativesAnalysis", "تحلیل گزینه‌ها"], ["assumptions", "مفروضات"], ["constraints", "محدودیت‌ها"],
  ["risks", "ریسک‌ها"], ["stakeholders", "ذینفعان"], ["preAssignedResources", "منابع از پیش تخصیص‌یافته"], ["managerAuthority", "اختیارات مدیر پروژه"],
];
const planFields = [
  ["overview", "نمای کلی"], ["scopeStatement", "بیانیه محدوده *"], ["inScope", "داخل محدوده"], ["outOfScope", "خارج از محدوده"], ["constraints", "محدودیت‌ها"], ["assumptions", "مفروضات"],
  ["milestones", "نقاط عطف"], ["longLeadRules", "قواعد اقلام زمان‌بر"], ["communicationPlan", "برنامه ارتباطات"], ["makeBuyPartner", "ساخت / خرید / شریک"],
  ["qualityPlan", "برنامه کیفیت"], ["changeManagement", "مدیریت تغییر"], ["otherSections", "سایر بخش‌ها"],
];
const fields = computed(() => (props.kind === "charter" ? charterFields : planFields));

const doc = ref<any>(null);
const busy = ref(false);
const note = ref("");
const isCharter = computed(() => props.kind === "charter");
const approved = computed(() => !!doc.value?.approvedAt);
const canEdit = computed(() => !approved.value && hasRole(isCharter.value ? ProjectControlRoles.charter : ProjectControlRoles.planningOrPm));
const role = currentRole();

async function load() {
  doc.value = null;
  const [data, error] = await (isCharter.value ? getCharter : getPlan)(props.contractId);
  if (error) return fail(error);
  doc.value = data;
}

async function save() {
  busy.value = true;
  const [data, error] = await (isCharter.value ? saveCharter : savePlan)({ ...doc.value, contractId: props.contractId });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  doc.value = { ...doc.value, ...data };
}

async function act(what: "accept" | "approve") {
  busy.value = true;
  const [data, error] = isCharter.value ? await charterAction(what, props.contractId, note.value) : await approvePlan(props.contractId, note.value);
  busy.value = false;
  if (error) return fail(error);
  ok("انجام شد.");
  doc.value = { ...doc.value, ...data };
  emit("changed");
}

onMounted(load);
watch(() => [props.contractId, props.kind], load);
</script>

<template>
  <div v-if="doc">
    <div class="d-flex flex-wrap gap-2 align-items-center mb-3">
      <span v-if="approved" class="inv-badge ok">تاییدشده {{ jalali(doc.approvedAt) }} ({{ doc.approvedByName }})</span>
      <span v-else class="inv-badge warn">تاییدنشده</span>
      <span v-if="isCharter && doc.managerAcceptedAt" class="inv-badge info">مدیر پروژه اختیارات را پذیرفت ({{ jalali(doc.managerAcceptedAt) }})</span>
    </div>
    <div class="row g-2">
      <div v-for="f in fields" :key="f[0]" class="col-md-6">
        <label class="form-label small mb-0">{{ f[1] }}</label>
        <textarea v-model="doc[f[0]]" :disabled="!canEdit" class="form-control form-control-sm" rows="3"></textarea>
      </div>
    </div>
    <div class="d-flex flex-wrap gap-2 align-items-end mt-3 d-print-none">
      <button v-if="canEdit" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
      <template v-if="!approved">
        <input v-model="note" class="form-control form-control-sm" placeholder="یادداشت تایید" style="max-width: 260px" />
        <button v-if="isCharter && role === 'PROJECTMANAGER' && !doc.managerAcceptedAt" :disabled="busy" class="btn btn-sm btn-outline-success" @click="act('accept')">پذیرش اختیارات (مدیر پروژه)</button>
        <button v-if="isCharter && role === 'CEO'" :disabled="busy" class="btn btn-sm btn-success" @click="act('approve')">تایید منشور (مدیرعامل)</button>
        <button v-if="!isCharter && role === 'PMO'" :disabled="busy" class="btn btn-sm btn-success" @click="act('approve')">تایید PEP (معاون PMO)</button>
      </template>
    </div>
    <p class="inv-muted mt-2 mb-0">
      <template v-if="isCharter">منشور را مدیرعامل پس از پذیرش اختیارات توسط مدیر پروژه تایید می‌کند. تغییر منشور تاییدشده از مسیر کنترل تغییرات انجام می‌شود.</template>
      <template v-else>برنامه اجرایی پروژه (PEP) را معاون PMO تایید می‌کند (مهلت تایید در گام‌های شروع پروژه دیده می‌شود).</template>
    </p>
  </div>
</template>
