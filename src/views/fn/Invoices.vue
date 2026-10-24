<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import ContractPick from "@/components/fn/ContractPick.vue";
import InvoicePrint from "@/components/fn/print/InvoicePrint.vue";
import QPager from "@/components/quality/QPager.vue";
import { docAction, draftInvoicesFromMilestones, getCustomers, getSalesInvoice, getSalesInvoices, invoiceAction, saveSalesInvoice } from "@/api/FnAPIs";
import { badgeOfDoc, docStatuses, invoiceTypes, money, parseAmount, printSheet, usePagedList } from "@/composables/fn";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { contractLabel } from "@/composables/projectControl";
import { FnRoles, hasRole } from "@/composables/roles";

// Sales invoices, progress statements (milestone based) and credit notes. Approval posts the voucher and needs another person than the creator.
const filters = ref({ Status: null as number | null, CustomerId: null as number | null, Type: null as number | null, OpenOnly: false, Contains: "" });
const list = usePagedList(getSalesInvoices, filters);
const customers = ref<any[]>([]);
const form = ref<any>(null);
const printed = ref<any>(null);
const busy = ref(false);
const canWrite = hasRole(FnRoles.accountants);
const originals = ref<any[]>([]);

const lineAmount = (l: any) => Math.max(0, (parseAmount(l.quantity) || 0) * (parseAmount(l.unitPrice) || 0) - (parseAmount(l.discount) || 0));
const subtotal = computed(() => (form.value ? form.value.lines.reduce((s: number, l: any) => s + lineAmount(l), 0) : 0));
const blankLine = () => ({ description: "", itemCode: "", quantity: "1", unit: "", unitPrice: "", discount: "", vatExempt: false });

function newInvoice() {
  form.value = { id: null, date: today(), dueDate: null, customerId: null, contractId: null, type: 2, creditedInvoiceId: null, description: "", advanceRecovery: "", useDefaultDeductionRates: true, performanceRetentionPercent: "", insuranceRetentionPercent: "", withholdingPercent: "", lines: [blankLine()] };
  originals.value = [];
}

async function edit(row: any) {
  const [d, error] = await getSalesInvoice(row.id);
  if (error) return fail(error);
  form.value = {
    id: d.id, date: String(d.date).slice(0, 10), dueDate: d.dueDate ? String(d.dueDate).slice(0, 10) : null, customerId: d.customerId, contractId: d.contractId, milestoneId: d.milestoneId, type: d.type, creditedInvoiceId: d.creditedInvoiceId,
    description: d.description || "", advanceRecovery: d.advanceRecovery ? String(d.advanceRecovery) : "", useDefaultDeductionRates: false,
    performanceRetentionPercent: String(d.performanceRetentionPercent ?? ""), insuranceRetentionPercent: String(d.insuranceRetentionPercent ?? ""), withholdingPercent: String(d.withholdingPercent ?? ""),
    lines: d.lines.map((l: any) => ({ description: l.description, itemCode: l.itemCode || "", quantity: String(l.quantity), unit: l.unit || "", unitPrice: String(l.unitPrice), discount: l.discount ? String(l.discount) : "", vatExempt: l.vatExempt })),
  };
  loadOriginals();
}

async function loadOriginals() {
  const f = form.value;
  originals.value = [];
  if (f.type !== 6 || !f.customerId) return;
  const [d] = await getSalesInvoices({ CustomerId: f.customerId, Status: 2, PageSize: 200 });
  originals.value = (Array.isArray(d) ? d : d?.data || []).filter((x: any) => x.type !== 6 && x.openAmount > 0);
}

async function save() {
  const f = form.value;
  if (!f.customerId) return fail("مشتری را انتخاب کنید.");
  if (f.type === 6 && !f.creditedInvoiceId) return fail("فاکتور اصلی اعلامیه بستانکار را انتخاب کنید.");
  const lines = f.lines.filter((l: any) => String(l.description).trim() || parseAmount(l.unitPrice));
  if (!lines.length) return fail("حداقل یک ردیف لازم است.");
  if (lines.some((l: any) => !String(l.description).trim())) return fail("شرح همه ردیف‌ها را وارد کنید.");
  const release = Number(f.type) === 7; // a retention release has no deductions and no VAT
  const pct = (v: string) => (String(v).trim() === "" ? null : parseAmount(v));
  busy.value = true;
  const [, error] = await saveSalesInvoice({
    id: f.id, date: f.date, dueDate: f.dueDate, customerId: f.customerId, contractId: f.contractId, milestoneId: f.milestoneId || null, type: Number(f.type), creditedInvoiceId: f.type === 6 ? f.creditedInvoiceId : null,
    description: f.description || null, advanceRecovery: release ? 0 : parseAmount(f.advanceRecovery) || 0, useDefaultDeductionRates: release ? false : f.useDefaultDeductionRates,
    performanceRetentionPercent: release || f.useDefaultDeductionRates ? null : pct(f.performanceRetentionPercent), insuranceRetentionPercent: release || f.useDefaultDeductionRates ? null : pct(f.insuranceRetentionPercent), withholdingPercent: release || f.useDefaultDeductionRates ? null : pct(f.withholdingPercent),
    lines: lines.map((l: any, i: number) => ({ rowNumber: i + 1, description: l.description, itemCode: l.itemCode || null, quantity: parseAmount(l.quantity) || 0, unit: l.unit || null, unitPrice: parseAmount(l.unitPrice) || 0, discount: parseAmount(l.discount) || 0, vatExempt: release ? true : !!l.vatExempt })),
  });
  busy.value = false;
  if (error) return fail(error);
  ok("فاکتور ذخیره شد.");
  form.value = null;
  list.load();
}

