<script setup lang="ts">
// The MDL of the PROJECT in the tender (employer rule 20) and the CEO's permit «tender without MDL». The agent drafts the list from the approved standard list; an engineer replaces its lines
// from a CSV file (data enters only by file, never by typing a line); the engineering manager approves; commercial records the client's approval. Every button takes its state and its reason
// from the server DTO (can<Action> / <action>BlockedReason, a missing flag is false); the page never re-implements a rule. Every one-click write asks for a confirmation (EnAction confirm).
import { computed, onMounted, ref, watch } from "vue";
import EnAction from "@/components/engineering/EnAction.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import EnPrompt from "@/components/engineering/EnPrompt.vue";
import {
  decideEnMdlPermit, downloadEnProjectMdlCsv, draftEnProjectMdl, draftEnProjectMdlFromFile, getEnProjectMdl, postEnProjectMdlAction, requestEnMdlPermit, uploadEnProjectMdlLines, withdrawEnMdlPermit,
} from "@/api/EngineeringAPIs";
import { downloadDoc, enWriting, EnRoles, faDigits, faNum, mdlStatuses, runEn, unitsText, uploadDoc, useEnDialog } from "@/composables/engineering";
import { fail } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";

const props = defineProps<{ caseId: number; caseNumber?: string }>();
const emit = defineEmits<{ (e: "changed"): void }>();

const d = ref<any>(null);
const readable = hasRole(EnRoles.mdlReaders);
const loading = ref(false);
const loadError = ref("");

async function load() {
  if (!readable) return;
  loading.value = true;
  const [r, e] = await getEnProjectMdl(props.caseId);
  loading.value = false;
  loadError.value = e ? e.message || "فهرست مدارک پروژه خوانده نشد." : "";
  d.value = e ? null : r;
}
// A change of this section can change the tender lock of the case, so the page is told to read the case again.
async function reload() {
  await load();
  emit("changed");
}
onMounted(load);
watch(() => props.caseId, load);
const dlg = useEnDialog(reload);

const m = computed(() => d.value?.mdl || null);
const permit = computed(() => d.value?.permit || null);
const lines = computed<any[]>(() => m.value?.lines || []);
const label = computed(() => faDigits(props.caseNumber || ""));
const visible = computed(() => !!d.value && (!!d.value.applies || !!m.value || !!permit.value));
const num = (v: any) => faNum(Number(v || 0), 0);

const act = (action: string, extra: any = {}) => postEnProjectMdlAction(m.value.id, action, { rowVersion: m.value.rowVersion, ...extra });
const csvName = computed(() => "MDL-" + (d.value?.caseNumber || props.caseId) + ".csv");
async function downloadCsv() {
  const e = await downloadEnProjectMdlCsv(m.value.id);
  if (e) fail(e);
}

const csvHint = "فایل CSV با همان ستون‌های فایل فهرست استاندارد (docType,title,discipline,required,tender,units,clientSubmittal,clause,note). ردیف‌ها فقط با فایل وارد می‌شوند؛ فایل را از «دانلود فایل فهرست (CSV)» بگیرید، ویرایش کنید و دوباره بارگذاری کنید.";
const draftFromFileDialog = () =>
  dlg.ask({
    title: "ساخت فهرست مدارک پروژه از فایل",
    hint: "برای این پرونده فهرست استاندارد تاییدشده‌ای نیست؛ همهٔ ردیف‌ها «افزوده برای پروژه» ثبت می‌شوند. " + csvHint,
    fields: [
      { key: "file", label: "فایل فهرست (CSV)", type: "file", required: true },
      { key: "note", label: "دلیل ساخت از فایل (حداقل ۱۵ نویسه)", type: "textarea", required: true, min: 15, max: 2000 },
    ],
    run: (v) => draftEnProjectMdlFromFile(props.caseId, v.note, v.file),
    okText: "پیش‌نویس فهرست مدارک پروژه از فایل ساخته شد.",
  });
