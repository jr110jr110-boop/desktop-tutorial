<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import ExcelRows, { type ImportField } from "@/components/fn/ExcelRows.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import { getSupplierNotes, reconcileSupplierStatement, saveSupplierNote, supplierNoteAction } from "@/api/FnAPIs";
import { getSupplierInvoices, getSuppliers } from "@/api/ProcurementAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { money, parseAmount, yesNo } from "@/composables/fn";
import { settlementStatuses, supplierNoteKinds } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Supplier credit / debit notes (draft -> approved by someone else) and the reconciliation of a supplier's statement with our ledger.
const tab = ref("notes");
const tabs = [{ key: "notes", title: "یادداشت بستانکار و بدهکار خرید" }, { key: "statement", title: "مغایرت‌گیری صورت‌حساب تامین‌کننده" }];
const canWrite = hasRole(FnRoles.accountants);
const busy = ref(false);
const suppliers = ref<any[]>([]);
const invoices = ref<any[]>([]);
const providerId = ref<number | null>(null);
const list = ref<any[]>([]);
const form = ref<any>(null);

async function load() {
  const [d, error] = await getSupplierNotes(providerId.value);
  if (error) return fail(error);
  list.value = d || [];
}
async function loadInvoices() {
  invoices.value = [];
  if (!form.value?.providerId) return;
  const [i] = await getSupplierInvoices({ ProviderId: form.value.providerId, PageSize: 200 });
  invoices.value = i?.data || [];
}
async function save() {
  const f = form.value;
  if (!f.providerId || !f.date || !(parseAmount(f.netAmount) || 0) || !String(f.reason).trim()) return fail("تامین‌کننده، تاریخ، مبلغ و علت را وارد کنید.");
  busy.value = true;
  const [, error] = await saveSupplierNote({ id: f.id || null, kind: Number(f.kind), providerId: f.providerId, supplierInvoiceId: f.supplierInvoiceId || null, noteNumber: f.noteNumber || null, date: f.date, netAmount: parseAmount(f.netAmount), vatAmount: String(f.vatAmount).trim() === "" ? null : parseAmount(f.vatAmount), reason: f.reason });
  busy.value = false;
  if (error) return fail(error);
  ok("یادداشت ثبت شد.");
  form.value = null;
  load();
}
async function act(n: any, action: string) {
  busy.value = true;
  const [, error] = await supplierNoteAction(n.id, action);
  busy.value = false;
  if (error) return fail(error);
  ok(action === "approve" ? "یادداشت تایید و سند صادر شد." : "یادداشت باطل شد.");
  load();
}

// statement reconciliation
const st = ref<any>({ providerId: null, asOf: today(), theirBalance: "", note: "", lines: [] as any[] });
const result = ref<any>(null);
const fields: ImportField[] = [
  { key: "date", label: "تاریخ", words: ["تاریخ"], type: "date", required: true },
  { key: "documentNumber", label: "شماره سند", words: ["شماره سند", "شماره", "سند"], type: "text" },
  { key: "description", label: "شرح", words: ["شرح", "توضیح"], type: "text" },
  { key: "credit", label: "بستانکار", words: ["بستانکار"], type: "number" },
  { key: "debit", label: "بدهکار", words: ["بدهکار"], type: "number" },
];
const lineSum = computed(() => st.value.lines.reduce((s: number, l: any) => s + (Number(l.credit) || 0) - (Number(l.debit) || 0), 0));
async function reconcile() {
  const s = st.value;
  if (!s.providerId || !s.asOf || parseAmount(s.theirBalance) === null) return fail("تامین‌کننده، تاریخ و مانده صورت‌حساب او را وارد کنید.");
  if (!s.lines.length) return fail("ردیف‌های صورت‌حساب را از فایل وارد کنید.");
  busy.value = true;
  const [d, error] = await reconcileSupplierStatement({ providerId: s.providerId, asOf: s.asOf, theirBalance: parseAmount(s.theirBalance), note: s.note || null, lines: s.lines.map((l: any) => ({ date: l.date, documentNumber: l.documentNumber || null, description: l.description || null, credit: Number(l.credit) || 0, debit: Number(l.debit) || 0 })) });
  busy.value = false;
  if (error) return fail(error);
  result.value = d;
  ok("مغایرت‌گیری انجام شد.");
}

