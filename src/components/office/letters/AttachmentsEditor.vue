<script setup lang="ts">
// Attachments of a draft: upload with the existing Documents API, then keep documentId + fileName. Download only through the authenticated letter route.
import { ref } from "vue";
import { downloadLetterFile } from "@/api/OfficeAPIs";
import { uploadFile } from "@/api/InventoryAPIs";
import { fail } from "@/composables/inventory";

const props = defineProps<{ modelValue: { documentId: string; fileName: string }[]; letterId?: number | null; savedIds?: string[]; disabled?: boolean }>();
const emit = defineEmits(["update:modelValue"]);
const busy = ref(false);

async function onFiles(event: any) {
  const files: File[] = Array.from(event.target.files || []);
  event.target.value = "";
  if (!files.length) return;
  busy.value = true;
  const next = [...props.modelValue];
  for (const file of files) {
    const [id, error] = await uploadFile(file, file.name);
    if (error || !id) {
      fail(error || "بارگذاری فایل انجام نشد.");
      continue;
    }
    next.push({ documentId: id as string, fileName: file.name });
  }
  busy.value = false;
  emit("update:modelValue", next);
}
function remove(i: number) {
  emit("update:modelValue", props.modelValue.filter((_, k) => k !== i));
}
async function download(a: { documentId: string; fileName: string }) {
  if (!props.letterId) return;
  const error = await downloadLetterFile(props.letterId, a.documentId, a.fileName);
  if (error) fail(error);
}
</script>

<template>
  <div>
    <ul v-if="modelValue.length" class="list-unstyled mb-2">
      <li v-for="(a, i) in modelValue" :key="a.documentId" class="d-flex align-items-center gap-2">
        <span>{{ a.fileName || "فایل" }}</span>
        <button v-if="letterId && savedIds && savedIds.includes(a.documentId)" class="btn btn-sm btn-link" @click="download(a)">دریافت</button>
        <button v-if="!disabled" class="btn btn-sm btn-link text-danger" @click="remove(i)">حذف</button>
      </li>
    </ul>
    <p v-else class="inv-muted mb-2">پیوستی نیست.</p>
    <label v-if="!disabled" :class="{ disabled: busy }" class="btn btn-sm btn-outline-secondary mb-0">
      {{ busy ? "در حال بارگذاری..." : "افزودن پیوست" }}<input class="d-none" multiple type="file" @change="onFiles" />
    </label>
  </div>
</template>