const uploadDialog = () =>
  dlg.ask({
    title: "جایگزینی ردیف‌ها از فایل",
    hint: "همهٔ ردیف‌های فهرست با ردیف‌های فایل جایگزین می‌شود: ردیف استاندارد که در فایل نباشد «برای این پروژه حذف شده» می‌ماند و ردیف تازه «افزوده برای پروژه» می‌شود. پس از جایگزینی، فهرست دوباره پیش‌نویس است و باید دوباره بررسی شود. " + csvHint,
    fields: [
      { key: "file", label: "فایل فهرست (CSV)", type: "file", required: true },
      { key: "note", label: "دلیل جایگزینی (حداقل ۱۵ نویسه)", type: "textarea", required: true, min: 15, max: 2000 },
    ],
    run: (v) => uploadEnProjectMdlLines(m.value.id, m.value.rowVersion, v.note, v.file),
    okText: "ردیف‌های فهرست از فایل جایگزین شد.",
  });
const returnDialog = () =>
  dlg.ask({
    title: "برگشت فهرست مدارک پروژه",
    hint: "فهرست به پیش‌نویس برمی‌گردد و کارشناس باید دوباره آن را بررسی کند.",
    fields: [{ key: "reason", label: "دلیل برگشت (حداقل ۱۵ نویسه)", type: "textarea", required: true, min: 15, max: 1000 }],
    run: (v) => act("return", { reason: v.reason }),
    okText: "فهرست برگشت داده شد.",
  });
const voidDialog = () =>
  dlg.ask({
    title: "ابطال فهرست مدارک پروژه",
    hint: "پس از ابطال می‌توان پیش‌نویس تازه ساخت. فهرست باطل‌شده حذف نمی‌شود و در تاریخچه می‌ماند.",
    fields: [{ key: "reason", label: "دلیل ابطال (حداقل ۱۵ نویسه)", type: "textarea", required: true, min: 15, max: 1000 }],
    run: (v) => act("void", { reason: v.reason }),
    okText: "فهرست باطل شد.",
  });
const clientDialog = () => {
  const correcting = !!m.value.clientApprovedAt;
  dlg.ask({
    title: correcting ? "اصلاح تایید کارفرما" : "ثبت تایید کارفرما",
    hint: correcting ? "تایید کارفرما یک‌بار ثبت شده است؛ اصلاح با دلیل انجام می‌شود و مقدار پیشین در سابقهٔ پرونده می‌ماند." : "فهرست مدارک پروژه همراه مدارک مناقصه برای کارفرما رفته است؛ شماره و فایل نامهٔ تایید کارفرما را ثبت کنید.",
    fields: [
      { key: "ref", label: "شمارهٔ نامهٔ کارفرما", required: true, max: 100, value: correcting ? m.value.clientApprovalRef || "" : "" },
      { key: "file", label: "نامهٔ تایید کارفرما (فایل)", type: "file", required: true },
      ...(correcting ? [{ key: "note", label: "دلیل اصلاح (حداقل ۱۵ نویسه)", type: "textarea" as const, required: true, min: 15, max: 1000 }] : []),
    ],
    run: async (v) => {
      const [fid, fe] = await uploadDoc(v.file);
      return fe ? [null, fe] : act("record-client-approval", { letterRef: v.ref, documentId: fid, note: v.note });
    },
    okText: "تایید کارفرما ثبت شد.",
  });
};
const permitRequestDialog = () =>
  dlg.ask({
    title: "درخواست مناقصه بدون MDL",
    hint: "فقط برای پروژهٔ خاص است؛ مدیرعامل تصمیم می‌گیرد و پس از تایید، بستهٔ فنی مناقصه بدون فهرست مدارک پروژه منتشر می‌شود.",
    fields: [{ key: "reason", label: "دلیل درخواست (حداقل ۵۰ نویسه)", type: "textarea", required: true, min: 50, max: 2000 }],
    run: (v) => requestEnMdlPermit(props.caseId, v.reason),
    okText: "درخواست مجوز ثبت شد.",
  });
const permitRejectDialog = () =>
  dlg.ask({
    title: "رد مجوز مناقصه بدون MDL",
    fields: [{ key: "note", label: "دلیل رد (حداقل ۱۵ نویسه)", type: "textarea", required: true, min: 15, max: 1000 }],
    run: (v) => decideEnMdlPermit(props.caseId, permit.value.id, false, v.note),
    okText: "مجوز رد شد.",
  });
