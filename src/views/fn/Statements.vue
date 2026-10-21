<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import QPager from "@/components/quality/QPager.vue";
import { autoMatchStatement, getBankAccounts, getIssuedCheques, getPayments, getReceipts, getReceivedCheques, getStatement, getStatements, importStatement, matchStatementLine } from "@/api/FnAPIs";
import { camelize, money, statementMatches, usePagedList, parseAmount } from "@/composables/fn";
import { cellDate, cellNumber, cellText, readExcel } from "@/composables/fnExcel";
import type { ExcelField, ExcelSheet } from "@/composables/fnExcel";
import { fail, jalali, ok } from "@/composables/inventory";
import { FnRoles, hasRole } from "@/composables/roles";

// Bank statements: import from Excel, automatic matching, and manual matching of each line to a cheque, payment, receipt, fee or interest.
const fields: ExcelField[] = [
  { key: "date", label: "تاریخ *", words: ["تاریخ", "date", "تاریخ تراکنش"] },
  { key: "description", label: "شرح", words: ["شرح", "توضیح", "توضیحات", "description"] },
  { key: "withdrawal", label: "برداشت *", words: ["برداشت", "بدهکار", "withdrawal", "debit"] },
  { key: "deposit", label: "واریز *", words: ["واریز", "بستانکار", "deposit", "credit"] },
  { key: "reference", label: "شماره پیگیری / مرجع", words: ["مرجع", "شماره پیگیری", "پیگیری", "reference"] },
  { key: "balance", label: "مانده", words: ["مانده", "balance"] },
];
const filters = ref({ BankAccountId: null as number | null });
const list = usePagedList(getStatements, filters);
const banks = ref<any[]>([]);
const sheet = ref<ExcelSheet | null>(null);
const header = ref({ bankAccountId: null as number | null, openingBalance: "", closingBalance: "" });
const detail = ref<any>(null);
const matching = ref<null | { line: any; kind: string; targetId: number | null; candidates: any[] }>(null);
const busy = ref(false);
const canWrite = hasRole(FnRoles.treasuryAndAccountants);
const mapped = computed(() => (sheet.value ? fields.filter((f) => sheet.value!.map[f.key] >= 0) : []));

async function pickFile(e: any) {
  const file = e.target.files?.[0];
  e.target.value = "";
  if (!file) return;
  try {
    sheet.value = await readExcel(file, fields);
  } catch (err: any) {
    fail("فایل خوانده نشد. فایل باید xlsx باشد.");
  }
}

function buildRows() {
  const s = sheet.value!;
  return s.rows.map((r) => ({
    date: cellDate(s, r, "date"),
    description: cellText(s, r, "description"),
    withdrawal: cellNumber(s, r, "withdrawal") ?? 0,
    deposit: cellNumber(s, r, "deposit") ?? 0,
    reference: cellText(s, r, "reference"),
    balance: cellNumber(s, r, "balance"),
  }));
}

async function runImport() {
  const s = sheet.value!;
  if (!header.value.bankAccountId) return fail("حساب بانکی را انتخاب کنید.");
  if (s.map.date < 0 || (s.map.withdrawal < 0 && s.map.deposit < 0)) return fail("ستون تاریخ و برداشت / واریز را مشخص کنید.");
  const rows = buildRows();
  if (rows.some((r) => !r.date)) return fail("تاریخ بعضی ردیف‌ها خوانده نشد؛ تاریخ‌ها باید شمسی یا میلادی معتبر باشند.");
  busy.value = true;
  const [, error] = await importStatement({ bankAccountId: header.value.bankAccountId, openingBalance: parseAmount(header.value.openingBalance) || 0, closingBalance: parseAmount(header.value.closingBalance) || 0, rows });
  busy.value = false;
  if (error) return fail(error);
  ok("صورت‌حساب بانکی وارد شد.");
  sheet.value = null;
  list.search();
}

async function open(id: number) {
  const [d, error] = await getStatement(id);
  if (error) return fail(error);
  detail.value = d;
}

async function autoMatch() {
  busy.value = true;
  const [d, error] = await autoMatchStatement(detail.value.id);
  busy.value = false;
  if (error) return fail(error);
  ok(`${d?.matched ?? d?.count ?? ""} ردیف به‌صورت خودکار تطبیق داده شد.`.replace("  ", " "));
  open(detail.value.id);
  list.load();
}