async function act(row: any, action: string) {
  if (action === "cancel" && !confirm("پیش‌نویس باطل شود؟")) return;
  busy.value = true;
  const [, error] = await invoiceAction({ id: row.id, action });
  busy.value = false;
  if (error) return fail(error);
  ok(action === "approve" ? "فاکتور تایید و سند حسابداری ثبت شد." : "فاکتور باطل شد.");
  list.load();
}

async function draftFromMilestones() {
  busy.value = true;
  const [d, error] = await draftInvoicesFromMilestones();
  busy.value = false;
  if (error) return fail(error);
  ok(`${d?.drafted ?? 0} پیش‌نویس از مراحل پرداخت قرارداد ساخته شد.`);
  if (d?.skipped?.length) fail(d.skipped.slice(0, 5).join("، "));
  list.search();
}

async function print(row: any) {
  const [d, error] = await getSalesInvoice(row.id);
  if (error) return fail(error);
  printed.value = d;
  await nextTick();
  printSheet();
}

onMounted(async () => {
  const [c] = await getCustomers({ PageSize: 200 });
  customers.value = (Array.isArray(c) ? c : c?.data || []).filter((x: any) => x.isActive);
  list.load();
});
</script>

<template>
  <div class="inv-page">
    <InvoicePrint v-if="printed" :inv="printed" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">فاکتورهای فروش و صورت‌وضعیت‌ها</h1>
        <div v-if="canWrite" class="d-flex gap-2">
          <button :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="draftFromMilestones">پیش‌نویس از مراحل پرداخت قرارداد</button>
          <button class="btn btn-sm btn-primary" @click="newInvoice">فاکتور جدید</button>
        </div>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in docStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.Type" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="(t, k) in invoiceTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.CustomerId" class="form-select form-select-sm"><option :value="null">همه مشتریان</option><option v-for="c in customers" :key="c.id" :value="c.id">{{ c.name }}</option></select>
        <label class="form-check small mb-0"><input v-model="filters.OpenOnly" class="form-check-input" type="checkbox" /> فقط مانده‌دار</label>
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا مشتری" @keydown.enter="list.search" />
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>تاریخ</th><th>نوع</th><th>مشتری</th><th>پروژه</th><th>جمع کل</th><th>قابل دریافت</th><th>دریافت‌شده</th><th>مانده</th><th>سررسید</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="12" class="text-center inv-muted">فاکتوری نیست.</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id">
              <td class="inv-ltr">{{ r.number }}</td><td>{{ jalali(r.date) }}</td><td>{{ invoiceTypes[r.type] }}</td><td>{{ r.customerName }}</td><td>{{ r.contractId ? contractLabel(r.contractId) : "" }}</td>
              <td class="fn-num">{{ money(r.grossAmount) }}</td><td class="fn-num">{{ money(r.receivableAmount) }}</td><td class="fn-num">{{ money(r.paidAmount) }}</td><td class="fn-num">{{ money(r.openAmount) }}</td>
              <td>{{ r.dueDate ? jalali(r.dueDate) : "" }}</td>
              <td><span :class="badgeOfDoc(r.status)" class="inv-badge">{{ docStatuses[r.status] }}</span></td>
              <td class="text-nowrap">
                <button class="btn btn-sm btn-link p-0 me-2" @click="print(r)">چاپ</button>
                <template v-if="canWrite && r.status === 0">
                  <button class="btn btn-sm btn-outline-secondary me-1" @click="edit(r)">ویرایش</button>
                  <button class="btn btn-sm btn-success me-1" :disabled="busy" @click="act(r, 'approve')">تایید</button>
                  <button class="btn btn-sm btn-outline-danger" :disabled="busy" @click="act(r, 'cancel')">ابطال</button>
                </template>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش پیش‌نویس فاکتور" : "فاکتور جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-3"><label class="form-label small mb-0">نوع</label><select v-model="form.type" class="form-select form-select-sm" @change="loadOriginals"><option v-for="(t, k) in invoiceTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">تاریخ</label><FnDate v-model="form.date" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">سررسید</label><FnDate v-model="form.dueDate" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">مشتری *</label><select v-model="form.customerId" class="form-select form-select-sm" @change="loadOriginals"><option :value="null">انتخاب...</option><option v-for="c in customers" :key="c.id" :value="c.id">{{ c.name }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">پروژه / قرارداد</label><ContractPick v-model="form.contractId" /></div>
          <div v-if="form.type === 6" class="col-md-6"><label class="form-label small mb-0">فاکتور اصلی *</label><select v-model="form.creditedInvoiceId" class="form-select form-select-sm"><option :value="null">انتخاب...</option><option v-for="o in originals" :key="o.id" :value="o.id">{{ o.number }} - مانده {{ money(o.openAmount) }}</option></select></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="form.description" class="form-control form-control-sm" /></div>
        </div>
        <div class="table-responsive mt-2">
          <table class="table table-sm fn-lines">
            <thead><tr><th>شرح *</th><th>کد کالا / خدمت</th><th>مقدار</th><th>واحد</th><th>فی (ریال)</th><th>تخفیف (ریال)</th><th>معاف از ارزش افزوده</th><th>مبلغ</th><th></th></tr></thead>
            <tbody>
              <tr v-for="(l, i) in form.lines" :key="i">
                <td><input v-model="l.description" class="form-control form-control-sm" /></td>
                <td><input v-model="l.itemCode" class="form-control form-control-sm inv-ltr" /></td>
                <td><input v-model="l.quantity" class="form-control form-control-sm fn-num" style="width: 5rem" /></td>
                <td><input v-model="l.unit" class="form-control form-control-sm" style="width: 5rem" /></td>
                <td><input v-model="l.unitPrice" class="form-control form-control-sm fn-num" /></td>
                <td><input v-model="l.discount" class="form-control form-control-sm fn-num" /></td>
                <td class="text-center"><input v-model="l.vatExempt" type="checkbox" class="form-check-input" /></td>
                <td class="fn-num">{{ money(lineAmount(l)) }}</td>
                <td><button class="btn btn-sm btn-link text-danger p-0" :disabled="form.lines.length < 2" @click="form.lines.splice(i, 1)">حذف</button></td>
              </tr>
            </tbody>
            <tfoot><tr><td colspan="7"><button class="btn btn-sm btn-outline-secondary" @click="form.lines.push(blankLine())">ردیف جدید</button></td><td class="fn-num fn-total">{{ money(subtotal) }}</td><td></td></tr></tfoot>
          </table>
        </div>
        <div v-if="[2, 3].includes(form.type)" class="row g-2 align-items-end">
          <div class="col-12"><label class="form-check small"><input v-model="form.useDefaultDeductionRates" class="form-check-input" type="checkbox" /> درصد کسورات از جدول نرخ‌ها گرفته شود</label></div>
          <template v-if="!form.useDefaultDeductionRates">
            <div class="col-md-3"><label class="form-label small mb-0">حسن انجام کار (٪)</label><input v-model="form.performanceRetentionPercent" class="form-control form-control-sm fn-num" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">سپرده بیمه (٪)</label><input v-model="form.insuranceRetentionPercent" class="form-control form-control-sm fn-num" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">مالیات تکلیفی (٪)</label><input v-model="form.withholdingPercent" class="form-control form-control-sm fn-num" /></div>
          </template>
          <div class="col-md-3"><label class="form-label small mb-0">کسر پیش‌دریافت (ریال)</label><input v-model="form.advanceRecovery" class="form-control form-control-sm fn-num" /></div>
        </div>
        <p v-if="form.type === 7" class="inv-muted small mt-2">آزادسازی حسن انجام کار: فروش جدید نیست؛ کسورات و مالیات ارزش افزوده ندارد (مالیات در فاکتور اصلی محاسبه شده) و مبلغ آن نباید از حسن انجام کار نگهداری‌شده این مشتری بیشتر باشد.</p>
        <p class="inv-muted small mt-2 mb-0">مالیات ارزش افزوده و کسورات هنگام ذخیره از جدول نرخ‌ها محاسبه می‌شود. تایید فاکتور باید توسط شخصی غیر از ثبت‌کننده انجام شود.</p>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره پیش‌نویس</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
