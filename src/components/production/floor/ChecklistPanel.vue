<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { getOperationChecklist, reviewChecklist, saveChecklistAnswers } from "@/api/ShopAPIs";
import { fail, ok } from "@/composables/inventory";

// The in-process checklist of an operation on the terminal (big controls for the tablet); the reviewer (QC / supervisor) approves or rejects it here too.
const props = defineProps<{ operationId: number }>();
const emit = defineEmits(["changed"]);
const c = ref<any>(null);
const answers = ref<Record<number, any>>({});
const busy = ref(false);
const statuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال‌شده، منتظر بازبینی", 2: "تأییدشده", 3: "ردشده در بازبینی" };

async function load() {
  const [d, error] = await getOperationChecklist(props.operationId);
  if (error) return fail(error);
  c.value = d;
  const map: Record<number, any> = {};
  for (const i of d.template?.items || []) {
    const a = d.answers.find((x: any) => x.itemId === i.id);
    map[i.id] = { yesNo: a?.yesNo ?? null, value: a?.value ?? "", text: a?.text ?? "", passed: a?.passed, measurementId: a?.measurementId ?? null };
  }
  answers.value = map;
}
watch(() => props.operationId, load);
onMounted(load);

async function save(submit: boolean) {
  busy.value = true;
  const list = Object.entries(answers.value).map(([id, a]: any) => ({ itemId: Number(id), yesNo: a.yesNo, value: a.value === "" || a.value === null ? null : Number(a.value), text: a.text || null }));
  const [d, error] = await saveChecklistAnswers({ operationId: props.operationId, answers: list, submit, rowVersion: c.value.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  ok(submit ? (d.allPassed ? "چک‌لیست ارسال شد؛ همه بندها قبول است." : "چک‌لیست ارسال شد ولی بند ردشده دارد.") : "پاسخ‌ها ذخیره شد.");
  await load();
  emit("changed");
}
async function review(action: "approve" | "reject") {
  const note = action === "reject" ? prompt("دلیل رد:") : null;
  if (action === "reject" && !note) return;
  const [, error] = await reviewChecklist({ operationId: props.operationId, action, note, rowVersion: c.value.rowVersion });
  if (error) return fail(error);
  await load();
  emit("changed");
}
const editable = () => c.value?.allowedActions?.includes("save");
</script>

<template>
  <div v-if="c && c.hasTemplate" class="cl-panel">
    <div class="tm-label">{{ c.template.title }} <span v-if="c.status !== null && c.status !== undefined" class="small">({{ statuses[c.status] }})</span></div>
    <div v-if="c.reviewNote" class="small text-danger">{{ c.reviewNote }}</div>
    <div v-for="i in c.template.items" :key="i.id" class="cl-row">
      <div class="cl-text">{{ i.text }}<span v-if="i.mandatory" class="text-danger">*</span><span v-if="i.kind === 2" class="inv-muted inv-ltr small"> [{{ i.min ?? "" }} … {{ i.max ?? "" }} {{ i.unit || "" }}]</span></div>
      <div v-if="i.kind === 1" class="tm-choices">
        <button :class="{ on: answers[i.id].yesNo === true }" :disabled="!editable()" class="tm-btn" @click="answers[i.id].yesNo = true">بله</button>
        <button :class="{ on: answers[i.id].yesNo === false }" :disabled="!editable()" class="tm-btn" @click="answers[i.id].yesNo = false">خیر</button>
      </div>
      <!-- P2 part E: a device-only item shows the value the probe or the gauge reported; it cannot be typed -->
      <span v-else-if="i.kind === 2 && i.deviceOnly" :class="answers[i.id].measurementId ? 'cl-dev ok' : 'cl-dev'">
        <b v-if="answers[i.id].measurementId" class="inv-ltr">{{ answers[i.id].value }}</b>
        <span class="small">{{ answers[i.id].measurementId ? answers[i.id].text : "منتظر اندازه از گیج یا پراب (" + i.characteristic + ")؛ پس از اندازه‌گیری «ذخیره» بزنید." }}</span>
      </span>
      <input v-else-if="i.kind === 2" v-model="answers[i.id].value" :disabled="!editable()" class="form-control inv-ltr" inputmode="decimal" style="max-width: 160px" />
      <input v-else v-model="answers[i.id].text" :disabled="!editable()" class="form-control" />
      <span v-if="answers[i.id].passed === false" class="text-danger small">رد</span>
    </div>
    <div class="tm-choices mt-2">
      <button v-if="editable()" :disabled="busy" class="tm-btn" @click="save(false)">ذخیره</button>
      <button v-if="c.allowedActions.includes('submit')" :disabled="busy" class="tm-btn on" @click="save(true)">ارسال چک‌لیست</button>
      <button v-if="c.allowedActions.includes('approve')" class="tm-btn on" @click="review('approve')">تأیید چک‌لیست</button>
      <button v-if="c.allowedActions.includes('reject')" class="tm-btn" @click="review('reject')">رد چک‌لیست</button>
    </div>
  </div>
</template>

<style scoped>
.cl-panel { border: 2px solid #90caf9; border-radius: 8px; padding: 10px; margin-top: 12px; }
.cl-row { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; padding: 6px 0; border-bottom: 1px dashed #ddd; }
.cl-text { flex: 1 1 200px; }
.cl-dev { display: inline-flex; gap: 8px; align-items: baseline; border: 1px dashed #90caf9; border-radius: 6px; padding: 4px 10px; color: #455a64; }
.cl-dev.ok { border-style: solid; border-color: #2e7d32; }
</style>
