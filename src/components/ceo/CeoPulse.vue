<script setup lang="ts">
// The pulse of the nine modules drawn as a control-room annunciator — the page's one bold device: a graphite instrument holding one equal window per module.
// Each window has a status dot, the module's name and its counts in words. A window in order stays unlit (quiet graphite); a window with warnings glows amber and one with
// critical items glows red (a soft inner glow and a 2px lamp bar along its top edge). A window is a button: it scrolls to its section, and lifts a little under the pointer.
// On the first load the panel runs a short lamp test (every window lights in turn, right to left, then settles) — the page's only motion that is not a reply to an action;
// it is skipped when the person asks for reduced motion.
import { computed, onMounted, ref } from "vue";
import type { CeoPulse } from "@/api/CeoAPIs";
import { faNum, statusTitles } from "@/composables/ceo";

const props = defineProps({
  pulse: { type: Array as () => CeoPulse[], required: true },
  lampTest: { type: Boolean, default: false },
});
const emit = defineEmits<{ (e: "go", key: string): void }>();

const testing = ref(false);
onMounted(() => {
  if (!props.lampTest) return;
  let reduce = false;
  try {
    reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  } catch {
    reduce = false;
  }
  if (reduce) return;
  testing.value = true;
  window.setTimeout(() => (testing.value = false), 1400);
});

const windows = computed(() =>
  props.pulse.map((p, i) => {
    const lines: { icon: string; text: string; tone: string }[] = [];
    if (p.dangerCount > 0) lines.push({ icon: "danger", text: `${faNum(p.dangerCount, 0)} بحرانی`, tone: "danger" });
    if (p.warnCount > 0) lines.push({ icon: "warn", text: `${faNum(p.warnCount, 0)} هشدار`, tone: "warn" });
    if (!lines.length) lines.push({ icon: p.status === "neutral" ? "neutral" : "ok", text: p.status === "neutral" ? "بدون داده" : "عادی", tone: p.status === "neutral" ? "neutral" : "ok" });
    const said = lines.map((l) => l.text).join("، ");
    return { ...p, i, lines, label: `${p.title}: ${said || statusTitles[p.status]}. رفتن به بخش ${p.title}` };
  })
);
</script>

<template>
  <nav :class="{ testing }" aria-label="وضعیت ماژول‌ها" class="annunciator">
    <ul class="ann-grid">
      <li v-for="w in windows" :key="w.key">
        <button :aria-label="w.label" :class="'lit-' + w.status" :style="'--i: ' + w.i" class="ann-window" type="button" @click="emit('go', w.key)">
          <span class="ann-head">
            <span aria-hidden="true" class="ann-dot" />
            <span class="ann-title">{{ w.title }}</span>
          </span>
          <span class="ann-state">
            <span v-for="l in w.lines" :key="l.tone" :class="'tone-' + l.tone" class="ann-line">{{ l.text }}</span>
          </span>
        </button>
      </li>
    </ul>
  </nav>
</template>

