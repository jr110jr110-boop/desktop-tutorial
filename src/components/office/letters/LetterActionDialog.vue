<script setup lang="ts">
// The dialog of one letter action (submit, approve, return, sign, register, send, receipt, archive, cancel).
// It only collects the extra values; the page calls letterAction() with them.
import { computed, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import PickSelect from "./PickSelect.vue";
import { uploadFile } from "@/api/InventoryAPIs";
import { actionTitles, deliveryMethods } from "@/composables/office";
import { fail, today } from "@/composables/inventory";
import { positionText, type MyPosition } from "./letterKit";

const props = defineProps<{ action: string; letter: any; myPositions: MyPosition[]; positions: any[]; templateChain: number[] }>();
const emit = defineEmits<{ (e: "confirm", payload: any): void; (e: "cancel"): void }>();

const needsActing = computed(() => ["approve", "return", "sign"].includes(props.action) && props.myPositions.length > 1);
const pendingPosition = (): number | null => {
  const mine = new Set(props.myPositions.map((p) => p.positionId));
  const approvals: any[] = props.letter.approvals || [];
  const round = props.letter.approvalRound ?? 0;
  const next = approvals.filter((a) => a.round === round && a.status === 0).sort((a, b) => a.seq - b.seq)[0];
  if (props.action !== "sign" && next && mine.has(next.positionId)) return next.positionId;
  if ((props.action === "sign" || props.action === "return") && props.letter.signerPositionId && mine.has(props.letter.signerPositionId)) return props.letter.signerPositionId;
  return props.myPositions[0]?.positionId ?? null;
};
const acting = ref<number | null>(needsActing.value ? pendingPosition() : null);
const comment = ref("");
const chain = ref<number[]>([...(props.templateChain || [])]);
const pick = ref<number | null>(null);
const method = ref<number | null>(null);
const date = ref<string | null>(today());
const receipt = ref(""); // receipt action only
const receiptDoc = ref<{ id: string; name: string } | null>(null);
const busy = ref(false);

// the reason of a cancel is required only for a letter that already has a number
const cancelNeedsReason = computed(() => !!props.letter.number);
const options = computed(() => props.positions.filter((p) => !chain.value.includes(p.id)).map((p) => ({ value: p.id, text: positionText(p) })));
const titleOf = (id: number) => positionText(props.positions.find((p) => p.id === id)) || String(id);
const hints: Record<string, string> = {
  sign: "نامه با تصویر امضای ثبت‌شده شما امضا می‌شود، اثر انگشت محتوا ذخیره می‌گردد و پس از آن هیچ ویرایشی ممکن نیست.",
  register: "نامه شماره می‌گیرد و در دفتر دبیرخانه ثبت می‌شود.",
  archive: "نامه بایگانی می‌شود.",
  submit: "نامه به ترتیب زیر برای تایید می‌رود. اگر فهرست خالی بماند زنجیره قالب نامه به کار می‌رود.",
};

function move(i: number, d: number) {
  const j = i + d;
  if (j < 0 || j >= chain.value.length) return;
  [chain.value[i], chain.value[j]] = [chain.value[j], chain.value[i]];
}
function addChain() {
  if (pick.value && !chain.value.includes(pick.value)) chain.value.push(pick.value);
  pick.value = null;
}
async function onReceiptFile(event: any) {
  const file: File | undefined = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  busy.value = true;
  const [id, error] = await uploadFile(file, file.name);
  busy.value = false;
  if (error || !id) return fail(error || "بارگذاری فایل انجام نشد.");
  receiptDoc.value = { id: id as string, name: file.name };
}
function confirm() {
  const a = props.action;
  if (needsActing.value && !acting.value) return fail("سمتی را که از طرف آن اقدام می‌کنید انتخاب کنید.");
  if (a === "return" && !comment.value.trim()) return fail("دلیل برگشت را بنویسید.");
  if (a === "cancel" && cancelNeedsReason.value && !comment.value.trim()) return fail("دلیل ابطال را بنویسید.");
  if (a === "send" && !method.value) return fail("نحوه ارسال را انتخاب کنید.");
  if (a === "send" && !date.value) return fail("تاریخ ارسال را وارد کنید.");
  emit("confirm", {
    approvalChain: a === "submit" ? chain.value : null,
    actingPositionId: acting.value,
    comment: comment.value.trim() || null,
    deliveryMethod: a === "send" ? method.value : null,
    date: a === "send" || a === "receipt" ? date.value : null,
    receipt: a === "receipt" ? receipt.value.trim() || null : null,
    receiptDocumentId: a === "receipt" ? receiptDoc.value?.id || null : null,
  });
}
</script>

<template>
  <div class="inv-modal-backdrop screen-only" @click.self="emit('cancel')">
    <div class="inv-modal">
      <h2 class="inv-title mb-2">{{ actionTitles[action] || action }}</h2>
      <p v-if="hints[action]" class="inv-muted">{{ hints[action] }}</p>

      <div v-if="needsActing" class="mb-2">
        <label class="form-label small mb-0">اقدام از طرف سمت</label>
        <select v-model.number="acting" class="form-select form-select-sm"><option :value="null">انتخاب سمت</option><option v-for="p in myPositions" :key="p.positionId" :value="p.positionId">{{ positionText(p) }}{{ p.onBehalf ? " (به نیابت)" : "" }}</option></select>
      </div>

      <template v-if="action === 'submit'">
        <ol v-if="chain.length" class="mb-2">
          <li v-for="(id, i) in chain" :key="id" class="mb-1">
            {{ titleOf(id) }}
            <button class="btn btn-sm btn-link p-0 mx-1" @click="move(i, -1)">بالا</button>
            <button class="btn btn-sm btn-link p-0 mx-1" @click="move(i, 1)">پایین</button>
            <button class="btn btn-sm btn-link text-danger p-0 mx-1" @click="chain.splice(i, 1)">حذف</button>
          </li>
        </ol>
        <div class="d-flex gap-2 align-items-start">
          <div style="flex: 1"><PickSelect v-model="pick" :options="options" placeholder="افزودن سمت به زنجیره تایید" /></div>
          <button class="btn btn-sm btn-outline-primary" @click="addChain">افزودن</button>
        </div>
      </template>

      <template v-if="action === 'send'">
        <div class="row g-2 mb-2">
          <div class="col-md-6"><label class="form-label small mb-0">نحوه ارسال *</label><select v-model.number="method" class="form-select form-select-sm"><option :value="null">انتخاب</option><option v-for="(t, k) in deliveryMethods" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">تاریخ ارسال *</label><FnDate v-model="date" /></div>
        </div>
      </template>

      <template v-if="action === 'receipt'">
        <div class="mb-2"><label class="form-label small mb-0">تاریخ دریافت</label><FnDate v-model="date" /></div>
        <label class="form-label small mb-0">توضیح رسید</label>
        <input v-model="receipt" class="form-control form-control-sm mb-2" />
        <label class="btn btn-sm btn-outline-secondary mb-0">فایل رسید<input class="d-none" type="file" @change="onReceiptFile" /></label>
        <span v-if="receiptDoc" class="small me-2">{{ receiptDoc.name }}</span>
      </template>

      <template v-if="['approve', 'return', 'cancel'].includes(action)">
        <label class="form-label small mb-0 mt-2">{{ action === "cancel" ? (cancelNeedsReason ? "دلیل ابطال *" : "دلیل ابطال") : action === "return" ? "دلیل برگشت *" : "توضیح" }}</label>
        <textarea v-model="comment" class="form-control form-control-sm" rows="2"></textarea>
      </template>

      <div class="d-flex gap-2 justify-content-end mt-3">
        <button class="btn btn-sm btn-outline-secondary" @click="emit('cancel')">انصراف</button>
        <button :class="action === 'cancel' ? 'btn-danger' : 'btn-primary'" :disabled="busy" class="btn btn-sm" @click="confirm">{{ actionTitles[action] || action }}</button>
      </div>
    </div>
  </div>
</template>
