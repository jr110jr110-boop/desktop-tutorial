<script setup lang="ts">
// Referral tree of a letter with the refer dialog and the referral actions (comment, close, cancel).
import { ref } from "vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import ReferDialog from "./ReferDialog.vue";
import ReferralNode from "./ReferralNode.vue";
import { referralAction } from "@/api/OfficeAPIs";
import { fail, ok } from "@/composables/inventory";
import type { MyPosition } from "./letterKit";

const props = defineProps<{ letter: any; myPositions: MyPosition[]; positions: any[]; canRefer: boolean; isSecretariat: boolean }>();
const emit = defineEmits(["changed"]);
const referTo = ref<{ parentId: number | null; acting: number | null } | null>(null);
const act = ref<{ node: any; action: "comment" | "close" | "cancel" } | null>(null);
const titles = { comment: "اظهارنظر درباره ارجاع", close: "بستن ارجاع", cancel: "لغو ارجاع" };

function actingFor(node: any, action: string): number | null {
  const want = action === "cancel" ? node.fromPositionId : node.toPositionId;
  return props.myPositions.some((p) => p.positionId === want) ? want : null;
}
async function run(values: Record<string, any> | null) {
  const { node, action } = act.value!;
  act.value = null;
  const [, error] = await referralAction(node.id, action, action === "cancel" ? undefined : values?.text, actingFor(node, action));
  if (error) return fail(error);
  ok("انجام شد.");
  emit("changed");
}
// cancel needs no reason (the server does not store it): confirm directly
function ask(node: any, action: "comment" | "close" | "cancel") {
  act.value = { node, action };
  if (action === "cancel") {
    if (!confirm("ارجاع لغو شود؟")) return void (act.value = null);
    run(null);
  }
}
</script>

<template>
  <div>
    <div class="d-flex justify-content-between align-items-center mb-2">
      <h2 class="h6 mb-0">ارجاع‌ها</h2>
      <button v-if="canRefer" class="btn btn-sm btn-primary" @click="referTo = { parentId: null, acting: null }">ارجاع نامه</button>
    </div>
    <p v-if="!letter.referrals || !letter.referrals.length" class="inv-muted">ارجاعی ثبت نشده است.</p>
    <ReferralNode
      v-for="r in letter.referrals" :key="r.id" :can-refer="canRefer" :depth="0" :is-secretariat="isSecretariat" :my-positions="myPositions" :node="r"
      @act="ask" @refer="(n) => (referTo = { parentId: n.id, acting: n.toPositionId })" />

    <ReferDialog v-if="referTo" :acting-default="referTo.acting" :letter-id="letter.id" :my-positions="myPositions" :parent-id="referTo.parentId" :positions="positions" @cancel="referTo = null" @done="referTo = null; emit('changed')" />
    <PromptModal
      v-if="act && act.action !== 'cancel'" :confirm-text="titles[act.action]" :fields="[{ key: 'text', label: 'توضیح', type: 'textarea', required: act.action !== 'close' }]"
      :title="titles[act.action]" @cancel="act = null" @confirm="run" />
  </div>
</template>
