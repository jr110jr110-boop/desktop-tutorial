<script setup lang="ts">
// One leave / mission / overtime / forgotten-clock request with its actions. Buttons come from allowedActions; the server checks again.
import { computed, onMounted, ref } from "vue";
import { downloadDocument, getLeave, leaveAction } from "@/api/OfficeAPIs";
import { fail, ok } from "@/composables/inventory";
import { actionTitles, badgeOfLeave, can, clockDirections, leaveKinds, leaveStatuses, persianOf, dateTimeText } from "@/composables/office";
import { days2, useMyOffice } from "./attCommon";

const props = defineProps<{ id: number }>();
const emit = defineEmits(["close", "changed", "edit"]);
const leave = ref<any>(null);
const missing = ref(false);
const busy = ref(false);
const me = useMyOffice();
const asking = ref<null | { action: "reject" | "approve" | "cancel" | "record"; required: boolean }>(null);
const comment = ref("");
const acting = ref<number | "">("");

async function load() {
  const [d, error] = await getLeave(props.id);
  if (error) {
    missing.value = true;
    return fail(error);
  }
  leave.value = d;
}
const myPositions = computed<any[]>(() => (me.value?.positions || []));
// positions that can act for the approver step: the approver position itself, or any managerial one when it is not known
const actingChoices = computed(() => {
  const l = leave.value;
  const exact = myPositions.value.filter((p) => p.positionId === l?.approverPositionId);
  return exact.length ? exact : myPositions.value.filter((p) => p.isManager);
});
async function attachment() {
  const error = await downloadDocument(leave.value.attachmentDocumentId, "پیوست درخواست");
  if (error) fail(error);
}
const buttons = ["submit", "approve", "reject", "record", "cancel"];

