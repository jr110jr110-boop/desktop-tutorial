<script setup lang="ts">
// Type-ahead picker of a user; the model is the user id, the text shown while only the id is known comes from fallbackText.
import { ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import { searchUsers, userLabel } from "./lookups";

const props = defineProps({ modelValue: { type: Number, default: null }, fallbackText: { type: String, default: "" }, placeholder: { type: String, default: "جستجوی کاربر..." }, disabled: Boolean });
const emit = defineEmits(["update:modelValue", "pick"]);
const picked = ref<any>(null);
function onPick(u: any) {
  picked.value = u;
  emit("update:modelValue", u ? u.id : null);
  emit("pick", u);
}
</script>

<template>
  <LookupBox :model-value="picked" :fetch="searchUsers" :label="userLabel" :placeholder="placeholder" :disabled="disabled" :fallback-text="modelValue ? fallbackText : ''" @pick="onPick" />
</template>
