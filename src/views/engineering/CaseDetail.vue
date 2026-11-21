<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import EnAction from "@/components/engineering/EnAction.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import EnGapView from "@/components/engineering/EnGapView.vue";
import EnInfoList from "@/components/engineering/EnInfoList.vue";
import EnPrompt from "@/components/engineering/EnPrompt.vue";
import EnWait from "@/components/engineering/EnWait.vue";
import {
  decideEnRegistration, getEnCase, getEnCharterView, getEnClarifications, getEnGap, getEnHandovers, getEnPlatforms, getEnReferenceLists, getEnRegistrations, postEnCaseAction, postEnClarificationAction,
  proposeEnRegistration, saveEnClarification,
} from "@/api/EngineeringAPIs";
import { caseKinds, clarStatuses, enPaths, EnRoles, eventKinds, faDigits, faNum, downloadDoc, handoverStatuses, loadStandardOptions, runEn, uploadDoc, useEnDialog } from "@/composables/engineering";
import { fail } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";

// An engineering case laid out as the paths of the employer's diagram: (1) platform or new design, (2) the tender technical package, (3) clarification through commercial,
// (4) charter review, (5) handover to the PMO. The agent prepares; the buttons are the gates. Every button takes its state and reason from the DTO.
const route = useRoute();
// Engineering asks the PMO (as the unit «مهندسی») about a case that has a contract; the server decides, the section only gives the engineers the list and the ask button.
const canAskPmo = hasRole(EnRoles.writers);
const id = computed(() => Number(route.params.id));
const c = ref<any>(null);
const gap = ref<any>(null);
const clars = ref<any[]>([]);
const handovers = ref<any[]>([]);
const regs = ref<any[]>([]);
const charter = ref<any>(null);
const products = ref<{ value: number; text: string }[]>([]);
const loading = ref(true);
const message = ref("");
const showCharter = ref(false);
const sideError = ref("");

async function load() {
  loading.value = true;
  const [d, e] = await getEnCase(id.value);
  loading.value = false;
  if (e || !d) {
    message.value = e?.message || "پرونده پیدا نشد.";
    return fail(e || message.value);
  }
  message.value = "";
  c.value = d;
  // The charter shown belongs to the case and its review state: it is read again after a change of case or a changed charter.
  charter.value = null;
  showCharter.value = false;
  const errs: string[] = [];
  const [cl, ce] = await getEnClarifications({ CaseId: id.value, PageSize: 100 });
  clars.value = cl?.data || [];
  if (ce) errs.push("شفاف‌سازی‌ها");
  const [hs, he] = await getEnHandovers({ CaseId: id.value, PageSize: 50 });
  handovers.value = hs?.data || [];
  if (he) errs.push("دورهای تحویل");
  const [rg, re] = await getEnRegistrations({ CaseId: id.value, PageSize: 20 });
  regs.value = rg?.data || [];
  if (re) errs.push("پیشنهادهای ثبت پلتفرم");
  gap.value = null;
  if (d.standardGap?.id) {
    const [g, ge] = await getEnGap(d.standardGap.id);
    gap.value = g;
    if (ge) errs.push("گزارش تفاوت دو استاندارد");
  }
  sideError.value = errs.length ? "این بخش‌ها خوانده نشد: " + errs.join("، ") + "." : "";
}
onMounted(load);
watch(id, load);
const dlg = useEnDialog(load);

const act = (action: string, extra: any = {}) => postEnCaseAction({ id: id.value, action, rowVersion: c.value.rowVersion, ...extra });
const direct = (action: string, okText: string) => runEn(() => act(action), okText, load);

