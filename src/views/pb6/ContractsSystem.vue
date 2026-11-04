<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import Pb6Table from "@/components/common/Pb6Table.vue";
import { dealAction, draftAction, getContractChanges, getContractMapping, getDeal, getDeals, getDraft, getDraftPrint, getDrafts, getTender, getTenders, saveDeal, saveDraft, saveTender, selectTenderWinner, tenderAction } from "@/api/Pb6APIs";
import { fail, jalali, ok, printPage, rial } from "@/composables/inventory";

// The contracts system (پیوست ۶): three cartables (deal request, tender and winner, contract draft and exchange) and the changes list.
const route = useRoute();
const tab = ref<"deals" | "tenders" | "drafts" | "changes">(route.query.tender ? "tenders" : route.query.draft ? "drafts" : "deals");
const rows = ref<any[]>([]);
const current = ref<any>(null);
const printing = ref<any>(null);
const mapping = ref<any[]>([]);
const note = ref("");
const reference = ref("");
const busy = ref(false);

const dealLevels: Record<number, string> = { 1: "کوچک", 2: "متوسط", 3: "بزرگ", 4: "راهبردی" };
const dealStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "در جریان تایید", 2: "تایید شد", 3: "برگشت خورد", 4: "در مناقصه", 9: "رد شد" };
const tenderStatuses: Record<number, string> = { 0: "باز", 1: "برنده انتخاب شد", 2: "برنده تایید شد", 9: "لغو شد" };
const tenderMethods: Record<number, string> = { 1: "مناقصه عمومی", 2: "مناقصه محدود", 3: "استعلام", 4: "ترک تشریفات", 5: "انتخاب مشاور" };
const draftStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال برای طرف مقابل", 2: "نظرات دریافت شد", 3: "توافق شد", 4: "امضا شد", 5: "ابلاغ شد", 9: "لغو شد" };

const columns = computed(() => {
  if (tab.value === "deals") return [{ key: "number", title: "شماره" }, { key: "title", title: "عنوان" }, { key: "levelText", title: "سطح" }, { key: "estimatedAmount", title: "برآورد (ریال)", kind: "money" }, { key: "statusText", title: "وضعیت" }, { key: "creatorName", title: "درخواست‌دهنده" }];
  if (tab.value === "tenders") return [{ key: "number", title: "شماره" }, { key: "dealTitle", title: "معامله" }, { key: "methodText", title: "روش" }, { key: "statusText", title: "وضعیت" }];
  if (tab.value === "drafts") return [{ key: "number", title: "شماره" }, { key: "title", title: "عنوان" }, { key: "counterpartyName", title: "طرف مقابل" }, { key: "contractAmount", title: "مبلغ (ریال)", kind: "money" }, { key: "statusText", title: "وضعیت" }];
  return [{ key: "kindText", title: "نوع" }, { key: "number", title: "شماره" }, { key: "contractTitle", title: "قرارداد" }, { key: "status", title: "وضعیت" }, { key: "amountBefore", title: "مبلغ قبل", kind: "money" }, { key: "amountAfter", title: "مبلغ بعد", kind: "money" }];
});

async function load() {
  current.value = null;
  printing.value = null;
  let res: any;
  if (tab.value === "deals") res = await getDeals();
  else if (tab.value === "tenders") res = await getTenders();
  else if (tab.value === "drafts") res = await getDrafts();
  else res = await getContractChanges();
  if (res[1]) return fail(res[1]);
  rows.value = (res[0] || []).map((r: any) => ({ ...r, levelText: dealLevels[r.dealLevel], statusText: (tab.value === "deals" ? dealStatuses : tab.value === "tenders" ? tenderStatuses : draftStatuses)[r.status], methodText: tenderMethods[r.method], kindText: "الحاقیه" }));
}

async function open(r: any) {
  if (tab.value === "changes") return;
  const res: any = tab.value === "deals" ? await getDeal(r.id) : tab.value === "tenders" ? await getTender(r.id) : await getDraft(r.id);
  if (res[1]) return fail(res[1]);
  current.value = res[0];
  note.value = "";
}

function switchTab(t: any) {
  tab.value = t;
  load();
}

