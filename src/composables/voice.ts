import { onBeforeUnmount, reactive, ref } from "vue";

// The voice session of the assistant (release D1 §7.3, "like Siri"): one tap starts listening, the recording stops by itself after a short silence, the answer is spoken,
// and in continuous conversation the microphone reopens for a few seconds for a follow-up. This file is only the mechanics (microphone, Web Audio analyser, silence detection,
// MediaRecorder, playback, the browser's own Persian voice as a fallback, the follow-up window); what is said and done is decided by the assistant panel and the page.
// The microphone is opened only when listening starts and its tracks are closed whenever listening stops (no always-on microphone, no wake word).

export type VoiceState = "idle" | "listening" | "thinking" | "speaking";
export interface VoiceCaption {
  id: number;
  who: "user" | "assistant" | "action" | "system";
  text: string;
  /** an action line: done (true) or refused (false) */
  ok?: boolean;
}
/** One thing to say: the server's audio (WAV base64 or a blob) when there is one, otherwise the text (the browser's Persian voice, or only the caption). */
export interface SpeechItem {
  text: string;
  audioBase64?: string | null;
  audioBlob?: Blob | null;
  /** false: speak without adding a caption line */
  caption?: boolean;
}
export interface SayOptions {
  /** reopen the microphone for a follow-up when the speech ends (continuous conversation); false for read-outs */
  followup?: boolean;
  /** called when everything was said (not when it was interrupted): deferred actions such as leaving the page */
  after?: () => void;
  /** mark() taken when the question was asked: if Esc was pressed (or listening started) since, nothing is said and nothing more happens */
  epoch?: number;
}

/** Timings of the session (ms). */
export const VOICE_TIMING = {
  /** the first part of every listening measures the room noise */
  calibrate: 300,
  /** silence after speech that ends the question */
  silence: 1200,
  /** the longest question */
  max: 15000,
  /** the follow-up window of continuous conversation */
  followup: 6000,
  /** nothing said at all: stop listening */
  noSpeech: 8000,
  /** speech must last this long before it counts (a click or a cough does not) */
  minSpeech: 150,
  /** a press held this long is push-to-talk: releasing it sends */
  hold: 450,
  /** an answer that can only be shown (no audio, no Persian voice) stays "spoken" for its reading time: per character, at least, at most */
  readPerChar: 55,
  readMin: 1500,
  readMax: 6000,
};

const MIMES = ["audio/webm;codecs=opus", "audio/webm", "audio/ogg;codecs=opus"];
function pickMime(): string | null {
  if (typeof MediaRecorder === "undefined" || typeof MediaRecorder.isTypeSupported !== "function") return null;
  return MIMES.find((m) => MediaRecorder.isTypeSupported(m)) || null;
}

/** The threshold between room noise and speech from the calibration samples (RMS of the first 300 ms): three times the noise floor, kept within sane bounds. */
export function thresholdOf(samples: number[]): number {
  if (!samples.length) return 0.02;
  const sorted = [...samples].sort((a, b) => a - b);
  const floor = sorted[Math.floor(sorted.length * 0.6)] || 0;
  return Math.min(0.09, Math.max(0.012, floor * 3 + 0.004));
}

/** The browser's own Persian voice, when it has one (used only when the server sent no audio). */
function persianVoice(): SpeechSynthesisVoice | null {
  try {
    const voices = window.speechSynthesis?.getVoices?.() || [];
    return voices.find((v) => /^fa([-_]|$)/i.test(v.lang)) || voices.find((v) => /persian|farsi/i.test(v.name)) || null;
  } catch {
    return null;
  }
}

