<script setup lang="ts">
// Attaches a file with the existing Documents endpoint; the id is kept in the record and the file can be downloaded again.
import { ref } from "vue";
import { downloadFile, uploadFile } from "@/api/InventoryAPIs";
import { fail, ok } from "@/composables/inventory";

const props = defineProps({
  modelValue: { type: String, default: null },
  title: { type: String, default: "پیوست" },
  disabled: Boolean,
});
const emit = defineEmits(["update:modelValue"]);
const busy = ref(false);

async function pick(event: Event) {
  const input = event.target as HTMLInputElement;
  const file = input.files && input.files[0];
  if (!file) return;
  busy.value = true;
  const [id, error] = await uploadFile(file, props.title);
  busy.value = false;
  input.value = "";
  if (error) return fail(error);
  emit("update:modelValue", id);
  ok("فایل پیوست شد.");
}
</script>

<template>
  <div class="d-flex flex-wrap gap-2 align-items-center">
    <input v-if="!disabled" :disabled="busy" class="form-control form-control-sm" style="max-width: 260px" type="file" @change="pick" />
    <button v-if="modelValue" class="btn btn-sm btn-link p-0" type="button" @click="downloadFile(modelValue, title)">دیدن فایل پیوست</button>
    <span v-else-if="disabled" class="inv-muted">پیوستی نیست</span>
  </div>
</template>
