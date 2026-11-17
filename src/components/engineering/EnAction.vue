<script setup lang="ts">
// A gate / action button of the engineering pages. Its enabled state and its reason come from the DTO (can<Action> / <action>BlockedReason): the page never re-implements the rule.
// When the flag is false the button stays visible but disabled and the server's own reason is written under it. `roles` (optional) keeps the button away from people whose role never
// has this action, so only the people who hold the role see a disabled button with a reason.
// `confirm` (optional, a Persian question that names the object and the consequence): a click first opens the module's own dialog (EnPrompt without fields, answer «بله» / «انصراف»)
// and only «بله» emits `go`. Every one-click write of the engineering pages carries it; a button that opens a form dialog of its own does not need it.
import { computed, ref } from "vue";
import EnPrompt from "@/components/engineering/EnPrompt.vue";
import { hasRole } from "@/composables/roles";
import { enWriting } from "@/composables/engineering";

const props = defineProps<{ label: string; can?: boolean | null; reason?: string | null; roles?: readonly string[]; variant?: string; busy?: boolean; confirm?: string }>();
const emit = defineEmits(["go"]);
const shown = computed(() => !!props.can || !props.roles || hasRole([...props.roles]));
const asking = ref(false);

function click() {
  if (props.confirm) asking.value = true;
  else emit("go");
}
function yes() {
  asking.value = false;
  emit("go");
}
</script>

<template>
  <span v-if="shown" class="en-act">
    <button :class="'btn btn-sm btn-' + (variant || 'primary')" :disabled="!can || busy || enWriting > 0" :title="!can ? reason || '' : ''" type="button" @click="click">{{ label }}</button>
    <span v-if="!can && reason" class="en-why">{{ reason }}</span>
    <EnPrompt v-if="asking" :fields="[]" :title="confirm || ''" confirm-text="بله" data-test="en-confirm" @cancel="asking = false" @confirm="yes" />
  </span>
</template>
