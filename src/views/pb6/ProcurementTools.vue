<script setup lang="ts">
import FnDate from "@/components/fn/FnDate.vue";
import { computed, onMounted, ref } from "vue";
import Pb6Table from "@/components/common/Pb6Table.vue";
import Pb6Dialog from "@/components/common/Pb6Dialog.vue";
import { addendumAction, saveAddendum, archiveDocument, convertBuyOrder, exportProcurementReport, getAddenda, getArchive, getGuarantees, getLegacyBuyOrders, getProcurementReport, getReportCatalog, guaranteeAction, restoreDocument, runGuaranteeAlerts, saveGuarantee } from "@/api/Pb6APIs";
import { fail, ok, printPage, today } from "@/composables/inventory";
import { getPurchaseOrderForReceipt } from "@/api/InventoryAPIs";

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

const dlg = ref<{ open: boolean; title: string; options: { value: string; label: string }[]; textLabel: string; textRequired: boolean; row: any }>({ open: false, title: "", options: [], textLabel: "", textRequired: false, row: null });
const guaranteeActions = [{ value: "request-release", label: "درخواست آزادسازی" }, { value: "release", label: "آزادسازی" }, { value: "reject-release", label: "رد آزادسازی" }, { value: "claim", label: "ضبط" }];
const addendumActions = [{ value: "submit", label: "ارسال برای تایید" }, { value: "approve", label: "تایید معاونت PMO" }, { value: "approve-ceo", label: "تایید مدیرعامل" }, { value: "reject", label: "رد" }];

function rowAction(r: any) {
  if (tab.value === "guarantees") dlg.value = { open: true, title: "عمل روی ضمانت‌نامه " + r.number, options: guaranteeActions, textLabel: "توضیح / علت (برای آزادسازی و ضبط الزامی است)", textRequired: false, row: r };
  else if (tab.value === "addenda") dlg.value = { open: true, title: "عمل روی الحاقیه " + r.number, options: addendumActions, textLabel: "توضیح / علت رد", textRequired: false, row: r };
  else if (tab.value === "archive") dlg.value = { open: true, title: "بازگردانی از بایگانی", options: [], textLabel: "علت بازگردانی", textRequired: true, row: r };
  else if (tab.value === "legacy") runRowAction(r, "", "");
}

