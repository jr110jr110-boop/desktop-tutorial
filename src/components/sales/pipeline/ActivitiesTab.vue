<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import { getSlActivities, getSlContacts, saveSlActivity } from "@/api/SalesAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize, usePagedList } from "@/composables/fn";
import { fail, ok, today } from "@/composables/inventory";
import { activityKinds, persianOf } from "@/composables/sales";
import { SalesRoles, currentUserId, hasRole } from "@/composables/roles";
import { blankToNull, customerLabel, inquiryLabel, inquirySearch, searchCustomers } from "./pipelineKit";

// Customer activities (calls, meetings, ...). Each can point at an inquiry, a proposal or a service request; the next-action date feeds the follow-up list.
const customer = ref<any>(null);
const filters = ref({ CustomerId: null as number | null, Contains: "" });
const list = usePagedList(getSlActivities, filters);
const form = ref<any>(null);
const formCustomer = ref<any>(null);
const formInquiry = ref<any>(null);
const contacts = ref<any[]>([]);
const busy = ref(false);
const canEdit = hasRole(SalesRoles.activityWriters);
// SALES may edit every activity; a SERVICE user only their own (the API refuses the rest). When the user id cannot be read from the token, the row stays editable and the API decides.
const isSales = hasRole(["SALES"]);
const me = currentUserId();
const canEditRow = (r: any) => canEdit && (isSales || me === null || r.ownerUserId === me);
const searchInquiries = inquirySearch();

function pick(c: any) {
  customer.value = c;
  filters.value.CustomerId = c ? c.id : null;
  list.search();
}
async function loadContacts(customerId: number | null) {
  contacts.value = [];
  if (!customerId) return;
  const [d] = await getSlContacts({ CustomerId: customerId, PageSize: 200 });
  contacts.value = rowsOf(camelize(d));
}
function pickFormCustomer(c: any) {
  form.value.customerId = c ? c.id : null;
  form.value.contactId = null;
  loadContacts(form.value.customerId);
}
function edit(r: any) {
  form.value = r ? { id: r.id, customerId: r.customerId, contactId: r.contactId ?? null, kind: r.kind, activityDate: r.activityDate ? String(r.activityDate).slice(0, 10) : null, subject: r.subject, notes: r.notes || "",
    nextActionDate: r.nextActionDate ? String(r.nextActionDate).slice(0, 10) : null, inquiryId: r.inquiryId ?? null, proposalId: r.proposalId ?? null, serviceRequestId: r.serviceRequestId ?? null }
    : { id: null, customerId: customer.value?.id ?? null, contactId: null, kind: 1, activityDate: today(), subject: "", notes: "", nextActionDate: null, inquiryId: null, proposalId: null, serviceRequestId: null };
  formCustomer.value = r ? { id: r.customerId, name: r.customerName } : customer.value;
  formInquiry.value = r?.inquiryId ? { id: r.inquiryId, number: "استعلام", title: String(r.inquiryId) } : null;
  loadContacts(form.value.customerId);
}
async function save() {
  const f = form.value;
  if (!f.customerId) return fail({ message: "مشتری را انتخاب کنید." });
  if (!String(f.subject).trim()) return fail({ message: "موضوع فعالیت را بنویسید." });
  busy.value = true;
  const [, error] = await saveSlActivity({ id: f.id, customerId: f.customerId, contactId: f.contactId, kind: f.kind, activityDate: f.activityDate, subject: f.subject.trim(), notes: blankToNull(f.notes),
    nextActionDate: f.nextActionDate, inquiryId: f.inquiryId, proposalId: f.proposalId, serviceRequestId: f.serviceRequestId });
  busy.value = false;
  if (error) return fail(error);
  ok("فعالیت ذخیره شد.");
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
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="موضوع" @keyup.enter="list.search" />
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <button v-if="canEdit" class="btn btn-sm btn-primary" @click="edit(null)">فعالیت جدید</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>تاریخ</th><th>مشتری</th><th>نوع</th><th>موضوع</th><th>اقدام بعدی</th><th>ارتباط</th><th>مسئول</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="8" class="text-center inv-muted">فعالیتی ثبت نشده است.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id">
            <td>{{ persianOf(r.activityDateText, r.activityDate) }}</td><td>{{ r.customerName }}</td><td>{{ activityKinds[r.kind] }}</td><td>{{ r.subject }}</td>
            <td>{{ persianOf(r.nextActionDateText, r.nextActionDate) || "-" }}</td>
            <td>
              <router-link v-if="r.inquiryId" class="me-2" :to="{ name: 'SlInquiry', params: { id: r.inquiryId } }">استعلام</router-link>
              <router-link v-if="r.proposalId" class="me-2" :to="{ name: 'SlProposal', params: { id: r.proposalId } }">پیشنهاد قیمت</router-link>
              <router-link v-if="r.serviceRequestId" :to="{ name: 'SvRequest', params: { id: r.serviceRequestId } }">درخواست خدمات</router-link>
            </td>
            <td>{{ r.ownerName || "-" }}</td>
            <td><button v-if="canEditRow(r)" class="btn btn-sm btn-outline-primary" @click="edit(r)">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal" style="max-width: 680px">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش فعالیت" : "فعالیت جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">مشتری</label><LookupBox v-model="formCustomer" :fetch="searchCustomers" :label="customerLabel" :disabled="!!form.id" @pick="pickFormCustomer" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">مخاطب</label><select v-model="form.contactId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="c in contacts" :key="c.id" :value="c.id">{{ c.fullName }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">نوع</label><select v-model="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in activityKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">تاریخ فعالیت</label><FnDate v-model="form.activityDate" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">تاریخ اقدام بعدی</label><FnDate v-model="form.nextActionDate" /></div>
          <div class="col-12"><label class="form-label small mb-0">موضوع</label><input v-model="form.subject" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">شرح</label><textarea v-model="form.notes" class="form-control form-control-sm" rows="3" /></div>
          <div class="col-12"><label class="form-label small mb-0">استعلام مرتبط</label><LookupBox v-model="formInquiry" :fetch="searchInquiries" :label="inquiryLabel" @pick="(i: any) => (form.inquiryId = i ? i.id : null)" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>
