<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import AiNotesBox from "@/components/ai/AiNotesBox.vue";
import MeetingAttendees from "@/components/office/wf/MeetingAttendees.vue";
import MeetingDecisionsEditor from "@/components/office/wf/MeetingDecisionsEditor.vue";
import MeetingPrint from "@/components/office/wf/MeetingPrint.vue";
import DateTimeField from "@/components/office/wf/DateTimeField.vue";
import PositionSelect from "@/components/office/wf/PositionSelect.vue";
import { getMeeting, meetingAction, saveMeeting } from "@/api/OfficeAPIs";
import { printSheet } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { actionTitles, badgeOfMeeting, can, meetingKinds, meetingStatuses, relatedKinds } from "@/composables/office";

// Meeting editor and minutes workflow. Buttons come from allowedActions of the meeting; the API checks again on every call.
const route = useRoute();
const router = useRouter();
const meeting = ref<any>(null);
const m = ref<any>(blank());
const snapshot = ref("");
const busy = ref(false);
const holding = ref<null | { ids: number[] }>(null);
const cancelling = ref<null | { reason: string }>(null);
const isNew = computed(() => route.params.id === "new");
const readOnly = computed(() => !isNew.value && !can(meeting.value, "save"));
const dirty = computed(() => JSON.stringify(m.value) !== snapshot.value);
const flowActions = computed(() => ["hold", "submit-minutes", "approve-minutes", "sign-minutes", "cancel"].filter((a) => can(meeting.value, a)));

function blank() {
  return { id: null as number | null, title: "", kind: 1, startAt: null as string | null, endAt: null as string | null, place: "", chairPositionId: null as number | null, secretaryPositionId: null as number | null, agenda: [] as any[], attendees: [] as any[], decisions: [] as any[], links: [] as any[], rowVersion: null as string | null };
}
function fill(d: any) {
  meeting.value = d;
  m.value = {
    id: d.id, title: d.title, kind: d.kind, startAt: d.startAt ? String(d.startAt).slice(0, 19) : null, endAt: d.endAt ? String(d.endAt).slice(0, 19) : null, place: d.place || "",
    chairPositionId: d.chairPositionId, secretaryPositionId: d.secretaryPositionId,
    agenda: (d.agenda || []).map((a: any) => ({ id: a.id, title: a.title, minutes: a.minutes || "" })),
    attendees: (d.attendees || []).map((a: any) => ({ ...a })),
    decisions: (d.decisions || []).map((x: any) => ({ id: x.id, text: x.text, ownerPositionId: x.ownerPositionId, ownerPositionTitle: x.ownerPositionTitle, dueDate: x.dueDate, agendaSeq: x.agendaSeq, status: x.status })),
    links: (d.links || []).map((l: any) => ({ kind: l.kind, recordId: l.recordId, text: l.text || "" })),
    rowVersion: d.rowVersion,
  };
  snapshot.value = JSON.stringify(m.value);
}
async function load() {
  if (isNew.value) {
    meeting.value = null;
    m.value = blank();
    snapshot.value = JSON.stringify(m.value);
    return;
  }
  const [d, error] = await getMeeting(Number(route.params.id));
  if (error) return fail(error);
  fill(d);
}
watch(() => route.params.id, load, { immediate: true });

function removeAgenda(i: number) {
  const seq = i + 1;
  m.value.agenda.splice(i, 1);
  for (const d of m.value.decisions) {
    if (d.agendaSeq === seq) d.agendaSeq = null;
    else if (d.agendaSeq > seq) d.agendaSeq -= 1;
  }
}

