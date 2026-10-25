<script setup lang="ts">
// The assistant (Local AI, release A1): a text box and a push-to-talk button. The answer, the verified badge, the citations that open the cited page, the tools that were called
// (collapsed) and the spoken answer. The assistant acts as the signed-in user and never approves anything; this panel has no approve action.
// The microphone is asked for only when the person presses the button and is released as soon as the recording stops (no always-on microphone).
// prefill(question) (exposed) writes a question into the box without sending it; the optional slot «suggestions» is shown just above the box. The colours are CSS variables (--ai-*) with the old values as defaults, so a page with
// its own theme (the CEO dashboard, light and dark) can set them; every other page looks as before.
//
// Voice mode (prop voiceMode, release D1 §7.3; set by the CEO page, every other page keeps push-to-talk): one tap starts listening with a waveform, a short silence ends the
// question (voice session, composables/voice.ts), the answer is spoken (the server's audio, else the browser's Persian voice, else the caption), the UI actions of the answer are
// carried out by the page (prop actionRunner) with a caption line each, and in continuous conversation the microphone reopens for a few seconds. uiContext is sent with every question.
// The session can come from the page (prop voiceSession: the page also listens with Space and reads sections aloud) or be created here (Esc then stops it on this page).
// Exposed: prefill, command, reveal, startVoice, stopVoice.
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import VoiceIndicator from "@/components/ai/VoiceIndicator.vue";
import VoiceWave from "@/components/ai/VoiceWave.vue";
import { aiChat, aiVoice, getAiConversation, getAiConversations, type AiUiAction, type AiUiContext } from "@/api/AiAPIs";
import { DEGRADED_TEXT, frontLink, NOT_VERIFIED_TEXT } from "@/composables/ai";
import { usePagedList } from "@/composables/fn";
import { fail, jalali } from "@/composables/inventory";
import { useVoiceSession, VOICE_TIMING, type ActionOutcome, type ActionResult, type SpeechItem, type VoiceSession } from "@/composables/voice";

const props = defineProps({
  compact: { type: Boolean, default: false },
  /** the voice session of §7.3 instead of push-to-talk */
  voiceMode: { type: Boolean, default: false },
  /** a session owned by the page (optional; without it voice mode makes its own) */
  voiceSession: { type: Object as () => VoiceSession | null, default: null },
  /** sent with every question (§7.1) */
  uiContext: { type: Object as () => AiUiContext | null, default: null },
  /** carries out the UI actions of an answer; without it the actions are only listed in the answer */
  actionRunner: { type: Function as unknown as () => ((actions: AiUiAction[]) => Promise<ActionOutcome>) | null, default: null },
});

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
  /** UI actions of the answer (§7.1) and, when the page carried them out, one result line each */
  actions?: AiUiAction[];
  results?: ActionResult[] | null;
};

const router = useRouter();
const turns = ref<Turn[]>([]);
const conversationId = ref<number | null>(null);
const text = ref("");
const busy = ref(false);
const listEl = ref<HTMLElement | null>(null);
const inputEl = ref<HTMLTextAreaElement | null>(null);
let seq = 0;

// ---------------------------------------------------------------- history drawer
const drawer = ref(false);
const history = usePagedList(getAiConversations, ref({}), 20);
function openHistory() {
  drawer.value = true;
  history.load();
}
async function openConversation(id: number) {
  if (busy.value || recording.value) return;
  const [data, error] = await getAiConversation(id);
  if (error) return fail(error);
  if (busy.value || recording.value || unmounted) return; // a question was sent meanwhile: the old conversation must not replace it
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
    actions: Array.isArray(r.actions) ? r.actions : [],
    results: null,
  });
  scrollDown();
}

async function send() {
  const question = text.value.trim();
  if (!question || busy.value || recording.value || voiceListening.value) return;
  if (question.length > 2000) return fail("متن پرسش نباید از ۲۰۰۰ نویسه بیشتر باشد.");
  await ask(question, false);
}

