<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import PositionSelect from "@/components/office/wf/PositionSelect.vue";
import { rowsOf } from "@/components/office/wf/lookups";
import QPager from "@/components/quality/QPager.vue";
import { uploadFile } from "@/api/InventoryAPIs";
import { checkOverdueDecisions, decisionAction, downloadDocument, getDecisions, getMeetings } from "@/api/OfficeAPIs";
import { camelize, usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { badgeOfDecision, can, dateText, decisionStatuses } from "@/composables/office";
import { OfficeRoles, hasRole } from "@/composables/roles";

// Decisions (مصوبات) of meetings with approved minutes. Done: the owner position or its deputy; cancel: chair, creator or CEO (the API decides).
const route = useRoute();
const filters = ref({ Status: null as number | null, OverdueOnly: false, MineOnly: false, MeetingId: route.query.meetingId ? Number(route.query.meetingId) : (null as number | null), OwnerPositionId: null as number | null, Contains: "" });
const list = usePagedList(getDecisions, filters);
const meetings = ref<any[]>([]);
const canCheck = hasRole(OfficeRoles.overdueCheck);
const act = ref<null | { d: any; kind: "done" | "cancel"; note: string; file: File | null }>(null);
const checkResult = ref<any>(null);
const busy = ref(false);

async function submit() {
  const a = act.value!;
  busy.value = true;
  let evidence: string | null = null;
  if (a.kind === "done" && a.file) {
    const [id, upError] = await uploadFile(a.file, "مستند اجرای مصوبه");
    if (upError || !id) {
      busy.value = false;
      return fail(upError || { message: "بارگذاری مستند انجام نشد." });
    }
    evidence = id;
  }
  const [, error] = await decisionAction({ id: a.d.id, action: a.kind, note: a.note, evidenceDocumentId: evidence });
  busy.value = false;
  if (error) return fail(error);
  ok(a.kind === "done" ? "مصوبه انجام‌شده ثبت شد." : "مصوبه لغو شد.");
  act.value = null;
  list.load();
}
async function evidence(d: any) {
  const error = await downloadDocument(d.evidenceDocumentId, "مستند اجرای مصوبه");
  if (error) fail(error);
}
async function check() {
  busy.value = true;
  const [d, error] = await checkOverdueDecisions();
  busy.value = false;
  if (error) return fail(error);
  checkResult.value = d;
  list.load();
}
onMounted(async () => {
  const [d] = await getMeetings({ PageNumber: 1, PageSize: 500 });
  meetings.value = rowsOf(camelize(d));
  list.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">پیگیری مصوبات</h1>
        <button v-if="canCheck" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="check">بررسی سررسیدها</button>
      </div>
      <div v-if="checkResult" class="alert alert-info py-1 small d-flex justify-content-between">
        <span>مصوبات معوق: <b>{{ checkResult.overdueDecisions }}</b> - هشدارهای تازه برای روسای جلسه: <b>{{ checkResult.alertsCreated }}</b></span>
        <button class="btn-close" @click="checkResult = null"></button>
      </div>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="جستجو در متن مصوبه" @keyup.enter="list.search" />
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in decisionStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.MeetingId" class="form-select form-select-sm"><option :value="null">همه جلسات</option><option v-for="m in meetings" :key="m.id" :value="m.id">{{ m.number }} - {{ m.title }}</option></select>
        <PositionSelect v-model="filters.OwnerPositionId" :active-only="false" placeholder="همه سمت‌های مسئول" />
        <label class="small d-flex align-items-center gap-1"><input v-model="filters.OverdueOnly" type="checkbox" class="form-check-input mt-0" />فقط معوق</label>
        <label class="small d-flex align-items-center gap-1"><input v-model="filters.MineOnly" type="checkbox" class="form-check-input mt-0" />فقط مصوبات من</label>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>جلسه</th><th>ردیف</th><th>مصوبه</th><th>مسئول اجرا</th><th>مهلت</th><th>وضعیت</th><th>توضیح اجرا</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="8" class="text-center inv-muted">مصوبه‌ای نیست.</td></tr>
            <tr v-for="d in list.rows.value" :key="d.id">
              <td><router-link :to="{ name: 'OfMeeting', params: { id: d.meetingId } }">{{ d.meetingNumber }}</router-link><div class="small inv-muted">{{ d.meetingTitle }}</div></td>
              <td>{{ d.seq }}</td><td style="white-space: pre-wrap">{{ d.text }}</td><td>{{ d.ownerPositionTitle }}</td>
              <td class="text-nowrap"><span class="inv-ltr">{{ d.dueDateText }}</span> <span v-if="d.overdue" class="inv-badge bad">معوق</span></td>
              <td><span :class="badgeOfDecision(d.status)" class="inv-badge">{{ decisionStatuses[d.status] }}</span></td>
              <td class="small">{{ d.doneNote }}<div v-if="d.doneByName" class="inv-muted">{{ d.doneByName }} - <span class="inv-ltr">{{ dateText(d.doneAt) }}</span></div><button v-if="d.evidenceDocumentId" class="btn btn-sm btn-link p-0" @click="evidence(d)">دریافت مستند</button></td>
              <td class="text-nowrap">
                <button v-if="can(d, 'done')" class="btn btn-sm btn-success me-1" @click="act = { d, kind: 'done', note: '', file: null }">انجام شد</button>
                <button v-if="can(d, 'cancel')" class="btn btn-sm btn-outline-danger" @click="act = { d, kind: 'cancel', note: '', file: null }">لغو</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <div v-if="act" class="inv-modal-backdrop" @click.self="act = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ act.kind === "done" ? "ثبت انجام مصوبه" : "لغو مصوبه" }}</h2>
        <p class="small">{{ act.d.text }}</p>
        <label class="form-label small mb-0">{{ act.kind === "done" ? "شرح اقدام انجام‌شده" : "علت لغو" }}</label>
        <textarea v-model="act.note" class="form-control form-control-sm" rows="3"></textarea>
        <div v-if="act.kind === 'done'" class="mt-2">
          <label class="form-label small mb-0">مستند اجرا (اختیاری)</label>
          <input type="file" class="form-control form-control-sm" @change="act.file = ($event.target as HTMLInputElement).files?.[0] || null" />
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="act = null">انصراف</button><button :disabled="busy" :class="act.kind === 'done' ? 'btn-success' : 'btn-danger'" class="btn btn-sm" @click="submit">{{ act.kind === "done" ? "ثبت انجام" : "لغو مصوبه" }}</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