async function loadProducts() {
  if (products.value.length) return;
  const [d] = await getEnPlatforms({ PageSize: 500 });
  const seen = new Map<number, string>();
  (d?.data || []).forEach((p: any) => seen.set(p.productId, `${p.productCode} — ${p.productTitle}`));
  products.value = [...seen.entries()].map(([value, text]) => ({ value, text }));
}
async function classifyDialog(action: "propose" | "change" | "reclassify") {
  const titles = { propose: "پیشنهاد طبقه‌بندی (پلتفرم یا طراحی جدید)", change: "اصلاح پیشنهاد طبقه‌بندی", reclassify: "تغییر طبقه‌بندی" };
  // reclassify: the server reads only the reason (the proposal goes back to the manager with the old content and a new proposal is made afterwards)
  if (action === "reclassify") {
    dlg.ask({
      title: titles.reclassify,
      hint: "این کار پرونده را به مرحلهٔ تایید مدیر مهندسی برمی‌گرداند و تصمیم‌های پس از آن باطل می‌شود؛ نوع تازه را پس از آن مهندس یا ایجنت پیشنهاد می‌دهد.",
      fields: [{ key: "note", label: "دلیل تغییر", type: "textarea", required: true, min: 15, max: 1000 }],
      run: (v) => act("reclassify", { note: v.note }),
    });
    return;
  }
  await loadProducts();
  dlg.ask({
    title: titles[action],
    hint: "ایجنت مهندسی پیشنهاد را با مبنای آن آماده می‌کند؛ این فرم فقط برای وقتی است که مهندس خودش پیشنهاد می‌دهد یا اصلاح می‌کند.",
    fields: [
      { key: "classification", label: "نوع", type: "select", required: true, value: String(c.value.proposedClassification || ""), options: [{ value: "1", text: "پلتفرم (توربین ساخته‌شده)" }, { value: "2", text: "طراحی جدید" }] },
      { key: "productId", label: "محصول پلتفرم (برای نوع پلتفرم)", type: "select", value: c.value.proposedProductId ? String(c.value.proposedProductId) : "", options: products.value.map((p) => ({ value: String(p.value), text: p.text })) },
      { key: "withChanges", label: "با تغییرات؟", type: "select", value: c.value.proposedWithChanges ? "1" : "0", options: [{ value: "0", text: "خیر" }, { value: "1", text: "بله" }] },
      { key: "changeNote", label: "شرح تغییرات", type: "textarea", max: 1000, value: c.value.proposedChangeNote || "" },
      // the basis is read only by «propose»; «change» keeps the stored basis and appends its reason to it
      ...(action === "propose" ? [{ key: "basis", label: "مبنای پیشنهاد", type: "textarea" as const, required: true, min: 10, max: 2000, value: c.value.proposalBasis || "" }] : [{ key: "note", label: "دلیل تغییر", type: "textarea" as const, required: true, min: 15, max: 1000 }]),
    ],
    run: (v) => act(action, { classification: Number(v.classification), productId: v.productId ? Number(v.productId) : null, withChanges: v.withChanges === "1", changeNote: v.changeNote, basis: v.basis, note: v.note }),
  });
}
const reasonDialog = (action: string, title: string, label: string, okText: string) =>
  dlg.ask({ title, fields: [{ key: "note", label, type: "textarea", required: true, min: 15, max: 2000 }], run: (v) => act(action, { note: v.note }), okText });
const OTHER_STANDARD = "__OTHER_STANDARD__";   // the select option «another standard»: the code is typed (the server accepts any code for a case)
async function standardDialog() {
  const standards = await loadStandardOptions();   // approved lists only: a code outside them is handled below as «another standard»
  // a code the case already carries that is not among the approved lists is shown as «another standard» with the code in the text field (so the select is never empty)
  const current = c.value.referenceCode || "";
  const currentIsOther = !!current && !standards.some((o: any) => String(o.value) === current);
  dlg.ask({
    title: "ثبت استاندارد پروژه از دیتاشیت کارفرما",
    hint: "استاندارد را کارفرما تعیین می‌کند؛ فایل دیتاشیت بارگذاری می‌شود و کد را از فهرست انتخاب می‌کنید. شاهد فقط اشاره‌ای کوتاه به همان فایل است (شماره سند و بند). ثبت دوباره با فایل دیگر، تغییر استاندارد حساب می‌شود و دلیل می‌خواهد.",
    fields: [
      { key: "code", label: "استاندارد (مطابق دیتاشیت؛ از فهرست‌های MDL تاییدشده)", type: "select", required: true, options: [...standards, { value: OTHER_STANDARD, text: "استاندارد دیگر (هنوز فهرست MDL تاییدشده ندارد)" }], value: currentIsOther ? OTHER_STANDARD : current },
      { key: "customCode", label: "کد استاندارد طبق دیتاشیت", type: "text", required: true, max: 60, value: currentIsOther ? current : "", showWhen: { key: "code", value: OTHER_STANDARD } },
      { key: "otherNotice", type: "notice", label: "", hint: "برای این استاندارد فهرست MDL تاییدشده‌ای نیست. ایجنت مهندسی پیش‌نویس فهرست را می‌سازد و گزارش تفاوت دو استاندارد لازم می‌شود؛ تا تایید فهرست، خطر مدارک برای این پرونده باز می‌ماند.", showWhen: { key: "code", value: OTHER_STANDARD } },
      { key: "evidence", label: "اشاره به فایل (شماره سند و بند)", type: "textarea", required: true, min: 5, max: 500, value: c.value.standardEvidence || "" },
      { key: "file", label: "فایل دیتاشیت کارفرما", type: "file" as const, required: true },
      ...(c.value.referenceCode ? [{ key: "note", label: "دلیل تغییر استاندارد", type: "textarea" as const, required: true, min: 15, max: 1000 }] : []),
    ],
    run: async (v) => {
      let att = c.value.standardAttachmentId || null;
      if (v.file) {
        const [fid, fe] = await uploadDoc(v.file);
        if (fe) return [null, fe];
        att = fid;
      }
      return act("set-standard", { referenceCode: v.code === OTHER_STANDARD ? String(v.customCode || "").trim() : v.code, standardEvidence: v.evidence, standardAttachmentId: att, note: v.note });
    },
    okText: "استاندارد از دیتاشیت ثبت شد.",
  });
}
const opinionDialog = () =>
  dlg.ask({
    title: "نظر فنی مهندسی",
    hint: "برای قطعه یدکی و خدمات، نظر فنی جای تعیین نوع و تایید مدیرعامل را می‌گیرد.",
    fields: [{ key: "opinion", label: "نظر فنی", type: "textarea", required: true, min: 30, max: 4000, value: c.value.opinionText || "" }],
    run: (v) => act("save-opinion", { opinionText: v.opinion }),
    okText: "نظر فنی ذخیره شد.",
  });
