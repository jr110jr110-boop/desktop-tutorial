<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import WorkOrderSelect from "@/components/quality/WorkOrderSelect.vue";
import { getReleaseChecks, getReleaseNote, releaseNoteAction, saveReleaseNote } from "@/api/QualityAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { releaseNoteStatuses } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// One release note: the checks per work order are computed by the API (ITP points, open NCRs, required reports, data book).
const route = useRoute();
const router = useRouter();
const isNew = route.params.id === "new";
const note = ref<any>(null);
const checks = ref<any[]>([]);
const canRelease = ref(false);
const reason = ref("");
const busy = ref(false);
const canManage = hasRole(QualityRoles.managers);
const isQc = hasRole(QualityRoles.inspectors);
const draft = () => isNew || note.value?.status === 0;

function load(dto: any) {
  note.value = dto;
  checks.value = dto.checks || [];
  canRelease.value = dto.canRelease;
}

async function loadChecks() {
  checks.value = [];
  canRelease.value = false;
  if (!note.value.workOrderId) return;
  const [data, error] = await getReleaseChecks(note.value.workOrderId);
  if (error) return fail(error);
  checks.value = data.checks;
  canRelease.value = data.canRelease;
}

async function save() {
  busy.value = true;
  const [data, error] = await saveReleaseNote({ id: note.value.id || null, workOrderId: note.value.workOrderId, itemsText: note.value.itemsText, note: note.value.note });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  load(data);
  if (isNew) router.replace({ name: "QualityReleaseNoteEdit", params: { id: data.id } });
}

async function act(action: string) {
  if (action === "cancel" && !reason.value.trim()) return fail("دلیل لغو را بنویسید.");
  busy.value = true;
  const [data, error] = await releaseNoteAction(note.value.id, action, reason.value);
  busy.value = false;
  if (error) return fail(error);
  load(data);
  ok(action === "release" ? "برگه ترخیص و گواهی انطباق صادر شد." : "برگه لغو شد.");
}

onMounted(async () => {
  if (isNew) {
    note.value = { id: null, workOrderId: route.query.workOrderId ? Number(route.query.workOrderId) : null, itemsText: "", note: "", status: 0 };
    loadChecks();
    return;
  }
  const [data, error] = await getReleaseNote(Number(route.params.id));
  if (error) return fail(error);
  load(data);
});
</script>

<template>
  <div v-if="note" class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">برگه ترخیص <span v-if="note.number" class="inv-ltr ms-2">{{ note.number }}</span></h1>
          <span :class="{ ok: note.status === 1, bad: note.status === 2, warn: note.status === 0 }" class="inv-badge">{{ releaseNoteStatuses[note.status] }}</span>
          <span v-if="note.certificateOfConformityNumber" class="inv-badge info ms-1">گواهی انطباق <span class="inv-ltr">{{ note.certificateOfConformityNumber }}</span></span>
        </div>
        <div class="d-flex flex-wrap gap-2">
          <router-link :to="{ name: 'QualityReleaseNotes' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <router-link v-if="note.workOrderId" :to="{ name: 'QualityDataBook', params: { workOrderId: note.workOrderId } }" class="btn btn-sm btn-outline-secondary">دیتابوک نهایی</router-link>
          <button v-if="draft() && canManage" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
          <button v-if="!isNew && note.status === 0 && isQc" :disabled="busy || !canRelease" class="btn btn-sm btn-success" @click="act('release')">صدور ترخیص و گواهی انطباق (مدیر QC)</button>
        </div>
      </div>
      <div class="row g-2">
        <div class="col-md-3">
          <label class="form-label small mb-0">دستور ساخت *</label>
          <WorkOrderSelect v-if="isNew" v-model="note.workOrderId" @change="loadChecks" />
          <div v-else class="small pt-1 inv-ltr">{{ note.workOrderCode }}</div>
        </div>
        <div class="col-md-9"><label class="form-label small mb-0">اقلام / سریال‌های ترخیص‌شونده</label><textarea v-model="note.itemsText" :disabled="!draft()" class="form-control form-control-sm" rows="2"></textarea></div>
        <div class="col-md-12"><label class="form-label small mb-0">توضیح</label><input v-model="note.note" :disabled="!draft()" class="form-control form-control-sm" /></div>
      </div>
      <p v-if="note.releasedByName" class="small mt-2 mb-0">صادرکننده: {{ note.releasedByName }} — {{ jalali(note.releasedAt, true) }}</p>
      <p v-if="note.cancelReason" class="small text-danger mt-2 mb-0">دلیل لغو: {{ note.cancelReason }}</p>
    </div>

    <div class="inv-card screen-only">
      <h2 class="inv-title mb-2">کنترل‌های ترخیص (محاسبه‌شده)</h2>
      <p class="inv-muted">{{ note.status === 1 ? "تصویر ثبت‌شده هنگام صدور" : "محاسبه همین لحظه؛ هیچ مورد دستی نیست" }}</p>
      <table class="table table-sm">
        <thead><tr><th style="width: 40px"></th><th>کنترل</th><th>جزئیات</th></tr></thead>
        <tbody>
          <tr v-if="!checks.length"><td colspan="3" class="text-center inv-muted">دستور ساخت را انتخاب کنید.</td></tr>
          <tr v-for="c in checks" :key="c.code">
            <td><span :class="c.passed ? 'ok' : 'bad'" class="inv-badge">{{ c.passed ? "✓" : "✗" }}</span></td>
            <td>{{ c.title }}</td><td class="small">{{ c.detail === "-" ? "" : c.detail }}</td>
          </tr>
        </tbody>
      </table>
      <div v-if="!isNew && note.status === 0 && canManage" class="d-flex gap-2 align-items-end">
        <div class="flex-grow-1"><label class="form-label small mb-0">دلیل لغو</label><input v-model="reason" class="form-control form-control-sm" /></div>
        <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('cancel')">لغو برگه</button>
      </div>
      <p class="inv-muted mt-2 mb-0">ترخیص فقط وقتی همه کنترل‌ها قبول باشد ممکن است؛ تحویل به مشتری هم برگه ترخیص صادرشده می‌خواهد.</p>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
