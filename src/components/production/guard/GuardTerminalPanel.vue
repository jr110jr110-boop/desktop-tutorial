<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { createOverride, getTerminalGuard } from "@/api/GuardAPIs";
import { fail, ok } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { GuardRoles, execModes, gateIcon, gateTone, programStates } from "@/composables/guard";

// The gates of a guarded machine inside the start dialog of the terminal: the link to the control, the program read back from the control, the tool table, the prove-out of a first run.
// A look only: the start itself checks again. On a blocked gate the production manager (and only he) can open it once here, with a written reason.
const props = defineProps<{ operationId: number; machineId: number }>();
const g = ref<any>(null);
const busy = ref(false);
const ask = ref<null | { gate: string; title: string; reason: string }>(null);
const canOverride = hasRole(GuardRoles.overriders);
const m = computed(() => g.value?.machine);

async function load() {
  busy.value = true;
  const [d, error] = await getTerminalGuard(props.operationId, props.machineId);
  busy.value = false;
  if (error) return;
  g.value = d;
}
watch(() => [props.operationId, props.machineId], load);
onMounted(load);

const overridable = (code: string) => ["program", "tools", "interlock", "prove-out"].includes(code);
async function sendOverride() {
  const a = ask.value!;
  busy.value = true;
  const [, error] = await createOverride({ machineId: props.machineId, operationId: props.operationId, gate: a.gate, reason: a.reason.trim() });
  busy.value = false;
  if (error) return fail(error);
  ok("مجوز یک‌باره ثبت شد (۳۰ دقیقه، یک بار).");
  ask.value = null;
  load();
}
</script>

<template>
  <div v-if="g && g.guarded" :class="g.canStart ? 'ok' : 'bad'" class="gt">
    <div class="gt-head">
      <div class="tm-label m-0">کنترل خودکار ماشین</div>
      <span v-if="m" class="small">{{ execModes[m.execMode] }} · {{ programStates[m.programState] }}<span v-if="m.selectedProgram" class="gd-ltr"> · {{ m.selectedProgram }}</span></span>
    </div>
    <ul class="gt-gates">
      <li v-for="x in g.gates" :key="x.code" :class="gateTone(x.state)">
        <span class="gt-icon" aria-hidden="true">{{ gateIcon(x.state) }}</span>
        <div>
          <b>{{ x.title }}</b><span v-if="x.overridden" class="gt-ov">مجوز یک‌باره</span>
          <div class="small">{{ x.text }}</div>
          <button v-if="canOverride && (x.state === 'blocked' || (x.state === 'pending' && x.code === 'prove-out')) && overridable(x.code)" class="tm-btn mt-1" @click="ask = { gate: x.code, title: x.title, reason: '' }">مجوز یک‌باره مدیر تولید</button>
        </div>
      </li>
    </ul>
    <div v-if="g.overrides?.length" class="small gt-active">مجوز فعال: <span v-for="o in g.overrides" :key="o.id">{{ o.gateText }} (تا {{ o.expiresAtText }}) </span></div>
    <div class="tm-choices mt-2"><button :disabled="busy" class="tm-btn" @click="load">بررسی دوباره با کنترلر</button></div>

    <div v-if="ask" class="gt-ask">
      <div class="small mb-1">گیت «{{ ask.title }}» فقط برای همین عملیات و فقط یک بار باز می‌شود؛ دلیل برای مدیرعامل و حسابرس فرستاده می‌شود.</div>
      <textarea v-model="ask.reason" class="form-control" rows="2" placeholder="دلیل (دست‌کم ۱۰ نویسه)"></textarea>
      <div class="tm-choices mt-2">
        <button class="tm-btn" @click="ask = null">انصراف</button>
        <button :disabled="busy || ask.reason.trim().length < 10" class="tm-btn on" @click="sendOverride">ثبت مجوز</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.gt { border: 2px solid #ccc; border-radius: 8px; padding: 10px; margin-top: 12px; }
.gt.ok { border-color: #2e7d32; }
.gt.bad { border-color: #c62828; }
.gt-head { display: flex; justify-content: space-between; align-items: baseline; gap: 8px; flex-wrap: wrap; }
.gt-gates { list-style: none; margin: 8px 0 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
.gt-gates li { display: grid; grid-template-columns: 28px 1fr; gap: 8px; align-items: start; padding: 6px 8px; border-radius: 6px; background: rgba(0, 0, 0, 0.03); }
.gt-icon { width: 24px; height: 24px; border-radius: 50%; display: inline-flex; align-items: center; justify-content: center; font-weight: 800; color: #fff; background: #9e9e9e; }
.gt-gates li.ok .gt-icon { background: #2e7d32; }
.gt-gates li.bad .gt-icon { background: #c62828; }
.gt-gates li.warn .gt-icon { background: #ef6c00; }
.gt-gates li.bad b { color: #c62828; }
.gt-ov { margin-inline-start: 6px; font-size: 0.75rem; background: #fff3e0; color: #8a4b00; border-radius: 999px; padding: 0 8px; }
.gt-active { margin-top: 6px; color: #8a4b00; }
.gt-ask { margin-top: 10px; padding: 8px; border-radius: 6px; background: #fff8e1; }
.gd-ltr { direction: ltr; unicode-bidi: embed; }
</style>
