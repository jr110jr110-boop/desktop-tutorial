<script setup lang="ts">
// A small dialog that asks for the extra values of an action (note, date, reference, bank account ...).
// fields: [{ key, label, type: text | textarea | date | select | number, required?, options?: [{ value, text }] }]
import { ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { fail } from "@/composables/inventory";

export interface PromptField {
  key: string;
  label: string;
  type?: "text" | "textarea" | "date" | "select" | "number";
  required?: boolean;
  options?: { value: any; text: string }[];
  value?: any;
  /** longest text the field takes (text and textarea) */
  maxlength?: number;
}

const props = defineProps<{ title: string; fields: PromptField[]; confirmText?: string; hint?: string }>();
const emit = defineEmits(["confirm", "cancel"]);
const values = ref<Record<string, any>>(Object.fromEntries(props.fields.map((f) => [f.key, f.value ?? (f.type === "select" ? null : "")])));

function confirm() {
  for (const f of props.fields) {
    const v = values.value[f.key];
    if (f.required && (v === null || v === undefined || String(v).trim() === "")) return fail(`«${f.label}» را وارد کنید.`);
  }
  emit("confirm", { ...values.value });
}
</script>

<template>
  <div class="inv-modal-backdrop screen-only" @click.self="emit('cancel')">
    <div class="inv-modal">
      <h2 class="inv-title mb-2">{{ title }}</h2>
      <p v-if="hint" class="inv-muted">{{ hint }}</p>
      <div v-for="f in fields" :key="f.key" class="mb-2">
        <label class="form-label small mb-0">{{ f.label }}<span v-if="f.required"> *</span></label>
        <textarea v-if="f.type === 'textarea'" v-model="values[f.key]" :maxlength="f.maxlength" class="form-control form-control-sm" rows="2"></textarea>
        <FnDate v-else-if="f.type === 'date'" v-model="values[f.key]" />
        <select v-else-if="f.type === 'select'" v-model="values[f.key]" class="form-select form-select-sm">
          <option :value="null">-</option>
          <option v-for="o in f.options" :key="o.value" :value="o.value">{{ o.text }}</option>
        </select>
        <input v-else v-model="values[f.key]" :maxlength="f.maxlength" :type="f.type === 'number' ? 'number' : 'text'" class="form-control form-control-sm" />
      </div>
      <div class="d-flex gap-2 justify-content-end mt-3">
        <button class="btn btn-sm btn-outline-secondary" @click="emit('cancel')">انصراف</button>
        <button class="btn btn-sm btn-primary" @click="confirm">{{ confirmText || "تایید" }}</button>
      </div>
    </div>
  </div>
</template>