const permitApprove = () => runEn(() => decideEnMdlPermit(props.caseId, permit.value.id, true, ""), "مجوز تایید شد.", reload);
// an approved permit that a new decision of the case made stale is withdrawn with a reason (at least 15); a requested one with one click
const permitIsApproved = computed(() => permit.value?.status === 1);
const permitWithdrawDialog = () =>
  dlg.ask({
    title: "پس‌گرفتن مجوز مناقصه بدون MDL",
    hint: "این مجوز پیش از آخرین تصمیم پرونده داده شده و دیگر بستهٔ فنی را باز نمی‌کند. پس از پس‌گرفتن می‌توانید مجوز تازه بخواهید یا فهرست مدارک پروژه را بسازید.",
    fields: [{ key: "note", label: "دلیل پس‌گرفتن (حداقل ۱۵ نویسه)", type: "textarea", required: true, min: 15, max: 1000 }],
    run: (v) => withdrawEnMdlPermit(props.caseId, permit.value.id, v.note),
    okText: "مجوز پس گرفته شد.",
  });
const permitWithdraw = () => (permitIsApproved.value ? permitWithdrawDialog() : runEn(() => withdrawEnMdlPermit(props.caseId, permit.value.id, ""), "درخواست مجوز پس گرفته شد.", reload));
const draft = () => runEn(() => draftEnProjectMdl(props.caseId), "پیش‌نویس فهرست مدارک پروژه ساخته شد.", reload);
const simple = (action: string, okText: string) => runEn(() => act(action), okText, reload);
</script>

