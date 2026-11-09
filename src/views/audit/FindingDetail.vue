<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getFinding, proposeAction, stepFinding, manualRecheck, rowsOf } from "@/api/AuditAPIs";
import { fail, ok, printPage } from "@/composables/inventory";
import { findingStatuses, findingStatusClass, severities, severityClass, stepTitles } from "@/composables/audit";

// One finding: the evidence (immutable snapshot with its SHA-256) and the closure chain. Closing needs QA, PMO and the CEO (three distinct persons); the owner, the actioner and AI seats can be none of them.
const route = useRoute();
const f = ref<any>(null);
const note = ref("");
const act = ref<any>({ rootCause: "", correction: "", correctiveAction: "", capaId: null });
const busy = ref(false);
const id = Number(route.params.id);

const allowed = computed<string[]>(() => f.value?.allowed || []);
const evidence = computed(() => {
  try { return JSON.stringify(JSON.parse(f.value?.evidenceJson || "{}"), null, 2); } catch { return f.value?.evidenceJson || ""; }
});

async function load() {
  const [d, e] = await getFinding(id);
  if (e) return fail(e);
  f.value = d;
}
async function run(fn: () => Promise<any>, message: string) {
  if (busy.value) return;
  busy.value = true;
  try {
    const [d, e] = await fn();
    if (e) return fail(e);
    f.value = d;
    note.value = "";
    ok(message);
  } finally { busy.value = false; }
}
const saveAction = () => run(() => proposeAction({ findingId: id, rootCause: act.value.rootCause, correction: act.value.correction, correctiveAction: act.value.correctiveAction, capaId: act.value.capaId || null }), "اقدام ثبت شد.");
const step = (s: string, refuse = false) => run(() => stepFinding(id, s, refuse, note.value), refuse ? "رد شد و یافته به حالت باز برگشت." : "ثبت شد.");
const recheck = (passed: boolean) => run(() => manualRecheck(id, passed, note.value), "نتیجه‌ی راستی‌آزمایی ثبت شد.");
onMounted(load);
void rowsOf;
</script>

