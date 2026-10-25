<script setup lang="ts">
// The list of the notes of the seats with filters (type, status, seat, rule). Used by «یادداشت‌های هوش مصنوعی» (every type the person may read) and by «یادداشت‌های ناهنجاری مالی»
// (only the given types). The server filters by the roles of the module of each note. Accepting or dismissing a note only closes it; the record the note is about is changed by a person
// in the module of the record.
import { computed, onMounted, ref } from "vue";
import type { PropType } from "vue";
import { useRouter } from "vue-router";
import PromptModal from "@/components/fn/PromptModal.vue";
import QPager from "@/components/quality/QPager.vue";
import { decideAiNote, getAiNotes } from "@/api/AiAPIs";
import { badgeOfNote, frontLink, noteStatuses, noteTypes, readableNoteTypes, ruleTitles, seatTitleOf, seatTitles } from "@/composables/ai";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";

const props = defineProps({
  /** the target types the person may pick; null = every type */
  types: { type: Array as PropType<string[] | null>, default: null },
  title: { type: String, default: "یادداشت‌های هوش مصنوعی" },
  /** show the rule filter and column (the financial anomaly rules) */
  withRule: { type: Boolean, default: false },
  withSeat: { type: Boolean, default: true },
  description: { type: String, default: "فقط یادداشت‌هایی نمایش داده می‌شود که نقش شما اجازه دیدن آن را دارد. پذیرش یا ردِ یادداشت، آن را می‌بندد و رکورد مربوط را تغییر نمی‌دهد." },
});
const router = useRouter();
const filters = ref<any>({ TargetType: null, Status: 1, Seat: null, Rule: null });
/** several fixed types and no type picked: the API takes them comma separated in one request (paged on the server) */
function fetchNotes(params: any) {
  if (props.types && props.types.length >= 1 && !filters.value.TargetType) return getAiNotes({ ...params, TargetType: props.types.join(",") });
  return getAiNotes(params);
}
const list = usePagedList(fetchNotes, filters, 25);
const typeChoices = computed(() => (props.types || readableNoteTypes()).map((k) => ({ key: k, title: noteTypes[k] || k })));
const open = ref<number | null>(null);
const refused = ref<Record<number, string>>({});
const dialog = ref<{ note: any; decision: "accept" | "dismiss" } | null>(null);
const busy = ref(false);
const seats = computed(() => Object.keys(seatTitles).filter((c) => c !== "EVAL"));

async function decide(values: { note?: string }) {
  const d = dialog.value;
  if (!d || busy.value) return;
  dialog.value = null;
  busy.value = true;
  const [data, error] = await decideAiNote({ id: d.note.id, decision: d.decision, note: values.note || null });
  busy.value = false;
  if (error) {
    refused.value = { ...refused.value, [d.note.id]: error.message || "شما مجاز به تصمیم درباره این یادداشت نیستید." };
    return fail(error);
  }
  ok(d.decision === "accept" ? "یادداشت پذیرفته شد." : "یادداشت رد شد.");
  const i = list.rows.value.findIndex((n) => n.id === d.note.id);
  if (i >= 0) list.rows.value.splice(i, 1, data);
}
function go(link: string) {
  const to = frontLink(router, link);
  if (to) router.push(to);
}
onMounted(list.load);
</script>

<template>
  <div>
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">{{ title }}</h1></div>
      <p class="inv-muted">{{ description }}</p>
      <div class="inv-filters mb-3">
        <div>
          <label class="form-label small mb-0">نوع رکورد</label>
          <select v-model="filters.TargetType" class="form-select form-select-sm">
            <option :value="null">همه</option>
            <option v-for="t in typeChoices" :key="t.key" :value="t.key">{{ t.title }}</option>
          </select>
        </div>
        <div>
          <label class="form-label small mb-0">وضعیت</label>
          <select v-model="filters.Status" class="form-select form-select-sm">
            <option :value="null">همه</option>
            <option v-for="(t, k) in noteStatuses" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
        </div>
        <div v-if="withRule">
          <label class="form-label small mb-0">قاعده</label>
          <select v-model="filters.Rule" class="form-select form-select-sm">
            <option :value="null">همه</option>
            <option v-for="(t, k) in ruleTitles" :key="k" :value="k">{{ t }}</option>
          </select>
        </div>
        <div v-if="withSeat">
          <label class="form-label small mb-0">دستیار</label>
          <select v-model="filters.Seat" class="form-select form-select-sm">
            <option :value="null">همه</option>
            <option v-for="c in seats" :key="c" :value="c">{{ seatTitles[c] }}</option>
          </select>
        </div>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>

      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>دستیار</th><th>رکورد</th><th>عنوان</th><th>زمان</th><th>راستی‌آزمایی</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">{{ list.loading.value ? "در حال بارگذاری..." : "یادداشتی نیست." }}</td></tr>
            <template v-for="n in list.rows.value" :key="n.id">
              <tr role="button" @click="open = open === n.id ? null : n.id">
                <td>{{ seatTitleOf(n.seatCode) }}</td>
                <td>{{ noteTypes[n.targetType] || n.targetType }}<span v-if="n.ruleCode" class="inv-badge info ms-1">{{ ruleTitles[n.ruleCode] || n.ruleCode }}</span></td>
                <td>{{ n.title }}</td>
                <td>{{ n.createdAtText }}</td>
                <td><span :class="n.verified ? 'ok' : 'warn'" class="inv-badge">{{ n.verified ? "راستی‌آزمایی‌شده" : "راستی‌آزمایی‌نشده" }}</span></td>
                <td><span :class="badgeOfNote(n.status)" class="inv-badge">{{ noteStatuses[n.status] }}</span></td>
                <td><button v-if="frontLink(router, n.link)" class="btn btn-sm btn-outline-primary" @click.stop="go(n.link)">باز کردن رکورد</button></td>
              </tr>
              <tr v-if="open === n.id">
                <td colspan="7" class="bg-light">
                  <div class="note-text">{{ n.text }}</div>
                  <div v-if="n.status !== 1" class="inv-muted mt-1">
                    {{ n.status === 2 ? "پذیرفته" : "ردشده" }} توسط {{ n.decidedByName || "-" }} در {{ n.decidedAtText }}<span v-if="n.decisionNote"> — {{ n.decisionNote }}</span>
                  </div>
                  <div v-else-if="refused[n.id]" class="alert alert-warning py-1 px-2 small mt-2 mb-0">{{ refused[n.id] }}</div>
                  <div v-else class="d-flex gap-2 mt-2">
                    <button :disabled="busy" class="btn btn-sm btn-outline-success" @click="dialog = { note: n, decision: 'accept' }">پذیرش</button>
                    <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="dialog = { note: n, decision: 'dismiss' }">رد</button>
                  </div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :page-size="list.pageSize" :total="list.total.value" @go="list.go" />
    </div>

    <PromptModal
      v-if="dialog"
      :confirm-text="dialog.decision === 'accept' ? 'پذیرش' : 'رد'"
      :fields="[{ key: 'note', label: 'توضیح (اختیاری، تا ۱۰۰۰ نویسه)', type: 'textarea', maxlength: 1000 }]"
      hint="این کار فقط یادداشت دستیار را می‌بندد؛ رکورد مربوط تغییر نمی‌کند."
      :title="dialog.decision === 'accept' ? 'پذیرش یادداشت' : 'رد یادداشت'"
      @cancel="dialog = null"
      @confirm="decide" />
  </div>
</template>

<style scoped>
.note-text {
  white-space: pre-wrap;
  line-height: 1.8;
  overflow-wrap: anywhere;
}
</style>
