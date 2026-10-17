<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import PrintForm from "@/components/print/PrintForm.vue";
import { evaluateSupplier, getSupplierEvaluations, getSuppliers, saveSupplierProfile } from "@/api/ProcurementAPIs";
import { fail, jalali, ok, printPage, today } from "@/composables/inventory";
import { evaluationKinds, evaluationQuestions, evaluationScore, gradeOfScore, isUnapproved, supplierGrades } from "@/composables/procurement";
import { hasRole, ProcurementRoles } from "@/composables/roles";

// Approved vendor list: grade A-D, blocked, warnings and performance; profile edit and the evaluation form.
const rows = ref<any[]>([]);
const contains = ref("");
const show = ref<"all" | "approved" | "unapproved">("all");
const profile = ref<any>(null);
const evalForm = ref<any>(null);
const history = ref<{ supplier: any; items: any[] } | null>(null);
const printKind = ref<"" | "eval" | "approved" | "unapproved">("");
const printEval = ref<any>(null);
const busy = ref(false);
const canProfile = hasRole(ProcurementRoles.commercial);
const canEvaluate = hasRole([...ProcurementRoles.commercial, ...ProcurementRoles.technical]);

const shown = computed(() => rows.value.filter((s) => (show.value === "all" ? true : show.value === "unapproved" ? isUnapproved(s) : !isUnapproved(s))));
const approved = computed(() => rows.value.filter((s) => !isUnapproved(s) && s.grade !== 0));
const unapproved = computed(() => rows.value.filter(isUnapproved));
const liveScore = computed(() => (evalForm.value ? evaluationScore(evalForm.value.lines) : null));
const liveGrade = computed(() => gradeOfScore(liveScore.value));

async function load() {
  const [data, error] = await getSuppliers(contains.value);
  if (error) return fail(error);
  rows.value = Array.isArray(data) ? data : data?.data || [];
}

function editProfile(s: any) {
  profile.value = { providerId: s.providerId, providerName: s.providerName, categories: s.categories || "", isStrategic: !!s.isStrategic, blocked: !!s.blocked, blockReason: s.blockReason || "", sepidarCode: s.sepidarCode || "", nationalId: s.nationalId || "", economicCode: s.economicCode || "" };
}

async function saveProfile() {
  if (profile.value.blocked && !String(profile.value.blockReason).trim()) return fail("علت مسدود شدن را بنویسید.");
  busy.value = true;
  const [, error] = await saveSupplierProfile(profile.value);
  busy.value = false;
  if (error) return fail(error);
  profile.value = null;
  ok("مشخصات تامین‌کننده ذخیره شد.");
  load();
}

function newEvaluation(s: any) {
  evalForm.value = { providerId: s.providerId, providerName: s.providerName, kind: s.grade === 0 ? 1 : 3, evaluationDate: today(), purchaseOrderId: null, notes: "", lines: [] };
  loadQuestions();
}

function loadQuestions() {
  evalForm.value.lines = evaluationQuestions[evalForm.value.kind].map((q) => ({ question: q.question, weight: q.weight, score: null as number | null, na: false }));
}

async function saveEvaluation() {
  const f = evalForm.value;
  const lines = f.lines.map((l: any) => ({ question: l.question, weight: Number(l.weight), score: l.na || l.score === null || l.score === "" ? null : Number(l.score) }));
  if (lines.some((l: any) => l.weight <= 0)) return fail("وزن هر پرسش باید بیشتر از صفر باشد.");
  if (lines.some((l: any) => l.score !== null && (l.score < 0 || l.score > 100))) return fail("امتیاز هر پرسش بین ۰ تا ۱۰۰ است.");
  if (!lines.some((l: any) => l.score !== null)) return fail("دست‌کم یک پرسش باید امتیاز داشته باشد.");
  busy.value = true;
  const [data, error] = await evaluateSupplier({ providerId: f.providerId, kind: f.kind, evaluationDate: f.evaluationDate, purchaseOrderId: f.purchaseOrderId, notes: f.notes, lines });
  busy.value = false;
  if (error) return fail(error);
  evalForm.value = null;
  ok(`ارزیابی ثبت شد: امتیاز ${data?.score ?? ""} - درجه ${supplierGrades[data?.grade] ?? ""}`);
  load();
}