<template>
  <div v-if="f" class="inv-page ks-report">
    <KsReportHeader :title="'یافته‌ی ' + f.number" :subtitle="f.title" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">{{ f.number }} - {{ f.title }}</h1>
        <div class="d-flex gap-2 d-print-none">
          <router-link :to="{ name: 'AuditFindings' }" class="btn btn-sm btn-outline-secondary">فهرست یافته‌ها</router-link>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <div class="row mb-3">
        <div class="col-md-3"><small class="inv-muted">شدت</small><div><span class="inv-badge" :class="severityClass[f.severityValue]">{{ severities[f.severityValue] }}</span></div></div>
        <div class="col-md-3"><small class="inv-muted">وضعیت</small><div><span class="inv-badge" :class="findingStatusClass[f.statusValue]">{{ findingStatuses[f.statusValue] }}</span></div></div>
        <div class="col-md-3"><small class="inv-muted">مسئول</small><div>{{ f.ownerName || f.ownerRole }}</div></div>
        <div class="col-md-3"><small class="inv-muted">مهلت</small><div :class="f.overdue ? 'text-danger' : ''">{{ f.dueDateText }}</div></div>
        <div class="col-md-3"><small class="inv-muted">استاندارد و بند</small><div class="inv-ltr">{{ f.standard }} {{ f.clause }}</div></div>
        <div class="col-md-3"><small class="inv-muted">رویه</small><div>{{ f.procedure }}</div></div>
        <div class="col-md-3"><small class="inv-muted">فرایند</small><div>{{ f.processTitle }}</div></div>
        <div class="col-md-3"><small class="inv-muted">شناسایی</small><div>{{ f.detectedAtText }} ({{ f.seenCount }} بار دیده شد)</div></div>
      </div>
      <p>{{ f.description }}</p>
      <div v-if="f.recurrenceOfId" class="alert alert-warning py-1">
        تکرار انحراف؛ تعداد تکرار: {{ f.recurrenceCount }}.
        <router-link :to="{ name: 'AuditFinding', params: { id: f.recurrenceOfId } }">یافته‌ی قبلی</router-link>
      </div>
      <div v-if="f.link" class="mb-2"><a :href="f.link">رکورد مربوط ({{ f.objectNumber || f.objectType }})</a></div>

      <h2 class="h6 mt-3">شواهد (تصویر ثابت)</h2>
      <pre class="inv-ltr small border rounded p-2" style="white-space: pre-wrap">{{ evidence }}</pre>
      <div class="small inv-muted inv-ltr mb-3">SHA-256: {{ f.evidenceSha256 }}</div>

      <h2 class="h6">زنجیره‌ی بستن</h2>
      <table class="table table-sm">
        <thead><tr><th>مرحله</th><th>شخص</th><th>زمان</th><th>توضیح</th></tr></thead>
        <tbody>
          <tr><td>اقدام مسئول</td><td>{{ f.actionByName }}</td><td>{{ f.actionAtText }}</td><td>{{ f.rootCause }}<div v-if="f.correction" class="small">اصلاح: {{ f.correction }}</div><div v-if="f.correctiveAction" class="small">اقدام اصلاحی: {{ f.correctiveAction }}</div></td></tr>
          <tr><td>بازبینی</td><td>خودکار / ممیز</td><td>{{ f.recheckAtText }}</td><td>{{ f.recheckPassed === null || f.recheckPassed === undefined ? "" : f.recheckPassed ? "قبول" : "رد" }}</td></tr>
          <tr><td>پیشنهاد QA</td><td>{{ f.qaName }}</td><td>{{ f.qaAtText }}</td><td>{{ f.qaNote }}</td></tr>
          <tr><td>پیشنهاد PMO</td><td>{{ f.pmoName }}</td><td>{{ f.pmoAtText }}</td><td>{{ f.pmoNote }}</td></tr>
          <tr><td>تایید مدیرعامل</td><td>{{ f.ceoName }}</td><td>{{ f.ceoAtText }}</td><td>{{ f.ceoNote }}</td></tr>
        </tbody>
      </table>
      <div v-if="f.voidReason" class="alert alert-secondary py-1">دلیل ابطال: {{ f.voidReason }} ({{ f.voidedAtText }})</div>
      <div v-if="f.nextStep" class="small inv-muted mb-2">گام بعدی: {{ f.nextStep }}</div>

      <div v-if="allowed.length" class="border rounded p-2 mb-3 d-print-none">
        <div v-if="allowed.includes('action')" class="mb-2">
          <h3 class="h6">پیشنهاد اقدام (مسئول)</h3>
          <textarea v-model="act.rootCause" class="form-control form-control-sm mb-1" rows="2" placeholder="ریشه‌ی علت (دست‌کم ۱۰ نویسه)"></textarea>
          <textarea v-model="act.correction" class="form-control form-control-sm mb-1" rows="2" placeholder="اصلاح انجام‌شده"></textarea>
          <textarea v-model="act.correctiveAction" class="form-control form-control-sm mb-1" rows="2" placeholder="اقدام اصلاحی برای جلوگیری از تکرار"></textarea>
          <input v-model.number="act.capaId" class="form-control form-control-sm mb-1 inv-ltr" placeholder="شناسه‌ی CAPA (اختیاری)" />
          <button class="btn btn-sm btn-primary" :disabled="busy" @click="saveAction">ثبت اقدام (بازبینی خودکار اجرا می‌شود)</button>
        </div>
        <input v-model="note" class="form-control form-control-sm mb-2" placeholder="توضیح یا دلیل (برای رد الزامی است)" />
        <div class="d-flex flex-wrap gap-2">
          <button v-if="allowed.includes('recheck')" class="btn btn-sm btn-success" :disabled="busy" @click="recheck(true)">راستی‌آزمایی: قبول</button>
          <button v-if="allowed.includes('recheck')" class="btn btn-sm btn-outline-danger" :disabled="busy" @click="recheck(false)">راستی‌آزمایی: رد</button>
          <button v-for="s in ['qa', 'pmo', 'ceo']" v-show="allowed.includes(s)" :key="s" class="btn btn-sm btn-primary" :disabled="busy" @click="step(s)">{{ stepTitles[s] }}</button>
          <button v-if="allowed.includes('refuse')" class="btn btn-sm btn-outline-danger" :disabled="busy" @click="step(allowed.includes('qa') ? 'qa' : allowed.includes('pmo') ? 'pmo' : 'ceo', true)">رد و بازگشت به باز</button>
          <button v-for="s in ['void-qa', 'void-pmo', 'void-ceo']" v-show="allowed.includes(s)" :key="s" class="btn btn-sm btn-outline-secondary" :disabled="busy" @click="step(s)">{{ stepTitles[s] }}</button>
          <button v-if="allowed.includes('void-refuse')" class="btn btn-sm btn-outline-danger" :disabled="busy" @click="step(allowed.includes('void-pmo') ? 'void-pmo' : 'void-ceo', true)">رد ابطال</button>
        </div>
      </div>

      <h2 class="h6">تاریخچه</h2>
      <table class="table table-sm">
        <thead><tr><th>زمان</th><th>رویداد</th><th>شخص</th><th>از</th><th>به</th><th>توضیح</th></tr></thead>
        <tbody>
          <tr v-for="(e, i) in f.events" :key="i"><td>{{ e.atText }}</td><td>{{ e.kindTitle }}</td><td>{{ e.userName }}</td><td>{{ e.from }}</td><td>{{ e.to }}</td><td>{{ e.note }}</td></tr>
        </tbody>
      </table>
      <template v-if="f.related?.length">
        <h2 class="h6">یافته‌های مرتبط (تکرار)</h2>
        <ul><li v-for="r in f.related" :key="r.id"><router-link :to="{ name: 'AuditFinding', params: { id: r.id } }">{{ r.number }}</router-link> - {{ findingStatuses[r.statusValue] }}</li></ul>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