// ---- forms
const deal = ref<any>({ title: "", subject: "", estimatedAmount: 0, dealLevel: 2, formKind: 2, fundingSource: "", organisationalUnit: "", partyName: "", submit: true });
const draft = ref<any>({ title: "", subject: "", contractAmount: 0, counterpartyName: "", dealRequestId: null, tenderRunId: null, paymentTerms: "" });
const tender = ref<any>({ dealRequestId: null, method: 3, participants: [{ companyName: "", offerAmount: 0 }, { companyName: "", offerAmount: 0 }] });

async function send(fn: () => Promise<any>, message: string) {
  busy.value = true;
  const [data, error] = await fn();
  busy.value = false;
  if (error) return fail(error);
  ok(message);
  current.value = data;
  await load();
  current.value = data;
}

const createDeal = () => send(() => saveDeal(deal.value), "درخواست معامله ثبت شد.");
const createDraft = () => send(() => saveDraft(draft.value), "پیش‌نویس قرارداد ذخیره شد.");
const createTender = () => send(() => saveTender(tender.value), "مناقصه ثبت شد.");
const doDeal = (a: string) => send(() => dealAction(current.value.id, a, note.value), "انجام شد.");
const doTender = (a: string) => send(() => tenderAction(current.value.id, a, note.value), "انجام شد.");
const doDraft = (a: string) => send(() => draftAction(current.value.id, a, note.value, reference.value), "انجام شد.");
const pickWinner = (p: any) => send(() => selectTenderWinner(current.value.id, p.id, note.value), "برنده انتخاب شد.");
async function showPrint() {
  const [data, error] = await getDraftPrint(current.value.id);
  if (error) return fail(error);
  printing.value = data;
}
onMounted(async () => {
  load();
  const [m] = await getContractMapping();
  mapping.value = m || [];
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">سامانه قراردادها (پیوست ۶)</h1>
      </div>
      <div class="btn-group mb-3 d-print-none">
        <button v-for="t in [['deals','درخواست معامله'],['tenders','مناقصه و برنده'],['drafts','پیش‌نویس و تبادل قرارداد'],['changes','تغییرات قرارداد']]" :key="t[0]" class="btn btn-sm" :class="tab === t[0] ? 'btn-primary' : 'btn-outline-primary'" @click="switchTab(t[0])">{{ t[1] }}</button>
      </div>

      <Pb6Table :columns="columns" :rows="rows" @select="open" />

      <div v-if="tab === 'deals' && !current" class="inv-card mt-3 d-print-none">
        <h2 class="h6">درخواست معامله جدید</h2>
        <div class="inv-filters">
          <input v-model="deal.title" class="form-control form-control-sm" placeholder="عنوان" />
          <input v-model="deal.subject" class="form-control form-control-sm" placeholder="موضوع" />
          <input v-model.number="deal.estimatedAmount" type="number" class="form-control form-control-sm inv-ltr" placeholder="برآورد (ریال)" />
          <select v-model.number="deal.dealLevel" class="form-select form-select-sm"><option v-for="(t, k) in dealLevels" :key="k" :value="Number(k)">{{ t }}</option></select>
          <select v-model.number="deal.formKind" class="form-select form-select-sm"><option :value="1">مشاور</option><option :value="2">کارفرما</option></select>
          <input v-model="deal.partyName" class="form-control form-control-sm" placeholder="طرف معامله" />
          <button class="btn btn-sm btn-primary" :disabled="busy" @click="createDeal">ثبت و ارسال</button>
        </div>
      </div>
      <div v-if="tab === 'tenders' && !current" class="inv-card mt-3 d-print-none">
        <h2 class="h6">مناقصه جدید (برای درخواست تاییدشده)</h2>
        <div class="inv-filters">
          <input v-model.number="tender.dealRequestId" type="number" class="form-control form-control-sm inv-ltr" placeholder="شناسه درخواست معامله" />
          <select v-model.number="tender.method" class="form-select form-select-sm"><option v-for="(t, k) in tenderMethods" :key="k" :value="Number(k)">{{ t }}</option></select>
        </div>
        <div v-for="(p, i) in tender.participants" :key="i" class="inv-filters mt-1">
          <input v-model="p.companyName" class="form-control form-control-sm" placeholder="شرکت‌کننده" />
          <input v-model.number="p.offerAmount" type="number" class="form-control form-control-sm inv-ltr" placeholder="پیشنهاد (ریال)" />
        </div>
        <button class="btn btn-sm btn-outline-secondary mt-1" @click="tender.participants.push({ companyName: '', offerAmount: 0 })">شرکت‌کننده دیگر</button>
        <button class="btn btn-sm btn-primary mt-1 ms-2" :disabled="busy" @click="createTender">ثبت مناقصه</button>
      </div>
      <div v-if="tab === 'drafts' && !current" class="inv-card mt-3 d-print-none">
        <h2 class="h6">پیش‌نویس قرارداد جدید</h2>
        <div class="inv-filters">
          <input v-model="draft.title" class="form-control form-control-sm" placeholder="عنوان" />
          <input v-model="draft.subject" class="form-control form-control-sm" placeholder="موضوع" />
          <input v-model="draft.counterpartyName" class="form-control form-control-sm" placeholder="طرف مقابل" />
          <input v-model.number="draft.contractAmount" type="number" class="form-control form-control-sm inv-ltr" placeholder="مبلغ (ریال)" />
          <input v-model.number="draft.tenderRunId" type="number" class="form-control form-control-sm inv-ltr" placeholder="شناسه مناقصه (در صورت وجود)" />
          <button class="btn btn-sm btn-primary" :disabled="busy" @click="createDraft">ذخیره</button>
        </div>
      </div>

      <div v-if="current" class="inv-card mt-3">
        <div class="d-flex justify-content-between"><h2 class="h6">{{ current.number }} {{ current.title || current.dealTitle }}</h2><button class="btn btn-sm btn-outline-secondary d-print-none" @click="current = null">بستن</button></div>
        <template v-if="tab === 'deals'">
          <p>مبلغ برآوردی: {{ rial(current.estimatedAmount) }} ریال، دور {{ current.round }}، وضعیت: {{ dealStatuses[current.status] }}</p>
          <p v-if="current.returnReason" class="text-danger">علت برگشت/رد: {{ current.returnReason }}</p>
          <table class="table table-sm"><thead><tr><th>مرحله</th><th>نقش</th><th>تصمیم‌گیرنده</th><th>نتیجه</th></tr></thead><tbody>
            <tr v-for="s in current.steps" :key="s.id"><td>{{ s.order }} (دور {{ s.round }})</td><td>{{ s.role }}</td><td>{{ s.designatedName || s.decidedByName }}</td><td>{{ ['در انتظار','تایید','برگشت','رد'][s.state] }} {{ s.note }}</td></tr>
          </tbody></table>
          <div class="d-print-none d-flex gap-2 flex-wrap">
            <input v-model="note" class="form-control form-control-sm w-auto" placeholder="توضیح / علت" />
            <button v-if="current.status === 0 || current.status === 3" class="btn btn-sm btn-primary" @click="doDeal('submit')">ارسال</button>
            <button v-if="current.status === 1" class="btn btn-sm btn-success" @click="doDeal('approve')">تایید</button>
            <button v-if="current.status === 1" class="btn btn-sm btn-warning" @click="doDeal('return')">برگشت</button>
            <button v-if="current.status === 1" class="btn btn-sm btn-danger" @click="doDeal('reject')">رد</button>
          </div>
        </template>
        <template v-else-if="tab === 'tenders'">
          <p>وضعیت: {{ tenderStatuses[current.status] }}. <span v-if="current.winnerSelectedByName">انتخاب‌کننده: {{ current.winnerSelectedByName }}</span> <span v-if="current.winnerConfirmedByName">تاییدکننده: {{ current.winnerConfirmedByName }}</span></p>
          <table class="table table-sm"><thead><tr><th>شرکت‌کننده</th><th>پیشنهاد (ریال)</th><th>امتیاز فنی</th><th></th></tr></thead><tbody>
            <tr v-for="p in current.participants" :key="p.id"><td>{{ p.providerName || p.companyName }} <span v-if="p.lowest" class="inv-badge ok">کمترین</span> <span v-if="p.disqualified" class="inv-badge bad">رد صلاحیت</span></td><td class="num">{{ rial(p.offerAmount) }}</td><td>{{ p.technicalScore }}</td>
              <td class="d-print-none"><button v-if="current.status === 0 && !p.disqualified" class="btn btn-sm btn-outline-primary" @click="pickWinner(p)">انتخاب برنده</button><span v-if="current.winnerParticipantId === p.id" class="inv-badge ok">برنده</span></td></tr>
          </tbody></table>
          <div class="d-print-none d-flex gap-2 flex-wrap">
            <input v-model="note" class="form-control form-control-sm w-auto" placeholder="علت (برای انتخاب غیر از کمترین، برگشت یا لغو)" />
            <button v-if="current.status === 1" class="btn btn-sm btn-success" @click="doTender('confirm-winner')">تایید برنده</button>
            <button v-if="current.status === 1" class="btn btn-sm btn-warning" @click="doTender('reopen')">بازگشایی</button>
            <button v-if="current.status < 2" class="btn btn-sm btn-danger" @click="doTender('cancel')">لغو مناقصه</button>
          </div>
        </template>
        <template v-else>
          <p>مبلغ: {{ rial(current.contractAmount) }} ریال. وضعیت: {{ draftStatuses[current.status] }}. دور تبادل: {{ current.exchangeRound }}</p>
          <p v-if="current.counterpartyComments">نظرات طرف مقابل: {{ current.counterpartyComments }}</p>
          <p v-if="current.contractNumber">شماره قرارداد: <span class="inv-ltr">{{ current.contractNumber }}</span> ({{ jalali(current.signedDate) }})</p>
          <div class="d-print-none d-flex gap-2 flex-wrap">
            <input v-model="note" class="form-control form-control-sm w-auto" placeholder="نظرات / علت" />
            <input v-model="reference" class="form-control form-control-sm w-auto inv-ltr" placeholder="شماره قرارداد (هنگام امضا)" />
            <button v-if="current.status === 0 || current.status === 2" class="btn btn-sm btn-primary" @click="doDraft('exchange')">ارسال برای طرف مقابل</button>
            <button v-if="current.status === 1" class="btn btn-sm btn-outline-primary" @click="doDraft('comments')">ثبت نظرات طرف مقابل</button>
            <button v-if="current.status === 1 || current.status === 2" class="btn btn-sm btn-success" @click="doDraft('agree')">ثبت توافق</button>
            <button v-if="current.status === 3" class="btn btn-sm btn-success" @click="doDraft('sign')">امضا (مدیرعامل)</button>
            <button v-if="current.status === 4" class="btn btn-sm btn-primary" @click="doDraft('notify')">ابلاغ</button>
            <button v-if="current.status < 4" class="btn btn-sm btn-danger" @click="doDraft('cancel')">لغو</button>
            <button class="btn btn-sm btn-outline-secondary" @click="showPrint">پیش‌نمایش چاپ</button>
          </div>
          <ul class="small mt-2"><li v-for="(h, i) in current.history" :key="i">{{ jalali(h.at, true) }} — {{ h.userName }}: {{ h.action }} {{ h.note }}</li></ul>
        </template>
      </div>

      <div v-if="printing" class="inv-card mt-3 pb6-sheet" dir="rtl">
        <h2 class="h5 text-center">{{ printing.draft.title }}</h2>
        <p>طرف مقابل: {{ printing.draft.counterpartyName }} — مبلغ: {{ rial(printing.draft.contractAmount) }} ریال ({{ printing.amountInWords }})</p>
        <div v-for="c in printing.clauses" :key="c.number"><strong>ماده {{ c.number }} — {{ c.title }}:</strong> {{ c.text }}</div>
        <button class="btn btn-sm btn-outline-secondary d-print-none mt-2" @click="printPage">چاپ</button>
      </div>

      <details v-if="tab === 'changes'" class="mt-3">
        <summary>نگاشت این سامانه به ماژول‌های موجود</summary>
        <Pb6Table :columns="[{ key: 'need', title: 'نیاز' }, { key: 'existingModule', title: 'ماژول موجود' }, { key: 'extension', title: 'گسترش' }]" :rows="mapping" />
      </details>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style>
.pb6-sheet { font-family: "B Zar", "BZar", Tahoma, sans-serif; }
@media print { @page { size: A4; margin: 12mm; } .pb6-sheet { direction: rtl; } }
</style>