async function run(action: "submit" | "approve" | "reject" | "record" | "cancel", text?: string) {
  const l = leave.value;
  const position = action === "approve" ? Number(acting.value) || (actingChoices.value.length === 1 ? actingChoices.value[0].positionId : null) : null;
  busy.value = true;
  const [, error] = await leaveAction(l.id, action, text || null, position, l.rowVersion);
  busy.value = false;
  if (error) return fail(error);
  ok(`${actionTitles[action]} انجام شد.`);
  asking.value = null;
  comment.value = "";
  await load();
  emit("changed");
}
function ask(action: "approve" | "reject" | "cancel" | "record") {
  comment.value = "";
  acting.value = actingChoices.value.length > 1 ? actingChoices.value[0].positionId : "";
  asking.value = { action, required: action === "reject" };
}
function press(a: string) {
  if (a === "submit") return run("submit");
  if (a === "approve" || a === "reject" || a === "cancel" || a === "record") return ask(a);
}
function confirmAsk() {
  const q = asking.value!;
  if (q.required && !comment.value.trim()) return fail("دلیل رد درخواست را بنویسید.");
  if (q.action === "approve" && actingChoices.value.length > 1 && !acting.value) return fail("سمت تاییدکننده را انتخاب کنید.");
  return run(q.action, comment.value.trim());
}
const placeText = (l: any) => (l.fromDateText === l.toDateText || !l.toDateText ? l.fromDateText : `${l.fromDateText} تا ${l.toDateText}`);
onMounted(load);
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal">
      <div v-if="missing" class="text-center py-3"><p>درخواست پیدا نشد یا اجازه دیدن آن را ندارید.</p><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">بستن</button></div>
      <template v-else-if="leave">
        <div class="d-flex align-items-center gap-2 mb-2">
          <h2 class="inv-title mb-0">{{ leaveKinds[leave.kind] }} <span class="inv-ltr">{{ leave.number }}</span></h2>
          <span :class="badgeOfLeave(leave.status)" class="inv-badge">{{ leaveStatuses[leave.status] }}</span>
        </div>
        <table class="table table-sm">
          <tbody>
            <tr><th style="width: 9rem">درخواست‌کننده</th><td>{{ leave.employeeName }}</td></tr>
            <tr><th>تاریخ</th><td class="inv-ltr">{{ placeText(leave) }}</td></tr>
            <tr v-if="leave.fromTime"><th>ساعت</th><td class="inv-ltr">{{ leave.fromTime }}<template v-if="leave.toTime"> تا {{ leave.toTime }}</template></td></tr>
            <tr v-if="leave.kind === 7 && leave.direction"><th>جهت تردد</th><td>{{ clockDirections[leave.direction] }}</td></tr>
            <tr v-if="leave.kind === 6"><th>ساعت اضافه‌کار</th><td class="fn-num">{{ days2(leave.hours) }}</td></tr>
            <tr v-if="leave.kind !== 5 && leave.kind !== 6 && leave.kind !== 7"><th>مدت (روز)</th><td class="fn-num">{{ days2(leave.days) }}</td></tr>
            <tr v-if="leave.reason"><th>توضیح</th><td>{{ leave.reason }}</td></tr>
            <tr v-if="leave.attachmentDocumentId"><th>پیوست</th><td><button class="btn btn-sm btn-link p-0" @click="attachment">دریافت پیوست</button></td></tr>
            <tr v-if="leave.approverPositionTitle"><th>تاییدکننده</th><td>{{ leave.approverPositionTitle }}</td></tr>
            <tr v-if="leave.approvedByName"><th>تایید توسط</th><td>{{ leave.approvedByName }}<span v-if="leave.approvedOnBehalf"> (به نیابت)</span> <span class="inv-ltr">{{ persianOf(null, leave.approvedAt, true) }}</span></td></tr>
            <tr v-if="leave.approverComment"><th>نظر تاییدکننده</th><td>{{ leave.approverComment }}</td></tr>
            <tr v-if="leave.recordedByName"><th>ثبت در حضور و غیاب</th><td>{{ leave.recordedByName }} <span class="inv-ltr">{{ dateTimeText(leave.recordedAt) }}</span></td></tr>
            <tr v-if="leave.rejectReason"><th>دلیل رد</th><td class="text-danger">{{ leave.rejectReason }}</td></tr>
          </tbody>
        </table>
        <div class="d-flex gap-2 justify-content-end flex-wrap">
          <button class="btn btn-sm btn-outline-secondary" @click="emit('close')">بستن</button>
          <button v-if="can(leave, 'save')" class="btn btn-sm btn-outline-primary" @click="emit('edit', leave)">ویرایش</button>
          <button v-for="a in buttons" v-show="can(leave, a)" :key="a" :disabled="busy" :class="a === 'reject' || a === 'cancel' ? 'btn-outline-danger' : 'btn-primary'" class="btn btn-sm" @click="press(a)">{{ actionTitles[a] }}</button>
        </div>

        <div v-if="asking" class="border rounded p-2 mt-3">
          <div v-if="asking.action === 'approve' && actingChoices.length > 1" class="mb-2">
            <label class="form-label small mb-0">تایید در سمت</label>
            <select v-model="acting" class="form-select form-select-sm"><option value="">انتخاب کنید</option><option v-for="p in actingChoices" :key="p.positionId" :value="p.positionId">{{ p.title }}{{ p.onBehalf ? " (به نیابت)" : "" }}</option></select>
          </div>
          <label class="form-label small mb-0">{{ asking.action === "reject" ? "دلیل رد *" : "توضیح (اختیاری)" }}</label>
          <textarea v-model="comment" class="form-control form-control-sm" rows="2"></textarea>
          <p v-if="asking.action === 'cancel'" class="inv-muted small mb-0 mt-1">درخواست باطل می‌شود و دوباره قابل ارسال نیست.</p>
          <div class="d-flex gap-2 justify-content-end mt-2"><button class="btn btn-sm btn-outline-secondary" @click="asking = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="confirmAsk">{{ actionTitles[asking.action] }}</button></div>
        </div>
      </template>
      <p v-else class="inv-muted">در حال بارگذاری...</p>
    </div>
  </div>
</template>
