<script setup lang="ts">
// The calm state mark of the voice session: listening = a dot with a slow pulsing ring, thinking = a thin running bar, speaking = a small equaliser, idle = a still microphone.
// The only motion of the CEO page besides the lamp test; with reduced motion every mark is still (the words beside it carry the state). An answer that is only shown
// (no audio, no Persian voice: muted) keeps the equaliser still, so nothing pretends to be heard.
import type { VoiceState } from "@/composables/voice";

defineProps({
  state: { type: String as () => VoiceState, default: "idle" },
  /** the follow-up window of continuous conversation (listening, quieter look) */
  followup: { type: Boolean, default: false },
  /** speaking without sound (the caption only) */
  muted: { type: Boolean, default: false },
});
</script>

<template>
  <span :class="['vi-' + state, { followup, muted }]" aria-hidden="true" class="voice-ind">
    <template v-if="state === 'listening'"><span class="vi-dot" /></template>
    <template v-else-if="state === 'thinking'"><span class="vi-track"><span class="vi-run" /></span></template>
    <template v-else-if="state === 'speaking'"><span v-for="i in 4" :key="i" :style="'--d: ' + i" class="vi-bar" /></template>
    <template v-else>
      <svg fill="none" height="16" stroke="currentColor" stroke-linecap="round" stroke-width="1.6" viewBox="0 0 20 20" width="16">
        <rect height="9.5" rx="3" width="6" x="7" y="2.5" />
        <path d="M4.5 9.5a5.5 5.5 0 0 0 11 0M10 15v2.5" />
      </svg>
    </template>
  </span>
</template>

<style scoped>
.voice-ind {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 2px;
  width: 22px;
  height: 16px;
  flex: none;
  color: var(--ai-accent, #245c8c);
}
.vi-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--ai-live, #b4382f);
  box-shadow: 0 0 0 0 var(--ai-live-ring, rgba(180, 56, 47, 0.45));
  animation: vi-pulse 1.6s ease-out infinite;
}
.followup .vi-dot {
  background: var(--ai-accent, #245c8c);
  box-shadow: 0 0 0 0 var(--ai-accent-ring, rgba(36, 92, 140, 0.4));
}
@keyframes vi-pulse {
  0% {
    box-shadow: 0 0 0 0 var(--ai-live-ring, rgba(180, 56, 47, 0.45));
  }
  70% {
    box-shadow: 0 0 0 7px transparent;
  }
  100% {
    box-shadow: 0 0 0 0 transparent;
  }
}
.vi-track {
  position: relative;
  width: 22px;
  height: 2px;
  border-radius: 1px;
  overflow: hidden;
  background: var(--ai-line, #dfe3e8);
}
.vi-run {
  position: absolute;
  top: 0;
  bottom: 0;
  width: 45%;
  background: var(--ai-accent, #245c8c);
  animation: vi-run 1.1s ease-in-out infinite;
}
@keyframes vi-run {
  0% {
    right: -45%;
  }
  100% {
    right: 100%;
  }
}
.vi-bar {
  width: 3px;
  height: 14px;
  border-radius: 1.5px;
  background: var(--ai-accent, #245c8c);
  transform-origin: center;
  animation: vi-eq 0.9s ease-in-out infinite;
  animation-delay: calc(var(--d) * -0.22s);
}
@keyframes vi-eq {
  0%,
  100% {
    transform: scaleY(0.3);
  }
  50% {
    transform: scaleY(1);
  }
}
.muted .vi-bar {
  animation: none;
  transform: scaleY(0.35);
  opacity: 0.6;
}
@media (prefers-reduced-motion: reduce) {
  .vi-dot,
  .vi-run,
  .vi-bar {
    animation: none;
  }
  .vi-run {
    right: 0;
    width: 100%;
    opacity: 0.6;
  }
  .vi-bar {
    transform: scaleY(0.6);
  }
}
</style>
