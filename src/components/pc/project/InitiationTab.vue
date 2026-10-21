<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { getInitiation, initiationAction, startInitiation } from "@/api/ProjectControlAPIs";
import UserSelect from "@/components/quality/UserSelect.vue";
import { fail, jalali, ok } from "@/composables/inventory";
import { gateRoleTitles, initiationSteps } from "@/composables/projectControl";
import { currentRole, hasRole, ProjectControlRoles } from "@/composables/roles";

// The ten steps of the project start (WI-PL-009) with the SLA of each step. Only the current step can be done; the API enforces the order and the gate role.
const props = defineProps({ contractId: { type: Number, required: true } });
const emit = defineEmits(["goto"]);
const init = ref<any>(null);
const missing = ref(false);
const busy = ref(false);
const note = ref("");
const managerId = ref<number | null>(null);
const canStart = hasRole(ProjectControlRoles.initiationStart);

async function load() {
  init.value = null;
  missing.value = false;
  const [data, error] = await getInitiation(props.contractId);
  if (error) {
    // no initiation record yet: the API answers "not found"
    missing.value = true;
    return;
  }
  init.value = data;
}

async function start() {
  busy.value = true;
  const [data, error] = await startInitiation(props.contractId);
  busy.value = false;
  if (error) return fail(error);
  ok("فرآیند شروع پروژه ثبت شد.");
  init.value = data;
  missing.value = false;
}

/** Which role may do the step by itself on this page (steps 2, 5, 6, 9, 10 are done in other tabs or by the system). */
const stepRoles: Record<number, string[]> = {
  3: ["PLANNING", "PMO", "COMMERCIAL"],
  4: ["CEO"],
  7: ["PROJECTMANAGER", "PMO"],
  8: ["PLANNING", "PMO"],
};
const where: Record<number, string> = { 2: "terms", 5: "charter", 6: "plan" };
const whereText: Record<number, string> = { 2: "در زبانه «شرایط و کد پروژه» صادر کنید", 5: "در زبانه «منشور» تایید کنید", 6: "در زبانه «برنامه اجرایی (PEP)» تایید کنید", 9: "سیستم ثبت می‌کند", 10: "سیستم ثبت می‌کند", 1: "سیستم ثبت می‌کند" };
const canDo = (step: number) => (stepRoles[step] || []).includes(currentRole());

async function act(step: number) {
  busy.value = true;
  const [data, error] = await initiationAction({ contractId: props.contractId, step, note: note.value || null, projectManagerUserId: step === 4 ? managerId.value : null });
  busy.value = false;
  if (error) return fail(error);
  ok("ثبت شد.");
  note.value = "";
  init.value = data;
}

const statusOf = (s: any) => (s.doneAt ? "ok" : s.overdue ? "bad" : s.step === init.value?.currentStep ? "info" : "");
const doneCount = computed(() => (init.value?.steps || []).filter((s: any) => s.doneAt).length);

onMounted(load);
watch(() => props.contractId, load);
</script>

<template>
  <div>
    <div v-if="missing" class="alert alert-warning d-flex justify-content-between align-items-center">
      <span>فرآیند شروع این پروژه هنوز ثبت نشده است.</span>
      <button v-if="canStart" :disabled="busy" class="btn btn-sm btn-primary" @click="start">شروع فرآیند (ثبت دریافت ابلاغ)</button>
    </div>
    <div v-if="init">
      <p class="mb-2">پیشرفت مراحل: <b>{{ doneCount }}</b> از {{ init.steps.length }} <span v-if="init.completed" class="inv-badge ok">کامل شد</span></p>
      <table class="table table-sm">
        <thead><tr><th>#</th><th>مرحله</th><th>مسئول</th><th>مهلت (روز کاری)</th><th>سررسید</th><th>انجام</th><th>یادداشت</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-for="s in init.steps" :key="s.step" :class="{ 'table-warning': s.step === init.currentStep && !init.completed }">
            <td>{{ s.step }}</td>
            <td>{{ initiationSteps[s.step] || s.title }}<div class="inv-muted">{{ s.title }}</div></td>
            <td>{{ gateRoleTitles[s.gateRole] || s.gateRole }}</td>
            <td>{{ s.slaDays || "" }}</td>
            <td>{{ jalali(s.dueDate) }} <span v-if="s.overdue" class="inv-badge bad">از مهلت گذشته</span></td>
            <td><span v-if="s.doneAt" :class="statusOf(s)" class="inv-badge">{{ jalali(s.doneAt) }}</span> <span class="small">{{ s.doneByName }}</span></td>
            <td>{{ s.note }}</td>
            <td class="d-print-none">
              <template v-if="!s.doneAt && s.step === init.currentStep">
                <template v-if="canDo(s.step)">
                  <div v-if="s.step === 4" class="mb-1"><UserSelect v-model="managerId" placeholder="مدیر پروژه" /></div>
                  <button :disabled="busy || (s.step === 4 && !managerId)" class="btn btn-sm btn-success" @click="act(s.step)">انجام شد</button>
                </template>
                <a v-else-if="where[s.step]" href="#" @click.prevent="emit('goto', where[s.step])">{{ whereText[s.step] }}</a>
                <span v-else class="inv-muted">{{ whereText[s.step] || "در انتظار " + (gateRoleTitles[s.gateRole] || s.gateRole) }}</span>
              </template>
              <a v-else-if="!s.doneAt && where[s.step]" href="#" @click.prevent="emit('goto', where[s.step])">{{ whereText[s.step] }}</a>
            </td>
          </tr>
        </tbody>
      </table>
      <div class="col-md-6 d-print-none"><label class="form-label small mb-0">یادداشت مرحله (اختیاری)</label><input v-model="note" class="form-control form-control-sm" /></div>
      <div v-if="init.unitsNoticeDraft" class="border rounded p-2 mt-3">
        <b>پیش‌نویس اعلام به واحدها (FO-PL-001):</b>
        <p class="mb-0 mt-1">{{ init.unitsNoticeDraft }}</p>
      </div>
    </div>
  </div>
</template>
