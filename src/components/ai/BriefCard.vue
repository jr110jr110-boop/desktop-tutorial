<script setup lang="ts">
// The daily brief of the CEO seat (CEO only: the brief endpoints are refused for every other role). The text is read from brief/latest; the spoken brief is made on demand
// by brief/latest/audio and fetched as a blob with the Bearer token (never as a link).
import { nextTick, onBeforeUnmount, onMounted, ref } from "vue";
import { getAiBrief, getAiBriefAudio } from "@/api/AiAPIs";
import { fail } from "@/composables/inventory";

const brief = ref<any>(null);
const message = ref("");
const loading = ref(false);
const audioBusy = ref(false);
const audioUrl = ref<string | null>(null);
const player = ref<HTMLAudioElement | null>(null);

async function load() {
  loading.value = true;
  const [data, error] = await getAiBrief();
  loading.value = false;
  if (error) {
    brief.value = null;
    // no brief yet (or the AI is down) is not a failure of the page: the card says so and the dashboard works as before
    message.value = error.message || "گزارش روزانه در دسترس نیست.";
    return;
  }
  brief.value = data;
  message.value = "";
}

function dropAudio() {
  player.value?.pause();
  if (audioUrl.value) URL.revokeObjectURL(audioUrl.value);
  audioUrl.value = null;
}

async function play() {
  if (audioBusy.value) return;
  audioBusy.value = true;
  const [blob, error] = await getAiBriefAudio();
  audioBusy.value = false;
  if (error) return fail(error);
  dropAudio();
  audioUrl.value = URL.createObjectURL(blob);
  await nextTick();
  player.value?.play().catch(() => undefined); // if the browser refuses to start it, the player controls are visible
}

onMounted(load);
onBeforeUnmount(dropAudio);
</script>

<template>
  <div class="brief">
    <div class="d-flex flex-wrap gap-2 align-items-center justify-content-between mb-2">
      <h2 class="inv-title">گزارش روزانه</h2>
      <div class="d-flex gap-2 align-items-center">
        <span v-if="brief" class="inv-muted">{{ brief.dateText }}</span>
        <span v-if="brief && brief.verified" class="inv-badge ok">راستی‌آزمایی‌شده</span>
        <span v-else-if="brief" class="inv-badge bad">راستی‌آزمایی‌نشده</span>
        <button v-if="brief" :disabled="audioBusy" class="btn btn-sm btn-outline-primary" @click="play">
          <span v-if="audioBusy" class="spinner-border spinner-border-sm me-1"></span>پخش گزارش صوتی
        </button>
        <button :disabled="loading" class="btn btn-sm btn-outline-secondary" @click="load">بازخوانی</button>
      </div>
    </div>
    <p v-if="loading && !brief" class="inv-muted mb-0"><span class="spinner-border spinner-border-sm"></span> در حال بارگذاری...</p>
    <p v-else-if="!brief" class="inv-muted mb-0">{{ message }}</p>
    <div v-else class="brief-text">{{ brief.text }}</div>
    <audio v-if="audioUrl" ref="player" :src="audioUrl" class="w-100 mt-2" controls></audio>
  </div>
</template>

<style scoped>
.brief {
  direction: rtl;
  text-align: right;
}
.brief-text {
  white-space: pre-wrap;
  line-height: 1.9;
  overflow-wrap: anywhere;
}
</style>
