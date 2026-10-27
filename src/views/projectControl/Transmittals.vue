<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import JDate from "@/components/pc/JDate.vue";
import { getDeliverables, getResponseCodes, getTransmittal, getTransmittals, recordTransmittalResponse, saveTransmittal } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok, today } from "@/composables/inventory";
import TransmittalPrint from "@/components/pc/print/TransmittalPrint.vue";
import { contractLabel, pcContractId, transmittalDirections, transmittalPurposes, usePcPrint } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Transmittals of MDL documents to the client (and incoming ones) with the client's response code for every line.
const { job, run } = usePcPrint();
const rows = ref<any[]>([]);
const onlyOverdue = ref(false);
const direction = ref<number | null>(null);
const detail = ref<any>(null);
const form = ref<any>(null);
const docs = ref<any[]>([]);
const codes = ref<any[]>([]);
const resp = ref<any>(null);
const busy = ref(false);
const can = hasRole(ProjectControlRoles.dccOnly);

async function load() {
  if (!pcContractId.value) return;
  const [d, e] = await getTransmittals({ ContractId: pcContractId.value, Direction: direction.value, OnlyOverdue: onlyOverdue.value || null });
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
async function openNew() {
  const [[d], [c]] = await Promise.all([getDeliverables({ ContractId: pcContractId.value, ClientOnly: true }), getResponseCodes()]);
  docs.value = rowsOf(d);
  codes.value = Array.isArray(c) ? c : [];
  form.value = { contractId: pcContractId.value, direction: 1, transmittalDate: today(), reference: "", note: "", picked: {} as Record<number, any> };
}
const pickedLines = computed(() => (form.value ? docs.value.filter((d) => form.value.picked[d.id]).map((d) => ({ deliverableId: d.id, revision: form.value.picked[d.id].revision || d.revision || "A", purpose: form.value.picked[d.id].purpose || 1 })) : []));
function toggle(d: any, on: boolean) {
  if (on) form.value.picked[d.id] = { revision: d.revision || "A", purpose: 1 };
  else delete form.value.picked[d.id];
}
async function save() {
  busy.value = true;
  const f = form.value;
  const [t, e] = await saveTransmittal({ contractId: f.contractId, direction: f.direction, transmittalDate: f.transmittalDate, reference: f.reference, note: f.note, lines: pickedLines.value });
  busy.value = false;
  if (e) return fail(e);
  ok("ترنسمیتال " + t.number + " ثبت شد.");
  form.value = null;
  load();
}
async function show(id: number) {
  const [[t, e], [c]] = await Promise.all([getTransmittal(id), getResponseCodes()]);
  if (e) return fail(e);
  detail.value = t;
  codes.value = Array.isArray(c) ? c : [];
}
async function record() {
  busy.value = true;
  const [, e] = await recordTransmittalResponse(resp.value);
  busy.value = false;
  if (e) return fail(e);
  ok("پاسخ ثبت شد.");
  const id = detail.value.id;
  resp.value = null;
  show(id);
  load();
}
// the link /project-control/transmittals?id=N (an inbox item: the client's answer is due) opens that transmittal, whatever project is chosen
const route = useRoute();
function openFromLink() {
  const id = Number(route.query.id);
  if (Number.isInteger(id) && id > 0) show(id);
}
onMounted(() => { load(); openFromLink(); });
watch(() => route.query.id, openFromLink);
watch(pcContractId, load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="ترنسمیتال مدارک" :subtitle="contractLabel(pcContractId)" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">ترنسمیتال مدارک</h1>
        <div class="d-flex flex-wrap gap-2 d-print-none"><ContractSelect /></div>
      </div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <div class="inv-filters mb-2 d-print-none">
          <select v-model="direction" class="form-select form-select-sm" @change="load"><option :value="null">همه</option><option v-for="(n, k) in transmittalDirections" :key="k" :value="Number(k)">{{ n }}</option></select>
          <label class="form-check mb-1"><input v-model="onlyOverdue" class="form-check-input" type="checkbox" @change="load" /> فقط پاسخ‌های از مهلت گذشته</label>
          <button v-if="can" class="btn btn-sm btn-primary" @click="openNew">ترنسمیتال جدید</button>
        </div>
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>جهت</th><th>تاریخ</th><th>مهلت پاسخ</th><th>مرجع</th><th>یادداشت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="7" class="text-center inv-muted">ترنسمیتالی نیست.</td></tr>
            <tr v-for="r in rows" :key="r.id" role="button" @click="show(r.id)">
              <td class="inv-ltr">{{ r.number }}</td><td>{{ transmittalDirections[r.direction] }}</td><td>{{ jalali(r.transmittalDate) }}</td>
              <td>{{ jalali(r.responseDue) }} <span v-if="r.overdue" class="inv-badge bad">از مهلت گذشته</span></td><td>{{ r.reference }}</td><td>{{ r.note }}</td><td class="text-end">باز کردن</td>
            </tr>
          </tbody>
        </table>
      </template>
    </div>

    <TransmittalPrint v-if="job?.kind === 'transmittal'" :t="job.data" />
    <div v-if="detail" class="inv-modal-backdrop" @click.self="detail = null">
      <div class="inv-modal" style="max-width: 900px; width: 96%">
        <h3 class="h6">ترنسمیتال <span class="inv-ltr">{{ detail.number }}</span> — {{ transmittalDirections[detail.direction] }} — {{ jalali(detail.transmittalDate) }}</h3>
        <table class="table table-sm">
          <thead><tr><th>سند</th><th>عنوان</th><th>ویرایش</th><th>منظور</th><th>پاسخ کارفرما</th><th></th></tr></thead>
          <tbody>
            <tr v-for="l in detail.lines" :key="l.id">
              <td class="inv-ltr">{{ l.documentNumber }}</td><td>{{ l.title }}</td><td class="inv-ltr">{{ l.revision }}</td><td>{{ transmittalPurposes[l.purpose] }}</td>
              <td><b v-if="l.responseCode" class="inv-ltr">{{ l.responseCode }}</b> {{ jalali(l.responseDate) }} <span class="small inv-muted">{{ l.responseNote }}</span></td>
              <td><button v-if="can && detail.direction === 1 && l.purpose === 1 && !l.responseCode" class="btn btn-sm btn-outline-primary" @click="resp = { transmittalLineId: l.id, responseCode: codes[0]?.code, responseDate: today(), note: '' }">ثبت پاسخ</button></td>
            </tr>
          </tbody>
        </table>
        <div class="d-flex justify-content-between"><button class="btn btn-sm btn-outline-secondary" @click="run('transmittal', detail)">چاپ ترنسمیتال</button><button class="btn btn-sm btn-secondary" @click="detail = null">بستن</button></div>
      </div>
    </div>
    <div v-if="resp" class="inv-modal-backdrop" @click.self="resp = null">
      <div class="inv-modal">
        <h3 class="h6">پاسخ کارفرما</h3>
        <label class="form-label small mb-0">کد پاسخ</label>
        <select v-model="resp.responseCode" class="form-select form-select-sm mb-2"><option v-for="c in codes" :key="c.id" :value="c.code">{{ c.code }} — {{ c.title }}</option></select>
        <label class="form-label small mb-0">تاریخ پاسخ</label><JDate v-model="resp.responseDate" class="mb-2" />
        <label class="form-label small mb-0">یادداشت</label><input v-model="resp.note" class="form-control form-control-sm mb-3" />
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="resp = null">انصراف</button><button :disabled="busy || !resp.responseCode" class="btn btn-sm btn-primary" @click="record">ثبت</button></div>
      </div>
    </div>
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal" style="max-width: 900px; width: 96%">
        <h3 class="h6">ترنسمیتال جدید</h3>
        <div class="row g-2 mb-2">
          <div class="col-md-3"><label class="form-label small mb-0">جهت</label><select v-model.number="form.direction" class="form-select form-select-sm"><option v-for="(n, k) in transmittalDirections" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">تاریخ</label><JDate v-model="form.transmittalDate" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">مرجع (نامه)</label><input v-model="form.reference" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">یادداشت</label><input v-model="form.note" class="form-control form-control-sm" /></div>
        </div>
        <div style="max-height: 40vh; overflow: auto">
          <table class="table table-sm">
            <thead><tr><th></th><th>سند</th><th>عنوان</th><th>وضعیت</th><th>ویرایش</th><th>منظور</th></tr></thead>
            <tbody>
              <tr v-for="d in docs" :key="d.id">
                <td><input :checked="!!form.picked[d.id]" type="checkbox" class="form-check-input" @change="toggle(d, ($event.target as HTMLInputElement).checked)" /></td>
                <td class="inv-ltr">{{ d.documentNumber }}</td><td>{{ d.title }}</td><td>{{ d.clientResponseCode || "—" }}</td>
                <td><input v-if="form.picked[d.id]" v-model="form.picked[d.id].revision" class="form-control form-control-sm inv-ltr" style="width: 70px" /></td>
                <td><select v-if="form.picked[d.id]" v-model.number="form.picked[d.id].purpose" class="form-select form-select-sm"><option v-for="(n, k) in transmittalPurposes" :key="k" :value="Number(k)">{{ n }}</option></select></td>
              </tr>
            </tbody>
          </table>
        </div>
        <p class="small inv-muted">مهلت پاسخ از «روزهای بررسی کارفرما» در شرایط قرارداد محاسبه می‌شود. فقط اسناد MDL در ترنسمیتال ارسالی می‌آیند.</p>
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="form = null">انصراف</button><button :disabled="busy || !pickedLines.length" class="btn btn-sm btn-primary" @click="save">ثبت ترنسمیتال ({{ pickedLines.length }} سند)</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
