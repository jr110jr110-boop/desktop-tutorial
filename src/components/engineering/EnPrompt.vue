<script setup lang="ts">
// The dialog of an action that needs a text, a date-time or a choice from a person (a reason, a note, a deadline, the units of a line).
// A refusal of the server is written inside the dialog (`error`), the dialog stays open. The counter under a text field shows the length against `min`; the server decides.
import { ref } from "vue";

export interface EnField {
  key: string;
  label: string;
  type?: "text" | "textarea" | "datetime" | "multi" | "select" | "number" | "file";
  required?: boolean;
  min?: number;
  max?: number;
  value?: any;
  hint?: string;
  options?: { value: any; text: string }[];
}

const props = defineProps<{ title: string; fields: EnField[]; confirmText?: string; hint?: string; error?: string | null; busy?: boolean }>();
const emit = defineEmits(["confirm", "cancel"]);
const values = ref<Record<string, any>>(Object.fromEntries(props.fields.map((f) => [f.key, f.value ?? (f.type === "multi" ? [] : "")])));
const local = ref("");

function go() {
  local.value = "";
  for (const f of props.fields) {
    const v = values.value[f.key];
    if (f.required && (v === null || v === undefined || (typeof v === "string" && v.trim() === "") || (Array.isArray(v) && !v.length))) {
      local.value = `«${f.label}» را وارد کنید.`;
      return;
    }
  }
  emit("confirm", { ...values.value });
}
</script>

<template>
  <div class="inv-modal-backdrop screen-only" @click.self="emit('cancel')">
    <div class="inv-modal" role="dialog" :aria-label="title">
      <h2 class="inv-title mb-2">{{ title }}</h2>
      <p v-if="hint" class="inv-muted">{{ hint }}</p>
      <div v-for="f in fields" :key="f.key" class="mb-2">
        <label class="form-label small mb-0">{{ f.label }}<span v-if="f.required"> *</span></label>
        <textarea v-if="f.type === 'textarea'" v-model="values[f.key]" :maxlength="f.max" class="form-control form-control-sm" rows="3"></textarea>
        <input v-else-if="f.type === 'file'" class="form-control form-control-sm" type="file" @change="values[f.key] = ($event.target as HTMLInputElement).files?.[0] || null" />
        <input v-else-if="f.type === 'datetime'" v-model="values[f.key]" class="form-control form-control-sm" type="datetime-local" />
        <div v-else-if="f.type === 'multi'" class="en-units">
          <label v-for="o in f.options" :key="o.value"><input v-model="values[f.key]" :value="o.value" type="checkbox" /> {{ o.text }}</label>
        </div>
        <select v-else-if="f.type === 'select'" v-model="values[f.key]" class="form-select form-select-sm">
          <option value="">—</option>
          <option v-for="o in f.options" :key="o.value" :value="o.value">{{ o.text }}</option>
        </select>
        <input v-else v-model="values[f.key]" :maxlength="f.max" :type="f.type === 'number' ? 'number' : 'text'" class="form-control form-control-sm" />
        <div v-if="f.min && (f.type === 'textarea' || !f.type || f.type === 'text')" class="en-count" data-test="en-counter">
          {{ String(values[f.key] || "").trim().length }} از حداقل {{ f.min }} نویسه
        </div>
        <div v-if="f.hint" class="inv-muted">{{ f.hint }}</div>
      </div>
      <div v-if="local || error" class="alert alert-danger py-2 small" role="alert" data-test="en-dialog-error">{{ local || error }}</div>
      <div class="d-flex gap-2 justify-content-end mt-3">
        <button class="btn btn-sm btn-outline-secondary" type="button" @click="emit('cancel')">انصراف</button>
        <button :disabled="busy" class="btn btn-sm btn-primary" type="button" @click="go">{{ confirmText || "تایید" }}</button>
      </div>
    </div>
  </div>
</template>
