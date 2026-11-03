<script setup lang="ts">
// Second-person decisions of the attendance module (R1f): import batches of clock events, manual / held clock events, and corrections of locked sheets
// that payroll proposed. The API decides who may act (allowedActions): another HR user than the author, never the employee himself.
import { ref } from "vue";
import ApprovalTable from "@/components/common/ApprovalTable.vue";
import type { ApprovalAction, ApprovalColumn } from "@/components/common/approvalTypes";
import PcTabs from "@/components/pc/PcTabs.vue";
import { attendanceCorrectionAction, clockBatchAction, clockRequestAction, getAttendanceCorrections, getClockEvents, getClockImportBatches } from "@/api/OfficeAPIs";
import { clockDirections } from "@/composables/office";

const tab = ref("batches");
const tabs = [{ key: "batches", title: "بسته‌های ورود تردد" }, { key: "events", title: "ترددهای در انتظار" }, { key: "corrections", title: "اصلاح برگه‌های قفل‌شده" }];
const status = ref<number | "">(0);
const waitingForMe = ref(false);

const word = (names: Record<string, string>, key: string) => names[key] || key;
const batchStatus: Record<string, string> = { Pending: "در انتظار تایید", Approved: "تاییدشده", Rejected: "ردشده" };
const badgeOf = (s: string) => (s === "Approved" ? "ok" : s === "Rejected" ? "bad" : "warn");

const batchCols: ApprovalColumn[] = [
  { key: "importedAtText", title: "زمان ورود", ltr: true },
  { key: "importedByName", title: "واردکننده" },
  { key: "source", title: "منبع", text: (r) => (r.source === "Excel" ? "فایل اکسل" : r.source === "Json" ? "ورود ردیفی / دستگاه" : r.source) },
  { key: "rows", title: "ردیف" },
  { key: "employeeCount", title: "کارمند" },
  { key: "period", title: "بازه", ltr: true, text: (r) => (r.firstTimestampText ? `${r.firstTimestampText} تا ${r.lastTimestampText}` : "") },
  { key: "reason", title: "توضیح" },
  { key: "status", title: "وضعیت", text: (r) => word(batchStatus, r.status), badge: (r) => badgeOf(r.status) },
  { key: "decision", title: "تصمیم", text: (r) => (r.decidedByName ? `${r.decidedByName}${r.decisionNote ? ": " + r.decisionNote : ""}` : "") },
];
const clockCols: ApprovalColumn[] = [
  { key: "timestampText", title: "تاریخ و ساعت", ltr: true },
  { key: "employeeName", title: "کارمند", text: (r) => `${r.employeeName} (${r.personnelCode})` },
  { key: "direction", title: "جهت", text: (r) => clockDirections[r.direction] },
  { key: "reason", title: "دلیل" },
  { key: "createdByName", title: "ثبت‌کننده" },
  { key: "approval", title: "وضعیت", text: (r) => (r.approval === 1 ? "در انتظار" : r.approval === 2 ? "رد شده" : "تاییدشده"), badge: (r) => (r.approval === 1 ? "warn" : r.approval === 2 ? "bad" : "ok") },
  { key: "decision", title: "تصمیم", text: (r) => (r.approvedByName ? `${r.approvedByName}${r.rejectReason ? ": " + r.rejectReason : ""}` : "") },
];
const sheetNumbers = (n: any) => (n ? `کارکرد ${n.daysWorked} · اضافه‌کار ${n.overtimeHours} · شب ${n.nightHours} · تعطیل ${n.holidayHours} · غیبت ${n.absenceDays} · مرخصی ${n.paidLeaveDays} · استعلاجی ${n.sickLeaveDays} · بدون حقوق ${n.unpaidLeaveDays} · ماموریت ${n.missionDays}` : "");
const correctionCols: ApprovalColumn[] = [
  { key: "employeeName", title: "کارمند", text: (r) => `${r.employeeName} (${r.personnelCode})` },
  { key: "month", title: "ماه", text: (r) => `${r.year}/${String(r.month).padStart(2, "0")}`, ltr: true },
  { key: "reason", title: "دلیل" },
  { key: "proposedByName", title: "پیشنهاددهنده" },
  { key: "status", title: "وضعیت", text: (r) => word({ Pending: "در انتظار", Approved: "تاییدشده", Rejected: "ردشده" }, r.status), badge: (r) => badgeOf(r.status) },
  { key: "decision", title: "تصمیم", text: (r) => (r.decidedByName ? `${r.decidedByName}${r.decisionNote ? ": " + r.decisionNote : ""}` : "") },
];

const actionsOf = (row: any): ApprovalAction[] =>
  (row.allowedActions || []).map((a: string) =>
    a === "approve"
      ? { key: "approve", label: "تایید", btn: "btn-success", reason: "optional" as const, done: "تایید شد." }
      : { key: "reject", label: "رد", btn: "btn-outline-danger", reason: true as const, hint: "دلیل رد برای ثبت‌کننده نوشته می‌شود.", done: "رد شد." }
  );
const note = (n: string | null) => n || null;
const runBatch = (row: any, a: string, n: string | null) => clockBatchAction(row.id, a as any, note(n));
const runEvent = (row: any, a: string, n: string | null) => clockRequestAction(row.id, a as any, note(n));
const runCorrection = (row: any, a: string, n: string | null) => attendanceCorrectionAction(row.id, a as any, note(n));
const batchFilters = () => ({ Status: status.value === "" ? null : status.value, WaitingForMe: waitingForMe.value || null });
</script>

<template>
  <div>
    <PcTabs v-model="tab" :tabs="tabs" />
    <p class="inv-muted small">
      ورود فایل یا ردیف تردد، ثبت دستی تردد و اصلاح برگه‌ی قفل‌شده «درخواست» هستند و تا تایید یک نفر دوم در کارکرد نمی‌آیند. نویسنده‌ی درخواست، خود کارمند و کسی که در بسته تردد دارد تصمیم نمی‌گیرد.
    </p>

    <template v-if="tab === 'batches'">
      <div class="inv-filters mb-2">
        <div><label class="form-label small mb-0">وضعیت</label><select v-model="status" class="form-select form-select-sm"><option value="">همه</option><option :value="0">در انتظار</option><option :value="1">تاییدشده</option><option :value="2">ردشده</option></select></div>
        <label class="small align-self-end"><input v-model="waitingForMe" type="checkbox" class="form-check-input me-1" />فقط آنچه من می‌توانم تصمیم بگیرم</label>
      </div>
      <ApprovalTable :fetcher="getClockImportBatches" :filters="batchFilters()" :columns="batchCols" :actions="actionsOf" :run="runBatch" empty-text="بسته‌ای نیست." />
    </template>
    <ApprovalTable v-else-if="tab === 'events'" :fetcher="getClockEvents" :filters="{ Approval: 1 }" :columns="clockCols" :actions="actionsOf" :run="runEvent" empty-text="تردد در انتظاری نیست." />
    <ApprovalTable v-else :fetcher="getAttendanceCorrections" :filters="{ Status: 0 }" :columns="correctionCols" :actions="actionsOf" :run="runCorrection" detail empty-text="درخواست اصلاحی نیست.">
      <template #detail="{ row }">
        <div class="small"><div>فعلی: {{ sheetNumbers(row.current) }}</div><div>پیشنهادی: {{ sheetNumbers(row.proposed) }}</div></div>
      </template>
    </ApprovalTable>
  </div>
</template>
