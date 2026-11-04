<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import Pb6Table from "@/components/common/Pb6Table.vue";
import { addendumAction, archiveDocument, convertBuyOrder, exportProcurementReport, getAddenda, getArchive, getGuarantees, getLegacyBuyOrders, getProcurementReport, getReportCatalog, guaranteeAction, restoreDocument, runGuaranteeAlerts, saveGuarantee } from "@/api/Pb6APIs";
import { fail, ok, printPage, today } from "@/composables/inventory";

// Procurement completion: guarantee register, addenda, archive, the bridge from legacy buy orders, reports with Excel export.
const tab = ref<"guarantees" | "addenda" | "archive" | "legacy" | "reports">("guarantees");
const rows = ref<any[]>([]);
const note = ref("");
const catalog = ref<any[]>([]);
const report = ref<any>(null);
const filter = ref<any>({ report: "requests", dateFrom: "", dateTo: "" });
const gForm = ref<any>({ providerId: null, purchaseOrderId: null, kind: 2, purpose: 1, amount: 0, guaranteeNumber: "", issuer: "", issueDate: today(), expiryDate: today() });
const arc = ref<any>({ ownerType: 1, ownerId: null, reason: "" });
const conv = ref<any>({ itemId: null, unitPrice: null });

const guaranteeStatuses: Record<number, string> = { 0: "فعال", 1: "درخواست آزادسازی", 2: "آزاد شد", 3: "منقضی", 4: "ضبط شد" };
const addendumStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "در جریان تایید", 2: "اعمال شد", 9: "رد شد" };
const ownerTypes: Record<number, string> = { 1: "درخواست خرید", 2: "استعلام", 3: "سفارش خرید", 4: "فاکتور", 5: "درخواست پرداخت" };

const columns = computed(() => {
  if (tab.value === "guarantees") return [{ key: "number", title: "شماره" }, { key: "providerName", title: "تامین‌کننده" }, { key: "amount", title: "مبلغ (ریال)", kind: "money" }, { key: "expiryDate", title: "سررسید", kind: "date" }, { key: "daysToExpiry", title: "روز مانده" }, { key: "statusText", title: "وضعیت" }];
  if (tab.value === "addenda") return [{ key: "number", title: "شماره" }, { key: "purchaseOrderNumber", title: "سفارش" }, { key: "amountBefore", title: "قبل", kind: "money" }, { key: "amountAfter", title: "بعد", kind: "money" }, { key: "statusText", title: "وضعیت" }];
  if (tab.value === "archive") return [{ key: "ownerText", title: "نوع" }, { key: "number", title: "شماره" }, { key: "archiveReason", title: "علت" }, { key: "archivedAt", title: "تاریخ", kind: "date" }, { key: "isArchivedText", title: "وضعیت" }];
  return [{ key: "id", title: "شناسه" }, { key: "title", title: "عنوان" }, { key: "statusTitle", title: "وضعیت" }, { key: "workOrderCode", title: "دستور ساخت" }, { key: "purchaseRequestNumber", title: "درخواست خرید" }];
});

async function load() {
  report.value = null;
  let res: any = [[], null];
  if (tab.value === "guarantees") res = await getGuarantees({});
  else if (tab.value === "addenda") res = await getAddenda();
  else if (tab.value === "archive") res = await getArchive({ PageSize: 100 });
  else if (tab.value === "legacy") res = await getLegacyBuyOrders(true);
  else {
    const [c] = await getReportCatalog();
    catalog.value = c || [];
    return;
  }
  if (res[1]) return fail(res[1]);
  const data = Array.isArray(res[0]) ? res[0] : res[0]?.data || [];
  rows.value = data.map((r: any) => ({ ...r, statusText: (tab.value === "guarantees" ? guaranteeStatuses : addendumStatuses)[r.status], ownerText: ownerTypes[r.ownerType], isArchivedText: r.isArchived ? "بایگانی" : "بازگردانی‌شده" }));
}
function switchTab(t: any) {
  tab.value = t;
  load();
}