/** Sends a written question; speak=true answers aloud (the voice-command chips; the microphone does not reopen after a click). */
async function ask(question: string, speak: boolean) {
  busy.value = true;
  voice?.setThinking(true);
  const mark = voice?.mark();
  const [r, error] = await aiChat({ conversationId: conversationId.value, text: question, uiContext: props.uiContext });
  if (unmounted) return;
  busy.value = false;
  voice?.setThinking(false);
  if (error) {
    voice?.caption("system", error.message || "پاسخ دریافت نشد.");
    return fail(error);
  }
  if (!speak) text.value = "";
  if (speak) voice?.caption("user", question);
  addTurn("text", question, r, null);
  await respond(r, speak, false, mark);
}

/** A voice command written or tapped (chips of the CEO page): sent at once and answered aloud. */
function command(question: string) {
  if (busy.value || voiceListening.value) return false;
  ask(String(question || "").slice(0, 2000), !!voice);
  return true;
}

/**
 * After an answer: the page carries out its UI actions in order (a caption line each), then the answer is spoken (speak=true), then the refusals (the answer cannot know that the
 * page refused an action), then the read-outs the actions asked for; leaving the page waits until all of it was said (interrupting it — a tap or Esc — keeps the CEO on the page).
 * In continuous conversation (followup=true: a spoken question) the follow-up window opens when the speech ends. If Esc was pressed while the answer was on its way (mark),
 * the answer is only listed: no action runs and nothing is said. Returns false when nothing more should happen on this page (it is being left, or Esc).
 */
async function respond(r: any, speak: boolean, followup: boolean, mark?: number): Promise<boolean> {
  const actions: AiUiAction[] = Array.isArray(r.actions) ? r.actions : [];
  const turn = turns.value[turns.value.length - 1];
  const stale = !!voice && mark !== undefined && voice.mark() !== mark;
  if (stale) {
    if (turn && actions.length) turn.results = actions.map((a) => ({ ok: false, text: (a.title || a.type) + ": لغو شد" }));
    return false;
  }
  let outcome: ActionOutcome | null = null;
  if (actions.length && props.actionRunner) {
    try {
      outcome = await props.actionRunner(actions);
    } catch {
      outcome = { results: actions.map((a) => ({ ok: false, text: (a.title || "کار دستیار") + ": انجام نشد" })), speech: [], deferred: [] };
    }
    if (unmounted) return false;
    if (turn) turn.results = outcome.results;
    outcome.results.forEach((x) => voice?.caption("action", x.text, x.ok));
  }
  const after = () => outcome?.deferred.forEach((fn) => fn());
  const leaves = !!outcome?.deferred.length;
  if (!voice) {
    after();
    return !leaves;
  }
  const items: SpeechItem[] = [];
  if (speak) {
    items.push({ text: r.answer || "", audioBase64: r.audioBase64 || null });
    items.push(...(outcome?.results || []).filter((x) => !x.ok).map((x) => ({ text: x.text, caption: false })));
  }
  items.push(...(outcome?.speech || []));
  if (items.length) voice.say(items, { after, followup: followup && !leaves, epoch: mark });
  else after();
  return !leaves;
}

/**
 * Writes a question into the box without sending it (the suggested questions of the CEO dashboard, «پرسش از دستیار درباره این جدول»): the person reads it, edits it and sends it.
 * Refused (false) while an answer or a recording is running.
 */
function prefill(question: string): boolean {
  if (busy.value || recording.value) return false;
  text.value = String(question || "").slice(0, 2000);
  nextTick(() => {
    const el = inputEl.value;
    if (!el) return;
    el.focus();
    el.setSelectionRange(el.value.length, el.value.length);
  });
  return true;
}
/** The drawer was opened: the newest turn in view (answers that came while it was closed were added out of sight). */
function reveal() {
  scrollDown();
}
defineExpose({ prefill, command, reveal, startVoice: () => voice?.start(), stopVoice: () => voice?.cancel() });

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
/** the recording formats the API accepts (webm, ogg, wav): the first one this browser can record */
const MIMES = ["audio/webm;codecs=opus", "audio/webm", "audio/ogg;codecs=opus"];
function pickMime(): string | null {
  if (typeof MediaRecorder === "undefined" || typeof MediaRecorder.isTypeSupported !== "function") return null;
  return MIMES.find((m) => MediaRecorder.isTypeSupported(m)) || null;
}
const recordMime = pickMime();
const secure = typeof window === "undefined" || window.isSecureContext !== false;
const micSupported = secure && typeof navigator !== "undefined" && !!navigator.mediaDevices?.getUserMedia && !!recordMime;
const micHint = !secure
  ? "گفت‌وگوی صوتی نیاز به اتصال امن (HTTPS) دارد؛ گفت‌وگوی متنی در دسترس است"
  : "این مرورگر ضبط صدا با قالب پشتیبانی‌شده (webm یا ogg) را ندارد؛ گفت‌وگوی متنی در دسترس است";
