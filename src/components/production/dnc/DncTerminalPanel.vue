<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { downloadTransfer, getTerminalNc, sendProgram, transferAction } from "@/api/DncAPIs";
import { fail, ok } from "@/composables/inventory";
import { transferStatuses } from "@/composables/dnc";

// The program of the operation on the chosen machine, inside the start dialog of the terminal: send the current revision, confirm it on the control (or download it for a manual link).
// When the machine requires it, the API refuses the start until the confirmation; this panel shows why and offers the next step with large buttons.
const props = defineProps<{ operationId: number; machineId: number }>();
const emit = defineEmits(["ready"]);
const nc = ref<any>(null);
const busy = ref(false);
let timer: any = null;

async function load() {
  const [d, error] = await getTerminalNc(props.operationId, props.machineId);
  if (error) return;
  nc.value = d;
  emit("ready", d?.canStart !== false);
  clearTimeout(timer);
  // while the transfer is on its way, look again shortly (the queue runs every few seconds on the server)
  const s = d?.lastTransfer?.status;
  if (s === 0 || s === 1) timer = setTimeout(load, 4000);
}
watch(() => [props.operationId, props.machineId], load);
onMounted(load);

async function send() {
  busy.value = true;
  const [, error] = await sendProgram({ programId: nc.value.programId, machineId: props.machineId, operationId: props.operationId });
  busy.value = false;
  if (error) return fail(error);
  ok("برنامه برای ارسال به ماشین در صف قرار گرفت؛ کنترلر را در حالت دریافت قرار دهید.");
  load();
}
async function confirmIt() {
  const t = nc.value.lastTransfer;
  busy.value = true;
  const [, error] = await transferAction({ transferId: t.id, action: "confirm", note: "روی کنترلر بررسی شد", rowVersion: t.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  ok("دریافت برنامه روی کنترلر تأیید شد.");
  load();
}
async function download() {
  const t = nc.value.lastTransfer;
  const message = await downloadTransfer(t.id, t.targetName);
  if (message) return fail(message);
  load();
}
const sentCurrent = () => nc.value?.lastTransfer && nc.value.lastTransfer.revisionId === nc.value.currentRevisionId && [0, 1, 2, 3].includes(nc.value.lastTransfer.status);
</script>

<template>
  <div v-if="nc && nc.hasProgram" :class="nc.canStart ? 'ok' : 'warn'" class="dnc-panel">
    <div class="tm-label">برنامه CNC</div>
    <div><b class="inv-ltr">{{ nc.programCode }}</b> {{ nc.programTitle }} — نسخه {{ nc.currentRevision ?? "تأییدنشده" }}</div>
    <div v-if="nc.lastTransfer" class="small">آخرین انتقال: {{ transferStatuses[nc.lastTransfer.status] }} (نسخه {{ nc.lastTransfer.revision }}، {{ nc.lastTransfer.targetName }})<span v-if="nc.lastTransfer.lastError" class="text-danger"> — {{ nc.lastTransfer.lastError }}</span></div>
    <div v-if="nc.problem" class="small text-danger">{{ nc.problem }}</div>
    <div class="tm-choices mt-2">
      <button v-if="nc.currentRevisionId && !sentCurrent()" :disabled="busy" class="tm-btn" @click="send">ارسال برنامه به ماشین</button>
      <button v-if="nc.lastTransfer?.allowedActions?.includes('download')" :disabled="busy" class="tm-btn" @click="download">دانلود فایل (USB)</button>
      <button v-if="nc.lastTransfer?.allowedActions?.includes('confirm') && nc.lastTransfer.revisionId === nc.currentRevisionId" :disabled="busy" class="tm-btn on" @click="confirmIt">برنامه روی کنترلر بررسی شد</button>
    </div>
  </div>
</template>

<style scoped>
.dnc-panel { border: 2px solid #ccc; border-radius: 8px; padding: 10px; margin-top: 12px; }
.dnc-panel.ok { border-color: #2e7d32; }
.dnc-panel.warn { border-color: #ef6c00; }
</style>
