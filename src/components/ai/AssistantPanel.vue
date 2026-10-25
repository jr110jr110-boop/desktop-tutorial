<script setup lang="ts">
// The assistant (Local AI, release A1): a text box and a push-to-talk button. The answer, the verified badge, the citations that open the cited page, the tools that were called
// (collapsed) and the spoken answer. The assistant acts as the signed-in user and never approves anything; this panel has no approve action.
// The microphone is asked for only when the person presses the button and is released as soon as the recording stops (no always-on microphone).
import { computed, nextTick, onBeforeUnmount, ref } from "vue";
import { useRouter } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import { aiChat, aiVoice, getAiConversation, getAiConversations } from "@/api/AiAPIs";
import { DEGRADED_TEXT, frontLink, NOT_VERIFIED_TEXT } from "@/composables/ai";
import { usePagedList } from "@/composables/fn";
import { fail, jalali } from "@/composables/inventory";

const props = defineProps({ compact: { type: Boolean, default: false } });

type Turn = {
  key: number;
  via: "text" | "voice" | "history";
  question: string;
  answer: string;
  citations: { type: string; id: string; title: string; link: string }[];
  toolCalls: { name: string; allowed: boolean }[];
  verified: boolean | null;
  degraded: boolean;
  model: string | null;
  audioUrl: string | null;
  at?: string | null;
};

const router = useRouter();
const turns = ref<Turn[]>([]);
const conversationId = ref<number | null>(null);
const text = ref("");
const busy = ref(false);
const listEl = ref<HTMLElement | null>(null);
let seq = 0;

// ---------------------------------------------------------------- history drawer
const drawer = ref(false);
const history = usePagedList(getAiConversations, ref({}), 20);
function openHistory() {
  drawer.value = true;
  history.load();
}
async function openConversation(id: number) {
  const [data, error] = await getAiConversation(id);
  if (error) return fail(error);
  dropAudio();
  const out: Turn[] = [];
  let pending: Turn | null = null;
  for (const m of data.messageList || []) {
    if (m.role === "User") {
      pending = { key: ++seq, via: "history", question: m.text, answer: "", citations: [], toolCalls: [], verified: null, degraded: false, model: null, audioUrl: null, at: m.createdAt };
      out.push(pending);
    } else if (m.role === "Assistant") {
      if (!pending) {
        pending = { key: ++seq, via: "history", question: "", answer: "", citations: [], toolCalls: [], verified: null, degraded: false, model: null, audioUrl: null };
        out.push(pending);
      }
      pending.answer = m.text;
      pending.verified = m.verified ?? null;
      pending.model = m.model || null;
      pending = null;
    }
  }
  turns.value = out;
  conversationId.value = data.id;
  drawer.value = false;
  scrollDown();
}

function freshConversation() {
  if (busy.value || recording.value) return;
  dropAudio();
  turns.value = [];
  conversationId.value = null;
  text.value = "";
}

// ---------------------------------------------------------------- text
function scrollDown() {
  nextTick(() => {
    if (listEl.value) listEl.value.scrollTop = listEl.value.scrollHeight;
  });
}

function addTurn(via: Turn["via"], question: string, r: any, audioUrl: string | null) {
  conversationId.value = r.conversationId ?? conversationId.value;
  turns.value.push({
    key: ++seq,
    via,
    question,
    answer: r.answer || "",
    citations: r.citations || [],
    toolCalls: r.toolCalls || [],
    verified: !!r.verified,
    degraded: !!r.degraded,
    model: r.model || null,
    audioUrl,
  });
  scrollDown();
}

async function send() {
  const question = text.value.trim();
  if (!question || busy.value || recording.value) return;
  if (question.length > 2000) return fail("متن پرسش نباید از ۲۰۰۰ نویسه بیشتر باشد.");
  busy.value = true;
  const [r, error] = await aiChat({ conversationId: conversationId.value, text: question });
  busy.value = false;
  if (error) return fail(error);
  text.value = "";
  addTurn("text", question, r, null);
}

function onEnter(e: KeyboardEvent) {
  if (e.shiftKey) return;
  e.preventDefault();
  send();
}

