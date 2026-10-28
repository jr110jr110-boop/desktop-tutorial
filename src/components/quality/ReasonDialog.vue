<script setup lang="ts">
// A small dialog that asks for a written reason (cancel, reject, return, reopen, weaken a control...). The text is trimmed and, when required, must not be empty;
// the API checks it again and its Persian refusal is shown by the caller.
import { computed, ref } from "vue";

const props = defineProps({
  title: { type: String, required: true },
  label: { type: String, default: "دلیل" },
  confirmText: { type: String, default: "تایید" },
  /** bootstrap button class of the confirm button */
  confirmClass: { type: String, default: "btn-danger" },
  hint: { type: String, default: "" },
  required: { type: Boolean, default: true },
  maxLength: { type: Number, default: 1000 },
  /** text the box starts with (for example the report that is being confirmed) */
  initialText: { type: String, default: "" },
  busy: Boolean,
});
const emit = defineEmits<{ (e: "confirm", text: string): void; (e: "close"): void }>();

const text = ref(props.initialText);
const trimmed = computed(() => text.value.trim());
const tooLong = computed(() => trimmed.value.length > props.maxLength);
const canConfirm = computed(() => !props.busy && !tooLong.value && (!props.required || trimmed.value.length > 0));

function confirm() {
  if (canConfirm.value) emit("confirm", trimmed.value);
}
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')" @keydown.esc="emit('close')">
    <div class="inv-modal" style="max-width: 560px" role="dialog" aria-modal="true">
      <h2 class="inv-title mb-2">{{ title }}</h2>
      <p v-if="hint" class="inv-muted small">{{ hint }}</p>
      <label class="form-label small mb-0">{{ label }}<span v-if="required"> *</span></label>
      <textarea v-model="text" class="form-control form-control-sm" rows="3" autofocus></textarea>
      <div class="d-flex justify-content-between small mt-1">
        <span v-if="tooLong" class="text-danger">حداکثر {{ maxLength }} نویسه مجاز است.</span>
        <span v-else class="inv-muted">{{ trimmed.length }} / {{ maxLength }}</span>
      </div>
      <div class="d-flex gap-2 justify-content-end mt-3">
        <button class="btn btn-sm btn-outline-secondary" type="button" @click="emit('close')">انصراف</button>
        <button :disabled="!canConfirm" class="btn btn-sm" :class="confirmClass" type="button" @click="confirm">{{ confirmText }}</button>
      </div>
    </div>
  </div>
</template>