const kindOptions = (line: any) => (line.deposit > 0 ? [["cheque", "چک دریافتی"], ["receipt", "سند دریافت"], ["interest", "سود بانکی"]] : [["cheque", "چک صادره"], ["payment", "سند پرداخت"], ["fee", "کارمزد"]]);

async function startMatch(line: any) {
  matching.value = { line, kind: kindOptions(line)[0][0], targetId: null, candidates: [] };
  await loadCandidates();
}

async function loadCandidates() {
  const m = matching.value!;
  m.targetId = null;
  m.candidates = [];
  const bank = detail.value.bankAccountId;
  let res: any;
  if (m.kind === "cheque") res = m.line.deposit > 0 ? await getReceivedCheques({ PageSize: 200, Kind: 2 }) : await getIssuedCheques({ PageSize: 200, BankAccountId: bank, Status: 1 });
  else if (m.kind === "payment") res = await getPayments({ PageSize: 200, Status: 2, BankAccountId: bank });
  else if (m.kind === "receipt") res = await getReceipts({ PageSize: 200, Status: 2, BankAccountId: bank });
  else return;
  const d = camelize(res[0]);
  const rows: any[] = Array.isArray(d) ? d : d?.data || [];
  const amount = m.line.deposit > 0 ? m.line.deposit : m.line.withdrawal;
  m.candidates = rows
    .map((r) => ({ id: r.id, amount: r.amount ?? r.netAmount ?? r.totalAmount, text: `${r.number || r.serial || r.sayadId} - ${r.payeeName || r.payerName || r.accountHolder || r.customerName || ""}`, date: r.dueDate || r.date }))
    .sort((a, b) => Number(b.amount === amount) - Number(a.amount === amount));
}

async function saveMatch() {
  const m = matching.value!;
  const needsTarget = !["fee", "interest"].includes(m.kind);
  if (needsTarget && !m.targetId) return fail("مورد تطبیق را انتخاب کنید.");
  const [, error] = await matchStatementLine(detail.value.id, m.line.id, m.kind, needsTarget ? m.targetId : null);
  if (error) return fail(error);
  ok("ردیف تطبیق داده شد.");
  matching.value = null;
  open(detail.value.id);
  list.load();
}