async function openHistory(s: any) {
  const [data, error] = await getSupplierEvaluations(s.providerId);
  if (error) return fail(error);
  history.value = { supplier: s, items: Array.isArray(data) ? data : data?.data || [] };
}

function printSheet(e: any) {
  printEval.value = e;
  printKind.value = "eval";
  nextTick(printPage);
}

function printList(kind: "approved" | "unapproved") {
  printKind.value = kind;
  nextTick(printPage);
}

onMounted(load);
</script>

<template>
  <div class="inv-page">
    <PrintForm
      v-if="printKind === 'eval' && printEval"
      title="برگه امتیازدهی نهایی تامین‌کننده"
      form-code="FO-PO-007"
      :fields="[
        { label: 'تامین‌کننده', value: history?.supplier.providerName || printEval.providerName },
        { label: 'نوع ارزیابی', value: evaluationKinds[printEval.kind] },
        { label: 'تاریخ ارزیابی', value: jalali(printEval.evaluationDate) },
        { label: 'امتیاز نهایی', value: printEval.score },
        { label: 'درجه', value: supplierGrades[printEval.grade] },
      ]"
      :notes="printEval.notes ? [{ label: 'توضیح', value: printEval.notes }] : []"
      :signatures="[
        { title: 'ارزیاب', name: printEval.evaluatorName, date: jalali(printEval.evaluationDate) },
        { title: 'مدیر بازرگانی' },
        { title: 'معاونت PMO' },
      ]"
      distribution="توزیع نسخ: ۱- بازرگانی ۲- پرونده تامین‌کننده">
      <table>
        <thead><tr><th>ردیف</th><th>پرسش</th><th>وزن (W)</th><th>امتیاز (S)</th><th>S × W</th></tr></thead>
        <tbody>
          <tr v-for="(l, i) in printEval.lines" :key="i">
            <td>{{ i + 1 }}</td><td>{{ l.question }}</td><td>{{ l.weight }}</td>
            <td>{{ l.score === null || l.score === undefined ? "N/A" : l.score }}</td>
            <td>{{ l.score === null || l.score === undefined ? "-" : l.score * l.weight }}</td>
          </tr>
        </tbody>
      </table>
      <p class="mt-2">امتیاز نهایی = مجموع (S × W) تقسیم بر مجموع W پرسش‌های پاسخ‌داده‌شده.</p>
    </PrintForm>

    <PrintForm
      v-if="printKind === 'approved' || printKind === 'unapproved'"
      :title="printKind === 'approved' ? 'فهرست تامین‌کنندگان تاییدشده' : 'فهرست تامین‌کنندگان تاییدنشده'"
      :form-code="printKind === 'approved' ? 'FO-PO-008' : 'FO-PO-009'"
      :fields="[{ label: 'تاریخ', value: jalali(today()) }, { label: 'تعداد', value: (printKind === 'approved' ? approved : unapproved).length }]"
      :signatures="[{ title: 'تهیه‌کننده (بازرگانی)' }, { title: 'تاییدکننده (معاونت PMO)' }]"
      distribution="توزیع نسخ: ۱- بازرگانی ۲- کنترل کیفیت ۳- انبار">
      <table>
        <thead>
          <tr>
            <th>ردیف</th><th>تامین‌کننده</th><th>گروه کالا / خدمت</th><th>درجه</th><th>امتیاز</th><th>آخرین ارزیابی</th><th>ارزیابی بعدی</th>
            <th v-if="printKind === 'unapproved'">علت</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(s, i) in printKind === 'approved' ? approved : unapproved" :key="s.providerId">
            <td>{{ i + 1 }}</td><td>{{ s.providerName }}</td><td>{{ s.categories }}</td><td>{{ supplierGrades[s.grade] }}</td><td>{{ s.score }}</td>
            <td>{{ jalali(s.evaluatedAt) }}</td><td>{{ jalali(s.nextEvaluation) }}</td>
            <td v-if="printKind === 'unapproved'">{{ s.blocked ? s.blockReason || "مسدود" : "درجه D" }}</td>
          </tr>
        </tbody>
      </table>
    </PrintForm>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">لیست تامین‌کنندگان تاییدشده</h1>
        <div class="d-flex gap-2">
          <button class="btn btn-sm btn-outline-secondary" @click="printList('approved')">چاپ لیست تاییدشده</button>
          <button class="btn btn-sm btn-outline-secondary" @click="printList('unapproved')">چاپ لیست تاییدنشده</button>
        </div>
      </div>
      <div class="inv-filters mb-3">
        <input v-model="contains" class="form-control form-control-sm" placeholder="نام تامین‌کننده" @keydown.enter="load" />
        <select v-model="show" class="form-select form-select-sm">
          <option value="all">همه</option>
          <option value="approved">تاییدشده</option>
          <option value="unapproved">تاییدنشده (درجه D یا مسدود)</option>
        </select>
        <button class="btn btn-sm btn-secondary" @click="load">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead>
            <tr><th>تامین‌کننده</th><th>درجه</th><th>امتیاز</th><th>هشدار</th><th>سفارش</th><th>به‌موقع %</th><th>NCR</th><th>مرجوعی</th><th>ارزیابی بعدی</th><th></th></tr>
          </thead>
          <tbody>
            <tr v-if="!shown.length"><td colspan="10" class="text-center inv-muted">موردی نیست.</td></tr>
            <tr v-for="s in shown" :key="s.providerId">
              <td>{{ s.providerName }} <span v-if="s.isStrategic" class="inv-badge">راهبردی</span> <span v-if="s.blocked" class="inv-badge bad" :title="s.blockReason">مسدود</span></td>
              <td><span class="inv-badge" :class="{ ok: s.grade === 1 || s.grade === 2, warn: s.grade === 3 || s.grade === 0, bad: s.grade === 4 }">{{ supplierGrades[s.grade] }}</span></td>
              <td class="num">{{ s.score ?? "-" }}</td>
              <td class="num">{{ s.warnings }}</td>
              <td class="num">{{ s.orders }}</td>
              <td class="num">{{ s.onTimePercent ?? "-" }}</td>
              <td class="num">{{ s.ncrs }}</td>
              <td class="num">{{ s.returns }}</td>
              <td>
                {{ jalali(s.nextEvaluation) }}
                <span v-if="s.evaluationDue" class="inv-badge warn">موعد ارزیابی</span>
              </td>
              <td class="text-nowrap">
                <button v-if="canEvaluate" class="btn btn-sm btn-primary me-1" @click="newEvaluation(s)">ارزیابی</button>
                <button class="btn btn-sm btn-outline-secondary me-1" @click="openHistory(s)">سوابق</button>
                <button v-if="canProfile" class="btn btn-sm btn-outline-secondary" @click="editProfile(s)">مشخصات</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="inv-muted small mb-0">درجه: A از ۸۵، B از ۶۵، C از ۴۵ و کمتر D. تامین‌کننده درجه D خودکار مسدود می‌شود. ارزیابی بعدی ۶ ماه بعد است.</p>
    </div>

    <div v-if="profile" class="inv-modal-backdrop screen-only" @click.self="profile = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">مشخصات {{ profile.providerName }}</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">گروه کالا / خدمت</label><input v-model="profile.categories" class="form-control form-control-sm" /></div>
          <div class="col-6"><label class="form-label small mb-0">کد سپیدار</label><input v-model="profile.sepidarCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-6"><label class="form-label small mb-0">شناسه ملی</label><input v-model="profile.nationalId" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-6"><label class="form-label small mb-0">کد اقتصادی</label><input v-model="profile.economicCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-6 d-flex align-items-end"><label class="small"><input v-model="profile.isStrategic" type="checkbox" /> تامین‌کننده راهبردی</label></div>
          <div class="col-12"><label class="small"><input v-model="profile.blocked" type="checkbox" /> مسدود (خرید از او ممکن نیست)</label></div>
          <div v-if="profile.blocked" class="col-12"><label class="form-label small mb-0">علت مسدود شدن *</label><input v-model="profile.blockReason" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="profile = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="saveProfile">ذخیره</button>
        </div>
      </div>
    </div>

    <div v-if="evalForm" class="inv-modal-backdrop screen-only" @click.self="evalForm = null">
      <div class="inv-modal inv-modal-lg">
        <h2 class="inv-title mb-2">ارزیابی {{ evalForm.providerName }}</h2>
        <div class="row g-2 mb-2">
          <div class="col-6">
            <label class="form-label small mb-0">نوع ارزیابی</label>
            <select v-model="evalForm.kind" class="form-select form-select-sm" @change="loadQuestions">
              <option v-for="(t, k) in evaluationKinds" :key="k" :value="Number(k)">{{ t }}</option>
            </select>
          </div>
          <div class="col-6"><label class="form-label small mb-0">تاریخ</label><input v-model="evalForm.evaluationDate" class="form-control form-control-sm" type="date" /></div>
        </div>
        <table class="table table-sm table-bordered">
          <thead><tr><th>پرسش</th><th style="width: 80px">وزن (W)</th><th style="width: 110px">امتیاز (S) ۰ تا ۱۰۰</th><th style="width: 60px">N/A</th></tr></thead>
          <tbody>
            <tr v-for="(l, i) in evalForm.lines" :key="i">
              <td><input v-model="l.question" class="form-control form-control-sm" /></td>
              <td><input v-model.number="l.weight" class="form-control form-control-sm" type="number" min="0" step="0.5" /></td>
              <td><input v-model.number="l.score" :disabled="l.na" class="form-control form-control-sm" type="number" min="0" max="100" /></td>
              <td class="text-center"><input v-model="l.na" type="checkbox" /></td>
            </tr>
          </tbody>
        </table>
        <div class="mb-2"><label class="form-label small mb-0">توضیح</label><input v-model="evalForm.notes" class="form-control form-control-sm" /></div>
        <div class="small">
          امتیاز نهایی (ΣS·W / ΣW): <b>{{ liveScore ?? "-" }}</b>
          <span class="inv-badge ms-2" :class="{ ok: liveGrade === 1 || liveGrade === 2, warn: liveGrade === 3, bad: liveGrade === 4 }">درجه {{ supplierGrades[liveGrade] }}</span>
          <span v-if="liveGrade === 4" class="text-danger ms-2">با ثبت، تامین‌کننده مسدود می‌شود.</span>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="evalForm = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="saveEvaluation">ثبت ارزیابی</button>
        </div>
      </div>
    </div>

    <div v-if="history" class="inv-modal-backdrop screen-only" @click.self="history = null">
      <div class="inv-modal inv-modal-lg">
        <h2 class="inv-title mb-2">سوابق ارزیابی {{ history.supplier.providerName }}</h2>
        <table class="table table-sm">
          <thead><tr><th>تاریخ</th><th>نوع</th><th>امتیاز</th><th>درجه</th><th>ارزیاب</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!history.items.length"><td colspan="6" class="text-center inv-muted">ارزیابی ثبت نشده است.</td></tr>
            <tr v-for="e in history.items" :key="e.id">
              <td>{{ jalali(e.evaluationDate) }}</td><td>{{ evaluationKinds[e.kind] }}</td><td class="num">{{ e.score }}</td>
              <td>{{ supplierGrades[e.grade] }}</td><td>{{ e.evaluatorName }}</td>
              <td><button class="btn btn-sm btn-outline-secondary" @click="printSheet(e)">چاپ FO-PO-007</button></td>
            </tr>
          </tbody>
        </table>
        <div class="text-end"><button class="btn btn-sm btn-outline-secondary" @click="history = null">بستن</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
