<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import { assessOot, closeOot, getOotCase } from "@/api/QualityAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { ootAssessments, ootStatuses } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Out-of-tolerance case: every inspection record / test report made with the instrument since its last good calibration needs an assessment.
const route = useRoute();
const c = ref<any>(null);
const edits = ref<Record<number, { assessment: number; note: string; ncrId: number | null }>>({});
const closeNote = ref("");
const busy = ref(false);
const canManage = hasRole(QualityRoles.managers);

function load(dto: any) {
  c.value = dto;
  edits.value = {};
  dto.items.forEach((i: any) => (edits.value[i.id] = { assessment: i.assessment === 0 ? 1 : i.assessment, note: i.assessmentNote || "", ncrId: i.ncrId }));
}

async function assess(i: any) {
  const e = edits.value[i.id];
  if (e.assessment === 0) return fail("نوع ارزیابی را انتخاب کنید.");
  busy.value = true;
  const [data, error] = await assessOot({ itemId: i.id, assessment: e.assessment, note: e.note, ncrId: e.ncrId || null });
  busy.value = false;
  if (error) return fail(error);
  load(data);
  ok("ارزیابی ثبت شد.");
}

async function close() {
  busy.value = true;
  const [data, error] = await closeOot(c.value.id, closeNote.value);
  busy.value = false;
  if (error) return fail(error);
  load(data);
  ok("پرونده بسته شد.");
}

const link = (i: any) => (i.sourceKind === "TestReport" ? { name: "QualityReportEdit", params: { id: i.sourceId } } : { name: "QualityRecordEdit", params: { id: i.sourceId } });

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
          <tr v-for="i in c.items" :key="i.id">
            <td>{{ i.sourceKind === "TestReport" ? "گزارش آزمون" : "سابقه بازرسی" }}</td>
            <td><router-link :to="link(i)" class="inv-ltr">{{ i.sourceNumber }}</router-link></td>
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
        </tbody>
      </table>
      <div v-if="c.status === 0 && canManage" class="d-flex gap-2 align-items-end">
        <div class="flex-grow-1"><label class="form-label small mb-0">توضیح بستن پرونده</label><input v-model="closeNote" class="form-control form-control-sm" /></div>
        <button :disabled="busy || c.pendingCount > 0" class="btn btn-sm btn-success" @click="close">بستن پرونده</button>
      </div>
      <p v-if="c.status === 0" class="inv-muted mt-2 mb-0">پرونده تا ارزیابی همه سوابق ({{ c.pendingCount }} مورد باقی‌مانده) بسته نمی‌شود.</p>
      <p v-if="c.note" class="small mt-2 mb-0">توضیح: {{ c.note }}</p>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