let unmounted = false;
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
    if (unmounted) return;
    return fail("اجازه استفاده از میکروفون داده نشد.");
  }
  starting.value = false;
  if (unmounted) {
    // the panel was left while the permission question was open: nothing may record
    releaseMic();
    return;
  }
  try {
    recorder = new MediaRecorder(stream, { mimeType: recordMime! });
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
  if (unmounted) return;
  const type = recorder?.mimeType || recordMime || "audio/webm";
  const blob = new Blob(chunks, { type });
  chunks = [];
  recorder = null;
  if (blob.size < MIN_BYTES) return fail("صدایی ضبط نشد؛ دکمه را بیشتر نگه دارید یا دوباره بزنید.");
  busy.value = true;
  const [r, error] = await aiVoice(blob, type.includes("ogg") ? "question.ogg" : "question.webm", conversationId.value);
  if (unmounted) return;
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

// ---------------------------------------------------------------- voice mode (§7.3)
const voice: VoiceSession | null = props.voiceSession || (props.voiceMode ? useVoiceSession() : null);
const voiceListening = computed(() => !!voice && voice.state === "listening");
/** a spoken question: speech to text and chat in one call, then the answer (actions, speech, follow-up); true = answered and the conversation goes on here */
async function handleUtterance(blob: Blob, mime: string): Promise<boolean> {
  busy.value = true;
  const mark = voice?.mark();
  const [r, error] = await aiVoice(blob, mime.includes("ogg") ? "question.ogg" : "question.webm", conversationId.value, props.uiContext);
  if (unmounted) return false;
  busy.value = false;
  if (error) {
    voice?.caption("system", error.message || "پاسخ دریافت نشد.");
    fail(error);
    return false;
  }
  if (!r.transcript && !r.answer) {
    voice?.caption("system", "سخن شما فهمیده نشد؛ دوباره بگویید.");
    return false;
  }
  if (r.transcript) voice?.caption("user", r.transcript);
  addTurn("voice", r.transcript || "", r, audioUrlOf(r.audioBase64));
  return respond(r, true, true, mark);
}
/** a session of this panel's own (not the page's): Esc stops it anywhere on the page */
function onWindowKey(e: KeyboardEvent) {
  if (e.key === "Escape" && voice && (voice.state !== "idle" || voice.countdown)) voice.cancel();
}
const ownSession = !props.voiceSession && !!voice;
onMounted(() => {
  voice?.onUtterance(handleUtterance);
  if (ownSession) window.addEventListener("keydown", onWindowKey);
});

let voicePressedAt = 0;
let voiceHeld = false;
let voiceIgnoreUp = false;
/** tap = start (the silence ends it) or, while listening, send now; while the assistant speaks = barge-in; a held press is push-to-talk */
function onVoicePress(e: PointerEvent) {
  if (!voice || (e.button !== undefined && e.button !== 0)) return;
  try {
    (e.currentTarget as HTMLElement).setPointerCapture?.(e.pointerId);
  } catch {
    /* not supported */
  }
  if (voice.state === "listening") {
    voiceIgnoreUp = true;
    voice.stop();
    return;
  }
  if (voice.state === "thinking") return;
  voiceIgnoreUp = false;
  voiceHeld = true;
  voicePressedAt = Date.now();
  voice.start();
}
function onVoiceRelease() {
  if (voiceIgnoreUp) {
    voiceIgnoreUp = false;
    return;
  }
  if (!voiceHeld || !voice) return;
  voiceHeld = false;
  if (voice.state === "listening" && Date.now() - voicePressedAt >= VOICE_TIMING.hold) voice.stop();
}
function onVoiceKey(e: KeyboardEvent) {
  if (e.repeat || !voice) return;
  if (voice.state === "listening") voice.stop();
  else if (voice.state !== "thinking") voice.start();
}
function onVoiceEsc() {
  voice?.cancel();
}
const voiceStateText = computed(() => {
  if (!voice) return "";
  switch (voice.state) {
    case "listening":
      return voice.followup && !voice.heard ? "اگر پرسش دیگری دارید، بگویید" : voice.heard ? "می‌شنوم… پس از مکثی کوتاه ارسال می‌شود" : "در حال شنیدن…";
    case "thinking":
      return "در حال فکر کردن…";
    case "speaking":
      return voice.muted ? "پاسخ روی صفحه (بدون صدا)" : "در حال گفتن… برای صحبت، میکروفون را بزنید";
    default:
      return voice.supported ? "برای گفتن، میکروفون را بزنید" : voice.hint;
  }
});
const voiceButtonText = computed(() => {
  if (!voice) return "";
  return voice.state === "listening" ? "ارسال" : voice.state === "speaking" ? "صحبت کنید" : voice.state === "thinking" ? "…" : "میکروفون";
});
/**
 * The caption under the conversation: the turns already show the question, the answer and the action lines, so here only what is being said right now (an answer or
 * a read-out) or, when nothing is said, a message of the session itself (nothing heard, no microphone).
 */
const liveCaption = computed(() => {
  if (!voice) return null;
  if (voice.state === "speaking" && voice.speaking?.text) return { who: "assistant", text: voice.speaking.text };
  const last = voice.captions[voice.captions.length - 1];
  return last && last.who === "system" && voice.state === "idle" ? { who: "system", text: last.text } : null;
});

const micLabel = computed(() => (recording.value ? "در حال ضبط... برای پایان رها کنید یا دوباره بزنید" : starting.value ? "منتظر اجازه میکروفون..." : "نگه دارید و صحبت کنید"));
const clock = computed(() => String(Math.floor(seconds.value / 60)).padStart(2, "0") + ":" + String(seconds.value % 60).padStart(2, "0"));

onBeforeUnmount(() => {
  unmounted = true;
  voice?.onUtterance(null);
  if (ownSession) window.removeEventListener("keydown", onWindowKey);
  if (recorder && recorder.state !== "inactive") {
    recorder.onstop = null;
    recorder.stop();
  }
  releaseMic();
  dropAudio();
});

// ---------------------------------------------------------------- what the answer says about itself
/** the model did not answer at all (no model, no tool): the dashboard is the only thing left. With a model or tools, degraded only means the speech service failed. */
const trueDegraded = (t: Turn) => t.degraded && !t.toolCalls.length && !t.model;
/** the server already wrote the same sentence at the start of the answer: the box keeps its colour but the sentence is not repeated */
const startsUnverified = (t: Turn) => t.answer.trim().startsWith("پاسخ مدل قابل راستی‌آزمایی نبود");
const isDegradedAnswer = (t: Turn) => t.answer.trim() === DEGRADED_TEXT;

// ---------------------------------------------------------------- UI actions listed in an answer (pages without an action runner: a navigation is one click away)
const actionLink = (a: AiUiAction) => (a.type === "navigate" ? frontLink(router, a.link || "") : null);
function openAction(a: AiUiAction) {
  const to = actionLink(a);
  if (to) router.push(to);
}

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
        <button :disabled="busy || recording" class="btn btn-sm btn-outline-secondary" @click="openHistory">تاریخچه</button>
      </div>
    </div>

    <div ref="listEl" class="ai-list">
      <p v-if="!turns.length && !busy && !voice" class="inv-muted mb-0">{{ micSupported ? "پرسش خود را بنویسید یا دکمه میکروفون را نگه دارید." : "پرسش خود را بنویسید." }} دستیار با دسترسی‌های خود شما پاسخ می‌دهد و هیچ چیزی را تایید نمی‌کند.</p>
      <p v-else-if="!turns.length && !busy" class="inv-muted mb-0">بگویید یا بنویسید؛ دستیار پاسخ را می‌گوید و کارهایی مثل باز کردن جدول، تحلیل یک بخش یا خواندن هشدارها را انجام می‌دهد. با دسترسی‌های خود شما کار می‌کند و هیچ چیزی را تایید نمی‌کند.</p>

      <div v-for="t in turns" :key="t.key" class="ai-turn">
        <div v-if="t.question" class="ai-q">
          <span class="ai-who">{{ t.via === "voice" ? "شما گفتید" : "شما" }}</span>
          <span class="ai-text">{{ t.question }}</span>
        </div>
        <div class="ai-a">
          <div class="ai-meta">
            <span class="ai-who">دستیار</span>
            <span v-if="trueDegraded(t)" class="inv-badge warn">حالت کاهش‌یافته</span>
            <span v-if="t.verified === true" class="inv-badge ok" title="اعداد پاسخ با داده‌های سامانه سنجیده شد">راستی‌آزمایی‌شده</span>
            <span v-else-if="t.verified === false" class="inv-badge bad">راستی‌آزمایی‌نشده</span>
            <span v-if="t.model" class="ai-model inv-ltr">{{ t.model }}</span>
            <button v-if="t.audioUrl" class="btn btn-sm btn-outline-primary py-0 ms-auto" @click="playing === t.key ? stopPlaying() : play(t)">{{ playing === t.key ? "توقف صدا" : "پخش صدا" }}</button>
          </div>
          <div v-if="trueDegraded(t) && !isDegradedAnswer(t)" class="alert alert-warning py-1 px-2 small mb-1">{{ DEGRADED_TEXT }}</div>
          <div v-else-if="t.degraded && !trueDegraded(t)" class="inv-muted small mb-1">پاسخ صوتی در دسترس نبود</div>
          <div v-if="t.verified === false && t.via !== 'history' && !startsUnverified(t)" class="ai-unverified small">{{ NOT_VERIFIED_TEXT }}</div>
          <div :class="{ 'ai-unverified': t.verified === false && startsUnverified(t), 'alert alert-warning py-1 px-2': trueDegraded(t) && isDegradedAnswer(t) }" class="ai-text">{{ t.answer }}</div>

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

          <ul v-if="t.results && t.results.length" class="ai-acts">
            <li v-for="(x, i) in t.results" :key="i" :class="x.ok ? 'ok' : 'bad'">{{ x.ok ? "✓" : "✗" }} {{ x.text }}</li>
          </ul>
          <div v-else-if="t.actions && t.actions.length" class="ai-chips">
            <button v-for="(a, i) in t.actions" :key="i" :disabled="!actionLink(a)" :title="actionLink(a) ? 'باز کردن' : 'این کار در داشبورد مدیرعامل انجام می‌شود'" class="ai-chip" @click="openAction(a)">
              {{ a.title || a.type }}
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

    <div v-if="voice" :class="'vs-' + voice.state" aria-live="polite" class="ai-voice">
      <div class="ai-voice-row">
        <VoiceIndicator :followup="voice.followup" :muted="voice.muted" :state="voice.state" />
        <span class="ai-voice-state">{{ voiceStateText }}</span>
        <span v-if="voice.countdown" class="ai-voice-count" title="پنجره‌ی پرسش بعدی">{{ voice.countdown.toLocaleString("fa-IR") }}</span>
        <label class="ai-voice-switch" title="پس از پاسخ، میکروفون چند ثانیه برای پرسش بعدی باز می‌ماند">
          <input v-model="voice.continuous" type="checkbox" />
          <span>گفت‌وگوی پیوسته</span>
        </label>
      </div>
      <VoiceWave v-if="voice.state === 'listening'" :voice="voice" />
      <p v-if="liveCaption" :class="'cap-' + liveCaption.who" class="ai-caption">{{ liveCaption.text }}</p>
    </div>

    <slot name="suggestions" />

    <div class="ai-input">
      <textarea
        ref="inputEl"
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
        <template v-if="voice">
          <button v-if="!voice.supported" :title="voice.hint" class="btn btn-sm btn-outline-secondary" disabled type="button">میکروفون</button>
          <button
            v-else
            :aria-pressed="voice.state === 'listening'"
            :class="voice.state === 'listening' ? 'btn-danger' : 'btn-outline-primary'"
            :disabled="voice.state === 'thinking'"
            :title="voice.state === 'listening' ? 'ارسال حالا (یا مکث کنید)' : voice.state === 'speaking' ? 'قطع پاسخ و صحبت' : 'بزنید و بگویید (Esc برای توقف)'"
            class="btn btn-sm ai-mic"
            type="button"
            @contextmenu.prevent
            @keydown.enter.prevent="onVoiceKey"
            @keydown.esc.prevent="onVoiceEsc"
            @keydown.space.prevent="onVoiceKey"
            @pointercancel="onVoiceRelease"
            @pointerdown.prevent="onVoicePress"
            @pointerup="onVoiceRelease">
            <span v-if="voice.state === 'listening'" class="ai-rec"></span>
            {{ voiceButtonText }}
          </button>
        </template>
        <button v-else-if="!micSupported" :title="micHint" class="btn btn-sm btn-outline-secondary" disabled type="button">میکروفون</button>
        <button
          v-else
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
      <div v-if="voice ? !voice.supported : !micSupported" class="inv-muted small">{{ voice ? voice.hint : micHint }}</div>
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
  background: var(--ai-q-bg, #eef3fb);
}
.ai-a {
  background: var(--ai-a-bg, #fff);
  border: 1px solid var(--ai-line, #dfe3e8);
}
.ai-who {
  font-size: 0.72rem;
  font-weight: 700;
  color: var(--ai-muted, #555);
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
  color: var(--ai-faint, #888);
}
.ai-unverified {
  border-right: 3px solid var(--ai-bad-edge, #dc3545);
  background: var(--ai-bad-bg, #fdf0f1);
  color: var(--ai-bad-ink, #842029);
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
  border: 1px solid var(--ai-chip-edge, #0d6efd);
  background: var(--ai-chip-bg, #f1f6ff);
  color: var(--ai-chip-ink, #084298);
  border-radius: 1rem;
  padding: 0.1rem 0.7rem;
  font-size: 0.78rem;
}
.ai-chip:disabled {
  border-color: var(--ai-line, #ced4da);
  background: var(--ai-off-bg, #f4f6f8);
  color: var(--ai-faint, #6c757d);
}
.ai-tools {
  margin-top: 0.4rem;
  font-size: 0.78rem;
  color: var(--ai-muted, #555);
}
.ai-busy {
  color: var(--ai-muted, #555);
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
.ai-acts {
  list-style: none;
  margin: 0.4rem 0 0;
  padding: 0;
  font-size: 0.8rem;
  color: var(--ai-muted, #555);
}
.ai-acts li.ok {
  color: var(--ai-ok, #1f7a5a);
}
.ai-acts li.bad {
  color: var(--ai-bad-ink, #842029);
}
.ai-voice {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  padding: 0.5rem 0.6rem;
  border: 1px solid var(--ai-line, #dfe3e8);
  border-radius: 8px;
  background: var(--ai-voice-bg, #f7f9fb);
}
.ai-voice.vs-listening {
  border-color: var(--ai-wave, #245c8c);
}
.ai-voice-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  font-size: 0.8rem;
  color: var(--ai-muted, #555);
}
.ai-voice-state {
  flex: 1;
  min-width: 0;
}
.ai-voice-count {
  min-width: 1.6rem;
  height: 1.6rem;
  border-radius: 50%;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--ai-wave, #245c8c);
  color: var(--ai-wave, #245c8c);
  font-weight: 700;
}
.ai-voice-switch {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  cursor: pointer;
  user-select: none;
}
.ai-caption {
  margin: 0;
  max-height: 7.2em;
  overflow-y: auto;
  font-size: 0.85rem;
  line-height: 1.8;
}
.ai-caption.cap-assistant {
  font-weight: 500;
}
.ai-caption.cap-system {
  color: var(--ai-bad-ink, #842029);
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
  background: var(--ai-a-bg, #fff);
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
  border: 1px solid var(--ai-line, #dfe3e8);
  background: var(--ai-a-bg, #fff);
  border-radius: 8px;
  padding: 0.45rem 0.7rem;
  margin-bottom: 0.4rem;
}
.ai-conv.active {
  border-color: var(--ai-chip-edge, #0d6efd);
  background: var(--ai-chip-bg, #f1f6ff);
}
</style>