<style scoped>
/* the instrument: graphite frame, 18px corners, an inner shadow so the windows sit in it */
.annunciator {
  background: var(--ceo-bezel);
  border-radius: 18px;
  padding: 8px;
  box-shadow: var(--ceo-bezel-inset), var(--ceo-shadow);
}
.ann-grid {
  display: grid;
  grid-template-columns: repeat(9, minmax(0, 1fr));
  gap: 6px;
  list-style: none;
  margin: 0;
  padding: 0;
}
.ann-grid > li {
  display: flex;
}
.ann-window {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 4px;
  width: 100%;
  height: 100%;
  min-height: 88px;
  padding: 16px 8px 12px;
  border: 1px solid var(--ceo-tile-edge);
  border-radius: 10px;
  background-color: var(--ceo-tile);
  background-image: linear-gradient(180deg, rgba(255, 255, 255, 0.04), rgba(255, 255, 255, 0) 45%);
  color: var(--ceo-tile-ink);
  font: inherit;
  text-align: center;
  cursor: pointer;
  transition: transform 120ms ease, background-color 120ms ease, box-shadow 120ms ease;
}
/* the lamp bar along the top edge: dark when unlit, the state colour when lit */
.ann-window::before {
  content: "";
  position: absolute;
  top: -1px;
  right: 16px;
  left: 16px;
  height: 2px;
  border-radius: 0 0 2px 2px;
  background: transparent;
}
.ann-head {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}
.ann-dot {
  width: 8px;
  height: 8px;
  flex: none;
  border-radius: 50%;
  background: var(--ceo-tile-muted);
  opacity: 0.45;
}
.ann-title {
  font-size: 14px;
  line-height: 20px;
  font-weight: 500;
}
.ann-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  font-size: 13px;
  line-height: 20px;
  color: var(--ceo-tile-muted);
}
.ann-line {
  white-space: nowrap;
}
.ann-line.tone-warn {
  color: var(--ceo-glow-warn);
  font-weight: 500;
}
.ann-line.tone-danger {
  color: var(--ceo-glow-danger);
  font-weight: 500;
}
.lit-ok .ann-dot {
  background: var(--ceo-glow-ok);
  opacity: 0.9;
}
.lit-warn {
  border-color: var(--ceo-lit-warn-edge);
  background-image: radial-gradient(130% 100% at 50% 0%, var(--ceo-lit-warn-fill), transparent 72%), linear-gradient(180deg, rgba(255, 255, 255, 0.04), rgba(255, 255, 255, 0) 45%);
  box-shadow: inset 0 0 20px var(--ceo-lit-warn-glow);
}
.lit-warn::before {
  background: var(--ceo-glow-warn);
  box-shadow: 0 0 10px var(--ceo-glow-warn);
}
.lit-warn .ann-dot {
  background: var(--ceo-glow-warn);
  opacity: 1;
  box-shadow: 0 0 0 3px var(--ceo-lit-warn-fill), 0 0 10px var(--ceo-glow-warn);
}
.lit-danger {
  border-color: var(--ceo-lit-danger-edge);
  background-image: radial-gradient(130% 100% at 50% 0%, var(--ceo-lit-danger-fill), transparent 72%), linear-gradient(180deg, rgba(255, 255, 255, 0.04), rgba(255, 255, 255, 0) 45%);
  box-shadow: inset 0 0 20px var(--ceo-lit-danger-glow);
}
.lit-danger::before {
  background: var(--ceo-glow-danger);
  box-shadow: 0 0 10px var(--ceo-glow-danger);
}
.lit-danger .ann-dot {
  background: var(--ceo-glow-danger);
  opacity: 1;
  box-shadow: 0 0 0 3px var(--ceo-lit-danger-fill), 0 0 10px var(--ceo-glow-danger);
}
.ann-window:hover {
  transform: translateY(-1px);
  background-color: var(--ceo-tile-hover);
}
.ann-window:focus-visible {
  outline: 2px solid var(--ceo-focus-on-bezel);
  outline-offset: 2px;
}
/* lamp test: each window glows warm white in turn, then falls back to its own state (no end keyframe = back to the element's style) */
.testing .ann-window {
  animation: lamp-test 760ms ease-out both;
  animation-delay: calc(var(--i) * 55ms);
}
@keyframes lamp-test {
  0%,
  42% {
    border-color: var(--ceo-glow-test);
    box-shadow: inset 0 0 24px var(--ceo-lit-test-glow), 0 0 12px var(--ceo-lit-test-glow);
  }
}
@media (prefers-reduced-motion: reduce) {
  .testing .ann-window {
    animation: none;
  }
  .ann-window {
    transition: none;
  }
  .ann-window:hover {
    transform: none;
  }
}
/* three rows of three when the page is narrow (class from the page, which knows its own width) */
.annunciator.compact .ann-grid {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}
.annunciator.compact .ann-window {
  min-height: 72px;
  padding: 14px 6px 10px;
}
</style>