async function runRowAction(r: any, choice: string, text: string) {
  if (tab.value === "guarantees") {
    const [, e] = await guaranteeAction(r.id, choice, text || note.value);
    if (e) return fail(e);
  } else if (tab.value === "addenda") {
    const [, e] = await addendumAction(r.id, choice, text || note.value);
    if (e) return fail(e);
  } else if (tab.value === "archive") {
    const [, e] = await restoreDocument(r.ownerType, r.ownerId, text);
    if (e) return fail(e);
  } else if (tab.value === "legacy") {
    const [d, e] = await convertBuyOrder({ buyOrderId: r.id, ...conv.value });
    if (e) return fail(e);
    ok(d?.message || "به درخواست خرید تبدیل شد.");
  }
  ok("انجام شد.");
  load();
}
const aForm = ref<any>({ purchaseOrderId: null, reason: "", deliveryDateAfter: "", lines: [] });
async function loadOrderLines() {
  const [o, e] = await getPurchaseOrderForReceipt(aForm.value.purchaseOrderId);
  if (e || !o) return fail(e || "سفارش پیدا نشد.");
  aForm.value.lines = (o.lines || []).map((l: any) => ({ orderLineId: l.id, rowNumber: l.rowNumber, description: l.description, quantityNow: l.quantity, priceNow: l.unitPrice, quantityAfter: l.quantity, unitPriceAfter: l.unitPrice }));
}
async function addAddendum() {
  const dto = {
    purchaseOrderId: aForm.value.purchaseOrderId,
    reason: aForm.value.reason,
    deliveryDateAfter: aForm.value.deliveryDateAfter || null,
    lines: aForm.value.lines.map((l: any) => ({ orderLineId: l.orderLineId, quantityAfter: l.quantityAfter, unitPriceAfter: l.unitPriceAfter })),
  };
  const [, e] = await saveAddendum(dto);
  if (e) return fail(e);
  ok("پیش‌نویس الحاقیه ذخیره شد؛ برای ارسال روی ردیف آن کلیک کنید.");
  aForm.value = { purchaseOrderId: null, reason: "", deliveryDateAfter: "", lines: [] };
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
        <Pb6Dialog v-model="dlg.open" :title="dlg.title" :options="dlg.options" :text-label="dlg.textLabel" :text-required="dlg.textRequired" @confirm="(v: any) => runRowAction(dlg.row, v.choice, v.text)" />
        <small class="inv-muted d-print-none">برای انجام عمل روی هر ردیف کلیک کنید.</small>
        <div v-if="tab === 'archive'" class="inv-filters mt-2 d-print-none">
          <select v-model.number="arc.ownerType" class="form-select form-select-sm"><option v-for="(t, k) in ownerTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
          <input v-model.number="arc.ownerId" type="number" class="form-control form-control-sm inv-ltr" placeholder="شناسه سند" />
          <input v-model="arc.reason" class="form-control form-control-sm" placeholder="علت بایگانی" />
          <button class="btn btn-sm btn-primary" @click="archiveNow">بایگانی</button>
        </div>
        <div v-if="tab === 'addenda'" class="inv-card mt-3 d-print-none">
          <h2 class="h6">الحاقیه جدید</h2>
          <div class="inv-filters mb-2">
            <input v-model.number="aForm.purchaseOrderId" type="number" class="form-control form-control-sm inv-ltr" placeholder="شناسه سفارش" />
            <button class="btn btn-sm btn-outline-secondary" @click="loadOrderLines">بارگذاری ردیف‌ها</button>
            <FnDate v-model="aForm.deliveryDateAfter" placeholder="تاریخ تحویل جدید (اختیاری)" />
            <input v-model="aForm.reason" class="form-control form-control-sm" placeholder="علت الحاقیه" />
          </div>
          <table v-if="aForm.lines.length" class="table table-sm">
            <thead><tr><th>ردیف</th><th>شرح</th><th>مقدار فعلی</th><th>مقدار جدید</th><th>قیمت فعلی</th><th>قیمت جدید</th></tr></thead>
            <tbody>
              <tr v-for="l in aForm.lines" :key="l.orderLineId">
                <td>{{ l.rowNumber }}</td><td>{{ l.description }}</td><td>{{ l.quantityNow }}</td>
                <td><input v-model.number="l.quantityAfter" type="number" class="form-control form-control-sm inv-ltr" /></td>
                <td>{{ l.priceNow }}</td>
                <td><input v-model.number="l.unitPriceAfter" type="number" class="form-control form-control-sm inv-ltr" /></td>
              </tr>
            </tbody>
          </table>
          <button class="btn btn-sm btn-primary" :disabled="!aForm.lines.length" @click="addAddendum">ذخیره پیش‌نویس الحاقیه</button>
          <small class="inv-muted d-block mt-1">سقف تغییر حجم و قیمت نسبت به مقدار امضاشده را سامانه کنترل می‌کند؛ تغییرهای کوچک‌تر از سقف مدیرعامل با خود سامانه تعیین می‌شود.</small>
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
            <FnDate v-model="gForm.expiryDate" />
            <button class="btn btn-sm btn-primary" @click="addGuarantee">ثبت</button>
          </div>
        </div>
      </template>

      <template v-else>
        <div class="inv-filters mb-2 d-print-none">
          <select v-model="filter.report" class="form-select form-select-sm"><option v-for="c in catalog" :key="c.key" :value="c.key">{{ c.title }}</option></select>
          <FnDate v-model="filter.dateFrom" />
          <FnDate v-model="filter.dateTo" />
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
