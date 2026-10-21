<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import UserSelect from "@/components/quality/UserSelect.vue";
import { getAuditPrograms, getAudits, saveAudit, saveAuditProgram } from "@/api/QualityAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { auditStatuses, dateInput, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

const router = useRouter();
const filters = ref({ Status: null as number | null, ProgramId: null as number | null, Contains: "" });
const { rows, total, page, search, go } = usePaged(getAudits, filters);
const programs = ref<any[]>([]);
const program = ref<any>(null);
const audit = ref<any>(null);
const busy = ref(false);
const canWrite = hasRole(QualityRoles.audit);

async function loadPrograms() {
  const [data, error] = await getAuditPrograms();
  if (error) return fail(error);
  programs.value = data || [];
}

async function saveProgramForm() {
  busy.value = true;
  const [, error] = await saveAuditProgram({ ...program.value });
  busy.value = false;
  if (error) return fail(error);
  program.value = null;
  ok("برنامه ذخیره شد.");
  loadPrograms();
}

async function saveAuditForm() {
  busy.value = true;
  const [data, error] = await saveAudit({ ...audit.value, teamUserIds: audit.value.team.filter(Boolean) });
  busy.value = false;
  if (error) return fail(error);
  audit.value = null;
  ok("ممیزی ثبت شد.");
  router.push({ name: "QualityAuditEdit", params: { id: data.id } });
}

onMounted(() => {
  search();
  loadPrograms();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">ممیزی داخلی</h1>
        <div v-if="canWrite" class="d-flex gap-2">
          <button class="btn btn-sm btn-outline-primary" @click="program = { id: null, year: new Date().getFullYear(), scope: '', note: '' }">برنامه سالانه جدید</button>
          <button class="btn btn-sm btn-primary" @click="audit = { id: null, programId: null, auditedUnit: '', criteria: 'ISO 9001:2015', plannedDate: '', leadAuditorUserId: null, team: [null] }">ممیزی جدید</button>
        </div>
      </div>

      <div v-if="program" class="border rounded p-2 mb-3">
        <div class="row g-2 align-items-end">
          <div class="col-md-2"><label class="form-label small mb-0">سال *</label><input v-model.number="program.year" class="form-control form-control-sm" type="number" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">دامنه *</label><input v-model="program.scope" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">توضیح</label><input v-model="program.note" class="form-control form-control-sm" /></div>
          <div class="col-md-2 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveProgramForm">ذخیره</button><button class="btn btn-sm btn-outline-secondary" @click="program = null">انصراف</button></div>
        </div>
      </div>

      <div v-if="audit" class="border rounded p-2 mb-3">
        <div class="row g-2">
          <div class="col-md-3"><label class="form-label small mb-0">برنامه</label><select v-model="audit.programId" class="form-select form-select-sm"><option :value="null">بدون برنامه</option><option v-for="p in programs" :key="p.id" :value="p.id">{{ p.year }} — {{ p.scope }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">واحد ممیزی‌شونده *</label><input v-model="audit.auditedUnit" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">معیار</label><input v-model="audit.criteria" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">تاریخ برنامه *</label><input v-model="audit.plannedDate" class="form-control form-control-sm" type="date" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">ممیز ارشد *</label><UserSelect v-model="audit.leadAuditorUserId" /></div>
          <div class="col-md-5">
            <label class="form-label small mb-0">اعضای تیم</label>
            <div v-for="(u, i) in audit.team" :key="i" class="mb-1"><UserSelect v-model="audit.team[i]" /></div>
            <button class="btn btn-sm btn-link p-0" @click="audit.team.push(null)">+ عضو</button>
          </div>
          <div class="col-md-3 d-flex align-items-end gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveAuditForm">ثبت</button><button class="btn btn-sm btn-outline-secondary" @click="audit = null">انصراف</button></div>
        </div>
        <p class="inv-muted small mt-2 mb-0">ممیز نمی‌تواند واحد خود را ممیزی کند؛ این قاعده در سرویس بررسی می‌شود.</p>
      </div>

      <table v-if="programs.length" class="table table-sm mb-3">
        <thead><tr><th>سال</th><th>دامنه برنامه ممیزی</th><th>تعداد ممیزی</th><th>توضیح</th><th></th></tr></thead>
        <tbody><tr v-for="p in programs" :key="p.id"><td>{{ p.year }}</td><td>{{ p.scope }}</td><td>{{ p.auditCount }}</td><td class="small">{{ p.note }}</td><td><button v-if="canWrite" class="btn btn-sm btn-outline-secondary" @click="program = { ...p }">ویرایش</button></td></tr></tbody>
      </table>

      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا واحد" @keydown.enter="search()" />
        <select v-model="filters.Status" class="form-select form-select-sm" @change="search()"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in auditStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.ProgramId" class="form-select form-select-sm" @change="search()"><option :value="null">همه برنامه‌ها</option><option v-for="p in programs" :key="p.id" :value="p.id">{{ p.year }}</option></select>
        <button class="btn btn-sm btn-secondary" @click="search()">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>واحد</th><th>معیار</th><th>تاریخ برنامه</th><th>ممیز ارشد</th><th>یافته‌ها</th><th>وضعیت</th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="7" class="text-center inv-muted">ممیزی‌ای نیست.</td></tr>
          <tr v-for="a in rows" :key="a.id" role="button" @click="router.push({ name: 'QualityAuditEdit', params: { id: a.id } })">
            <td class="inv-ltr">{{ a.number }}</td><td>{{ a.auditedUnit }}</td><td class="inv-ltr">{{ a.criteria }}</td><td>{{ jalali(a.plannedDate) }}</td><td>{{ a.leadAuditorName }}</td><td>{{ a.findingCount }}</td>
            <td><span :class="{ ok: a.status === 2, info: a.status === 1, warn: a.status === 0 }" class="inv-badge">{{ auditStatuses[a.status] }}</span><span v-if="a.overdue" class="inv-badge bad ms-1">عقب‌افتاده</span></td>
          </tr>
        </tbody>
      </table>
      <QPager :page="page" :total="total" @go="go" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
