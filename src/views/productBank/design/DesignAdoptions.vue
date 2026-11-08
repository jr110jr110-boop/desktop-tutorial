<script setup lang="ts">
import { onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import { designAdoptionAction, getDesignAdoptions, requestDesignAdoption } from "@/api/DesignControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok, printPage } from "@/composables/inventory";

// Package A1 (fix round 1): adoption of an EXISTING design. A BOM revision released before the design control started is not blocked afterwards, but it is shown as "قدیمی، بدون تایید طراحی"
// until two people accept its design: ENGINEERING asks (reason and evidence), QA, a different person, accepts. A BOM that changed after the request needs a new request.
const rows = ref<any[]>([]);
const text = ref("");
const onlyLegacy = ref(true);
const busy = ref(false);
const request = ref<null | { bomHeaderId: number; title: string; reason: string; evidenceRef: string }>(null);
const dialog = ref<null | { id: number; action: string; title: string; rowVersion: string | null }>(null);

const statusText: Record<number, string> = { 0: "درخواست‌شده", 1: "پذیرفته شده", 2: "باطل" };
const statusClass: Record<number, string> = { 0: "warn", 1: "ok", 2: "bad" };

async function load() {
  const [data, error] = await getDesignAdoptions({ OnlyLegacy: onlyLegacy.value, Contains: text.value });
  if (error) return fail(error);
  rows.value = rowsOf(data);
}
async function send() {
  const r = request.value!;
  busy.value = true;
  const [, error] = await requestDesignAdoption({ bomHeaderId: r.bomHeaderId, reason: r.reason, evidenceRef: r.evidenceRef });
  busy.value = false;
  if (error) return fail(error);
  request.value = null;
  ok("درخواست پذیرش طراحی موجود ثبت شد؛ QA (نفر دوم) باید آن را بپذیرد.");
  load();
}
async function act(note: string) {
  const d = dialog.value!;
  busy.value = true;
  const [, error] = await designAdoptionAction({ id: d.id, action: d.action, note, rowVersion: d.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  dialog.value = null;
  ok("انجام شد.");
  load();
}
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="پذیرش طراحی موجود" subtitle="BOM های منتشرشده پیش از آغاز کنترل طراحی" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">پذیرش طراحی موجود (BOM های قدیمی)</h1>
        <div class="d-flex gap-2 d-print-none"><button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button></div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <input v-model="text" class="form-control form-control-sm" placeholder="کد BOM یا محصول" @keyup.enter="load" />
        <label class="small text-nowrap"><input v-model="onlyLegacy" type="checkbox" @change="load" /> فقط BOM های قدیمی</label>
        <button class="btn btn-sm btn-outline-secondary" @click="load">نمایش</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>BOM</th><th>بازنگری</th><th>محصول</th><th>انتشار</th><th>وضعیت پذیرش</th><th>دلیل و مدرک</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="7" class="text-center inv-muted">BOM قدیمی‌ای وجود ندارد.</td></tr>
          <tr v-for="r in rows" :key="r.bomHeaderId">
            <td class="inv-ltr">{{ r.bomCode }}</td>
            <td class="inv-ltr">{{ String(r.bomRevision).padStart(2, "0") }}</td>
            <td class="inv-ltr">{{ r.productCode }}</td>
            <td>{{ jalali(r.releasedAt) }}</td>
            <td>
              <span v-if="r.status === null || r.status === undefined" class="inv-badge warn">قدیمی، بدون تایید طراحی</span>
              <span v-else class="inv-badge" :class="statusClass[r.status]">{{ statusText[r.status] }}</span>
              <div v-if="r.acceptedByName" class="small">پذیرنده: {{ r.acceptedByName }} {{ jalali(r.acceptedAt) }}</div>
              <div v-if="r.voidReason" class="small">{{ r.voidReason }}</div>
            </td>
            <td class="small"><div v-if="r.reason">{{ r.reason }}</div><div v-if="r.evidenceRef" class="inv-muted">مدرک: {{ r.evidenceRef }}</div><div v-if="r.requestedByName" class="inv-muted">درخواست: {{ r.requestedByName }}</div></td>
            <td class="d-print-none text-nowrap">
              <button v-if="r.canRequest" class="btn btn-sm btn-outline-primary py-0" @click="request = { bomHeaderId: r.bomHeaderId, title: r.bomCode, reason: '', evidenceRef: '' }">درخواست پذیرش</button>
              <button v-if="r.canAccept" class="btn btn-sm btn-outline-success py-0" @click="dialog = { id: r.adoptionId, action: 'accept', title: 'پذیرش طراحی موجود (نفر دوم)', rowVersion: r.rowVersion }">پذیرش</button>
              <button v-if="r.canVoid" class="btn btn-sm btn-outline-danger py-0 ms-1" @click="dialog = { id: r.adoptionId, action: 'void', title: 'ابطال پذیرش', rowVersion: r.rowVersion }">ابطال</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="request" class="inv-modal-backdrop d-print-none" @click.self="request = null">
      <div class="inv-modal" style="max-width: 600px" role="dialog" aria-modal="true">
        <h2 class="inv-title mb-2">درخواست پذیرش طراحی موجود: {{ request.title }}</h2>
        <label class="form-label small mb-0">دلیل پذیرش *</label>
        <textarea v-model="request.reason" class="form-control form-control-sm mb-2" rows="3" maxlength="1000"></textarea>
        <label class="form-label small mb-0">مرجع مدرک * (گزارش آزمون، سابقه بهره‌برداری، تایید مشتری)</label>
        <input v-model="request.evidenceRef" class="form-control form-control-sm" maxlength="500" />
        <div class="d-flex gap-2 mt-3">
          <button :disabled="busy || !request.reason.trim() || !request.evidenceRef.trim()" class="btn btn-sm btn-primary" @click="send">ثبت درخواست</button>
          <button class="btn btn-sm btn-outline-secondary" @click="request = null">انصراف</button>
        </div>
      </div>
    </div>
    <ReasonDialog v-if="dialog" :title="dialog.title" :busy="busy" :required="dialog.action === 'void'" @confirm="act" @close="dialog = null" />
  </div>
</template>

<style lang="scss" src="../../inventory/inventory.scss"></style>
