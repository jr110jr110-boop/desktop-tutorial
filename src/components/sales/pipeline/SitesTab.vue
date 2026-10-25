<script setup lang="ts">
import { onMounted, ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import { getSlSites, saveSlSite } from "@/api/SalesAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { SalesRoles, hasRole } from "@/composables/roles";
import { blankToNull, customerLabel, searchCustomers } from "./pipelineKit";

// Installation / project sites of customers.
const customer = ref<any>(null);
const filters = ref({ CustomerId: null as number | null, Contains: "" });
const list = usePagedList(getSlSites, filters);
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
  form.value = r ? { id: r.id, customerId: r.customerId, title: r.title, address: r.address || "", country: r.country || "", coordinatesText: r.coordinatesText || "" }
    : { id: null, customerId: customer.value?.id ?? null, title: "", address: "", country: "", coordinatesText: "" };
  formCustomer.value = r ? { id: r.customerId, name: r.customerName } : customer.value;
}
async function save() {
  const f = form.value;
  if (!f.customerId) return fail({ message: "مشتری را انتخاب کنید." });
  if (!String(f.title).trim()) return fail({ message: "عنوان سایت را بنویسید." });
  busy.value = true;
  const [, error] = await saveSlSite({ id: f.id, customerId: f.customerId, title: f.title.trim(), address: blankToNull(f.address), country: blankToNull(f.country), coordinatesText: blankToNull(f.coordinatesText) });
  busy.value = false;
  if (error) return fail(error);
  ok("سایت ذخیره شد.");
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
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="عنوان سایت" @keyup.enter="list.search" />
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <button v-if="canEdit" class="btn btn-sm btn-primary" @click="edit(null)">سایت جدید</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>مشتری</th><th>عنوان</th><th>نشانی</th><th>کشور</th><th>مختصات</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="6" class="text-center inv-muted">سایتی ثبت نشده است.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id">
            <td>{{ r.customerName }}</td><td>{{ r.title }}</td><td>{{ r.address || "-" }}</td><td>{{ r.country || "-" }}</td><td class="inv-ltr">{{ r.coordinatesText || "-" }}</td>
            <td><button v-if="canEdit" class="btn btn-sm btn-outline-primary" @click="edit(r)">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal" style="max-width: 560px">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش سایت" : "سایت جدید" }}</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">مشتری</label><LookupBox v-model="formCustomer" :fetch="searchCustomers" :label="customerLabel" :disabled="!!form.id" @pick="(c: any) => (form.customerId = c ? c.id : null)" /></div>
          <div class="col-md-8"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">کشور</label><input v-model="form.country" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">نشانی</label><input v-model="form.address" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">مختصات</label><input v-model="form.coordinatesText" class="form-control form-control-sm inv-ltr" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>
