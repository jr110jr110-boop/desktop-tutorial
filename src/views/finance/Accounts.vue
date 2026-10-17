<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getAccountMappings, getCostCenters, saveAccountMapping, saveCostCenter } from "@/api/FinanceAPIs";
import { fail, ok } from "@/composables/inventory";
import { roleTitle } from "@/composables/finance";
import { FinanceRoles, hasRole } from "@/composables/roles";

// Which Sepidar account each role of the vouchers goes to, and the cost centres (projects) with their Sepidar codes.
const tab = ref<"accounts" | "centers">("accounts");
const accounts = ref<any[]>([]);
const centers = ref<any[]>([]);
const center = ref<any>(null);
const newRole = ref({ role: "", title: "" });
const canWrite = hasRole(FinanceRoles.writers);
const busy = ref(false);

async function loadAccounts() {
  const [data, error] = await getAccountMappings();
  if (error) return fail(error);
  accounts.value = (data || []).map((a: any) => ({ ...a, dirty: false }));
}

async function loadCenters() {
  const [data, error] = await getCostCenters();
  if (error) return fail(error);
  centers.value = data || [];
}

async function saveAccount(a: any) {
  busy.value = true;
  const [, error] = await saveAccountMapping({
    id: a.id, role: a.role, title: a.title, sepidarAccountCode: a.sepidarAccountCode, description: a.description, isActive: a.isActive,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  loadAccounts();
}

async function addRole() {
  if (!newRole.value.role.trim()) return fail("نقش حساب را بنویسید.");
  const [, error] = await saveAccountMapping({ role: newRole.value.role.trim(), title: newRole.value.title.trim(), sepidarAccountCode: "", isActive: true });
  if (error) return fail(error);
  newRole.value = { role: "", title: "" };
  loadAccounts();
}

async function saveCenterForm() {
  busy.value = true;
  const [, error] = await saveCostCenter(center.value);
  busy.value = false;
  if (error) return fail(error);
  ok("مرکز هزینه ذخیره شد.");
  center.value = null;
  loadCenters();
}

onMounted(() => {
  loadAccounts();
  loadCenters();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">نگاشت حساب‌ها و مراکز هزینه</h1>
        <div class="btn-group">
          <button class="btn btn-sm" :class="tab === 'accounts' ? 'btn-primary' : 'btn-outline-primary'" @click="tab = 'accounts'">حساب‌های سپیدار</button>
          <button class="btn btn-sm" :class="tab === 'centers' ? 'btn-primary' : 'btn-outline-primary'" @click="tab = 'centers'">مراکز هزینه</button>
        </div>
      </div>

      <template v-if="tab === 'accounts'">
        <p class="inv-muted">
          هر ردیف سند حسابداری یک «نقش حساب» دارد (مثلاً موجودی قطعات، کالای دریافتی فاکتورنشده). کد حساب هر نقش در سپیدار اینجا ثبت می‌شود؛
          تا کد همه نقش‌ها نباشد، «آمادگی سپیدار» آماده نمی‌شود.
        </p>
        <div class="table-responsive">
          <table class="table table-sm table-bordered">
            <thead>
              <tr><th>نقش</th><th>عنوان</th><th style="width: 200px">کد حساب در سپیدار</th><th>توضیح</th><th>فعال</th><th v-if="canWrite"></th></tr>
            </thead>
            <tbody>
              <tr v-for="a in accounts" :key="a.id || a.role" :class="{ 'table-warning': !a.sepidarAccountCode }">
                <td class="inv-ltr">{{ a.role }}</td>
                <td>{{ a.title || roleTitle(a.role) }}</td>
                <td><input v-model="a.sepidarAccountCode" :disabled="!canWrite" class="form-control form-control-sm inv-ltr" @input="a.dirty = true" /></td>
                <td><input v-model="a.description" :disabled="!canWrite" class="form-control form-control-sm" @input="a.dirty = true" /></td>
                <td><input v-model="a.isActive" :disabled="!canWrite" class="form-check-input" type="checkbox" @change="a.dirty = true" /></td>
                <td v-if="canWrite"><button :disabled="busy || !a.dirty" class="btn btn-sm btn-outline-primary" @click="saveAccount(a)">ذخیره</button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <div v-if="canWrite" class="d-flex gap-2 align-items-end">
          <div>
            <label class="form-label small mb-0">نقش جدید (مثلاً Inventory.09)</label>
            <input v-model="newRole.role" class="form-control form-control-sm inv-ltr" />
          </div>
          <div>
            <label class="form-label small mb-0">عنوان</label>
            <input v-model="newRole.title" class="form-control form-control-sm" />
          </div>
          <button class="btn btn-sm btn-outline-primary" @click="addRole">+ نقش</button>
        </div>
      </template>

      <template v-else>
        <div class="d-flex justify-content-between align-items-center mb-2">
          <p class="inv-muted mb-0">مرکز هزینه هر پروژه / قرارداد؛ کد سپیدار آن در سند حسابداری می‌آید.</p>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="center = { id: null, code: '', title: '', sepidarCode: '', isActive: true }">مرکز هزینه جدید</button>
        </div>
        <table class="table table-sm table-hover">
          <thead><tr><th>کد</th><th>عنوان</th><th>کد سپیدار</th><th>فعال</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!centers.length"><td colspan="5" class="text-center inv-muted">مرکز هزینه‌ای نیست.</td></tr>
            <tr v-for="c in centers" :key="c.id" :class="{ 'opacity-50': !c.isActive }">
              <td class="inv-ltr">{{ c.code }}</td>
              <td>{{ c.title }}</td>
              <td class="inv-ltr">{{ c.sepidarCode }}</td>
              <td>{{ c.isActive ? "بله" : "خیر" }}</td>
              <td v-if="canWrite"><button class="btn btn-sm btn-link p-0" @click="center = { ...c }">ویرایش</button></td>
            </tr>
          </tbody>
        </table>
      </template>
    </div>

    <div v-if="center" class="inv-modal-backdrop" @click.self="center = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ center.id ? "ویرایش مرکز هزینه" : "مرکز هزینه جدید" }}</h2>
        <div class="row g-2">
          <div class="col-4"><label class="form-label small mb-0">کد *</label><input v-model="center.code" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-8"><label class="form-label small mb-0">عنوان *</label><input v-model="center.title" class="form-control form-control-sm" /></div>
          <div class="col-6"><label class="form-label small mb-0">کد سپیدار</label><input v-model="center.sepidarCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-6 d-flex align-items-end">
            <label class="form-check small"><input v-model="center.isActive" class="form-check-input" type="checkbox" /> فعال</label>
          </div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="center = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="saveCenterForm">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
