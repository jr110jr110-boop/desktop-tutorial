<script setup lang="ts">
// «گذار به کنترل» (Package G): the day the quality and configuration control started, what is still legacy, every legacy exemption that was used (never silent) and the two adoption workflows that bring legacy items under control one by one:
// «پذیرش مشخصه‌های نقشه موجود» (engineering writes the list, QA approves) and «پذیرش پیکربندی پروژه موجود» (preparer, one more engineer and one PMO person: three different people).
// Also the pre-check of the transmittal revision setting and the open purchase order lines that cite a superseded drawing revision.
import { onMounted, ref } from "vue";
import { charAdoptionAction, configAdoptionAction, confirmPoLine, getCharAdoptions, getCharCandidates, getConfigAdoptions, getConfigCandidates, getGoLiveOverview, getRevisionPreCheck, getStalePoLines, requestCharAdoption, requestConfigAdoption } from "@/api/GoLiveAPIs";
import { fail, ok } from "@/composables/inventory";

const overview = ref<any>(null);
const charCandidates = ref<any[]>([]);
const charAdoptions = ref<any[]>([]);
const configCandidates = ref<any[]>([]);
const configAdoptions = ref<any[]>([]);
const precheck = ref<any>(null);
const stale = ref<any[]>([]);
const busy = ref(false);
const note = ref("");
const charForm = ref<any>({ revisionId: null, reason: "", evidenceRef: "", emptyListReason: "", items: [] });
const configForm = ref<any>({ workOrderId: null, productId: null, bomHeaderId: null, reason: "", evidenceRef: "", comparisonNote: "" });
const confirmForm = ref<{ lineId: number | null; reference: string }>({ lineId: null, reference: "" });
const classes: Record<number, string> = { 1: "بحرانی", 2: "مهم", 3: "عادی" };

async function load() {
  const [o] = await getGoLiveOverview();
  overview.value = o;
  charCandidates.value = (await getCharCandidates())[0] || [];
  charAdoptions.value = (await getCharAdoptions())[0] || [];
  configCandidates.value = (await getConfigCandidates())[0] || [];
  configAdoptions.value = (await getConfigAdoptions())[0] || [];
  stale.value = (await getStalePoLines())[0] || [];
}

async function runPreCheck() {
  const [p, error] = await getRevisionPreCheck();
  if (error) return fail(error);
  precheck.value = p;
}

function addItem() {
  charForm.value.items.push({ no: "", name: "", nominal: 0, tolPlus: 0, tolMinus: 0, unit: "mm", class: 1, instrumentClass: 0, method: "" });
}

async function sendChar() {
  busy.value = true;
  const [, error] = await requestCharAdoption(charForm.value);
  busy.value = false;
  if (error) return fail(error);
  ok("درخواست پذیرش ثبت شد و منتظر تایید QA است.");
  charForm.value = { revisionId: null, reason: "", evidenceRef: "", emptyListReason: "", items: [] };
  load();
}

async function sendConfig() {
  busy.value = true;
  const [, error] = await requestConfigAdoption(configForm.value);
  busy.value = false;
  if (error) return fail(error);
  ok("درخواست پذیرش پیکربندی ثبت شد و منتظر بازبینی مهندسی و PMO است.");
  configForm.value = { workOrderId: null, productId: null, bomHeaderId: null, reason: "", evidenceRef: "", comparisonNote: "" };
  load();
}

