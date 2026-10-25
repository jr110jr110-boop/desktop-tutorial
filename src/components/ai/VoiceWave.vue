<script setup lang="ts">
// The waveform of the microphone while the assistant listens (Web Audio AnalyserNode of the voice session): the loudness of the last few seconds as bars, twenty a second,
// the newest at the left edge — time runs right to left like the page's charts, so a pause shows as a flat stretch growing from the left, which is what ends the question.
// With reduced motion it becomes a plain level bar updated a few times a second. The colour is the CSS variable --ai-wave (default steel blue).
import { onBeforeUnmount, onMounted, ref } from "vue";
import type { VoiceSession } from "@/composables/voice";

const props = defineProps({
  voice: { type: Object as () => VoiceSession, required: true },
  height: { type: Number, default: 44 },
});

const canvas = ref<HTMLCanvasElement | null>(null);
const reduce = (() => {
  try {
    return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  } catch {
    return false;
  }
})();
let frame = 0;
let timer: ReturnType<typeof setInterval> | null = null;
const buf = new Uint8Array(1024);
const levelPct = ref(0);
/** peak loudness 0..1 of the last samples, newest first */
const history: number[] = [];
let sampledAt = 0;

function draw(now: number) {
  frame = requestAnimationFrame(draw);
  const c = canvas.value;
  if (!c) return;
  const dpr = Math.max(1, window.devicePixelRatio || 1);
  const w = c.clientWidth;
  const h = c.clientHeight;
  if (!w || !h) return; // hidden (the drawer is closed): nothing to draw
  if (c.width !== Math.round(w * dpr) || c.height !== Math.round(h * dpr)) {
    c.width = Math.round(w * dpr);
    c.height = Math.round(h * dpr);
  }
  const g = c.getContext("2d");
  if (!g) return;
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  g.clearRect(0, 0, w, h);
  const color = getComputedStyle(c).getPropertyValue("--ai-wave").trim() || "#245c8c";
  const bars = Math.max(12, Math.floor(w / 4));
  if (now - sampledAt >= 50) {
    sampledAt = now;
    let peak = 0;
    if (props.voice.readWave(buf)) for (let j = 0; j < buf.length; j++) peak = Math.max(peak, Math.abs(buf[j] - 128) / 128);
    history.unshift(peak);
    if (history.length > bars) history.length = bars;
  }
  // bars mirrored around the middle line; the square root keeps quiet speech visible next to loud speech
  const slot = w / bars;
  g.fillStyle = color;
  for (let i = 0; i < bars; i++) {
    const amp = Math.max(0.05, Math.min(1, Math.sqrt(history[i] || 0) * 1.35));
    const bh = Math.max(2, amp * (h - 4));
    g.globalAlpha = history[i] ? 0.4 + 0.6 * amp : 0.3;
    g.fillRect(i * slot + 0.5, (h - bh) / 2, Math.max(1.5, slot - 1.5), bh);
  }
  g.globalAlpha = 1;
}

onMounted(() => {
  if (reduce) timer = setInterval(() => (levelPct.value = Math.round(props.voice.level * 100)), 250);
  else frame = requestAnimationFrame(draw);
});
onBeforeUnmount(() => {
  cancelAnimationFrame(frame);
  if (timer) clearInterval(timer);
});
</script>

<template>
  <div class="voice-wave" :style="'height: ' + height + 'px'" aria-hidden="true">
    <div v-if="reduce" class="voice-level"><span :style="'width: ' + levelPct + '%'" /></div>
    <canvas v-else ref="canvas" />
  </div>
</template>

<style scoped>
.voice-wave {
  width: 100%;
}
.voice-wave canvas {
  display: block;
  width: 100%;
  height: 100%;
}
.voice-level {
  height: 6px;
  margin-top: 16px;
  border-radius: 3px;
  background: var(--ai-line, #dfe3e8);
  overflow: hidden;
}
.voice-level span {
  display: block;
  height: 100%;
  background: var(--ai-wave, #245c8c);
}
</style>
