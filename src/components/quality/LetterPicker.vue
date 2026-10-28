<script setup lang="ts">
// Picks a letter of the Office that proves something to the quality module (R1q D9): the outgoing letter, already SENT to the client, that carried a witness notice
// ("outgoing"), or the client's registered incoming letter that agreed a control point is not applicable ("incoming"). The list is narrowed to the letters of the contract
// of the plan; the API checks the rest (client as recipient / sender, dates) and answers in Persian.
import { computed, ref, watch } from "vue";
import { getLetters } from "@/api/OfficeAPIs";
import { rowsOf } from "@/api/ApiClient";

const props = defineProps({
  modelValue: { type: Number, default: null },
  /** outgoing: letters of the system with the status Sent; incoming: letters the secretariat registered (Received) or archived */
  mode: { type: String as () => "outgoing" | "incoming", required: true },
  /** the contract of the plan; the letter must be connected to it */
  contractId: { type: Number, default: null },
  disabled: Boolean,
});
const emit = defineEmits<{ (e: "update:modelValue", value: number | null): void }>();

const letters = ref<any[]>([]);
const loading = ref(false);
const contains = ref("");
let token = 0;

const kind = computed(() => (props.mode === "outgoing" ? 2 : 1));
// OfLetterStatus: Sent 4, Received 5, Archived 6
const statuses = computed(() => (props.mode === "outgoing" ? [4] : [5, 6]));

async function load() {
  const mine = ++token;
  loading.value = true;
  const [data] = await getLetters({ Kind: kind.value, Status: props.mode === "outgoing" ? 4 : null, Contains: contains.value || null, PageSize: 100 });
  if (mine !== token) return;
  loading.value = false;
  letters.value = rowsOf(data).filter(
    (l: any) =>
      statuses.value.includes(l.status) &&
      !l.metadataOnly &&
      (props.mode !== "outgoing" || !l.imported) &&
      (props.contractId === null || l.contractId === props.contractId)
  );
}

const label = (l: any) => `${l.number || "بدون شماره"} — ${l.subject || "-"} — ${l.dateText || ""}`;

function onPick(event: Event) {
  const raw = (event.target as HTMLSelectElement).value;
  emit("update:modelValue", raw === "" ? null : Number(raw));
}

watch(() => [props.mode, props.contractId], load, { immediate: true });
</script>

<template>
  <div>
    <div class="d-flex flex-wrap gap-2 align-items-center">
      <select :value="modelValue ?? ''" :disabled="disabled" class="form-select form-select-sm" style="max-width: 420px" @change="onPick">
        <option value="">{{ loading ? "در حال بارگذاری…" : letters.length ? (mode === "outgoing" ? "بدون نامه (تاریخ اعلام = امروز)" : "نامه را انتخاب کنید") : "نامه مناسبی پیدا نشد" }}</option>
        <option v-for="l in letters" :key="l.id" :value="l.id">{{ label(l) }}</option>
      </select>
      <input v-model="contains" :disabled="disabled" class="form-control form-control-sm" placeholder="جستجوی شماره یا موضوع" style="max-width: 200px" @keydown.enter.prevent="load" />
      <button :disabled="disabled" class="btn btn-sm btn-outline-secondary" type="button" @click="load">جستجو</button>
    </div>
    <p class="inv-muted small mb-0 mt-1">
      <template v-if="mode === 'outgoing'">فقط نامه صادره‌ای که دبیرخانه آن را «ارسال‌شده» ثبت کرده و به قرارداد این برنامه وصل است و برای کارفرما رفته است؛ تاریخ اعلام همان تاریخ ارسال نامه است.</template>
      <template v-else>فقط نامه وارده ثبت‌شده در دبیرخانه که کارفرما نوشته و به قرارداد این برنامه وصل است و پس از آخرین تایید برنامه رسیده است.</template>
    </p>
  </div>
</template>
