<script setup lang="ts">
// Checks an item code against the coding procedure (WI-WH-002) while it is typed and shows its segments.
import { ref, watch } from "vue";
import { checkCode } from "@/api/InventoryAPIs";

const props = defineProps({
  code: { type: String, default: "" },
  itemId: { type: Number, default: null },
});
const emit = defineEmits(["checked"]);

const result = ref<any>(null);
let timer: any = null;

watch(
  () => props.code,
  (code) => {
    clearTimeout(timer);
    if (!code || code.trim().length < 3) {
      result.value = null;
      emit("checked", null);
      return;
    }
    timer = setTimeout(async () => {
      const [data] = await checkCode(code, props.itemId);
      result.value = data;
      emit("checked", data);
    }, 350);
  },
  { immediate: true }
);
</script>

<template>
  <div v-if="result" class="code-check small">
    <div class="d-flex flex-wrap gap-1 mb-1">
      <span v-for="(segment, i) in result.segments" :key="i" class="seg">
        <b class="inv-ltr">{{ segment.value }}</b>
        <span>{{ segment.title }}<template v-if="segment.meaning">: {{ segment.meaning }}</template></span>
      </span>
    </div>
    <div v-for="(e, i) in result.errors" :key="'e' + i" class="text-danger">✗ {{ e }}</div>
    <div v-for="(w, i) in result.warnings" :key="'w' + i" class="text-warning-emphasis">⚠ {{ w }}</div>
    <div v-if="result.isValid && !result.errors.length" class="text-success">✓ کد مطابق دستورالعمل WI-WH-002 است.</div>
    <div v-if="result.otherStages && result.otherStages.length" class="inv-muted mt-1">
      مراحل دیگر همین قطعه:
      <span v-for="s in result.otherStages" :key="s" class="d-block">{{ s }}</span>
    </div>
  </div>
</template>

<style scoped>
.code-check {
  background: #f8f9fa;
  border: 1px solid #e3e6ea;
  border-radius: 0.4rem;
  padding: 0.4rem 0.6rem;
  margin-top: 0.25rem;
}
.seg {
  display: inline-flex;
  flex-direction: column;
  align-items: center;
  border: 1px solid #ced4da;
  border-radius: 0.3rem;
  padding: 0.1rem 0.4rem;
  background: #fff;
  line-height: 1.3;
}
.seg span {
  font-size: 0.72rem;
  color: #555;
}
</style>
