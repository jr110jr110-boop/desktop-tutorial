<script setup lang="ts">
import { ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import { getSlContacts, saveSlInquiry } from "@/api/SalesAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";
import { fail, ok, today } from "@/composables/inventory";
import { productTypes } from "@/composables/sales";
import { attachmentList, blankToNull, customerLabel, numOrNull, openDoc, searchCustomers, uploadDoc } from "./pipelineKit";

// Inquiry register form (create / edit). Attachments go through the existing document upload; their ids are sent comma separated.
const props = defineProps({ row: { type: Object, default: null } });
const emit = defineEmits(["close", "saved"]);

const r = props.row;
const day = (v: any) => (v ? String(v).slice(0, 10) : null);
const f = ref<any>({
  id: r?.id ?? null, receivedDate: day(r?.receivedDate) || today(), customerId: r?.customerId ?? null, contactId: r?.contactId ?? null, channel: r?.channel || "", customerReference: r?.customerReference || "",
  title: r?.title || "", productType: r?.productType ?? 9, quantity: r?.quantity ?? 1, requiredDeliveryDate: day(r?.requiredDeliveryDate), proposalDueDate: day(r?.proposalDueDate),
  note: r?.note || "", rowVersion: r?.rowVersion ?? null,
});
const customer = ref<any>(r ? { id: r.customerId, name: r.customerName } : null);
const attachments = ref<string[]>(attachmentList(r?.attachmentIds));
const contacts = ref<any[]>([]);
const busy = ref(false);

async function loadContacts() {
  contacts.value = [];
  if (!f.value.customerId) return;
  const [d] = await getSlContacts({ CustomerId: f.value.customerId, PageSize: 200 });
  contacts.value = rowsOf(camelize(d));
}
function pickCustomer(c: any) {
  f.value.customerId = c ? c.id : null;
  f.value.contactId = null;
  loadContacts();
}
async function upload(e: Event) {
  const input = e.target as HTMLInputElement;
  const files = Array.from(input.files || []);
  for (const file of files) {
    const id = await uploadDoc(file);
    if (id) attachments.value.push(id);
  }
  input.value = "";
}
async function save() {
  const v = f.value;
  if (!v.customerId) return fail({ message: "مشتری را انتخاب کنید." });
  if (!String(v.title).trim()) return fail({ message: "عنوان استعلام را بنویسید." });
  const qty = numOrNull(v.quantity);
  if (!qty || qty <= 0) return fail({ message: "تعداد باید بیشتر از صفر باشد." });
  busy.value = true;
  const [d, error] = await saveSlInquiry({
    id: v.id, receivedDate: v.receivedDate, customerId: v.customerId, contactId: v.contactId, channel: blankToNull(v.channel), customerReference: blankToNull(v.customerReference), title: v.title.trim(),
    productType: v.productType, quantity: qty, requiredDeliveryDate: v.requiredDeliveryDate, proposalDueDate: v.proposalDueDate, attachmentIds: attachments.value.join(",") || null, note: blankToNull(v.note), rowVersion: v.rowVersion,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("استعلام ذخیره شد.");
  emit("saved", d);
}
if (r) loadContacts();
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal" style="max-width: 820px">
      <h2 class="inv-title mb-2">{{ r ? "ویرایش استعلام " + r.number : "ثبت استعلام جدید" }}</h2>
      <div class="row g-2">
        <div class="col-md-4"><label class="form-label small mb-0">مشتری</label><LookupBox v-model="customer" :fetch="searchCustomers" :label="customerLabel" @pick="pickCustomer" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">مخاطب</label><select v-model="f.contactId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="c in contacts" :key="c.id" :value="c.id">{{ c.fullName }}</option></select></div>
        <div class="col-md-4"><label class="form-label small mb-0">تاریخ دریافت</label><FnDate v-model="f.receivedDate" /></div>
        <div class="col-md-8"><label class="form-label small mb-0">عنوان</label><input v-model="f.title" class="form-control form-control-sm" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">نوع محصول</label><select v-model="f.productType" class="form-select form-select-sm"><option v-for="(t, k) in productTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-3"><label class="form-label small mb-0">تعداد</label><input v-model="f.quantity" class="form-control form-control-sm inv-ltr" inputmode="decimal" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">کانال دریافت</label><input v-model="f.channel" class="form-control form-control-sm" placeholder="ایمیل، نامه، مناقصه..." /></div>
        <div class="col-md-3"><label class="form-label small mb-0">تحویل درخواستی</label><FnDate v-model="f.requiredDeliveryDate" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">مهلت ارسال پیشنهاد</label><FnDate v-model="f.proposalDueDate" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">شماره ارجاع مشتری</label><input v-model="f.customerReference" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">پیوست‌ها</label><input class="form-control form-control-sm" type="file" multiple @change="upload" /></div>
        <div v-if="attachments.length" class="col-12 d-flex flex-wrap gap-2">
          <span v-for="(a, i) in attachments" :key="a" class="inv-badge info">
            <a href="#" @click.prevent="openDoc(a, 'attachment-' + (i + 1))">پیوست {{ i + 1 }}</a>
            <a href="#" class="ms-1 text-danger" @click.prevent="attachments.splice(i, 1)">حذف</a>
          </span>
        </div>
        <div class="col-12"><label class="form-label small mb-0">یادداشت</label><textarea v-model="f.note" class="form-control form-control-sm" rows="2" /></div>
      </div>
      <div class="d-flex gap-2 justify-content-end mt-3">
        <button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
      </div>
    </div>
  </div>
</template>
