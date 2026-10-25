<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getSlReasonCodes, slProposalResult } from "@/api/SalesAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { rialText } from "@/composables/sales";
import { blankToNull, groupDigits, numOrNull } from "./pipelineKit";

// Result of a submitted proposal. Won also drafts the contract (terms, customer link, equipment rows) with a draft contract review and project initiation; Lost needs a reason code.
const props = defineProps({ proposal: { type: Object, required: true } });
const emit = defineEmits(["close", "done"]);

const result = ref<"won" | "lost">("won");
const reasons = ref<any[]>([]);
const reasonId = ref<number | null>(null);
const competitor = ref("");
const competitorPrice = ref("");
const lessons = ref("");
const busy = ref(false);

async function loadReasons() {
  const [d] = await getSlReasonCodes({ Kind: result.value === "won" ? 1 : 2, IsActive: true, PageSize: 200 });
  reasons.value = rowsOf(camelize(d));
  reasonId.value = null;
}
async function save() {
  if (result.value === "lost" && !reasonId.value) return fail({ message: "برای پیشنهاد بازنده، دلیل را انتخاب کنید." });
  busy.value = true;
  const [d, error] = await slProposalResult({
    id: props.proposal.id, result: result.value, reasonId: reasonId.value, competitor: blankToNull(competitor.value), competitorPrice: numOrNull(competitorPrice.value),
    lessonsLearned: blankToNull(lessons.value), rowVersion: props.proposal.rowVersion,
  });
  busy.value = false;
  if (error) return fail(error);
  ok(result.value === "won" ? "پیشنهاد برنده شد و قرارداد پیش‌نویس ساخته شد." : "نتیجه بازنده ثبت شد.");
  emit("done", d);
}
onMounted(loadReasons);
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal" style="max-width: 560px">
      <h2 class="inv-title mb-2">ثبت نتیجه پیشنهاد {{ proposal.number }}</h2>
      <div class="d-flex gap-3 mb-2">
        <label class="form-check"><input v-model="result" class="form-check-input" type="radio" value="won" @change="loadReasons" /> <span class="form-check-label">برنده</span></label>
        <label class="form-check"><input v-model="result" class="form-check-input" type="radio" value="lost" @change="loadReasons" /> <span class="form-check-label">بازنده</span></label>
      </div>
      <p v-if="result === 'won'" class="small text-muted">با ثبت برنده، قرارداد پیش‌نویس ساخته و به مشتری وصل می‌شود و بازبینی قرارداد و آغاز پروژه به‌صورت پیش‌نویس در کنترل پروژه ایجاد می‌شود. مبلغ پیشنهاد: {{ rialText(proposal.price) }} ریال.</p>
      <div class="row g-2">
        <div class="col-12">
          <label class="form-label small mb-0">دلیل{{ result === "lost" ? " (الزامی)" : " (اختیاری)" }}</label>
          <select v-model="reasonId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="r in reasons" :key="r.id" :value="r.id">{{ r.title }}</option></select>
          <div v-if="!reasons.length" class="small text-warning">کد دلیل فعالی برای این نوع تعریف نشده است؛ از تنظیمات فروش تعریف کنید.</div>
        </div>
        <div class="col-md-6"><label class="form-label small mb-0">رقیب</label><input v-model="competitor" class="form-control form-control-sm" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">قیمت رقیب (ریال)</label><input v-model="competitorPrice" class="form-control form-control-sm inv-ltr" inputmode="numeric" @blur="competitorPrice = groupDigits(competitorPrice)" /></div>
        <div class="col-12"><label class="form-label small mb-0">درس‌آموخته‌ها</label><textarea v-model="lessons" class="form-control form-control-sm" rows="3" /></div>
      </div>
      <div class="d-flex gap-2 justify-content-end mt-3">
        <button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">{{ result === "won" ? "ثبت برد و تبدیل به قرارداد" : "ثبت باخت" }}</button>
      </div>
    </div>
  </div>
</template>