function blobOfBase64(base64: string): Blob | null {
  try {
    const bin = atob(base64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return new Blob([bytes], { type: "audio/wav" });
  } catch {
    return null;
  }
}

export function useVoiceSession() {
  const state = ref<VoiceState>("idle");
  const continuous = ref(true);
  /** seconds left in the follow-up window (0 when it is not open) */
  const countdown = ref(0);
  /** listening opened by continuous conversation (a follow-up) */
  const followup = ref(false);
  const captions = ref<VoiceCaption[]>([]);
  /** what is being said right now */
  const speaking = ref<SpeechItem | null>(null);
  /** the current item is only shown (no audio and no Persian voice in this browser) */
  const muted = ref(false);
  /** input level 0..1 (meters) */
  const level = ref(0);
  /** speech was heard in this listening */
  const heard = ref(false);
  const seconds = ref(0);

  const recordMime = pickMime();
  const secure = typeof window === "undefined" || window.isSecureContext !== false;
  const hasAudioContext = typeof window !== "undefined" && !!(window.AudioContext || (window as any).webkitAudioContext);
  const supported = secure && typeof navigator !== "undefined" && !!navigator.mediaDevices?.getUserMedia && !!recordMime && hasAudioContext;
  const hint = !secure
    ? "گفت‌وگوی صوتی نیاز به اتصال امن (HTTPS یا localhost) دارد؛ گفت‌وگوی متنی در دسترس است"
    : "این مرورگر ضبط صدا با قالب پشتیبانی‌شده (webm یا ogg) را ندارد؛ گفت‌وگوی متنی در دسترس است";

  let seq = 0;
  /** changes with every start and every Esc: an answer asked for before it is not spoken (SayOptions.epoch) */
  let epoch = 0;
  let unmounted = false;
  let stream: MediaStream | null = null;
  let ctx: AudioContext | null = null;
  let analyser: AnalyserNode | null = null;
  let recorder: MediaRecorder | null = null;
  let chunks: BlobPart[] = [];
  let monitor: ReturnType<typeof setInterval> | null = null;
  let startedAt = 0;
  let calib: number[] = [];
  let threshold: number | null = null;
  let speechMs = 0;
  let silentMs = 0;
  let discard = false;
  let manual = false;
  let handler: ((blob: Blob, mime: string) => Promise<boolean> | boolean) | null = null;
  let speakToken = 0;
  let resolvePlay: (() => void) | null = null;
  const audio: HTMLAudioElement | null = typeof Audio !== "undefined" ? new Audio() : null;
  let audioUrl: string | null = null;
  const floatBuf = new Float32Array(1024);
  try {
    window.speechSynthesis?.getVoices?.(); // starts loading the voice list
  } catch {
    /* no speech synthesis */
  }

  function caption(who: VoiceCaption["who"], text: string, ok?: boolean) {
    if (!text) return;
    captions.value = [...captions.value.slice(-11), { id: ++seq, who, text, ok }];
  }

  // ---------------------------------------------------------------- listening
  function release() {
    if (monitor) clearInterval(monitor);
    monitor = null;
    stream?.getTracks().forEach((t) => t.stop());
    stream = null;
    analyser = null;
    const c = ctx;
    ctx = null;
    c?.close?.().catch(() => undefined);
    level.value = 0;
    countdown.value = 0;
  }

  /**
   * Phones play audio started later (the answer, after the server answered) only from an element that was played once inside a tap: the first start() — always called
   * from a tap, Space or a click — plays a twentieth of a second of silence on the shared audio element.
   */
  let unlocked = false;
  function unlockAudio() {
    if (unlocked || !audio) return;
    unlocked = true;
    try {
      const n = 400; // 0.05 s at 8 kHz, 16-bit mono
      const buf = new ArrayBuffer(44 + n * 2);
      const v = new DataView(buf);
      const w = (o: number, t: string) => {
        for (let i = 0; i < t.length; i++) v.setUint8(o + i, t.charCodeAt(i));
      };
      w(0, "RIFF");
      v.setUint32(4, 36 + n * 2, true);
      w(8, "WAVEfmt ");
      v.setUint32(16, 16, true);
      v.setUint16(20, 1, true);
      v.setUint16(22, 1, true);
      v.setUint32(24, 8000, true);
      v.setUint32(28, 16000, true);
      v.setUint16(32, 2, true);
      v.setUint16(34, 16, true);
      w(36, "data");
      v.setUint32(40, n * 2, true);
      audioUrl = URL.createObjectURL(new Blob([buf], { type: "audio/wav" }));
      audio.src = audioUrl;
      audio.play().catch(() => undefined);
    } catch {
      /* nothing to unlock */
    }
  }

  /** Starts listening. While the assistant speaks this is the barge-in: the speech stops and listening starts at once. */
  async function start(opts: { followup?: boolean } = {}) {
    if (unmounted || state.value === "listening" || state.value === "thinking") return;
    if (!supported) {
      caption("system", hint);
      return;
    }
    stopSpeaking();
    if (!opts.followup) unlockAudio();
    epoch++;
    followup.value = !!opts.followup;
    state.value = "listening";
    heard.value = false;
    seconds.value = 0;
    countdown.value = followup.value ? Math.round(VOICE_TIMING.followup / 1000) : 0;
    // the analyser's audio context is made now, inside the tap (some browsers keep a context made later suspended, and the silence would never end)
    let context: AudioContext | null = null;
    try {
      const AC = window.AudioContext || (window as any).webkitAudioContext;
      context = new AC();
      context.resume?.().catch(() => undefined);
    } catch {
      context = null;
    }
    let s: MediaStream;
    try {
      s = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true } });
    } catch {
      context?.close?.().catch(() => undefined);
      if (!unmounted && state.value === "listening") {
        state.value = "idle";
        caption("system", "اجازه استفاده از میکروفون داده نشد.");
      }
      return;
    }
    if (unmounted || state.value !== "listening" || !context) {
      // the page was left, or Esc was pressed, while the permission question was open: nothing may record
      s.getTracks().forEach((t) => t.stop());
      context?.close?.().catch(() => undefined);
      if (!context && !unmounted && state.value === "listening") {
        state.value = "idle";
        caption("system", "ضبط صدا در این مرورگر آغاز نشد.");
      }
      return;
    }
    stream = s;
    ctx = context;
    try {
      const source = ctx.createMediaStreamSource(s);
      analyser = ctx.createAnalyser();
      analyser.fftSize = 1024;
      analyser.smoothingTimeConstant = 0.15;
      source.connect(analyser);
      recorder = new MediaRecorder(s, { mimeType: recordMime! });
    } catch {
      release();
      state.value = "idle";
      caption("system", "ضبط صدا در این مرورگر آغاز نشد.");
      return;
    }
    chunks = [];
    discard = false;
    manual = false;
    recorder.ondataavailable = (e) => {
      if (e.data && e.data.size > 0) chunks.push(e.data);
    };
    recorder.onstop = onRecorderStop;
    recorder.start(250);
    beginMonitor();
  }

  function beginMonitor() {
    startedAt = performance.now();
    calib = [];
    threshold = null;
    speechMs = 0;
    silentMs = 0;
    monitor = setInterval(tick, 50);
  }

  function rms(): number {
    if (!analyser) return 0;
    analyser.getFloatTimeDomainData(floatBuf);
    let sum = 0;
    for (let i = 0; i < floatBuf.length; i++) sum += floatBuf[i] * floatBuf[i];
    return Math.sqrt(sum / floatBuf.length);
  }

  /**
   * Every 50 ms: the first 300 ms calibrate the noise floor; then speech counts after 150 ms above the threshold, and 1.2 s below 80% of it ends the question.
   * 15 s is the longest question; in a follow-up window nothing said in 6 s closes the microphone quietly; otherwise nothing said in 8 s stops with a caption.
   */
  function tick() {
    if (!analyser) return;
    const t = performance.now() - startedAt;
    const r = rms();
    level.value = Math.min(1, r * 6);
    seconds.value = Math.floor(t / 1000);
    if (t < VOICE_TIMING.calibrate) {
      calib.push(r);
      return;
    }
    if (threshold === null) threshold = thresholdOf(calib);
    if (r > threshold) {
      speechMs += 50;
      silentMs = 0;
      if (speechMs >= VOICE_TIMING.minSpeech) heard.value = true;
    } else {
      if (heard.value && r < threshold * 0.8) silentMs += 50;
      if (!heard.value) speechMs = Math.max(0, speechMs - 25);
    }
    countdown.value = followup.value && !heard.value ? Math.max(0, Math.ceil((VOICE_TIMING.followup - t) / 1000)) : 0;
    if (heard.value && silentMs >= VOICE_TIMING.silence) return finish();
    if (t >= VOICE_TIMING.max) return heard.value ? finish() : quit(followup.value ? "" : "چیزی شنیده نشد؛ دوباره بگویید.");
    if (!heard.value && followup.value && t >= VOICE_TIMING.followup) return quit("");
    if (!heard.value && !followup.value && t >= VOICE_TIMING.noSpeech) return quit("چیزی شنیده نشد؛ دوباره بگویید.");
  }

  function stopRecorder() {
    if (monitor) clearInterval(monitor);
    monitor = null;
    if (recorder && recorder.state !== "inactive") recorder.stop();
    else onRecorderStop();
    // the microphone closes now; the recorder still delivers what it has
    release();
  }
  /** listening ends and the question is sent */
  function finish() {
    discard = false;
    stopRecorder();
  }
  /** listening ends without sending */
  function quit(message: string) {
    discard = true;
    stopRecorder();
    if (message) caption("system", message);
  }

  async function onRecorderStop() {
    const type = recorder?.mimeType || recordMime || "audio/webm";
    const blob = new Blob(chunks, { type });
    chunks = [];
    recorder = null;
    if (unmounted) return;
    if (discard || !handler || (!heard.value && !manual)) {
      if (state.value === "listening") state.value = "idle";
      return;
    }
    if (blob.size < 800) {
      state.value = "idle";
      caption("system", "صدایی ضبط نشد؛ دوباره بگویید.");
      return;
    }
    state.value = "thinking";
    let answered = false;
    try {
      answered = !!(await handler(blob, type));
    } catch {
      caption("system", "پاسخ دریافت نشد.");
    }
    if (unmounted) return;
    // the handler speaks (state "speaking") or not: with nothing to say the answer has "ended" now
    if (state.value === "thinking") {
      state.value = "idle";
      if (answered && continuous.value) start({ followup: true });
    }
  }

  /** The person finished (a tap on the microphone, the end of a held press): the question is sent even when it was quiet. */
  function stop() {
    if (state.value !== "listening") return;
    manual = true;
    finish();
  }

  /** Esc: listening is dropped (nothing is sent), the speech stops, the follow-up window closes, and an answer still on its way will not be spoken. */
  function cancel() {
    epoch++;
    if (state.value === "listening") {
      if (recorder || monitor) quit("");
      state.value = "idle";
    }
    if (state.value === "thinking") state.value = "idle";
    stopSpeaking();
    followup.value = false;
    countdown.value = 0;
  }

  /** The current epoch, taken when a question is asked and handed back to say() with the answer. */
  function mark(): number {
    return epoch;
  }

  // ---------------------------------------------------------------- speaking
  function dropAudio() {
    if (audio) {
      audio.pause();
      audio.removeAttribute("src");
    }
    if (audioUrl) URL.revokeObjectURL(audioUrl);
    audioUrl = null;
  }

  function stopSpeaking() {
    speakToken++;
    dropAudio();
    try {
      window.speechSynthesis?.cancel();
    } catch {
      /* none */
    }
    const r = resolvePlay;
    resolvePlay = null;
    r?.();
    speaking.value = null;
    muted.value = false;
    if (state.value === "speaking") state.value = "idle";
  }

  /** The browser's Persian voice says the text (false when there is none). */
  function speakText(text: string, done: () => void): boolean {
    const voice = persianVoice();
    if (!voice || !text || !window.speechSynthesis) return false;
    try {
      const u = new SpeechSynthesisUtterance(text);
      u.lang = voice.lang || "fa-IR";
      u.voice = voice;
      u.onend = done;
      u.onerror = done;
      window.speechSynthesis.speak(u);
      return true;
    } catch {
      return false;
    }
  }

  /**
   * Plays one item and resolves when it ended, failed or was stopped: the server's audio; when there is none (or the browser cannot play it) the browser's Persian voice;
   * when there is none either, only the caption, which then stays for its reading time (a page left after the answer is not left before the answer could be read).
   */
  function playItem(item: SpeechItem, token: number): Promise<void> {
    return new Promise<void>((resolve) => {
      if (token !== speakToken) return resolve();
      let finished = false;
      const done = () => {
        if (finished) return;
        finished = true;
        if (resolvePlay === done) resolvePlay = null;
        resolve();
      };
      resolvePlay = done;
      const withoutAudio = () => {
        if (finished || token !== speakToken) return done();
        if (speakText(item.text, done)) return;
        muted.value = true;
        setTimeout(done, Math.min(VOICE_TIMING.readMax, Math.max(VOICE_TIMING.readMin, (item.text || "").length * VOICE_TIMING.readPerChar)));
      };
      const blob = item.audioBlob || (item.audioBase64 ? blobOfBase64(item.audioBase64) : null);
      if (!blob || !audio) return withoutAudio();
      dropAudio();
      audioUrl = URL.createObjectURL(blob);
      audio.src = audioUrl;
      audio.onended = done;
      audio.onerror = withoutAudio;
      audio.play().catch(withoutAudio);
    });
  }

  /**
   * Says the items in order (each with its caption), then — unless interrupted — calls after() and, in continuous conversation, opens the follow-up window.
   * Resolves when the speech ended, was interrupted, or was dropped because Esc was pressed (or listening started) after the question was asked (opts.epoch).
   */
  async function say(items: SpeechItem[], opts: SayOptions = {}) {
    if (unmounted) return;
    if (opts.epoch !== undefined && opts.epoch !== epoch) {
      if (state.value === "thinking") state.value = "idle";
      return;
    }
    if (state.value === "listening") quit(""); // a read-out asked for while the microphone was open: the microphone closes first
    stopSpeaking();
    const token = ++speakToken;
    const list = items.filter((i) => i && (i.text || i.audioBase64 || i.audioBlob));
    state.value = "speaking";
    for (const item of list) {
      if (token !== speakToken || unmounted) return;
      speaking.value = item;
      muted.value = false;
      if (item.caption !== false && item.text) caption("assistant", item.text);
      await playItem(item, token);
    }
    if (token !== speakToken || unmounted) return;
    speaking.value = null;
    muted.value = false;
    state.value = "idle";
    opts.after?.();
    if (opts.followup !== false && continuous.value) start({ followup: true });
  }

  /** The page shows "thinking" while it waits for something it will say (a typed command, a read-out). */
  function setThinking(on: boolean) {
    if (on && state.value === "idle") state.value = "thinking";
    else if (!on && state.value === "thinking") state.value = "idle";
  }

  /** Time-domain samples for the waveform; false when nothing is being listened to. */
  function readWave(buf: Uint8Array): boolean {
    if (!analyser || state.value !== "listening") return false;
    analyser.getByteTimeDomainData(buf);
    return true;
  }

  /** The assistant panel gives the function that sends a question and answers it (resolves true when an answer came). */
  function onUtterance(fn: ((blob: Blob, mime: string) => Promise<boolean> | boolean) | null) {
    handler = fn;
  }

  onBeforeUnmount(() => {
    unmounted = true;
    if (recorder && recorder.state !== "inactive") {
      recorder.onstop = null;
      recorder.stop();
    }
    recorder = null;
    release();
    stopSpeaking();
    handler = null;
  });

  return reactive({
    state,
    continuous,
    countdown,
    followup,
    captions,
    speaking,
    muted,
    level,
    heard,
    seconds,
    supported,
    hint,
    start,
    stop,
    cancel,
    say,
    stopSpeaking,
    setThinking,
    readWave,
    caption,
    onUtterance,
    mark,
  });
}

export type VoiceSession = ReturnType<typeof useVoiceSession>;

/** A spoken action confirmation or refusal, shown as a caption line (and listed in the turn). */
export interface ActionResult {
  ok: boolean;
  text: string;
}
/**
 * What the page did with the actions of an answer (§7.1): a caption line per action, the read-outs to say after the answer, and the deferred actions (leaving the page)
 * that run only after the spoken confirmation.
 */
export interface ActionOutcome {
  results: ActionResult[];
  speech: SpeechItem[];
  deferred: (() => void)[];
}
