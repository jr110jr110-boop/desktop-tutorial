<script setup lang="ts">
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import { getCustomers, saveCustomer } from "@/api/FnAPIs";
import { fail, ok } from "@/composables/inventory";
import { money, usePagedList } from "@/composables/fn";
import { FnRoles, hasRole } from "@/composables/roles";

// Customers (employers) of the sales invoices; the economic code, national id and postal code are needed for the taxpayer list.
const filters = ref({ Contains: "" });
const list = usePagedList(getCustomers, filters);
const form = ref<any>(null);
const busy = ref(false);
const canWrite = hasRole(FnRoles.accountants);

async function save() {
  const f = form.value;
  if (!String(f.name).trim()) return fail("نام مشتری را وارد کنید.");
  busy.value = true;
  const [, error] = await saveCustomer({ ...f });
  busy.value = false;
  if (error) return fail(error);
  ok("مشتری ذخیره شد.");
  form.value = null;
  list.load();
}
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">مشتریان</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = { id: null, name: '', economicCode: '', nationalId: '', postalCode: '', address: '', phone: '', employerName: '', isActive: true }">مشتری جدید</button>
      </div>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="نام، کد اقتصادی یا شناسه ملی" @keydown.enter="list.search" />
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>نام</th><th>کارفرما</th><th>کد اقتصادی</th><th>شناسه ملی</th><th>کد پستی</th><th>تلفن</th><th>مانده باز (ریال)</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="9" class="text-center inv-muted">مشتری‌ای نیست.</td></tr>
            <tr v-for="c in list.rows.value" :key="c.id">
              <td>{{ c.name }}</td><td>{{ c.employerName }}</td><td class="inv-ltr">{{ c.economicCode }}</td><td class="inv-ltr">{{ c.nationalId }}</td><td class="inv-ltr">{{ c.postalCode }}</td><td class="inv-ltr">{{ c.phone }}</td>
              <td class="fn-num">{{ money(c.openAmount) }}</td><td><span :class="c.isActive ? 'ok' : 'bad'" class="inv-badge">{{ c.isActive ? "فعال" : "غیرفعال" }}</span></td>
              <td class="text-nowrap">
                <router-link :to="{ name: 'FnCustomerStatement', params: { id: c.id } }" class="btn btn-sm btn-link p-0 me-2">صورت‌حساب</router-link>
                <button v-if="canWrite" class="btn btn-sm btn-outline-secondary" @click="form = { ...c }">ویرایش</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش مشتری" : "مشتری جدید" }}</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">نام *</label><input v-model="form.name" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">نام کارفرما</label><input v-model="form.employerName" class="form-control form-control-sm" /></div>
          <div class="col-6"><label class="form-label small mb-0">کد اقتصادی</label><input v-model="form.economicCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-6"><label class="form-label small mb-0">شناسه ملی</label><input v-model="form.nationalId" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-6"><label class="form-label small mb-0">کد پستی</label><input v-model="form.postalCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-6"><label class="form-label small mb-0">تلفن</label><input v-model="form.phone" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-12"><label class="form-label small mb-0">نشانی</label><input v-model="form.address" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-check small"><input v-model="form.isActive" class="form-check-input" type="checkbox" /> فعال</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