async function save() {
  const f = m.value;
  busy.value = true;
  const [d, error] = await saveMeeting({
    id: f.id, title: f.title, kind: f.kind, startAt: f.startAt, endAt: f.endAt || null, place: f.place, chairPositionId: f.chairPositionId, secretaryPositionId: f.secretaryPositionId,
    agenda: f.agenda.map((a: any, i: number) => ({ id: a.id || null, seq: i + 1, title: a.title, minutes: a.minutes })),
    attendees: f.attendees.map((a: any) => ({ positionId: a.positionId || null, userId: a.userId || null, externalName: a.positionId || a.userId ? null : a.externalName, present: !!a.present })),
    links: f.links.filter((l: any) => l.recordId || l.text).map((l: any) => ({ kind: l.kind, recordId: l.recordId || null, text: l.text || null })),
    // finished decisions stay as they are on the server; only open ones are sent
    decisions: f.decisions.filter((x: any) => !x.id || x.status === 0).map((x: any) => ({ id: x.id || null, text: x.text, ownerPositionId: x.ownerPositionId, dueDate: x.dueDate ? String(x.dueDate).slice(0, 10) : null, agendaSeq: x.agendaSeq || null })),
    rowVersion: f.rowVersion,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("جلسه ذخیره شد.");
  if (isNew.value) router.replace({ name: "OfMeeting", params: { id: d.id } });
  else fill(d);
}

async function run(action: string, extra: any = {}) {
  busy.value = true;
  const [d, error] = await meetingAction({ id: meeting.value.id, action, rowVersion: meeting.value.rowVersion, ...extra });
  busy.value = false;
  if (error) return fail(error);
  ok(action === "approve-minutes" ? "صورتجلسه تایید شد؛ مصوبات به کارتابل مسئولان رفت." : "انجام شد.");
  holding.value = null;
  cancelling.value = null;
  fill(d);
}
function start(action: string) {
  if (action === "hold") holding.value = { ids: meeting.value.attendees.filter((a: any) => a.present).map((a: any) => a.id) };
  else if (action === "cancel") cancelling.value = { reason: "" };
  else if (action === "approve-minutes" && !confirm("صورتجلسه تایید شود؟ پس از تایید ویرایش نمی‌شود.")) return;
  else run(action);
}
async function print() {
  await nextTick();
  printSheet();
}
function toggleHolding(id: number) {
  const ids = holding.value!.ids;
  const at = ids.indexOf(id);
  if (at >= 0) ids.splice(at, 1);
  else ids.push(id);
}
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">
          {{ isNew ? "جلسه جدید" : "جلسه " + (meeting?.number || "") }}
          <span v-if="meeting" :class="badgeOfMeeting(meeting.status)" class="inv-badge ms-2">{{ meetingStatuses[meeting.status] }}</span>
        </h1>
        <div class="d-flex gap-1 flex-wrap">
          <router-link :to="{ name: 'OfMeetings' }" class="btn btn-sm btn-outline-secondary">بازگشت به فهرست</router-link>
          <button v-if="meeting" class="btn btn-sm btn-outline-secondary" @click="print">چاپ صورتجلسه</button>
          <button v-if="isNew || !readOnly" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>

      <div v-if="meeting?.status === 4" class="alert alert-danger py-1 small">جلسه لغو شد: {{ meeting.cancelReason }}</div>
      <div v-if="meeting?.minutesApprovedAtText" class="alert alert-success py-1 small">صورتجلسه در {{ meeting.minutesApprovedAtText }} توسط {{ meeting.minutesApprovedByName }}{{ meeting.minutesApprovedOnBehalf ? " (به نیابت رئیس جلسه)" : "" }} تایید شد.</div>

      <div class="row g-2">
        <div class="col-12 col-md-6"><label class="form-label small mb-0">عنوان جلسه</label><input v-model="m.title" :disabled="readOnly" class="form-control form-control-sm" /></div>
        <div class="col-6 col-md-3"><label class="form-label small mb-0">نوع</label><select v-model="m.kind" :disabled="readOnly" class="form-select form-select-sm"><option v-for="(t, k) in meetingKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-6 col-md-3"><label class="form-label small mb-0">محل</label><input v-model="m.place" :disabled="readOnly" class="form-control form-control-sm" placeholder="مثلا اتاق جلسات" /></div>
        <div class="col-12 col-md-4"><label class="form-label small mb-0">شروع (تاریخ و ساعت)</label><DateTimeField v-model="m.startAt" :disabled="readOnly" /></div>
        <div class="col-12 col-md-4"><label class="form-label small mb-0">پایان (اختیاری)</label><DateTimeField v-model="m.endAt" :disabled="readOnly" /></div>
        <div class="col-6 col-md-2"><label class="form-label small mb-0">رئیس جلسه</label><PositionSelect v-model="m.chairPositionId" :disabled="readOnly" /></div>
        <div class="col-6 col-md-2"><label class="form-label small mb-0">دبیر جلسه</label><PositionSelect v-model="m.secretaryPositionId" :disabled="readOnly" /></div>
      </div>

      <div v-if="meeting && flowActions.length" class="mt-3 p-2 border rounded bg-light d-flex flex-wrap gap-2 align-items-center">
        <b class="small">اقدام:</b>
        <button v-for="a in flowActions" :key="a" :disabled="busy || dirty" :class="a === 'cancel' ? 'btn-outline-danger' : 'btn-success'" class="btn btn-sm" @click="start(a)">{{ actionTitles[a] }}</button>
        <span v-if="dirty" class="inv-muted small">ابتدا تغییرات را ذخیره کنید.</span>
      </div>

      <h2 class="h6 mt-3">دستور جلسه و مذاکرات</h2>
      <div v-for="(a, i) in m.agenda" :key="i" class="border rounded p-2 mb-2">
        <div class="d-flex gap-2 mb-1"><span class="fn-num mt-1">{{ i + 1 }}.</span><input v-model="a.title" :disabled="readOnly" class="form-control form-control-sm" placeholder="موضوع بند" /><button v-if="!readOnly" class="btn btn-sm btn-outline-danger" @click="removeAgenda(i)">حذف</button></div>
        <textarea v-model="a.minutes" :disabled="readOnly" class="form-control form-control-sm" rows="2" placeholder="خلاصه مذاکرات (متن صورتجلسه)"></textarea>
      </div>
      <button v-if="!readOnly" class="btn btn-sm btn-outline-primary" @click="m.agenda.push({ id: null, title: '', minutes: '' })">بند جدید</button>

      <h2 class="h6 mt-3">حاضران</h2>
      <MeetingAttendees :list="m.attendees" :read-only="readOnly" :show-present="!isNew && (meeting?.status ?? 0) >= 1" />

      <h2 class="h6 mt-3">مصوبات</h2>
      <MeetingDecisionsEditor :list="m.decisions" :agenda="m.agenda" :read-only="readOnly" />

      <h2 class="h6 mt-3">پیوندها</h2>
      <div v-for="(l, i) in m.links" :key="i" class="inv-filters mb-1">
        <select v-model="l.kind" :disabled="readOnly" class="form-select form-select-sm"><option v-for="(t, k) in relatedKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
        <input v-model.number="l.recordId" :disabled="readOnly" class="form-control form-control-sm inv-ltr" placeholder="شناسه رکورد" />
        <input v-model="l.text" :disabled="readOnly" class="form-control form-control-sm" placeholder="توضیح" />
        <button v-if="!readOnly" class="btn btn-sm btn-outline-danger" @click="m.links.splice(i, 1)">حذف</button>
      </div>
      <button v-if="!readOnly" class="btn btn-sm btn-outline-primary" @click="m.links.push({ kind: 99, recordId: null, text: '' })">پیوند جدید</button>
    </div>

    <MeetingPrint v-if="meeting" :m="meeting" />

    <div v-if="holding" class="inv-modal-backdrop screen-only" @click.self="holding = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">برگزاری جلسه؛ حاضران را علامت بزنید</h2>
        <div v-for="a in meeting.attendees" :key="a.id" class="form-check">
          <input :id="'at' + a.id" :checked="holding.ids.includes(a.id)" type="checkbox" class="form-check-input" @change="toggleHolding(a.id)" />
          <label :for="'at' + a.id" class="form-check-label">{{ a.positionTitle || a.userName || a.externalName }}</label>
        </div>
        <p v-if="!meeting.attendees.length" class="inv-muted small">حاضری ثبت نشده است.</p>
        <p class="inv-muted small mt-2 mb-0">اگر هیچ‌کس علامت نخورد، همان وضعیت ذخیره‌شده حفظ می‌شود.</p>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="holding = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-success" @click="run('hold', { presentAttendeeIds: holding.ids })">ثبت برگزاری</button></div>
      </div>
    </div>

    <!-- minutes summary note of the AI seat OFFICE-DRAFT -->
    <AiNotesBox v-if="meeting && meeting.id" :target-id="meeting.id" target-type="OfficeMeeting" />

    <div v-if="cancelling" class="inv-modal-backdrop screen-only" @click.self="cancelling = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">لغو جلسه</h2>
        <label class="form-label small mb-0">علت لغو (الزامی)</label>
        <textarea v-model="cancelling.reason" class="form-control form-control-sm" rows="3"></textarea>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="cancelling = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-danger" @click="run('cancel', { reason: cancelling.reason })">لغو جلسه</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
