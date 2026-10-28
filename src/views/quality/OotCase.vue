<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import { assessOot, closeOot, getOotCase, linkOotReinspection } from "@/api/QualityAPIs";
import ReinspectionPicker from "@/components/quality/ReinspectionPicker.vue";
import { fail, jalali, ok } from "@/composables/inventory";
import { ootAssessments, ootStatuses, reinspectionKinds, type ReinspectionValue } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Out-of-tolerance case: every inspection record / test report / incoming inspection made with the instrument since its last good calibration needs an assessment.
// "Re-inspect" is a task, not a tick (R1q D40): the accepted new inspection has to be linked to the row, and the case cannot be closed before that.
const route = useRoute();
const c = ref<any>(null);
const edits = ref<Record<number, { assessment: number; note: string; ncrId: number | null }>>({});
const links = ref<Record<number, ReinspectionValue>>({});
const closeNote = ref("");
const busy = ref(false);
const canManage = hasRole(QualityRoles.managers);

const REINSPECT = 2;
const reinspectionOptions = ["QcRecord", "TestReport", "QcInspection"];
const sourceKinds: Record<string, string> = { QcRecord: "سابقه بازرسی", TestReport: "گزارش آزمون", Inspection: "بازرسی ورودی" };

/** `changedId`: only that row came back changed from the server, so the unsaved drafts of the other rows stay. */
function load(dto: any, changedId?: number) {
  const nextEdits: Record<number, { assessment: number; note: string; ncrId: number | null }> = {};
  const nextLinks: Record<number, ReinspectionValue> = {};
  dto.items.forEach((i: any) => {
    const keep = changedId !== undefined && i.id !== changedId;
    nextEdits[i.id] = keep && edits.value[i.id] ? edits.value[i.id] : { assessment: i.assessment === 0 ? 1 : i.assessment, note: i.assessmentNote || "", ncrId: i.ncrId };
    nextLinks[i.id] = keep && links.value[i.id] ? links.value[i.id] : { kind: i.reinspectionKind || null, recordId: i.reinspectionRecordId || null };
  });
  c.value = dto;
  edits.value = nextEdits;
  links.value = nextLinks;
}

async function assess(i: any) {
  const e = edits.value[i.id];
  if (e.assessment === 0) return fail("نوع ارزیابی را انتخاب کنید.");
  busy.value = true;
  const [data, error] = await assessOot({ itemId: i.id, assessment: e.assessment, note: e.note, ncrId: e.ncrId || null });
  busy.value = false;
  if (error) return fail(error);
  load(data, i.id);
  ok(e.assessment === REINSPECT ? "ارزیابی ثبت شد؛ بازرسی مجدد را انجام دهید و سند قبول‌شده را به همین ردیف پیوند کنید." : "ارزیابی ثبت شد.");
}

async function linkReinspection(i: any) {
  const v = links.value[i.id];
  if (!v || !v.kind || !v.recordId) return fail("نوع و سند بازرسی مجدد قبول‌شده را انتخاب کنید.");
  busy.value = true;
  const [data, error] = await linkOotReinspection({ itemId: i.id, kind: v.kind, recordId: v.recordId });
  busy.value = false;
  if (error) return fail(error);
  load(data, i.id);
  ok("بازرسی مجدد به ردیف پیوند شد.");
}

async function close() {
  busy.value = true;
  const [data, error] = await closeOot(c.value.id, closeNote.value);
  busy.value = false;
  if (error) return fail(error);
  load(data);
  ok("پرونده بسته شد.");
}

// an incoming inspection (sourceKind "Inspection") has no page of its own; records and reports do
function link(i: any) {
  if (i.sourceKind === "TestReport") return { name: "QualityReportEdit", params: { id: i.sourceId } };
  if (i.sourceKind === "QcRecord") return { name: "QualityRecordEdit", params: { id: i.sourceId } };
  return null;
}

function reinspectionLink(i: any) {
  if (i.reinspectionKind === "TestReport") return { name: "QualityReportEdit", params: { id: i.reinspectionRecordId } };
  if (i.reinspectionKind === "QcRecord") return { name: "QualityRecordEdit", params: { id: i.reinspectionRecordId } };
  return null;
}

// rows whose assessment is "re-inspect" but no accepted re-inspection is linked yet: the case cannot be closed with them
const waitingReinspection = computed(() => (c.value ? c.value.items.filter((i: any) => i.assessment === REINSPECT && !i.reinspectionRecordId).length : 0));
const canClose = computed(() => !!c.value && c.value.pendingCount === 0 && waitingReinspection.value === 0);

onMounted(async () => {
  const [data, error] = await getOotCase(Number(route.params.id));
  if (error) return fail(error);
  load(data);
});
</script>

