<script setup lang="ts">
import { onMounted, ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import { getSlContacts, saveSlContact } from "@/api/SalesAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { SalesRoles, hasRole } from "@/composables/roles";
import { blankToNull, customerLabel, searchCustomers } from "./pipelineKit";

// Contacts of customers (people are entered by users; nothing is seeded).
const customer = ref<any>(null);
const filters = ref({ CustomerId: null as number | null, Contains: "" });
const list = usePagedList(getSlContacts, filters);
const form = ref<any>(null);
const formCustomer = ref<any>(null);
const busy = ref(false);
const canEdit = hasRole(SalesRoles.masterWriters);

function pick(c: any) {
  customer.value = c;
  filters.value.CustomerId = c ? c.id : null;
  list.search();
}
function edit(r: any) {
  form.value = r ? { id: r.id, customerId: r.customerId, fullName: r.fullName, jobTitle: r.jobTitle || "", phone: r.phone || "", email: r.email || "", isPrimary: !!r.isPrimary, isActive: r.isActive !== false }
    : { id: null, customerId: customer.value?.id ?? null, fullName: "", jobTitle: "", phone: "", email: "", isPrimary: false, isActive: true };
  formCustomer.value = r ? { id: r.customerId, name: r.customerName } : customer.value;
}
async function save() {
  const f = form.value;
  if (!f.customerId) return fail({ message: "مشتری را انتخاب کنید." });
  if (!String(f.fullName).trim()) return fail({ message: "نام مخاطب را بنویسید." });
  busy.value = true;
  const [, error] = await saveSlContact({ id: f.id, customerId: f.customerId, fullName: f.fullName.trim(), jobTitle: blankToNull(f.jobTitle), phone: blankToNull(f.phone), email: blankToNull(f.email), isPrimary: f.isPrimary, isActive: f.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("مخاطب ذخیره شد.");
  form.value = null;
  list.load();
}
onMounted(list.load);
</script>

<template>
  <div>
    <div class="inv-toolbar">
      <div class="inv-filters">
        <LookupBox :model-value="customer" :fetch="searchCustomers" :label="customerLabel" placeholder="مشتری" @pick="pick" />
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="نام مخاطب" @keyup.enter="list.search" />
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <button v-if="canEdit" class="btn btn-sm btn-primary" @click="edit(null)">مخاطب جدید</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>مشتری</th><th>نام</th><th>سمت</th><th>تلفن</th><th>ایمیل</th><th>اصلی</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="8" class="text-center inv-muted">مخاطبی ثبت نشده است.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id">
            <td>{{ r.customerName }}</td><td>{{ r.fullName }}</td><td>{{ r.jobTitle || "-" }}</td><td class="inv-ltr">{{ r.phone || "-" }}</td><td class="inv-ltr">{{ r.email || "-" }}</td>
            <td>{{ r.isPrimary ? "بله" : "" }}</td><td><span :class="r.isActive ? 'ok' : ''" class="inv-badge">{{ r.isActive ? "فعال" : "غیرفعال" }}</span></td>
            <td><button v-if="canEdit" class="btn btn-sm btn-outline-primary" @click="edit(r)">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal" style="max-width: 560px">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش مخاطب" : "مخاطب جدید" }}</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">مشتری</label><LookupBox v-model="formCustomer" :fetch="searchCustomers" :label="customerLabel" :disabled="!!form.id" @pick="(c: any) => (form.customerId = c ? c.id : null)" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">نام و نام خانوادگی</label><input v-model="form.fullName" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">سمت</label><input v-model="form.jobTitle" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">تلفن</label><input v-model="form.phone" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">ایمیل</label><input v-model="form.email" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6"><label class="form-check"><input v-model="form.isPrimary" class="form-check-input" type="checkbox" /> <span class="form-check-label">مخاطب اصلی</span></label></div>
          <div class="col-md-6"><label class="form-check"><input v-model="form.isActive" class="form-check-input" type="checkbox" /> <span class="form-check-label">فعال</span></label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>