// ---------------------------------------------------------------- the spoken answer
const player = ref<HTMLAudioElement | null>(null);
const playing = ref<number | null>(null);
const urls: string[] = [];
function audioUrlOf(base64: string | null | undefined): string | null {
  if (!base64) return null;
  try {
    const bin = atob(base64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    const url = URL.createObjectURL(new Blob([bytes], { type: "audio/wav" }));
    urls.push(url);
    return url;
  } catch {
    return null;
  }
}
function dropAudio() {
  player.value?.pause();
  playing.value = null;
  while (urls.length) URL.revokeObjectURL(urls.pop()!);
}
async function play(t: Turn) {
  if (!t.audioUrl || !player.value) return;
  player.value.src = t.audioUrl;
  playing.value = t.key;
  try {
    await player.value.play();
  } catch {
    playing.value = null; // the browser refused to start the sound without a click: the button is the click
  }
}
function stopPlaying() {
  player.value?.pause();
  playing.value = null;
}

// ---------------------------------------------------------------- push to talk
const recording = ref(false);
const starting = ref(false);
const seconds = ref(0);
const micSupported = typeof navigator !== "undefined" && !!navigator.mediaDevices?.getUserMedia && typeof MediaRecorder !== "undefined";
let recorder: MediaRecorder | null = null;
let stream: MediaStream | null = null;
let chunks: BlobPart[] = [];
let timer: ReturnType<typeof setInterval> | null = null;
let pressedAt = 0;
let held = false;
let ignoreUp = false;
const HOLD_MS = 450;
const MAX_SECONDS = 120;
const MIN_BYTES = 800;

function releaseMic() {
  if (timer) clearInterval(timer);
  timer = null;
  stream?.getTracks().forEach((t) => t.stop());
  stream = null;
}

async function startRecording() {
  if (!micSupported) return fail("این مرورگر ضبط صدا را پشتیبانی نمی‌کند یا اتصال امن (HTTPS) ندارد.");
  if (busy.value || starting.value || recording.value) return;
  starting.value = true;
  try {
    stream = await navigator.mediaDevices.getUserMedia({ audio: true });
  } catch {
    starting.value = false;
    return fail("اجازه استفاده از میکروفون داده نشد.");
  }
  starting.value = false;
  const mime = typeof MediaRecorder.isTypeSupported === "function" && MediaRecorder.isTypeSupported("audio/webm") ? "audio/webm" : undefined;
  try {
    recorder = mime ? new MediaRecorder(stream, { mimeType: mime }) : new MediaRecorder(stream);
  } catch {
    releaseMic();
    return fail("ضبط صدا در این مرورگر آغاز نشد.");
  }
  chunks = [];
  recorder.ondataavailable = (e) => {
    if (e.data && e.data.size > 0) chunks.push(e.data);
  };
  recorder.onstop = onRecorded;
  recorder.start();
  recording.value = true;
  seconds.value = 0;
  timer = setInterval(() => {
    seconds.value++;
    if (seconds.value >= MAX_SECONDS) stopRecording();
  }, 1000);
  // the finger was lifted while the permission question was open: it was a click, so the recording goes on until the next click
}

function stopRecording() {
  if (recorder && recorder.state !== "inactive") recorder.stop();
  recording.value = false;
  releaseMic();
}

async function onRecorded() {
  const type = recorder?.mimeType || "audio/webm";
  const blob = new Blob(chunks, { type });
  chunks = [];
  recorder = null;
  if (blob.size < MIN_BYTES) return fail("صدایی ضبط نشد؛ دکمه را بیشتر نگه دارید یا دوباره بزنید.");
  busy.value = true;
  const [r, error] = await aiVoice(blob, "question.webm", conversationId.value);
  busy.value = false;
  if (error) return fail(error);
  const url = audioUrlOf(r.audioBase64);
  addTurn("voice", r.transcript || "", r, url);
  const last = turns.value[turns.value.length - 1];
  if (url && last) play(last);
}

function onPress(e: PointerEvent) {
  if (e.button !== undefined && e.button !== 0) return;
  // keep the pointer events coming to the button even when the finger slides off it while held
  try {
    (e.currentTarget as HTMLElement).setPointerCapture?.(e.pointerId);
  } catch {
    /* not supported: the release outside the button is then handled by pointercancel */
  }
  if (recording.value) {
    // click-to-stop
    ignoreUp = true;
    stopRecording();
    return;
  }
  ignoreUp = false;
  held = true;
  pressedAt = Date.now();
  startRecording();
}
function onRelease() {
  if (ignoreUp) {
    ignoreUp = false;
    return;
  }
  if (!held) return;
  held = false;
  // held down for a while = press-and-hold: stop now; a short press = click-to-start, the next click stops
  if (recording.value && Date.now() - pressedAt >= HOLD_MS) stopRecording();
}
function onKeyToggle(e: KeyboardEvent) {
  // keyboard: Enter / Space toggles
  if (e.repeat) return;
  if (recording.value) stopRecording();
  else startRecording();
}

const micLabel = computed(() => (recording.value ? "در حال ضبط... برای پایان رها کنید یا دوباره بزنید" : starting.value ? "منتظر اجازه میکروفون..." : "نگه دارید و صحبت کنید"));
const clock = computed(() => String(Math.floor(seconds.value / 60)).padStart(2, "0") + ":" + String(seconds.value % 60).padStart(2, "0"));

onBeforeUnmount(() => {
  if (recorder && recorder.state !== "inactive") {
    recorder.onstop = null;
    recorder.stop();
  }
  releaseMic();
  dropAudio();
});

// ---------------------------------------------------------------- citations
function linkOf(c: { link: string }) {
  return frontLink(router, c.link);
}
function openCitation(c: { link: string }) {
  const to = linkOf(c);
  if (to) router.push(to);
}
</script>

<template>
  <div class="ai-panel" :class="{ compact: props.compact }">
    <div class="ai-head">
      <h2 class="inv-title">دستیار هوشمند</h2>
      <div class="d-flex gap-2 flex-wrap">
        <button class="btn btn-sm btn-outline-secondary" :disabled="busy || recording" @click="freshConversation">گفت‌وگوی تازه</button>
        <button class="btn btn-sm btn-outline-secondary" @click="openHistory">تاریخچه</button>
      </div>
    </div>

    <div ref="listEl" class="ai-list">
      <p v-if="!turns.length && !busy" class="inv-muted mb-0">پرسش خود را بنویسید یا دکمه میکروفون را نگه دارید. دستیار با دسترسی‌های خود شما پاسخ می‌دهد و هیچ چیزی را تایید نمی‌کند.</p>

      <div v-for="t in turns" :key="t.key" class="ai-turn">
        <div v-if="t.question" class="ai-q">
          <span class="ai-who">{{ t.via === "voice" ? "شما گفتید" : "شما" }}</span>
          <span class="ai-text">{{ t.question }}</span>
        </div>
        <div class="ai-a">
          <div class="ai-meta">
            <span class="ai-who">دستیار</span>
            <span v-if="t.degraded" class="inv-badge warn">حالت کاهش‌یافته</span>
            <span v-else-if="t.verified === true" class="inv-badge ok" title="اعداد پاسخ با داده‌های سامانه سنجیده شد">راستی‌آزمایی‌شده</span>
            <span v-else-if="t.verified === false" class="inv-badge bad">راستی‌آزمایی‌نشده</span>
            <span v-if="t.model" class="ai-model inv-ltr">{{ t.model }}</span>
            <button v-if="t.audioUrl" class="btn btn-sm btn-outline-primary py-0 ms-auto" @click="playing === t.key ? stopPlaying() : play(t)">{{ playing === t.key ? "توقف صدا" : "پخش صدا" }}</button>
          </div>
          <div v-if="t.degraded" class="alert alert-warning py-1 px-2 small mb-1">{{ DEGRADED_TEXT }}</div>
          <div v-else-if="t.verified === false && t.via !== 'history'" class="ai-unverified small">{{ NOT_VERIFIED_TEXT }}</div>
          <div class="ai-text">{{ t.answer }}</div>

          <div v-if="t.citations.length" class="ai-chips">
            <button
              v-for="(c, i) in t.citations"
              :key="i"
              :disabled="!linkOf(c)"
              :title="linkOf(c) ? 'باز کردن رکورد' : 'صفحه این رکورد در دسترس نیست'"
              class="ai-chip"
              @click="openCitation(c)">
              {{ c.title || c.type + " " + c.id }}
            </button>
          </div>

          <details v-if="t.toolCalls.length" class="ai-tools">
            <summary>ابزارهای استفاده‌شده ({{ t.toolCalls.length }})</summary>
            <ul class="mb-0">
              <li v-for="(c, i) in t.toolCalls" :key="i">
                <span class="inv-ltr">{{ c.name }}</span>
                <span v-if="!c.allowed" class="inv-badge bad ms-1">بدون دسترسی</span>
              </li>
            </ul>
          </details>
        </div>
      </div>

      <div v-if="busy" class="ai-busy"><span class="spinner-border spinner-border-sm"></span> در حال پاسخ‌دادن...</div>
    </div>

    <div class="ai-input">
      <textarea
        v-model="text"
        :disabled="busy || recording"
        class="form-control form-control-sm"
        maxlength="2000"
        placeholder="پرسش خود را بنویسید (Enter برای ارسال، Shift+Enter برای خط تازه)"
        rows="2"
        @keydown.enter="onEnter"></textarea>
      <div class="ai-actions">
        <button :disabled="busy || recording || !text.trim()" class="btn btn-sm btn-primary" @click="send">
          <span v-if="busy" class="spinner-border spinner-border-sm me-1"></span>ارسال
        </button>
        <button
          v-if="micSupported"
          :class="recording ? 'btn-danger' : 'btn-outline-primary'"
          :disabled="busy"
          :title="micLabel"
          class="btn btn-sm ai-mic"
          type="button"
          @contextmenu.prevent
          @keydown.enter.prevent="onKeyToggle"
          @keydown.space.prevent="onKeyToggle"
          @pointercancel="onRelease"
          @pointerdown.prevent="onPress"
          @pointerup="onRelease">
          <span v-if="recording" class="ai-rec"></span>
          {{ recording ? "ضبط " + clock : starting ? "..." : "میکروفون" }}
        </button>
      </div>
      <div v-if="recording" class="inv-muted small">در حال ضبط... رها کنید تا ارسال شود (یا اگر کوتاه زدید، دوباره بزنید).</div>
    </div>

    <audio ref="player" class="d-none" @ended="playing = null"></audio>

    <div v-if="drawer" class="ai-drawer-backdrop" @click.self="drawer = false">
      <aside class="ai-drawer">
        <div class="d-flex justify-content-between align-items-center mb-2">
          <h3 class="inv-title">تاریخچه گفت‌وگوها</h3>
          <button class="btn btn-sm btn-outline-secondary" @click="drawer = false">بستن</button>
        </div>
        <p v-if="history.loading.value" class="inv-muted"><span class="spinner-border spinner-border-sm"></span> در حال بارگذاری...</p>
        <p v-else-if="!history.rows.value.length" class="inv-muted">گفت‌وگویی ثبت نشده است.</p>
        <ul class="list-unstyled mb-2">
          <li v-for="c in history.rows.value" :key="c.id">
            <button :class="{ active: c.id === conversationId }" class="ai-conv" @click="openConversation(c.id)">
              <span class="fw-bold">{{ c.title || "گفت‌وگو " + c.id }}</span>
              <span class="inv-muted">{{ jalali(c.lastAt, true) }} — {{ c.messages }} پیام</span>
            </button>
          </li>
        </ul>
        <QPager :page="history.page.value" :page-size="history.pageSize" :total="history.total.value" @go="history.go" />
      </aside>
    </div>
  </div>
</template>

<style scoped>
.ai-panel {
  direction: rtl;
  text-align: right;
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}
.ai-head {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: center;
  justify-content: space-between;
}
.ai-list {
  display: flex;
  flex-direction: column;
  gap: 0.7rem;
  min-height: 160px;
  max-height: 60vh;
  overflow-y: auto;
  padding: 0.2rem;
}
.compact .ai-list {
  max-height: 340px;
}
.ai-turn {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}
.ai-q,
.ai-a {
  border-radius: 8px;
  padding: 0.5rem 0.7rem;
}
.ai-q {
  background: #eef3fb;
}
.ai-a {
  background: #fff;
  border: 1px solid #dfe3e8;
}
.ai-who {
  font-size: 0.72rem;
  font-weight: 700;
  color: #555;
  margin-left: 0.4rem;
}
.ai-text {
  white-space: pre-wrap;
  line-height: 1.8;
  overflow-wrap: anywhere;
}
.ai-meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.4rem;
  margin-bottom: 0.25rem;
}
.ai-model {
  font-size: 0.68rem;
  color: #888;
}
.ai-unverified {
  border-right: 3px solid #dc3545;
  background: #fdf0f1;
  color: #842029;
  padding: 0.25rem 0.5rem;
  margin-bottom: 0.3rem;
}
.ai-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
  margin-top: 0.4rem;
}
.ai-chip {
  border: 1px solid #0d6efd;
  background: #f1f6ff;
  color: #084298;
  border-radius: 1rem;
  padding: 0.1rem 0.7rem;
  font-size: 0.78rem;
}
.ai-chip:disabled {
  border-color: #ced4da;
  background: #f4f6f8;
  color: #6c757d;
}
.ai-tools {
  margin-top: 0.4rem;
  font-size: 0.78rem;
  color: #555;
}
.ai-busy {
  color: #555;
  font-size: 0.85rem;
}
.ai-input {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}
.ai-actions {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}
.ai-mic {
  touch-action: none;
  user-select: none;
  -webkit-user-select: none;
}
.ai-rec {
  display: inline-block;
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: #fff;
  margin-left: 0.3rem;
  animation: ai-blink 1s infinite;
}
@keyframes ai-blink {
  50% {
    opacity: 0.25;
  }
}
.ai-drawer-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.35);
  z-index: 1050;
  display: flex;
  justify-content: flex-start;
}
.ai-drawer {
  background: #fff;
  width: min(420px, 100%);
  height: 100%;
  overflow-y: auto;
  padding: 1rem;
  direction: rtl;
}
.ai-conv {
  display: flex;
  flex-direction: column;
  width: 100%;
  text-align: right;
  gap: 0.1rem;
  border: 1px solid #dfe3e8;
  background: #fff;
  border-radius: 8px;
  padding: 0.45rem 0.7rem;
  margin-bottom: 0.4rem;
}
.ai-conv.active {
  border-color: #0d6efd;
  background: #f1f6ff;
}
</style>
