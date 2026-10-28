<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import QPager from "@/components/quality/QPager.vue";
import { checkDocumentCode, getControlledDocuments, getMasterList, getRetention, saveControlledDocument, saveRetention } from "@/api/QualityAPIs";
import { fail, jalali, ok, printPage } from "@/composables/inventory";
import { documentStatusBadge, documentStatuses, documentTypes, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Document control (ISO 9001 7.5): master list of the effective revisions, all documents, retention rules.
const router = useRouter();
const tab = ref<"master" | "all" | "retention">("master");
const filters = ref({ Type: null as number | null, Status: null as number | null, Department: "", Contains: "" });
const master = usePaged(getMasterList, filters);
const all = usePaged(getControlledDocuments, filters);
const retention = ref<any[]>([]);
const form = ref<any>(null);
const codeCheck = ref<any>(null);
const rule = ref<any>(null);
const busy = ref(false);
const canWrite = hasRole(QualityRoles.documentReviewers);
const canRetention = hasRole(QualityRoles.documentApprovers);
const src = () => (tab.value === "master" ? master : all);

const blank = () => ({ id: null, type: 3, department: "", serial: null, title: "", retentionClass: "", responsibleUnit: "" });

async function check() {
  const f = form.value;
  if (!f.department.trim()) return fail("واحد (مثلاً QC) را وارد کنید.");
  const [data, error] = await checkDocumentCode(f.type, f.department, f.serial);
  if (error) return fail(error);
  codeCheck.value = data;
}

async function create() {
  busy.value = true;
  const [data, error] = await saveControlledDocument({ ...form.value, serial: form.value.serial || null });
  busy.value = false;
  if (error) return fail(error);
  ok("سند ثبت شد.");
  form.value = null;
  router.push({ name: "QualityDocumentEdit", params: { id: data.id } });
}

async function loadRetention() {
  const [data, error] = await getRetention();
  if (error) return fail(error);
  retention.value = data || [];
}

async function saveRule() {
  busy.value = true;
  const [data, error] = await saveRetention({ recordType: rule.value.recordType, years: rule.value.permanent ? null : Number(rule.value.years) || null, permanent: rule.value.permanent, source: rule.value.source });
  busy.value = false;
  if (error) return fail(error);
  retention.value = data || retention.value;
  rule.value = null;
  ok("ذخیره شد.");
}

function setTab(t: "master" | "all" | "retention") {
  tab.value = t;
  if (t === "retention") loadRetention();
  else src().search();
}

onMounted(master.load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader :title="tab === 'master' ? 'فهرست اصلی مدارک (Master List)' : tab === 'retention' ? 'قواعد نگهداری سوابق' : 'مدارک کنترل‌شده'" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">کنترل مدارک</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="(form = blank()), (codeCheck = null)">سند جدید</button>
        </div>
      </div>

      <div v-if="form" class="border rounded p-2 mb-3 d-print-none">
        <div class="row g-2">
          <div class="col-md-2"><label class="form-label small mb-0">نوع</label><select v-model="form.type" class="form-select form-select-sm"><option v-for="(t, k) in documentTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">واحد (کد) *</label><input v-model="form.department" class="form-control form-control-sm inv-ltr" placeholder="QC" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">شماره (خالی = بعدی)</label><input v-model.number="form.serial" class="form-control form-control-sm" type="number" min="1" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">واحد مسئول</label><input v-model="form.responsibleUnit" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">رده نگهداری</label><input v-model="form.retentionClass" class="form-control form-control-sm" /></div>
          <div class="col-md-8 d-flex align-items-end gap-2">
            <button class="btn btn-sm btn-outline-secondary" @click="check">بررسی کد</button>
            <button :disabled="busy" class="btn btn-sm btn-primary" @click="create">ثبت</button>
            <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
            <span v-if="codeCheck" class="small">کد <b class="inv-ltr">{{ codeCheck.code }}</b>: {{ codeCheck.exists ? "تکراری است؛ کد آزاد بعدی " + codeCheck.nextFreeCode : "آزاد است" }}</span>
          </div>
        </div>
      </div>

      <ul class="nav nav-tabs mb-3 d-print-none">
        <li class="nav-item"><button :class="{ active: tab === 'master' }" class="nav-link" @click="setTab('master')">فهرست اصلی (معتبرها)</button></li>
        <li class="nav-item"><button :class="{ active: tab === 'all' }" class="nav-link" @click="setTab('all')">همه مدارک</button></li>
        <li class="nav-item"><button :class="{ active: tab === 'retention' }" class="nav-link" @click="setTab('retention')">قواعد نگهداری</button></li>
      </ul>

      <template v-if="tab !== 'retention'">
        <div class="inv-filters mb-3 d-print-none">
          <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="کد یا عنوان" @keydown.enter="src().search()" />
          <select v-model="filters.Type" class="form-select form-select-sm" @change="src().search()"><option :value="null">همه انواع</option><option v-for="(t, k) in documentTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
          <select v-if="tab === 'all'" v-model="filters.Status" class="form-select form-select-sm" @change="src().search()"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in documentStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
          <input v-model="filters.Department" class="form-control form-control-sm inv-ltr" placeholder="واحد" @keydown.enter="src().search()" />
          <button class="btn btn-sm btn-secondary" @click="src().search()">جستجو</button>
        </div>
        <table class="table table-sm table-hover">
          <thead><tr><th>کد</th><th>عنوان</th><th>نوع</th><th>بازنگری جاری</th><th>تاریخ اجرا</th><th>رده نگهداری</th><th>واحد مسئول</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-if="!src().rows.value.length"><td colspan="8" class="text-center inv-muted">سندی نیست.</td></tr>
            <tr v-for="d in src().rows.value" :key="d.id" role="button" @click="router.push({ name: 'QualityDocumentEdit', params: { id: d.id } })">
              <td class="inv-ltr">{{ d.code }}</td><td>{{ d.title }}</td><td>{{ documentTypes[d.type] }}</td><td class="inv-ltr">{{ d.currentRevision }}</td><td>{{ jalali(d.effectiveDate) }}</td>
              <td>{{ d.retentionClass }}</td><td>{{ d.responsibleUnit }}</td>
              <td><span :class="documentStatusBadge(d.status)" class="inv-badge">{{ documentStatuses[d.status] }}</span></td>
            </tr>
          </tbody>
        </table>
        <QPager :page="src().page.value" :total="src().total.value" @go="src().go" />
      </template>

      <template v-else>
        <div class="d-flex justify-content-end mb-2 d-print-none"><button v-if="canRetention" class="btn btn-sm btn-primary" @click="rule = { recordType: '', years: 10, permanent: false, source: '' }">قاعده جدید</button></div>
        <div v-if="rule" class="border rounded p-2 mb-3 d-print-none">
          <div class="row g-2 align-items-end">
            <div class="col-md-3"><label class="form-label small mb-0">نوع سابقه *</label><input v-model="rule.recordType" class="form-control form-control-sm" /></div>
            <div class="col-md-2"><label class="form-label small mb-0">سال</label><input v-model.number="rule.years" :disabled="rule.permanent" class="form-control form-control-sm" type="number" min="1" /></div>
            <div class="col-md-2"><label class="form-check small"><input v-model="rule.permanent" class="form-check-input" type="checkbox" /> دائمی</label></div>
            <div class="col-md-3"><label class="form-label small mb-0">مبنا</label><input v-model="rule.source" class="form-control form-control-sm" /></div>
            <div class="col-md-2 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveRule">ذخیره</button><button class="btn btn-sm btn-outline-secondary" @click="rule = null">انصراف</button></div>
          </div>
        </div>
        <table class="table table-sm">
          <thead><tr><th>نوع سابقه</th><th>مدت نگهداری</th><th>مبنا</th><th class="d-print-none"></th></tr></thead>
          <tbody>
            <tr v-if="!retention.length"><td colspan="4" class="text-center inv-muted">قاعده‌ای نیست.</td></tr>
            <tr v-for="r in retention" :key="r.id">
              <td>{{ r.recordType }}</td><td>{{ r.permanent ? "دائمی (عمر محصول)" : r.years + " سال" }}</td><td class="small">{{ r.source }}</td>
              <td class="d-print-none"><button v-if="canRetention" class="btn btn-sm btn-outline-secondary" @click="rule = { ...r }">ویرایش</button></td>
            </tr>
          </tbody>
        </table>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
