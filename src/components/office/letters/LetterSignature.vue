<script setup lang="ts">
// Signature block (signed by, Persian time, content hash) and the authenticity check of the server.
import { ref } from "vue";
import { verifyLetter } from "@/api/OfficeAPIs";
import { fail } from "@/composables/inventory";

const props = defineProps<{ letter: any }>();
const result = ref<any>(null);
const busy = ref(false);

async function verify() {
  busy.value = true;
  const [data, error] = await verifyLetter(props.letter.id);
  busy.value = false;
  if (error) return fail(error);
  result.value = data;
}
function changedNames(): string[] {
  const names: string[] = [];
  for (const id of result.value?.changedDocumentIds || []) {
    const a = (props.letter.attachments || []).find((x: any) => String(x.documentId).toLowerCase() === String(id).toLowerCase());
    names.push(a ? a.fileName || "پیوست" : String(id).toLowerCase() === String(props.letter.signatureDocumentId || "").toLowerCase() ? "تصویر امضا" : "فایل نامشخص");
  }
  return names;
}
</script>

<template>
  <div v-if="letter.signedAt" class="border rounded p-2 mb-3">
    <div class="d-flex justify-content-between align-items-start flex-wrap gap-2">
      <div>
        <div><b>امضاشده توسط:</b> {{ letter.signedByName }}<span v-if="letter.signedOnBehalfOfPosition"> (به نیابت از {{ letter.signedOnBehalfOfPosition }})</span></div>
        <div><b>سمت:</b> {{ letter.signerPositionTitle }}</div>
        <div><b>زمان امضا:</b> {{ letter.signedAtText }}</div>
        <div class="small"><b>اثر انگشت محتوا:</b> <span class="inv-ltr" style="word-break: break-all; white-space: normal">{{ letter.contentHash }}</span></div>
      </div>
      <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="verify">بررسی اصالت</button>
    </div>
    <div v-if="result" :class="result.valid ? 'alert-success' : 'alert-danger'" class="alert py-2 mt-2 mb-0">
      <template v-if="!result.signed">این نامه هنوز امضا نشده است.</template>
      <template v-else-if="result.valid">نامه معتبر است: محتوا و پیوست‌ها بعد از امضا تغییر نکرده‌اند.</template>
      <template v-else>
        نامه نامعتبر است: محتوا یا فایل‌ها بعد از امضا تغییر کرده‌اند.
        <ul v-if="changedNames().length" class="mb-0 mt-1"><li v-for="(n, i) in changedNames()" :key="i">فایل تغییرکرده: {{ n }}</li></ul>
      </template>
    </div>
  </div>
</template>
