<script setup lang="ts">
// The pulse of the nine modules drawn as a control-room annunciator: a dark bezel holding one labelled window per module. A window "lights" amber (warn) or red (danger)
// with its shape icon and the counts in words, stays unlit when the module is in order, and shows a dash when there is nothing to judge. A window is a button: it scrolls to its section.
// On the first load the panel runs a short lamp test (every window lights in turn, right to left, then settles) — the page's only motion that is not a reply to an action;
// it is skipped when the person asks for reduced motion.
import { computed, onMounted, ref } from "vue";
import type { CeoPulse } from "@/api/CeoAPIs";
import CeoIcon from "@/components/ceo/CeoIcon.vue";
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
          <span class="ann-title">{{ w.title }}</span>
          <span class="ann-state">
            <span v-for="l in w.lines" :key="l.tone" :class="'tone-' + l.tone" class="ann-line">
              <CeoIcon :name="l.icon" :size="14" />
              <span>{{ l.text }}</span>
            </span>
          </span>
        </button>
      </li>
    </ul>
  </nav>
</template>

<style scoped>
.annunciator {
  background: var(--ceo-bezel);
  border-radius: 12px;
  padding: 7px;
  box-shadow: 0 0 0 1px var(--ceo-bezel-ring), inset 0 1px 0 var(--ceo-bezel-edge);
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
  min-height: 80px;
  padding: 15px 6px 10px;
  border: 1px solid var(--ceo-acrylic-edge);
  border-radius: 5px;
  background: var(--ceo-acrylic);
  color: var(--ceo-ink);
  font: inherit;
  text-align: center;
  cursor: pointer;
  box-shadow: inset 0 1px 0 var(--ceo-acrylic-shine);
}
/* the lamp line along the top edge of each window */
.ann-window::before {
  content: "";
  position: absolute;
  top: 5px;
  right: 10px;
  left: 10px;
  height: 3px;
  border-radius: 2px;
  background: var(--ceo-lamp-off);
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
  gap: 1px;
  font-size: 12px;
  line-height: 18px;
}
.ann-line {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: var(--ceo-muted);
  white-space: nowrap;
}
.ann-line.tone-ok .ceo-icon {
  color: var(--ceo-ok);
}
.ann-line.tone-warn {
  color: var(--ceo-warn-ink);
  font-weight: 500;
}
.ann-line.tone-danger {
  color: var(--ceo-danger-ink);
  font-weight: 500;
}
.lit-ok::before {
  background: var(--ceo-ok);
  opacity: 0.55;
}
.lit-warn {
  background: var(--ceo-lit-warn);
  border-color: var(--ceo-lit-warn-edge);
  box-shadow: inset 0 1px 0 var(--ceo-acrylic-shine), inset 0 0 14px var(--ceo-lit-warn-glow);
}
.lit-warn::before {
  background: var(--ceo-warn);
}
.lit-danger {
  background: var(--ceo-lit-danger);
  border-color: var(--ceo-lit-danger-edge);
  box-shadow: inset 0 1px 0 var(--ceo-acrylic-shine), inset 0 0 14px var(--ceo-lit-danger-glow);
}
.lit-danger::before {
  background: var(--ceo-danger);
}
.ann-window:hover {
  filter: brightness(0.985);
}
.ann-window:focus-visible {
  outline: 2px solid var(--ceo-focus-on-bezel);
  outline-offset: 2px;
}
/* lamp test: each window lights in turn, then falls back to its own state (no end keyframe = back to the element's style) */
.testing .ann-window {
  animation: lamp-test 760ms ease-out both;
  animation-delay: calc(var(--i) * 55ms);
}
@keyframes lamp-test {
  0%,
  42% {
    background-color: var(--ceo-lamp-test);
    border-color: var(--ceo-lamp-test-edge);
    box-shadow: inset 0 0 18px var(--ceo-lamp-test-glow);
  }
}
@media (prefers-reduced-motion: reduce) {
  .testing .ann-window {
    animation: none;
  }
}
/* three rows of three when the page is narrow (class from the page, which knows its own width) */
.annunciator.compact .ann-grid {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}
.annunciator.compact .ann-window {
  min-height: 66px;
  padding: 13px 6px 8px;
}
</style>
