<script setup lang="ts">
// PB3: پرونده درخواست. Any number (request, inquiry, order, receipt, inspection, NCR, invoice, payment, petty cash) opens the whole
// chain: every step with status, date, people and the attached files, so the dossier replaces the paper folder. Prices are shown only
// to the roles that may see them. A payment waiting for the finance approval can be approved from here.
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { getDossier } from "@/api/AttachmentAPIs";
import { downloadFile } from "@/api/InventoryAPIs";
import { paymentAction } from "@/api/ProcurementAPIs";
import { fileSizeText } from "@/composables/attachments";
import { fail, jalali, ok, printPage, rial } from "@/composables/inventory";

const route = useRoute();
const router = useRouter();
const number = ref(String(route.query.number || ""));
const dossier = ref<any>(null);
const busy = ref(false);
const searched = ref(false);

const stepsWithRecords = computed<any[]>(() => (dossier.value?.steps || []).filter((s: any) => s.records.length));

const bySupplierNumber = ref(false);

function pick(c: any) {
  number.value = c.number;
  search(c.kind);
}

async function search(kind?: string) {
  const n = number.value.trim();
  if (!kind && bySupplierNumber.value) kind = "SupplierInvoiceNumber";
  if (!n) return fail("شماره را وارد کنید.");
  busy.value = true;
  const [data, error] = await getDossier(n, kind);
  busy.value = false;
  searched.value = true;
  if (error) {
    dossier.value = null;
    return fail(error);
  }
  dossier.value = data;
  router.replace({ query: { number: n } });
}

async function approve(r: any) {
  if (!window.confirm(`درخواست پرداخت ${r.number} تایید شود؟`)) return;
  busy.value = true;
  const [, error] = await paymentAction(r.id, "approve");
  busy.value = false;
  if (error) return fail(error);
  ok("درخواست پرداخت تایید شد.");
  search();
}

onMounted(() => {
  if (number.value) search();
});
</script>

<template>
  <div class="inv-page dossier">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">پرونده درخواست</h1>
        <form class="d-flex gap-2 align-items-center" @submit.prevent="search()">
          <input v-model="number" class="form-control form-control-sm inv-ltr" style="width: 16rem" placeholder="شماره درخواست، استعلام، سفارش، رسید، فاکتور، پرداخت ..." />
          <label class="small mb-0 text-nowrap"><input v-model="bySupplierNumber" type="checkbox" /> شماره فاکتور خود تامین‌کننده</label>
          <button :disabled="busy" class="btn btn-sm btn-primary" type="submit">جستجو</button>
          <button v-if="dossier" class="btn btn-sm btn-outline-secondary" type="button" @click="printPage()">چاپ</button>
        </form>
      </div>
      <div v-if="dossier && dossier.choices && dossier.choices.length" class="mt-2">
        <p class="small mb-1">{{ dossier.foundAs }}</p>
        <button v-for="c in dossier.choices" :key="c.kind + c.number" class="btn btn-sm btn-outline-primary me-2 mb-1" type="button" @click="pick(c)">{{ c.title }} <span class="inv-ltr">{{ c.number }}</span> <span class="inv-muted small">{{ c.description }}</span></button>
      </div>
      <p v-if="!dossier && !searched" class="inv-muted small mb-0">هر شماره‌ای از زنجیره خرید را بنویسید؛ کل پرونده از درخواست تا پرداخت نمایش داده می‌شود.</p>
      <p v-if="!dossier && searched" class="inv-muted small mb-0">پرونده‌ای با این شماره پیدا نشد.</p>
    </div>

    <div v-if="dossier && dossier.choices && dossier.choices.length" class="inv-muted small screen-only"></div>
    <div v-if="dossier && !(dossier.choices && dossier.choices.length)" class="dossier-sheet">
      <div class="dossier-head">
        <h1 class="h5 mb-1">پرونده درخواست {{ dossier.query }}</h1>
        <div class="small">
          {{ dossier.foundAs }} · {{ dossier.stepCount }} مرحله · {{ dossier.recordCount }} سند · {{ dossier.attachmentCount }} پیوست
          · تهیه: {{ dossier.generatedBy }} {{ jalali(dossier.generatedAt, true) }}
        </div>
      </div>

      <p v-if="dossier.hiddenStepCount" class="small inv-muted">{{ dossier.hiddenStepsText }}</p>
      <div v-for="s in stepsWithRecords" :key="s.code" class="inv-card dossier-step">
        <h2 class="inv-title">{{ s.order }}. {{ s.title }}</h2>
        <div v-for="r in s.records" :key="s.code + r.id" class="dossier-record">
          <div class="d-flex flex-wrap gap-3 align-items-baseline">
            <b class="inv-ltr">{{ r.number }}</b>
            <span class="inv-badge">{{ r.status }}</span>
            <span v-if="r.date">{{ jalali(r.date) }}</span>
            <span v-if="r.createdByName" class="small">تهیه: {{ r.createdByName }}</span>
            <span v-if="r.approvedByName" class="small">تایید: {{ r.approvedByName }} {{ jalali(r.approvedAt, true) }}</span>
            <span v-if="r.amount != null" class="small">مبلغ: {{ rial(r.amount) }}</span>
            <router-link v-if="r.route && r.route.startsWith('/')" :to="r.route" class="small screen-only">باز کردن</router-link>
            <button v-if="r.canApprovePayment" :disabled="busy" class="btn btn-sm btn-success screen-only" @click="approve(r)">تایید پرداخت</button>
          </div>
          <div v-if="r.description" class="small inv-muted">{{ r.description }}</div>
          <table v-if="r.attachments.length" class="table table-sm table-bordered mt-1 mb-0">
            <thead><tr><th>نوع</th><th>عنوان</th><th>بازنگری</th><th>فایل</th><th>بارگذاری</th><th>وضعیت</th></tr></thead>
            <tbody>
              <tr v-for="a in r.attachments" :key="a.id + '-' + a.documentId" :class="{ 'text-muted': !a.current }">
                <td>{{ a.kindTitle }}</td>
                <td>{{ a.title }}</td>
                <td class="inv-ltr">{{ a.revision }}</td>
                <td><a href="#" @click.prevent="downloadFile(a.documentId, a.fileName)">{{ a.fileName }}</a> <span class="inv-muted small">{{ fileSizeText(a.sizeBytes) }}</span></td>
                <td class="small">{{ a.uploadedByName }} {{ jalali(a.uploadedAt, true) }}</td>
                <td class="small"><span v-if="a.voided">باطل: {{ a.voidReason }}</span><span v-else-if="a.supersededById">جایگزین‌شده</span><span v-else>معتبر</span></td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" scoped>
.dossier-record + .dossier-record { border-top: 1px dashed #bbb; margin-top: 0.75rem; padding-top: 0.75rem; }
@media print {
  .dossier-step { break-inside: avoid; box-shadow: none; border: 1px solid #999; }
}
</style>
