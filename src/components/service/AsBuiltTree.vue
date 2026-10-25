<script setup lang="ts">
// As-built tree of an installed unit (SvAsBuiltDto.trace = SerialTraceDto): the top assembly -> ... -> the unit -> its serial children and the lot pieces installed in it.
// A unit without a serial unit has no trace, so the tree is not available.
import { computed } from "vue";
import AsBuiltNode from "./AsBuiltNode.vue";
import { dateText } from "@/composables/service";

const props = defineProps({ asBuilt: { type: Object, default: null }, hasSerial: Boolean });

const serialNode = (s: any, children: any[] = []) => ({ label: [s.serialNumber, s.itemTitle || s.itemCode].filter(Boolean).join(" - "), sub: s.lotNumber ? "بچ " + s.lotNumber : "", children });

const tree = computed<any | null>(() => {
  const t = props.asBuilt?.trace;
  if (!t || !t.unit) return null;
  const lots = (t.lotComponents || []).map((l: any) => ({
    label: [l.childItemCode, l.childItemTitle].filter(Boolean).join(" - "),
    sub: [l.childLotNumber ? "بچ " + l.childLotNumber : "", l.childHeatNumber ? "ذوب " + l.childHeatNumber : "", l.quantity ? "تعداد " + l.quantity : ""].filter(Boolean).join("، "),
    children: [],
  }));
  const kids = (t.children || []).map((c: any) => serialNode(c));
  let node: any = { ...serialNode(t.unit, [...kids, ...lots]), tag: "واحد" };
  // ancestors run from the direct parent up to the top assembly
  for (const a of t.ancestors || []) node = serialNode(a, [node]);
  return node;
});
const history = computed<any[]>(() => (props.asBuilt?.trace?.assemblyHistory || []).filter((h: any) => h.removedAt));
</script>

<template>
  <div>
    <p v-if="!hasSerial" class="inv-muted">این واحد قطعه سریالی ندارد؛ درخت as-built برای آن در دسترس نیست.</p>
    <p v-else-if="!tree" class="inv-muted">درخت as-built پیدا نشد.</p>
    <template v-else>
      <ul class="p-0 m-0 list-unstyled"><AsBuiltNode :node="tree" /></ul>
      <div v-if="history.length" class="mt-3">
        <h4 class="small fw-bold">قطعات جداشده از مجموعه</h4>
        <table class="table table-sm">
          <thead><tr><th>قطعه</th><th>نصب</th><th>جدا شدن</th></tr></thead>
          <tbody>
            <tr v-for="h in history" :key="h.id">
              <td>{{ h.childSerialNumber || h.childLotNumber }} {{ h.childItemTitle }}</td>
              <td>{{ dateText(h.installedAt) }}</td>
              <td>{{ dateText(h.removedAt) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>