<template>
  <div v-if="readable" data-test="en-pmdl">
    <p v-if="loading && !d" class="inv-muted">در حال خواندن…</p>
    <div v-if="loadError" class="en-banner amber" role="status">{{ loadError }}</div>
    <template v-if="d">
      <p v-if="!visible && d.notApplicableText" class="inv-muted small" data-test="en-pmdl-na">{{ d.notApplicableText }}</p>
      <div v-if="d.releaseLockText" class="en-banner amber" data-test="en-pmdl-lock">{{ d.releaseLockText }}</div>

      <div v-if="m" class="en-box" data-test="en-pmdl-box">
        <div class="d-flex flex-wrap gap-2 align-items-center mb-2">
          <strong>{{ faDigits(m.number) }}</strong>
          <span class="small">ویرایش {{ num(m.revision) }}</span>
          <EnChip :status="m.status" :text="m.statusText || mdlStatuses[m.status]" group="mdl" />
          <span v-if="m.approvalStale" class="inv-badge bad">تایید با محتوا یا تصمیم فعلی پرونده نمی‌خواند</span>
        </div>
        <dl class="en-kv">
          <dt>فهرست استاندارد مبنا</dt>
          <dd><template v-if="m.basedOnCode"><span class="inv-ltr">{{ m.basedOnCode }}</span> — ویرایش {{ num(m.basedOnRevision) }}</template><template v-else>بدون فهرست استاندارد (ساخته‌شده از فایل)</template></dd>
          <dt>ردیف‌ها</dt>
          <dd data-test="en-pmdl-counts">استاندارد نگه‌داشته‌شده {{ num(m.standardKeptCount) }}، حذف‌شده برای این پروژه {{ num(m.removedCount) }}، افزوده برای پروژه {{ num(m.addedCount) }} (جمع ردیف‌های فعال {{ num(m.lineCount) }})</dd>
          <dt v-if="m.sourceDocumentId">فایل مبنای ردیف‌ها</dt>
          <dd v-if="m.sourceDocumentId"><a href="#" @click.prevent="downloadDoc(m.sourceDocumentId, m.sourceFileName || 'mdl-source.csv')">{{ m.sourceFileName || "دیدن فایل" }}</a></dd>
          <dt v-if="m.changeNote">یادداشت تغییر</dt>
          <dd v-if="m.changeNote" class="en-pre">{{ m.changeNote }}</dd>
          <dt>بررسی کارشناس</dt>
          <dd>{{ m.checkedByName ? m.checkedByName + "، " + faDigits(m.checkedAtText || "") : "—" }}</dd>
          <dt>تایید مدیر مهندسی</dt>
          <dd>{{ m.approvedByName ? m.approvedByName + "، " + faDigits(m.approvedAtText || "") : "—" }}</dd>
          <dt v-if="m.issuedDocumentId">فایل تاییدشده</dt>
          <dd v-if="m.issuedDocumentId"><a href="#" @click.prevent="downloadDoc(m.issuedDocumentId, m.issuedFileName || 'mdl.csv')">{{ m.issuedFileName || "دیدن فایل تاییدشده" }}</a></dd>
          <dt v-if="m.voidReason">دلیل ابطال</dt>
          <dd v-if="m.voidReason" class="en-pre">{{ m.voidReason }}</dd>
          <dt>تایید کارفرما</dt>
          <dd data-test="en-pmdl-client">
            <template v-if="m.clientApprovedAt || m.clientApprovalRef">
              نامهٔ شمارهٔ <span class="inv-ltr">{{ faDigits(m.clientApprovalRef || "") }}</span>، {{ faDigits(m.clientApprovedAtText || "") }}<span v-if="m.clientApprovalRecordedByName"> — ثبت: {{ m.clientApprovalRecordedByName }}</span>
              <a v-if="m.clientApprovalDocumentId" href="#" class="ms-2" @click.prevent="downloadDoc(m.clientApprovalDocumentId, 'client-approval')">دیدن نامهٔ کارفرما</a>
            </template>
            <template v-else>هنوز ثبت نشده</template>
          </dd>
        </dl>

        <div class="table-responsive">
          <table class="table table-sm" data-test="en-pmdl-lines">
            <thead><tr><th>#</th><th>نوع مدرک</th><th>رشته</th><th>عنوان</th><th>الزامی</th><th>مرحلهٔ مناقصه</th><th>تحویل به کارفرما</th><th>بند استاندارد</th><th>واحدها</th><th>وضعیت ردیف</th></tr></thead>
            <tbody>
              <tr v-for="l in lines" :key="l.id" :class="{ 'text-decoration-line-through text-muted': l.removed }" :data-removed="l.removed ? '1' : '0'">
                <td>{{ num(l.seq) }}</td>
                <td class="inv-ltr">{{ l.documentType }}</td>
                <td>{{ l.discipline || "—" }}</td>
                <td>{{ l.title }}</td>
                <td>{{ l.required ? "بله" : "خیر" }}</td>
                <td>{{ l.tenderStage ? "بله" : "خیر" }}</td>
                <td>{{ l.clientSubmittal ? "بله" : "خیر" }}</td>
                <td>{{ l.standardClause || "—" }}</td>
                <td>{{ unitsText(l.units) }}</td>
                <td class="text-nowrap" style="text-decoration: none">
                  <span v-if="l.removed" class="inv-badge bad">برای این پروژه حذف شده</span>
                  <span v-else-if="l.origin === 1" class="inv-badge info">افزوده برای پروژه</span>
                  <span v-else class="inv-muted small">استاندارد</span>
                </td>
              </tr>
              <tr v-if="!lines.length"><td colspan="10" class="inv-muted">ردیفی نیست.</td></tr>
            </tbody>
          </table>
        </div>
        <details v-if="d.history?.length > 1" class="small mb-2">
          <summary>تاریخچهٔ ویرایش‌ها</summary>
          <ul class="mb-0"><li v-for="h in d.history" :key="h.id">{{ faDigits(h.number) }} — ویرایش {{ num(h.revision) }} — {{ h.statusText || mdlStatuses[h.status] }}<span v-if="h.approvedAtText"> — تایید {{ faDigits(h.approvedAtText) }}</span><span v-if="h.voidReason"> — {{ h.voidReason }}</span></li></ul>
        </details>

        <button class="btn btn-sm btn-outline-secondary me-1" type="button" :disabled="enWriting > 0" :title="csvName" @click="downloadCsv">دانلود فایل فهرست (CSV)</button>
        <EnAction :can="d.canUpload" :reason="d.uploadBlockedReason" :roles="EnRoles.writers" label="جایگزینی ردیف‌ها از فایل" variant="outline-primary" @go="uploadDialog" />
        <EnAction :can="d.canCheck" :reason="d.checkBlockedReason" :roles="EnRoles.writers" :confirm="`فهرست مدارک پروژهٔ ${label} بررسی و تایید کارشناس شود؟ پس از آن مدیر مهندسی می‌تواند آن را تایید کند.`" label="تایید بررسی کارشناس" variant="success" @go="simple('check', 'بررسی کارشناس ثبت شد.')" />
        <EnAction :can="d.canReturn" :reason="d.returnBlockedReason" :roles="EnRoles.manager" label="برگشت" variant="outline-danger" @go="returnDialog" />
        <EnAction :can="d.canApprove" :reason="d.approveBlockedReason" :roles="EnRoles.manager" :confirm="`فهرست مدارک پروژهٔ ${label} تایید مدیر مهندسی شود؟ فایل فهرست تاییدشده ساخته می‌شود و همراه بستهٔ فنی مناقصه برای کارفرما می‌رود.`" label="تایید مدیر مهندسی" variant="success" @go="simple('approve', 'فهرست مدارک پروژه تایید شد.')" />
        <EnAction :can="d.canVoid" :reason="d.voidBlockedReason" :roles="EnRoles.manager" label="ابطال" variant="outline-danger" @go="voidDialog" />
        <EnAction :can="d.canRecordClientApproval" :reason="d.recordClientApprovalBlockedReason" :roles="EnRoles.commercial" label="ثبت تایید کارفرما" variant="outline-primary" @go="clientDialog" />
      </div>
      <p v-else-if="visible && !permit" class="inv-muted" data-test="en-pmdl-none">فهرست مدارک پروژه هنوز ساخته نشده است.</p>

      <div v-if="permit" class="en-box" data-test="en-pmdl-permit">
        <div class="d-flex flex-wrap gap-2 align-items-center mb-1">
          <strong>مجوز مناقصه بدون MDL {{ faDigits(permit.number) }}</strong>
          <EnChip :status="permit.status" :text="permit.statusText" group="permit" />
          <span v-if="permit.approvalStale" class="inv-badge bad">پیش از آخرین تصمیم پرونده تایید شده است</span>
        </div>
        <dl class="en-kv small">
          <dt>درخواست‌کننده</dt><dd>{{ permit.requestedByName || "—" }} {{ faDigits(permit.requestedAtText || "") }}</dd>
          <dt>دلیل</dt><dd class="en-pre">{{ permit.reason }}</dd>
          <dt v-if="permit.decidedByName">تصمیم مدیرعامل</dt><dd v-if="permit.decidedByName">{{ permit.decidedByName }}، {{ faDigits(permit.decidedAtText || "") }}<span v-if="permit.decisionNote"> — {{ permit.decisionNote }}</span></dd>
        </dl>
        <EnAction :can="d.canDecidePermit" :reason="d.decidePermitBlockedReason" :roles="EnRoles.ceo" :confirm="`مجوز مناقصه بدون MDL برای پروندهٔ ${label} تایید شود؟ بستهٔ فنی مناقصه بدون فهرست مدارک پروژه منتشر خواهد شد.`" label="تایید مجوز" variant="success" @go="permitApprove" />
        <EnAction :can="d.canDecidePermit" :reason="d.decidePermitBlockedReason" :roles="EnRoles.ceo" label="رد مجوز" variant="outline-danger" @go="permitRejectDialog" />
        <EnAction :can="d.canWithdrawPermit" :reason="d.withdrawPermitBlockedReason" :roles="EnRoles.manager" :confirm="permitIsApproved ? undefined : `درخواست مجوز مناقصه بدون MDL برای پروندهٔ ${label} پس گرفته شود؟`" :label="permitIsApproved ? 'پس‌گرفتن مجوز' : 'پس‌گرفتن درخواست'" variant="outline-secondary" @go="permitWithdraw" />
      </div>

      <EnAction :can="d.canDraft" :reason="d.draftBlockedReason" :roles="EnRoles.writers" :confirm="`پیش‌نویس فهرست مدارک پروژهٔ ${label} از فهرست استاندارد تاییدشده ساخته شود؟`" label="ساخت پیش‌نویس از فهرست استاندارد" variant="outline-primary" @go="draft" />
      <EnAction :can="!!d.canDraftFromFile" :reason="d.draftFromFileBlockedReason" :roles="EnRoles.writers" label="ساخت فهرست مدارک پروژه از فایل" variant="outline-primary" @go="draftFromFileDialog" />
      <EnAction :can="d.canRequestPermit" :reason="d.requestPermitBlockedReason" :roles="EnRoles.manager" label="درخواست مناقصه بدون MDL" variant="outline-secondary" @go="permitRequestDialog" />
    </template>
    <EnPrompt v-if="dlg.dlg.value" :busy="dlg.busy.value" :confirm-text="dlg.dlg.value.confirmText" :error="dlg.error.value" :fields="dlg.dlg.value.fields" :hint="dlg.dlg.value.hint" :title="dlg.dlg.value.title" @cancel="dlg.close" @confirm="dlg.confirm" />
  </div>
</template>
