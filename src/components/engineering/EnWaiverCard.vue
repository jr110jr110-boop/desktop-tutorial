<script setup lang="ts">
// An emergency waiver of the document lock: the reason, the missing lines, the debt deadline (date-time, at most 72 hours), who asked and for how long it has waited. The CEO decides
// (approve with an optional shorter deadline, or reject with a note); the buttons follow the DTO flags and the server's refusal is written inside the dialog.
import EnAction from "@/components/engineering/EnAction.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import EnPrompt from "@/components/engineering/EnPrompt.vue";
import EnWait from "@/components/engineering/EnWait.vue";
import { decideEnWaiver } from "@/api/EngineeringAPIs";
import { enPaths, EnRoles, faDigits, stamp, useEnDialog } from "@/composables/engineering";

const props = defineProps<{ waiver: any }>();
const emit = defineEmits(["changed"]);
const d = useEnDialog(() => emit("changed"));
function approve() {
  d.ask({
    title: "تایید مجوز اضطرار",
    hint: "مهلت بدهی را می‌توانید کوتاه‌تر کنید؛ بیشتر از مهلت درخواست‌شده نمی‌شود.",
    fields: [
      { key: "due", label: "مهلت بدهی مدارک", type: "datetime", value: props.waiver.debtDueAt },
      { key: "n", label: "یادداشت (اختیاری)", type: "textarea", max: 2000 },
    ],
    run: (v) => decideEnWaiver({ id: props.waiver.id, decision: "approve", note: v.n, debtDueAt: v.due || null, rowVersion: props.waiver.rowVersion }),
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
      <dt>مهلت بدهی</dt><dd>{{ stamp(waiver.debtDueAt) }}</dd>
      <dt>مدارک کم</dt><dd><span v-if="!waiver.missingLines?.length">—</span><template v-else><div v-for="m in waiver.missingLines" :key="m">{{ m }}</div></template></dd>
      <dt v-if="waiver.dossierUnconfirmed">پرونده پلتفرم</dt><dd v-if="waiver.dossierUnconfirmed">تایید نشده</dd>
      <dt>پوشش</dt><dd>{{ waiver.covers ? "همه کمبودها را می‌پوشاند" : "کمبودها را کامل نمی‌پوشاند" }}<span v-if="waiver.coverageNote"> — {{ waiver.coverageNote }}</span></dd>
      <dt v-if="waiver.decidedByName">تصمیم</dt><dd v-if="waiver.decidedByName">{{ waiver.decidedByName }}، {{ faDigits(waiver.decidedAtText || "") }}<span v-if="waiver.decisionNote"> — {{ waiver.decisionNote }}</span></dd>
    </dl>
    <EnAction :can="waiver.canDecide" :reason="waiver.decideBlockedReason" :roles="EnRoles.ceo" label="تایید مجوز اضطرار" variant="success" @go="approve" />
    <EnAction :can="waiver.canDecide" :reason="waiver.decideBlockedReason" :roles="EnRoles.ceo" label="رد مجوز اضطرار" variant="outline-danger" @go="reject" />
    <EnPrompt v-if="d.dlg.value" :busy="d.busy.value" :error="d.error.value" :fields="d.dlg.value.fields" :hint="d.dlg.value.hint" :title="d.dlg.value.title" @cancel="d.close" @confirm="d.confirm" />
  </div>
</template>
