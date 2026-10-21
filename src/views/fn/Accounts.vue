<script setup lang="ts">
import { onMounted, ref } from "vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import QPager from "@/components/quality/QPager.vue";
import { deleteAccount, getAccounts, getDetailAccounts, saveAccount, saveDetailAccount } from "@/api/FnAPIs";
import { fail, ok } from "@/composables/inventory";
import { accountLevels, accountNatures, accountTypes, detailTypes, usePagedList, yesNo } from "@/composables/fn";
import { FnRoles, hasRole } from "@/composables/roles";

// Chart of accounts (group / general / subsidiary) and detail accounts (persons, suppliers, customers, projects ...).
const tab = ref("accounts");
const tabs = [{ key: "accounts", title: "سرفصل حساب‌ها" }, { key: "details", title: "حساب‌های تفصیلی" }];
const accFilters = ref({ Level: null as number | null, Type: null as number | null, Contains: "", IncludeInactive: false });
const detFilters = ref({ Type: null as number | null, Contains: "" });
const accounts = usePagedList(getAccounts, accFilters);
const details = usePagedList(getDetailAccounts, detFilters);
const form = ref<any>(null);
const detailForm = ref<any>(null);
const busy = ref(false);
const canAccounts = hasRole(FnRoles.managers);
const canDetails = hasRole(FnRoles.accountants);

/** Names of the detail types the API keeps in DetailTypes / Detail2Types (comma separated enum names). */
const detailNames: { name: string; value: number }[] = [
  { name: "Person", value: 1 }, { name: "Supplier", value: 2 }, { name: "Customer", value: 3 }, { name: "Employee", value: 4 }, { name: "Bank", value: 5 },
  { name: "CashBox", value: 6 }, { name: "PettyCash", value: 7 }, { name: "Project", value: 8 }, { name: "WorkOrder", value: 9 }, { name: "CostCenter", value: 10 },
  { name: "Asset", value: 11 }, { name: "Other", value: 99 },
];
const titleOfName = (name: string) => detailTypes[detailNames.find((d) => d.name === name)?.value ?? 0] || name;
const namesText = (csv: string) => (csv ? csv.split(",").filter(Boolean).map((n) => titleOfName(n.trim())).join("، ") : "-");

function editAccount(a: any) {
  form.value = a
    ? { ...a, d1: (a.detailTypes || "").split(",").filter(Boolean), d2: (a.detail2Types || "").split(",").filter(Boolean) }
    : { id: null, code: "", title: "", nature: 3, type: 1, temporary: false, d1: [] as string[], d2: [] as string[], requiresDetail: false, currencyAllowed: false, statementLine: "", isActive: true, sepidarCode: "" };
}