onMounted(async () => {
  load();
  const [s] = await getSuppliers();
  suppliers.value = Array.isArray(s) ? s : s?.data || [];
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">کنترل حساب تامین‌کنندگان</h1>
        <button v-if="canWrite && tab === 'notes'" class="btn btn-sm btn-primary" @click="form = { kind: 1, providerId: providerId, supplierInvoiceId: null, noteNumber: '', date: today(), netAmount: '', vatAmount: '', reason: '' }; loadInvoices()">یادداشت جدید</button>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'notes'">
        <div class="inv-filters mb-3"><select v-model="providerId" class="form-select form-select-sm" @change="load"><option :value="null">همه تامین‌کنندگان</option><option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option></select></div>
        <table class="table table-sm">
          <thead><tr><th>شماره</th><th>نوع</th><th>تامین‌کننده</th><th>شماره یادداشت</th><th>تاریخ</th><th>خالص</th><th>مالیات</th><th>جمع</th><th>علت</th><th>وضعیت</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!list.length"><td colspan="11" class="text-center inv-muted">یادداشتی نیست.</td></tr>
            <tr v-for="n in list" :key="n.id">
              <td class="inv-ltr">{{ n.number }}</td><td>{{ supplierNoteKinds[n.kind] }}</td><td>{{ n.providerName }}</td><td class="inv-ltr">{{ n.noteNumber }}</td><td>{{ jalali(n.date) }}</td><td class="fn-num">{{ money(n.netAmount) }}</td><td class="fn-num">{{ money(n.vatAmount) }}</td><td class="fn-num">{{ money(n.totalAmount) }}</td><td>{{ n.reason }}</td>
              <td>{{ settlementStatuses[n.status] }}<span v-if="n.voucherId"> (سند {{ n.voucherId }})</span></td>
              <td v-if="canWrite" class="text-nowrap"><template v-if="n.status === 0"><button :disabled="busy" class="btn btn-sm btn-success me-1" @click="act(n, 'approve')">تایید</button><button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act(n, 'cancel')">ابطال</button></template></td>
            </tr>
          </tbody>
        </table>
        <p class="inv-muted small">تایید باید توسط شخصی غیر از ثبت‌کننده انجام شود. مبلغ یادداشت‌های بستانکار از سقف فاکتور در درخواست پرداخت کم می‌شود.</p>
      </template>

      <template v-else>
        <div class="row g-2 mb-2">
          <div class="col-md-3"><label class="form-label small mb-0">تامین‌کننده *</label><select v-model="st.providerId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">تا تاریخ *</label><FnDate v-model="st.asOf" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">مانده صورت‌حساب تامین‌کننده (ریال؛ مثبت = ما بدهکار) *</label><input v-model="st.theirBalance" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">توضیح</label><input v-model="st.note" class="form-control form-control-sm" /></div>
        </div>
        <ExcelRows :fields="fields" hint="ستون‌ها: تاریخ، شماره سند، شرح، بستانکار (ما را طلبکار می‌داند)، بدهکار. تاریخ‌ها شمسی باشند." @rows="(r: any[]) => { st.lines = r; result = null; }" />
        <p v-if="st.lines.length" class="inv-muted">{{ st.lines.length }} ردیف - جمع خالص ردیف‌ها {{ money(lineSum) }} ریال</p>
        <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-primary mb-3" @click="reconcile">مغایرت‌گیری</button>
        <template v-if="result">
          <p>
            شماره {{ result.number }} - مانده او: <b>{{ money(result.theirBalance) }}</b> - مانده ما: <b>{{ money(result.ourBalance) }}</b> - اختلاف: <b>{{ money(result.difference) }}</b> - توضیح‌داده‌شده: {{ money(result.explained) }} - توضیح‌داده‌نشده: {{ money(result.unexplained) }}
            <span :class="result.reconciled ? 'ok' : 'bad'" class="inv-badge">{{ result.reconciled ? "مطابق" : "مغایر" }}</span>
          </p>
          <h2 class="inv-subtitle">ردیف‌های صورت‌حساب او</h2>
          <table class="table table-sm">
            <thead><tr><th>#</th><th>تاریخ</th><th>شماره سند</th><th>شرح</th><th>بستانکار</th><th>بدهکار</th><th>نتیجه تطبیق</th><th>سند ما</th><th>اختلاف مبلغ</th></tr></thead>
            <tbody>
              <tr v-for="l in result.lines" :key="l.rowNumber" :class="!l.matchedKind ? 'table-warning' : l.amountDifference ? 'table-danger' : ''">
                <td>{{ l.rowNumber }}</td><td>{{ jalali(l.date) }}</td><td class="inv-ltr">{{ l.documentNumber }}</td><td>{{ l.description }}</td><td class="fn-num">{{ money(l.credit) }}</td><td class="fn-num">{{ money(l.debit) }}</td>
                <td>{{ !l.matchedKind ? "بدون تطبیق در دفاتر ما" : l.amountDifference ? "اختلاف مبلغ" : "مطابق" }}</td><td class="inv-ltr">{{ l.matchedKind }} {{ l.matchedNumber }}</td><td class="fn-num">{{ l.amountDifference ? money(l.amountDifference) : "" }}</td>
              </tr>
            </tbody>
          </table>
          <h2 class="inv-subtitle">اقلام ما که در صورت‌حساب او نیست</h2>
          <table class="table table-sm">
            <thead><tr><th>تاریخ</th><th>نوع</th><th>شماره</th><th>بستانکار</th><th>بدهکار</th></tr></thead>
            <tbody>
              <tr v-if="!result.unmatchedOurs.length"><td colspan="5" class="text-center inv-muted">موردی نیست.</td></tr>
              <tr v-for="(o, i) in result.unmatchedOurs" :key="i"><td>{{ jalali(o.date) }}</td><td>{{ o.kind }}</td><td class="inv-ltr">{{ o.number }}</td><td class="fn-num">{{ money(o.credit) }}</td><td class="fn-num">{{ money(o.debit) }}</td></tr>
            </tbody>
          </table>
        </template>
      </template>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">یادداشت بستانکار / بدهکار خرید</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">نوع</label><select v-model="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in supplierNoteKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">تامین‌کننده *</label><select v-model="form.providerId" class="form-select form-select-sm" @change="loadInvoices"><option :value="null">-</option><option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">فاکتور مربوط</label><select v-model="form.supplierInvoiceId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="i in invoices" :key="i.id" :value="i.id">{{ i.invoiceNumber }} ({{ money(i.totalAmount) }})</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">شماره یادداشت</label><input v-model="form.noteNumber" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">تاریخ *</label><FnDate v-model="form.date" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">مبلغ خالص (ریال) *</label><input v-model="form.netAmount" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">مالیات ارزش افزوده (خالی = طبق نرخ)</label><input v-model="form.vatAmount" class="form-control form-control-sm fn-num" /></div>
          <div class="col-12"><label class="form-label small mb-0">علت *</label><input v-model="form.reason" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره پیش‌نویس</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
