<script setup lang="ts">
// An emergency permit of the lock of the release to execution (asked once by the manager after the PMO accepted an incomplete package): the reason, the missing lines, the debt period in hours (1..72, counted from the CEO's approval; the deadline date-time exists only after the
// approval), who asked and for how long it has waited. The CEO decides (approve, optionally lowering the hours, or reject with a note); the buttons follow the DTO flags and the server's refusal
// is written inside the dialog.
import { computed } from "vue";
import EnAction from "@/components/engineering/EnAction.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import EnPrompt from "@/components/engineering/EnPrompt.vue";
import EnWait from "@/components/engineering/EnWait.vue";
import { decideEnWaiver } from "@/api/EngineeringAPIs";
import { enPaths, EnRoles, faDigits, faNum, stamp, useEnDialog } from "@/composables/engineering";

const props = defineProps<{ waiver: any }>();
const emit = defineEmits(["changed"]);
const d = useEnDialog(() => emit("changed"));
// The server sends the missing documents as { docType, title }; a plain «TYPE|title» text (an older answer) is split the same way.
const missing = computed<{ docType: string; title: string }[]>(() =>
  (props.waiver.missingLines || []).map((m: any) => {
    if (typeof m !== "string") return { docType: m.docType || "", title: m.title || "" };
    const i = m.indexOf("|");
    return i < 0 ? { docType: "", title: m } : { docType: m.slice(0, i), title: m.slice(i + 1) };
  })
);
function approve() {
  d.ask({
    title: "تایید مجوز اضطرار",
    hint: "مهلت تأمین مدرک از لحظهٔ تایید مدیرعامل شمرده می‌شود؛ حداکثر ۷۲ ساعت اداری. ساعت اداری: ۸ تا ۱۹، شنبه تا پنجشنبه؛ این مهلت فقط برای انسان است. فقط می‌توانید ساعت‌ها را کمتر کنید، بیشتر از ساعت درخواست‌شده نمی‌شود.",
    fields: [
      { key: "hours", label: "مهلت تأمین مدرک (ساعت اداری)", type: "number", required: true, lo: 1, hi: Math.min(72, props.waiver.debtHours || 72), value: props.waiver.debtHours || 72 },
      { key: "n", label: "یادداشت (اختیاری)", type: "textarea", max: 2000 },
    ],
    run: (v) => decideEnWaiver({ id: props.waiver.id, decision: "approve", note: v.n, debtHours: v.hours, rowVersion: props.waiver.rowVersion }),
    okText: "مجوز اضطرار تایید شد.",
  });
}
function reject() {
  d.ask({
    title: "رد مجوز اضطرار",
    fields: [{ key: "n", label: "دلیل رد", type: "textarea", required: true, min: 15, max: 2000 }],
    run: (v) => decideEnWaiver({ id: props.waiver.id, decision: "reject", note: v.n, rowVersion: props.waiver.rowVersion }),
    okText: "مجوز اضطرار رد شد.",
  });
}
</script>

<template>
  <div class="en-box" data-test="en-waiver">
    <div class="d-flex flex-wrap gap-2 align-items-center mb-1">
      <strong>مجوز اضطرار {{ faDigits(waiver.number) }}</strong>
      <EnChip :status="waiver.status" :text="waiver.statusText" group="waiver" />
      <router-link :to="enPaths.handover(waiver.handoverId)" class="small">تحویل {{ faDigits(waiver.handoverNumber) }}</router-link>
      <span class="small inv-muted">{{ waiver.requestedByName }} — {{ faDigits(waiver.requestedAtText || "") }}</span>
    </div>
    <EnWait v-if="waiver.status === 0" :hours="waiver.waitingHours" text="مجوز اضطرار منتظر تصمیم مدیرعامل است" />
    <dl class="en-kv">
      <dt>دلیل</dt><dd class="en-pre">{{ waiver.reason }}</dd>
      <dt>مدت مهلت</dt><dd data-test="en-waiver-hours">{{ faNum(waiver.debtHours, 0) }} ساعت اداری از لحظهٔ تایید مدیرعامل</dd>
      <dt v-if="waiver.status === 1 && waiver.debtDueAt">مهلت تأمین مدرک</dt><dd v-if="waiver.status === 1 && waiver.debtDueAt" data-test="en-waiver-due">{{ stamp(waiver.debtDueAt) }}</dd>
      <dt>مدارک کم</dt>
      <dd>
        <span v-if="!missing.length">—</span>
        <table v-else class="table table-sm mb-0 w-auto" data-test="en-waiver-missing">
          <thead><tr><th>نوع</th><th>عنوان</th></tr></thead>
          <tbody><tr v-for="(m, i) in missing" :key="i"><td class="inv-ltr">{{ m.docType || "—" }}</td><td>{{ m.title }}</td></tr></tbody>
        </table>
      </dd>
      <dt v-if="waiver.dossierUnconfirmed">پرونده پلتفرم</dt><dd v-if="waiver.dossierUnconfirmed">تایید نشده</dd>
      <dt v-if="waiver.status !== 0">پوشش</dt><dd v-if="waiver.status !== 0">{{ waiver.covers ? "همه کمبودها را می‌پوشاند" : "کمبودها را کامل نمی‌پوشاند" }}<span v-if="waiver.coverageNote"> — {{ waiver.coverageNote }}</span></dd>
      <dt v-if="waiver.decidedByName">تصمیم</dt><dd v-if="waiver.decidedByName">{{ waiver.decidedByName }}، {{ faDigits(waiver.decidedAtText || "") }}<span v-if="waiver.decisionNote"> — {{ waiver.decisionNote }}</span></dd>
    </dl>
    <p v-if="waiver.status === 1 && !waiver.used" class="small inv-muted mb-1" data-test="en-waiver-open">مجوز تاییدشده است؛ PMO می‌تواند تحویل پذیرفته‌شده را ابلاغ کند و بدهی مدارک هنگام ابلاغ ثبت می‌شود.</p>
    <EnAction :can="waiver.canDecide" :reason="waiver.decideBlockedReason" :roles="EnRoles.ceo" label="تایید مجوز اضطرار" variant="success" @go="approve" />
    <EnAction :can="waiver.canDecide" :reason="waiver.decideBlockedReason" :roles="EnRoles.ceo" label="رد مجوز اضطرار" variant="outline-danger" @go="reject" />
    <EnPrompt v-if="d.dlg.value" :busy="d.busy.value" :error="d.error.value" :fields="d.dlg.value.fields" :hint="d.dlg.value.hint" :title="d.dlg.value.title" @cancel="d.close" @confirm="d.confirm" />
  </div>
</template>
