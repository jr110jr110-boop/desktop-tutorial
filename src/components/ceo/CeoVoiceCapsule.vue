<script setup lang="ts">
// The assistant's capsule at the bottom-left of the CEO page (while the drawer is closed): a microphone button and the button that opens the drawer.
// The microphone works like the one in the drawer: a tap starts listening (a short silence sends), a tap while listening sends now, a tap while the assistant speaks
// interrupts it and listens (barge-in), a held press is push-to-talk. While the session is active a caption card rises above the capsule: the state (pulsing dot,
// thin running bar, equaliser — still with reduced motion), the waveform while listening, the follow-up countdown, the current caption and a stop button (Esc does the same).
// Idle, the capsule stays under the slide-overs (a detail table keeps its corner); while the session is active it floats above them, so the captions are always readable.
import { computed, onBeforeUnmount, ref, watch } from "vue";
import VoiceIndicator from "@/components/ai/VoiceIndicator.vue";
import VoiceWave from "@/components/ai/VoiceWave.vue";
import CeoIcon from "@/components/ceo/CeoIcon.vue";
import { faNum } from "@/composables/ceo";
import { VOICE_TIMING, type VoiceSession } from "@/composables/voice";

const props = defineProps({
  voice: { type: Object as () => VoiceSession, required: true },
});
const emit = defineEmits<{ (e: "open-drawer"): void }>();

// the card stays a few seconds after the session went idle, so the last caption («چیزی شنیده نشد»، an action line) can be read
const lingering = ref(false);
let lingerTimer: ReturnType<typeof setTimeout> | null = null;
watch(
  () => [props.voice.state, props.voice.captions.length],
  () => {
    if (props.voice.state !== "idle") {
      lingering.value = false;
      return;
    }
    lingering.value = true;
    if (lingerTimer) clearTimeout(lingerTimer);
    lingerTimer = setTimeout(() => (lingering.value = false), 5000);
  }
);
onBeforeUnmount(() => lingerTimer && clearTimeout(lingerTimer));

const active = computed(() => props.voice.state !== "idle" || (lingering.value && props.voice.captions.length > 0));
const stateText = computed(() => {
  const v = props.voice;
  switch (v.state) {
    case "listening":
      return v.followup && !v.heard ? "پرسش بعدی را بگویید" : "در حال شنیدن…";
    case "thinking":
      return "در حال فکر کردن…";
    case "speaking":
      return v.muted ? "پاسخ (بدون صدا)" : "در حال گفتن…";
    default:
      return "دستیار";
  }
});
const line = computed(() => {
  const v = props.voice;
  if (v.state === "speaking" && v.speaking?.text) return v.speaking.text;
  if (v.state === "listening" && !v.followup) return "بگویید؛ پس از مکثی کوتاه ارسال می‌شود.";
  if (v.state === "thinking") return "";
  const last = v.captions[v.captions.length - 1];
  return last ? (last.who === "user" ? "شما: " : last.who === "action" ? (last.ok === false ? "✗ " : "✓ ") : "") + last.text : "";
});
const micLabel = computed(() => {
  const s = props.voice.state;
  return s === "listening" ? "ارسال پرسش" : s === "speaking" ? "قطع پاسخ و صحبت" : s === "thinking" ? "در حال فکر کردن" : "صحبت با دستیار (یا Space را نگه دارید)";
});

let pressedAt = 0;
let held = false;
let ignoreUp = false;
function onPress(e: PointerEvent) {
  if (e.button !== undefined && e.button !== 0) return;
  try {
    (e.currentTarget as HTMLElement).setPointerCapture?.(e.pointerId);
  } catch {
    /* not supported */
  }
  const v = props.voice;
  if (v.state === "listening") {
    ignoreUp = true;
    v.stop();
    return;
  }
  if (v.state === "thinking") return;
  ignoreUp = false;
  held = true;
  pressedAt = Date.now();
  v.start();
}
function onRelease() {
  if (ignoreUp) {
    ignoreUp = false;
    return;
  }
  if (!held) return;
  held = false;
  if (props.voice.state === "listening" && Date.now() - pressedAt >= VOICE_TIMING.hold) props.voice.stop();
}
function onKey(e: KeyboardEvent) {
  if (e.repeat) return;
  const v = props.voice;
  if (v.state === "listening") v.stop();
  else if (v.state !== "thinking") v.start();
}
</script>

