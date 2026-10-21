<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import DocAttach from "@/components/quality/DocAttach.vue";
import { auditAction, getAudit, saveAuditFinding } from "@/api/QualityAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { auditStatuses, capaStatuses, dateInput, findingTypes } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// An audit: findings (a nonconformance drafts a CAPA automatically), the report document, done and close.
const route = useRoute();
const audit = ref<any>(null);
const finding = ref<any>(null);
const reportDoc = ref<string | null>(null);
const busy = ref(false);
const canWrite = hasRole(QualityRoles.audit);

async function load() {
  const [data, error] = await getAudit(Number(route.params.id));
  if (error) return fail(error);
  audit.value = data;
  reportDoc.value = data.reportDocumentId || null;
}

async function saveFinding() {
  busy.value = true;
  const f = finding.value;
  const [, error] = await saveAuditFinding({ auditId: audit.value.id, id: f.id, clause: f.clause, type: f.type, evidence: f.evidence, dueDate: f.dueDate || null, capaId: f.capaId || null });
  busy.value = false;
  if (error) return fail(error);
  finding.value = null;
  ok("یافته ثبت شد.");
  load();
}

async function act(action: string) {
  busy.value = true;
  const [data, error] = await auditAction(audit.value.id, action, reportDoc.value);
  busy.value = false;
  if (error) return fail(error);
  audit.value = data;
  ok("انجام شد.");
}

onMounted(load);
</script>

<template>
  <div v-if="audit" class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">ممیزی <span class="inv-ltr">{{ audit.number }}</span> — {{ audit.auditedUnit }}</h1>
          <span :class="{ ok: audit.status === 2, info: audit.status === 1, warn: audit.status === 0 }" class="inv-badge">{{ auditStatuses[audit.status] }}</span>
        </div>
        <div class="d-flex gap-2">
          <router-link :to="{ name: 'QualityAudits' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button v-if="canWrite && audit.status === 0" :disabled="busy" class="btn btn-sm btn-primary" @click="act('done')">ممیزی انجام شد</button>
          <button v-if="canWrite && audit.status === 1" :disabled="busy" class="btn btn-sm btn-success" @click="act('close')">بستن ممیزی</button>
        </div>
      </div>
      <div class="row g-2 small">
        <div class="col-md-3">معیار: <b class="inv-ltr">{{ audit.criteria }}</b></div>
        <div class="col-md-3">تاریخ برنامه: {{ jalali(audit.plannedDate) }}</div>
        <div class="col-md-3">ممیز ارشد: {{ audit.leadAuditorName }}</div>
        <div class="col-md-3">تیم: {{ (audit.teamNames || []).join("، ") || "—" }}</div>
        <div class="col-md-6"><label class="form-label small mb-0">گزارش ممیزی (فایل)</label><DocAttach v-model="reportDoc" title="گزارش ممیزی" :disabled="!canWrite || audit.status === 2" /></div>
      </div>
    </div>

    <div class="inv-card">
      <div class="inv-toolbar">
        <h2 class="inv-title">یافته‌ها</h2>
        <button v-if="canWrite && audit.status !== 2" class="btn btn-sm btn-primary" @click="finding = { id: null, clause: '', type: 2, evidence: '', dueDate: '', capaId: null }">یافته جدید</button>
      </div>
      <div v-if="finding" class="border rounded p-2 mb-3">
        <div class="row g-2">
          <div class="col-md-2"><label class="form-label small mb-0">بند استاندارد</label><input v-model="finding.clause" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">نوع</label><select v-model="finding.type" class="form-select form-select-sm"><option v-for="(t, k) in findingTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">مهلت اقدام</label><input v-model="finding.dueDate" class="form-control form-control-sm" type="date" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">شناسه CAPA موجود (اختیاری)</label><input v-model.number="finding.capaId" class="form-control form-control-sm" type="number" /></div>
          <div class="col-12"><label class="form-label small mb-0">شواهد *</label><textarea v-model="finding.evidence" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-12 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveFinding">ثبت</button><button class="btn btn-sm btn-outline-secondary" @click="finding = null">انصراف</button></div>
        </div>
        <p class="inv-muted small mt-2 mb-0">برای عدم انطباق، اگر CAPA مشخص نشود سیستم پیش‌نویس آن را می‌سازد.</p>
      </div>
      <table class="table table-sm">
        <thead><tr><th>بند</th><th>نوع</th><th>شواهد</th><th>مهلت</th><th>CAPA</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!audit.findings.length"><td colspan="6" class="text-center inv-muted">یافته‌ای ثبت نشده.</td></tr>
          <tr v-for="f in audit.findings" :key="f.id">
            <td class="inv-ltr">{{ f.clause }}</td>
            <td><span :class="f.type <= 2 ? 'bad' : 'info'" class="inv-badge">{{ findingTypes[f.type] }}</span></td>
            <td class="small">{{ f.evidence }}</td><td>{{ jalali(f.dueDate) }}</td>
            <td><router-link v-if="f.capaId" :to="{ name: 'QualityCapaEdit', params: { id: f.capaId } }" class="inv-ltr">{{ f.capaNumber }}</router-link><span v-if="f.capaStatus != null" class="inv-muted small ms-1">{{ capaStatuses[f.capaStatus] }}</span></td>
            <td><button v-if="canWrite && audit.status !== 2" class="btn btn-sm btn-outline-secondary" @click="finding = { ...f, dueDate: dateInput(f.dueDate) }">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