<template>
  <div v-if="c" class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">پرونده خارج از تلرانس <span class="inv-ltr ms-2">{{ c.number }}</span></h1>
          <span :class="c.status === 0 ? 'bad' : 'ok'" class="inv-badge">{{ ootStatuses[c.status] }}</span>
          <span class="inv-muted ms-2">تجهیز <span class="inv-ltr">{{ c.equipmentCode }}</span> — از {{ jalali(c.fromDate) || "ابتدا" }} تا {{ jalali(c.toDate) }}</span>
        </div>
        <router-link :to="{ name: 'QualityCalibration' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
      </div>
      <table class="table table-sm">
        <thead><tr><th>نوع</th><th>سابقه</th><th>تاریخ</th><th>ارزیابی</th><th>توضیح</th><th>NCR</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!c.items.length"><td colspan="7" class="text-center inv-muted">سابقه‌ای با این تجهیز ثبت نشده است.</td></tr>
          <template v-for="i in c.items" :key="i.id">
            <tr>
              <td>{{ sourceKinds[i.sourceKind] || i.sourceKind }}</td>
              <td>
                <router-link v-if="link(i)" :to="link(i)" class="inv-ltr">{{ i.sourceNumber }}</router-link>
                <span v-else class="inv-ltr">{{ i.sourceNumber }}</span>
              </td>
              <td>{{ jalali(i.sourceDate) }}</td>
              <template v-if="c.status === 0 && canManage">
                <td><select v-model="edits[i.id].assessment" class="form-select form-select-sm"><option v-for="(t, k) in ootAssessments" :key="k" :value="Number(k)">{{ t }}</option></select></td>
                <td><input v-model="edits[i.id].note" class="form-control form-control-sm" /></td>
                <td>
                  <input v-if="edits[i.id].assessment === 3" v-model.number="edits[i.id].ncrId" class="form-control form-control-sm" placeholder="شناسه NCR (خالی = پیش‌نویس سیستم)" type="number" />
                  <router-link v-else-if="i.ncrId" :to="{ name: 'InventoryNcrEdit', params: { id: i.ncrId } }" class="inv-ltr">{{ i.ncrNumber }}</router-link>
                </td>
                <td><button :disabled="busy" class="btn btn-sm btn-primary" @click="assess(i)">ثبت</button></td>
              </template>
              <template v-else>
                <td><span :class="i.assessment === 0 ? 'warn' : i.assessment === 3 ? 'bad' : 'ok'" class="inv-badge">{{ ootAssessments[i.assessment] }}</span></td>
                <td>{{ i.assessmentNote }}</td>
                <td><router-link v-if="i.ncrId" :to="{ name: 'InventoryNcrEdit', params: { id: i.ncrId } }" class="inv-ltr">{{ i.ncrNumber }}</router-link></td>
                <td></td>
              </template>
            </tr>
            <!-- the draft says "re-inspect" but it is not saved yet -->
            <tr v-if="c.status === 0 && canManage && edits[i.id] && edits[i.id].assessment === REINSPECT && i.assessment !== REINSPECT">
              <td colspan="7" class="small inv-muted">پس از ثبت ارزیابی «بازرسی مجدد»، همین‌جا سند بازرسی مجدد قبول‌شده را به این ردیف پیوند کنید؛ تا آن زمان پرونده بسته نمی‌شود.</td>
            </tr>
            <tr v-else-if="c.status === 0 && canManage && i.assessment === REINSPECT && i.reinspectionRecordId && edits[i.id] && edits[i.id].assessment !== REINSPECT">
              <td colspan="7" class="small text-danger">با تغییر این ارزیابی، پیوند بازرسی مجدد این ردیف پاک می‌شود.</td>
            </tr>
            <!-- re-inspection of a row assessed "re-inspect" (R1q D40) -->
            <tr v-if="i.assessment === REINSPECT">
              <td colspan="7" class="bg-light">
                <div class="d-flex flex-wrap gap-2 align-items-center small">
                  <strong>بازرسی مجدد:</strong>
                  <template v-if="i.reinspectionRecordId">
                    <span class="inv-badge ok">پیوند شده</span>
                    <span>{{ reinspectionKinds[i.reinspectionKind] || i.reinspectionKind }}</span>
                    <router-link v-if="reinspectionLink(i)" :to="reinspectionLink(i)" class="inv-ltr">{{ i.reinspectionNumber || i.reinspectionRecordId }}</router-link>
                    <span v-else class="inv-ltr">{{ i.reinspectionNumber || i.reinspectionRecordId }}</span>
                  </template>
                  <template v-else>
                    <span class="inv-badge warn">هنوز پیوند نشده</span>
                    <span class="inv-muted">بازرسی مجدد کالای همین سند را با نتیجه «قبول» انجام دهید (پس از کالیبراسیونی که تجهیز را خارج از تلرانس نشان داد) و آن را اینجا پیوند کنید.</span>
                  </template>
                </div>
                <div v-if="c.status === 0 && canManage && links[i.id]" class="mt-2">
                  <ReinspectionPicker v-model="links[i.id]" :kinds="reinspectionOptions" :disabled="busy" />
                  <button :disabled="busy || !links[i.id].kind || !links[i.id].recordId" class="btn btn-sm btn-outline-primary mt-2" @click="linkReinspection(i)">
                    {{ i.reinspectionRecordId ? "تغییر سند بازرسی مجدد" : "پیوند بازرسی مجدد" }}
                  </button>
                </div>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
      <div v-if="c.status === 0 && canManage" class="d-flex gap-2 align-items-end">
        <div class="flex-grow-1"><label class="form-label small mb-0">توضیح بستن پرونده</label><input v-model="closeNote" class="form-control form-control-sm" /></div>
        <button :disabled="busy || !canClose" class="btn btn-sm btn-success" @click="close">بستن پرونده</button>
      </div>
      <p v-if="c.status === 0" class="inv-muted mt-2 mb-0">
        پرونده تا ارزیابی همه سوابق ({{ c.pendingCount }} مورد باقی‌مانده) و پیوند بازرسی مجدد قبول‌شده برای هر ردیف با ارزیابی «بازرسی مجدد» ({{ waitingReinspection }} مورد باقی‌مانده) بسته نمی‌شود.
      </p>
      <p v-if="c.note" class="small mt-2 mb-0">توضیح: {{ c.note }}</p>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
