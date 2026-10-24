<script setup lang="ts">
// One node of the referral tree (nested). Buttons depend on the user's positions; the server decides again on every call.
import { computed } from "vue";
import { referralActions, referralStatuses } from "@/composables/office";
import type { MyPosition } from "./letterKit";

const props = defineProps<{ node: any; myPositions: MyPosition[]; canRefer: boolean; isSecretariat: boolean; depth: number }>();
const emit = defineEmits<{ (e: "act", node: any, action: "comment" | "close" | "cancel"): void; (e: "refer", node: any): void }>();
const mine = computed(() => props.myPositions.some((p) => p.positionId === props.node.toPositionId));
const pending = computed(() => props.node.status === 0);
</script>

<template>
  <div class="ref-node" :style="{ marginRight: depth ? '1.25rem' : '0' }">
    <div class="ref-box">
      <div class="d-flex justify-content-between flex-wrap gap-1">
        <div>
          <b>{{ node.fromPositionTitle || "-" }}</b> ← <b>{{ node.toPositionTitle }}</b>
          <span class="inv-badge info me-1">{{ referralActions[node.action] }}</span>
          <span :class="node.status === 1 ? 'ok' : node.status === 2 ? 'bad' : 'warn'" class="inv-badge me-1">{{ referralStatuses[node.status] }}</span>
        </div>
        <div class="small inv-muted">{{ node.createdAtText }}<span v-if="node.dueDateText"> - مهلت {{ node.dueDateText }}</span></div>
      </div>
      <div class="my-1">{{ node.instruction }}</div>
      <div v-for="(e, i) in node.entries" :key="i" class="small border-top pt-1">
        <b>{{ e.userName }}</b><span v-if="e.onBehalf"> (به نیابت)</span> <span class="inv-muted">{{ e.atText }}</span>: {{ e.text }}
      </div>
      <div v-if="node.doneAt || node.doneNote" class="small border-top pt-1">پایان: {{ node.doneByName }} - {{ node.doneNote }}</div>
      <div v-if="pending" class="d-flex gap-1 flex-wrap mt-1">
        <button v-if="mine" class="btn btn-sm btn-outline-secondary" @click="emit('act', node, 'comment')">اظهارنظر</button>
        <button v-if="mine" class="btn btn-sm btn-outline-success" @click="emit('act', node, 'close')">بستن ارجاع</button>
        <button v-if="canRefer && mine" class="btn btn-sm btn-outline-primary" @click="emit('refer', node)">ارجاع مجدد</button>
        <button v-if="node.canCancel" class="btn btn-sm btn-outline-danger" @click="emit('act', node, 'cancel')">لغو ارجاع</button>
      </div>
    </div>
    <ReferralNode v-for="c in node.children" :key="c.id" :can-refer="canRefer" :depth="depth + 1" :is-secretariat="isSecretariat" :my-positions="myPositions" :node="c" @act="(n, a) => emit('act', n, a)" @refer="(n) => emit('refer', n)" />
  </div>
</template>

<style scoped>
.ref-box { border: 1px solid #dee2e6; border-radius: 0.4rem; padding: 0.5rem 0.75rem; margin-bottom: 0.4rem; background: #fff; }
</style>