<template>
  <div :class="['cs-' + voice.state, { live: active }]" class="capsule">
    <transition name="ceo-pop">
      <div v-if="active" aria-live="polite" class="capsule-card" role="status">
        <div class="cc-head">
          <VoiceIndicator :followup="voice.followup" :muted="voice.muted" :state="voice.state" />
          <span class="cc-state">{{ stateText }}</span>
          <span v-if="voice.countdown" class="cc-count" title="پنجره‌ی پرسش بعدی">{{ faNum(voice.countdown, 0) }}</span>
          <button aria-label="توقف (Esc)" class="cc-stop" title="توقف (Esc)" type="button" @click="voice.cancel()"><CeoIcon :size="16" name="close" /></button>
        </div>
        <div v-if="voice.state === 'thinking'" class="cc-progress"><span /></div>
        <VoiceWave v-if="voice.state === 'listening'" :height="38" :voice="voice" />
        <p v-if="line" class="cc-caption">{{ line }}</p>
      </div>
    </transition>
    <div class="capsule-bar">
      <button
        v-if="voice.supported"
        :aria-label="micLabel"
        :aria-pressed="voice.state === 'listening'"
        :disabled="voice.state === 'thinking'"
        :title="micLabel"
        class="cap-mic"
        type="button"
        @contextmenu.prevent
        @keydown.enter.prevent="onKey"
        @keydown.space.prevent="onKey"
        @pointercancel="onRelease"
        @pointerdown.prevent="onPress"
        @pointerup="onRelease">
        <VoiceIndicator v-if="voice.state !== 'idle'" :followup="voice.followup" :muted="voice.muted" :state="voice.state" />
        <svg v-else fill="none" height="20" stroke="currentColor" stroke-linecap="round" stroke-width="1.7" viewBox="0 0 20 20" width="20" aria-hidden="true">
          <rect height="9.5" rx="3" width="6" x="7" y="2.5" />
          <path d="M4.5 9.5a5.5 5.5 0 0 0 11 0M10 15v2.5" />
        </svg>
      </button>
      <button aria-label="باز کردن دستیار هوشمند" class="cap-open" type="button" @click="emit('open-drawer')">
        <CeoIcon :size="18" name="assistant" />
        <span>دستیار هوشمند</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.capsule {
  position: fixed;
  left: 20px;
  bottom: 20px;
  z-index: 1030;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 10px;
  --ai-accent: var(--ceo-on-primary);
}
.capsule.live {
  z-index: 1046;
}
.capsule-bar {
  display: inline-flex;
  align-items: stretch;
  height: 46px;
  border-radius: 999px;
  background: var(--ceo-primary-btn);
  color: var(--ceo-on-primary);
  box-shadow: 0 6px 20px rgba(10, 18, 26, 0.22);
  overflow: visible;
}
.cap-mic,
.cap-open {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  border: 0;
  background: transparent;
  color: inherit;
  font: inherit;
  font-size: 13px;
  font-weight: 500;
  line-height: 20px;
  cursor: pointer;
}
/* the microphone at the start (right) end of the capsule, the drawer button at the end (left): logical corners follow the page direction */
.cap-mic {
  position: relative;
  justify-content: center;
  width: 50px;
  border-start-start-radius: 999px;
  border-end-start-radius: 999px;
  border-inline-end: 1px solid rgba(255, 255, 255, 0.22);
}
.cap-open {
  padding-inline: 14px 18px;
  border-start-end-radius: 999px;
  border-end-end-radius: 999px;
}
.cap-mic:hover,
.cap-open:hover {
  background: rgba(255, 255, 255, 0.1);
}
.cap-mic:focus-visible,
.cap-open:focus-visible {
  outline: 2px solid var(--ceo-focus);
  outline-offset: 3px;
}
/* listening: the microphone button glows red with a slow ring (no ring with reduced motion) */
.cs-listening .cap-mic {
  background: var(--ceo-danger);
  color: #fff;
  --ai-live: #fff;
  --ai-live-ring: rgba(255, 255, 255, 0.55);
  --ai-accent: #fff;
  --ai-accent-ring: rgba(255, 255, 255, 0.5);
}
.cs-speaking .cap-mic,
.cs-thinking .cap-mic {
  --ai-accent: var(--ceo-on-primary);
  --ai-line: rgba(255, 255, 255, 0.3);
}
.capsule-card {
  width: min(360px, calc(100vw - 40px));
  padding: 10px 12px 12px;
  border: 1px solid var(--ceo-line);
  border-radius: 12px;
  background: var(--ceo-card);
  color: var(--ceo-ink);
  box-shadow: 0 10px 32px rgba(10, 18, 26, 0.2);
  --ai-accent: var(--ceo-primary);
  --ai-wave: var(--ceo-primary);
  --ai-line: var(--ceo-line);
}
.cc-head {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  line-height: 20px;
  color: var(--ceo-muted);
}
.cc-state {
  flex: 1;
}
.cc-count {
  min-width: 24px;
  height: 24px;
  border-radius: 50%;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--ceo-primary);
  color: var(--ceo-primary);
  font-weight: 500;
}
.cc-stop {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: var(--ceo-muted);
  cursor: pointer;
}
.cc-stop:hover {
  background: var(--ceo-card-hover);
  color: var(--ceo-ink);
}
.cc-progress {
  position: relative;
  height: 2px;
  margin: 8px 0 2px;
  border-radius: 1px;
  overflow: hidden;
  background: var(--ceo-line);
}
.cc-progress span {
  position: absolute;
  top: 0;
  bottom: 0;
  width: 35%;
  background: var(--ceo-primary);
  animation: cc-run 1.1s ease-in-out infinite;
}
@keyframes cc-run {
  0% {
    right: -35%;
  }
  100% {
    right: 100%;
  }
}
.cc-caption {
  margin: 8px 0 0;
  max-height: 120px;
  overflow-y: auto;
  font-size: 14px;
  line-height: 24px;
  color: var(--ceo-ink);
}
.ceo-pop-enter-active,
.ceo-pop-leave-active {
  transition: opacity 160ms ease, transform 160ms ease;
}
.ceo-pop-enter-from,
.ceo-pop-leave-to {
  opacity: 0;
  transform: translateY(6px);
}
@media (prefers-reduced-motion: reduce) {
  .cc-progress span {
    animation: none;
    right: 0;
    width: 100%;
    opacity: 0.6;
  }
  .ceo-pop-enter-active,
  .ceo-pop-leave-active {
    transition: none;
  }
}
@media print {
  .capsule {
    display: none !important;
  }
}
@media (max-width: 640px) {
  .capsule {
    left: 16px;
    bottom: 16px;
  }
  .capsule-bar {
    height: 52px;
  }
  .cap-mic {
    width: 56px;
  }
  .cap-open {
    padding: 0 14px;
  }
  /* on a phone the second button is the icon alone (its name is read out by aria-label) */
  .cap-open span {
    display: none;
  }
}
</style>