// Spare parts and services: the manager chooses the handover list at the confirm (the server reads it with the confirm action).
async function confirmListDialog() {
  const [d, e] = await getEnReferenceLists({ Status: 2, PageSize: 100 });
  if (e) fail(e);
  const options = (d?.data || []).filter((l: any) => l.isCurrent).map((l: any) => ({ value: l.code, text: `${l.code} — ${l.title}` }));
  dlg.ask({
    title: "تایید مدیر مهندسی",
    hint: "فهرست مدارک تحویل این پرونده را (اختیاری) از فهرست‌های تاییدشده انتخاب کنید.",
    fields: [{ key: "code", label: "فهرست مدارک تحویل", type: "select", options, value: c.value.handoverListCode || "" }],
    run: (v) => act("confirm", { handoverListCode: v.code || null }),
    okText: "تایید مدیر مهندسی ثبت شد.",
  });
}
// Adding a tender or concept document: shown only when the server says it is allowed (canAddTenderDocument).
const tenderDocDialog = () =>
  dlg.ask({
    title: "افزودن مدرک به بسته فنی مناقصه",
    fields: [{ key: "title", label: "عنوان مدرک", required: true, min: 3, max: 200 }, { key: "file", label: "فایل", type: "file", required: true }],
    run: async (v) => {
      const [fid, fe] = await uploadDoc(v.file);
      return fe ? [null, fe] : act("add-tender-document", { documentId: fid, title: v.title });
    },
    okText: "مدرک به بسته افزوده شد.",
  });
const conceptDialog = () =>
  dlg.ask({ title: "یادداشت طرح مفهومی", fields: [{ key: "t", label: "یادداشت", type: "textarea", required: true, min: 10, max: 4000, value: c.value.conceptNote || "" }], run: (v) => act("save-concept", { conceptNote: v.t }), okText: "یادداشت ذخیره شد." });

// clarifications
const clarAct = (x: any, action: string, extra: any = {}) => postEnClarificationAction({ id: x.id, action, rowVersion: x.rowVersion, ...extra });
// A file typed into a dialog is uploaded first; the server's refusal of the file stays in the dialog.
const withFile = async (file: File | null): Promise<[string[], Error | null]> => {
  if (!file) return [[], null];
  const [fid, fe] = await uploadDoc(file);
  return fe ? [[], fe] : [[fid as string], null];
};
const newQuestion = () =>
  dlg.ask({
    title: "پرسش تازه از کارفرما",
    fields: [{ key: "q", label: "پرسش", type: "textarea", required: true, max: 4000 }, { key: "file", label: "پیوست پرسش (اختیاری)", type: "file" }],
    run: async (v) => {
      const [att, fe] = await withFile(v.file);
      return fe ? [null, fe] : saveEnClarification({ caseId: id.value, question: v.q, questionAttachments: att });
    },
    okText: "پرسش ذخیره شد.",
  });
const sendDialog = (x: any) =>
  dlg.ask({ title: "ثبت ارسال به کارفرما", fields: [{ key: "ref", label: "شماره نامه ارسالی", required: true, max: 100 }], run: (v) => clarAct(x, "send", { clientLetterRef: v.ref }), okText: "ارسال به کارفرما ثبت شد." });
const answerDialog = (x: any) =>
  dlg.ask({
    title: "ثبت پاسخ کارفرما",
    fields: [{ key: "a", label: "پاسخ", type: "textarea", required: true, max: 4000 }, { key: "file", label: "نامهٔ پاسخ کارفرما (پیوست، اختیاری)", type: "file" }],
    run: async (v) => {
      const [att, fe] = await withFile(v.file);
      return fe ? [null, fe] : clarAct(x, "answer", { answer: v.a, answerAttachments: att });
    },
    okText: "پاسخ ثبت شد.",
  });
const reaskDialog = (x: any) =>
  dlg.ask({ title: "پرسش مجدد", fields: [{ key: "q", label: "پرسش تازه", type: "textarea", required: true, max: 4000 }], run: (v) => clarAct(x, "reask", { question: v.q }), okText: "پرسش مجدد ثبت شد." });
const voidClar = (x: any) =>
  dlg.ask({ title: "ابطال شفاف‌سازی", fields: [{ key: "n", label: "دلیل", type: "textarea", required: true, min: 15, max: 2000 }], run: (v) => clarAct(x, "void", { note: v.n }), okText: "شفاف‌سازی باطل شد." });