onMounted(async () => {
  const [b] = await getBankAccounts({ PageSize: 200 });
  banks.value = (b?.data || []).filter((x: any) => x.isActive);
  list.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">صورت‌حساب بانکی</h1>
        <label v-if="canWrite" class="btn btn-sm btn-outline-primary mb-0">ورود از اکسل<input accept=".xlsx,.xls" class="d-none" type="file" @change="pickFile" /></label>
      </div>
      <div v-if="sheet" class="mb-3">
        <p class="small mb-2">فایل: {{ sheet.fileName }} - {{ sheet.rows.length }} ردیف. ستون هر فیلد را بررسی کنید.</p>
        <div class="row g-2">
          <div v-for="f in fields" :key="f.key" class="col-md-2">
            <label class="form-label small mb-0">{{ f.label }}</label>
            <select v-model="sheet.map[f.key]" class="form-select form-select-sm"><option :value="-1">- ندارد -</option><option v-for="(h, i) in sheet.headers" :key="i" :value="i">{{ h || "ستون " + (i + 1) }}</option></select>
          </div>
        </div>
        <div class="table-responsive mt-2" style="max-height: 180px">
          <table class="table table-sm table-bordered"><thead><tr><th v-for="f in mapped" :key="f.key">{{ f.label }}</th></tr></thead>
            <tbody><tr v-for="(r, i) in sheet.rows.slice(0, 5)" :key="i"><td v-for="f in mapped" :key="f.key">{{ r[sheet.map[f.key]] }}</td></tr></tbody></table>
        </div>
        <div class="row g-2 mt-1 align-items-end">
          <div class="col-md-4"><label class="form-label small mb-0">حساب بانکی *</label><select v-model="header.bankAccountId" class="form-select form-select-sm"><option :value="null">انتخاب...</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">مانده اول دوره (ریال)</label><input v-model="header.openingBalance" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">مانده پایان (ریال)</label><input v-model="header.closingBalance" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-2 text-end"><button :disabled="busy" class="btn btn-sm btn-primary" @click="runImport">ورود صورت‌حساب</button></div>
        </div>
        <p class="inv-muted small mt-1 mb-0">مانده اول + واریزها - برداشت‌ها باید با مانده پایان برابر باشد و هر ردیف فقط یکی از برداشت یا واریز را داشته باشد.</p>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="filters.BankAccountId" class="form-select form-select-sm" @change="list.search"><option :value="null">همه حساب‌ها</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>حساب</th><th>از</th><th>تا</th><th>مانده اول</th><th>مانده پایان</th><th>تطبیق‌شده</th><th>تطبیق‌نشده</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="8" class="text-center inv-muted">صورت‌حسابی نیست.</td></tr>
            <tr v-for="s in list.rows.value" :key="s.id">
              <td>{{ s.bankAccountTitle }}</td><td>{{ jalali(s.fromDate) }}</td><td>{{ jalali(s.toDate) }}</td><td class="fn-num">{{ money(s.openingBalance) }}</td><td class="fn-num">{{ money(s.closingBalance) }}</td>
              <td><span class="inv-badge ok">{{ s.matched }}</span></td><td><span :class="s.unmatched ? 'warn' : 'ok'" class="inv-badge">{{ s.unmatched }}</span></td>
              <td><button class="btn btn-sm btn-outline-primary" @click="open(s.id)">ردیف‌ها</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal wide">
        <div class="inv-toolbar">
          <h2 class="inv-title">{{ detail.bankAccountTitle }} - {{ jalali(detail.fromDate) }} تا {{ jalali(detail.toDate) }}</h2>
          <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="autoMatch">تطبیق خودکار</button>
        </div>
        <div class="table-responsive" style="max-height: 60vh; overflow-y: auto">
          <table class="table table-sm">
            <thead><tr><th>#</th><th>تاریخ</th><th>شرح</th><th>برداشت</th><th>واریز</th><th>مرجع</th><th>مانده</th><th>تطبیق</th><th></th></tr></thead>
            <tbody>
              <tr v-for="l in detail.lines" :key="l.id">
                <td>{{ l.rowNumber }}</td><td>{{ jalali(l.date) }}</td><td>{{ l.description }}</td>
                <td class="fn-num">{{ l.withdrawal ? money(l.withdrawal) : "" }}</td><td class="fn-num">{{ l.deposit ? money(l.deposit) : "" }}</td>
                <td class="inv-ltr">{{ l.reference }}</td><td class="fn-num">{{ l.balance === null || l.balance === undefined ? "" : money(l.balance) }}</td>
                <td><span :class="l.matchKind ? 'ok' : 'warn'" class="inv-badge">{{ statementMatches[l.matchKind] }}</span></td>
                <td><button v-if="canWrite && !l.matchKind" class="btn btn-sm btn-outline-secondary" @click="startMatch(l)">تطبیق دستی</button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="text-end mt-2"><button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button></div>
      </div>
    </div>

    <div v-if="matching" class="inv-modal-backdrop screen-only" @click.self="matching = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">تطبیق ردیف {{ matching.line.rowNumber }} ({{ money(matching.line.deposit || matching.line.withdrawal) }} ریال)</h2>
        <label class="form-label small mb-0">نوع تطبیق</label>
        <select v-model="matching.kind" class="form-select form-select-sm mb-2" @change="loadCandidates"><option v-for="k in kindOptions(matching.line)" :key="k[0]" :value="k[0]">{{ k[1] }}</option></select>
        <template v-if="!['fee', 'interest'].includes(matching.kind)">
          <label class="form-label small mb-0">مورد</label>
          <select v-model="matching.targetId" class="form-select form-select-sm"><option :value="null">انتخاب...</option><option v-for="c in matching.candidates" :key="c.id" :value="c.id">{{ c.text }} - {{ money(c.amount) }} ریال - {{ jalali(c.date) }}</option></select>
        </template>
        <p v-else class="inv-muted small">سند کارمزد یا سود بانکی ثبت می‌شود (فقط حسابدار).</p>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="matching = null">انصراف</button><button class="btn btn-sm btn-primary" @click="saveMatch">تطبیق</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
