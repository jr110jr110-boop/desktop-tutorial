<script setup lang="ts">
// Links of a letter to other records (contract, project, purchase order ...): a kind and a free text (number or title).
import { relatedKinds } from "@/composables/office";
import { ref } from "vue";
import { fail } from "@/composables/inventory";

const props = defineProps<{ modelValue: { kind: number; recordId?: number | null; text?: string | null }[]; disabled?: boolean }>();
const emit = defineEmits(["update:modelValue"]);
const draft = ref<{ kind: number; text: string }>({ kind: 1, text: "" });

function add() {
  if (!draft.value.text.trim()) return fail("شماره یا عنوان رکورد را وارد کنید.");
  emit("update:modelValue", [...props.modelValue, { kind: draft.value.kind, recordId: null, text: draft.value.text.trim() }]);
  draft.value.text = "";
}
</script>

<template>
  <div>
    <ul v-if="modelValue.length" class="list-unstyled mb-2">
      <li v-for="(l, i) in modelValue" :key="i">
        <span class="inv-badge info">{{ relatedKinds[l.kind] || "سایر" }}</span> {{ l.text }}
        <button v-if="!disabled" class="btn btn-sm btn-link text-danger" @click="emit('update:modelValue', modelValue.filter((_, k) => k !== i))">حذف</button>
      </li>
    </ul>
    <p v-else class="inv-muted mb-2">پیوند به رکورد دیگری نیست.</p>
    <div v-if="!disabled" class="d-flex gap-2 flex-wrap">
      <select v-model.number="draft.kind" class="form-select form-select-sm" style="width: 11rem"><option v-for="(t, k) in relatedKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
      <input v-model="draft.text" class="form-control form-control-sm" placeholder="شماره یا عنوان" style="max-width: 18rem" @keydown.enter.prevent="add" />
      <button class="btn btn-sm btn-outline-primary" @click="add">افزودن پیوند</button>
    </div>
  </div>
</template>