// registration of a new design as a platform
// Whether the registration may be proposed is the server's decision (canProposeRegistration / proposeRegistrationBlockedReason on the case); without the flag nothing is shown.
async function registerDialog() {
  await loadProducts();
  const standards = await loadStandardOptions(c.value.referenceCode);
  dlg.ask({
    title: "پیشنهاد ثبت به‌عنوان پلتفرم",
    hint: "مدیر مهندسی پیشنهاد می‌دهد و مدیرعامل تصمیم می‌گیرد.",
    fields: [
      { key: "productId", label: "محصول", type: "select", required: true, options: products.value.map((p) => ({ value: String(p.value), text: p.text })) },
      { key: "code", label: "استاندارد (از فهرست‌های MDL تاییدشده)", type: "select", required: true, options: standards, value: c.value.referenceCode || "" },
      { key: "basis", label: "مبنای استاندارد (دیتاشیت و بند)", required: true, min: 5, max: 500, value: c.value.standardEvidence || "" },
      { key: "why", label: "توجیه ثبت", type: "textarea", required: true, min: 30, max: 2000 },
    ],
    run: (v) => proposeEnRegistration({ caseId: id.value, productId: Number(v.productId), referenceCode: v.code, standardBasis: v.basis, rationale: v.why }),
    okText: "پیشنهاد ثبت پلتفرم ثبت شد.",
  });
}
const regDecide = (r: any, decision: string) =>
  dlg.ask({
    title: decision === "approve" ? "تایید ثبت پلتفرم" : decision === "reject" ? "رد ثبت پلتفرم" : "پس‌گرفتن پیشنهاد",
    fields: [{ key: "n", label: "یادداشت", type: "textarea", required: decision !== "approve", min: decision === "approve" ? undefined : 15, max: 2000 }],
    run: (v) => decideEnRegistration({ id: r.id, decision, note: v.n, rowVersion: r.rowVersion }),
    okText: "تصمیم ثبت شد.",
  });