async function rowAction(r: any) {
  if (tab.value === "guarantees") {
    const a = prompt("عمل: request-release / release / reject-release / claim");
    if (!a) return;
    const [, e] = await guaranteeAction(r.id, a, note.value);
    if (e) return fail(e);
  } else if (tab.value === "addenda") {
    const a = prompt("عمل: submit / approve / approve-ceo / reject");
    if (!a) return;
    const [, e] = await addendumAction(r.id, a, note.value);
    if (e) return fail(e);
  } else if (tab.value === "archive") {
    const reason = prompt("علت بازگردانی؟");
    if (!reason) return;
    const [, e] = await restoreDocument(r.ownerType, r.ownerId, reason);
    if (e) return fail(e);
  } else if (tab.value === "legacy") {
    const [d, e] = await convertBuyOrder({ buyOrderId: r.id, ...conv.value });
    if (e) return fail(e);
    ok(d?.message || "به درخواست خرید تبدیل شد.");
  }
  ok("انجام شد.");
  load();
}
async function addGuarantee() {
  const [, e] = await saveGuarantee(gForm.value);
  if (e) return fail(e);
  ok("ضمانت‌نامه ثبت شد.");
  load();
}
async function archiveNow() {
  const [, e] = await archiveDocument(arc.value.ownerType, arc.value.ownerId, arc.value.reason);
  if (e) return fail(e);
  ok("بایگانی شد.");
  load();
}
async function alerts() {
  const [d, e] = await runGuaranteeAlerts();
  if (e) return fail(e);
  ok(`هشدارها اجرا شد (${d?.alerted ?? 0}).`);
  load();
}
async function runReport() {
  const [d, e] = await getProcurementReport(filter.value);
  if (e) return fail(e);
  report.value = d;
}
async function exportReport() {
  const e = await exportProcurementReport(filter.value);
  if (e) fail(e);
}
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">تکمیل زنجیره خرید</h1></div>
      <div class="btn-group mb-3 d-print-none">
        <button v-for="t in [['guarantees','ضمانت‌نامه‌ها'],['addenda','الحاقیه‌ها'],['archive','بایگانی'],['legacy','پل سفارش‌های قدیمی'],['reports','گزارش‌ها']]" :key="t[0]" class="btn btn-sm" :class="tab === t[0] ? 'btn-primary' : 'btn-outline-primary'" @click="switchTab(t[0])">{{ t[1] }}</button>
      </div>

      <template v-if="tab !== 'reports'">
        <p v-if="tab === 'legacy'" class="inv-muted">ثبت سفارش خرید قدیمی بسته شده است؛ فقط سفارش‌های قدیمیِ باز را اینجا به درخواست خرید زنجیره جدید تبدیل کنید. کالای ردیف و قیمت واحد را در صورت نیاز بنویسید.</p>
        <div v-if="tab === 'legacy'" class="inv-filters mb-2"><input v-model.number="conv.itemId" type="number" class="form-control form-control-sm inv-ltr" placeholder="شناسه کالا" /><input v-model.number="conv.unitPrice" type="number" class="form-control form-control-sm inv-ltr" placeholder="قیمت واحد (ریال)" /></div>
        <div class="d-print-none mb-2 d-flex gap-2">
          <input v-model="note" class="form-control form-control-sm w-auto" placeholder="توضیح / علت" />
          <button v-if="tab === 'guarantees'" class="btn btn-sm btn-outline-secondary" @click="alerts">اجرای هشدار سررسید</button>
        </div>
        <Pb6Table :columns="columns" :rows="rows" @select="rowAction" empty="موردی نیست." />
        <small class="inv-muted d-print-none">برای انجام عمل روی هر ردیف کلیک کنید.</small>
        <div v-if="tab === 'archive'" class="inv-filters mt-2 d-print-none">
          <select v-model.number="arc.ownerType" class="form-select form-select-sm"><option v-for="(t, k) in ownerTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
          <input v-model.number="arc.ownerId" type="number" class="form-control form-control-sm inv-ltr" placeholder="شناسه سند" />
          <input v-model="arc.reason" class="form-control form-control-sm" placeholder="علت بایگانی" />
          <button class="btn btn-sm btn-primary" @click="archiveNow">بایگانی</button>
        </div>
        <div v-if="tab === 'guarantees'" class="inv-card mt-3 d-print-none">
          <h2 class="h6">ضمانت‌نامه جدید</h2>
          <div class="inv-filters">
            <input v-model.number="gForm.providerId" type="number" class="form-control form-control-sm inv-ltr" placeholder="شناسه تامین‌کننده" />
            <input v-model.number="gForm.purchaseOrderId" type="number" class="form-control form-control-sm inv-ltr" placeholder="شناسه سفارش" />
            <select v-model.number="gForm.kind" class="form-select form-select-sm"><option :value="1">چک</option><option :value="2">ضمانت‌نامه بانکی</option><option :value="3">سفته</option></select>
            <input v-model.number="gForm.amount" type="number" class="form-control form-control-sm inv-ltr" placeholder="مبلغ (ریال)" />
            <input v-model="gForm.guaranteeNumber" class="form-control form-control-sm inv-ltr" placeholder="شماره" />
            <input v-model="gForm.issuer" class="form-control form-control-sm" placeholder="صادرکننده" />
            <input v-model="gForm.expiryDate" type="date" class="form-control form-control-sm" />
            <button class="btn btn-sm btn-primary" @click="addGuarantee">ثبت</button>
          </div>
        </div>
      </template>

      <template v-else>
        <div class="inv-filters mb-2 d-print-none">
          <select v-model="filter.report" class="form-select form-select-sm"><option v-for="c in catalog" :key="c.key" :value="c.key">{{ c.title }}</option></select>
          <input v-model="filter.dateFrom" type="date" class="form-control form-control-sm" />
          <input v-model="filter.dateTo" type="date" class="form-control form-control-sm" />
          <button class="btn btn-sm btn-secondary" @click="runReport">نمایش</button>
          <button class="btn btn-sm btn-outline-success" @click="exportReport">خروجی Excel</button>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
        <template v-if="report">
          <h2 class="h6">{{ report.title }}</h2>
          <Pb6Table :columns="report.columns" :rows="report.rows" />
          <p v-for="(v, k) in report.totals" :key="k" class="small">جمع {{ report.columns.find((c: any) => c.key === k)?.title }}: {{ Number(v).toLocaleString("en-US") }}</p>
        </template>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
