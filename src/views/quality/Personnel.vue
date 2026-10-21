<script setup lang="ts">
import { nextTick } from "vue";
import { printForm } from "@/composables/quality";
import WelderCardPrint from "@/components/quality/print/WelderCardPrint.vue";
import { computed, onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import QPager from "@/components/quality/QPager.vue";
import DocAttach from "@/components/quality/DocAttach.vue";
import UserSelect from "@/components/quality/UserSelect.vue";
import { getContinuity, getPerson, getPersons, getQualifications, qualificationAction, savePerson, saveQualification } from "@/api/QualityAPIs";
import { fail, jalali, ok, printPage, today } from "@/composables/inventory";
import { employerKinds, ndtLevels, qualificationBadge, qualificationKinds, qualificationSchemes, qualificationStates, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Qualified persons and their qualifications (welders, NDT personnel, inspectors; ASME IX QW-322 continuity).
// Names printed on forms are job titles; this page is the register of the persons themselves.
const tab = ref<"persons" | "qualifications" | "continuity">("persons");
const canManage = hasRole(QualityRoles.managers);
const busy = ref(false);
async function doPrint() {
  await nextTick();
  printForm();
}

const personFilters = ref({ Contains: "", Employer: null as number | null, IsActive: true as boolean | null });
const persons = usePaged(getPersons, personFilters);
const qualFilters = ref({ Kind: null as number | null, State: null as number | null, Method: "", Contains: "" });
const quals = usePaged(getQualifications, qualFilters);
const contFilters = ref({ Days: 30 });
const cont = usePaged(getContinuity, contFilters);

const personForm = ref<any>(null);
const qualForm = ref<any>(null);
const selected = ref<any>(null);
const revoke = ref({ id: null as number | null, reason: "" });
const ndt = computed(() => qualForm.value?.kind === 3);

const blankPerson = () => ({ id: null, name: "", personnelCode: "", employer: 1, providerId: null, stampId: "", userId: null, isActive: true });
const blankQual = (personId: number | null = null, renews: any = null) => ({
  personId,
  kind: renews?.kind ?? 1,
  method: renews?.method ?? "",
  level: renews?.level ?? 0,
  scheme: renews?.scheme ?? 1,
  scope: renews?.scope ?? "",
  certificateNumber: "",
  certificateDocumentId: null,
  issuedDate: today(),
  expiryDate: null,
  renewsQualificationId: renews?.id ?? null,
});

async function savePersonForm() {
  busy.value = true;
  const [data, error] = await savePerson(personForm.value);
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  personForm.value = null;
  persons.load();
  selected.value = data;
}

async function openPerson(p: any) {
  const [data, error] = await getPerson(p.id);
  if (error) return fail(error);
  selected.value = data;
}

async function saveQualForm() {
  const q = qualForm.value;
  busy.value = true;
  const [, error] = await saveQualification({ ...q, level: ndt.value ? Number(q.level) : 0, expiryDate: q.expiryDate || null });
  busy.value = false;
  if (error) return fail(error);
  ok("گواهی ثبت شد.");
  qualForm.value = null;
  quals.load();
  cont.load();
  if (selected.value) openPerson(selected.value);
}

async function doRevoke() {
  if (!revoke.value.reason.trim()) return fail("دلیل ابطال را بنویسید.");
  busy.value = true;
  const [, error] = await qualificationAction(revoke.value.id!, "revoke", revoke.value.reason);
  busy.value = false;
  if (error) return fail(error);
  ok("گواهی ابطال شد.");
  revoke.value = { id: null, reason: "" };
  quals.load();
  cont.load();
  if (selected.value) openPerson(selected.value);
}

function setTab(t: "persons" | "qualifications" | "continuity") {
  tab.value = t;
  if (t === "qualifications") quals.load();
  if (t === "continuity") cont.load();
}

onMounted(persons.load);
</script>

<template>
  <div class="inv-page ks-report">
    <WelderCardPrint v-if="selected" :person="selected" />
    <KsReportHeader title="پرسنل صلاحیت‌دار و گواهی‌ها" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">پرسنل صلاحیت‌دار (جوشکار، NDT، بازرس)</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <router-link :to="{ name: 'QualityWps' }" class="btn btn-sm btn-outline-secondary">WPS و PQR</router-link>
          <router-link :to="{ name: 'QualityWeldLogs' }" class="btn btn-sm btn-outline-secondary">گزارش جوشکاری</router-link>
          <button v-if="canManage" class="btn btn-sm btn-primary" @click="personForm = blankPerson()">شخص جدید</button>
          <button v-if="canManage" class="btn btn-sm btn-primary" @click="qualForm = blankQual(selected?.id ?? null)">گواهی جدید</button>
        </div>
      </div>

      <div v-if="personForm" class="border rounded p-2 mb-3 d-print-none">
        <div class="row g-2">
          <div class="col-md-3"><label class="form-label small mb-0">نام *</label><input v-model="personForm.name" class="form-control form-control-sm" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">کد پرسنلی</label><input v-model="personForm.personnelCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">کارفرمای شخص</label><select v-model="personForm.employer" class="form-select form-select-sm"><option v-for="(t, k) in employerKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div v-if="personForm.employer !== 1" class="col-md-2"><label class="form-label small mb-0">شناسه تامین‌کننده</label><input v-model.number="personForm.providerId" class="form-control form-control-sm" type="number" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">شماره مهر / شناسه جوشکار</label><input v-model="personForm.stampId" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">کاربر سیستم (اگر دارد)</label><UserSelect v-model="personForm.userId" /></div>
          <div class="col-md-2 d-flex align-items-end"><label class="form-check small"><input v-model="personForm.isActive" class="form-check-input" type="checkbox" /> فعال</label></div>
          <div class="col-md-3 d-flex align-items-end gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="savePersonForm">ذخیره</button><button class="btn btn-sm btn-outline-secondary" @click="personForm = null">انصراف</button></div>
        </div>
      </div>

      <div v-if="qualForm" class="border rounded p-2 mb-3 d-print-none">
        <h2 class="inv-title mb-2">{{ qualForm.renewsQualificationId ? "تمدید (آزمون جدید) گواهی" : "گواهی جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-3">
            <label class="form-label small mb-0">شخص *</label>
            <select v-model="qualForm.personId" :disabled="!!qualForm.renewsQualificationId" class="form-select form-select-sm">
              <option :value="null">-</option><option v-for="p in persons.rows.value" :key="p.id" :value="p.id">{{ p.name }}</option>
            </select>
          </div>
          <div class="col-md-2"><label class="form-label small mb-0">نوع</label><select v-model="qualForm.kind" :disabled="!!qualForm.renewsQualificationId" class="form-select form-select-sm"><option v-for="(t, k) in qualificationKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">فرایند / روش *</label><input v-model="qualForm.method" :disabled="!!qualForm.renewsQualificationId" class="form-control form-control-sm inv-ltr" placeholder="GTAW, SMAW, UT, PT ..." /></div>
          <div v-if="ndt" class="col-md-1"><label class="form-label small mb-0">سطح</label><select v-model="qualForm.level" class="form-select form-select-sm"><option v-for="(t, k) in ndtLevels" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div v-if="ndt" class="col-md-2"><label class="form-label small mb-0">طرح گواهی</label><select v-model="qualForm.scheme" class="form-select form-select-sm"><option :value="2">ASNT SNT-TC-1A (با رویه نوشتاری)</option><option :value="3">ISO 9712</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">دامنه (P-No, F-No, ضخامت، وضعیت‌ها)</label><input v-model="qualForm.scope" class="form-control form-control-sm" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">شماره گواهی *</label><input v-model="qualForm.certificateNumber" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">تاریخ صدور / آزمون *</label><input v-model="qualForm.issuedDate" class="form-control form-control-sm" type="date" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">تاریخ انقضا</label><input v-model="qualForm.expiryDate" class="form-control form-control-sm" type="date" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">فایل گواهی</label><DocAttach v-model="qualForm.certificateDocumentId" title="گواهی صلاحیت" /></div>
          <div class="col-md-3 d-flex align-items-end gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveQualForm">ثبت</button><button class="btn btn-sm btn-outline-secondary" @click="qualForm = null">انصراف</button></div>
        </div>
      </div>

      <div v-if="revoke.id" class="border rounded p-2 mb-3 d-print-none d-flex gap-2 align-items-end">
        <div class="flex-grow-1"><label class="form-label small mb-0">دلیل ابطال گواهی *</label><input v-model="revoke.reason" class="form-control form-control-sm" /></div>
        <button :disabled="busy" class="btn btn-sm btn-danger" @click="doRevoke">ابطال</button>
        <button class="btn btn-sm btn-outline-secondary" @click="revoke = { id: null, reason: '' }">انصراف</button>
      </div>

      <ul class="nav nav-tabs mb-3 d-print-none">
        <li class="nav-item"><button :class="{ active: tab === 'persons' }" class="nav-link" @click="setTab('persons')">اشخاص</button></li>
        <li class="nav-item"><button :class="{ active: tab === 'qualifications' }" class="nav-link" @click="setTab('qualifications')">گواهی‌ها</button></li>
        <li class="nav-item"><button :class="{ active: tab === 'continuity' }" class="nav-link" @click="setTab('continuity')">تداوم جوشکاری (QW-322)</button></li>
      </ul>

      <template v-if="tab === 'persons'">
        <div class="inv-filters mb-3 d-print-none">
          <input v-model="personFilters.Contains" class="form-control form-control-sm" placeholder="نام یا کد" @keydown.enter="persons.search()" />
          <select v-model="personFilters.Employer" class="form-select form-select-sm" @change="persons.search()"><option :value="null">همه</option><option v-for="(t, k) in employerKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
          <select v-model="personFilters.IsActive" class="form-select form-select-sm" @change="persons.search()"><option :value="null">فعال و غیرفعال</option><option :value="true">فعال</option><option :value="false">غیرفعال</option></select>
          <button class="btn btn-sm btn-secondary" @click="persons.search()">جستجو</button>
        </div>
        <table class="table table-sm table-hover">
          <thead><tr><th>نام</th><th>کد پرسنلی</th><th>کارفرما</th><th>شماره مهر</th><th>فعال</th><th>گواهی‌ها</th></tr></thead>
          <tbody>
            <tr v-if="!persons.rows.value.length"><td colspan="6" class="text-center inv-muted">شخصی نیست.</td></tr>
            <tr v-for="p in persons.rows.value" :key="p.id" role="button" @click="openPerson(p)">
              <td>{{ p.name }}</td><td class="inv-ltr">{{ p.personnelCode }}</td><td>{{ employerKinds[p.employer] }}</td><td class="inv-ltr">{{ p.stampId }}</td>
              <td>{{ p.isActive ? "بله" : "خیر" }}</td><td>{{ (p.qualifications || []).length }}</td>
            </tr>
          </tbody>
        </table>
        <QPager :page="persons.page.value" :total="persons.total.value" @go="persons.go" />
        <div v-if="selected" class="border rounded p-2 mt-3">
          <div class="d-flex justify-content-between align-items-center">
            <b>{{ selected.name }} <span class="inv-ltr">{{ selected.personnelCode }}</span></b>
            <div class="d-flex gap-2 d-print-none">
              <button class="btn btn-sm btn-outline-secondary" @click="doPrint">چاپ کارت صلاحیت</button>
              <button v-if="canManage" class="btn btn-sm btn-outline-primary" @click="personForm = { ...selected }">ویرایش شخص</button>
              <button v-if="canManage" class="btn btn-sm btn-outline-primary" @click="qualForm = blankQual(selected.id)">گواهی جدید</button>
              <button class="btn btn-sm btn-outline-secondary" @click="selected = null">بستن</button>
            </div>
          </div>
          <table class="table table-sm mt-2">
            <thead><tr><th>نوع</th><th>روش</th><th>سطح</th><th>طرح</th><th>شماره گواهی</th><th>صدور</th><th>انقضا</th><th>آخرین جوشکاری</th><th>قطع تداوم در</th><th>وضعیت</th><th></th></tr></thead>
            <tbody>
              <tr v-for="q in selected.qualifications" :key="q.id">
                <td>{{ qualificationKinds[q.kind] }}</td><td class="inv-ltr">{{ q.method }}</td><td>{{ ndtLevels[q.level] }}</td><td>{{ qualificationSchemes[q.scheme] }}</td>
                <td class="inv-ltr">{{ q.certificateNumber }}</td><td>{{ jalali(q.issuedDate) }}</td><td>{{ jalali(q.expiryDate) }}</td><td>{{ jalali(q.lastUse) }}</td><td>{{ jalali(q.lapsesOn) }}</td>
                <td><span :class="qualificationBadge(q.state)" class="inv-badge" :title="q.stateText">{{ qualificationStates[q.state] }}</span></td>
                <td class="text-nowrap d-print-none">
                  <button v-if="canManage && (q.state === 2 || q.state === 3)" class="btn btn-sm btn-outline-success" @click="qualForm = blankQual(selected.id, q)">آزمون مجدد / تمدید</button>
                  <button v-if="canManage && !q.revoked && !q.superseded" class="btn btn-sm btn-outline-danger" @click="revoke = { id: q.id, reason: '' }">ابطال</button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>

      <template v-if="tab === 'qualifications'">
        <div class="inv-filters mb-3 d-print-none">
          <input v-model="qualFilters.Contains" class="form-control form-control-sm" placeholder="نام یا شماره گواهی" @keydown.enter="quals.search()" />
          <select v-model="qualFilters.Kind" class="form-select form-select-sm" @change="quals.search()"><option :value="null">همه انواع</option><option v-for="(t, k) in qualificationKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
          <input v-model="qualFilters.Method" class="form-control form-control-sm inv-ltr" placeholder="روش (GTAW, UT ...)" @keydown.enter="quals.search()" />
          <select v-model="qualFilters.State" class="form-select form-select-sm" @change="quals.search()"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in qualificationStates" :key="k" :value="Number(k)">{{ t }}</option></select>
          <button class="btn btn-sm btn-secondary" @click="quals.search()">جستجو</button>
        </div>
        <table class="table table-sm table-hover">
          <thead><tr><th>شخص</th><th>مهر</th><th>نوع</th><th>روش</th><th>سطح</th><th>شماره گواهی</th><th>صدور</th><th>انقضا</th><th>آخرین جوشکاری</th><th>قطع تداوم در</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-if="!quals.rows.value.length"><td colspan="11" class="text-center inv-muted">گواهی‌ای نیست.</td></tr>
            <tr v-for="q in quals.rows.value" :key="q.id">
              <td>{{ q.personName }}</td><td class="inv-ltr">{{ q.stampId }}</td><td>{{ qualificationKinds[q.kind] }}</td><td class="inv-ltr">{{ q.method }}</td><td>{{ ndtLevels[q.level] }}</td>
              <td class="inv-ltr">{{ q.certificateNumber }}</td><td>{{ jalali(q.issuedDate) }}</td><td>{{ jalali(q.expiryDate) }}</td><td>{{ jalali(q.lastUse) }}</td><td>{{ jalali(q.lapsesOn) }}</td>
              <td><span :class="qualificationBadge(q.state)" class="inv-badge">{{ qualificationStates[q.state] }}</span></td>
            </tr>
          </tbody>
        </table>
        <QPager :page="quals.page.value" :total="quals.total.value" @go="quals.go" />
      </template>

      <template v-if="tab === 'continuity'">
        <div class="inv-filters mb-3 d-print-none">
          <label class="small">جوشکارانی که تا <input v-model.number="contFilters.Days" class="form-control form-control-sm d-inline-block" style="width: 80px" type="number" min="1" /> روز آینده تداومشان قطع می‌شود یا قطع شده</label>
          <button class="btn btn-sm btn-secondary" @click="cont.search()">نمایش</button>
        </div>
        <p class="inv-muted">اگر جوشکار بیش از شش ماه در یک فرایند جوشکاری نکند گواهی او «قطع تداوم» می‌شود و تا آزمون مجدد قابل استفاده نیست.</p>
        <table class="table table-sm">
          <thead><tr><th>جوشکار</th><th>مهر</th><th>فرایند</th><th>شماره گواهی</th><th>آخرین جوشکاری</th><th>قطع تداوم در</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-if="!cont.rows.value.length"><td colspan="7" class="text-center inv-muted">موردی نیست.</td></tr>
            <tr v-for="q in cont.rows.value" :key="q.id">
              <td>{{ q.personName }}</td><td class="inv-ltr">{{ q.stampId }}</td><td class="inv-ltr">{{ q.method }}</td><td class="inv-ltr">{{ q.certificateNumber }}</td>
              <td>{{ jalali(q.lastUse) }}</td><td>{{ jalali(q.lapsesOn) }}</td><td><span :class="qualificationBadge(q.state)" class="inv-badge">{{ qualificationStates[q.state] }}</span></td>
            </tr>
          </tbody>
        </table>
        <QPager :page="cont.page.value" :total="cont.total.value" @go="cont.go" />
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