async function toggleCharter() {
  showCharter.value = !showCharter.value;
  if (showCharter.value && !charter.value) {
    const [d, e] = await getEnCharterView(id.value);
    if (e) fail(e);
    else charter.value = d;
  }
}
const charterSections = [
  ["overview", "کلیات"], ["scopeDescription", "دامنه"], ["requirements", "الزامات"], ["deliverables", "تحویل‌دادنی‌ها"], ["boundaries", "مرزها"], ["timeBaselineEstimate", "برآورد زمان پایه"],
  ["alternativesAnalysis", "تحلیل گزینه‌ها"], ["assumptions", "فرض‌ها"], ["constraints", "محدودیت‌ها"], ["risks", "ریسک‌ها"], ["stakeholders", "ذی‌نفعان"], ["preAssignedResources", "منابع از پیش تعیین‌شده"],
  ["managerAuthority", "اختیارات مدیر پروژه"],
];
const cls = (done: boolean, wait = false) => ({ done, wait });
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">پرونده مهندسی <span v-if="c">{{ faDigits(c.number) }}</span></h1>
        <router-link :to="enPaths.cases" class="btn btn-sm btn-outline-secondary">فهرست پرونده‌ها</router-link>
      </div>
      <p v-if="loading && !c" class="inv-muted">در حال خواندن…</p>
      <div v-if="message" class="en-banner red" role="alert">{{ message }}</div>

      <template v-if="c">
        <div class="d-flex flex-wrap gap-2 align-items-center mb-2">
          <span class="fw-bold">{{ c.title }}</span>
          <span class="inv-badge info" data-test="en-kind">{{ c.kindText || caseKinds[c.kind] }}</span>
          <EnChip :status="c.status" :text="c.statusText" group="case" />
          <span v-if="c.customerName" class="small inv-muted">مشتری: {{ c.customerName }}</span>
        </div>
        <dl class="en-kv mb-3">
          <dt>استعلام</dt><dd>{{ c.inquiryNumber ? faDigits(c.inquiryNumber) : "—" }}</dd>
          <dt>قرارداد</dt><dd>{{ c.contractCode ? faDigits(c.contractCode) : "—" }}</dd>
          <dt v-if="c.customerRequirementNote">الزام مشتری</dt><dd v-if="c.customerRequirementNote">{{ c.customerRequirementNote }}</dd>
        </dl>

        <div v-if="c.status === 2 || c.status === 3" class="en-banner amber" data-test="en-case-wait">
          این پرونده {{ c.status === 2 ? "منتظر تایید مدیر مهندسی" : "منتظر تایید مدیرعامل" }} است؛ درخواست منقضی نمی‌شود و تا تصمیم گرفته شود هشدار می‌ماند.
        </div>
        <div v-if="c.returnReason" class="en-banner amber">برگشت‌خورده: {{ c.returnReason }}</div>
        <div v-if="sideError" class="en-banner amber" role="status">{{ sideError }}</div>
        <div v-if="c.documentRisk" :class="['en-banner', c.documentRiskAwarded ? 'red' : 'amber']" data-test="en-risk-banner">
          خطر مدارک{{ c.documentRiskAwarded ? " (پرونده برنده شده)" : "" }}: {{ c.documentRisk }}
        </div>

        <h2 class="h6">استاندارد پروژه (از دیتاشیت کارفرما)</h2>
        <div class="en-box" data-test="en-standard">
          <div v-if="c.standardNotice" class="en-banner amber mb-2" role="status" data-test="en-standard-notice">{{ c.standardNotice }}</div>
          <div v-if="c.standardMissing" class="en-banner amber mb-2">استاندارد این پروژه هنوز از دیتاشیت کارفرما ثبت نشده است.</div>
          <dl v-else class="en-kv mb-2"><dt>کد استاندارد</dt><dd class="inv-ltr">{{ c.referenceCode }}</dd><dt>شاهد</dt><dd>{{ c.standardEvidence }}<a v-if="c.standardAttachmentId" href="#" class="ms-2" @click.prevent="downloadDoc(c.standardAttachmentId, 'datasheet')">دیدن دیتاشیت</a></dd></dl>
          <EnAction :can="c.canSetStandard" :reason="c.setStandardBlockedReason" :roles="EnRoles.standardSetters" :label="c.referenceCode ? 'تغییر استاندارد (با دلیل)' : 'ثبت از دیتاشیت'" variant="outline-primary" @go="standardDialog" />
        </div>

        <ol class="en-timeline mt-3">
          <!-- 1 -->
          <li :class="['en-step', cls(c.status >= 4, c.status === 2 || c.status === 3)]">
            <h3>۱) {{ c.kind === 1 ? "تشخیص پلتفرم یا طراحی جدید" : "نظر فنی مهندسی" }}</h3>
            <template v-if="c.kind === 1">
              <div class="en-box">
                <dl class="en-kv">
                  <dt>طبقه‌بندی فعلی</dt><dd>{{ c.classificationText }}<span v-if="c.productCode"> — {{ c.productCode }}</span></dd>
                  <dt>پیشنهاد</dt><dd>{{ c.proposedClassificationText }}<span v-if="c.proposedProductCode"> — {{ c.proposedProductCode }}</span><span v-if="c.proposedWithChanges"> (با تغییرات)</span> <span v-if="c.proposedByAgent" class="inv-badge info">پیشنهاد ایجنت</span><span v-else-if="c.proposedByName"> {{ c.proposedByName }}</span></dd>
                  <dt>مبنا</dt><dd class="en-pre">{{ c.proposalBasis || "—" }}</dd>
                  <dt v-if="c.proposedChangeNote">تغییرات</dt><dd v-if="c.proposedChangeNote" class="en-pre">{{ c.proposedChangeNote }}</dd>
                  <dt>تایید مدیر مهندسی</dt><dd>{{ c.engConfirmedByName ? c.engConfirmedByName + "، " + faDigits(c.engConfirmedAtText || "") : "—" }}</dd>
                  <dt>تایید مدیرعامل</dt><dd>{{ c.ceoApprovedByName ? c.ceoApprovedByName + "، " + faDigits(c.ceoApprovedAtText || "") : "—" }}</dd>
                </dl>
              </div>
              <div v-if="c.platformIncomplete" class="en-banner amber" data-test="en-point-a">
                هشدار نقطه الف (مانع نیست): {{ c.platformWarning || "پرونده مدارک این پلتفرم کامل نیست." }}
                <router-link v-if="c.productId" :to="enPaths.platform(c.productId, c.referenceCode)">دیدن پرونده مدارک پلتفرم</router-link>
              </div>
              <EnAction :can="c.canPropose" :reason="c.proposeBlockedReason" :roles="EnRoles.writers" label="پیشنهاد طبقه‌بندی" variant="outline-primary" @go="classifyDialog('propose')" />
              <EnAction :can="c.canChange" :reason="c.changeBlockedReason" :roles="EnRoles.writers" label="اصلاح پیشنهاد" variant="outline-primary" @go="classifyDialog('change')" />
              <EnAction :can="c.canConfirm" :reason="c.confirmBlockedReason" :roles="EnRoles.manager" :confirm="`پیشنهاد طبقه‌بندی پروندهٔ ${faDigits(c.number)} تایید شود؟ پس از آن پرونده منتظر تصمیم مدیرعامل می‌ماند.`" label="تایید مدیر مهندسی" variant="success" @go="direct('confirm', 'تایید مدیر مهندسی ثبت شد.')" />
              <EnAction :can="c.canApprove" :reason="c.approveBlockedReason" :roles="EnRoles.ceo" :confirm="`طبقه‌بندی پروندهٔ ${faDigits(c.number)} تایید نهایی شود؟ پس از تایید مدیرعامل پرونده وارد مرحلهٔ بعد می‌شود.`" label="تایید مدیرعامل" variant="success" @go="direct('approve', 'تایید مدیرعامل ثبت شد.')" />
              <EnAction :can="c.canReturn" :reason="c.returnBlockedReason" :roles="[...EnRoles.manager, ...EnRoles.ceo]" label="برگشت با دلیل" variant="outline-danger" @go="reasonDialog('return', 'برگشت پیشنهاد', 'دلیل برگشت', 'پرونده برگشت داده شد.')" />
              <EnAction :can="c.canReclassify" :reason="c.reclassifyBlockedReason" :roles="EnRoles.manager" label="تغییر طبقه‌بندی" variant="outline-secondary" @go="classifyDialog('reclassify')" />
            </template>
            <template v-else>
              <div class="en-box">
                <dl class="en-kv"><dt>نظر فنی</dt><dd class="en-pre">{{ c.opinionText || "هنوز ثبت نشده" }}</dd><dt>ثبت‌کننده</dt><dd>{{ c.opinionByName || "—" }} {{ faDigits(c.opinionAtText || "") }}</dd>
                  <dt>تایید مدیر مهندسی</dt><dd>{{ c.engConfirmedByName ? c.engConfirmedByName + "، " + faDigits(c.engConfirmedAtText || "") : "—" }}</dd></dl>
              </div>
              <EnAction :can="c.canSaveOpinion" :reason="c.saveOpinionBlockedReason" :roles="EnRoles.writers" label="ثبت نظر فنی" variant="outline-primary" @go="opinionDialog" />
              <EnAction :can="c.canConfirm" :reason="c.confirmBlockedReason" :roles="EnRoles.manager" label="تایید مدیر مهندسی" variant="success" @go="confirmListDialog" />
              <EnAction :can="c.canReturn" :reason="c.returnBlockedReason" :roles="EnRoles.manager" label="برگشت با دلیل" variant="outline-danger" @go="reasonDialog('return', 'برگشت نظر فنی', 'دلیل برگشت', 'پرونده برگشت داده شد.')" />
            </template>
            <h4 v-if="gap" class="h6 mt-2">تفاوت استاندارد</h4>
            <EnGapView v-if="gap" :gap="gap" link @changed="load" />
            <p v-else-if="c.standardGap" class="small">{{ c.standardGap.number }}: {{ c.standardGap.summary }}</p>
          </li>

          <!-- 2 -->
          <li :class="['en-step', cls(c.status >= 5, c.status === 4)]">
            <h3>۲) بسته فنی مناقصه</h3>
            <div class="en-box">
              <dl class="en-kv">
                <dt>بسته</dt><dd>{{ c.tenderPackageCode ? faDigits(c.tenderPackageCode) : "هنوز ساخته نشده" }} — {{ faNum(c.tenderDocumentCount, 0) }} مدرک</dd>
                <dt>انتشار</dt><dd>{{ c.tenderReleasedByName ? c.tenderReleasedByName + "، " + faDigits(c.tenderReleasedAtText || "") : "—" }}</dd>
                <dt>یادداشت طرح مفهومی</dt><dd class="en-pre">{{ c.conceptNote || "—" }}</dd>
              </dl>
              <template v-if="c.tenderDocuments?.length">
                <div class="small mt-2"><strong>مدارک بسته:</strong></div>
                <ul class="small mb-0"><li v-for="(t, i) in c.tenderDocuments" :key="t.documentId || i"><a v-if="t.documentId" href="#" @click.prevent="downloadDoc(t.documentId, t.title || 'document')">{{ t.title }}</a><span v-else>{{ t.title }}</span><span v-if="t.revision" class="inv-ltr"> — {{ t.revision }}</span><span v-if="t.issuedAtText" class="inv-muted"> — {{ faDigits(t.issuedAtText) }}</span><span v-if="t.addedByAgent" class="inv-badge info ms-1">افزوده‌شده توسط ایجنت</span></li></ul>
              </template>
              <div v-if="c.tenderStale" class="en-banner amber mt-2 mb-0">محتوای بسته پس از آخرین بررسی تغییر کرده است؛ ایجنت آن را دوباره بررسی می‌کند.</div>
            </div>
            <EnAction :can="c.canSaveConcept" :reason="c.saveConceptBlockedReason" :roles="EnRoles.writers" label="ثبت یادداشت طرح مفهومی" variant="outline-primary" @go="conceptDialog" />
            <EnAction :can="c.canAddTenderDocument" :reason="c.addTenderDocumentBlockedReason" :roles="EnRoles.writers" label="افزودن مدرک طرح مفهومی یا بسته مناقصه" variant="outline-primary" @go="tenderDocDialog" />
            <EnAction :can="c.canReleaseTender" :reason="c.releaseTenderBlockedReason" :roles="EnRoles.manager" :confirm="`بسته فنی پروندهٔ ${faDigits(c.number)} برای بازرگانی منتشر شود؟ پس از انتشار مانع مهندسی برای تایید پیشنهاد برداشته می‌شود.`" label="انتشار بسته فنی" variant="success" @go="direct('release-tender', 'بسته فنی منتشر شد.')" />
          </li>

          <!-- 3 -->
          <li id="clarifications" :class="['en-step', cls(clars.length > 0 && clars.every((x) => x.status >= 4), clars.some((x) => x.status < 4))]">
            <h3>۳) شفاف‌سازی با کارفرما (از طریق بازرگانی)</h3>
            <p v-if="!clars.length" class="inv-muted">شفاف‌سازی ثبت نشده است.</p>
            <div v-for="x in clars" :key="x.id" class="en-box" data-test="en-clar">
              <div class="d-flex flex-wrap gap-2 align-items-center mb-1"><strong>{{ faDigits(x.number) }}</strong><EnChip :status="x.status" :text="x.statusText || clarStatuses[x.status]" group="clar" /><span class="small inv-muted">{{ x.askedByName }} — {{ faDigits(x.askedAtText || "") }}</span></div>
              <p class="en-pre mb-1">{{ x.question }}</p>
              <dl class="en-kv small">
                <dt>ارسال به کارفرما (بازرگانی)</dt><dd>{{ x.sentToClientByName ? x.sentToClientByName + "، " + faDigits(x.sentToClientAtText || "") : "—" }}</dd>
                <dt>شماره نامه</dt><dd>{{ x.clientLetterRef ? faDigits(x.clientLetterRef) : "—" }}</dd>
                <dt>پاسخ کارفرما</dt><dd class="en-pre">{{ x.answer || "—" }}</dd>
                <dt v-if="x.questionAttachments?.length">پیوست پرسش</dt><dd v-if="x.questionAttachments?.length"><a v-for="(a, i) in x.questionAttachments" :key="a" class="me-2" href="#" @click.prevent="downloadDoc(a, 'question-' + (i + 1))">فایل {{ faNum(i + 1, 0) }}</a></dd>
                <dt v-if="x.answerAttachments?.length">پیوست پاسخ</dt><dd v-if="x.answerAttachments?.length"><a v-for="(a, i) in x.answerAttachments" :key="a" class="me-2" href="#" @click.prevent="downloadDoc(a, 'answer-' + (i + 1))">فایل {{ faNum(i + 1, 0) }}</a></dd>
              </dl>
              <EnAction :can="x.canSubmit" :reason="x.submitBlockedReason" :roles="EnRoles.writers" :confirm="`پرسش ${faDigits(x.number)} به بازرگانی ارسال شود؟ بازرگانی آن را برای کارفرما می‌فرستد.`" label="ارسال به بازرگانی" variant="outline-primary" @go="runEn(() => clarAct(x, 'submit'), 'به بازرگانی ارسال شد.', load)" />
              <EnAction :can="x.canSend" :reason="x.sendBlockedReason" :roles="EnRoles.commercial" label="ثبت ارسال به کارفرما" variant="outline-primary" @go="sendDialog(x)" />
              <EnAction :can="x.canAnswer" :reason="x.answerBlockedReason" :roles="EnRoles.commercial" label="ثبت پاسخ کارفرما" variant="outline-primary" @go="answerDialog(x)" />
              <EnAction :can="x.canClose" :reason="x.closeBlockedReason" :roles="EnRoles.writers" :confirm="`شفاف‌سازی ${faDigits(x.number)} بسته شود؟ پس از بستن پاسخ کارفرما نهایی می‌شود.`" label="بستن" variant="success" @go="runEn(() => clarAct(x, 'close'), 'شفاف‌سازی بسته شد.', load)" />
              <EnAction :can="x.canReask" :reason="x.reaskBlockedReason" :roles="EnRoles.writers" label="پرسش مجدد" variant="outline-secondary" @go="reaskDialog(x)" />
              <EnAction :can="x.canVoid" :reason="x.voidBlockedReason" :roles="EnRoles.writers" label="ابطال" variant="outline-danger" @go="voidClar(x)" />
            </div>
            <EnAction :can="c.canAskClarification" reason="برای این وضعیت پرونده پرسش ثبت نمی‌شود." :roles="EnRoles.writers" label="پرسش تازه" variant="outline-primary" @go="newQuestion" />
          </li>

          <!-- 4 -->
          <li :class="['en-step', cls(c.charterReviewOutcome === 1, c.charterExists && c.charterReviewOutcome === 0)]">
            <h3>۴) تایید منشور پروژه</h3>
            <div class="en-box">
              <dl class="en-kv">
                <dt>نتیجه بررسی</dt><dd>{{ c.charterReviewOutcomeText || "—" }} <span v-if="c.charterReviewedByName">{{ c.charterReviewedByName }}، {{ faDigits(c.charterReviewedAtText || "") }}</span></dd>
                <dt v-if="c.charterReviewNote">یادداشت</dt><dd v-if="c.charterReviewNote" class="en-pre">{{ c.charterReviewNote }}</dd>
              </dl>
              <div v-if="c.charterStale" class="en-banner amber mt-2 mb-0">منشور پس از بررسی تغییر کرده است؛ بررسی دوباره لازم است.</div>
              <button v-if="c.charterExists" class="btn btn-sm btn-link px-0" type="button" @click="toggleCharter">{{ showCharter ? "بستن منشور" : "نمایش منشور (بدون قیمت)" }}</button>
              <div v-if="showCharter && charter" class="en-box" data-test="en-charter">
                <template v-for="s in charterSections" :key="s[0]"><div v-if="charter[s[0]]"><strong>{{ s[1] }}</strong><p class="en-pre">{{ charter[s[0]] }}</p></div></template>
                <p v-if="charter.managerAcceptedAtText || charter.approvedAtText" class="small inv-muted">{{ charter.managerAcceptedAtText ? "پذیرش مدیر پروژه: " + faDigits(charter.managerAcceptedAtText) + "  " : "" }}{{ charter.approvedAtText ? "تصویب: " + faDigits(charter.approvedAtText) : "" }}</p>
                <p v-if="charter.priceNotice" class="small inv-muted">{{ charter.priceNotice }}</p>
              </div>
            </div>
            <EnAction :can="c.canCharterReview" :reason="c.charterReviewBlockedReason" :roles="EnRoles.manager" :confirm="`منشور پروژهٔ پروندهٔ ${faDigits(c.number)} تایید شود؟ تحویل مدارک به PMO بدون این تایید ساخته نمی‌شود.`" label="تایید منشور" variant="success" @go="direct('charter-confirm', 'منشور تایید شد.')" />
            <EnAction :can="c.canCharterReview" :reason="c.charterReviewBlockedReason" :roles="EnRoles.manager" label="برگشت منشور" variant="outline-danger" @go="reasonDialog('charter-return', 'برگشت منشور', 'دلیل برگشت منشور', 'منشور برگشت داده شد.')" />
          </li>

          <!-- 5 -->
          <li :class="['en-step', cls(c.status >= 9, c.status === 8)]">
            <h3>۵) تحویل مدارک به PMO</h3>
            <p v-if="!handovers.length" class="inv-muted">هنوز دور تحویلی ساخته نشده است؛ ایجنت مهندسی پیش‌نویس را می‌سازد.</p>
            <ul class="mb-2">
              <li v-for="h in handovers" :key="h.id"><router-link :to="enPaths.handover(h.id)">دور {{ faNum(h.round, 0) }} — {{ faDigits(h.number) }}</router-link> <EnChip :status="h.status" :text="h.statusText || handoverStatuses[h.status]" group="handover" /><div v-if="h.incompleteNotice" class="small text-warning-emphasis" data-test="en-case-incomplete">{{ h.incompleteNotice }}</div></li>
            </ul>
            <div v-for="r in regs" :key="r.id" class="en-box" data-test="en-reg">
              <strong>ثبت به‌عنوان پلتفرم {{ faDigits(r.number) }}</strong> <EnChip :status="r.status" :text="r.statusText" group="reg" />
              <p class="small mb-1">{{ r.productCode }} / {{ r.referenceCode }} — {{ r.rationale }}</p>
              <EnWait v-if="r.status === 0" :hours="r.waitingHours" text="پیشنهاد ثبت پلتفرم منتظر مدیرعامل است" />
              <EnAction :can="r.canDecide" :reason="r.decideBlockedReason" :roles="EnRoles.ceo" label="تایید ثبت" variant="success" @go="regDecide(r, 'approve')" />
              <EnAction :can="r.canDecide" :reason="r.decideBlockedReason" :roles="EnRoles.ceo" label="رد ثبت" variant="outline-danger" @go="regDecide(r, 'reject')" />
              <EnAction :can="r.canWithdraw" :reason="r.withdrawBlockedReason" :roles="EnRoles.manager" label="پس‌گرفتن" variant="outline-secondary" @go="regDecide(r, 'withdraw')" />
            </div>
            <EnAction :can="c.canProposeRegistration" :reason="c.proposeRegistrationBlockedReason" :roles="EnRoles.manager" label="پیشنهاد ثبت این طراحی به‌عنوان پلتفرم" variant="outline-primary" @go="registerDialog" />
          </li>
        </ol>

        <template v-if="canAskPmo && c.contractId">
          <h2 class="h6 mt-3">پرسش از PMO</h2>
          <EnInfoList :case-id="c.id" />
        </template>

        <EnAction :can="c.canCancel" :reason="c.cancelBlockedReason" :roles="EnRoles.manager" label="لغو پرونده" variant="outline-danger" @go="reasonDialog('cancel', 'لغو پرونده', 'دلیل لغو', 'پرونده لغو شد.')" />

        <h2 class="h6 mt-3">تاریخچه رویدادها</h2>
        <p v-if="!c.events?.length" class="inv-muted">رویدادی ثبت نشده است.</p>
        <ul class="small">
          <li v-for="(ev, i) in c.events" :key="i">{{ faDigits(ev.atText || "") }} — {{ ev.userName || "سیستم" }} — {{ eventKinds[ev.kind] || ev.kind }}<span v-if="ev.note">: {{ ev.note }}</span></li>
        </ul>
      </template>
    </div>
    <EnPrompt v-if="dlg.dlg.value" :busy="dlg.busy.value" :confirm-text="dlg.dlg.value.confirmText" :error="dlg.error.value" :fields="dlg.dlg.value.fields" :hint="dlg.dlg.value.hint" :title="dlg.dlg.value.title" @cancel="dlg.close" @confirm="dlg.confirm" />
  </div>
</template>