async function act(kind: "char" | "config", row: any, action: string) {
  busy.value = true;
  const fn = kind === "char" ? charAdoptionAction : configAdoptionAction;
  const [, error] = await fn({ id: row.id, action, note: note.value || undefined, rowVersion: row.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  ok("انجام شد.");
  note.value = "";
  load();
}

async function confirmLine() {
  const f = confirmForm.value;
  if (!f.lineId) return;
  busy.value = true;
  const [, error] = await confirmPoLine({ lineId: f.lineId, reference: f.reference });
  busy.value = false;
  if (error) return fail(error);
  ok("تایید تامین‌کننده ثبت شد.");
  confirmForm.value = { lineId: null, reference: "" };
  load();
}

onMounted(load);
</script>

<template>
  <div class="inv-page" dir="rtl">
    <div class="inv-card">
      <h1 class="inv-title">گذار به کنترل پیکربندی و کیفیت</h1>
      <p class="inv-muted small">
        کارهای موجود پیش از روز آغاز کنترل «قدیمی» نشان داده می‌شوند و متوقف نمی‌شوند؛ هر استفاده از معافیت قدیمی با یادداشت ثبت و به ممیز هوش مصنوعی گزارش می‌شود. هر مورد قدیمی با یکی از دو پذیرش زیر وارد کنترل می‌شود.
        روز آغاز کنترل فقط با درخواست دو نفره از «پروفایل سخت‌گیرانه تولید» جابه‌جا می‌شود.
      </p>
      <div v-if="overview" class="d-flex flex-wrap gap-3 mb-3">
        <span class="inv-badge ok">روز آغاز کنترل: {{ overview.controlStartText }}</span>
        <span class="inv-badge" :class="overview.legacyWorkOrders ? 'warn' : 'ok'">{{ overview.legacyWorkOrders }} دستور کار قدیمی بدون پایه‌ریزی</span>
        <span class="inv-badge" :class="overview.legacyDrawingsWithoutList ? 'warn' : 'ok'">{{ overview.legacyDrawingsWithoutList }} نقشه قدیمی بدون فهرست مشخصه</span>
        <span class="inv-badge">{{ overview.openCharAdoptions }} پذیرش مشخصه در جریان</span>
        <span class="inv-badge">{{ overview.openConfigAdoptions }} پذیرش پیکربندی در جریان</span>
      </div>
    </div>

    <div class="inv-card">
      <h2 class="h6">پذیرش مشخصه‌های نقشه موجود (مهندسی + QA)</h2>
      <div class="table-responsive"><table class="table table-sm">
        <thead><tr><th>نقشه</th><th>بازنگری</th><th>تاریخ انتشار</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!charCandidates.length"><td colspan="4" class="text-center inv-muted">نقشه قدیمی بدون فهرست مشخصه‌ای نیست.</td></tr>
          <tr v-for="c in charCandidates" :key="c.revisionId"><td>{{ c.documentNumber }} <span class="inv-muted">{{ c.title }}</span></td><td>{{ c.revisionCode }}</td><td>{{ c.releasedAt ? new Date(c.releasedAt).toLocaleDateString("fa-IR") : "-" }}</td>
            <td><span v-if="c.adoptionId" class="inv-badge warn">پذیرش در جریان</span><button v-else class="btn btn-sm btn-outline-primary" @click="charForm.revisionId = c.revisionId">پذیرش</button></td></tr>
        </tbody>
      </table></div>
      <div v-if="charForm.revisionId" class="border rounded p-2 mb-2">
        <div class="row g-2">
          <div class="col-md-6"><input v-model="charForm.reason" class="form-control" placeholder="دلیل پذیرش (دست‌کم ۱۵ نویسه)" /></div>
          <div class="col-md-6"><input v-model="charForm.evidenceRef" class="form-control" placeholder="مرجع مدرک (برگه بازرسی، گزارش اندازه‌گیری)" /></div>
        </div>
        <table class="table table-sm mt-2"><thead><tr><th>شماره</th><th>نام</th><th>اسمی</th><th>+</th><th>−</th><th>واحد</th><th>رده</th></tr></thead>
          <tbody><tr v-for="(it, i) in charForm.items" :key="i">
            <td><input v-model="it.no" class="form-control form-control-sm" /></td><td><input v-model="it.name" class="form-control form-control-sm" /></td>
            <td><input v-model.number="it.nominal" type="number" class="form-control form-control-sm" /></td><td><input v-model.number="it.tolPlus" type="number" class="form-control form-control-sm" /></td>
            <td><input v-model.number="it.tolMinus" type="number" class="form-control form-control-sm" /></td><td><input v-model="it.unit" class="form-control form-control-sm" /></td>
            <td><select v-model.number="it.class" class="form-select form-select-sm"><option v-for="(t, k) in classes" :key="k" :value="Number(k)">{{ t }}</option></select></td>
          </tr></tbody></table>
        <input v-if="!charForm.items.length" v-model="charForm.emptyListReason" class="form-control mb-2" placeholder="فهرست خالی: دلیل «بدون مشخصه قابل اندازه‌گیری»" />
        <button class="btn btn-sm btn-outline-secondary me-2" @click="addItem">+ مشخصه</button>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="sendChar">ثبت درخواست</button>
      </div>
      <div class="table-responsive"><table class="table table-sm">
        <thead><tr><th>نقشه</th><th>وضعیت</th><th>درخواست‌کننده</th><th>دلیل</th><th></th></tr></thead>
        <tbody><tr v-for="a in charAdoptions" :key="a.id"><td>{{ a.documentNumber }} / {{ a.revisionCode }}</td><td>{{ a.statusText }}</td><td>{{ a.requestedByName }}</td><td>{{ a.reason }}</td>
          <td><button v-if="a.canAccept" :disabled="busy" class="btn btn-sm btn-success me-1" @click="act('char', a, 'accept')">تایید QA</button><button v-if="a.canVoid" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('char', a, 'void')">ابطال (با شرح)</button></td></tr></tbody>
      </table></div>
    </div>

    <div class="inv-card">
      <h2 class="h6">پذیرش پیکربندی پروژه موجود (مهندسی + مهندسی دوم + PMO)</h2>
      <div class="table-responsive"><table class="table table-sm">
        <thead><tr><th>دستور کار</th><th>ایجاد</th><th>ردیف BOM قدیمی</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!configCandidates.length"><td colspan="4" class="text-center inv-muted">دستور کار قدیمی بدون پایه‌ریزی نیست.</td></tr>
          <tr v-for="c in configCandidates" :key="c.workOrderId"><td>{{ c.title }} ({{ c.workOrderId }})</td><td>{{ c.createdAt ? new Date(c.createdAt).toLocaleDateString("fa-IR") : "-" }}</td><td>{{ c.legacyLineCount }}</td>
            <td><span v-if="c.adoptionId" class="inv-badge warn">پذیرش در جریان</span><button v-else class="btn btn-sm btn-outline-primary" @click="configForm.workOrderId = c.workOrderId">پذیرش</button></td></tr>
        </tbody>
      </table></div>
      <div v-if="configForm.workOrderId" class="border rounded p-2 mb-2">
        <div class="row g-2">
          <div class="col-md-3"><input v-model.number="configForm.productId" type="number" class="form-control" placeholder="شناسه محصول بانک محصول" /></div>
          <div class="col-md-3"><input v-model.number="configForm.bomHeaderId" type="number" class="form-control" placeholder="شناسه BOM منتشرشده" /></div>
          <div class="col-md-6"><input v-model="configForm.reason" class="form-control" placeholder="دلیل پذیرش" /></div>
          <div class="col-md-6"><input v-model="configForm.evidenceRef" class="form-control" placeholder="مرجع مدرک" /></div>
          <div class="col-md-6"><input v-model="configForm.comparisonNote" class="form-control" placeholder="مقایسه BOM قدیمی با BOM بانک محصول" /></div>
        </div>
        <button :disabled="busy" class="btn btn-sm btn-primary mt-2" @click="sendConfig">ثبت درخواست</button>
      </div>
      <input v-model="note" class="form-control form-control-sm mb-2" placeholder="شرح بازبینی / دلیل ابطال" />
      <div class="table-responsive"><table class="table table-sm">
        <thead><tr><th>دستور کار</th><th>BOM</th><th>وضعیت</th><th>تهیه‌کننده</th><th>بازبین مهندسی</th><th>بازبین PMO</th><th></th></tr></thead>
        <tbody><tr v-for="a in configAdoptions" :key="a.id"><td>{{ a.workOrderId }}</td><td>{{ a.bomCode }} / {{ a.bomRevision }} ({{ a.bankLineCount }} در برابر {{ a.legacyLineCount }})</td><td>{{ a.statusText }}</td><td>{{ a.requestedByName }}</td><td>{{ a.engReviewerName || "-" }}</td><td>{{ a.pmoReviewerName || "-" }}</td>
          <td><button v-if="a.canEngReview" :disabled="busy" class="btn btn-sm btn-success me-1" @click="act('config', a, 'eng-review')">بازبینی مهندسی</button><button v-if="a.canPmoReview" :disabled="busy" class="btn btn-sm btn-success me-1" @click="act('config', a, 'pmo-review')">بازبینی PMO</button><button v-if="a.canVoid" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('config', a, 'void')">ابطال</button></td></tr></tbody>
      </table></div>
    </div>

    <div class="inv-card">
      <h2 class="h6">معافیت‌های قدیمی که استفاده شده است</h2>
      <div class="table-responsive"><table class="table table-sm">
        <thead><tr><th>نوع</th><th>شماره</th><th>یادداشت</th><th>کاربر</th><th>زمان</th></tr></thead>
        <tbody>
          <tr v-if="!overview?.uses?.length"><td colspan="5" class="text-center inv-muted">تا کنون معافیتی استفاده نشده است.</td></tr>
          <tr v-for="u in overview?.uses || []" :key="u.id"><td>{{ u.kindText }}</td><td>{{ u.number }}</td><td>{{ u.note }}</td><td>{{ u.usedByName }}</td><td>{{ new Date(u.usedAt).toLocaleString("fa-IR") }}</td></tr>
        </tbody>
      </table></div>
    </div>

    <div class="inv-card">
      <h2 class="h6">ردیف‌های سفارش خرید با بازنگری منسوخ‌شده نقشه</h2>
      <div class="table-responsive"><table class="table table-sm">
        <thead><tr><th>سفارش</th><th>نقشه</th><th>بازنگری ارجاع‌شده</th><th>بازنگری جاری</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!stale.length"><td colspan="5" class="text-center inv-muted">ردیف بازی به بازنگری منسوخ ارجاع ندارد.</td></tr>
          <tr v-for="s in stale" :key="s.lineId"><td>{{ s.orderNumber }}</td><td>{{ s.drawingNumber }}</td><td>{{ s.drawingRevision }}</td><td>{{ s.currentRevision }}</td>
            <td><button class="btn btn-sm btn-outline-primary" @click="confirmForm.lineId = s.lineId">ثبت تایید تامین‌کننده</button></td></tr>
        </tbody>
      </table></div>
      <div v-if="confirmForm.lineId" class="d-flex gap-2"><input v-model="confirmForm.reference" class="form-control" placeholder="مرجع تایید تامین‌کننده (نامه، ایمیل)" /><button :disabled="busy" class="btn btn-primary" @click="confirmLine">ثبت</button></div>
    </div>

    <div class="inv-card">
      <h2 class="h6">پیش‌بررسی «ترنسمیتال باید به بازنگری ثبت‌شده ارجاع دهد»</h2>
      <button class="btn btn-sm btn-outline-secondary mb-2" @click="runPreCheck">بررسی کن</button>
      <div v-if="precheck"><p>{{ precheck.summary }}</p>
        <ul class="small"><li v-for="r in precheck.rows" :key="r.recordId">{{ r.number }} — {{ r.title }}: {{ r.problem }}</li></ul></div>
    </div>
  </div>
</template>
