<script setup lang="ts">
// A standard-gap report (made by the engineering agent): summary, counts and the lines grouped Added / Removed / RequiredChanged / Same. The manager confirms with an impact note;
// the button follows canConfirm and shows the server's reason when disabled.
import { computed } from "vue";
import EnAction from "@/components/engineering/EnAction.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import EnPrompt from "@/components/engineering/EnPrompt.vue";
import { confirmEnGap } from "@/api/EngineeringAPIs";
import { EnRoles, enPaths, faDigits, faNum, gapKindKeys, gapKinds, useEnDialog } from "@/composables/engineering";

const props = defineProps<{ gap: any; link?: boolean }>();
const emit = defineEmits(["changed"]);
const d = useEnDialog(() => emit("changed"));
const groups = computed(() =>
  [1, 2, 3, 0].map((k) => ({ kind: k, rows: (props.gap.lines || []).filter((l: any) => l.kind === k) })).filter((g) => g.rows.length)
);
function confirm() {
  d.ask({
    title: "تایید گزارش تفاوت استاندارد",
    hint: "اثر این تفاوت بر کار مهندسی را بنویسید (حداقل ۳۰ نویسه).",
    fields: [{ key: "note", label: "توضیح اثر بر مواد و طراحی", type: "textarea", required: true, min: 30, max: 2000 }],
    confirmText: "تایید گزارش تفاوت",
    run: (v) => confirmEnGap({ id: props.gap.id, engineeringImpactNote: v.note, rowVersion: props.gap.rowVersion }),
    okText: "گزارش تفاوت دو استاندارد تایید شد.",
  });
}
</script>

<template>
  <div class="en-box" data-test="en-gap">
    <div class="d-flex flex-wrap gap-2 align-items-center mb-1">
      <strong>{{ faDigits(gap.number) }}</strong>
      <EnChip :text="gap.statusText" :status="gap.status" group="gap" />
      <span class="small inv-muted">از استاندارد {{ gap.baseCode }} (ویرایش {{ faNum(gap.baseRevision, 0) }}) به {{ gap.targetCode }} (ویرایش {{ faNum(gap.targetRevision, 0) }})</span>
      <router-link v-if="link" :to="enPaths.gap(gap.id)" class="small">صفحه تفاوت دو استاندارد</router-link>
    </div>
    <p class="mb-1">{{ gap.summary }}</p>
    <p class="small mb-2">افزوده: {{ faNum(gap.addedCount, 0) }} — حذف‌شده: {{ faNum(gap.removedCount, 0) }} — تغییر الزام: {{ faNum(gap.requiredChangedCount, 0) }} — قابل استفاده مجدد: {{ faNum(gap.reusableCount, 0) }}</p>
    <p v-if="gap.engineeringImpactNote" class="small"><strong>توضیح اثر بر مواد و طراحی:</strong> {{ gap.engineeringImpactNote }} <span v-if="gap.confirmedByName" class="inv-muted">({{ gap.confirmedByName }}، {{ faDigits(gap.confirmedAtText || "") }})</span></p>
    <template v-if="gap.lines && gap.lines.length">
      <table v-for="g in groups" :key="g.kind" class="table table-sm mb-2">
        <thead><tr><th colspan="4">{{ gapKinds[g.kind] }} ({{ faNum(g.rows.length, 0) }})</th></tr></thead>
        <tbody>
          <tr v-for="l in g.rows" :key="l.seq" :class="['en-gapline', gapKindKeys[g.kind]]">
            <td>{{ faNum(l.seq, 0) }}</td><td>{{ l.documentType }}</td><td>{{ l.discipline }}</td><td>{{ l.title }}<span v-if="l.baseRequired !== l.targetRequired" class="small inv-muted"> (الزامی: {{ l.baseRequired ? "بله" : "خیر" }} ← {{ l.targetRequired ? "بله" : "خیر" }})</span></td>
          </tr>
        </tbody>
      </table>
    </template>
    <EnAction :can="gap.canConfirm" :reason="gap.confirmBlockedReason" :roles="EnRoles.manager" label="تایید گزارش تفاوت دو استاندارد" variant="success" @go="confirm" />
    <EnPrompt v-if="d.dlg.value" :busy="d.busy.value" :confirm-text="d.dlg.value.confirmText" :error="d.error.value" :fields="d.dlg.value.fields" :hint="d.dlg.value.hint" :title="d.dlg.value.title" @cancel="d.close" @confirm="d.confirm" />
  </div>
</template>
