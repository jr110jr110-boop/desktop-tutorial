<script setup lang="ts">
// Approval chain of a letter as a timeline (rounds after a return are shown one below the other).
import { computed } from "vue";
import { approvalStatuses } from "@/composables/office";

const props = defineProps<{ approvals: any[] }>();
const rows = computed(() => [...(props.approvals || [])].sort((a, b) => a.round - b.round || a.seq - b.seq));
const activeKey = computed(() => {
  const maxRound = Math.max(0, ...rows.value.map((r) => r.round));
  const pending = rows.value.find((r) => r.round === maxRound && r.status === 0);
  return pending ? `${pending.round}-${pending.seq}` : "";
});
const stepClass = (r: any) => (r.status === 1 ? "done" : r.status === 2 ? "rejected" : `${r.round}-${r.seq}` === activeKey.value ? "active" : "");
</script>

<template>
  <div v-if="rows.length" class="inv-timeline">
    <div v-for="r in rows" :key="r.round + '-' + r.seq" :class="stepClass(r)" class="step">
      <b>{{ r.positionTitle }}</b>
      <span class="inv-badge me-1" :class="r.status === 1 ? 'ok' : r.status === 2 ? 'bad' : 'warn'">{{ approvalStatuses[r.status] }}</span>
      <span class="small inv-muted me-1">دور {{ r.round }}</span>
      <div v-if="r.actedAtText || r.actedByName" class="small">{{ r.actedByName }}<span v-if="r.onBehalf"> (به نیابت)</span> - {{ r.actedAtText }}</div>
      <div v-if="r.comment" class="small">{{ r.comment }}</div>
    </div>
  </div>
  <p v-else class="inv-muted">زنجیره تاییدی ثبت نشده است.</p>
</template>