async function saveAccountForm() {
  const f = form.value;
  if (!String(f.code).trim() || !String(f.title).trim()) return fail("کد و عنوان حساب را وارد کنید.");
  busy.value = true;
  const [, error] = await saveAccount({
    id: f.id, code: f.code, title: f.title, nature: Number(f.nature), type: Number(f.type), temporary: !!f.temporary,
    detailTypes: f.d1.join(","), detail2Types: f.d2.join(","), requiresDetail: !!f.requiresDetail, currencyAllowed: !!f.currencyAllowed,
    statementLine: f.statementLine || null, isActive: !!f.isActive, sepidarCode: f.sepidarCode || null,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("حساب ذخیره شد.");
  form.value = null;
  accounts.load();
}

async function removeAccount(a: any) {
  if (!confirm(`حساب ${a.code} - ${a.title} حذف شود؟`)) return;
  const [, error] = await deleteAccount(a.id);
  if (error) return fail(error);
  ok("حذف شد.");
  accounts.load();
}

async function saveDetailForm() {
  const f = detailForm.value;
  if (!String(f.code).trim() || !String(f.title).trim()) return fail("کد و عنوان را وارد کنید.");
  busy.value = true;
  const [, error] = await saveDetailAccount({ id: f.id, code: f.code, title: f.title, type: Number(f.type), isActive: !!f.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  detailForm.value = null;
  details.load();
}

function toggle(list: string[], name: string) {
  const i = list.indexOf(name);
  if (i >= 0) list.splice(i, 1);
  else list.push(name);
}

onMounted(() => {
  accounts.load();
  details.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">سرفصل حساب‌ها و تفصیلی‌ها</h1>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'accounts'">
        <div class="inv-filters mb-3">
          <select v-model="accFilters.Level" class="form-select form-select-sm"><option :value="null">همه سطوح</option><option v-for="(t, k) in accountLevels" :key="k" :value="Number(k)">{{ t }}</option></select>
          <select v-model="accFilters.Type" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="(t, k) in accountTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
          <input v-model="accFilters.Contains" class="form-control form-control-sm" placeholder="کد یا عنوان" @keydown.enter="accounts.search" />
          <label class="form-check small"><input v-model="accFilters.IncludeInactive" class="form-check-input" type="checkbox" /> با حساب‌های غیرفعال</label>
          <button class="btn btn-sm btn-secondary" @click="accounts.search">جستجو</button>
          <button v-if="canAccounts" class="btn btn-sm btn-primary" @click="editAccount(null)">حساب جدید</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>کد</th><th>عنوان</th><th>سطح</th><th>نوع</th><th>ماهیت</th><th>موقت</th><th>تفصیلی اول</th><th>تفصیلی دوم</th><th>کد سپیدار</th><th>فعال</th><th v-if="canAccounts"></th></tr></thead>
            <tbody>
              <tr v-if="!accounts.rows.value.length"><td colspan="11" class="text-center inv-muted">حسابی نیست.</td></tr>
              <tr v-for="a in accounts.rows.value" :key="a.id" :class="{ 'opacity-50': !a.isActive }">
                <td class="inv-ltr">{{ a.code }}</td>
                <td>{{ a.title }}</td>
                <td>{{ accountLevels[a.level] }}</td>
                <td>{{ accountTypes[a.type] }}</td>
                <td>{{ accountNatures[a.nature] }}</td>
                <td>{{ yesNo(a.temporary) }}</td>
                <td>{{ namesText(a.detailTypes) }}<span v-if="a.requiresDetail" class="inv-badge warn me-1">اجباری</span></td>
                <td>{{ namesText(a.detail2Types) }}</td>
                <td class="inv-ltr">{{ a.sepidarCode }}</td>
                <td>{{ yesNo(a.isActive) }}</td>
                <td v-if="canAccounts" class="text-nowrap">
                  <button class="btn btn-sm btn-link p-0 me-2" @click="editAccount(a)">ویرایش</button>
                  <button v-if="!a.hasPostings" class="btn btn-sm btn-link text-danger p-0" @click="removeAccount(a)">حذف</button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="accounts.page.value" :total="accounts.total.value" @go="accounts.go" />
      </template>

      <template v-else>
        <div class="inv-filters mb-3">
          <select v-model="detFilters.Type" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="(t, k) in detailTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
          <input v-model="detFilters.Contains" class="form-control form-control-sm" placeholder="کد یا عنوان" @keydown.enter="details.search" />
          <button class="btn btn-sm btn-secondary" @click="details.search">جستجو</button>
          <button v-if="canDetails" class="btn btn-sm btn-primary" @click="detailForm = { id: null, code: '', title: '', type: 1, isActive: true }">تفصیلی جدید</button>
        </div>
        <table class="table table-sm table-hover">
          <thead><tr><th>کد</th><th>عنوان</th><th>نوع</th><th>فعال</th><th v-if="canDetails"></th></tr></thead>
          <tbody>
            <tr v-if="!details.rows.value.length"><td colspan="5" class="text-center inv-muted">تفصیلی‌ای نیست.</td></tr>
            <tr v-for="d in details.rows.value" :key="d.id" :class="{ 'opacity-50': !d.isActive }">
              <td class="inv-ltr">{{ d.code }}</td>
              <td>{{ d.title }}</td>
              <td>{{ detailTypes[d.type] }}</td>
              <td>{{ yesNo(d.isActive) }}</td>
              <td v-if="canDetails"><button class="btn btn-sm btn-link p-0" @click="detailForm = { ...d }">ویرایش</button></td>
            </tr>
          </tbody>
        </table>
        <QPager :page="details.page.value" :total="details.total.value" @go="details.go" />
      </template>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش حساب" : "حساب جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-3"><label class="form-label small mb-0">کد *</label><input v-model="form.code" :disabled="!!form.id" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-9"><label class="form-label small mb-0">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نوع</label><select v-model="form.type" class="form-select form-select-sm"><option v-for="(t, k) in accountTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">ماهیت</label><select v-model="form.nature" class="form-select form-select-sm"><option v-for="(t, k) in accountNatures" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">کد سپیدار (برای خروجی مالیات و بیمه)</label><input v-model="form.sepidarCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-12">
            <label class="form-label small mb-0">انواع تفصیلی اول مجاز</label>
            <div class="d-flex flex-wrap gap-3"><label v-for="n in detailNames" :key="n.name" class="form-check small"><input :checked="form.d1.includes(n.name)" class="form-check-input" type="checkbox" @change="toggle(form.d1, n.name)" /> {{ detailTypes[n.value] }}</label></div>
          </div>
          <div class="col-12">
            <label class="form-label small mb-0">انواع تفصیلی دوم مجاز (پروژه، دستور ساخت، مرکز هزینه)</label>
            <div class="d-flex flex-wrap gap-3"><label v-for="n in detailNames" :key="n.name" class="form-check small"><input :checked="form.d2.includes(n.name)" class="form-check-input" type="checkbox" @change="toggle(form.d2, n.name)" /> {{ detailTypes[n.value] }}</label></div>
          </div>
          <div class="col-md-6"><label class="form-label small mb-0">ردیف صورت‌های مالی</label><input v-model="form.statementLine" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6 d-flex align-items-end gap-3 flex-wrap">
            <label class="form-check small"><input v-model="form.temporary" class="form-check-input" type="checkbox" /> حساب موقت (بسته می‌شود)</label>
            <label class="form-check small"><input v-model="form.requiresDetail" class="form-check-input" type="checkbox" /> تفصیلی اجباری</label>
            <label class="form-check small"><input v-model="form.currencyAllowed" class="form-check-input" type="checkbox" /> ارزی</label>
            <label class="form-check small"><input v-model="form.isActive" class="form-check-input" type="checkbox" /> فعال</label>
          </div>
        </div>
        <p class="inv-muted mt-2 mb-0">سند فقط به حساب معین (سطح ۳) ثبت می‌شود.</p>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="saveAccountForm">ذخیره</button>
        </div>
      </div>
    </div>

    <div v-if="detailForm" class="inv-modal-backdrop screen-only" @click.self="detailForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ detailForm.id ? "ویرایش تفصیلی" : "تفصیلی جدید" }}</h2>
        <div class="row g-2">
          <div class="col-4"><label class="form-label small mb-0">کد *</label><input v-model="detailForm.code" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-8"><label class="form-label small mb-0">عنوان *</label><input v-model="detailForm.title" class="form-control form-control-sm" /></div>
          <div class="col-8"><label class="form-label small mb-0">نوع</label><select v-model="detailForm.type" class="form-select form-select-sm"><option v-for="(t, k) in detailTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-4 d-flex align-items-end"><label class="form-check small"><input v-model="detailForm.isActive" class="form-check-input" type="checkbox" /> فعال</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="detailForm = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="saveDetailForm">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
