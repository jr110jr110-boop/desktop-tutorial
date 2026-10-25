<script setup lang="ts">
// The notes the AI seats left for one record (a gate of a module). The server decides who may read and who may decide; a refusal is shown with its own message and the buttons of that note
// are hidden. Accepting or dismissing only closes the note: it never changes the record it is about (a person does that behind the module's own gate). Nothing here approves anything.
import { onMounted, ref, watch } from "vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import { decideAiNote, getAiNotes } from "@/api/AiAPIs";
import { badgeOfNote, noteStatuses, ruleTitles, seatTitleOf } from "@/composables/ai";
import { fail, ok } from "@/composables/inventory";

const props = defineProps({
  targetType: { type: String, required: true },
  targetId: { type: Number, default: null },
  ruleCode: { type: String, default: null },
  /** the title above the box */
  title: { type: String, default: "یادداشت‌های دستیار هوشمند" },
});
const emit = defineEmits(["changed"]);

const notes = ref<any[]>([]);
const loading = ref(false);
const refused = ref<Record<number, string>>({});
const dialog = ref<{ note: any; decision: "accept" | "dismiss" } | null>(null);
const busy = ref(false);

const PAGE = 50;
const page = ref(1);
const total = ref(0);
let token = 0;

/** more = false reads the first page again; true appends the next one. An older answer that arrives after a newer request was sent is dropped. */
async function load(more = false) {
  const mine = ++token;
  const wanted = more === true ? page.value + 1 : 1;
  loading.value = true;
  const [data, error] = await getAiNotes({ TargetType: props.targetType, TargetId: props.targetId, Rule: props.ruleCode, PageNumber: wanted, PageSize: PAGE });
  if (mine !== token) return;
  loading.value = false;
  // a record the person may open but whose notes the person may not read simply shows no box
  if (error) {
    if (wanted === 1) {
      notes.value = [];
      total.value = 0;
    }
    return;
  }
  page.value = wanted;
  total.value = data?.totalItems ?? (data?.data || []).length;
  notes.value = wanted === 1 ? data?.data || [] : [...notes.value, ...(data?.data || [])];
}

function ask(note: any, decision: "accept" | "dismiss") {
  dialog.value = { note, decision };
}

async function decide(values: { note?: string }) {
  const d = dialog.value;
  if (!d || busy.value) return;
  dialog.value = null;
  busy.value = true;
  const [data, error] = await decideAiNote({ id: d.note.id, decision: d.decision, note: values.note || null });
  busy.value = false;
  if (error) {
    refused.value = { ...refused.value, [d.note.id]: error.message || "شما مجاز به تصمیم درباره این یادداشت نیستید." };
    fail(error);
    load(); // the note may have been decided by somebody else meanwhile: show what the server has now
    return;
  }
  ok(d.decision === "accept" ? "یادداشت پذیرفته شد." : "یادداشت رد شد.");
  const i = notes.value.findIndex((n) => n.id === d.note.id);
  if (i >= 0) notes.value.splice(i, 1, data);
  emit("changed");
}

onMounted(() => load());
watch(() => [props.targetType, props.targetId, props.ruleCode], () => load());
defineExpose({ reload: () => load() });
</script>

<template>
  <div v-if="notes.length" class="inv-card screen-only ai-notes">
    <div class="inv-toolbar">
      <h2 class="inv-title">{{ title }}</h2>
      <span class="inv-muted">پیشنهاد دستیار است و هیچ رکوردی را تغییر نمی‌دهد؛ پذیرش فقط یادداشت را می‌بندد.</span>
    </div>
    <div v-for="n in notes" :key="n.id" class="ai-note">
      <div class="d-flex flex-wrap gap-2 align-items-center mb-1">
        <b>{{ n.title || seatTitleOf(n.seatCode) }}</b>
        <span class="inv-muted">{{ seatTitleOf(n.seatCode) }}</span>
        <span class="inv-muted">{{ n.createdAtText }}</span>
        <span v-if="n.ruleCode" class="inv-badge info">{{ ruleTitles[n.ruleCode] || n.ruleCode }}</span>
        <span v-if="n.verified" class="inv-badge ok">راستی‌آزمایی‌شده</span>
        <span v-else class="inv-badge warn">راستی‌آزمایی‌نشده</span>
        <span :class="badgeOfNote(n.status)" class="inv-badge">{{ noteStatuses[n.status] }}</span>
      </div>
      <div class="ai-note-text">{{ n.text }}</div>
      <div v-if="n.status !== 1" class="inv-muted mt-1">
        {{ n.status === 2 ? "پذیرفته" : "ردشده" }} توسط {{ n.decidedByName || "-" }} در {{ n.decidedAtText }}<span v-if="n.decisionNote"> — {{ n.decisionNote }}</span>
      </div>
      <div v-else-if="refused[n.id]" class="alert alert-warning py-1 px-2 small mt-2 mb-0">{{ refused[n.id] }}</div>
      <div v-else class="d-flex gap-2 mt-2">
        <button :disabled="busy" class="btn btn-sm btn-outline-success" @click="ask(n, 'accept')">پذیرش</button>
        <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="ask(n, 'dismiss')">رد</button>
      </div>
    </div>

    <div v-if="notes.length < total" class="mt-2">
      <button :disabled="loading" class="btn btn-sm btn-outline-secondary" @click="load(true)">بیشتر ({{ total - notes.length }} یادداشت دیگر)</button>
    </div>

    <PromptModal
      v-if="dialog"
      :confirm-text="dialog.decision === 'accept' ? 'پذیرش' : 'رد'"
      :fields="[{ key: 'note', label: 'توضیح (اختیاری، تا ۱۰۰۰ نویسه)', type: 'textarea', maxlength: 1000 }]"
      :hint="'این کار فقط یادداشت دستیار را می‌بندد؛ رکورد مربوط تغییر نمی‌کند.'"
      :title="dialog.decision === 'accept' ? 'پذیرش یادداشت' : 'رد یادداشت'"
      @cancel="dialog = null"
      @confirm="decide" />
  </div>
</template>

<style scoped>
.ai-note {
  border-top: 1px solid #e9ecef;
  padding: 0.6rem 0;
}
.ai-note-text {
  white-space: pre-wrap;
  line-height: 1.8;
  overflow-wrap: anywhere;
}
</style>
