<script setup lang="ts">
// The assistant beside the dashboard: a drawer on the left edge (the end side in right-to-left) hosting the existing AssistantPanel — the same conversation, voice, citations
// and verification as everywhere else; this drawer only adds the suggested questions of the visible sections, which prefill the question box (nothing is sent by itself).
// On a wide screen the drawer sits beside the page (the page makes room); on a tablet or a phone it slides over the page with a scrim. The panel is created on the first opening
// and kept while the page is open, so closing the drawer does not lose the conversation.
import { computed, nextTick, ref, watch } from "vue";
import AssistantPanel from "@/components/ai/AssistantPanel.vue";
import CeoIcon from "@/components/ceo/CeoIcon.vue";

const props = defineProps({
  open: { type: Boolean, default: false },
  /** beside the page (true) or over it (false) */
  beside: { type: Boolean, default: false },
  /** suggested questions, most urgent section first: [{ key, title, questions }] */
  suggestions: { type: Array as () => { key: string; title: string; questions: string[] }[], default: () => [] },
});
const emit = defineEmits<{ (e: "close"): void }>();

const created = ref(false);
const panel = ref<InstanceType<typeof AssistantPanel> | null>(null);
const drawer = ref<HTMLElement | null>(null);
const pending = ref<string | null>(null);
let opener: HTMLElement | null = null;

watch(
  () => props.open,
  async (open) => {
    if (open) {
      opener = document.activeElement as HTMLElement | null;
      created.value = true;
      await nextTick();
      if (pending.value) {
        const q = pending.value;
        pending.value = null;
        (panel.value as any)?.prefill?.(q);
      } else drawer.value?.querySelector<HTMLTextAreaElement>("textarea")?.focus();
    } else opener?.focus?.();
  },
  { immediate: true }
);

/** Writes a question into the box (the drawer may still be opening). */
function ask(question: string) {
  if (!created.value || !panel.value) {
    pending.value = question;
    return;
  }
  nextTick(() => (panel.value as any)?.prefill?.(question));
}
defineExpose({ ask });

const flat = computed(() => props.suggestions.flatMap((s) => s.questions.slice(0, 1).map((q) => ({ key: s.key, section: s.title, q }))));
const showAll = ref(false);
const visible = computed(() => (showAll.value ? props.suggestions.flatMap((s) => s.questions.map((q) => ({ key: s.key, section: s.title, q }))) : flat.value.slice(0, 6)));

function onKey(e: KeyboardEvent) {
  if (e.key === "Escape") {
    e.preventDefault();
    emit("close");
  }
}
</script>

<template>
  <div class="assist-root">
    <transition name="ceo-scrim">
      <div v-if="open && !beside" class="assist-scrim" @click="emit('close')" />
    </transition>
    <aside
      v-show="open"
      ref="drawer"
      :aria-label="beside ? 'دستیار هوشمند' : undefined"
      :aria-modal="beside ? undefined : 'true'"
      :class="{ beside }"
      :role="beside ? 'complementary' : 'dialog'"
      class="assist-drawer"
      dir="rtl"
      @keydown="onKey">
      <div class="assist-bar">
        <button aria-label="بستن دستیار" class="ceo-icon-btn" title="بستن دستیار (Esc)" type="button" @click="emit('close')"><CeoIcon name="close" /></button>
      </div>
      <div class="assist-body">
        <AssistantPanel v-if="created" ref="panel">
          <template #suggestions>
            <div v-if="visible.length" class="assist-chips">
              <span class="assist-chips-title">پرسش‌های پیشنهادی</span>
              <div class="assist-chip-list">
                <button v-for="(c, i) in visible" :key="i" :title="'بخش ' + c.section" class="ceo-chip" type="button" @click="ask(c.q)">{{ c.q }}</button>
                <button v-if="suggestions.length" class="ceo-chip more" type="button" @click="showAll = !showAll">{{ showAll ? "پرسش‌های کمتر" : "همه‌ی پرسش‌ها" }}</button>
              </div>
            </div>
          </template>
        </AssistantPanel>
      </div>
    </aside>
  </div>
</template>

<style scoped>
.assist-scrim {
  position: fixed;
  inset: 0;
  z-index: 1039;
  background: var(--ceo-scrim);
}
.assist-drawer {
  position: fixed;
  top: 52px;
  bottom: 0;
  left: 0;
  z-index: 1040;
  width: min(420px, 100vw);
  display: flex;
  flex-direction: column;
  background: var(--ceo-card);
  border-right: 1px solid var(--ceo-line);
  box-shadow: 18px 0 48px rgba(10, 18, 26, 0.16);
  color: var(--ceo-ink);
  text-align: right;
}
.assist-drawer.beside {
  width: var(--ceo-drawer-w, 400px);
  box-shadow: none;
}
.assist-bar {
  display: flex;
  justify-content: flex-end;
  padding: 10px 12px 0;
}
.assist-body {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  padding: 8px 16px 16px;
}
.assist-body :deep(.ai-panel) {
  flex: 1;
  min-height: 0;
}
.assist-body :deep(.ai-list) {
  flex: 1;
  max-height: none;
  min-height: 120px;
}
.assist-chips {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding-top: 8px;
  border-top: 1px solid var(--ceo-line);
}
.assist-chips-title {
  font-size: 12px;
  line-height: 18px;
  color: var(--ceo-muted);
}
.assist-chip-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  max-height: 148px;
  overflow-y: auto;
}
.ceo-chip.more {
  border-style: dashed;
}
@media (max-width: 640px) {
  .assist-drawer {
    top: 0;
  }
}
</style>
