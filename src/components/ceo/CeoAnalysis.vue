<script setup lang="ts">
// The AI analysis of a section (or of the whole company) shown inside the page: the narrative, the verification badge (the numbers were checked against the data, or not),
// the citations as chips that open the cited page, the model name, and the standard degraded sentence when the model is down. Same rules as the assistant panel (AssistantPanel.vue).
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import type { CeoAnalysis } from "@/api/CeoAPIs";
import CeoIcon from "@/components/ceo/CeoIcon.vue";
import { DEGRADED_TEXT, NOT_VERIFIED_TEXT } from "@/composables/ai";
import { faNum, openableLink } from "@/composables/ceo";

const props = defineProps({
  result: { type: Object as () => CeoAnalysis | null, default: null },
  busy: { type: Boolean, default: false },
  error: { type: String, default: "" },
  startedAt: { type: Number, default: 0 },
});
const emit = defineEmits<{ (e: "retry"): void; (e: "close"): void }>();

const router = useRouter();
const now = ref(Date.now());
let timer: ReturnType<typeof setInterval> | null = null;
onMounted(() => (timer = setInterval(() => (now.value = Date.now()), 1000)));
onBeforeUnmount(() => timer && clearInterval(timer));
const seconds = computed(() => Math.max(0, Math.round((now.value - props.startedAt) / 1000)));

/** the model did not answer at all (no model, no tool): only the deterministic dashboard is left */
const trueDegraded = computed(() => !!props.result && props.result.degraded && !props.result.toolCalls?.length && !props.result.model);
const startsUnverified = computed(() => !!props.result && props.result.answer.trim().startsWith("پاسخ مدل قابل راستی‌آزمایی نبود"));
const isDegradedAnswer = computed(() => !!props.result && props.result.answer.trim() === DEGRADED_TEXT);
const chips = computed(() => (props.result?.citations || []).map((c) => ({ ...c, to: openableLink(router, c.link) })));
</script>

<template>
  <div aria-live="polite" class="analysis">
    <div v-if="busy" class="analysis-busy">
      <span aria-hidden="true" class="spinner-border spinner-border-sm" />
      <span>در حال تحلیل داده‌ها… {{ faNum(seconds, 0) }} ثانیه</span>
    </div>
    <div v-else-if="error" class="analysis-error">
      <span>تحلیل انجام نشد: {{ error }}</span>
      <button class="ceo-btn ghost" type="button" @click="emit('retry')">تلاش دوباره</button>
    </div>
    <template v-else-if="result">
      <div class="analysis-meta">
        <span class="analysis-who"><CeoIcon :size="16" name="analysis" /> تحلیل هوشمند</span>
        <span v-if="trueDegraded" class="ceo-badge warn">حالت کاهش‌یافته</span>
        <span v-else-if="result.verified" class="ceo-badge ok" title="اعداد این تحلیل با داده‌های سامانه سنجیده شد"><CeoIcon :size="13" name="ok" /> راستی‌آزمایی‌شده</span>
        <span v-else class="ceo-badge bad"><CeoIcon :size="13" name="warn" /> راستی‌آزمایی‌نشده</span>
        <span v-if="result.model" class="analysis-model">{{ result.model }}</span>
        <button aria-label="بستن تحلیل" class="ceo-icon-btn small" title="بستن تحلیل" type="button" @click="emit('close')"><CeoIcon :size="16" name="close" /></button>
      </div>
      <p v-if="trueDegraded && !isDegradedAnswer" class="analysis-degraded">{{ DEGRADED_TEXT }}</p>
      <p v-if="!result.verified && !trueDegraded && !startsUnverified" class="analysis-unverified">{{ NOT_VERIFIED_TEXT }}</p>
      <div :class="{ 'analysis-degraded': trueDegraded && isDegradedAnswer, 'analysis-unverified': !result.verified && startsUnverified }" class="analysis-text">{{ result.answer }}</div>
      <div v-if="chips.length" class="analysis-chips">
        <span class="analysis-chips-label">منابع:</span>
        <button v-for="(c, i) in chips" :key="i" :disabled="!c.to" :title="c.to ? 'باز کردن ' + c.title : 'صفحه‌ی این مورد برای نقش شما در دسترس نیست'" class="ceo-chip" type="button" @click="c.to && router.push(c.to)">
          {{ c.title || c.type + " " + c.id }}
        </button>
      </div>
    </template>
  </div>
</template>

<style scoped>
/* an inner panel of its card: the soft cell fill, 10px corners, no shadow */
.analysis {
  padding: 16px 20px;
  border: 1px solid var(--ceo-line-soft);
  border-radius: 10px;
  background: var(--ceo-cell);
}
.analysis-busy,
.analysis-error {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  font-size: 13px;
  line-height: 20px;
  color: var(--ceo-muted);
}
.analysis-error {
  color: var(--ceo-danger-ink);
}
.analysis-meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px 10px;
  margin-bottom: 8px;
}
.analysis-who {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  line-height: 20px;
  font-weight: 500;
  color: var(--ceo-primary);
}
.analysis-model {
  font-size: 12px;
  color: var(--ceo-muted);
}
.analysis-meta .ceo-icon-btn {
  margin-inline-start: auto;
}
.analysis-text {
  white-space: pre-wrap;
  font-size: 14px;
  line-height: 26px;
  color: var(--ceo-ink);
  overflow-wrap: anywhere;
  max-width: 72ch;
}
.analysis-unverified {
  border-radius: 8px;
  border-right: 3px solid var(--ceo-danger);
  background: var(--ceo-danger-soft);
  color: var(--ceo-danger-ink);
  padding: 6px 10px;
  margin: 0 0 8px;
  font-size: 13px;
  line-height: 20px;
}
.analysis-degraded {
  border-radius: 8px;
  border-right: 3px solid var(--ceo-warn);
  background: var(--ceo-warn-soft);
  color: var(--ceo-warn-ink);
  padding: 6px 10px;
  margin: 0 0 8px;
  font-size: 13px;
  line-height: 20px;
}
.analysis-chips {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  margin-top: 10px;
}
.analysis-chips-label {
  font-size: 12px;
  color: var(--ceo-muted);
}
</style>
