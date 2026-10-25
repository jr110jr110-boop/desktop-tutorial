<script setup lang="ts">
import { ref } from "vue";
import { fail } from "@/composables/inventory";

// A confirmation with a mandatory reason (skip, cancel, kit-override, release with shortage). The server repeats the check.
const props = defineProps<{ title: string; label?: string; hint?: string; confirmText?: string; danger?: boolean; busy?: boolean }>();
const emit = defineEmits<{ (e: "confirm", reason: string): void; (e: "close"): void }>();
const reason = ref("");

function go() {
  if (!reason.value.trim()) return fail("دلیل را بنویسید.");
  emit("confirm", reason.value.trim());
}
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal" style="max-width: 520px">
      <h2 class="inv-title mb-2">{{ props.title }}</h2>
      <p v-if="props.hint" class="inv-muted">{{ props.hint }}</p>
      <label class="form-label small mb-0">{{ props.label || "دلیل" }}</label>
      <textarea v-model="reason" class="form-control form-control-sm" rows="3"></textarea>
      <div class="d-flex gap-2 justify-content-end mt-3">
        <button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button>
        <button :class="props.danger ? 'btn-danger' : 'btn-primary'" :disabled="props.busy" class="btn btn-sm" @click="go">{{ props.confirmText || "تایید" }}</button>
      </div>
    </div>
  </div>
</template>
