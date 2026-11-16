<script setup lang="ts">
// A gate / action button of the engineering pages. Its enabled state and its reason come from the DTO (can<Action> / <action>BlockedReason): the page never re-implements the rule.
// When the flag is false the button stays visible but disabled and the server's own reason is written under it. `roles` (optional) keeps the button away from people whose role never
// has this action, so only the people who hold the role see a disabled button with a reason.
import { computed } from "vue";
import { hasRole } from "@/composables/roles";

const props = defineProps<{ label: string; can?: boolean | null; reason?: string | null; roles?: readonly string[]; variant?: string; busy?: boolean }>();
const emit = defineEmits(["go"]);
const shown = computed(() => !!props.can || !props.roles || hasRole([...props.roles]));
</script>

<template>
  <span v-if="shown" class="en-act">
    <button :class="'btn btn-sm btn-' + (variant || 'primary')" :disabled="!can || busy" :title="!can ? reason || '' : ''" type="button" @click="emit('go')">{{ label }}</button>
    <span v-if="!can && reason" class="en-why">{{ reason }}</span>
  </span>
</template>
